using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.Signals;

public sealed class SignalProcessor(
    ILogger<SignalProcessor> logger,
    BotOptions botOptions,
    PolymarketAuthOptions authOptions,
    PaperTradingOptions paperTradingOptions,
    WatchlistOptions watchlistOptions,
    ILeaderTradeCandidateQueue candidateQueue,
    IPolymarketClobPublicClient clobClient,
    IPolymarketTradingClient tradingClient,
    ISignalEngine signalEngine,
    IPaperTradingEngine paperTradingEngine,
    IAppRepository repository) : ISignalProcessor
{
    private const int MaxCandidatesPerLoop = 500;

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

        return new SignalProcessingResult(candidates.Count, accepted, rejected, paperOrdersCreated);
    }

    private async Task<SignalEvaluationResult> EvaluateAsync(LeaderTrade trade, CancellationToken cancellationToken)
    {
        var traderRule = ResolveTraderRule(trade);
        var orderBook = trade.Side == TradeSide.Buy
            ? await clobClient.GetOrderBookAsync(trade.AssetId, cancellationToken)
            : null;
        var openOrders = await repository.GetOpenPaperOrdersAsync(cancellationToken);
        var positions = await repository.GetPaperPositionsAsync(cancellationToken);
        var exposure = BuildExposure(trade, openOrders, positions);

        var decision = signalEngine.Evaluate(
            new SignalEvaluationContext(
                trade,
                traderRule,
                null,
                orderBook,
                exposure));

        return new SignalEvaluationResult(decision, orderBook);
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
        IReadOnlyList<PaperPosition> positions)
    {
        var orderExposure = openOrders.Sum(order => order.NotionalUsd);
        var positionExposure = positions.Sum(position => position.EstimatedValueUsd);
        var marketOrderExposure = openOrders
            .Where(order => string.Equals(order.ConditionId, trade.ConditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.NotionalUsd);
        var marketPositionExposure = positions
            .Where(position => string.Equals(position.ConditionId, trade.ConditionId, StringComparison.OrdinalIgnoreCase))
            .Sum(position => position.EstimatedValueUsd);
        var oldestOpenOrderAgeSeconds = openOrders.Count == 0
            ? 0
            : (int)Math.Max(
                0,
                openOrders.Max(order => (DateTimeOffset.UtcNow - order.CreatedAtUtc).TotalSeconds));

        return new ExposureSnapshot(
            marketOrderExposure + marketPositionExposure,
            0m,
            0m,
            orderExposure + positionExposure,
            0m,
            openOrders.Count,
            oldestOpenOrderAgeSeconds);
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
