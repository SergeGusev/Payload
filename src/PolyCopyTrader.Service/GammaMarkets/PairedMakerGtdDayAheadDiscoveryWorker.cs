using System.Globalization;
using System.Text.RegularExpressions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.GammaMarkets;

public sealed record PairedMakerGtdFirstAcceptingCandidate(
    PolymarketGammaMarket Market,
    DateTimeOffset RequestStartedAtUtc,
    DateTimeOffset ResponseCompletedAtUtc,
    DateTimeOffset FirstObservedAcceptingAtUtc);

public sealed record PairedMakerGtdFirstAcceptingResult(
    int MarketsProcessed = 0,
    int LegsAccepted = 0,
    int LegsSkipped = 0);

public interface IPairedMakerGtdFirstAcceptingProcessor
{
    Task<PairedMakerGtdFirstAcceptingResult> ProcessFirstAcceptingMarketAsync(
        PairedMakerGtdFirstAcceptingCandidate candidate,
        CancellationToken cancellationToken = default);

    Task<PairedMakerGtdFirstAcceptingResult> ProcessDueAsync(
        CancellationToken cancellationToken = default);
}

public sealed record PairedMakerGtdDayAheadDiscoveryResult(
    int SlugsRequested,
    int BatchesRequested,
    int MarketsFetched,
    int MarketsValidated,
    int MarketsUpserted,
    int FirstAcceptingMarketsProcessed,
    int InvalidMarketsSkipped,
    PairedMakerGtdFirstAcceptingResult DueResult);

public sealed partial class PairedMakerGtdDayAheadDiscoveryWorker : BackgroundService
{
    private const long FiveMinuteWindowSeconds = 300;
    private static readonly string[] AssetSymbols = ["BTC", "ETH", "SOL"];
    private static readonly IReadOnlySet<string> AllowedAssetSymbols = new HashSet<string>(
        AssetSymbols,
        StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<PairedMakerGtdDayAheadDiscoveryWorker> logger;
    private readonly BotOptions botOptions;
    private readonly PaperTradingOptions paperTradingOptions;
    private readonly PairedMakerGtdDayAheadDiscoveryOptions options;
    private readonly IPolymarketGammaClient gammaClient;
    private readonly IActiveMarketAssetSubscriptionRegistry activeMarketAssetSubscriptionRegistry;
    private readonly IAppRepository repository;
    private readonly IPairedMakerGtdFirstAcceptingProcessor firstAcceptingProcessor;
    private readonly TimeProvider clock;
    private readonly Dictionary<string, PairedMakerGtdFirstAcceptingCandidate> pendingFirstAccepting =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> processedFirstAccepting =
        new(StringComparer.OrdinalIgnoreCase);

    public PairedMakerGtdDayAheadDiscoveryWorker(
        ILogger<PairedMakerGtdDayAheadDiscoveryWorker> logger,
        BotOptions botOptions,
        PaperTradingOptions paperTradingOptions,
        PairedMakerGtdDayAheadDiscoveryOptions options,
        IPolymarketGammaClient gammaClient,
        IActiveMarketAssetSubscriptionRegistry activeMarketAssetSubscriptionRegistry,
        IAppRepository repository,
        IPairedMakerGtdFirstAcceptingProcessor firstAcceptingProcessor,
        TimeProvider? timeProvider = null)
    {
        this.logger = logger;
        this.botOptions = botOptions;
        this.paperTradingOptions = paperTradingOptions;
        this.options = options;
        this.gammaClient = gammaClient;
        this.activeMarketAssetSubscriptionRegistry = activeMarketAssetSubscriptionRegistry;
        this.repository = repository;
        this.firstAcceptingProcessor = firstAcceptingProcessor;
        clock = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Paired Maker-GTD day-ahead discovery is disabled.");
            return;
        }

        if (!RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions))
        {
            logger.LogInformation(
                "Paired Maker-GTD day-ahead discovery will not start. {Reason}",
                RuntimeModePolicy.PaperTradingDisabledReason(botOptions, paperTradingOptions));
            return;
        }

        logger.LogInformation(
            "Paired Maker-GTD day-ahead discovery started. Mode={Mode} RunInLiveMode={RunInLiveMode} PollIntervalSeconds={PollIntervalSeconds} LeadBandHours={MinimumLeadHours}..{MaximumLeadHours} GammaBatchSize={GammaBatchSize}",
            botOptions.Mode,
            paperTradingOptions.RunInLiveMode,
            options.PollIntervalSeconds,
            options.MinimumLeadHours,
            options.MaximumLeadHours,
            options.GammaBatchSize);

        var pollInterval = TimeSpan.FromSeconds(options.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await RunCycleAsync(stoppingToken);
                logger.LogDebug(
                    "Paired Maker-GTD day-ahead discovery cycle completed. Slugs={Slugs} Batches={Batches} Fetched={Fetched} Validated={Validated} Upserted={Upserted} FirstAccepting={FirstAccepting} Invalid={Invalid} DueMarkets={DueMarkets}",
                    result.SlugsRequested,
                    result.BatchesRequested,
                    result.MarketsFetched,
                    result.MarketsValidated,
                    result.MarketsUpserted,
                    result.FirstAcceptingMarketsProcessed,
                    result.InvalidMarketsSkipped,
                    result.DueResult.MarketsProcessed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Paired Maker-GTD day-ahead discovery cycle failed.");
                await TryRecordApiErrorAsync(ex.Message, stoppingToken);
            }

            await Task.Delay(pollInterval, clock, stoppingToken);
        }

        logger.LogInformation("Paired Maker-GTD day-ahead discovery stopped.");
    }

    public async Task<PairedMakerGtdDayAheadDiscoveryResult> RunCycleAsync(
        CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.GetUtcNow();
        PruneCompletedObservations(nowUtc);
        var minimumStartUtc = nowUtc.AddHours(options.MinimumLeadHours);
        var maximumStartUtc = nowUtc.AddHours(options.MaximumLeadHours);
        var slugs = BuildSlugs(minimumStartUtc, maximumStartUtc);
        var requestedSlugs = slugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var marketsFetched = 0;
        var marketsValidated = 0;
        var marketsUpserted = 0;
        var firstAcceptingMarketsProcessed = 0;
        var invalidMarketsSkipped = 0;
        var batchesRequested = 0;
        var conditionsSeenThisCycle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dueResult = new PairedMakerGtdFirstAcceptingResult();
        try
        {
            for (var offset = 0; offset < slugs.Count; offset += options.GammaBatchSize)
            {
                var batch = slugs.Skip(offset).Take(options.GammaBatchSize).ToArray();
                var requestStartedAtUtc = clock.GetUtcNow();
                var markets = await gammaClient.GetMarketsBySlugsAsync(
                    batch,
                    activeOnly: true,
                    cancellationToken);
                var responseCompletedAtUtc = clock.GetUtcNow();
                batchesRequested++;
                marketsFetched += markets.Count;

                foreach (var market in markets)
                {
                    if (!TryValidateMarket(
                            market,
                            requestedSlugs,
                            minimumStartUtc,
                            maximumStartUtc,
                            out var marketStartUtc,
                            out var rejectionReason))
                    {
                        invalidMarketsSkipped++;
                        logger.LogWarning(
                            "Paired Maker-GTD day-ahead Gamma market rejected. MarketId={MarketId} ConditionId={ConditionId} Slug={Slug} Reason={Reason}",
                            market.MarketId,
                            market.ConditionId,
                            market.Slug,
                            rejectionReason);
                        continue;
                    }

                    if (!conditionsSeenThisCycle.Add(market.ConditionId))
                    {
                        continue;
                    }

                    marketsValidated++;
                    PairedMakerGtdFirstAcceptingCandidate? candidate = null;
                    if (market.AcceptingOrders && !processedFirstAccepting.ContainsKey(market.ConditionId))
                    {
                        if (!pendingFirstAccepting.TryGetValue(market.ConditionId, out candidate))
                        {
                            candidate = new PairedMakerGtdFirstAcceptingCandidate(
                                market,
                                requestStartedAtUtc,
                                responseCompletedAtUtc,
                                responseCompletedAtUtc);
                            pendingFirstAccepting.Add(market.ConditionId, candidate);
                        }

                        activeMarketAssetSubscriptionRegistry.AddOrUpdateMarkets(
                            [market],
                            protectFromFullScanRetention: true);
                    }

                    await repository.UpsertPolymarketGammaMarketAsync(market, cancellationToken);
                    marketsUpserted++;
                    if (candidate is null)
                    {
                        continue;
                    }

                    await firstAcceptingProcessor.ProcessFirstAcceptingMarketAsync(candidate, cancellationToken);
                    pendingFirstAccepting.Remove(market.ConditionId);
                    processedFirstAccepting[market.ConditionId] = marketStartUtc;
                    firstAcceptingMarketsProcessed++;
                }
            }
        }
        finally
        {
            // Persisted Observed rows must remain recoverable after a process restart or
            // after their market leaves the discovery lead band.
            dueResult = await firstAcceptingProcessor.ProcessDueAsync(cancellationToken);
        }

        return new PairedMakerGtdDayAheadDiscoveryResult(
            slugs.Count,
            batchesRequested,
            marketsFetched,
            marketsValidated,
            marketsUpserted,
            firstAcceptingMarketsProcessed,
            invalidMarketsSkipped,
            dueResult);
    }

    internal static IReadOnlyList<string> BuildSlugs(
        DateTimeOffset minimumStartUtc,
        DateTimeOffset maximumStartUtc)
    {
        if (maximumStartUtc <= minimumStartUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumStartUtc),
                "Maximum start UTC must be later than minimum start UTC.");
        }

        var minimumUnixSeconds = minimumStartUtc.ToUnixTimeSeconds();
        var maximumUnixSeconds = maximumStartUtc.ToUnixTimeSeconds();
        var firstWindowUnixSeconds = minimumUnixSeconds % FiveMinuteWindowSeconds == 0
            ? minimumUnixSeconds
            : minimumUnixSeconds + FiveMinuteWindowSeconds - (minimumUnixSeconds % FiveMinuteWindowSeconds);
        var lastWindowUnixSeconds = maximumUnixSeconds - (maximumUnixSeconds % FiveMinuteWindowSeconds);
        var windowCount = lastWindowUnixSeconds < firstWindowUnixSeconds
            ? 0
            : checked((int)((lastWindowUnixSeconds - firstWindowUnixSeconds) / FiveMinuteWindowSeconds) + 1);
        var slugs = new List<string>(AssetSymbols.Length * windowCount);

        for (var windowUnixSeconds = firstWindowUnixSeconds;
             windowUnixSeconds <= lastWindowUnixSeconds;
             windowUnixSeconds += FiveMinuteWindowSeconds)
        {
            foreach (var assetSymbol in AssetSymbols)
            {
                slugs.Add(
                    assetSymbol.ToLowerInvariant() +
                    "-updown-5m-" +
                    windowUnixSeconds.ToString(CultureInfo.InvariantCulture));
            }
        }

        return slugs;
    }

    private static bool TryValidateMarket(
        PolymarketGammaMarket market,
        IReadOnlySet<string> requestedSlugs,
        DateTimeOffset minimumStartUtc,
        DateTimeOffset maximumStartUtc,
        out DateTimeOffset marketStartUtc,
        out string rejectionReason)
    {
        marketStartUtc = default;
        rejectionReason = string.Empty;
        var marketSlug = market.Slug;
        if (string.IsNullOrWhiteSpace(marketSlug))
        {
            rejectionReason = "slug_not_exactly_requested_btc_eth_sol_5m";
            return false;
        }

        var match = CryptoUpDown5mSlugRegex().Match(marketSlug);
        if (!match.Success ||
            !AllowedAssetSymbols.Contains(match.Groups["asset"].Value) ||
            !requestedSlugs.Contains(marketSlug))
        {
            rejectionReason = "slug_not_exactly_requested_btc_eth_sol_5m";
            return false;
        }

        if (!long.TryParse(
                match.Groups["unix"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var unixSeconds) ||
            unixSeconds % FiveMinuteWindowSeconds != 0)
        {
            rejectionReason = "slug_start_not_five_minute_aligned";
            return false;
        }

        marketStartUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (marketStartUtc < minimumStartUtc || marketStartUtc > maximumStartUtc)
        {
            rejectionReason = "market_start_outside_configured_lead_band";
            return false;
        }

        if (market.EventStartTimeUtc is { } eventStartTimeUtc && eventStartTimeUtc != marketStartUtc)
        {
            rejectionReason = "event_start_does_not_match_slug";
            return false;
        }

        if (market.EndDateUtc is not { } endDateUtc || endDateUtc != marketStartUtc.AddMinutes(5))
        {
            rejectionReason = "market_end_does_not_match_five_minute_slot";
            return false;
        }

        if (string.IsNullOrWhiteSpace(market.MarketId) ||
            string.IsNullOrWhiteSpace(market.ConditionId) ||
            string.IsNullOrWhiteSpace(market.QuestionId))
        {
            rejectionReason = "required_market_identity_missing";
            return false;
        }

        if (!market.Active || market.Closed || market.Archived || !market.EnableOrderBook)
        {
            rejectionReason = "market_not_active_order_book_candidate";
            return false;
        }

        if (market.Outcomes.Count != 2 ||
            !market.Outcomes.Contains("Up", StringComparer.Ordinal) ||
            !market.Outcomes.Contains("Down", StringComparer.Ordinal))
        {
            rejectionReason = "outcomes_not_exactly_up_down";
            return false;
        }

        if (market.ClobTokenIds.Count != 2 ||
            market.ClobTokenIds.Any(IsInvalidTokenId) ||
            market.ClobTokenIds.Distinct(StringComparer.Ordinal).Count() != 2)
        {
            rejectionReason = "clob_tokens_not_two_distinct_valid_ids";
            return false;
        }

        return true;
    }

    private static bool IsInvalidTokenId(string? tokenId)
    {
        return string.IsNullOrWhiteSpace(tokenId) ||
            tokenId.Trim().Equals("0", StringComparison.Ordinal) ||
            tokenId.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
    }

    private void PruneCompletedObservations(DateTimeOffset nowUtc)
    {
        foreach (var conditionId in processedFirstAccepting
                     .Where(pair => pair.Value.AddMinutes(5) < nowUtc)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            processedFirstAccepting.Remove(conditionId);
        }
    }

    private async Task TryRecordApiErrorAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(
                    Guid.NewGuid(),
                    nameof(PairedMakerGtdDayAheadDiscoveryWorker),
                    "DiscoverFirstAcceptingMarkets",
                    message,
                    clock.GetUtcNow()),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist paired Maker-GTD day-ahead discovery error.");
        }
    }

    [GeneratedRegex(
        "^(?<asset>btc|eth|sol)-updown-5m-(?<unix>\\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CryptoUpDown5mSlugRegex();
}
