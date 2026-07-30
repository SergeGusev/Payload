using System.Text;
using System.Text.Json;
using ReferenceAverageHistoryCorrectionGraphPreview;

namespace ReferenceAverageHistoryCorrectionGraphPreview.Tests;

public sealed class ReplayEvidenceTests
{
    [Fact]
    public void ExactComposedGraphQueriesHaveRequiredWhitespaceBoundary()
    {
        var main = GraphDatabaseReader.BuildMainOrdersSql();
        var child = GraphDatabaseReader.BuildChildOrdersSql();

        Assert.Contains("signal.id = strategy_run.signal_id\nWHERE strategy_run.id", main, StringComparison.Ordinal);
        Assert.Contains(
            "signal.id = strategy_run.signal_id\nWHERE strategy_run.entry_due_at_utc",
            child,
            StringComparison.Ordinal);
        Assert.DoesNotContain("signal_idWHERE", main, StringComparison.Ordinal);
        Assert.DoesNotContain("signal_idWHERE", child, StringComparison.Ordinal);
        Assert.Equal(64, CanonicalEvidence.HashSql(main).Length);
        Assert.Equal(64, CanonicalEvidence.HashSql(child).Length);
    }

    [Fact]
    public void AddSizingUsesHistoricalMultiplierWorstPriceAndRegularFill()
    {
        var result = Calculate(winner: "Up", fillPrice: 0.52m, ledgerSource: "MarketWebSocket",
            archivedWinner: "Down", ledgerWinningToken: "up-token");

        Assert.True(result.CanAdd);
        Assert.True(result.Won);
        Assert.Equal(5.445m, result.RawWorstPriceNotionalUsd);
        Assert.Equal(6m, result.RoundedWorstPriceNotionalUsd);
        Assert.Equal(6.07m, result.WorstPriceTargetSizeShares);
        Assert.Equal(6.0093m, result.RequestedNotionalUsd);
        Assert.Equal(11.55634615m, result.FilledSizeShares);
        Assert.Equal(5.54704615m, result.RealizedPnlUsd);
        Assert.Equal(2, result.AgreeingIndependentResolutionSourceCount);
        Assert.False(result.ArchivedTickAgreesWithAuthoritativeWinner);
    }

    [Fact]
    public void AddSizingUsesLowEnterFillAndLossSettlement()
    {
        var result = Calculate(winner: "Down", fillPrice: 0.50m, ledgerSource: "BinanceTimedClose",
            archivedWinner: "Down", ledgerWinningToken: "down-token");

        Assert.False(result.Won);
        Assert.Equal(12.0186m, result.FilledSizeShares);
        Assert.Equal(0m, result.SettlementValueUsd);
        Assert.Equal(-6.0093m, result.RealizedPnlUsd);
        Assert.Equal(2, result.AgreeingIndependentResolutionSourceCount);
    }

    [Fact]
    public void LedgerWinningAssetConflictIsReportedButDoesNotChangeOfficialWinner()
    {
        var result = Calculate(winner: "Up", fillPrice: 0.52m, ledgerSource: "MarketWebSocket",
            archivedWinner: "Up", ledgerWinningToken: "down-token");

        Assert.True(result.CanAdd);
        Assert.True(result.Won);
        Assert.False(result.ResolutionLedgerWinningAssetAgreesWithGamma);
        Assert.Equal("down-token", result.ResolutionLedgerWinningAssetId);
        Assert.Equal("up-token", result.ResolvedWinningTokenId);
    }

    [Fact]
    public void AddFailsWhenOfficialGammaAndLedgerOutcomeDisagree()
    {
        var input = AddInput(0.52m);
        var exception = Assert.Throws<InvalidDataException>(() => AddFeasibilityCalculator.Calculate(
            input,
            AddRun(),
            Gamma(),
            LiveGamma("Up"),
            UnavailableRawDiagnostics("Up"),
            Archived("Up"),
            Ledger("Down", "down-token", "MarketWebSocket")));

        Assert.Contains("ledger_identity_timing_or_outcome_invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyRawWebSocketDiagnosticsAreExplicitlyUnavailableAndNotIndependent()
    {
        var summary = AddFeasibilityCalculator.ValidateRawMarketResolvedDiagnostics(
            AddInput(0.52m), AddRun(), Gamma(), LiveGamma("Up"), []);

        Assert.Equal(0, summary.DiagnosticRowCount);
        Assert.Equal(0, summary.DistinctRawEventCount);
        Assert.Empty(summary.WinningOutcome);
        Assert.Empty(summary.WinningTokenId);
        Assert.Empty(summary.ProvenanceGroup);
        Assert.Contains("unavailable", summary.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RawWebSocketDiagnosticRequiresExactRawIdentityAndTokenBijection()
    {
        var run = AddRun();
        var eventTime = run.MarketEndUtc!.Value;
        var raw = JsonSerializer.Serialize(new
        {
            event_type = "market_resolved",
            id = "market",
            market = "condition",
            assets_ids = new[] { "up-token", "down-token" },
            winning_asset_id = "up-token",
            winning_outcome = "Up",
            timestamp = eventTime.ToUnixTimeMilliseconds().ToString()
        });
        var diagnostic = new MarketResolvedEventEvidence(
            Guid.NewGuid(), "PolymarketMarketWebSocket:critical", "market_resolved", "up-token",
            "condition", "up-token", "Up", eventTime, eventTime.AddSeconds(1), true,
            "market", "condition", "market-slug", "ETH", eventTime.AddMinutes(-5), true,
            "RecordedCryptoUpDown5mResult", raw, "ABC", Encoding.UTF8.GetByteCount(raw),
            eventTime.AddSeconds(2));

        var summary = AddFeasibilityCalculator.ValidateRawMarketResolvedDiagnostics(
            AddInput(0.52m), run, Gamma(), LiveGamma("Up"), [diagnostic]);

        Assert.Equal(1, summary.DiagnosticRowCount);
        Assert.Equal(1, summary.DistinctRawEventCount);
        Assert.Equal("PolymarketMarketDataWebSocket", summary.ProvenanceGroup);
    }

    [Fact]
    public void ArchivedTicksRejectStaleClose()
    {
        var marketEnd = DateTimeOffset.Parse("2026-07-01T00:05:00Z");
        var samples = new[]
        {
            Tick(marketEnd.AddMinutes(-5), 100m, 100m, marketEnd),
            Tick(marketEnd.AddSeconds(-16), 101m, 100m, marketEnd)
        };

        var exception = Assert.Throws<InvalidDataException>(() =>
            AddFeasibilityCalculator.ReplayArchivedReferenceTicks(
                "ETH", "market", "condition", marketEnd, samples));
        Assert.Contains("stale", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildRequiresExactPricingModeAndAllParentEvidence()
    {
        var parent = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var child = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Child", null);
        child = child with { RawDecisionJson = ChildJson(parent, child, "child_parent_mirror") };

        var result = Match(child, parent);

        Assert.Equal(ChildLinkDisposition.Exact, result.Disposition);
        Assert.Equal(parent.RunId, result.ParentRunId);
    }

    [Fact]
    public void ExactCorrectionFakRowAndSingleFillAreAccepted()
    {
        var order = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var fill = FakFill(order);

        Assert.True(CorrectionGraphInvariantValidator.ValidateRow(order).Valid);
        Assert.True(CorrectionGraphInvariantValidator.ValidateFillSet(order, [fill]).Valid);
    }

    [Fact]
    public void CorrectionRowRejectsNonFakNonFilledCorrelationAndTimestampDrift()
    {
        var order = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var mutations = new[]
        {
            order with { OrderExecutionModeProof = "GTD" },
            order with { ExecutionSource = "btc_updown5m_gtd_limit" },
            order with { OrderStatus = "PartiallyFilled" },
            order with { CorrelationId = Guid.NewGuid() },
            order with { OrderExpiresAtUtc = order.OrderExpiresAtUtc.AddTicks(1) }
        };

        Assert.All(mutations, item => Assert.False(CorrectionGraphInvariantValidator.ValidateRow(item).Valid));
    }

    [Fact]
    public void CorrectionRowRejectsAnyNonNullSignalContextShape()
    {
        var order = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null) with
        {
            SignalBestBidProof = 0.51m,
            SignalNullableShapeValidProof = false
        };

        var validation = CorrectionGraphInvariantValidator.ValidateRow(order);

        Assert.False(validation.Valid);
        Assert.Contains("signal_shape", validation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CorrectionFakRequiresExactlyOneFill()
    {
        var order = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var fill = FakFill(order);

        Assert.False(CorrectionGraphInvariantValidator.ValidateFillSet(order, []).Valid);
        Assert.False(CorrectionGraphInvariantValidator.ValidateFillSet(order, [fill, fill with { FillId = Guid.NewGuid() }]).Valid);
    }

    [Fact]
    public void ChildFakParityRejectsMoneyAndSettlementDrift()
    {
        var parent = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var child = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Child", parent.RunId);
        var parentFill = FakFill(parent);
        var childFill = FakFill(child);

        Assert.True(CorrectionGraphInvariantValidator.ValidateChildParentFillParity(
            child, parent, [childFill], [parentFill]).Valid);
        Assert.False(CorrectionGraphInvariantValidator.ValidateChildParentFillParity(
            child with { StakeUsd = child.StakeUsd + 0.01m }, parent, [childFill], [parentFill]).Valid);
        Assert.False(CorrectionGraphInvariantValidator.ValidateChildParentFillParity(
            child with { SettlementPrice = 0m }, parent, [childFill], [parentFill]).Valid);
    }

    [Fact]
    public void GuidParameterBatchingPreservesAllIdsBeyondTwoBatches()
    {
        var first = Enumerable.Range(0, GraphDatabaseReader.BatchSize * 2 + 1)
            .Select(index => GuidFromIndex(index))
            .ToArray();
        var second = Enumerable.Range(0, GraphDatabaseReader.BatchSize + 17)
            .Select(index => GuidFromIndex(index + 1_000_000))
            .ToArray();

        var batches = GraphDatabaseReader.BatchGuidParameterSets(first, second).ToArray();

        Assert.Equal(3, batches.Length);
        Assert.All(batches, batch => Assert.All(batch, values => Assert.True(values.Length <= GraphDatabaseReader.BatchSize)));
        Assert.Equal(first, batches.SelectMany(batch => batch[0]));
        Assert.Equal(second, batches.SelectMany(batch => batch[1]));
    }

    [Fact]
    public void CsvOutputRowsHaveExactHeaderCardinality()
    {
        var graph = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var add = Calculate("Up", 0.52m, "MarketWebSocket", "Up", "up-token");
        var main = new MainRemovalSummary(
            graph.RunId, graph.StrategyId, graph.StrategyCode, graph.MarketId, graph.OrderId,
            graph.SignalId, graph.AssetId, graph.OrderOutcome, graph.CopiedTraderWallet, 1,
            graph.OrderSizeShares, graph.OrderNotionalUsd, 0m, graph.RunRealizedPnlUsd!.Value,
            graph.SettledAtUtc!.Value, CorrectionContract.CorrectedSkipReason, graph.OrderCreatedAtUtc,
            1m, 1m, graph.OrderNotionalUsd, "test", new string('A', 64), "Remove", "test",
            CorrectionContract.RequiredInputManifestSha256,
            CorrectionContract.RequiredInputReplayClassifierSha256, "{}", new string('A', 64),
            CanonicalEvidence.HashGraphOrder(graph), CanonicalEvidence.HashFillSet([FakFill(graph)]));
        var footprint = new OperationFootprintRow(
            "scope", "table", "DELETE", "selector", 1, 1, 128, 1, true, "evidence");

        Assert.Equal(OutputRows.MainRemovalHeader.Count, OutputRows.MainRemoval(main).Count);
        Assert.Equal(OutputRows.GraphOrderHeader.Count, OutputRows.GraphOrder(graph).Count);
        Assert.Equal(OutputRows.AddHeader.Count, OutputRows.Add(add).Count);
        Assert.Equal(
            OutputRows.ReconciliationTargetHeader.Count,
            OutputRows.ReconciliationTargetRow(ReconciliationContract.Targets[0]).Count);
        Assert.Equal(
            OutputRows.OperationFootprintHeader.Count,
            OutputRows.OperationFootprintCsvRow(footprint).Count);
    }

    [Fact]
    public void ReconciliationContractIsExactVersionedAllowlistAndKeepsAllBlockers()
    {
        var targets = ReconciliationContract.Targets;

        Assert.Equal(14, targets.Count);
        Assert.Equal(10, targets.Count(item => item.BlocksMutation));
        Assert.Equal(targets.Count, targets.Select(item => item.TargetId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(targets.Count, targets.Select(item => item.MethodId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(targets, item => Assert.True(CanonicalEvidence.IsSha256(item.TargetContractSha256)));
        Assert.Equal(
            "4ACCCDFBBE34B1C3AEB1B3CAF7B982FB280B55EBE56E4CF00755EE56B169A7D8",
            ReconciliationContract.ContractSha256);
    }

    [Fact]
    public void AddCollisionSqlIncludesAllTimeWalletAssetCollisionGate()
    {
        Assert.Contains(
            "paper_order.copied_trader_wallet = target.wallet",
            GraphDatabaseReader.AddCollisionSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "paper_order.asset_id = target.token_id",
            GraphDatabaseReader.AddCollisionSql,
            StringComparison.Ordinal);
        Assert.Contains("paper_orders_wallet_asset", GraphDatabaseReader.AddCollisionSql, StringComparison.Ordinal);
    }

    [Fact]
    public void LowerEnterClonePayloadUsesFiftyCentPremiseAndWorstPriceTargetSize()
    {
        var input = AddInput(0.50m) with { Kind = "LowerEnter clone" };
        var result = AddFeasibilityCalculator.Calculate(
            input, AddRun(), Gamma(), LiveGamma("Up"), UnavailableRawDiagnostics("Up"),
            Archived("Up"), Ledger("Up", "up-token", "MarketWebSocket"));

        using var raw = JsonDocument.Parse(result.ModeledRawDecisionJson);
        Assert.Equal("sufficient_depth_for_full_fill_at_0.50",
            raw.RootElement.GetProperty("liquidity_assumption").GetString());
        Assert.Equal(result.WorstPriceTargetSizeShares,
            raw.RootElement.GetProperty("target_size_shares").GetDecimal());
        Assert.NotEqual(result.FilledSizeShares,
            raw.RootElement.GetProperty("target_size_shares").GetDecimal());
        Assert.Equal(64, result.ModeledMutationPayloadSha256.Length);
    }

    [Fact]
    public void LinkedChildWithWrongPricingModeIsInvariantError()
    {
        var parent = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var child = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Child", null);
        child = child with { RawDecisionJson = ChildJson(parent, child, "legacy_mode") };

        Assert.Equal(ChildLinkDisposition.InvariantError, Match(child, parent).Disposition);
    }

    [Fact]
    public void OrphanRawChildOrderIsRejectedByIndependentRunLinkValidation()
    {
        var validation = ChildLinkMatcher.ValidateOrderRunLink(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            CorrectionContract.RequiredCutoffUtc,
            []);

        Assert.False(validation.Valid);
        Assert.Equal(0, validation.CandidateRunCount);
        Assert.Contains("missing_partial_or_ambiguous", validation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void PostCutoffRawChildOrderIsStillRejectedWhenItsOnlyRunIsPostCutoff()
    {
        var orderId = Guid.NewGuid();
        var signalId = Guid.NewGuid();
        var strategyId = Guid.NewGuid();
        var postCutoffOrderCreatedAtUtc = CorrectionContract.RequiredCutoffUtc.AddMinutes(1);
        Assert.True(postCutoffOrderCreatedAtUtc > CorrectionContract.RequiredCutoffUtc);

        var validation = ChildLinkMatcher.ValidateOrderRunLink(
            orderId,
            signalId,
            strategyId,
            CorrectionContract.RequiredCutoffUtc,
            [new ChildRunLinkEvidence(
                Guid.NewGuid(), strategyId, signalId, orderId,
                CorrectionContract.RequiredCutoffUtc.AddSeconds(1))]);

        Assert.False(validation.Valid);
        Assert.Equal(1, validation.CandidateRunCount);
        Assert.Equal(0, validation.ExactPreCutoffRunCount);
    }

    [Fact]
    public void CatalogHashGateRejectsSameShapeCatalogWithDifferentBytes()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            ChildCatalogParser.RequirePinnedSha256(
                new string('A', 64),
                CorrectionContract.RequiredInputCatalogSourceSha256));

        Assert.Contains("does not match pinned", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedWebSocketEventsAllowStickyScalarAndLatestRawTimestampsToDiffer()
    {
        var marketEnd = AddRun().MarketEndUtc!.Value;
        var ledger = Ledger("Up", "down-token", "MarketWebSocket") with
        {
            EventTimestampUtc = marketEnd.AddSeconds(1),
            FirstReceivedAtUtc = marketEnd.AddSeconds(2),
            LastReceivedAtUtc = marketEnd.AddSeconds(4),
            EventCount = 2,
            ResultDelaySeconds = 2m,
            UpdatedAtUtc = marketEnd.AddSeconds(4)
        };

        var result = AddFeasibilityCalculator.Calculate(
            AddInput(0.52m), AddRun(), Gamma(), LiveGamma("Up"),
            UnavailableRawDiagnostics("Up"), Archived("Up"), ledger);

        Assert.True(result.CanAdd);
        Assert.True(result.ResolutionLedgerRawValidated);
        Assert.False(result.ResolutionLedgerWinningAssetAgreesWithGamma);
        Assert.NotEqual(result.ResolutionLedgerEventTimestampUtc, marketEnd.AddSeconds(3));
        Assert.Equal(marketEnd.AddSeconds(3), result.ResolutionLedgerRawEventTimestampUtc);
    }

    [Fact]
    public void MarketWebSocketLedgerMissingExactRawEvidenceBlocksAdd()
    {
        var malformed = WithLedgerRaw(Ledger("Up", "up-token", "MarketWebSocket"), "{}");

        var exception = Assert.Throws<InvalidDataException>(() =>
            AddFeasibilityCalculator.Calculate(
                AddInput(0.52m), AddRun(), Gamma(), LiveGamma("Up"),
                UnavailableRawDiagnostics("Up"), Archived("Up"), malformed));

        Assert.Contains("raw_identity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BinanceTimedCloseLedgerMustAgreeWithExactArchivedReplay()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            AddFeasibilityCalculator.Calculate(
                AddInput(0.50m), AddRun(), Gamma(), LiveGamma("Up"),
                UnavailableRawDiagnostics("Up"), Archived("Down"),
                Ledger("Up", "up-token", "BinanceTimedClose")));

        Assert.Contains("exact_archived_tick_replay", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExclusivePositionSettlementMustMatchGraphArithmetic()
    {
        var order = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var fill = new GraphFill(
            "Main", null, order.RunId, order.OrderId, Guid.NewGuid(), 0.52m,
            order.OrderSizeShares, order.OrderCreatedAtUtc.AddSeconds(1), 0m, "test", new string('A', 64));
        var position = new PositionRow(
            Guid.NewGuid(), order.CopiedTraderWallet, order.AssetId, order.ConditionId,
            order.OrderOutcome, 0m, 0m, 0m, 0m, order.SettledAtUtc!.Value, new string('A', 64));
        var settlement = new PositionSettlementRow(
            Guid.NewGuid(), order.CopiedTraderWallet, order.AssetId, order.ConditionId,
            order.OrderOutcome, order.AssetId, order.OrderOutcome, order.OrderSizeShares,
            order.OrderPrice, order.OrderNotionalUsd, order.SettlementValueUsd!.Value,
            order.RunRealizedPnlUsd!.Value, true, "BtcUpDown5mGammaClosedMarket", order.SettledAtUtc.Value,
            order.RunCategoryProof, order.SettledAtUtc.Value, new string('A', 64));

        var validation = PositionEvidenceValidator.ValidateExclusiveKey(
            [order], [fill], [position], [settlement]);

        Assert.True(validation.Valid, validation.Details);
    }

    [Fact]
    public void MalformedPositionSettlementIsRejected()
    {
        var order = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var fill = new GraphFill(
            "Main", null, order.RunId, order.OrderId, Guid.NewGuid(), 0.52m,
            order.OrderSizeShares, order.OrderCreatedAtUtc.AddSeconds(1), 0m, "test", new string('A', 64));
        var position = new PositionRow(
            Guid.NewGuid(), order.CopiedTraderWallet, order.AssetId, order.ConditionId,
            order.OrderOutcome, 0m, 0m, 0m, 0m, order.SettledAtUtc!.Value, new string('A', 64));
        var settlement = new PositionSettlementRow(
            Guid.NewGuid(), order.CopiedTraderWallet, order.AssetId, order.ConditionId,
            order.OrderOutcome, order.AssetId, order.OrderOutcome, order.OrderSizeShares,
            order.OrderPrice, order.OrderNotionalUsd + 0.01m, order.SettlementValueUsd!.Value,
            order.RunRealizedPnlUsd!.Value, true, "BtcUpDown5mGammaClosedMarket", order.SettledAtUtc.Value,
            order.RunCategoryProof, order.SettledAtUtc.Value, new string('A', 64));

        var validation = PositionEvidenceValidator.ValidateExclusiveKey(
            [order], [fill], [position], [settlement]);

        Assert.False(validation.Valid);
        Assert.Contains("arithmetic_mismatch", validation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LosingPositionRequiresExactBinaryOppositeWinningOutcome()
    {
        var order = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null) with
        {
            SettlementPrice = 0m,
            SettlementValueUsd = 0m,
            RunRealizedPnlUsd = -6.0093m
        };
        var fill = new GraphFill(
            "Main", null, order.RunId, order.OrderId, Guid.NewGuid(), 0.52m,
            order.OrderSizeShares, order.OrderCreatedAtUtc.AddSeconds(1), 0m, "test", new string('A', 64));
        var position = new PositionRow(
            Guid.NewGuid(), order.CopiedTraderWallet, order.AssetId, order.ConditionId,
            order.OrderOutcome, 0m, 0m, 0m, 0m, order.SettledAtUtc!.Value, new string('A', 64));
        var malformed = new PositionSettlementRow(
            Guid.NewGuid(), order.CopiedTraderWallet, order.AssetId, order.ConditionId,
            order.OrderOutcome, null, "garbage", order.OrderSizeShares,
            order.OrderPrice, order.OrderNotionalUsd, 0m, -order.OrderNotionalUsd,
            false, "BtcUpDown5mGammaClosedMarket", order.SettledAtUtc.Value,
            order.RunCategoryProof, order.SettledAtUtc.Value, new string('A', 64));

        var validation = PositionEvidenceValidator.ValidateExclusiveKey(
            [order], [fill], [position], [malformed]);

        Assert.False(validation.Valid);
    }

    [Fact]
    public void RemovalStakeProofRestoresExactBaseAndValidatesHistoricalSizing()
    {
        var order = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var raw = JsonSerializer.Serialize(new
        {
            order_execution_mode = "FAK",
            paper_lost_counter_coeff = 0,
            paper_lost_base_stake_usd = 1m,
            paper_lost_add_stake_usd = 0m,
            paper_lost_effective_stake_usd = 1m,
            stake_multiplier = 1m,
            stake_sizing_source = "ClobBook",
            target_notional_usd = order.OrderNotionalUsd,
            target_size_shares = order.OrderSizeShares,
            paper_fak_average_fill_price = order.OrderPrice,
            paper_fak_filled_size_shares = order.OrderSizeShares,
            paper_fak_filled_notional_usd = order.OrderNotionalUsd,
            paper_fak_partial_fill = false
        });

        var evidence = RemovalStakeEvidenceParser.Parse(order, raw);

        Assert.Equal(1m, evidence.BaseStakeUsd);
        Assert.Equal(1m, evidence.EffectiveStakeUsd);
        Assert.Equal(order.OrderNotionalUsd, evidence.TargetNotionalUsd);
        Assert.Equal(64, evidence.ProofSha256.Length);
    }

    [Fact]
    public void RemovalStakeProofRejectsMissingBaseStake()
    {
        var order = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var exception = Assert.Throws<InvalidDataException>(() =>
            RemovalStakeEvidenceParser.Parse(order, "{}"));

        Assert.Contains("paper_lost_base_stake_usd", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalMutationScopeHashesChangeWithState()
    {
        var order = Graph(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", null);
        var original = CanonicalEvidence.HashGraphOrder(order);
        var changed = CanonicalEvidence.HashGraphOrder(order with { StakeUsd = order.StakeUsd + 0.01m });

        Assert.Equal(64, original.Length);
        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void LiveGammaParserAcceptsEncodedArraysAndRequiresExactOneZeroResolution()
    {
        var bytes = Encoding.UTF8.GetBytes("""
            {"id":2954335,"conditionId":"condition","slug":"market-slug","closed":true,
             "outcomes":"[\"Up\",\"Down\"]","clobTokenIds":"[\"up-token\",\"down-token\"]",
             "outcomePrices":"[\"1\",\"0\"]","orderMinSize":"5","resolutionSource":"Chainlink ETH/USD"}
            """);

        var parsed = LiveGammaResolutionReader.Parse(
            "2954335", "https://gamma-api.polymarket.com/markets/2954335", bytes,
            DateTimeOffset.Parse("2026-07-27T13:30:00Z"));

        Assert.Equal("Up", parsed.WinningOutcome);
        Assert.Equal("up-token", parsed.WinningTokenId);
        Assert.Equal(5m, parsed.OrderMinSize);
        Assert.Equal(64, parsed.RawSha256.Length);
    }

    [Fact]
    public async Task InputReaderRejectsAnyUnpinnedManifestBeforeParsingRows()
    {
        var directory = NewTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), "{}", new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(directory, "remove.csv"), "x", new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(directory, "add.csv"), "x", new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(directory, "catalog.csv"), "x", new UTF8Encoding(false));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SignalPreviewInputReader.LoadAsync(
                directory, CorrectionContract.RequiredCutoffUtc, CancellationToken.None));
        Assert.Contains(CorrectionContract.RequiredInputManifestSha256, exception.Message, StringComparison.Ordinal);
    }

    private static AddFeasibility Calculate(
        string winner,
        decimal fillPrice,
        string ledgerSource,
        string archivedWinner,
        string ledgerWinningToken) =>
        AddFeasibilityCalculator.Calculate(
            AddInput(fillPrice),
            AddRun(),
            Gamma(),
            LiveGamma(winner),
            UnavailableRawDiagnostics(winner),
            Archived(archivedWinner),
            Ledger(winner, ledgerWinningToken, ledgerSource));

    private static SignalPreviewRow AddInput(decimal price) => new(
        "potential_add", "ETH", "OptimizedReferenceAverage", "Direct", "Base", "Down", 2,
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "strategy",
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), null, "market",
        DateTimeOffset.Parse("2026-07-01T00:00:00Z"), null, "", "", "Add", "selector_delta",
        price, "Skip", "Up", "{}", new string('A', 64));

    private static AddSourceRow AddRun() => new(
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "strategy", "market", "condition",
        "Skipped", CorrectionContract.PotentialAddSkipReason,
        DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        DateTimeOffset.Parse("2026-07-01T00:05:00Z"), 1m,
        null, null, null, null, null, null, null, null, null, null, null,
        "{\"decision_source\":\"reference_price_max_average_bps_premarket\",\"target_notional_usd\":1}",
        "market-slug", new string('A', 64), DateTimeOffset.Parse("2026-07-01T00:00:01Z"), "crypto");

    private static GammaMarketEvidence Gamma() => new(
        "market", "condition", 5m, "[\"Up\",\"Down\"]", "[\"up-token\",\"down-token\"]");

    private static LiveGammaResolutionEvidence LiveGamma(string winner)
    {
        var winnerToken = winner == "Up" ? "up-token" : "down-token";
        var prices = winner == "Up" ? "[1,0]" : "[0,1]";
        return new LiveGammaResolutionEvidence(
            "market", "condition", "market-slug", true, "[\"Up\",\"Down\"]",
            "[\"up-token\",\"down-token\"]", prices, winner, winnerToken, 5m,
            "Chainlink ETH/USD", "https://gamma-api.polymarket.com/markets/market", "ABC", 3,
            DateTimeOffset.Parse("2026-07-27T13:30:00Z"));
    }

    private static ValidatedMarketResolvedDiagnostics UnavailableRawDiagnostics(string winner) => new(
        "market", "condition", "", "", 0, 0,
        "market_resolved_event_diagnostics:unavailable_no_matching_rows", "");

    private static ArchivedReferenceResolution Archived(string winner) => new(
        "ETH", "market", "condition", 2, 100m, winner == "Up" ? 101m : 99m,
        DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        DateTimeOffset.Parse("2026-07-01T00:05:00Z"),
        DateTimeOffset.Parse("2026-07-01T00:05:00Z"), 0m, winner,
        "archived_odds_ticks:BinanceStartEnd", "BinanceArchivedReferenceTicks");

    private static ResolvedMarketLedgerEvidence Ledger(string winner, string token, string source)
    {
        var marketEnd = DateTimeOffset.Parse("2026-07-01T00:05:00Z");
        var officialToken = winner == "Up" ? "up-token" : "down-token";
        var raw = source == "MarketWebSocket"
            ? JsonSerializer.Serialize(new
            {
                event_type = "market_resolved",
                id = "market",
                market = "condition",
                assets_ids = new[] { "up-token", "down-token" },
                winning_asset_id = officialToken,
                winning_outcome = winner,
                timestamp = marketEnd.AddSeconds(3).ToUnixTimeMilliseconds().ToString()
            })
            : "{}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        return new ResolvedMarketLedgerEvidence(
            Guid.NewGuid(), "ETH", "market", "condition", "market-slug",
            marketEnd.AddMinutes(-5), marketEnd, winner, token,
            marketEnd.AddSeconds(1), marketEnd.AddSeconds(1), marketEnd.AddSeconds(4),
            1, 1m, source,
            source == "MarketWebSocket" ? "market_resolved" : "binance_timed_close_provisional",
            raw, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            bytes.LongLength, marketEnd, marketEnd.AddSeconds(4));
    }

    private static ResolvedMarketLedgerEvidence WithLedgerRaw(
        ResolvedMarketLedgerEvidence ledger,
        string raw)
    {
        var bytes = Encoding.UTF8.GetBytes(raw);
        return ledger with
        {
            RawJson = raw,
            RawSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            RawBytes = bytes.LongLength
        };
    }

    private static ReferenceResolutionTick Tick(
        DateTimeOffset sampledAt,
        decimal price,
        decimal start,
        DateTimeOffset marketEnd) => new(
        "ETH", "market", "condition", marketEnd, sampledAt, price, start, sampledAt, sampledAt);

    private static GraphOrder Graph(Guid run, Guid order, Guid signal, string scope, Guid? parent)
    {
        var strategyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var enteredAt = DateTimeOffset.Parse("2026-07-01T00:00:01Z");
        var settledAt = DateTimeOffset.Parse("2026-07-01T00:05:00Z");
        var hash = new string('A', 64);
        return new GraphOrder(
            Scope: scope,
            ParentMainRunId: parent,
            RunId: run,
            StrategyId: strategyId,
            StrategyCode: "strategy",
            MarketId: "market",
            ConditionId: "condition",
            EntryDueAtUtc: DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            RunStatus: "Settled",
            RunOutcome: "Up",
            RunAssetId: "token",
            EntryPrice: 0.52m,
            StakeUsd: 6.0093m,
            RunSizeShares: 11.55634615m,
            SettlementPrice: 1m,
            SettlementValueUsd: 11.55634615m,
            RunRealizedPnlUsd: 5.54704615m,
            SettledAtUtc: settledAt,
            OrderId: order,
            SignalId: signal,
            OrderStatus: "Filled",
            OrderSide: "Buy",
            OrderOutcome: "Up",
            AssetId: "token",
            CopiedTraderWallet: "strategy:strategy",
            OrderPrice: 0.52m,
            OrderSizeShares: 11.55634615m,
            OrderNotionalUsd: 6.0093m,
            CorrelationId: null,
            ExecutionSource: scope == "Child"
                ? CorrectionGraphInvariantValidator.ChildFakExecutionSource
                : CorrectionGraphInvariantValidator.MainFakExecutionSource,
            OrderCreatedAtUtc: enteredAt,
            RunSignalIdProof: signal,
            RunPaperOrderIdProof: order,
            OrderStrategyIdProof: strategyId,
            SignalRowIdProof: signal,
            SignalOutcomeProof: "Up",
            SignalAssetIdProof: "token",
            SignalConditionIdProof: "condition",
            SignalTraderWalletProof: "strategy:strategy",
            SignalLeaderPriceProof: 0.52m,
            SignalScoreProof: 100,
            SignalAcceptedProof: true,
            SignalDecisionProof: "strategy_entry",
            SignalProposedPaperPriceProof: 0.52m,
            SignalProposedSizeSharesProof: 11.55634615m,
            SignalProposedNotionalUsdProof: 6.0093m,
            SignalCreatedAtUtcProof: enteredAt,
            OrderExpiresAtUtc: enteredAt,
            OrderFilledAtUtc: enteredAt,
            OrderCancelledAtUtc: null,
            RunFullRowSha256: hash,
            OrderFullRowSha256: hash,
            SignalFullRowSha256: hash,
            StrategyNameProof: "strategy name",
            MarketSlugProof: "market-slug",
            RunCategoryProof: "crypto",
            RunEnteredAtUtcProof: enteredAt,
            RunCreatedAtUtcProof: enteredAt,
            RunUpdatedAtUtcProof: settledAt,
            MarketEndUtcProof: settledAt,
            RunSkipReasonProof: null,
            RunSkipDiagnosticsIsNullProof: true,
            SignalLeaderTradeIdProof: null,
            SignalBestBidProof: null,
            SignalBestAskProof: null,
            SignalSpreadAbsProof: null,
            SignalSpreadPctProof: null,
            SignalLagSecondsProof: null,
            SignalRawContextJsonProof: null,
            SignalNullableShapeValidProof: true,
            OrderExecutionModeProof: scope == "Main" ? "FAK" : string.Empty,
            RawDecisionProofSha256: hash,
            RawDecisionJson: null);
    }

    private static GraphFill FakFill(GraphOrder order) => new(
        order.Scope,
        order.ParentMainRunId,
        order.RunId,
        order.OrderId,
        Guid.NewGuid(),
        order.OrderPrice,
        order.OrderSizeShares,
        order.OrderCreatedAtUtc,
        0m,
        "test-fak-fill",
        new string('A', 64));

    private static Guid GuidFromIndex(int index)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(index).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private static string ChildJson(GraphOrder parent, GraphOrder child, string pricingMode) =>
        JsonSerializer.Serialize(new
        {
            pricing_mode = pricingMode,
            execution_source = child.ExecutionSource,
            copied_at_utc = child.OrderCreatedAtUtc,
            parent_strategy_id = parent.StrategyId,
            parent_strategy_code = parent.StrategyCode,
            parent_strategy_name = parent.StrategyNameProof,
            parent_run_id = parent.RunId,
            parent_signal_id = parent.SignalId,
            parent_paper_order_id = parent.OrderId,
            market_id = parent.MarketId,
            market_slug = parent.MarketSlugProof,
            child_strategy_id = child.StrategyId,
            child_strategy_code = child.StrategyCode,
            condition_id = child.ConditionId,
            outcome = child.OrderOutcome,
            asset_id = child.AssetId,
            order_price = child.OrderPrice,
            entry_price = child.EntryPrice,
            stake_usd = child.StakeUsd,
            size_shares = child.RunSizeShares
        });

    private static ChildLinkResult Match(GraphOrder child, GraphOrder parent) =>
        ChildLinkMatcher.Match(
            child,
            new Dictionary<Guid, GraphOrder> { [parent.RunId] = parent },
            new Dictionary<Guid, GraphOrder> { [parent.OrderId] = parent },
            new Dictionary<Guid, GraphOrder> { [parent.SignalId] = parent });

    private static string NewTempDirectory()
    {
        var temp = Environment.GetEnvironmentVariable("TEMP") ??
            throw new InvalidOperationException("TEMP missing");
        var directory = Path.Combine(temp, "graph-preview-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
