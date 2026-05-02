using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainSignalCandidateProcessor(
    OnChainIngestionOptions onChainOptions,
    SignalOptions signalOptions,
    IAppRepository repository) : IOnChainSignalCandidateProcessor
{
    private const string AcceptedStatus = "Accepted";
    private const string RejectedStatus = "Rejected";
    private const string ReadyDecisionCode = "onchain_candidate_ready";

    public async Task<OnChainSignalCandidateRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!onChainOptions.Enabled || !onChainOptions.BackgroundSignalCandidateRefreshEnabled)
        {
            return new OnChainSignalCandidateRefreshResult(0, 0, 0, 0, 0, 0, 0);
        }

        var queueResult = await repository.RefreshPolymarketOnChainSignalCandidateQueueAsync(
            onChainOptions.SignalCandidateQueueSeedBatchSize,
            onChainOptions.SignalCandidateRetryBatchSize,
            cancellationToken);

        var sources = await repository.GetPolymarketOnChainSignalCandidateSourcesAsync(
            onChainOptions.SignalCandidateBatchSize,
            cancellationToken);
        if (sources.Count == 0)
        {
            return new OnChainSignalCandidateRefreshResult(
                queueResult.SourcesQueued,
                queueResult.RetriesQueued,
                0,
                0,
                0,
                0,
                queueResult.QueueRemaining);
        }

        var decisions = sources.Select(Evaluate).ToArray();
        await repository.UpsertPolymarketOnChainSignalCandidateDecisionsAsync(decisions, cancellationToken);

        return new OnChainSignalCandidateRefreshResult(
            queueResult.SourcesQueued,
            queueResult.RetriesQueued,
            sources.Count,
            decisions.Length,
            decisions.Count(decision => decision.Candidate.DecisionStatus == AcceptedStatus),
            decisions.Count(decision => decision.Candidate.DecisionStatus == RejectedStatus),
            Math.Max(0, queueResult.QueueRemaining - decisions.Length));
    }

    private PolymarketOnChainSignalCandidateDecision Evaluate(PolymarketOnChainSignalCandidateSource source)
    {
        var now = DateTimeOffset.UtcNow;
        var metadata = source.TokenMetadata;
        var performance = source.WalletCategoryPerformance;
        var category = NormalizeCategory(metadata?.Category);
        var reasons = new List<PolymarketOnChainSignalCandidateReason>();

        AddDataAndTradeReasons(source, metadata, category, reasons, now);
        AddPerformanceReasons(source, metadata, performance, category, reasons, now);

        var accepted = reasons.Count == 0;
        var candidate = new PolymarketOnChainSignalCandidate(
            Guid.NewGuid(),
            source.SourceFillId,
            source.ContractName,
            source.ContractAddress,
            source.ExchangeVersion,
            source.BlockNumber,
            source.BlockTimestampUtc,
            source.TransactionHash,
            source.LogIndex,
            source.OrderHash,
            source.ParticipantRole,
            NormalizeWallet(source.Wallet),
            NormalizeWallet(source.Counterparty),
            source.Side,
            source.TokenId,
            metadata?.ConditionId ?? string.Empty,
            metadata?.MarketId ?? string.Empty,
            metadata?.MarketSlug ?? string.Empty,
            metadata?.MarketTitle ?? string.Empty,
            metadata?.Outcome ?? string.Empty,
            category,
            metadata?.LookupSucceeded ?? false,
            metadata?.Active ?? false,
            metadata?.Closed ?? false,
            metadata?.Archived ?? false,
            metadata?.Resolved ?? false,
            metadata?.WinningOutcome,
            source.Price,
            source.SizeShares,
            source.NotionalUsd,
            source.FeeAmount,
            source.FeeAssetId,
            performance?.PositionsCount,
            performance?.ResolvedPositions,
            performance?.MarketsTraded,
            performance?.VolumeUsd,
            performance?.ResolvedPnlUsd,
            performance?.ResolvedRoiPct,
            performance?.WinRatePct,
            performance?.Score,
            performance?.SampleQuality,
            performance?.RefreshedAtUtc,
            accepted ? AcceptedStatus : RejectedStatus,
            accepted ? ReadyDecisionCode : reasons[0].ReasonCode,
            performance?.Score ?? 0m,
            now,
            now);

        return new PolymarketOnChainSignalCandidateDecision(candidate, reasons);
    }

    private void AddDataAndTradeReasons(
        PolymarketOnChainSignalCandidateSource source,
        PolymarketOnChainTokenMetadata? metadata,
        string? category,
        List<PolymarketOnChainSignalCandidateReason> reasons,
        DateTimeOffset now)
    {
        if (source.Side != TradeSide.Buy)
        {
            AddReason(reasons, SignalReasonCodes.UnsupportedSide, "Only BUY fills are eligible for copy candidates.", now);
        }

        if (metadata is null || !metadata.LookupSucceeded)
        {
            AddReason(reasons, SignalReasonCodes.MissingMarketMetadata, "Token metadata is missing or lookup failed.", now);
            return;
        }

        if (IsUnknownCategory(category))
        {
            AddReason(reasons, SignalReasonCodes.MissingMarketCategory, "Market category is missing or unknown.", now);
        }

        if (!metadata.Active || metadata.Closed || metadata.Archived)
        {
            AddReason(reasons, SignalReasonCodes.MarketInactive, "Market is inactive, closed, or archived.", now);
        }

        if (metadata.Resolved)
        {
            AddReason(reasons, SignalReasonCodes.MarketResolved, "Market is already resolved.", now);
        }
    }

    private void AddPerformanceReasons(
        PolymarketOnChainSignalCandidateSource source,
        PolymarketOnChainTokenMetadata? metadata,
        PolymarketOnChainWalletCategoryPerformance? performance,
        string? category,
        List<PolymarketOnChainSignalCandidateReason> reasons,
        DateTimeOffset now)
    {
        if (metadata is null || !metadata.LookupSucceeded || IsUnknownCategory(category))
        {
            return;
        }

        if (performance is null ||
            !string.Equals(performance.Wallet, source.Wallet, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(performance.Category, category, StringComparison.OrdinalIgnoreCase))
        {
            AddReason(reasons, SignalReasonCodes.MissingLeaderCategoryPerformance, "No wallet/category performance row exists yet.", now);
            return;
        }

        if (now - performance.RefreshedAtUtc > TimeSpan.FromHours(signalOptions.LeaderCategoryPerformanceStaleAfterHours))
        {
            AddReason(reasons, SignalReasonCodes.LeaderCategoryPerformanceStale, "Wallet/category performance is stale.", now);
        }

        if (performance.ResolvedPositions < signalOptions.MinLeaderCategoryResolvedPositions)
        {
            AddReason(
                reasons,
                SignalReasonCodes.LeaderCategoryResolvedSampleTooSmall,
                $"Resolved sample {performance.ResolvedPositions} is below configured minimum {signalOptions.MinLeaderCategoryResolvedPositions}.",
                now);
        }

        if (SampleQualityRank(performance.SampleQuality) < SampleQualityRank(signalOptions.MinLeaderCategorySampleQuality))
        {
            AddReason(
                reasons,
                SignalReasonCodes.LeaderCategorySampleQualityTooLow,
                $"Sample quality {performance.SampleQuality} is below configured minimum {signalOptions.MinLeaderCategorySampleQuality}.",
                now);
        }

        if (performance.Score < signalOptions.MinLeaderCategoryScore)
        {
            AddReason(
                reasons,
                SignalReasonCodes.LeaderCategoryScoreTooLow,
                $"Category score {performance.Score:0.########} is below configured minimum {signalOptions.MinLeaderCategoryScore:0.########}.",
                now);
        }

        if (performance.ResolvedRoiPct < signalOptions.MinLeaderCategoryResolvedRoiPct)
        {
            AddReason(
                reasons,
                SignalReasonCodes.LeaderCategoryRoiTooLow,
                $"Resolved ROI {performance.ResolvedRoiPct:0.########}% is below configured minimum {signalOptions.MinLeaderCategoryResolvedRoiPct:0.########}%.",
                now);
        }

        if (performance.WinRatePct < signalOptions.MinLeaderCategoryWinRatePct)
        {
            AddReason(
                reasons,
                SignalReasonCodes.LeaderCategoryWinRateTooLow,
                $"Win rate {performance.WinRatePct:0.########}% is below configured minimum {signalOptions.MinLeaderCategoryWinRatePct:0.########}%.",
                now);
        }
    }

    private static void AddReason(
        List<PolymarketOnChainSignalCandidateReason> reasons,
        string reasonCode,
        string reasonDetails,
        DateTimeOffset createdAtUtc)
    {
        reasons.Add(new PolymarketOnChainSignalCandidateReason(
            Guid.NewGuid(),
            Guid.Empty,
            reasonCode,
            reasonDetails,
            createdAtUtc));
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

    private static int SampleQualityRank(string? sampleQuality)
    {
        if (string.Equals(sampleQuality, "High", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (string.Equals(sampleQuality, "Medium", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(sampleQuality, "Low", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }
}
