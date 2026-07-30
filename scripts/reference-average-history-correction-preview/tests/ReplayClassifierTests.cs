using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using ReferenceAverageHistoryCorrectionPreview;

namespace ReferenceAverageHistoryCorrectionPreviewTests;

public sealed class ReplayClassifierTests
{
    [Fact]
    public void ExistingConfiguredUpRetainsAtInclusivePositiveThreshold()
    {
        var strategy = Strategy(StrategyFamily.ReferenceAverage, StrategyTrigger.Up);
        var json = ReferenceJson(
            current: 100.01m,
            threshold: 1m,
            ("3h", 10_800, 100m));

        var result = ReplayClassifier.ClassifyExistingEntry(strategy, "Down", "Down", json);

        Assert.Equal(ReplayAction.Retain, result.Action);
        Assert.Equal(SignalOutcome.Down, result.LegacyV1Outcome);
        Assert.Equal(SignalOutcome.Down, result.CorrectedV2Outcome);
    }

    [Fact]
    public void ExistingConfiguredDownRetainsAtInclusiveNegativeThreshold()
    {
        var strategy = Strategy(StrategyFamily.ReferenceAverage, StrategyTrigger.Down);
        var json = ReferenceJson(
            current: 99.99m,
            threshold: 1m,
            ("3h", 10_800, 100m));

        var result = ReplayClassifier.ClassifyExistingEntry(strategy, "Up", "Up", json);

        Assert.Equal(ReplayAction.Retain, result.Action);
        Assert.Equal(SignalOutcome.Up, result.CorrectedV2Outcome);
    }

    [Fact]
    public void MinimumTiePrefersLongerSixHourOverThreeHour()
    {
        var strategy = Strategy(StrategyFamily.ReferenceAverage, StrategyTrigger.Down);
        var json = ReferenceJson(
            current: 90m,
            threshold: 1m,
            ("24h", 86_400, 110m),
            ("3h", 10_800, 100m),
            ("6h", 21_600, 100m));

        var result = ReplayClassifier.ClassifyExistingEntry(strategy, "Up", "Up", json);

        Assert.Equal(ReplayAction.Retain, result.Action);
        Assert.Equal("6h", result.MinimumAverageWindow);
    }

    [Fact]
    public void MinimumTiePrefersThreeHourOverNinetyMinutes()
    {
        var strategy = Strategy(StrategyFamily.ReferenceAverage, StrategyTrigger.Down);
        var json = ReferenceJson(
            current: 90m,
            threshold: 1m,
            ("24h", 86_400, 110m),
            ("90m", 5_400, 100m),
            ("3h", 10_800, 100m));

        var result = ReplayClassifier.ClassifyExistingEntry(strategy, "Up", "Up", json);

        Assert.Equal(ReplayAction.Retain, result.Action);
        Assert.Equal("3h", result.MinimumAverageWindow);
    }

    [Fact]
    public void BpsConfirmedReadsBaseSignalDecision()
    {
        var strategy = Strategy(StrategyFamily.BpsConfirmedAverage, StrategyTrigger.Composite);
        var referenceJson = ReferenceJson(
            current: 90m,
            threshold: 1m,
            ("24h", 86_400, 110m),
            ("3h", 10_800, 100m));
        var wrapper = Wrap("base_signal_decision", referenceJson);

        var result = ReplayClassifier.ClassifyExistingEntry(strategy, "Up", "Up", wrapper);

        Assert.Equal(ReplayAction.Retain, result.Action);
        Assert.Equal("3h", result.MinimumAverageWindow);
    }

    [Fact]
    public void DiffConfirmedReadsConfirmationSignalDecision()
    {
        var strategy = Strategy(StrategyFamily.DiffConfirmedAverage, StrategyTrigger.Composite);
        var referenceJson = ReferenceJson(
            current: 90m,
            threshold: 1m,
            ("24h", 86_400, 110m),
            ("3h", 10_800, 100m));
        var wrapper = Wrap("confirmation_signal_decision", referenceJson);

        var result = ReplayClassifier.ClassifyExistingEntry(strategy, "Up", "Up", wrapper);

        Assert.Equal(ReplayAction.Retain, result.Action);
        Assert.Equal("3h", result.MinimumAverageWindow);
    }

    [Fact]
    public void DiffConfirmedUsesPersistedHistoricalReferenceThreshold()
    {
        var strategy = Strategy(StrategyFamily.DiffConfirmedAverage, StrategyTrigger.Composite) with
        {
            ReferenceThresholdBps = 45
        };
        var referenceJson = ReferenceJson(
            current: 90m,
            threshold: 10m,
            ("24h", 86_400, 110m),
            ("3h", 10_800, 100m));
        var wrapper = Wrap("confirmation_signal_decision", referenceJson);

        var result = ReplayClassifier.ClassifyExistingEntry(strategy, "Up", "Up", wrapper);

        Assert.Equal(ReplayAction.Retain, result.Action);
        Assert.Equal(10m, result.ThresholdBps);
    }

    [Fact]
    public void OptimizedPotentialAddRequiresRecomputedV1MaxFailureAndV2ThreeHourMinimum()
    {
        var strategy = Strategy(
            StrategyFamily.OptimizedReferenceAverage,
            StrategyTrigger.Down,
            lowerEnter: true);
        var json = ReferenceJson(
            current: 89.99m,
            threshold: 1m,
            requiredWindow: "3h",
            ("24h", 86_400, 100m),
            ("3h", 10_800, 90m));

        var result = ReplayClassifier.ClassifyPotentialAdd(
            strategy,
            ReplayClassifier.PotentialAddSkipReason,
            json);

        Assert.Equal(ReplayAction.Add, result.Action);
        Assert.Equal(ReplayClassifier.LowerEnterAssumedFillPrice, result.AssumedFillPrice);
        Assert.Equal(1m, result.HistoricalStakeMultiplier);
        Assert.Equal(SignalOutcome.Skip, result.LegacyV1Outcome);
        Assert.Equal(SignalOutcome.Up, result.CorrectedV2Outcome);
    }

    [Fact]
    public void OptimizedRegularPotentialAddUsesPointFiftyTwoModeledFill()
    {
        var strategy = Strategy(StrategyFamily.OptimizedReferenceAverage, StrategyTrigger.Neutral);
        var json = ReferenceJson(
            current: 89.99m,
            threshold: 1m,
            requiredWindow: "3h",
            ("24h", 86_400, 100m),
            ("3h", 10_800, 90m));

        var result = ReplayClassifier.ClassifyPotentialAdd(
            strategy,
            ReplayClassifier.PotentialAddSkipReason,
            json);

        Assert.Equal(ReplayAction.Add, result.Action);
        Assert.Equal(ReplayClassifier.RegularAssumedFillPrice, result.AssumedFillPrice);
    }

    [Fact]
    public void PotentialAddRequiresPersistedHistoricalStakeMultiplier()
    {
        var strategy = Strategy(StrategyFamily.OptimizedReferenceAverage, StrategyTrigger.Down);
        var jsonNode = JsonNode.Parse(ReferenceJson(
            current: 89.99m,
            threshold: 1m,
            requiredWindow: "3h",
            ("24h", 86_400, 100m),
            ("3h", 10_800, 90m)))!.AsObject();
        jsonNode.Remove("target_notional_usd");

        var result = ReplayClassifier.ClassifyPotentialAdd(
            strategy,
            ReplayClassifier.PotentialAddSkipReason,
            jsonNode.ToJsonString());

        Assert.Equal(ReplayAction.Unreplayable, result.Action);
        Assert.Equal("historical_stake_multiplier_missing_or_invalid_for_add", result.Reason);
    }

    [Fact]
    public void ExistingOptimizedEntryRecoversThreeHourRequirementFromExactCodexBackfillMarker()
    {
        var strategy = Strategy(StrategyFamily.OptimizedReferenceAverage, StrategyTrigger.Neutral);
        var jsonNode = JsonNode.Parse(ReferenceJson(
            current: 89m,
            threshold: 1m,
            requiredWindow: null,
            ("24h", 86_400, 100m),
            ("3h", 10_800, 110m)))!.AsObject();
        jsonNode["codex_backfill"] = ReplayClassifier.OptimizedHistoryBackfillMarker;

        var result = ReplayClassifier.ClassifyExistingEntry(
            strategy,
            "Up",
            "Up",
            jsonNode.ToJsonString());

        Assert.Equal(ReplayAction.Remove, result.Action);
        Assert.Equal("3h", result.RequiredWindow);
    }

    [Fact]
    public void ExistingSolDownOptimizedEntryRecoversThreeHourRequirementFromExactHistoryBackfill()
    {
        var strategy = Strategy(StrategyFamily.OptimizedReferenceAverage, StrategyTrigger.Down) with
        {
            Asset = "SOL"
        };
        var jsonNode = JsonNode.Parse(ReferenceJson(
            current: 89m,
            threshold: 1m,
            requiredWindow: null,
            ("24h", 86_400, 100m),
            ("3h", 10_800, 110m)))!.AsObject();
        jsonNode["reference_asset_symbol"] = "SOL";
        jsonNode["history_backfill"] = new JsonObject
        {
            ["backfill_id"] = ReplayClassifier.SolDownOptimizedHistoryBackfillId
        };

        var result = ReplayClassifier.ClassifyExistingEntry(
            strategy,
            "Up",
            "Up",
            jsonNode.ToJsonString());

        Assert.Equal(ReplayAction.Remove, result.Action);
        Assert.Equal("3h", result.RequiredWindow);
    }

    [Fact]
    public void ExistingOptimizedEntryRejectsUnknownRequiredWindowBackfillMarker()
    {
        var strategy = Strategy(StrategyFamily.OptimizedReferenceAverage, StrategyTrigger.Neutral);
        var jsonNode = JsonNode.Parse(ReferenceJson(
            current: 89m,
            threshold: 1m,
            requiredWindow: null,
            ("24h", 86_400, 100m),
            ("3h", 10_800, 110m)))!.AsObject();
        jsonNode["codex_backfill"] = "unverified-marker";

        var result = ReplayClassifier.ClassifyExistingEntry(
            strategy,
            "Up",
            "Up",
            jsonNode.ToJsonString());

        Assert.Equal(ReplayAction.Unreplayable, result.Action);
        Assert.Equal("optimized_required_window_missing_or_invalid", result.Reason);
    }

    [Fact]
    public void OrdinaryReferenceFallbackCanProveStillSkipWithoutStakeMultiplier()
    {
        var strategy = Strategy(StrategyFamily.OptimizedReferenceAverage, StrategyTrigger.Down);
        var jsonNode = JsonNode.Parse(ReferenceJson(
            current: 95m,
            threshold: 1m,
            requiredWindow: null,
            ("24h", 86_400, 100m),
            ("3h", 10_800, 90m)))!.AsObject();
        jsonNode.Remove("target_notional_usd");

        var result = ReplayClassifier.ClassifyPotentialAddFromOrdinaryReferenceEvidence(
            strategy,
            ReplayClassifier.PotentialAddSkipReason,
            "Up",
            jsonNode.ToJsonString());

        Assert.Equal(ReplayAction.StillSkip, result.Action);
        Assert.Equal(SignalOutcome.Skip, result.LegacyV1Outcome);
        Assert.Equal(SignalOutcome.Skip, result.CorrectedV2Outcome);
    }

    [Fact]
    public void OrdinaryReferenceFallbackDoesNotInventStakeMultiplierForAdd()
    {
        var strategy = Strategy(StrategyFamily.OptimizedReferenceAverage, StrategyTrigger.Down);
        var jsonNode = JsonNode.Parse(ReferenceJson(
            current: 89.99m,
            threshold: 1m,
            requiredWindow: null,
            ("24h", 86_400, 100m),
            ("3h", 10_800, 90m)))!.AsObject();
        jsonNode.Remove("target_notional_usd");

        var result = ReplayClassifier.ClassifyPotentialAddFromOrdinaryReferenceEvidence(
            strategy,
            ReplayClassifier.PotentialAddSkipReason,
            "Up",
            jsonNode.ToJsonString());

        Assert.Equal(ReplayAction.Unreplayable, result.Action);
        Assert.Equal("historical_stake_multiplier_missing_or_invalid_for_add", result.Reason);
    }

    [Fact]
    public void OrdinaryReferenceFallbackRequiresSourceOutcomeToMatchRecomputedLegacyOutcome()
    {
        var strategy = Strategy(StrategyFamily.OptimizedReferenceAverage, StrategyTrigger.Down);
        var json = ReferenceJson(
            current: 95m,
            threshold: 1m,
            requiredWindow: null,
            ("24h", 86_400, 100m),
            ("3h", 10_800, 90m));

        var result = ReplayClassifier.ClassifyPotentialAddFromOrdinaryReferenceEvidence(
            strategy,
            ReplayClassifier.PotentialAddSkipReason,
            "Down",
            json);

        Assert.Equal(ReplayAction.InvariantError, result.Action);
        Assert.Equal(
            "ordinary_reference_evidence_outcome_does_not_match_recomputed_v1",
            result.Reason);
    }

    [Fact]
    public void MigrationCatalogParsesExactEightHundredFortyEightAllowlist()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(GetSourceDirectory(), "..", "..", ".."));
        var catalogPath = Path.Combine(
            repositoryRoot,
            "Codex",
            "Tasks",
            "REFERENCE_AVERAGE_MAX_MIN_MIGRATION_2026-07-27.md");
        Assert.True(File.Exists(catalogPath), $"Catalog not found at {catalogPath}");
        var strategies = CatalogParser.ParseAndValidate(catalogPath);
        Assert.Equal(CatalogParser.ExpectedStrategyCount, strategies.Count);
        Assert.Equal(
            CatalogParser.ExpectedPotentialAddStrategyCount,
            strategies.Count(item =>
                item.Family == StrategyFamily.OptimizedReferenceAverage &&
                item.Trigger is StrategyTrigger.Down or StrategyTrigger.Neutral));
        Assert.Equal(326, strategies.Count(item => item.UsesLowEnterPrice));
        Assert.Equal(
            96,
            strategies.Count(item =>
                item.UsesLowEnterPrice &&
                item.Family == StrategyFamily.OptimizedReferenceAverage &&
                item.Trigger is StrategyTrigger.Down or StrategyTrigger.Neutral));
        Assert.All(
            strategies.Where(item => item.Family == StrategyFamily.NativeLowEnterReferenceAverage),
            item => Assert.True(item.UsesLowEnterPrice));
        Assert.All(
            strategies.Where(item => item.Family == StrategyFamily.DiffConfirmedAverage && item.Asset == "BTC"),
            item => Assert.Equal(45, item.ReferenceThresholdBps));
        Assert.All(
            strategies.Where(item => item.Family == StrategyFamily.DiffConfirmedAverage && item.Asset == "ETH"),
            item => Assert.Equal(5, item.ReferenceThresholdBps));
        Assert.All(
            strategies.Where(item => item.Family == StrategyFamily.DiffConfirmedAverage && item.Asset == "SOL"),
            item => Assert.Equal(35, item.ReferenceThresholdBps));
    }

    private static string GetSourceDirectory([CallerFilePath] string sourcePath = "") =>
        Path.GetDirectoryName(sourcePath)!;

    [Fact]
    public void PotentialAddFailsInvariantWhenLegacyMaxThresholdDidNotPass()
    {
        var strategy = Strategy(StrategyFamily.OptimizedReferenceAverage, StrategyTrigger.Down);
        var json = ReferenceJson(
            current: 99.995m,
            threshold: 1m,
            requiredWindow: "3h",
            ("24h", 86_400, 100m),
            ("3h", 10_800, 90m));

        var result = ReplayClassifier.ClassifyPotentialAdd(
            strategy,
            ReplayClassifier.PotentialAddSkipReason,
            json);

        Assert.Equal(ReplayAction.InvariantError, result.Action);
        Assert.Equal("legacy_skip_reason_not_confirmed_by_recomputed_v1", result.Reason);
    }

    [Fact]
    public void OptimizedNeutralLegacyUpperBranchWindowSkipRemainsSkip()
    {
        var strategy = Strategy(StrategyFamily.OptimizedReferenceAverage, StrategyTrigger.Neutral);
        var json = ReferenceJson(
            current: 111m,
            threshold: 1m,
            requiredWindow: "3h",
            ("24h", 86_400, 110m),
            ("3h", 10_800, 100m));

        var result = ReplayClassifier.ClassifyPotentialAdd(
            strategy,
            ReplayClassifier.PotentialAddSkipReason,
            json);

        Assert.Equal(ReplayAction.StillSkip, result.Action);
        Assert.Equal("legacy_upper_branch_window_gate_unchanged", result.Reason);
    }

    [Fact]
    public void NeutralActualUpInsideV2EnvelopeIsRemoved()
    {
        var strategy = Strategy(StrategyFamily.ReferenceAverage, StrategyTrigger.Neutral, threshold: 4);
        var json = ReferenceJson(
            current: 95m,
            threshold: 4m,
            ("24h", 86_400, 100m),
            ("3h", 10_800, 90m));

        var result = ReplayClassifier.ClassifyExistingEntry(strategy, "Up", "Up", json);

        Assert.Equal(ReplayAction.Remove, result.Action);
        Assert.Equal(SignalOutcome.Up, result.LegacyV1Outcome);
        Assert.Equal(SignalOutcome.Skip, result.CorrectedV2Outcome);
    }

    [Fact]
    public void CorruptedOppositeActualOutcomeIsInvariantError()
    {
        var strategy = Strategy(StrategyFamily.ReferenceAverage, StrategyTrigger.Neutral);
        var json = ReferenceJson(
            current: 101m,
            threshold: 1m,
            ("24h", 86_400, 100m),
            ("3h", 10_800, 90m));

        var result = ReplayClassifier.ClassifyExistingEntry(strategy, "Up", "Up", json);

        Assert.Equal(ReplayAction.InvariantError, result.Action);
        Assert.Equal("legacy_v1_outcome_does_not_match_actual", result.Reason);
    }

    [Fact]
    public void PersistedReferenceThresholdMustMatchExactStrategyThreshold()
    {
        var strategy = Strategy(StrategyFamily.ReferenceAverage, StrategyTrigger.Down, threshold: 2);
        var json = ReferenceJson(
            current: 99m,
            threshold: 3m,
            ("3h", 10_800, 100m));

        var result = ReplayClassifier.ClassifyExistingEntry(strategy, "Up", "Up", json);

        Assert.Equal(ReplayAction.InvariantError, result.Action);
        Assert.Equal("reference_average_threshold_mismatch", result.Reason);
    }

    private static StrategyDefinition Strategy(
        StrategyFamily family,
        StrategyTrigger trigger,
        bool lowerEnter = false,
        int threshold = 1)
    {
        return new StrategyDefinition(
            Guid.Parse("11111111-2222-4333-8444-555555555555"),
            "test_strategy",
            "Test strategy",
            "ETH",
            family,
            family is StrategyFamily.BpsConfirmedAverage or StrategyFamily.DiffConfirmedAverage
                ? StrategyLocation.Indirect
                : StrategyLocation.Direct,
            trigger,
            threshold,
            threshold,
            lowerEnter ? "LowerEnter clone" : "Base",
            lowerEnter);
    }

    private static string ReferenceJson(
        decimal current,
        decimal threshold,
        params (string Window, int Seconds, decimal Price)[] averages) =>
        ReferenceJson(current, threshold, requiredWindow: null, averages);

    private static string ReferenceJson(
        decimal current,
        decimal threshold,
        string? requiredWindow,
        params (string Window, int Seconds, decimal Price)[] averages)
    {
        var maximum = averages
            .OrderByDescending(item => item.Price)
            .ThenByDescending(item => item.Seconds)
            .ThenBy(item => item.Window, StringComparer.OrdinalIgnoreCase)
            .First();
        return JsonSerializer.Serialize(new
        {
            decision_source = ReplayClassifier.LegacyDecisionSource,
            reference_asset_symbol = "ETH",
            current_price_usd = current,
            selected_reference_average_window = maximum.Window,
            reference_average_min_move_from_middle_bps = threshold,
            optimized_average_required_window = requiredWindow,
            target_notional_usd = 1m,
            reference_averages = averages.Select(item => new
            {
                window = item.Window,
                window_seconds = item.Seconds,
                is_full_window = true,
                average_price_usd = item.Price
            })
        });
    }

    private static string Wrap(string propertyName, string referenceJson)
    {
        var wrapper = new JsonObject
        {
            [propertyName] = JsonNode.Parse(referenceJson)
        };
        return wrapper.ToJsonString();
    }
}
