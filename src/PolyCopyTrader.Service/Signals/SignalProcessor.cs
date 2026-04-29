using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.Signals;

public sealed class SignalProcessor(
    ILogger<SignalProcessor> logger,
    BotOptions botOptions,
    PaperTradingOptions paperTradingOptions,
    WatchlistOptions watchlistOptions,
    ILeaderTradeCandidateQueue candidateQueue,
    IPolymarketClobPublicClient clobClient,
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
                var decision = await EvaluateAsync(trade, cancellationToken);
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
                await repository.AddApiErrorAsync(
                    new ApiError(Guid.NewGuid(), "SignalProcessor", "ProcessTrade", ex.Message, DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        }

        return new SignalProcessingResult(candidates.Count, accepted, rejected, paperOrdersCreated);
    }

    private async Task<SignalDecision> EvaluateAsync(LeaderTrade trade, CancellationToken cancellationToken)
    {
        var traderRule = ResolveTraderRule(trade);
        var orderBook = trade.Side == TradeSide.Buy
            ? await clobClient.GetOrderBookAsync(trade.AssetId, cancellationToken)
            : null;
        var openOrders = await repository.GetOpenPaperOrdersAsync(cancellationToken);
        var positions = await repository.GetPaperPositionsAsync(cancellationToken);
        var exposure = BuildExposure(trade, openOrders, positions);

        return signalEngine.Evaluate(
            new SignalEvaluationContext(
                trade,
                traderRule,
                null,
                orderBook,
                exposure));
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
}
