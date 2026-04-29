using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.Signals;

public sealed class SignalProcessor(
    ILogger<SignalProcessor> logger,
    BotOptions botOptions,
    PolymarketAuthOptions authOptions,
    PaperTradingOptions paperTradingOptions,
    LiveTradingOptions liveTradingOptions,
    WatchlistOptions watchlistOptions,
    ILeaderTradeCandidateQueue candidateQueue,
    IPolymarketClobPublicClient clobClient,
    IPolymarketGeoClient geoClient,
    IPolymarketTradingClient tradingClient,
    IPolymarketAuthService authService,
    ISignalEngine signalEngine,
    IPaperTradingEngine paperTradingEngine,
    ServiceControlState controlState,
    IAppRepository repository) : ISignalProcessor
{
    private const int MaxCandidatesPerLoop = 500;
    private static readonly string[] BlockedLiveMarketText =
    [
        "crypto",
        "bitcoin",
        " btc",
        "ethereum",
        " eth",
        "solana",
        "sports",
        " nba",
        " nfl",
        " mlb",
        " nhl",
        " epl",
        "soccer",
        "football"
    ];

    public async Task<SignalProcessingResult> ProcessQueuedAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await candidateQueue.DrainAsync(MaxCandidatesPerLoop, cancellationToken);
        if (candidates.Count == 0)
        {
            return new SignalProcessingResult(0, 0, 0, 0);
        }

        var accepted = 0;
        var rejected = 0;
        var paperOrdersCreated = 0;
        var liveOrdersSubmitted = 0;
        foreach (var trade in candidates)
        {
            try
            {
                var evaluation = await EvaluateAsync(trade, cancellationToken);
                var decision = evaluation.Decision;
                var signal = ToSignal(trade, decision);
                await repository.AddSignalAsync(signal, cancellationToken);

                if (decision.Accepted)
                {
                    accepted++;
                    if (botOptions.Mode == BotMode.Paper &&
                        decision.ProposedPrice is { } price &&
                        decision.ProposedSizeShares is { } sizeShares)
                    {
                        var order = paperTradingEngine.CreateOrder(
                            signal,
                            price,
                            sizeShares,
                            decision.CreatedAtUtc.AddSeconds(paperTradingOptions.DefaultOrderTtlSeconds));
                        await repository.AddPaperOrderAsync(order, cancellationToken);
                        paperOrdersCreated++;
                    }

                    if (botOptions.Mode == BotMode.DryRun &&
                        decision.ProposedPrice is { } dryRunPrice &&
                        decision.ProposedSizeShares is { } dryRunSizeShares)
                    {
                        var result = await tradingClient.PrepareDryRunOrderAsync(
                            CreateDryRunRequest(trade, dryRunPrice, dryRunSizeShares, evaluation.OrderBook, decision.CreatedAtUtc),
                            cancellationToken);
                        await repository.AddDryRunOrderAsync(ToDryRunOrder(signal, trade, dryRunPrice, dryRunSizeShares, result), cancellationToken);
                    }

                    if (botOptions.Mode == BotMode.Live)
                    {
                        liveOrdersSubmitted += await TryPlaceLiveOrderAsync(signal, trade, cancellationToken) ? 1 : 0;
                    }

                    continue;
                }

                rejected++;
                foreach (var reason in decision.Reasons)
                {
                    await repository.AddSignalRejectionAsync(
                        new SignalRejection(Guid.NewGuid(), signal.Id, reason, reason, decision.CreatedAtUtc),
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                rejected++;
                logger.LogError(ex, "Signal processing failed for trade {TransactionHash}.", trade.TransactionHash);
                await TryRecordApiErrorAsync("ProcessTrade", ex.Message, cancellationToken);
            }
        }

        return new SignalProcessingResult(candidates.Count, accepted, rejected, paperOrdersCreated, liveOrdersSubmitted);
    }

    private async Task<SignalEvaluationResult> EvaluateAsync(LeaderTrade trade, CancellationToken cancellationToken)
    {
        var traderRule = ResolveTraderRule(trade);
        var orderBook = trade.Side == TradeSide.Buy
            ? await clobClient.GetOrderBookAsync(trade.AssetId, cancellationToken)
            : null;
        var openOrders = await repository.GetOpenPaperOrdersAsync(cancellationToken);
        var positions = await repository.GetPaperPositionsAsync(cancellationToken);
        var liveOrders = await repository.GetOpenLiveOrdersAsync(cancellationToken);
        var exposure = BuildExposure(trade, openOrders, positions, liveOrders);

        var decision = signalEngine.Evaluate(
            new SignalEvaluationContext(
                trade,
                traderRule,
                null,
                orderBook,
                exposure));

        return new SignalEvaluationResult(decision, orderBook);
    }

    private async Task<bool> TryPlaceLiveOrderAsync(
        Signal signal,
        LeaderTrade trade,
        CancellationToken cancellationToken)
    {
        var validation = new List<string>();
        var now = DateTimeOffset.UtcNow;

        if (!botOptions.EnableLiveTrading)
        {
            validation.Add("Live trading is not explicitly enabled.");
        }

        if (controlState.KillSwitchActive)
        {
            validation.Add("Kill switch is active.");
        }

        if (controlState.LiveTradingPaused)
        {
            validation.Add("Live trading is paused.");
        }

        if (trade.Side != TradeSide.Buy)
        {
            validation.Add("Initial live trading only allows BUY orders.");
        }

        if (IsBlockedMarketForLive(trade))
        {
            validation.Add("Initial live trading blocks crypto and sports markets.");
        }

        var openLiveOrders = await repository.GetOpenLiveOrdersAsync(cancellationToken);
        if (openLiveOrders.Count >= liveTradingOptions.MaxOpenLiveOrders)
        {
            validation.Add("Maximum open live order count reached.");
        }

        if (openLiveOrders.Any(order => now - order.CreatedAtUtc > TimeSpan.FromSeconds(liveTradingOptions.DefaultOrderTtlSeconds)))
        {
            validation.Add("A stale live order exists; live placement is locked until maintenance cancels it.");
        }

        var apiErrors = await repository.GetRecentApiErrorsAsync(cancellationToken: cancellationToken);
        var lockoutStart = now.AddMinutes(-liveTradingOptions.ApiErrorLockoutWindowMinutes);
        var recentPolymarketErrors = apiErrors.Count(error =>
            error.CreatedAtUtc >= lockoutStart &&
            error.Component.Contains("Polymarket", StringComparison.OrdinalIgnoreCase));
        if (recentPolymarketErrors >= liveTradingOptions.ApiErrorLockoutCount)
        {
            validation.Add("API error lockout is active.");
        }

        var riskEvents = await repository.GetRecentRiskEventsAsync(cancellationToken: cancellationToken);
        if (riskEvents.Any(item =>
            item.CreatedAtUtc >= now.AddDays(-1) &&
            item.ReasonCode.Contains("daily_loss", StringComparison.OrdinalIgnoreCase)))
        {
            validation.Add("Daily loss lockout is active.");
        }

        var authReadiness = await authService.GetReadinessAsync(cancellationToken);
        if (!authReadiness.CanAuthenticate)
        {
            validation.Add("Polymarket auth is not ready: " + string.Join(", ", authReadiness.MissingRequirements));
        }

        try
        {
            var geoblock = await geoClient.GetGeoblockStatusAsync(cancellationToken);
            if (geoblock.Blocked)
            {
                validation.Add($"Geoblock is active for VPS IP {geoblock.Ip ?? "unknown"}.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validation.Add("Geoblock check failed: " + ex.Message);
        }

        OrderBookSnapshot? orderBook = null;
        try
        {
            orderBook = await clobClient.GetOrderBookAsync(trade.AssetId, cancellationToken);
            var serverTime = await clobClient.GetServerTimeAsync(cancellationToken);
            if (Math.Abs((serverTime - now).TotalSeconds) > liveTradingOptions.MaxClockDriftSeconds)
            {
                validation.Add("CLOB server time drift exceeds configured limit.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validation.Add("Live preflight market-data check failed: " + ex.Message);
        }

        var traderRule = ResolveTraderRule(trade);
        var paperOrders = await repository.GetOpenPaperOrdersAsync(cancellationToken);
        var positions = await repository.GetPaperPositionsAsync(cancellationToken);
        var exposure = BuildExposure(trade, paperOrders, positions, openLiveOrders);
        var freshDecision = signalEngine.Evaluate(new SignalEvaluationContext(trade, traderRule, null, orderBook, exposure));
        if (!freshDecision.Accepted)
        {
            validation.Add("Fresh signal evaluation rejected live order: " + string.Join(", ", freshDecision.Reasons));
        }

        if (freshDecision.ProposedPrice is not { } price || freshDecision.ProposedSizeShares is not { } proposedSizeShares)
        {
            validation.Add("Fresh signal evaluation did not produce a live order price and size.");
            price = 0m;
            proposedSizeShares = 0m;
        }

        if (orderBook?.BestAsk is not { } bestAsk || price <= 0m || price >= bestAsk)
        {
            validation.Add("Live maker BUY price would cross or lacks a fresh best ask.");
        }

        var maxNotional = Math.Min(
            liveTradingOptions.MaxOrderNotionalUsd,
            liveTradingOptions.MaxTradeBankrollPct / 100m * paperTradingOptions.InitialBankrollUsd);
        var notional = Math.Min(freshDecision.ProposedNotionalUsd ?? 0m, maxNotional);
        var liveSizeShares = price > 0m ? RoundDown(notional / price, 4) : 0m;
        if (liveSizeShares <= 0m)
        {
            validation.Add("Live order size after tiny-size cap is zero.");
        }

        if (validation.Count > 0)
        {
            await PersistLiveOrderAsync(
                CreateRejectedLiveOrder(signal, trade, price, liveSizeShares, validation, now),
                "Preflight",
                "Rejected",
                string.Join("; ", validation),
                cancellationToken);
            return false;
        }

        var request = CreateLiveRequest(trade, price, liveSizeShares, orderBook, now);
        LiveOrderPlacementResult result;
        try
        {
            result = await tradingClient.PlaceLiveOrderAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await PersistLiveOrderAsync(
                CreateRejectedLiveOrder(signal, trade, price, liveSizeShares, ["Live order placement failed: " + ex.Message], now) with
                {
                    Status = LiveOrderStatus.Error
                },
                "PlaceLiveOrder",
                "Error",
                ex.Message,
                cancellationToken);
            return false;
        }

        var liveOrder = ToLiveOrder(signal, trade, price, liveSizeShares, request.GtdExpirationUtc!.Value, result);
        if (string.Equals(result.ResponseStatus, "matched", StringComparison.OrdinalIgnoreCase))
        {
            liveOrder = liveOrder with
            {
                Status = LiveOrderStatus.Error,
                ValidationSummary = "Maker-only live order returned matched status; live trading paused."
            };
            controlState.PauseLiveTrading("SignalProcessor");
        }

        await PersistLiveOrderAsync(
            liveOrder,
            "PlaceLiveOrder",
            result.Success ? "OK" : "Rejected",
            result.ErrorMessage ?? result.ResponseStatus,
            cancellationToken);
        return result.Success && (liveOrder.Status is LiveOrderStatus.Live or LiveOrderStatus.Delayed);
    }

    private ClobV2OrderRequest CreateDryRunRequest(
        LeaderTrade trade,
        decimal price,
        decimal sizeShares,
        OrderBookSnapshot? orderBook,
        DateTimeOffset createdAtUtc)
    {
        var signer = authOptions.SigningAddress;
        var maker = string.IsNullOrWhiteSpace(authOptions.FunderAddress)
            ? signer
            : authOptions.FunderAddress;

        return new ClobV2OrderRequest(
            trade.AssetId,
            trade.Side,
            price,
            sizeShares,
            orderBook?.TickSize ?? 0.01m,
            orderBook?.MinOrderSize ?? 1m,
            maker,
            signer,
            ParseSignatureType(authOptions.SignatureType),
            ClobV2OrderType.GTC,
            createdAtUtc,
            NegativeRisk: orderBook?.NegativeRisk ?? false);
    }

    private ClobV2OrderRequest CreateLiveRequest(
        LeaderTrade trade,
        decimal price,
        decimal sizeShares,
        OrderBookSnapshot? orderBook,
        DateTimeOffset createdAtUtc)
    {
        return new ClobV2OrderRequest(
            trade.AssetId,
            trade.Side,
            price,
            sizeShares,
            orderBook?.TickSize ?? 0.01m,
            orderBook?.MinOrderSize ?? 1m,
            authOptions.FunderAddress,
            authOptions.SigningAddress,
            ParseSignatureType(authOptions.SignatureType),
            ClobV2OrderType.GTD,
            createdAtUtc,
            createdAtUtc.AddSeconds(liveTradingOptions.DefaultOrderTtlSeconds),
            NegativeRisk: orderBook?.NegativeRisk ?? false,
            PostOnly: true);
    }

    private static DryRunOrder ToDryRunOrder(
        Signal signal,
        LeaderTrade trade,
        decimal price,
        decimal sizeShares,
        ClobV2DryRunOrderResult result)
    {
        return new DryRunOrder(
            Guid.NewGuid(),
            signal.Id,
            result.Status,
            trade.Side,
            trade.AssetId,
            trade.ConditionId,
            trade.Outcome,
            price,
            sizeShares,
            price * sizeShares,
            result.Order.OrderType.ToString(),
            result.RedactedPayloadJson,
            result.ValidationMessages.Count == 0 ? string.Empty : string.Join("; ", result.ValidationMessages),
            signal.CreatedAtUtc);
    }

    private async Task PersistLiveOrderAsync(
        LiveOrder order,
        string action,
        string status,
        string details,
        CancellationToken cancellationToken)
    {
        await repository.AddLiveOrderAsync(order, cancellationToken);
        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(Guid.NewGuid(), action, status, details, DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private static LiveOrder CreateRejectedLiveOrder(
        Signal signal,
        LeaderTrade trade,
        decimal price,
        decimal sizeShares,
        IReadOnlyList<string> validation,
        DateTimeOffset createdAtUtc)
    {
        return new LiveOrder(
            Guid.NewGuid(),
            signal.Id,
            LiveOrderStatus.PreflightRejected,
            null,
            trade.Side,
            trade.AssetId,
            trade.ConditionId,
            trade.Outcome,
            price,
            sizeShares,
            price * sizeShares,
            "GTD",
            createdAtUtc,
            createdAtUtc,
            null,
            "preflight_rejected",
            0m,
            0m,
            string.Empty,
            "{}",
            string.Join("; ", validation),
            DateTimeOffset.UtcNow);
    }

    private static LiveOrder ToLiveOrder(
        Signal signal,
        LeaderTrade trade,
        decimal price,
        decimal sizeShares,
        DateTimeOffset expiresAtUtc,
        LiveOrderPlacementResult result)
    {
        var status = MapPlacementStatus(result);
        return new LiveOrder(
            Guid.NewGuid(),
            signal.Id,
            status,
            result.OrderId,
            trade.Side,
            trade.AssetId,
            trade.ConditionId,
            trade.Outcome,
            price,
            sizeShares,
            price * sizeShares,
            "GTD",
            signal.CreatedAtUtc,
            expiresAtUtc,
            result.Success ? DateTimeOffset.UtcNow : null,
            result.ResponseStatus,
            status == LiveOrderStatus.Matched ? sizeShares : 0m,
            status == LiveOrderStatus.Matched ? 0m : sizeShares,
            string.Empty,
            string.IsNullOrWhiteSpace(result.RawResponseJson) ? "{}" : result.RawResponseJson,
            result.ErrorMessage ?? string.Empty,
            DateTimeOffset.UtcNow);
    }

    private static LiveOrderStatus MapPlacementStatus(LiveOrderPlacementResult result)
    {
        if (!result.Success)
        {
            return LiveOrderStatus.Rejected;
        }

        return (result.ResponseStatus ?? string.Empty).ToLowerInvariant() switch
        {
            "live" => LiveOrderStatus.Live,
            "matched" => LiveOrderStatus.Matched,
            "delayed" => LiveOrderStatus.Delayed,
            _ => LiveOrderStatus.Submitted
        };
    }

    private static bool IsBlockedMarketForLive(LeaderTrade trade)
    {
        var text = " " + trade.MarketSlug + " " + trade.MarketTitle + " ";
        return BlockedLiveMarketText.Any(blocked =>
            text.Contains(blocked, StringComparison.OrdinalIgnoreCase));
    }

    private static decimal RoundDown(decimal value, int decimals)
    {
        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Floor(value * factor) / factor;
    }

    private static ClobV2SignatureType ParseSignatureType(string value)
    {
        return Enum.TryParse<ClobV2SignatureType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ClobV2SignatureType.EOA;
    }

    private TraderRule ResolveTraderRule(LeaderTrade trade)
    {
        var configured = watchlistOptions.Traders.FirstOrDefault(
            trader => string.Equals(
                WalletAddressValidator.Normalize(trader.Wallet),
                WalletAddressValidator.Normalize(trade.TraderWallet),
                StringComparison.Ordinal));

        if (configured is null)
        {
            return new TraderRule(trade.TraderWallet, [], 0, 0m, 0m, 0m, 0m, Enabled: false);
        }

        return new TraderRule(
            trade.TraderWallet,
            configured.AllowedCategories,
            configured.MaxLagSeconds,
            configured.MaxSlippageCents,
            configured.MaxSpreadCents,
            configured.MaxSpreadPct,
            configured.MinLeaderTradeUsd,
            configured.Enabled);
    }

    private static ExposureSnapshot BuildExposure(
        LeaderTrade trade,
        IReadOnlyList<PaperOrder> openOrders,
        IReadOnlyList<PaperPosition> positions,
        IReadOnlyList<LiveOrder> liveOrders)
    {
        var orderExposure = openOrders.Sum(order => order.NotionalUsd);
        var liveOrderExposure = liveOrders.Sum(order => order.NotionalUsd);
        var positionExposure = positions.Sum(position => position.EstimatedValueUsd);
        var marketOrderExposure = openOrders
            .Where(order => string.Equals(order.ConditionId, trade.ConditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.NotionalUsd);
        var marketLiveOrderExposure = liveOrders
            .Where(order => string.Equals(order.ConditionId, trade.ConditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.NotionalUsd);
        var marketPositionExposure = positions
            .Where(position => string.Equals(position.ConditionId, trade.ConditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(position => position.EstimatedValueUsd);
        var oldestPaperOrderAgeSeconds = openOrders.Count == 0
            ? 0
            : (int)Math.Max(
                0,
                openOrders.Max(order => (DateTimeOffset.UtcNow - order.CreatedAtUtc).TotalSeconds));
        var oldestLiveOrderAgeSeconds = liveOrders.Count == 0
            ? 0
            : (int)Math.Max(
                0,
                liveOrders.Max(order => (DateTimeOffset.UtcNow - order.CreatedAtUtc).TotalSeconds));

        return new ExposureSnapshot(
            marketOrderExposure + marketLiveOrderExposure + marketPositionExposure,
            0m,
            0m,
            orderExposure + liveOrderExposure + positionExposure,
            0m,
            openOrders.Count + liveOrders.Count,
            Math.Max(oldestPaperOrderAgeSeconds, oldestLiveOrderAgeSeconds));
    }

    private static Signal ToSignal(LeaderTrade trade, SignalDecision decision)
    {
        return new Signal(
            Guid.NewGuid(),
            trade,
            decision.Score,
            decision.Accepted,
            decision.DecisionCode,
            decision.Reasons,
            decision.ProposedPrice,
            decision.ProposedSizeShares,
            decision.ProposedNotionalUsd,
            decision.CreatedAtUtc);
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "SignalProcessor", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist signal processor API error for {Operation}.", operation);
        }
    }

    private sealed record SignalEvaluationResult(SignalDecision Decision, OrderBookSnapshot? OrderBook);
}
