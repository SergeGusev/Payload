using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReferenceAverageHistoryCorrectionApply.Tests;

public sealed class ModeledAddPayloadTests
{
    [Theory]
    [InlineData("Regular", "0.52")]
    [InlineData("LowEnter", "0.50")]
    public void ExactModeledPayloadUsesWorstPriceSizingButActualFillPrice(
        string kind,
        string priceText)
    {
        var add = BuildAdd(kind, decimal.Parse(priceText, System.Globalization.CultureInfo.InvariantCulture));

        ModeledAddPayloadValidator.Validate(add);

        using var raw = JsonDocument.Parse(add.ModeledRawDecisionJson);
        Assert.Equal(0.99m, raw.RootElement.GetProperty("paper_fak_worst_price").GetDecimal());
        Assert.Equal(add.WorstPriceTargetSizeShares,
            raw.RootElement.GetProperty("target_size_shares").GetDecimal());
        Assert.Equal(add.FilledSizeShares,
            raw.RootElement.GetProperty("paper_fak_filled_size_shares").GetDecimal());
        Assert.NotEqual(add.WorstPriceTargetSizeShares, add.FilledSizeShares);
    }

    [Fact]
    public void ModeledPayloadRejectsHashSizingAndTimestampTamper()
    {
        var add = BuildAdd("Regular", 0.52m);

        Assert.Throws<InvalidDataException>(() => ModeledAddPayloadValidator.Validate(
            add with { ModeledRawDecisionJson = add.ModeledRawDecisionJson + " " }));
        Assert.Throws<InvalidDataException>(() => ModeledAddPayloadValidator.Validate(
            add with { WorstPriceTargetSizeShares = add.WorstPriceTargetSizeShares + 0.01m }));
        Assert.Throws<InvalidDataException>(() => ModeledAddPayloadValidator.Validate(
            add with { ModeledSettledAtUtc = add.ModeledSettledAtUtc.AddSeconds(1) }));
    }

    [Fact]
    public void ModeledPayloadRejectsEveryWorstPriceSizingInputAndReplayEvidenceTamper()
    {
        var add = BuildAdd("Regular", 0.52m);

        Assert.Contains("worst-price formula", Assert.Throws<InvalidDataException>(() =>
            ModeledAddPayloadValidator.Validate(add with
            {
                GammaOrderMinSize = add.GammaOrderMinSize + 0.01m
            })).Message, StringComparison.Ordinal);
        Assert.Contains("worst-price formula", Assert.Throws<InvalidDataException>(() =>
            ModeledAddPayloadValidator.Validate(add with
            {
                RawWorstPriceNotionalUsd = add.RawWorstPriceNotionalUsd + 0.01m
            })).Message, StringComparison.Ordinal);
        Assert.Contains("worst-price formula", Assert.Throws<InvalidDataException>(() =>
            ModeledAddPayloadValidator.Validate(add with
            {
                RoundedWorstPriceNotionalUsd = add.RoundedWorstPriceNotionalUsd + 1m
            })).Message, StringComparison.Ordinal);

        var raw = JsonNode.Parse(add.ModeledRawDecisionJson)?.AsObject() ??
                  throw new InvalidDataException("Test payload parse failed.");
        raw["replay_evidence_sha256"] = new string('c', 64);
        var tamperedRaw = raw.ToJsonString();
        var replayException = Assert.Throws<InvalidDataException>(() =>
            ModeledAddPayloadValidator.Validate(add with
            {
                ModeledRawDecisionJson = tamperedRaw,
                ModeledRawDecisionSha256 = Hash(tamperedRaw)
            }));
        Assert.Contains("replay_evidence_json SHA-256", replayException.Message, StringComparison.Ordinal);
    }

    private static AddCandidate BuildAdd(string kind, decimal price)
    {
        var runId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var strategyId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var entry = DateTimeOffset.Parse("2026-07-01T12:00:00Z");
        var settled = DateTimeOffset.Parse("2026-07-01T12:05:03Z");
        const string strategyCode = "eth-updown-5m-reference-average-2bps";
        const string marketId = "market-1";
        const string conditionId = "condition-1";
        const string token = "token-up";
        const string outcome = "Up";
        const decimal gammaOrderMinSize = 5m;
        const decimal rawWorstPriceNotional = 5.445m;
        const decimal roundedWorstPriceNotional = 6m;
        const decimal worstSize = 6.07m;
        const decimal requested = 6.0093m;
        var filledSize = decimal.Round(requested / price, 8, MidpointRounding.AwayFromZero);
        var sourceHash = new string('a', 64);
        var evidenceHash = new string('b', 64);
        var replayEvidenceJson = JsonSerializer.Serialize(new { decision = outcome });
        var replayEvidenceHash = Hash(replayEvidenceJson);
        var rawDecision = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            pricing_mode = "history_correction_modeled_fak",
            order_execution_mode = "FAK",
            post_only = false,
            decision_source = "reference_average_history_correction_v2_modeled_add",
            algorithm_decision_source = "reference_price_average_envelope_bps_premarket_v2",
            correction_provenance = "pinned_replay_classifier_plus_user_authorized_liquidity_assumption",
            historical_orderbook_snapshot_asserted = false,
            liquidity_assumption = price == 0.50m
                ? "sufficient_depth_for_full_fill_at_0.50"
                : "sufficient_depth_for_full_fill_at_0.52",
            source_skipped_run_id = runId,
            source_run_full_row_sha256 = sourceHash,
            signal_preview_manifest_sha256 = ModeledAddPayloadValidator.SignalPreviewManifestSha256,
            replay_classifier_sha256 = ModeledAddPayloadValidator.ReplayClassifierSha256,
            replay_evidence_json = replayEvidenceJson,
            replay_evidence_sha256 = replayEvidenceHash,
            strategy_code = strategyCode,
            market_id = marketId,
            condition_id = conditionId,
            asset_id = token,
            outcome,
            assumed_fill_price = price,
            paper_fak_worst_price = 0.99m,
            sizing_rounding = "ceil_usd_then_ceil_worst_price_shares_to_2_decimals",
            target_notional_usd = requested,
            target_size_shares = worstSize,
            paper_fak_average_fill_price = price,
            paper_fak_filled_size_shares = filledSize,
            paper_fak_filled_notional_usd = requested,
            paper_fak_partial_fill = false,
            modeled_entry_at_utc = entry,
            modeled_settled_at_utc = settled,
            modeled_settlement_timestamp_source =
                "modeled_earliest_observable_resolution=max(market_end_utc,resolution_ledger_first_received_at_utc)"
        });
        var rawHash = Hash(rawDecision);
        var mutationPayload = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            operation = "reference_average_history_correction_v2_modeled_add",
            deterministic_ids = new
            {
                algorithm = "UUIDv5/RFC4122-SHA1",
                namespace_id = ModeledAddPayloadValidator.IdNamespace,
                name_format = ModeledAddPayloadValidator.IdNameFormat,
                graph_manifest_sha256_is_bound_after_manifest_creation = true,
                entity_kinds = new[]
                {
                    "signal", "paper_order", "paper_fill", "paper_position",
                    "paper_position_settlement"
                }
            },
            source = new
            {
                run_id = runId,
                run_full_row_sha256 = sourceHash,
                entry_timestamp_source = "exact_source_skipped_run_updated_at_utc",
                modeled_settlement_timestamp_source =
                    "modeled_earliest_observable_resolution=max(market_end_utc,resolution_ledger_first_received_at_utc)",
                historical_orderbook_snapshot_asserted = false
            },
            signal = new
            {
                id_kind = "signal",
                leader_trade_id = (object?)null,
                trader_wallet = "strategy:" + strategyCode,
                condition_id = conditionId,
                asset_id = token,
                outcome,
                leader_price = price,
                best_bid = (object?)null,
                best_ask = (object?)null,
                spread_abs = (object?)null,
                spread_pct = (object?)null,
                lag_seconds = (object?)null,
                score = 100,
                decision = strategyCode + "_entry",
                accepted = true,
                proposed_paper_price = price,
                proposed_size_shares = filledSize,
                proposed_notional_usd = requested,
                created_at_utc = entry,
                raw_context_json = (object?)null
            },
            paper_order = new
            {
                id_kind = "paper_order",
                signal_id_kind = "signal",
                strategy_id = strategyId,
                copied_trader_wallet = "strategy:" + strategyCode,
                status = "Filled",
                side = "Buy",
                asset_id = token,
                condition_id = conditionId,
                outcome,
                price,
                size_shares = filledSize,
                notional_usd = requested,
                created_at_utc = entry,
                expires_at_utc = entry,
                filled_at_utc = entry,
                cancelled_at_utc = (object?)null,
                raw_decision_json = rawDecision,
                raw_decision_sha256 = rawHash,
                correlation_id = (object?)null,
                execution_source = ModeledAddPayloadValidator.MainFakExecutionSource
            },
            paper_fill = new
            {
                id_kind = "paper_fill",
                paper_order_id_kind = "paper_order",
                price,
                size_shares = filledSize,
                filled_at_utc = entry,
                evidence = ModeledAddPayloadValidator.FillEvidence,
                realized_pnl_usd = 0m
            },
            strategy_market_paper_run_update = new
            {
                id = runId,
                status = "Settled",
                selected_asset_id = token,
                selected_outcome = outcome,
                entry_price = price,
                stake_usd = requested,
                size_shares = filledSize,
                signal_id_kind = "signal",
                paper_order_id_kind = "paper_order",
                entered_at_utc = entry,
                settlement_price = 1m,
                settlement_value_usd = filledSize,
                realized_pnl_usd = filledSize - requested,
                settled_at_utc = settled,
                skip_reason = (object?)null,
                skip_diagnostics_json = (object?)null,
                updated_at_utc = settled,
                preserve_unlisted_source_columns = true
            },
            paper_position = new
            {
                id_kind = "paper_position",
                copied_trader_wallet = "strategy:" + strategyCode,
                asset_id = token,
                condition_id = conditionId,
                outcome,
                size_shares = 0m,
                average_price = 0m,
                estimated_value_usd = 0m,
                unrealized_pnl_usd = 0m,
                updated_at_utc = settled
            },
            paper_position_settlement = new
            {
                id_kind = "paper_position_settlement",
                copied_trader_wallet = "strategy:" + strategyCode,
                asset_id = token,
                condition_id = conditionId,
                outcome,
                winning_asset_id = token,
                winning_outcome = outcome,
                category = "Crypto",
                settled_size_shares = filledSize,
                average_price = price,
                cost_basis_usd = requested,
                settlement_value_usd = filledSize,
                realized_pnl_usd = filledSize - requested,
                won = true,
                settlement_source = ModeledAddPayloadValidator.SettlementSource,
                settled_at_utc = settled,
                created_at_utc = settled
            }
        });

        return new AddCandidate(
            runId, strategyId, strategyCode, marketId, conditionId, "ETH", kind,
            sourceHash, sourceHash, entry, settled,
            "modeled_earliest_observable_resolution=max(market_end_utc,resolution_ledger_first_received_at_utc)",
            "Crypto", rawDecision, rawHash, ModeledAddPayloadValidator.FillEvidence,
            mutationPayload, Hash(mutationPayload), price, 1m, gammaOrderMinSize,
            rawWorstPriceNotional, roundedWorstPriceNotional, outcome, token, outcome, token,
            "resolved-market-ledger", "ledger", "resolved", evidenceHash, 100,
            settled, settled, true, "binance-ticks", "ticks", 2, true,
            "Gamma", "gamma", "https://gamma-api.polymarket.com/markets/market-1",
            evidenceHash, 100, settled, 2, worstSize, requested, filledSize, true,
            1m, filledSize, filledSize - requested, true, "modeled add");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
