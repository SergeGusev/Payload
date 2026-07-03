using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Storage;
using System.Text.Json;

namespace PolyCopyTrader.Service.Strategies;

public sealed class CryptoUpDown5mResultPollingProcessor(
    ILogger<CryptoUpDown5mResultPollingProcessor> logger,
    CryptoUpDown5mResultPollingOptions options,
    IAppRepository repository,
    IPolymarketGammaClient gammaClient,
    IPolymarketClobPublicClient clobClient,
    IMarketDataCache marketDataCache,
    IBtcUsdReferencePriceClient btcUsdReferencePriceClient,
    ICryptoReferencePriceClient cryptoReferencePriceClient) : ICryptoUpDown5mResultPollingProcessor
{
    private const decimal ResultThreshold = 0.50m;
    private const string SourceMarketWebSocket = "MarketWebSocket";
    private const string SourceReferenceStartEnd = "ReferenceStartEnd";
    private const string SourceBinanceTimedClose = "BinanceTimedClose";
    private const string SourceTerminalOrderBook = "TerminalOrderBook";
    private const string SourceGammaClosedMarket = "GammaClosedMarket";
    private const string StatusPending = "Pending";
    private const string StatusResolved = "Resolved";
    private const string StatusTimedOut = "TimedOut";
    private const string ResponseReferenceStartEnd = "reference_start_end_result";
    private const string ResponseNotFound = "not_found";
    private const string ResponseNotClosed = "not_closed";
    private const string ResponseClosedWithoutWinner = "closed_without_winner";
    private const string ResponseWinnerFound = "winner_found";
    private const string ResponseError = "error";
    private const int MaxErrorLength = 2_000;

    public async Task<CryptoUpDown5mResultPollingCycleResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return new CryptoUpDown5mResultPollingCycleResult(0, 0, 0, 0, 0, 0);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var assetSymbols = NormalizeSymbols(options.AssetSymbols);
        if (assetSymbols.Count == 0)
        {
            return new CryptoUpDown5mResultPollingCycleResult(0, 0, 0, 0, 0, 0);
        }

        var markets = await repository.GetCryptoUpDown5mGammaMarketsAsync(
            assetSymbols,
            options.MaxMarketsPerCycle,
            cancellationToken);
        var candidates = SelectCandidates(markets, assetSymbols, nowUtc).ToArray();
        if (candidates.Length == 0)
        {
            return new CryptoUpDown5mResultPollingCycleResult(markets.Count, 0, 0, 0, 0, 0);
        }

        var observationStartUtc = nowUtc
            .AddMinutes(-Math.Max(options.MaxMarketAgeMinutes, options.MaxResultWaitMinutes))
            .AddMinutes(-10);
        var observations = await repository.GetCryptoUpDown5mResultPollingObservationsAsync(
            assetSymbols,
            observationStartUtc,
            nowUtc.AddMinutes(5),
            cancellationToken);
        var observationsByMarketId = observations
            .GroupBy(observation => observation.MarketId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.UpdatedAtUtc).First(), StringComparer.OrdinalIgnoreCase);
        var resolvedMarkets = await repository.GetCryptoUpDown5mWebSocketResolvedMarketsAsync(
            assetSymbols,
            observationStartUtc,
            nowUtc.AddMinutes(5),
            cancellationToken);
        var resolvedByAssetAndStart = resolvedMarkets
            .GroupBy(
                item => new AssetMarketStartKey(item.AssetSymbol.Trim().ToUpperInvariant(), item.MarketStartUtc),
                item => item)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => IsConfirmedResultSource(item.Source)).ThenByDescending(item => item.UpdatedAtUtc).First());

        var pollsSent = 0;
        var resultsFound = 0;
        var timedOut = 0;
        var errors = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observationsByMarketId.TryGetValue(candidate.Market.MarketId, out var existing);
            if (existing is not null &&
                (string.Equals(existing.Status, StatusResolved, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(existing.Status, StatusTimedOut, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var observation = existing ?? CreateInitialObservation(candidate, nowUtc);
            resolvedByAssetAndStart.TryGetValue(new AssetMarketStartKey(candidate.AssetSymbol, candidate.MarketStartUtc), out var existingResolvedMarket);
            var pollResult = await PollCandidateAsync(candidate, observation, existingResolvedMarket, cancellationToken);
            pollsSent++;
            if (pollResult.ResultFound)
            {
                resultsFound++;
            }

            if (pollResult.TimedOut)
            {
                timedOut++;
            }

            if (pollResult.Error)
            {
                errors++;
            }
        }

        return new CryptoUpDown5mResultPollingCycleResult(
            markets.Count,
            candidates.Length,
            pollsSent,
            resultsFound,
            timedOut,
            errors);
    }

    public async Task<CryptoUpDown5mBinanceTimedCloseCycleResult> ProcessBinanceTimedCloseAsync(
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || !options.BinanceTimedCloseEnabled)
        {
            return new CryptoUpDown5mBinanceTimedCloseCycleResult(0, 0, 0, 0, 0, 0, 0, 0);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var assetSymbols = NormalizeSymbols(options.AssetSymbols);
        if (assetSymbols.Count == 0)
        {
            return new CryptoUpDown5mBinanceTimedCloseCycleResult(0, 0, 0, 0, 0, 0, 0, 0);
        }

        var markets = await repository.GetCryptoUpDown5mGammaMarketsAsync(
            assetSymbols,
            options.MaxMarketsPerCycle,
            cancellationToken);
        var candidates = SelectBinanceTimedCloseCandidates(markets, assetSymbols, nowUtc).ToArray();
        if (candidates.Length == 0)
        {
            return new CryptoUpDown5mBinanceTimedCloseCycleResult(markets.Count, 0, 0, 0, 0, 0, 0, 0);
        }

        var earliestStartUtc = candidates.Min(candidate => candidate.MarketStartUtc);
        var latestStartUtc = candidates.Max(candidate => candidate.MarketStartUtc);
        var resolvedMarkets = await repository.GetCryptoUpDown5mWebSocketResolvedMarketsAsync(
            assetSymbols,
            earliestStartUtc.AddMinutes(-1),
            latestStartUtc.AddMinutes(1),
            cancellationToken);
        var resolvedKeys = resolvedMarkets
            .Where(IsAcceptedResolvedMarketLedgerResult)
            .Select(result => new AssetMarketStartKey(result.AssetSymbol.Trim().ToUpperInvariant(), result.MarketStartUtc))
            .ToHashSet();

        var alreadyResolved = 0;
        var resolved = 0;
        var skippedUncertain = 0;
        var missingStartPrice = 0;
        var missingClosePrice = 0;
        var errors = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resolvedKeys.Contains(new AssetMarketStartKey(candidate.AssetSymbol, candidate.MarketStartUtc)))
            {
                alreadyResolved++;
                continue;
            }

            try
            {
                var result = await TryCreateBinanceTimedCloseResultAsync(candidate, nowUtc, cancellationToken);
                switch (result.Status)
                {
                    case BinanceTimedCloseResultStatus.Resolved:
                        await repository.UpsertCryptoUpDown5mWebSocketResolvedMarketAsync(
                            BuildResolvedMarketLedgerRow(candidate, result.Result!, nowUtc, SourceBinanceTimedClose),
                            cancellationToken);
                        resolved++;
                        resolvedKeys.Add(new AssetMarketStartKey(candidate.AssetSymbol, candidate.MarketStartUtc));
                        break;
                    case BinanceTimedCloseResultStatus.Uncertain:
                        skippedUncertain++;
                        break;
                    case BinanceTimedCloseResultStatus.MissingStartPrice:
                        missingStartPrice++;
                        break;
                    case BinanceTimedCloseResultStatus.MissingClosePrice:
                        missingClosePrice++;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                logger.LogDebug(
                    ex,
                    "Crypto Up or Down 5m Binance timed close result failed. Asset={AssetSymbol} MarketSlug={MarketSlug}",
                    candidate.AssetSymbol,
                    candidate.MarketSlug);
            }
        }

        return new CryptoUpDown5mBinanceTimedCloseCycleResult(
            markets.Count,
            candidates.Length,
            alreadyResolved,
            resolved,
            skippedUncertain,
            missingStartPrice,
            missingClosePrice,
            errors);
    }

    private async Task<BinanceTimedCloseResult> TryCreateBinanceTimedCloseResultAsync(
        PollCandidate candidate,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        var start = await TryGetBinanceTimedCloseStartPriceAsync(candidate, cancellationToken);
        if (start is null)
        {
            return BinanceTimedCloseResult.MissingStartPrice();
        }

        var close = await TryGetBinanceTimedClosePriceAsync(candidate.AssetSymbol, cancellationToken);
        if (close is null)
        {
            return BinanceTimedCloseResult.MissingClosePrice();
        }

        var closeAge = observedAtUtc - close.Price.FetchedAtUtc;
        if (closeAge < TimeSpan.Zero)
        {
            closeAge = TimeSpan.Zero;
        }

        var closeOffset = close.Price.SourceUpdatedAtUtc - candidate.MarketEndUtc;
        var maxCloseAge = TimeSpan.FromMilliseconds(Math.Max(1, options.BinanceTimedCloseMaxPriceAgeMilliseconds));
        if (closeAge > maxCloseAge || closeOffset.Duration() > maxCloseAge)
        {
            return BinanceTimedCloseResult.MissingClosePrice();
        }

        if (start.PriceUsd <= 0m || close.Price.PriceUsd <= 0m)
        {
            return start.PriceUsd <= 0m
                ? BinanceTimedCloseResult.MissingStartPrice()
                : BinanceTimedCloseResult.MissingClosePrice();
        }

        var moveUsd = close.Price.PriceUsd - start.PriceUsd;
        var moveBps = moveUsd / start.PriceUsd * 10_000m;
        var minMoveBps = Math.Max(0m, options.BinanceTimedCloseMinMoveBps);
        if (Math.Abs(moveBps) < minMoveBps)
        {
            return BinanceTimedCloseResult.Uncertain();
        }

        var winningOutcome = moveBps > 0m ? "Up" : "Down";
        var result = new InferredMarketResult(
            winningOutcome,
            TryGetOutcomeAssetId(candidate.Market, winningOutcome),
            close.Price.SourceUpdatedAtUtc,
            JsonSerializer.Serialize(new
            {
                source = SourceBinanceTimedClose,
                provisional = true,
                inferred_at_utc = observedAtUtc,
                market_start_utc = candidate.MarketStartUtc,
                market_end_utc = candidate.MarketEndUtc,
                asset_symbol = candidate.AssetSymbol,
                binance_symbol = close.BinanceSymbol,
                start_price_usd = start.PriceUsd,
                close_price_usd = close.Price.PriceUsd,
                move_usd = moveUsd,
                move_bps = moveBps,
                min_move_bps = minMoveBps,
                winning_outcome = winningOutcome,
                start_price_sampled_at_utc = start.SampledAtUtc,
                close_price_source_updated_at_utc = close.Price.SourceUpdatedAtUtc,
                close_price_fetched_at_utc = close.Price.FetchedAtUtc,
                close_price_age_ms = ToDecimalMilliseconds(closeAge),
                close_price_offset_ms = ToDecimalMilliseconds(closeOffset),
                max_close_price_age_or_offset_ms = Math.Max(1, options.BinanceTimedCloseMaxPriceAgeMilliseconds),
                close_delay_ms = Math.Max(0, options.BinanceTimedCloseDelayMilliseconds)
            }));
        return BinanceTimedCloseResult.Resolved(result);
    }

    private async Task<BinanceTimedCloseStartPrice?> TryGetBinanceTimedCloseStartPriceAsync(
        PollCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (string.Equals(candidate.AssetSymbol, "BTC", StringComparison.OrdinalIgnoreCase))
        {
            var ticks = await repository.GetBtcUpDown5mOddsTicksForMarketAsync(
                candidate.Market.MarketId,
                limit: 50,
                cancellationToken);
            var tick = ticks.FirstOrDefault(item => item.BinanceStartPriceUsd > 0m);
            return tick is null
                ? null
                : new BinanceTimedCloseStartPrice(
                    tick.BinanceStartPriceUsd,
                    tick.SampledAtUtc,
                    "BTCUSDT");
        }

        var cryptoTicks = await repository.GetCryptoUpDown5mOddsTicksForMarketAsync(
            candidate.AssetSymbol,
            candidate.Market.MarketId,
            limit: 50,
            cancellationToken);
        var cryptoTick = cryptoTicks.FirstOrDefault(item => item.BinanceStartPriceUsd > 0m);
        return cryptoTick is null
            ? null
            : new BinanceTimedCloseStartPrice(
                cryptoTick.BinanceStartPriceUsd,
                cryptoTick.SampledAtUtc,
                cryptoTick.BinanceSymbol);
    }

    private async Task<BinanceTimedClosePrice?> TryGetBinanceTimedClosePriceAsync(
        string assetSymbol,
        CancellationToken cancellationToken)
    {
        if (string.Equals(assetSymbol, "BTC", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var price = await btcUsdReferencePriceClient.GetBtcUsdPriceAsync(cancellationToken);
                return new BinanceTimedClosePrice("BTCUSDT", price);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        try
        {
            var cryptoPrice = await cryptoReferencePriceClient.GetPriceAsync(assetSymbol, cancellationToken);
            return new BinanceTimedClosePrice(
                cryptoPrice.BinanceSymbol,
                new BtcUsdReferencePricePoint(
                    cryptoPrice.PriceUsd,
                    cryptoPrice.SourceUpdatedAtUtc,
                    cryptoPrice.FetchedAtUtc,
                    cryptoPrice.Source));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<PollResult> PollCandidateAsync(
        PollCandidate candidate,
        CryptoUpDown5mResultPollingObservation observation,
        CryptoUpDown5mWebSocketResolvedMarket? existingResolvedMarket,
        CancellationToken cancellationToken)
    {
        try
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            if (existingResolvedMarket is null)
            {
                var referenceResult = await TryUpsertReferencePriceResultAsync(candidate, startedAtUtc, cancellationToken);
                if (referenceResult is not null)
                {
                    var immediateUpdated = ApplyImmediateResultResponse(
                        candidate,
                        observation,
                        referenceResult.WinningOutcome,
                        startedAtUtc,
                        ResponseReferenceStartEnd);
                    await repository.UpsertCryptoUpDown5mResultPollingObservationAsync(immediateUpdated, cancellationToken);
                    return new PollResult(ResultFound: true, TimedOut: false, Error: false);
                }

                await TryUpsertProvisionalOrderBookResultAsync(candidate, startedAtUtc, cancellationToken);
            }
            else if (IsAcceptedResolvedMarketLedgerResult(existingResolvedMarket))
            {
                var immediateUpdated = ApplyImmediateResultResponse(
                    candidate,
                    observation,
                    existingResolvedMarket.WinningOutcome,
                    startedAtUtc,
                    "resolved_market_ledger_" + existingResolvedMarket.Source);
                await repository.UpsertCryptoUpDown5mResultPollingObservationAsync(immediateUpdated, cancellationToken);
                return new PollResult(ResultFound: true, TimedOut: false, Error: false);
            }

            var market = await gammaClient.GetClosedMarketBySlugAsync(candidate.MarketSlug, cancellationToken);
            var polledAtUtc = DateTimeOffset.UtcNow;
            var updated = ApplyPollResponse(candidate, observation, market, polledAtUtc);
            if (market is not null &&
                string.Equals(updated.Status, StatusResolved, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(updated.WinningOutcome))
            {
                await TryUpsertGammaConfirmedResultAsync(candidate, market, updated.WinningOutcome, polledAtUtc, cancellationToken);
            }

            await repository.UpsertCryptoUpDown5mResultPollingObservationAsync(updated, cancellationToken);
            return new PollResult(
                string.Equals(updated.Status, StatusResolved, StringComparison.OrdinalIgnoreCase),
                string.Equals(updated.Status, StatusTimedOut, StringComparison.OrdinalIgnoreCase),
                Error: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var polledAtUtc = DateTimeOffset.UtcNow;
            logger.LogDebug(
                ex,
                "Crypto Up or Down 5m result polling request failed. Asset={AssetSymbol} MarketSlug={MarketSlug}",
                candidate.AssetSymbol,
                candidate.MarketSlug);
            var updated = observation with
            {
                LastPollAtUtc = polledAtUtc,
                PollAttempts = observation.PollAttempts + 1,
                Status = ShouldTimeOut(candidate, polledAtUtc) ? StatusTimedOut : StatusPending,
                LastResponseStatus = ResponseError,
                LastError = TrimError(ex.Message),
                UpdatedAtUtc = polledAtUtc
            };
            await repository.UpsertCryptoUpDown5mResultPollingObservationAsync(updated, cancellationToken);
            return new PollResult(
                ResultFound: false,
                TimedOut: string.Equals(updated.Status, StatusTimedOut, StringComparison.OrdinalIgnoreCase),
                Error: true);
        }
    }

    private async Task<InferredMarketResult?> TryUpsertReferencePriceResultAsync(
        PollCandidate candidate,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!options.ReferencePriceResultEnabled)
        {
            return null;
        }

        try
        {
            var result = await TryCreateReferencePriceResultAsync(candidate, observedAtUtc, cancellationToken);
            if (result is null)
            {
                return null;
            }

            await repository.UpsertCryptoUpDown5mWebSocketResolvedMarketAsync(
                BuildResolvedMarketLedgerRow(candidate, result, observedAtUtc, SourceReferenceStartEnd),
                cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Crypto Up or Down 5m reference price result failed. Asset={AssetSymbol} MarketSlug={MarketSlug}",
                candidate.AssetSymbol,
                candidate.MarketSlug);
            return null;
        }
    }

    private async Task<InferredMarketResult?> TryCreateReferencePriceResultAsync(
        PollCandidate candidate,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        var samples = await GetReferencePriceResultSamplesAsync(candidate, cancellationToken);
        if (samples.Count < Math.Max(1, options.ReferencePriceResultMinSamples))
        {
            return null;
        }

        var first = samples[0];
        var end = samples
            .Where(sample => sample.SampledAtUtc <= candidate.MarketEndUtc.AddSeconds(1))
            .OrderBy(sample => sample.SampledAtUtc)
            .ThenBy(sample => sample.CreatedAtUtc)
            .LastOrDefault() ?? samples[^1];
        if (first.StartPriceUsd <= 0m || end.PriceUsd <= 0m)
        {
            return null;
        }

        var endAge = candidate.MarketEndUtc - end.SampledAtUtc;
        if (endAge < TimeSpan.Zero)
        {
            endAge = TimeSpan.Zero;
        }

        var maxEndAge = TimeSpan.FromMilliseconds(Math.Max(1, options.ReferencePriceResultMaxEndAgeMilliseconds));
        if (endAge > maxEndAge)
        {
            return null;
        }

        var comparison = end.PriceUsd.CompareTo(first.StartPriceUsd);
        if (comparison == 0)
        {
            return null;
        }

        var winningOutcome = comparison > 0 ? "Up" : "Down";
        var moveUsd = end.PriceUsd - first.StartPriceUsd;
        var moveBps = first.StartPriceUsd == 0m ? 0m : moveUsd / first.StartPriceUsd * 10_000m;
        return new InferredMarketResult(
            winningOutcome,
            TryGetOutcomeAssetId(candidate.Market, winningOutcome),
            candidate.MarketEndUtc,
            JsonSerializer.Serialize(new
            {
                source = SourceReferenceStartEnd,
                inferred_at_utc = observedAtUtc,
                market_start_utc = candidate.MarketStartUtc,
                market_end_utc = candidate.MarketEndUtc,
                asset_symbol = candidate.AssetSymbol,
                binance_symbol = end.BinanceSymbol,
                start_price_usd = first.StartPriceUsd,
                end_price_usd = end.PriceUsd,
                move_usd = moveUsd,
                move_bps = moveBps,
                winning_outcome = winningOutcome,
                sample_count = samples.Count,
                min_samples = Math.Max(1, options.ReferencePriceResultMinSamples),
                max_end_age_ms = Math.Max(1, options.ReferencePriceResultMaxEndAgeMilliseconds),
                end_tick_age_ms = ToDecimalMilliseconds(endAge),
                start_tick_sampled_at_utc = first.SampledAtUtc,
                end_tick_sampled_at_utc = end.SampledAtUtc,
                end_tick_source_updated_at_utc = end.SourceUpdatedAtUtc
            }));
    }

    private async Task<IReadOnlyList<ReferencePriceResultSample>> GetReferencePriceResultSamplesAsync(
        PollCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (string.Equals(candidate.AssetSymbol, "BTC", StringComparison.OrdinalIgnoreCase))
        {
            var ticks = await repository.GetBtcUpDown5mOddsTicksForMarketAsync(
                candidate.Market.MarketId,
                limit: 500,
                cancellationToken);
            return ticks
                .Select(tick => new ReferencePriceResultSample(
                    "BTC",
                    "BTCUSDT",
                    tick.MarketId,
                    tick.SampledAtUtc,
                    tick.BinancePriceUsd,
                    tick.BinanceStartPriceUsd,
                    tick.BinanceSourceUpdatedAtUtc,
                    tick.CreatedAtUtc))
                .ToArray();
        }

        var cryptoTicks = await repository.GetCryptoUpDown5mOddsTicksForMarketAsync(
            candidate.AssetSymbol,
            candidate.Market.MarketId,
            limit: 500,
            cancellationToken);
        return cryptoTicks
            .Select(tick => new ReferencePriceResultSample(
                tick.AssetSymbol,
                tick.BinanceSymbol,
                tick.MarketId,
                tick.SampledAtUtc,
                tick.BinancePriceUsd,
                tick.BinanceStartPriceUsd,
                tick.BinanceSourceUpdatedAtUtc,
                tick.CreatedAtUtc))
            .ToArray();
    }

    private static string? TryGetOutcomeAssetId(PolymarketGammaMarket market, string outcome)
    {
        if (market.Outcomes.Count == 0 || market.Outcomes.Count != market.ClobTokenIds.Count)
        {
            return null;
        }

        for (var index = 0; index < market.Outcomes.Count; index++)
        {
            if (string.Equals(market.Outcomes[index], outcome, StringComparison.OrdinalIgnoreCase))
            {
                return market.ClobTokenIds[index];
            }
        }

        return null;
    }

    private async Task TryUpsertProvisionalOrderBookResultAsync(
        PollCandidate candidate,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!options.ProvisionalOrderBookResultEnabled)
        {
            return;
        }

        try
        {
            var provisional = await TryCreateProvisionalOrderBookResultAsync(candidate, observedAtUtc, cancellationToken);
            if (provisional is null)
            {
                return;
            }

            await repository.UpsertCryptoUpDown5mWebSocketResolvedMarketAsync(
                BuildResolvedMarketLedgerRow(candidate, provisional, observedAtUtc, SourceTerminalOrderBook),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Crypto Up or Down 5m provisional order-book result failed. Asset={AssetSymbol} MarketSlug={MarketSlug}",
                candidate.AssetSymbol,
                candidate.MarketSlug);
        }
    }

    private async Task<InferredMarketResult?> TryCreateProvisionalOrderBookResultAsync(
        PollCandidate candidate,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        var quotes = BtcUpDown5mMarketAnalyzer.GetOutcomeQuotes(candidate.Market);
        var up = quotes.FirstOrDefault(quote => string.Equals(quote.Outcome, "Up", StringComparison.OrdinalIgnoreCase));
        var down = quotes.FirstOrDefault(quote => string.Equals(quote.Outcome, "Down", StringComparison.OrdinalIgnoreCase));
        if (up is null || down is null)
        {
            return null;
        }

        var upBook = await GetProvisionalOrderBookAsync(up.AssetId, cancellationToken);
        var downBook = await GetProvisionalOrderBookAsync(down.AssetId, cancellationToken);
        if (upBook is null || downBook is null)
        {
            return null;
        }

        var upWins = IsWinnerBook(upBook, downBook);
        var downWins = IsWinnerBook(downBook, upBook);
        if (upWins == downWins)
        {
            return null;
        }

        var winningOutcome = upWins ? "Up" : "Down";
        var winningAssetId = upWins ? up.AssetId : down.AssetId;
        var eventTimestampUtc = upBook.SnapshotAtUtc >= downBook.SnapshotAtUtc
            ? upBook.SnapshotAtUtc
            : downBook.SnapshotAtUtc;
        return new InferredMarketResult(
            winningOutcome,
            winningAssetId,
            eventTimestampUtc,
            JsonSerializer.Serialize(new
            {
                source = SourceTerminalOrderBook,
                inferred_at_utc = observedAtUtc,
                market_end_utc = candidate.MarketEndUtc,
                thresholds = new
                {
                    winner_bid_min = options.ProvisionalWinnerBidMin,
                    loser_ask_max = options.ProvisionalLoserAskMax
                },
                up = BuildBookSummary(upBook),
                down = BuildBookSummary(downBook)
            }));
    }

    private async Task<OrderBookSnapshot?> GetProvisionalOrderBookAsync(
        string assetId,
        CancellationToken cancellationToken)
    {
        var maxAge = TimeSpan.FromMilliseconds(Math.Max(1, options.ProvisionalMaxOrderBookAgeMilliseconds));
        var cacheLookup = marketDataCache.GetOrderBook(assetId, maxAge);
        if (cacheLookup.Status == OrderBookCacheLookupStatus.Fresh && cacheLookup.Snapshot is not null)
        {
            return cacheLookup.Snapshot;
        }

        if (!options.ProvisionalRestFallbackEnabled)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.ProvisionalRestRequestTimeoutSeconds)));
        return await clobClient.GetOrderBookAsync(assetId, timeout.Token);
    }

    private bool IsWinnerBook(OrderBookSnapshot winnerBook, OrderBookSnapshot loserBook)
    {
        return winnerBook.BestBid is { } winnerBid &&
            winnerBid >= options.ProvisionalWinnerBidMin &&
            IsLoserBookLow(loserBook);
    }

    private bool IsLoserBookLow(OrderBookSnapshot loserBook)
    {
        if (loserBook.BestBid is { } loserBid && loserBid <= options.ProvisionalLoserAskMax)
        {
            return true;
        }

        return loserBook.BestAsk is { } loserAsk && loserAsk <= options.ProvisionalLoserAskMax;
    }

    private async Task TryUpsertGammaConfirmedResultAsync(
        PollCandidate candidate,
        PolymarketGammaMarket market,
        string winningOutcome,
        DateTimeOffset confirmedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var quotes = BtcUpDown5mMarketAnalyzer.GetOutcomeQuotes(market);
            var winningAssetId = quotes
                .FirstOrDefault(quote => string.Equals(quote.Outcome, winningOutcome, StringComparison.OrdinalIgnoreCase))
                ?.AssetId;
            var result = new InferredMarketResult(
                string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase) ? "Up" : "Down",
                winningAssetId,
                confirmedAtUtc,
                JsonSerializer.Serialize(new
                {
                    source = SourceGammaClosedMarket,
                    confirmed_at_utc = confirmedAtUtc,
                    market_closed = market.Closed,
                    market_slug = market.Slug,
                    raw_winning_outcome = winningOutcome
                }));
            await repository.UpsertCryptoUpDown5mWebSocketResolvedMarketAsync(
                BuildResolvedMarketLedgerRow(candidate, result, confirmedAtUtc, SourceGammaClosedMarket),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Crypto Up or Down 5m Gamma confirmed result ledger write failed. Asset={AssetSymbol} MarketSlug={MarketSlug}",
                candidate.AssetSymbol,
                candidate.MarketSlug);
        }
    }

    private static CryptoUpDown5mWebSocketResolvedMarket BuildResolvedMarketLedgerRow(
        PollCandidate candidate,
        InferredMarketResult result,
        DateTimeOffset receivedAtUtc,
        string source)
    {
        return new CryptoUpDown5mWebSocketResolvedMarket(
            Guid.NewGuid(),
            candidate.AssetSymbol,
            candidate.Market.MarketId,
            candidate.Market.ConditionId,
            candidate.MarketSlug,
            candidate.MarketStartUtc,
            candidate.MarketEndUtc,
            result.WinningOutcome,
            result.WinningAssetId,
            result.EventTimestampUtc,
            receivedAtUtc,
            receivedAtUtc,
            1,
            ToDecimalSeconds(receivedAtUtc - candidate.MarketEndUtc),
            source,
            source switch
            {
                SourceGammaClosedMarket => "gamma_closed_market",
                SourceReferenceStartEnd => "reference_start_end",
                SourceBinanceTimedClose => "binance_timed_close_provisional",
                _ => "terminal_order_book_provisional"
            },
            result.RawJson,
            receivedAtUtc,
            receivedAtUtc);
    }

    private static object BuildBookSummary(OrderBookSnapshot book)
    {
        return new
        {
            asset_id = book.AssetId,
            condition_id = book.ConditionId,
            snapshot_at_utc = book.SnapshotAtUtc,
            best_bid = book.BestBid,
            best_bid_size = BestBidSize(book),
            best_ask = book.BestAsk,
            best_ask_size = BestAskSize(book),
            last_trade_price = book.LastTradePrice
        };
    }

    private static decimal? BestBidSize(OrderBookSnapshot book)
    {
        return book.BestBid is { } bestBid
            ? book.Bids.Where(level => level.Price == bestBid).Sum(level => level.Size)
            : null;
    }

    private static decimal? BestAskSize(OrderBookSnapshot book)
    {
        return book.BestAsk is { } bestAsk
            ? book.Asks.Where(level => level.Price == bestAsk).Sum(level => level.Size)
            : null;
    }

    private CryptoUpDown5mResultPollingObservation ApplyPollResponse(
        PollCandidate candidate,
        CryptoUpDown5mResultPollingObservation observation,
        PolymarketGammaMarket? market,
        DateTimeOffset polledAtUtc)
    {
        var firstClosedAtUtc = observation.FirstClosedAtUtc;
        var firstWinnerAtUtc = observation.FirstWinnerAtUtc;
        var winningOutcome = observation.WinningOutcome;
        var closedDelaySeconds = observation.ClosedDelaySeconds;
        var resultDelaySeconds = observation.ResultDelaySeconds;
        var status = StatusPending;
        var responseStatus = ResponseNotFound;

        if (market is not null)
        {
            responseStatus = market.Closed ? ResponseClosedWithoutWinner : ResponseNotClosed;
            if (market.Closed && firstClosedAtUtc is null)
            {
                firstClosedAtUtc = polledAtUtc;
                closedDelaySeconds = ToDecimalSeconds(polledAtUtc - candidate.MarketEndUtc);
            }

            if (TryGetWinningOutcome(market, out var outcome))
            {
                responseStatus = ResponseWinnerFound;
                if (firstWinnerAtUtc is null)
                {
                    firstWinnerAtUtc = polledAtUtc;
                    winningOutcome = outcome;
                    resultDelaySeconds = ToDecimalSeconds(polledAtUtc - candidate.MarketEndUtc);
                }

                status = StatusResolved;
            }
        }

        if (!string.Equals(status, StatusResolved, StringComparison.OrdinalIgnoreCase) &&
            ShouldTimeOut(candidate, polledAtUtc))
        {
            status = StatusTimedOut;
        }

        return observation with
        {
            LastPollAtUtc = polledAtUtc,
            PollAttempts = observation.PollAttempts + 1,
            FirstClosedAtUtc = firstClosedAtUtc,
            FirstWinnerAtUtc = firstWinnerAtUtc,
            WinningOutcome = winningOutcome,
            ClosedDelaySeconds = closedDelaySeconds,
            ResultDelaySeconds = resultDelaySeconds,
            Status = status,
            LastResponseStatus = responseStatus,
            LastError = null,
            UpdatedAtUtc = polledAtUtc
        };
    }

    private static CryptoUpDown5mResultPollingObservation ApplyImmediateResultResponse(
        PollCandidate candidate,
        CryptoUpDown5mResultPollingObservation observation,
        string winningOutcome,
        DateTimeOffset resolvedAtUtc,
        string responseStatus)
    {
        var delaySeconds = ToDecimalSeconds(resolvedAtUtc - candidate.MarketEndUtc);
        return observation with
        {
            LastPollAtUtc = resolvedAtUtc,
            PollAttempts = observation.PollAttempts + 1,
            FirstClosedAtUtc = observation.FirstClosedAtUtc ?? resolvedAtUtc,
            FirstWinnerAtUtc = observation.FirstWinnerAtUtc ?? resolvedAtUtc,
            WinningOutcome = string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase) ? "Up" : "Down",
            ClosedDelaySeconds = observation.ClosedDelaySeconds ?? delaySeconds,
            ResultDelaySeconds = observation.ResultDelaySeconds ?? delaySeconds,
            Status = StatusResolved,
            LastResponseStatus = responseStatus,
            LastError = null,
            UpdatedAtUtc = resolvedAtUtc
        };
    }

    private CryptoUpDown5mResultPollingObservation CreateInitialObservation(
        PollCandidate candidate,
        DateTimeOffset nowUtc)
    {
        return new CryptoUpDown5mResultPollingObservation(
            Guid.NewGuid(),
            candidate.AssetSymbol,
            candidate.Market.MarketId,
            candidate.Market.ConditionId,
            candidate.MarketSlug,
            candidate.MarketStartUtc,
            candidate.MarketEndUtc,
            nowUtc,
            nowUtc,
            null,
            0,
            null,
            null,
            null,
            null,
            null,
            StatusPending,
            "created",
            null,
            nowUtc,
            nowUtc);
    }

    private IReadOnlyList<PollCandidate> SelectCandidates(
        IReadOnlyCollection<PolymarketGammaMarket> markets,
        IReadOnlySet<string> assetSymbols,
        DateTimeOffset nowUtc)
    {
        var maxAge = TimeSpan.FromMinutes(options.MaxMarketAgeMinutes);
        return markets
            .Select(market => TryCreateCandidate(market, assetSymbols, nowUtc, maxAge, out var candidate)
                ? candidate
                : null)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderBy(candidate => candidate.MarketEndUtc)
            .ThenBy(candidate => candidate.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<PollCandidate> SelectBinanceTimedCloseCandidates(
        IReadOnlyCollection<PolymarketGammaMarket> markets,
        IReadOnlySet<string> assetSymbols,
        DateTimeOffset nowUtc)
    {
        var closeDelay = TimeSpan.FromMilliseconds(Math.Max(0, options.BinanceTimedCloseDelayMilliseconds));
        var maxAge = TimeSpan.FromSeconds(Math.Max(1, options.BinanceTimedCloseMaxCandidateAgeSeconds));
        return markets
            .Select(market => TryCreateCandidate(market, assetSymbols, nowUtc, maxAge, out var candidate)
                ? candidate
                : null)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .Where(candidate =>
                nowUtc >= candidate.MarketEndUtc.Add(closeDelay) &&
                nowUtc - candidate.MarketEndUtc <= maxAge)
            .OrderBy(candidate => candidate.MarketEndUtc)
            .ThenBy(candidate => candidate.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryCreateCandidate(
        PolymarketGammaMarket market,
        IReadOnlySet<string> assetSymbols,
        DateTimeOffset nowUtc,
        TimeSpan maxAge,
        out PollCandidate candidate)
    {
        candidate = default!;
        var marketSlug = !string.IsNullOrWhiteSpace(market.Slug) ? market.Slug.Trim() : market.EventSlug?.Trim();
        if (string.IsNullOrWhiteSpace(marketSlug) ||
            !CryptoUpDown5mMarketAnalyzer.TryGetAssetSymbol(market, assetSymbols, out var assetSymbol) ||
            CryptoUpDown5mMarketAnalyzer.GetMarketInterval(market) != BtcUpDownMarketInterval.FiveMinutes)
        {
            return false;
        }

        var marketStartUtc = CryptoUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        if (marketStartUtc is null)
        {
            return false;
        }

        var marketEndUtc = marketStartUtc.Value.Add(CryptoUpDown5mMarketAnalyzer.GetIntervalDuration(BtcUpDownMarketInterval.FiveMinutes));
        if (nowUtc < marketEndUtc ||
            nowUtc - marketEndUtc > maxAge)
        {
            return false;
        }

        candidate = new PollCandidate(assetSymbol, market, marketSlug, marketStartUtc.Value, marketEndUtc);
        return true;
    }

    private static bool TryGetWinningOutcome(PolymarketGammaMarket market, out string winningOutcome)
    {
        winningOutcome = string.Empty;
        var quotes = BtcUpDown5mMarketAnalyzer.GetOutcomeQuotes(market);
        if (quotes.Count != 2)
        {
            return false;
        }

        var maxPrice = quotes.Max(quote => quote.Price);
        if (maxPrice <= ResultThreshold)
        {
            return false;
        }

        var winners = quotes
            .Where(quote => quote.Price == maxPrice)
            .ToArray();
        if (winners.Length != 1)
        {
            return false;
        }

        if (string.Equals(winners[0].Outcome, "Up", StringComparison.OrdinalIgnoreCase))
        {
            winningOutcome = "Up";
            return true;
        }

        if (string.Equals(winners[0].Outcome, "Down", StringComparison.OrdinalIgnoreCase))
        {
            winningOutcome = "Down";
            return true;
        }

        return false;
    }

    private bool ShouldTimeOut(PollCandidate candidate, DateTimeOffset nowUtc)
    {
        return nowUtc - candidate.MarketEndUtc >= TimeSpan.FromMinutes(options.MaxResultWaitMinutes);
    }

    private static IReadOnlySet<string> NormalizeSymbols(IEnumerable<string> assetSymbols)
    {
        return assetSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static decimal ToDecimalSeconds(TimeSpan value)
    {
        return Math.Round((decimal)value.TotalSeconds, 3, MidpointRounding.AwayFromZero);
    }

    private static decimal ToDecimalMilliseconds(TimeSpan value)
    {
        return Math.Round((decimal)value.TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
    }

    private static string TrimError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown error";
        }

        var trimmed = message.Trim();
        return trimmed.Length > MaxErrorLength ? trimmed[..MaxErrorLength] : trimmed;
    }

    private static bool IsConfirmedResultSource(string? source)
    {
        return string.Equals(source, SourceGammaClosedMarket, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, SourceMarketWebSocket, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, SourceReferenceStartEnd, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAcceptedResolvedMarketLedgerResult(CryptoUpDown5mWebSocketResolvedMarket result)
    {
        return IsAcceptedResolvedMarketLedgerSource(result.Source) &&
            (string.Equals(result.WinningOutcome, "Up", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.WinningOutcome, "Down", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAcceptedResolvedMarketLedgerSource(string? source)
    {
        return string.Equals(source, SourceReferenceStartEnd, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, SourceBinanceTimedClose, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, SourceMarketWebSocket, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, SourceTerminalOrderBook, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, SourceGammaClosedMarket, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AssetMarketStartKey(string AssetSymbol, DateTimeOffset MarketStartUtc);

    private sealed record InferredMarketResult(
        string WinningOutcome,
        string? WinningAssetId,
        DateTimeOffset EventTimestampUtc,
        string RawJson);

    private sealed record BinanceTimedCloseStartPrice(
        decimal PriceUsd,
        DateTimeOffset SampledAtUtc,
        string BinanceSymbol);

    private sealed record BinanceTimedClosePrice(
        string BinanceSymbol,
        BtcUsdReferencePricePoint Price);

    private enum BinanceTimedCloseResultStatus
    {
        Resolved,
        Uncertain,
        MissingStartPrice,
        MissingClosePrice
    }

    private sealed record BinanceTimedCloseResult(
        BinanceTimedCloseResultStatus Status,
        InferredMarketResult? Result = null)
    {
        public static BinanceTimedCloseResult Resolved(InferredMarketResult result)
        {
            return new BinanceTimedCloseResult(BinanceTimedCloseResultStatus.Resolved, result);
        }

        public static BinanceTimedCloseResult Uncertain()
        {
            return new BinanceTimedCloseResult(BinanceTimedCloseResultStatus.Uncertain);
        }

        public static BinanceTimedCloseResult MissingStartPrice()
        {
            return new BinanceTimedCloseResult(BinanceTimedCloseResultStatus.MissingStartPrice);
        }

        public static BinanceTimedCloseResult MissingClosePrice()
        {
            return new BinanceTimedCloseResult(BinanceTimedCloseResultStatus.MissingClosePrice);
        }
    }

    private sealed record PollCandidate(
        string AssetSymbol,
        PolymarketGammaMarket Market,
        string MarketSlug,
        DateTimeOffset MarketStartUtc,
        DateTimeOffset MarketEndUtc);

    private sealed record ReferencePriceResultSample(
        string AssetSymbol,
        string BinanceSymbol,
        string MarketId,
        DateTimeOffset SampledAtUtc,
        decimal PriceUsd,
        decimal StartPriceUsd,
        DateTimeOffset SourceUpdatedAtUtc,
        DateTimeOffset CreatedAtUtc);

    private sealed record PollResult(bool ResultFound, bool TimedOut, bool Error);
}
