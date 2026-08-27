namespace PolyCopyTrader.Tests;

public sealed class HistoricalGrossNetParityLiveFlowTests
{
    [Fact]
    public void OrdinaryLiveSettlement_ExplicitlyFlowsReturnedRowVersionIntoBalanceCas()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "LiveTrading",
            "LiveTradingProcessor.cs");
        var start = source.IndexOf(
            "private async Task<int> SettleMatchedOrdersAsync",
            StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf(
            "private async Task TrySyncPaperShadowBeforeLiveSettlementAsync",
            start,
            StringComparison.Ordinal);

        Assert.True(end > start);
        var method = source[start..end];
        Assert.Contains(
            "settlementOrder = await repository.UpdateLiveOrderWithConcurrencyAsync(",
            method,
            StringComparison.Ordinal);
        Assert.Contains(
            "repository.ApplyLiveOrderSettlementToStrategyBalanceWithConcurrencyAsync(",
            method,
            StringComparison.Ordinal);
        Assert.Contains("settlementOrder.RowVersion", method, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "repository.ApplyLiveOrderSettlementToStrategyBalanceAsync(",
            method,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AsyncLocal", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DonorAggregation_PreservesRawCountsWhenExactMembershipIsEmpty()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.HistoricalGrossNetParityDonorStreaming.cs");

        Assert.Contains("long rawCount = 0;", source, StringComparison.Ordinal);
        Assert.Contains("long exactCount = 0;", source, StringComparison.Ordinal);
        Assert.Contains("long selectedCount = 0;", source, StringComparison.Ordinal);
        Assert.Contains("rawCount++;", source, StringComparison.Ordinal);
        Assert.Contains("exactCount++;", source, StringComparison.Ordinal);
        Assert.Contains("selectedCount++;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PaperRunCandidateEligibility_UsesLinkedFillOriginBeforeRunTimestamps()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.HistoricalGrossNetParity.cs");

        Assert.Contains(
            "ELSE linked_run_fill.originated_at < @CutoffUtc",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "LEAST(linked_run_fill.originated_at,",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MixedPaperDonor_RequiresFreshCandidateScopedReplayEvidenceRatherThanSourceText()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.HistoricalGrossNetParityDonorStreaming.cs");

        Assert.Contains(
            "ProveHistoricalGrossNetParityFreshPaperDonorAsync",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ReplayHistoricalGrossNetParityDonorStreamAsync",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ComputeHistoricalGrossNetParityPoolComponentHashAsync",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "HistoricalGrossNetDonorHashV1.CreateComponentEvidenceHashBuilder",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ReadHistoricalGrossNetParityDonorFillsAsync",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DonorSelection_RequiresFeeIdentityAndDeduplicatesOnlyProvedLinkedRepresentations()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.HistoricalGrossNetParityDonorStreaming.cs");
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(
            "raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill ||\n                     effectiveFee == raw.StoredFee",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "HasHistoricalGrossNetParityExactLinkedLiveAsync",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "HistoricalGrossNetParityFreshDonorProof(true, componentHash, overlap)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "processed.ExcludeAfterLineage",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsHistoricalGrossNetParityPreferredRepresentation",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHEN live_order.paper_order_id IS NOT NULL\n                THEN 'paper-order:'",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateDiscovery_UsesCompleteWalletAssetPoolAndRuntimeDonorPlanGuard()
    {
        var repositorySource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.HistoricalGrossNetParity.cs")
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var streamingSource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.HistoricalGrossNetParityDonorStreaming.cs")
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain(
            "paper_order.strategy_id = position.mapped_strategy_id",
            streamingSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "paper_order.strategy_id = settlement.mapped_strategy_id",
            streamingSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "buy_order.strategy_id = sell_order.strategy_id",
            streamingSource,
            StringComparison.Ordinal);
        Assert.Contains("EXPLAIN (FORMAT JSON)", repositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "HistoricalGrossNetParitySequentialDonorPlanException",
            repositorySource,
            StringComparison.Ordinal);
        Assert.Contains(
            "LoadHistoricalGrossNetParityDonorAggregateStreamingAsync(",
            repositorySource,
            StringComparison.Ordinal);
        Assert.True(
            streamingSource.Split(
                "EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync",
                StringSplitOptions.None).Length - 1 >= 4,
            "Every streaming donor statement must opt into the runtime EXPLAIN gate.");
        Assert.Contains("LIMIT @PageSize", streamingSource, StringComparison.Ordinal);
        Assert.Contains("IAsyncEnumerable<HistoricalGrossNetParityPaperFillObservation>", streamingSource,
            StringComparison.Ordinal);
        Assert.Contains("\"strategy_paper_skip_rollups\"", repositorySource, StringComparison.Ordinal);
        Assert.Contains("\"historical_gross_net_parity_audit\"", repositorySource, StringComparison.Ordinal);
        Assert.Contains(
            "HistoricalGrossNetParityStreamingFold",
            streamingSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DirectFixedFallback_DoesNotDispatchOrRevalidateDonors()
    {
        var processorSource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "PaperTrading",
            "PaperFakFeeBackfillProcessor.cs")
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var storageSource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.HistoricalGrossNetParity.cs")
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var fallbackStart = processorSource.IndexOf(
            "private async Task ProcessFallbackTargetAsync(",
            StringComparison.Ordinal);
        var fallbackEnd = processorSource.IndexOf(
            "private async Task<HistoricalGrossNetParityApplyResult> ApplyDecisionAsync(",
            fallbackStart,
            StringComparison.Ordinal);
        Assert.True(fallbackStart >= 0);
        Assert.True(fallbackEnd > fallbackStart);
        var fallback = processorSource[fallbackStart..fallbackEnd];

        Assert.Contains(
            "HistoricalGrossNetParityDecisionFactory.CreateFallback(",
            fallback,
            StringComparison.Ordinal);
        Assert.Contains("decision,\n                [],", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrderedCandidates", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadHistoricalGrossNetParityDonorPreviewAsync", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoricalGrossNetDonorMatcher", fallback, StringComparison.Ordinal);

        Assert.Contains("HistoricalGrossNetExactDecimal.Parse(\"0.0333\")", processorSource,
            StringComparison.Ordinal);
        Assert.Contains("HistoricalGrossNetParityDecisionKind.Fixed0p0333", processorSource,
            StringComparison.Ordinal);
        Assert.Contains("historical-gross-net-parity-fixed-net-roi-minus-3p33-v1", processorSource,
            StringComparison.Ordinal);
        Assert.Contains("donorPolicy = \"disabled\"", processorSource, StringComparison.Ordinal);

        Assert.Contains(
            "decision.DecisionKind == HistoricalGrossNetParityDecisionKind.Fixed0p0333 ||",
            storageSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "Donor decisions require selection proof; direct fixed decisions do not.",
            storageSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateDiscovery_BoundsEachRankedStrategyAndAppliesCursorBeforeInnerLimit()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.HistoricalGrossNetParity.cs")
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var candidateMethodStart = source.IndexOf(
            "private static async Task<IReadOnlyList<HistoricalGrossNetParityCandidateKey>>\n" +
            "        LoadHistoricalGrossNetParityCandidateKeysAsync(",
            StringComparison.Ordinal);
        var candidateMethodEnd = source.IndexOf(
            "private static Guid[] GetHistoricalGrossNetParityIds(",
            candidateMethodStart,
            StringComparison.Ordinal);

        Assert.True(candidateMethodStart >= 0);
        Assert.True(candidateMethodEnd > candidateMethodStart);
        var method = source[candidateMethodStart..candidateMethodEnd];
        var lateralStart = method.IndexOf("CROSS JOIN LATERAL (", StringComparison.Ordinal);
        var innerCursor = method.IndexOf("WHERE NOT @HasAfter", lateralStart, StringComparison.Ordinal);
        var innerLimit = method.IndexOf("LIMIT @PageSize", lateralStart, StringComparison.Ordinal);

        Assert.True(lateralStart >= 0);
        Assert.True(innerCursor > lateralStart);
        Assert.True(innerLimit > innerCursor);
        Assert.Contains("run.strategy_id = ranked.id", method, StringComparison.Ordinal);
        Assert.Contains("sell_order.strategy_id = ranked.id", method, StringComparison.Ordinal);
        Assert.Contains("live_order.strategy_id = ranked.id", method, StringComparison.Ordinal);
        Assert.DoesNotContain("raw_candidates AS", method, StringComparison.Ordinal);
        Assert.DoesNotContain("uses_runs AS", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PaperSellBinding_UsesFullCanonicalEventKeyAtEqualTimestamp()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.HistoricalGrossNetParity.cs")
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(
            "ROW(lower(selected_order.paper_order_id::text), lower(fill.id::text)) <=",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ROW(lower(paper_order.id::text), lower(fill.id::text)) <=",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ROW(lower(buy_order.id::text), lower(buy_fill.id::text)) <=",
            source,
            StringComparison.Ordinal);
    }

    private static string ReadRepositorySource(params string[] pathParts)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("POLYCOPYTRADER_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var configuredPath = Path.GetFullPath(Path.Combine(configuredRoot, Path.Combine(pathParts)));
            if (File.Exists(configuredPath))
            {
                return File.ReadAllText(configuredPath);
            }
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, Path.Combine(pathParts));
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Repository source file '{Path.Combine(pathParts)}' was not found from '{AppContext.BaseDirectory}'.");
    }
}
