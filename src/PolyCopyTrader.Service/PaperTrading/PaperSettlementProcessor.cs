using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperSettlementProcessor(
    ILogger<PaperSettlementProcessor> logger,
    LiveTradingOptions liveTradingOptions,
    IPolymarketGammaClient gammaClient,
    IExposureSnapshotCache exposureCache,
    IAppRepository repository) : IPaperSettlementProcessor
{
    private static readonly TimeSpan StrategyPauseLookback = TimeSpan.FromHours(12);

    public async Task<PaperSettlementProcessingResult> ProcessOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        var positions = (await repository.GetPaperPositionsAsync(cancellationToken))
            .Where(position => position.SizeShares > 0m)
            .ToArray();
        if (positions.Length == 0)
        {
            var refreshed = await repository.RefreshPaperCopiedTraderPerformanceAsync(cancellationToken);
            return new PaperSettlementProcessingResult(0, 0, 0, refreshed);
        }

        var checkedPositions = 0;
        var settledPositions = 0;
        var insertedSettlements = 0;
        var processedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var position in positions)
        {
            if (!processedConditions.Add(position.ConditionId))
            {
                continue;
            }

            checkedPositions += positions.Count(item =>
                string.Equals(item.ConditionId, position.ConditionId, StringComparison.OrdinalIgnoreCase));

            try
            {
                var metadata = await GetResolvedMetadataAsync(position, cancellationToken);
                if (metadata.Count == 0)
                {
                    continue;
                }

                var winningOutcome = metadata.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.WinningOutcome))?.WinningOutcome;
                if (string.IsNullOrWhiteSpace(winningOutcome))
                {
                    continue;
                }

                var winningAssetId = metadata.FirstOrDefault(item =>
                    string.Equals(item.Outcome, winningOutcome, StringComparison.OrdinalIgnoreCase))?.TokenId;
                var category = metadata.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Category))?.Category;
                var result = await SettleMarketResolutionAsync(
                    position.ConditionId,
                    null,
                    winningAssetId,
                    winningOutcome,
                    category,
                    "GammaClosedMarket",
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                settledPositions += result.PositionsSettled;
                insertedSettlements += result.SettlementsInserted;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Paper settlement lookup failed for condition {ConditionId} asset {AssetId}.", position.ConditionId, position.AssetId);
                await TryRecordApiErrorAsync("ProcessOpenPosition", ex.Message, cancellationToken);
            }
        }

        var performanceRows = await repository.RefreshPaperCopiedTraderPerformanceAsync(cancellationToken);
        return new PaperSettlementProcessingResult(checkedPositions, settledPositions, insertedSettlements, performanceRows);
    }

    public async Task<PaperSettlementProcessingResult> SettleMarketResolutionAsync(
        string? conditionId,
        string? assetId,
        string? winningAssetId,
        string? winningOutcome,
        string? category,
        string settlementSource,
        DateTimeOffset settledAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(winningAssetId) && string.IsNullOrWhiteSpace(winningOutcome))
        {
            return new PaperSettlementProcessingResult(0, 0, 0, 0);
        }

        var positions = (await repository.GetPaperPositionsAsync(cancellationToken))
            .Where(position => position.SizeShares > 0m)
            .Where(position =>
                (!string.IsNullOrWhiteSpace(conditionId) &&
                    string.Equals(position.ConditionId, conditionId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(assetId) &&
                    string.Equals(position.AssetId, assetId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (positions.Length == 0)
        {
            return new PaperSettlementProcessingResult(0, 0, 0, 0);
        }

        var inserted = 0;
        foreach (var position in positions)
        {
            var won = IsWinningPosition(position, winningAssetId, winningOutcome);
            var costBasis = position.AveragePrice * position.SizeShares;
            var settlementValue = won ? position.SizeShares : 0m;
            var now = DateTimeOffset.UtcNow;
            var settlement = new PaperPositionSettlement(
                Guid.NewGuid(),
                position.CopiedTraderWallet,
                position.AssetId,
                position.ConditionId,
                position.Outcome,
                winningAssetId,
                winningOutcome ?? string.Empty,
                category,
                position.SizeShares,
                position.AveragePrice,
                costBasis,
                settlementValue,
                settlementValue - costBasis,
                won,
                settlementSource,
                settledAtUtc,
                now);

            if (await repository.TryAddPaperPositionSettlementAsync(settlement, cancellationToken))
            {
                inserted++;
                await UpdateStrategyAutoLivePauseAsync(position.CopiedTraderWallet, now, cancellationToken);
            }

            var settledPosition = position with
            {
                SizeShares = 0m,
                AveragePrice = 0m,
                EstimatedValueUsd = 0m,
                UnrealizedPnlUsd = 0m,
                UpdatedAtUtc = now
            };
            await repository.UpsertPaperPositionAsync(settledPosition, cancellationToken);
            exposureCache.ApplyPaperPosition(settledPosition);
        }

        var performanceRows = await repository.RefreshPaperCopiedTraderPerformanceAsync(cancellationToken);
        return new PaperSettlementProcessingResult(positions.Length, positions.Length, inserted, performanceRows);
    }

    private async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetResolvedMetadataAsync(
        PaperPosition position,
        CancellationToken cancellationToken)
    {
        var byToken = await gammaClient.GetTokenMetadataAsync(position.AssetId, closed: true, cancellationToken);
        var metadata = byToken.Count > 0
            ? byToken
            : await gammaClient.GetTokenMetadataByConditionIdAsync(position.ConditionId, position.AssetId, closed: true, cancellationToken);

        return metadata
            .Where(item => item.Resolved && !string.IsNullOrWhiteSpace(item.WinningOutcome))
            .ToArray();
    }

    private static bool IsWinningPosition(PaperPosition position, string? winningAssetId, string? winningOutcome)
    {
        return (!string.IsNullOrWhiteSpace(winningAssetId) &&
                string.Equals(position.AssetId, winningAssetId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(winningOutcome) &&
                string.Equals(position.Outcome, winningOutcome, StringComparison.OrdinalIgnoreCase));
    }

    private async Task UpdateStrategyAutoLivePauseAsync(
        string copiedTraderWallet,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var strategyId = ResolveStrategyId(copiedTraderWallet);
        if (strategyId is null)
        {
            return;
        }

        if (!StrategyAutoLivePausePolicy.IsEnabledForStrategy(liveTradingOptions, strategyId.Value))
        {
            logger.LogInformation(
                "Paper settlement auto live pause update skipped because it is not enabled for this strategy. StrategyId={StrategyId}",
                StrategyIds.Normalize(strategyId.Value));
            return;
        }

        try
        {
            var decision = await repository.UpdateStrategyAutoLivePauseFromRecentPnlAsync(
                strategyId.Value,
                nowUtc.Subtract(StrategyPauseLookback),
                nowUtc,
                StrategyAutoLivePauseUpdateMode.ResumeFromPaperSettlements,
                cancellationToken);
            if (!decision.AutoLivePauseChanged)
            {
                logger.LogInformation(
                    "Paper settlement left strategy auto live pause unchanged. StrategyId={StrategyId} RecentPnlUsd={RecentPnlUsd} RecentSettledCount={RecentSettledCount} AutoLivePaused={AutoLivePaused}",
                    StrategyIds.Normalize(strategyId.Value),
                    decision.RecentPnlUsd,
                    decision.RecentSettledCount,
                    decision.AutoLivePaused);
                return;
            }

            if (decision.AutoLivePaused)
            {
                logger.LogWarning(
                    "Strategy auto live pause enabled after recent PnL turned negative. StrategyId={StrategyId} RecentPnlUsd={RecentPnlUsd} RecentSettledCount={RecentSettledCount}",
                    StrategyIds.Normalize(strategyId.Value),
                    decision.RecentPnlUsd,
                    decision.RecentSettledCount);
            }
            else
            {
                logger.LogInformation(
                    "Strategy auto live pause cleared after recent PnL turned positive. StrategyId={StrategyId} RecentPnlUsd={RecentPnlUsd} RecentSettledCount={RecentSettledCount}",
                    StrategyIds.Normalize(strategyId.Value),
                    decision.RecentPnlUsd,
                    decision.RecentSettledCount);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update strategy auto live pause after paper settlement. CopiedTraderWallet={CopiedTraderWallet}", copiedTraderWallet);
            await TryRecordApiErrorAsync("UpdateStrategyAutoLivePause", ex.Message, cancellationToken);
        }
    }

    private static Guid? ResolveStrategyId(string copiedTraderWallet)
    {
        const string strategyPrefix = "strategy:";
        if (copiedTraderWallet.StartsWith(strategyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return StrategyIds.TryGetStrategyIdByCode(copiedTraderWallet[strategyPrefix.Length..]);
        }

        return StrategyIds.FollowLeader;
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "PaperSettlementProcessor", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist paper settlement API error for {Operation}.", operation);
        }
    }
}
