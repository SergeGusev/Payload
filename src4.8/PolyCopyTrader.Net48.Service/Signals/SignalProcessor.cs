using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.LiveTrading;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Service.Strategies;
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
    IExposureSnapshotCache exposureCache,
    IStrategyStateProvider strategyStateProvider,
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

        var followLeaderSettings = await strategyStateProvider.GetStrategySettingsAsync(StrategyIds.FollowLeader, cancellationToken);
        if (!followLeaderSettings.Enabled)
        {
            logger.LogInformation(
                "Follow leader signal processing skipped because the strategy is disabled. DroppedCandidates={DroppedCandidates}",
                candidates.Count);
            return new SignalProcessingResult(candidates.Count, 0, candidates.Count, 0);
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
                    if (RuntimeModePolicy.IsPaperTradingEnabled(botOptions, paperTradingOptions) &&
                        decision.ProposedPrice is { } price &&
                        decision.ProposedSizeShares is { } sizeShares)
                    {
                        var order = paperTradingEngine.CreateOrder(
                            signal,
                            price,
                            sizeShares,
                            decision.CreatedAtUtc.AddSeconds(paperTradingOptions.DefaultOrderTtlSeconds));
                        await repository.AddPaperOrderAsync(order, cancellationToken);
                        exposureCache.ApplyPaperOrder(order);
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

                    if (botOptions.Mode == BotMode.Live && followLeaderSettings.LiveStakes)
                    {
                        liveOrdersSubmitted += await TryPlaceLiveOrderAsync(signal, trade, followLeaderSettings, cancellationToken) ? 1 : 0;
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
        var marketInfo = await ResolveMarketInfoAsync(trade, cancellationToken);
        var leaderCategoryPerformance = await ResolveLeaderCategoryPerformanceAsync(trade, marketInfo, cancellationToken);
        var copiedTraderOverallPerformance = await ResolveCopiedTraderPerformanceAsync(trade, "OVERALL", cancellationToken);
        var copiedTraderCategoryPerformance = await ResolveCopiedTraderPerformanceAsync(trade, marketInfo.Category, cancellationToken);
        var orderBook = trade.Side is TradeSide.Buy or TradeSide.Sell
            ? await clobClient.GetOrderBookAsync(trade.AssetId, cancellationToken)
            : null;
        var exposureSnapshot = await exposureCache.GetSnapshotAsync(cancellationToken);
        var exposure = BuildExposure(
            trade,
            exposureSnapshot.OpenPaperOrders,
            exposureSnapshot.PaperPositions,
            exposureSnapshot.OpenLiveOrders);
        var availablePositionSize = FindCopiedPosition(exposureSnapshot.PaperPositions, trade)?.SizeShares;

        var decision = signalEngine.Evaluate(
            new SignalEvaluationContext(
                trade,
                traderRule,
                marketInfo,
                orderBook,
                exposure,
                leaderCategoryPerformance,
                copiedTraderOverallPerformance,
                copiedTraderCategoryPerformance,
                availablePositionSize));

        return new SignalEvaluationResult(decision, orderBook);
    }

    private async Task<bool> TryPlaceLiveOrderAsync(
        Signal signal,
        LeaderTrade trade,
        StrategyRuntimeSettings strategySettings,
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

        validation.Add("Live placement for leader-price Follow leader signals is disabled until a separate live execution policy is explicitly implemented.");

        if (IsBlockedMarketForLive(trade))
        {
            validation.Add("Initial live trading blocks crypto and sports markets.");
        }

        var exposureSnapshot = await exposureCache.GetSnapshotAsync(cancellationToken);
        var openLiveOrders = exposureSnapshot.OpenLiveOrders;
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
            var clockCheckUtc = DateTimeOffset.UtcNow;
            if (Math.Abs((serverTime - clockCheckUtc).TotalSeconds) > liveTradingOptions.MaxClockDriftSeconds)
            {
                validation.Add("CLOB server time drift exceeds configured limit.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validation.Add("Live preflight market-data check failed: " + ex.Message);
        }

        var traderRule = ResolveTraderRule(trade);
        var marketInfo = await ResolveMarketInfoAsync(trade, cancellationToken);
        var leaderCategoryPerformance = await ResolveLeaderCategoryPerformanceAsync(trade, marketInfo, cancellationToken);
        var copiedTraderOverallPerformance = await ResolveCopiedTraderPerformanceAsync(trade, "OVERALL", cancellationToken);
        var copiedTraderCategoryPerformance = await ResolveCopiedTraderPerformanceAsync(trade, marketInfo.Category, cancellationToken);
        var exposure = BuildExposure(
            trade,
            exposureSnapshot.OpenPaperOrders,
            exposureSnapshot.PaperPositions,
            openLiveOrders);
        var availablePositionSize = FindCopiedPosition(exposureSnapshot.PaperPositions, trade)?.SizeShares;
        var freshDecision = signalEngine.Evaluate(new SignalEvaluationContext(
            trade,
            traderRule,
            marketInfo,
            orderBook,
            exposure,
            leaderCategoryPerformance,
            copiedTraderOverallPerformance,
            copiedTraderCategoryPerformance,
            availablePositionSize));
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
            validation.Add("Live maker BUY price would cross/use best ask, or lacks a fresh best ask; live taker execution is not enabled.");
        }

        var maxNotional = Math.Min(
            liveTradingOptions.MaxOrderNotionalUsd,
            liveTradingOptions.MaxTradeBankrollPct / 100m * paperTradingOptions.InitialBankrollUsd);
        var desiredNotional = strategySettings.LiveStakeAmount > 0m
            ? strategySettings.LiveStakeAmount
            : freshDecision.ProposedNotionalUsd ?? 0m;
        var notional = Math.Min(desiredNotional, maxNotional);
        if (notional > 0m)
        {
            await ValidateStrategyLiveBalanceAsync(
                strategySettings,
                openLiveOrders,
                notional,
                validation,
                now,
                cancellationToken);
        }

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

    private async Task ValidateStrategyLiveBalanceAsync(
        StrategyRuntimeSettings strategySettings,
        IReadOnlyList<LiveOrder> openLiveOrders,
        decimal requiredNotionalUsd,
        List<string> validation,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategySettings.StrategyId);
        var reservedNotionalUsd = openLiveOrders
            .Where(order => StrategyIds.Normalize(order.StrategyId) == normalizedStrategyId)
            .Sum(order => order.NotionalUsd);
        var availableForNewStake = strategySettings.LiveAvailableBalance - reservedNotionalUsd;
        if (availableForNewStake >= requiredNotionalUsd)
        {
            return;
        }

        var message =
            $"Strategy live available balance is insufficient. StrategyId={normalizedStrategyId}; " +
            $"Available={strategySettings.LiveAvailableBalance:0.########}; Reserved={reservedNotionalUsd:0.########}; " +
            $"AvailableForNewStake={availableForNewStake:0.########}; Required={requiredNotionalUsd:0.########}.";
        validation.Add(message);
        logger.LogError(
            "Strategy live available balance is insufficient. StrategyId={StrategyId} Available={AvailableBalance} Reserved={ReservedNotionalUsd} Required={RequiredNotionalUsd}. Live stakes will be disabled for this strategy.",
            normalizedStrategyId,
            strategySettings.LiveAvailableBalance,
            reservedNotionalUsd,
            requiredNotionalUsd);
        await repository.SetStrategyLiveStakesAsync(normalizedStrategyId, false, nowUtc, cancellationToken);
        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(Guid.NewGuid(), "StrategyLiveBalance", "Error", message, nowUtc),
            cancellationToken);
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
            signal.CreatedAtUtc,
            StrategyId: StrategyIds.FollowLeader);
    }

    private async Task PersistLiveOrderAsync(
        LiveOrder order,
        string action,
        string status,
        string details,
        CancellationToken cancellationToken)
    {
        await repository.AddLiveOrderAsync(order, cancellationToken);
        exposureCache.ApplyLiveOrder(order);
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
            DateTimeOffset.UtcNow,
            StrategyId: StrategyIds.FollowLeader);
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
        var fillSummary = LiveOrderPlacementAccounting.FromPlacementResult(
            trade.Side,
            price,
            sizeShares,
            status,
            result);
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
            fillSummary.FilledSize,
            fillSummary.RemainingSize,
            string.Empty,
            string.IsNullOrWhiteSpace(result.RawResponseJson) ? "{}" : result.RawResponseJson,
            result.ErrorMessage ?? string.Empty,
            DateTimeOffset.UtcNow,
            StrategyId: StrategyIds.FollowLeader,
            AverageFillPrice: fillSummary.AverageFillPrice,
            FilledNotionalUsd: fillSummary.FilledNotionalUsd,
            CostBasisUsd: fillSummary.CostBasisUsd);
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
            "unmatched" => LiveOrderStatus.Unmatched,
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

    private static PaperPosition? FindCopiedPosition(
        IEnumerable<PaperPosition> positions,
        LeaderTrade trade)
    {
        return positions.FirstOrDefault(position =>
            string.Equals(position.AssetId, trade.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(position.CopiedTraderWallet, trade.TraderWallet, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MarketInfo> ResolveMarketInfoAsync(LeaderTrade trade, CancellationToken cancellationToken)
    {
        var metadata = await repository.GetPolymarketOnChainTokenMetadataAsync(trade.AssetId, cancellationToken);
        if (metadata is null)
        {
            return new MarketInfo(
                trade.ConditionId,
                trade.MarketSlug,
                trade.MarketTitle,
                null,
                null);
        }

        return new MarketInfo(
            string.IsNullOrWhiteSpace(metadata.ConditionId) ? trade.ConditionId : metadata.ConditionId,
            string.IsNullOrWhiteSpace(metadata.MarketSlug) ? trade.MarketSlug : metadata.MarketSlug,
            string.IsNullOrWhiteSpace(metadata.MarketTitle) ? trade.MarketTitle : metadata.MarketTitle,
            NormalizeCategory(metadata.Category),
            metadata.EndDateUtc);
    }

    private async Task<PolymarketOnChainWalletCategoryPerformance?> ResolveLeaderCategoryPerformanceAsync(
        LeaderTrade trade,
        MarketInfo marketInfo,
        CancellationToken cancellationToken)
    {
        var category = NormalizeCategory(marketInfo.Category);
        if (IsUnknownCategory(category))
        {
            return null;
        }

        return await repository.GetPolymarketOnChainWalletCategoryPerformanceAsync(
            NormalizeWallet(trade.TraderWallet),
            category!,
            cancellationToken);
    }

    private async Task<PaperCopiedTraderPerformance?> ResolveCopiedTraderPerformanceAsync(
        LeaderTrade trade,
        string? category,
        CancellationToken cancellationToken)
    {
        var normalizedCategory = NormalizeCategory(category);
        if (IsUnknownCategory(normalizedCategory))
        {
            return null;
        }

        return await repository.GetPaperCopiedTraderPerformanceAsync(
            NormalizeWallet(trade.TraderWallet),
            normalizedCategory!,
            cancellationToken);
    }

    private static string NormalizeWallet(string wallet)
    {
        return wallet.Trim().ToLowerInvariant();
    }

    private static string? NormalizeCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? null : category.Trim();
    }

    private static bool IsUnknownCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ||
            string.Equals(category, "unknown", StringComparison.OrdinalIgnoreCase);
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
