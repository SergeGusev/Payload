using System.Text.Json;
using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class StorageTests
{
    [Fact]
    public void NormalizeLiveOrderRawResponseJson_WrapsPlainTextBody()
    {
        var normalized = PostgresAppRepository.NormalizeLiveOrderRawResponseJson("service not ready");

        using var document = JsonDocument.Parse(normalized);
        Assert.Equal("service not ready", document.RootElement.GetProperty("raw").GetString());
    }

    [Fact]
    public void NormalizeLiveOrderRawResponseJson_PreservesJsonBody()
    {
        Assert.Equal("""{"status":"matched"}""", PostgresAppRepository.NormalizeLiveOrderRawResponseJson(""" {"status":"matched"} """));
        Assert.Equal("{}", PostgresAppRepository.NormalizeLiveOrderRawResponseJson(""));
    }

    [Fact]
    public void GetPersistedSkipDiagnosticsJson_LossDiffPlacementSkipDiagnosticsPreserveExactParent()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 26, 15, 0, 0, TimeSpan.Zero);
        var observed = new StrategyMarketPaperRun(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "market-1",
            "condition-1",
            "btc-updown-5m-1",
            "BTC Up or Down",
            "Crypto",
            nowUtc,
            nowUtc.AddMinutes(5),
            nowUtc,
            nowUtc,
            StrategyMarketPaperRunStatuses.Observed,
            null,
            null,
            null,
            1m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            nowUtc,
            nowUtc,
            """{"book":"large diagnostic payload"}""");

        Assert.Equal(
            observed.SkipDiagnosticsJson,
            PostgresAppRepository.GetPersistedSkipDiagnosticsJson(observed));
        var skipped = observed with { Status = StrategyMarketPaperRunStatuses.Skipped };
        Assert.Null(PostgresAppRepository.GetPersistedSkipDiagnosticsJson(skipped));

        var makerGtdPlacementReasons = new[]
        {
            "maker_gtd_variant_not_paper_only",
            "maker_gtd_market_start_unknown",
            "maker_gtd_market_end_unknown",
            "maker_gtd_effective_expiration_elapsed",
            "maker_gtd_maximum_order_price_invalid",
            "maker_gtd_premarket_entry_window_elapsed",
            "maker_gtd_post_only_attempts_exhausted"
        };
        foreach (var reason in makerGtdPlacementReasons)
        {
            var diagnostics = JsonSerializer.Serialize(new
            {
                execution_source = "eth_reference_average_maker_gtd_paper",
                skip_reason = reason,
                maker_gtd = new
                {
                    execution_source = "eth_reference_average_maker_gtd_paper",
                    terminal_outcome = "skipped",
                    terminal_reason = reason,
                    attempts = new[] { new { attempt_number = 1 } }
                }
            });
            var makerSkipped = skipped with
            {
                SkipReason = reason,
                SkipDiagnosticsJson = diagnostics
            };

            Assert.Equal(
                diagnostics,
                PostgresAppRepository.GetPersistedSkipDiagnosticsJson(makerSkipped));
        }

        Assert.Null(PostgresAppRepository.GetPersistedSkipDiagnosticsJson(skipped with
        {
            SkipReason = "maker_gtd_post_only_attempts_exhausted",
            SkipDiagnosticsJson = """{"execution_source":"another_source"}"""
        }));
        Assert.Null(PostgresAppRepository.GetPersistedSkipDiagnosticsJson(skipped with
        {
            SkipReason = "unrelated_skip",
            SkipDiagnosticsJson = """{"execution_source":"eth_reference_average_maker_gtd_paper"}"""
        }));

        foreach (var lossDiffCase in new[]
        {
            new
            {
                Child = StrategyIds.EthLossDiff4Plus,
                Parent = StrategyIds.EthDiffConfirmedAveragePremarketParent,
                Threshold = 4
            },
            new
            {
                Child = StrategyIds.EthLossDiff13PlusPositive,
                Parent = StrategyIds.EthDiffConfirmedAveragePremarketParent,
                Threshold = 13
            },
            new
            {
                Child = StrategyIds.EthUp8BpsLossDiff3Plus,
                Parent = StrategyIds.EthUp8BpsReferenceAveragePremarketParent,
                Threshold = 3
            },
            new
            {
                Child = StrategyIds.EthUp8BpsLossDiff16PlusPositive,
                Parent = StrategyIds.EthUp8BpsReferenceAveragePremarketParent,
                Threshold = 16
            }
        })
        {
            var lossDiffDiagnostics = JsonSerializer.Serialize(new
            {
                pricing_mode = "child_parent_mirror",
                child_strategy_id = lossDiffCase.Child,
                parent_strategy_id = lossDiffCase.Parent,
                parent_run_id = Guid.NewGuid(),
                loss_diff = new
                {
                    pre_entry_value = lossDiffCase.Threshold - 1,
                    threshold = lossDiffCase.Threshold,
                    gate_passed = false
                }
            });
            var lossDiffSkipped = skipped with
            {
                StrategyId = lossDiffCase.Child,
                SkipReason = "parent_lossdiff_below_threshold",
                SkipDiagnosticsJson = lossDiffDiagnostics
            };
            Assert.Equal(
                lossDiffDiagnostics,
                PostgresAppRepository.GetPersistedSkipDiagnosticsJson(lossDiffSkipped));
            Assert.Null(PostgresAppRepository.GetPersistedSkipDiagnosticsJson(lossDiffSkipped with
            {
                SkipDiagnosticsJson = lossDiffDiagnostics.Replace(
                    "\"gate_passed\":false",
                    "\"gate_passed\":true",
                    StringComparison.Ordinal)
            }));
            var wrongParentDiagnostics = lossDiffDiagnostics.Replace(
                lossDiffCase.Parent.ToString(),
                Guid.NewGuid().ToString(),
                StringComparison.OrdinalIgnoreCase);
            Assert.Null(PostgresAppRepository.GetPersistedSkipDiagnosticsJson(lossDiffSkipped with
            {
                SkipDiagnosticsJson = wrongParentDiagnostics
            }));
        }
    }

    [Fact]
    public void PostgresSchema_FeeAccountingColumnsAreBackwardCompatibleAndDoNotBackfillHistory()
    {
        var schema = PostgresSchema.SchemaSql.Replace("\r\n", "\n", StringComparison.Ordinal);
        var feeTables = new[]
        {
            "paper_fills",
            "strategy_market_paper_runs",
            "paper_positions",
            "paper_position_settlements",
            "live_orders"
        };

        foreach (var table in feeTables)
        {
            Assert.Contains($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS fee_usd numeric(28,8) NOT NULL DEFAULT 0;", schema, StringComparison.Ordinal);
            Assert.Contains($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS fee_accounting_status text NOT NULL DEFAULT 'LegacyUnknown';", schema, StringComparison.Ordinal);
            Assert.Contains($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS fee_liquidity_role text NOT NULL DEFAULT 'Unknown';", schema, StringComparison.Ordinal);
            Assert.Contains($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS fee_calculation_source text NOT NULL DEFAULT '';", schema, StringComparison.Ordinal);
            Assert.Contains($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS fee_rate numeric(28,8) NULL;", schema, StringComparison.Ordinal);
            Assert.Contains($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS fee_exponent integer NULL;", schema, StringComparison.Ordinal);
            Assert.Contains($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS fee_taker_only boolean NULL;", schema, StringComparison.Ordinal);
            Assert.Contains($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS fee_calculated_at_utc timestamptz NULL;", schema, StringComparison.Ordinal);
        }

        foreach (var table in new[] { "paper_fills", "strategy_market_paper_runs", "paper_position_settlements", "live_orders" })
        {
            Assert.Contains($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS net_realized_pnl_usd numeric(28,8) NULL;", schema, StringComparison.Ordinal);
        }

        Assert.Contains("ALTER TABLE paper_positions ADD COLUMN IF NOT EXISTS net_unrealized_pnl_usd numeric(28,8) NULL;", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("SET fee_accounting_status = 'Calculated'", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("SET net_realized_pnl_usd =", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("SET net_unrealized_pnl_usd =", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_FeeAccountingFieldsFlowThroughSingleBatchReadAndReconciliationMappings()
    {
        var source = ReadStorageRepositorySource().Replace("\r\n", "\n", StringComparison.Ordinal);
        var directCompactionSource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.DirectSkipCompaction.cs").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate", source, StringComparison.Ordinal);
        Assert.Contains("fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd", source, StringComparison.Ordinal);
        Assert.Contains("fee_exponent, fee_taker_only, fee_calculated_at_utc, net_unrealized_pnl_usd", source, StringComparison.Ordinal);

        Assert.Contains("fee_accounting_status = fill.FeeAccountingStatus", source, StringComparison.Ordinal);
        Assert.Contains("fee_accounting_status = position.FeeAccountingStatus", source, StringComparison.Ordinal);
        Assert.Contains("fee_accounting_status = settlement.FeeAccountingStatus", source, StringComparison.Ordinal);
        Assert.Contains("fee_accounting_status = run.FeeAccountingStatus", source, StringComparison.Ordinal);
        Assert.Contains("fee_accounting_status = @FeeAccountingStatus", source, StringComparison.Ordinal);

        Assert.Contains("AddWithValue(\"FeeAccountingStatus\", fill.FeeAccountingStatus)", source, StringComparison.Ordinal);
        Assert.Contains("AddWithValue(\"FeeAccountingStatus\", position.FeeAccountingStatus)", source, StringComparison.Ordinal);
        Assert.Contains("AddWithValue(\"FeeAccountingStatus\", settlement.FeeAccountingStatus)", source, StringComparison.Ordinal);
        Assert.Contains("AddWithValue(\"FeeAccountingStatus\", run.FeeAccountingStatus)", source, StringComparison.Ordinal);
        Assert.Contains("AddWithValue(\"FeeAccountingStatus\", order.FeeAccountingStatus)", source, StringComparison.Ordinal);

        Assert.Contains("reader.GetString(29)", source, StringComparison.Ordinal);
        Assert.Contains("reader.IsDBNull(46) ? null : reader.GetDecimal(46)", source, StringComparison.Ordinal);
        Assert.Contains("net_realized_pnl_usd numeric", directCompactionSource, StringComparison.Ordinal);
        Assert.Contains("fee_accounting_status, fee_liquidity_role, fee_calculation_source", directCompactionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectPaperSkipCompaction_ProductionV1UsesCSharpUniquenessGateWithoutQuadraticSqlScan()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.DirectSkipCompaction.cs").Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal);
        var v1Start = source.IndexOf(
            "private const string DirectNewPaperSkipArchiveSql",
            StringComparison.Ordinal);
        var v2Start = source.IndexOf(
            "private const string DirectNewPaperSkipArchiveV2Sql",
            StringComparison.Ordinal);
        Assert.True(v1Start >= 0);
        Assert.True(v2Start > v1Start);

        var v1Sql = source[v1Start..v2Start];
        Assert.DoesNotContain("FROM input_rows other_input", v1Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("other_input.ordinality <> candidate.ordinality", v1Sql, StringComparison.Ordinal);

        var productionMethodStart = source.IndexOf(
            "private async Task<IReadOnlySet<Guid>> TryAddStrategyMarketPaperRunsWithDirectCompactionAsync",
            v2Start,
            StringComparison.Ordinal);
        Assert.True(productionMethodStart > v2Start);
        var uniquenessGate = source.IndexOf(
            "EnsureUnambiguousDirectPaperSkipInput(runs);",
            productionMethodStart,
            StringComparison.Ordinal);
        var archiveCall = source.IndexOf(
            "var archivedIds = await ArchiveNewDirectPaperSkippedRunsAsync(",
            productionMethodStart,
            StringComparison.Ordinal);
        Assert.True(uniquenessGate > productionMethodStart);
        Assert.True(archiveCall > uniquenessGate);
    }

    [Fact]
    public void PostgresSchema_ContainsRequiredTables()
    {
        foreach (var table in PostgresSchema.RequiredTables)
        {
            Assert.Contains($"CREATE TABLE IF NOT EXISTS {table}", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        }

        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_leader_trades_dedup", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS ix_polymarket_http_logs_requested", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_up_down_5m_strategy_stage_timings", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS strategy_child_parent_assignments", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("parent_roi_pct numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategy_child_parent_assignments", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS parent_roi_pct numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_strategy_child_parent_assignments_active_child", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_child_parent_assignments_active_parent", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_up_down_5m_strategy_stage_timings_started", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_up_down_5m_strategy_stage_timings_cycle", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_positions", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_captures", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_capture_cursors", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_paper_signal_results", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_paper_signal_results_wallet_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_polymarket_onchain_trade_captures_tx_log", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_trade_captures_contract_block", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_trade_captures_pending_order", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_position_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_token_metadata_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_token_metadata_refresh_queue_next_attempt", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_category_mappings", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_polymarket_category_mappings_local_lower", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('Politics', 'POLITICS'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_activity", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_wallet_activity_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_wallet_performance_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_wallet_category_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_onchain_wallet_category_performance_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_details", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_trade_details_recent", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_participant_details", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_participant_details_score", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidates", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidate_reasons", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidate_refresh_queue", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_signal_candidate_backfill_cursors", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_signal_candidates_status_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_signal_candidate_refresh_queue_next_attempt", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_wallet_fills_signal_candidate_backfill", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_onchain_wallet_fills_source_role", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE IF NOT EXISTS polymarket_data_api_trades", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_gamma_markets", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_gamma_markets_created", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_gamma_markets_condition", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_gamma_markets_active_end_date", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_gamma_markets_active_event_start", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_gamma_markets_clob_token_ids", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("order_min_size numeric", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("order_price_min_tick_size numeric", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("last_trade_price numeric", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_websocket_trade_ticks", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("trader_match_status integer NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("transaction_hash_present boolean NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("WHERE dedup_key IS NULL OR dedup_key = '' OR updated_at_utc IS NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_polymarket_websocket_trade_ticks_dedup", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("copied_trader_wallet text NOT NULL DEFAULT ''", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS strategies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE public.copy_strategies RENAME TO strategies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("live_available_balance numeric(28,8) NOT NULL DEFAULT 100.00", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_available_balance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("paper_lost_coeff numeric(28,8) NOT NULL DEFAULT 1.00", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("live_lost_coeff numeric(28,8) NOT NULL DEFAULT 1.00", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("paper_lost_counter integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("live_lost_counter integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paper_lost_coeff", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_lost_coeff", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paper_lost_counter", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_lost_counter", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ck_strategies_paper_lost_coeff_minimum", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ck_strategies_live_lost_coeff_minimum", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS ck_strategies_paper_lost_counter_nonnegative", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT IF EXISTS ck_strategies_live_lost_counter_nonnegative", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CHECK (paper_lost_counter >= 0)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CHECK (live_lost_counter >= 0)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("paused boolean NOT NULL DEFAULT false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paused boolean NOT NULL DEFAULT false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS paused_until_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("auto_live_paused boolean NOT NULL DEFAULT false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS auto_live_paused boolean NOT NULL DEFAULT false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("auto_live_paused_at_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS auto_live_paused_at_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("auto_live_pause_window_start_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS auto_live_pause_window_start_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("live_enabled_at_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ADD COLUMN IF NOT EXISTS live_enabled_at_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("first_live_events AS", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260602_clear_auto_live_pause_by_default", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260605_backfill_auto_live_pause_anchors", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("WHERE auto_live_paused", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("SET paused = false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE strategies ALTER COLUMN live_stake_amount SET DEFAULT 1.00", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ck_strategies_live_available_balance_nonnegative", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ck_strategies_live_available_balance_maximum", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("WHERE live_available_balance > 100.00", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260709_remove_follow_leader_strategy", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("Follow accepted signals from selected leader traders.", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_less_30'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_less_30_gamma'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_less_180_martin'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_270'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_30_below_55'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_60_below_60'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_60_below_55'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_120_below_70'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_150_below_65'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_270_below_65'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_270_below_60'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_less_120_below_20'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_less_120_below_30'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_less_90_below_20'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_less_60_below_20'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_90_below_70'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_90_below_65'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_60_gamma_below_70'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_60_gamma_below_80'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_120_gamma_below_65'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_90_gamma_below_70'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_120_gamma_below_70'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_150_gamma_below_70'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_150_gamma_below_80'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_more_270_gamma'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_middle_100'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_middle_100_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_middle_' || depths.depth || '_bps_' || thresholds.threshold_digit", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_middle_' || depths.depth || '_bps_' || thresholds.threshold_digit || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_middle_' || depths.depth || '_revert_bps_' || thresholds.threshold_digit", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_middle_' || depths.depth || '_revert_bps_' || thresholds.threshold_digit || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8029-' || lpad((CASE WHEN depths.depth = 100 THEN 100 + thresholds.threshold_digit ELSE (depths.depth * 100) + thresholds.threshold_digit END)::text, 12, '0')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8030-' || lpad((CASE WHEN depths.depth = 100 THEN 100 + thresholds.threshold_digit ELSE (depths.depth * 100) + thresholds.threshold_digit END)::text, 12, '0')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("generate_series(1, 100)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("VALUES (100), (90), (80), (70), (60), (50), (40), (30), (20), (10)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_middle_' || depth || code_suffix", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_middle_' || depths.depth || '_bps_' || thresholds.threshold_digit", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_middle_' || depths.depth || '_bps_' || thresholds.threshold_digit || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_middle_' || depths.depth || '_revert_bps_' || thresholds.threshold_digit", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_middle_' || depths.depth || '_revert_bps_' || thresholds.threshold_digit || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('ETH', '8071', '8072', '8073', '8074')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('SOL', '8075', '8076', '8077', '8078')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260522_rescale_middle_bps_history_reset", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260522_retire_middle_depth_2_5", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_skip_1'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_skip_5'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_skip_1_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_skip_5_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_skip_' || depth_name", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_skip_bps_' || code_suffix", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_skip_bps_' || code_suffix || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('ETH', '8061', '8062')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('SOL', '8063', '8064')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260707_remove_eth_binance_bps_strategies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260709_remove_sol_binance_bps_strategies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_up'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_down'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('BTC', '8121', '8122')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('ETH', '8123', '8124')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('SOL', '8125', '8126')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-8130-000000000109'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'eth_up_down_5m_down_bps_9_fak'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'ETH Up or Down 5m Down 9 bps'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("WITH legacy_eth_down_premarket_thresholds(threshold_tenths) AS", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('b7c50005-0000-4000-8131-' || lpad((100 + threshold_tenths)::text, 12, '0'))::uuid", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'ETH Up or Down 5m Down ' || threshold_name || ' bps Premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('BTC', '8135', '8136')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8137', '8140')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8138', '8139')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('BTC', '8178')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8179')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8180')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('BTC', '8213')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8214')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8215')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_low_enter_average_bps_' || threshold_value::text || '_fak_premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("asset_symbol || ' Up or Down 5m ' || threshold_value::text || ' bps LowEnter Average Premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('BTC', '8182', '8191')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8183', '8192')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8184', '8193')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_futures_basis_bps_' || threshold_value::text || mode_code || '_fak_premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("asset_symbol || ' Up or Down 5m ' || threshold_value::text || ' bps Futures Basis' || mode_name || ' Premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("select the three live OKX linear USD fixed-expiry contracts with the closest distinct expiries at or after the target market end", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("threshold only to the nearest expiry and require both following expiries to confirm its nonzero basis sign", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("Require all three fresh contracts and never substitute a perpetual contract", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("Binance USD-M futures ' || asset_symbol || 'USDT", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('_revert', ' Revert', assets.revert_id_group)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('BTC', '8185', '8188', '8194', '8197')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8186', '8189', '8195', '8198')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8187', '8190', '8196', '8199')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_' || lookback_hours::text || '_' || mode_code", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("asset_symbol || ' Up or Down 5m ' || lookback_hours::text || ' ' || mode_name", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("select the enabled non-Child, non-Futures ' || asset_symbol || ' strategy", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('child_roi', 'Child ROI', 'excluding strategies whose name contains Progress', 'child_roi', 'sample-adjusted paper ROI after minimum sample gates')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('child_progress_roi', 'Child Progress ROI', 'including Progress strategies', 'child_progress_roi', 'sample-adjusted paper ROI after minimum sample gates')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8181-' || lpad((100 + threshold_tenths)::text, 12, '0')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260712_remove_eth_down_filtered_average_premarket_strategies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-8181-000000000101'::uuid", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("generate_series(15, 100, 5)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("THEN '_reference_average' ELSE '' END ||", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("asset_symbol || ' Up or Down 5m ' || trigger_name || ' ' || threshold_name || ' bps Reference Average Premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_reference_average_bps_' || code_suffix || '_fak_premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("asset_symbol || ' Up or Down 5m ' || threshold_name || ' bps Reference Average Premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'eth_up_down_5m_down_filtered_average_bps_' || code_suffix || '_fak_premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'ETH Up or Down 5m Down ' || threshold_name || ' bps Filtered Average Premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("If the selected reference window is 6h or 12h, skip.", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("largest usable in-memory reference average across every configured 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m window", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("smallest usable in-memory reference average across every configured 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m window", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("envelope formed by the smallest and largest usable in-memory reference averages across every configured 24h, 12h, 6h, 3h, 90m, 45m, 20m, and 10m window", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("whether complete or incomplete; gaps and incomplete coverage alone do not block calculation", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("oldest available real bucket in the 24h window; that 24h window may be incomplete, but no other denominator is substituted", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("If the current price moves Up by at least", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("If the current price moves Down by at least", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("if it is below the minimum boundary by at least", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("strategies.code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("replace(name, ' bps FAK Premarket', ' bps Premarket')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy.live_stakes AS legacy_live_stakes", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy_live_available_balance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        var normalizedSchemaSql = PostgresSchema.SchemaSql.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("20260623_restore_eth_down_previous_result_premarket_enabled", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains(
            "SET enabled = true,\n            updated_at_utc = clock_timestamp()\n        WHERE strategy.code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'",
            normalizedSchemaSql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SET enabled = false,\n    live_stakes = false,\n    updated_at_utc = now()\nWHERE strategies.code ~ '^eth_up_down_5m_down_bps_[0-9]+_fak_premarket$'",
            normalizedSchemaSql,
            StringComparison.Ordinal);
        Assert.Contains("(8132, -10, 10, 40)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("(8133, -5, 5, 35)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'eth_up_down_5m_down_bps_' || code_suffix || '_fak_premarket_' || premarket_suffix", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'ETH Up or Down 5m Down ' || threshold_name || ' bps Premarket -' || sample_seconds_before_end::text || 's'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('BTC', '8146', '8148')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8144', '8134')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8150', '8152')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('up', 'Up', 'UpCount - DownCount', 'Down', 'countertrend')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("('down', 'Down', 'DownCount - UpCount', 'Up', 'countertrend')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(asset_symbol) || '_up_down_5m_' || diff_code || '_diff_' || threshold_value::text || '_fak_premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("revert_code_suffix", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy_kind = 'revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("20260708_remove_simple_strategies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_' || direction_code || '_simple'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("asset_symbol || ' Up or Down 5m ' || direction_name || ' Simple'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("description = excluded.description,\n    live_stakes = false,\n    updated_at_utc = excluded.updated_at_utc", normalizedSchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_up_maker'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_down_maker'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_down_maker_50'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('BTC', '8097', '8098', '8115', '8116')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('ETH', '8099', '8100', '8117', '8118')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('SOL', '8101', '8102', '8119', '8120')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_' || diff_code || '_shift_diff_' || shift_value::text || '_' || threshold_value::text || revert_code_suffix || '_instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("asset_symbol || ' Up or Down 5m ' || diff_name || ' ' || shift_value::text || ' ' || threshold_value::text || ' ShiftDiff' || revert_name_suffix || ' Instant'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_statistics'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_prev_score_countertrend_' || prices.price_cents", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8025-' || lpad(prices.price_cents::text, 12, '0')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("btc_up_down_5m_prev_score_countertrend_90", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Prev Score Countertrend 90", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("btc_up_down_5m_prev_score_countertrend_85", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Prev Score Countertrend 85", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("btc_up_down_5m_prev_score_countertrend_80", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Prev Score Countertrend 80", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("btc_up_down_5m_prev_score_countertrend_75", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Prev Score Countertrend 75", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("btc_up_down_5m_prev_score_countertrend_70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Prev Score Countertrend 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("btc_up_down_5m_prev_score_countertrend_65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Prev Score Countertrend 65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("btc_up_down_5m_prev_score_countertrend_60", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Prev Score Countertrend 60", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("btc_up_down_5m_prev_score_countertrend_55", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Prev Score Countertrend 55", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8025-000000000998'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_prev_score_countertrend_fak'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'BTC Up or Down 5m Prev Score Countertrend'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8025-000000000997'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_prev_score_countertrend_fak_premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'BTC Up or Down 5m Prev Score Countertrend Premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8025-000000000999'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_prev_score_countertrend_fak_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'BTC Up or Down 5m Prev Score Countertrend Revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8025-000000000996'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m_prev_score_countertrend_fak_premarket_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'BTC Up or Down 5m Prev Score Countertrend Premarket Revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('ETH', '8141')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("('SOL', '8142')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_prev_score_countertrend_fak'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("asset_symbol || ' Up or Down 5m Prev Score Countertrend'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8141-000000000999'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'eth_up_down_5m_prev_score_countertrend_fak_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'ETH Up or Down 5m Prev Score Countertrend Revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8142-000000000999'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'sol_up_down_5m_prev_score_countertrend_fak_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'SOL Up or Down 5m Prev Score Countertrend Revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8141-000000000996'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'eth_up_down_5m_prev_score_countertrend_fak_premarket_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'ETH Up or Down 5m Prev Score Countertrend Premarket Revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-8142-000000000996'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'sol_up_down_5m_prev_score_countertrend_fak_premarket_revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'SOL Up or Down 5m Prev Score Countertrend Premarket Revert'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("lower(asset_symbol) || '_up_down_5m_prev_score_countertrend_fak_premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("asset_symbol || ' Up or Down 5m Prev Score Countertrend Premarket'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_' || intervals.interval_code || '_preopen_'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-803' || intervals.interval_id", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("_preopen_full_", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'b7c50005-0000-4000-804' || intervals.interval_id", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("PreOpen Full ' || outcomes.outcome_name || ' ' || prices.price_cents || ' Sell'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("lower(COALESCE(NEW.category, '')) LIKE '%preopen%'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Less 30", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Less 30 Gamma", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Less 180 Martin", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 270", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 30 Below 55", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 60 Below 60", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 60 Below 55", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 120 Below 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 150 Below 65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 270 Below 65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 270 Below 60", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Less 120 Below 20", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Less 120 Below 30", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Less 90 Below 20", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Less 60 Below 20", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 90 Below 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 90 Below 65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Up Maker 50", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Down Maker 50", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 60 Gamma Below 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 60 Gamma Below 80", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 120 Gamma Below 65", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 90 Gamma Below 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 120 Gamma Below 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 150 Gamma Below 70", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 150 Gamma Below 80", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m More 270 Gamma", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Middle 100", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Middle 100 Revert", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Middle ' || depths.depth || ' ' || thresholds.threshold_name || ' bps Instant", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Middle ' || depths.depth || ' Revert ' || thresholds.threshold_name || ' bps Instant", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Middle ' || depths.depth || ' ' || thresholds.threshold_name || ' bps", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Middle ' || depths.depth || ' Revert ' || thresholds.threshold_name || ' bps", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Skip 5", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Skip 5 Revert", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("asset_symbol || ' Up or Down 5m Skip ' || depth_name", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("asset_symbol || ' Up or Down 5m Skip ' || threshold_name || ' bps Instant", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'BTC Up or Down 5m Up',", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'BTC Up or Down 5m Down',", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("Simple strategy: immediately after", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("BTC Up or Down 5m Statistics", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'btc_up_down_5m',", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("strategy_id uuid NOT NULL DEFAULT 'f0110a0d-1ead-4c00-8b01-000000000001'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("REFERENCES strategies(id)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_strategy_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_strategy_perf_cover", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_id_strategy_side_cover", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_copied_wallet_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_created_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("realized_pnl_usd numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_fills_order_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("balance_effect_applied boolean NOT NULL DEFAULT false", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("average_fill_price numeric(18,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("filled_notional_usd numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("cost_basis_usd numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("fee_usd numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("won boolean NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("settlement_source text NOT NULL DEFAULT ''", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("correlation_id uuid NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("execution_source text NOT NULL DEFAULT ''", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("post_only boolean NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("paper_order_id uuid NULL REFERENCES paper_orders(id)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_live_orders_strategy_settlement", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_live_orders_pending_balance_settlement", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_live_shadow_decisions", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_live_shadow_discrepancies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_live_shadow_decisions_strategy_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_live_shadow_discrepancies_correlation", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS strategy_market_paper_runs", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (strategy_id, market_id)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_entry_due", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_settlement_due", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_status_market_end", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_strategy_entered", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_strategy_updated", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_strategy_settled", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_fills_filled_time_order", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_fills_filled_perf_cover", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_updated_time_strategy", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_entered_time_strategy", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_strategy_market_paper_runs_settled_time_strategy", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_paper_positions_wallet_asset", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain("ix_paper_positions_wallet_updated", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_positions_updated_page_cover", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ON paper_positions(updated_at_utc DESC, copied_trader_wallet, asset_id)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_positions_total_pnl_usd numeric(28,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_leaderboard_pnl_usd numeric(28,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE polymarket_data_api_wallet_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE polymarket_data_api_wallet_category_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("polymarket_rating_next_refresh_at_utc timestamptz NOT NULL DEFAULT now()", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS polymarket_data_api_wallet_category_ratings", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_data_api_wallet_category_ratings_category_pnl", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_data_api_wallet_category_ratings_lookup", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("leaderboard_pnl_to_volume_pct numeric(18,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_data_api_wallet_category_ratings_leaderboard_ratio", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("current_positions_percent_pnl numeric(18,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("closed_positions_realized_pnl_usd numeric(28,8) NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("positions_total_percent_pnl numeric(18,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_polymarket_data_api_wallet_category_ratings_positions_pnl", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_position_settlements", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_paper_position_settlements_wallet_asset", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_copied_trader_performance", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_copied_trader_performance_score", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_usd_reference_correlation_samples", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_usd_reference_correlation_samples_created", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crypto_reference_price_ticks", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_crypto_reference_price_ticks_asset_bucket", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_crypto_reference_price_ticks_asset_sampled", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_up_down_5m_odds_ticks", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_up_down_5m_odds_ticks_market_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_5m_history", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("id bigserial PRIMARY KEY", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("seconds integer NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("cents integer NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("count integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("up_count integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("down_count integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT ux_btc_5m_history_seconds_cents UNIQUE (seconds, cents)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE btc_5m_history", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT ux_btc_5m_history_seconds_cents UNIQUE (seconds, cents)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_5m_history_live_observations", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (market_id, seconds)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_5m_history_live_observations_due", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_up_down_5m_statistics_ticks", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("effective_count numeric(28,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("decision_code text NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_up_down_5m_statistics_ticks_decision", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_up_down_5m_arbitrage_scans", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("net_profit_usd numeric(28,8) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("would_arbitrage boolean NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_up_down_5m_arbitrage_scans_decision", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS btc_up_down_5m_result_streak_diagnostics", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("close_book_streak_result_count integer NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("cumulative_abs_move_bps numeric(28,12) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ux_btc_up_down_5m_result_streak_diagnostics_market", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_btc_up_down_5m_result_streak_diagnostics_streak", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crypto_up_down_5m_odds_ticks", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_crypto_up_down_5m_odds_ticks_asset_market_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("up_price_proxy_kind text NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("binance_start_price_usd numeric(28,8) NOT NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crypto_up_down_5m_diff_snapshots", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT ux_crypto_up_down_5m_diff_snapshots_asset_market UNIQUE (asset_symbol, market_start_utc)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_crypto_up_down_5m_diff_snapshots_asset_sampled", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("diff_count integer NOT NULL DEFAULT 0", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE crypto_up_down_5m_diff_snapshots", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("history_fetch_retry_after_utc timestamptz NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crypto_up_down_5m_result_polling_observations", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT ux_crypto_up_down_5m_result_polling_market UNIQUE (market_id)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("result_delay_seconds numeric(18,3) NULL", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_crypto_up_down_5m_result_polling_winner", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_copied_leader_positions", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_copied_leader_positions_due", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_copied_leader_positions_wallet_asset", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_copied_leader_activity_events", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (dedup_key)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("ix_paper_copied_leader_activity_events_wallet_asset_time", PostgresSchema.SchemaSql, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_SeedsConfirmedAveragePremarketStrategiesPaperOnly()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var bpsStatement = Assert.Single(statements, statement =>
            statement.Contains("'_bps_confirmed_average_premarket'", StringComparison.Ordinal));
        var diffStatement = Assert.Single(statements, statement =>
            statement.Contains("'_diff_confirmed_average_premarket'", StringComparison.Ordinal));

        Assert.Contains("('BTC', '8200', 5)", bpsStatement, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8201', 3)", bpsStatement, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8202', 1)", bpsStatement, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 10)", bpsStatement, StringComparison.Ordinal);
        Assert.Contains("generate_series(15, 100, 5)", bpsStatement, StringComparison.Ordinal);
        Assert.Contains("lpad((100 + threshold_value)::text, 12, '0')", bpsStatement, StringComparison.Ordinal);

        Assert.Contains("('BTC', '8203', 45)", diffStatement, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8204', 5)", diffStatement, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8205', 35)", diffStatement, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 10)", diffStatement, StringComparison.Ordinal);
        Assert.Contains("(VALUES (15), (20), (25), (30))", diffStatement, StringComparison.Ordinal);

        foreach (var statement in new[] { bpsStatement, diffStatement })
        {
            var normalizedStatement = statement.Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.Contains("enabled,", statement, StringComparison.Ordinal);
            Assert.Contains("live_stakes,", statement, StringComparison.Ordinal);
            Assert.Contains("paused,", statement, StringComparison.Ordinal);
            Assert.Contains(
                "true,\n    false,\n    1.00,\n    1.00,\n    100.00,\n    false,",
                normalizedStatement,
                StringComparison.Ordinal);
            Assert.DoesNotContain("enabled = excluded.enabled", statement, StringComparison.Ordinal);
            Assert.DoesNotContain("live_stakes = excluded.live_stakes", statement, StringComparison.Ordinal);
            Assert.DoesNotContain("paused = excluded.paused", statement, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PostgresSchema_SeedsEthOptimizedAveragePremarketGridLiveDisabledByDefault()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var statement = Assert.Single(statements, item =>
            item.Contains("'eth_up_down_5m_' || code_trigger_prefix || 'optimized_average_bps_'", StringComparison.Ordinal));
        var normalizedStatement = statement.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("('8209', 'up_', 'Up ', 'Up', 'Down')", statement, StringComparison.Ordinal);
        Assert.Contains("('8210', 'down_', 'Down ', 'Down', 'Up')", statement, StringComparison.Ordinal);
        Assert.Contains("('8211', '', '', NULL, NULL)", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 10)", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(15, 100, 5)", statement, StringComparison.Ordinal);
        Assert.Contains("lpad((100 + threshold_value)::text, 12, '0')", statement, StringComparison.Ordinal);
        Assert.Contains(
            "'eth_up_down_5m_' || code_trigger_prefix || 'optimized_average_bps_' || threshold_value::text || '_fak_premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'ETH Up or Down 5m ' || name_trigger_prefix || threshold_value::text || ' bps Optimized Average Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("Use the largest usable reference average as the maximum boundary", statement, StringComparison.Ordinal);
        Assert.Contains("Use the smallest usable reference average as the minimum boundary", statement, StringComparison.Ordinal);
        Assert.Contains("Use the envelope formed by the smallest and largest usable reference averages", statement, StringComparison.Ordinal);
        Assert.Contains("whether complete or incomplete; gaps and incomplete coverage alone do not block calculation", statement, StringComparison.Ordinal);
        Assert.Contains("direction-relevant maximum boundary came from the 3h window", statement, StringComparison.Ordinal);
        Assert.Contains("direction-relevant minimum boundary came from the 3h window", statement, StringComparison.Ordinal);
        Assert.Contains("envelope boundary that triggered the signal came from the 3h window", statement, StringComparison.Ordinal);
        Assert.Contains("Live execution is not supported for this optimized Paper experiment", statement, StringComparison.Ordinal);
        Assert.Contains(
            "true,\n    false,\n    1.00,\n    1.00,\n    100.00,\n    false,",
            normalizedStatement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("enabled = excluded.enabled", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stakes = excluded.live_stakes", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_stake_amount = excluded.paper_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stake_amount = excluded.live_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paused = excluded.paused", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_lost_counter = excluded.paper_lost_counter", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_lost_counter = excluded.live_lost_counter", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_SeedsExactBtcOptimizedAveragePremarketGridsLiveDisabledByDefault()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var statement = Assert.Single(statements, item =>
            item.Contains("'btc_up_down_5m_' || code_trigger_prefix || 'optimized_average_bps_'", StringComparison.Ordinal));
        var normalizedStatement = statement.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("('8219', 'up_', 'Up ', 'Up', 'Down')", statement, StringComparison.Ordinal);
        Assert.Contains("('8212', 'down_', 'Down ', 'Down', 'Up')", statement, StringComparison.Ordinal);
        Assert.Contains("('8220', '', '', NULL, NULL)", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 10)", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("generate_series(15, 100, 5)", statement, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-' || id_group || '-'", statement, StringComparison.Ordinal);
        Assert.Contains("lpad((100 + threshold_value)::text, 12, '0')", statement, StringComparison.Ordinal);
        Assert.Contains(
            "'btc_up_down_5m_' || code_trigger_prefix || 'optimized_average_bps_' || threshold_value::text || '_fak_premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'BTC Up or Down 5m ' || name_trigger_prefix || threshold_value::text || ' bps Optimized Average Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("latest Binance BTC/USDT reference price", statement, StringComparison.Ordinal);
        Assert.Contains("Use the largest usable reference average as the maximum boundary", statement, StringComparison.Ordinal);
        Assert.Contains("Use the smallest usable reference average as the minimum boundary", statement, StringComparison.Ordinal);
        Assert.Contains("Use the envelope formed by the smallest and largest usable reference averages", statement, StringComparison.Ordinal);
        Assert.Contains("whether complete or incomplete; gaps and incomplete coverage alone do not block calculation", statement, StringComparison.Ordinal);
        Assert.Contains("direction-relevant maximum boundary came from the 3h window", statement, StringComparison.Ordinal);
        Assert.Contains("direction-relevant minimum boundary came from the 3h window", statement, StringComparison.Ordinal);
        Assert.Contains("envelope boundary that triggered the signal came from the 3h window", statement, StringComparison.Ordinal);
        Assert.Contains("Live execution is not supported for this optimized Paper experiment", statement, StringComparison.Ordinal);
        Assert.Contains(
            "true,\n    false,\n    1.00,\n    1.00,\n    100.00,\n    false,",
            normalizedStatement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("enabled = excluded.enabled", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stakes = excluded.live_stakes", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_stake_amount = excluded.paper_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stake_amount = excluded.live_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paused = excluded.paused", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_lost_counter = excluded.paper_lost_counter", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_lost_counter = excluded.live_lost_counter", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_SeedsExactSolOptimizedAveragePremarketGridsLiveDisabledByDefault()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var statement = Assert.Single(statements, item =>
            item.Contains("'sol_up_down_5m_' || code_trigger_prefix || 'optimized_average_bps_'", StringComparison.Ordinal));
        var normalizedStatement = statement.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("('8221', 'up_', 'Up ', 'Up', 'Down')", statement, StringComparison.Ordinal);
        Assert.Contains("('8218', 'down_', 'Down ', 'Down', 'Up')", statement, StringComparison.Ordinal);
        Assert.Contains("('8222', '', '', NULL, NULL)", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 10)", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("generate_series(15, 100, 5)", statement, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-' || id_group || '-'", statement, StringComparison.Ordinal);
        Assert.Contains("lpad((100 + threshold_value)::text, 12, '0')", statement, StringComparison.Ordinal);
        Assert.Contains(
            "'sol_up_down_5m_' || code_trigger_prefix || 'optimized_average_bps_' || threshold_value::text || '_fak_premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'SOL Up or Down 5m ' || name_trigger_prefix || threshold_value::text || ' bps Optimized Average Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("latest Binance SOL/USDT reference price", statement, StringComparison.Ordinal);
        Assert.Contains("Use the largest usable reference average as the maximum boundary", statement, StringComparison.Ordinal);
        Assert.Contains("Use the smallest usable reference average as the minimum boundary", statement, StringComparison.Ordinal);
        Assert.Contains("Use the envelope formed by the smallest and largest usable reference averages", statement, StringComparison.Ordinal);
        Assert.Contains("whether complete or incomplete; gaps and incomplete coverage alone do not block calculation", statement, StringComparison.Ordinal);
        Assert.Contains("direction-relevant maximum boundary came from the 3h window", statement, StringComparison.Ordinal);
        Assert.Contains("direction-relevant minimum boundary came from the 3h window", statement, StringComparison.Ordinal);
        Assert.Contains("envelope boundary that triggered the signal came from the 3h window", statement, StringComparison.Ordinal);
        Assert.Contains("Live execution is not supported for this optimized Paper experiment", statement, StringComparison.Ordinal);
        Assert.Contains(
            "true,\n    false,\n    1.00,\n    1.00,\n    100.00,\n    false,",
            normalizedStatement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("enabled = excluded.enabled", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stakes = excluded.live_stakes", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_stake_amount = excluded.paper_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stake_amount = excluded.live_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paused = excluded.paused", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_lost_counter = excluded.paper_lost_counter", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_lost_counter = excluded.live_lost_counter", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_SeedsLowEnterAveragePremarketGridForEveryAssetPaperOnly()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var statement = Assert.Single(statements, item =>
            item.Contains("'_up_down_5m_low_enter_average_bps_'", StringComparison.Ordinal));
        var normalizedStatement = statement.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("('BTC', '8213')", statement, StringComparison.Ordinal);
        Assert.Contains("('ETH', '8214')", statement, StringComparison.Ordinal);
        Assert.Contains("('SOL', '8215')", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 10)", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(15, 100, 5)", statement, StringComparison.Ordinal);
        Assert.Contains("lpad((100 + threshold_value)::text, 12, '0')", statement, StringComparison.Ordinal);
        Assert.Contains(
            "lower(asset_symbol) || '_up_down_5m_low_enter_average_bps_' || threshold_value::text || '_fak_premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "asset_symbol || ' Up or Down 5m ' || threshold_value::text || ' bps LowEnter Average Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("envelope formed by the smallest and largest usable in-memory reference averages", statement, StringComparison.Ordinal);
        Assert.Contains("whether complete or incomplete; gaps and incomplete coverage alone do not block calculation", statement, StringComparison.Ordinal);
        Assert.Contains("above the maximum boundary by at least", statement, StringComparison.Ordinal);
        Assert.Contains("below the minimum boundary by at least", statement, StringComparison.Ordinal);
        Assert.Contains("maximum order price of 0.50", statement, StringComparison.Ordinal);
        Assert.Contains("immediately executable asks at or below that price", statement, StringComparison.Ordinal);
        Assert.Contains("remainder", statement, StringComparison.Ordinal);
        Assert.Contains("Live execution is not supported for this Paper experiment", statement, StringComparison.Ordinal);
        Assert.Contains(
            "true,\n    false,\n    1.00,\n    1.00,\n    100.00,\n    false,",
            normalizedStatement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("enabled = excluded.enabled", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stakes = excluded.live_stakes", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_stake_amount = excluded.paper_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stake_amount = excluded.live_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paused = excluded.paused", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_SeedsEthThreeHourAveragePremarketGrids()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var statement = Assert.Single(statements, item =>
            item.Contains("'eth_up_down_5m_' || code_marker || '_bps_'", StringComparison.Ordinal));
        var normalizedStatement = statement.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("('8216', '3hour_average', '3Hour Average'", statement, StringComparison.Ordinal);
        Assert.Contains("('8217', '3hour_low_enter_average', '3Hour LowEnter Average'", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(1, 10)", statement, StringComparison.Ordinal);
        Assert.Contains("generate_series(15, 100, 5)", statement, StringComparison.Ordinal);
        Assert.Contains("lpad((100 + threshold_value)::text, 12, '0')", statement, StringComparison.Ordinal);
        Assert.Contains(
            "'eth_up_down_5m_' || code_marker || '_bps_' || threshold_value::text || '_fak_premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'ETH Up or Down 5m ' || threshold_value::text || ' bps ' || name_marker || ' Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("usable in-memory 3h reference average only; other configured windows do not participate", statement, StringComparison.Ordinal);
        Assert.Contains("whether complete or incomplete; gaps and incomplete coverage alone do not block calculation", statement, StringComparison.Ordinal);
        Assert.Contains("maximum order price of 0.50", statement, StringComparison.Ordinal);
        Assert.Contains("immediately executable asks at or below that price", statement, StringComparison.Ordinal);
        Assert.Contains("remainder", statement, StringComparison.Ordinal);
        Assert.Contains(
            "true,\n    false,\n    1.00,\n    1.00,\n    100.00,\n    false,",
            normalizedStatement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("enabled = excluded.enabled", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stakes = excluded.live_stakes", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_stake_amount = excluded.paper_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stake_amount = excluded.live_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paused = excluded.paused", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_SeedsExactBtcLowerEnterPremarketCloneAllowlistPaperOnly()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var statement = Assert.Single(statements, item =>
            item.Contains("_lower_enter_premarket", StringComparison.Ordinal));

        var lowerEnterVariants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant => variant.LowerEnterSourceStrategyId is not null)
            .ToArray();

        Assert.Equal(324, StrategyIds.BtcLowerEnterPremarketVariants.Count);
        Assert.Equal(438, lowerEnterVariants.Length);
        Assert.Equal(
            438,
            statement.Split("b7c50005-0001-4000-", StringSplitOptions.None).Length - 1);
        foreach (var variant in lowerEnterVariants)
        {
            Assert.Contains(
                $"'{variant.Id:D}', '{variant.Code}', '{variant.Name}'",
                statement,
                StringComparison.Ordinal);
        }
        Assert.Contains(
            "'b7c50005-0001-4000-8219-000000000101', 'btc_up_down_5m_up_optimized_average_bps_1_fak_lower_enter_premarket', 'BTC Up or Down 5m Up 1 bps Optimized Average LowerEnter Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'b7c50005-0001-4000-8220-000000000101', 'btc_up_down_5m_optimized_average_bps_1_fak_lower_enter_premarket', 'BTC Up or Down 5m 1 bps Optimized Average LowerEnter Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'b7c50005-0001-4000-8221-000000000101', 'sol_up_down_5m_up_optimized_average_bps_1_fak_lower_enter_premarket', 'SOL Up or Down 5m Up 1 bps Optimized Average LowerEnter Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'b7c50005-0001-4000-8222-000000000101', 'sol_up_down_5m_optimized_average_bps_1_fak_lower_enter_premarket', 'SOL Up or Down 5m 1 bps Optimized Average LowerEnter Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'b7c50005-0001-4000-8218-000000000101', 'sol_up_down_5m_down_optimized_average_bps_1_fak_lower_enter_premarket', 'SOL Up or Down 5m Down 1 bps Optimized Average LowerEnter Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'b7c50005-0001-4000-8209-000000000101', 'eth_up_down_5m_up_optimized_average_bps_1_fak_lower_enter_premarket', 'ETH Up or Down 5m Up 1 bps Optimized Average LowerEnter Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'b7c50005-0001-4000-8210-000000000101', 'eth_up_down_5m_down_optimized_average_bps_1_fak_lower_enter_premarket', 'ETH Up or Down 5m Down 1 bps Optimized Average LowerEnter Premarket'",
            statement,
            StringComparison.Ordinal);
        Assert.Contains(
            "'b7c50005-0001-4000-8211-000000000101', 'eth_up_down_5m_optimized_average_bps_1_fak_lower_enter_premarket', 'ETH Up or Down 5m 1 bps Optimized Average LowerEnter Premarket'",
            statement,
            StringComparison.Ordinal);

        Assert.Contains("true, false, 1.00, 1.00, 100.00, false", statement, StringComparison.Ordinal);
        Assert.Contains("maximum order price of 0.50", statement, StringComparison.Ordinal);
        Assert.Contains("immediately executable asks at or below that price", statement, StringComparison.Ordinal);
        Assert.Contains("remainder", statement, StringComparison.Ordinal);
        Assert.Contains("Live execution is disabled", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("enabled = excluded.enabled", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("live_stakes = excluded.live_stakes", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_stake_amount = excluded.paper_stake_amount", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("paused = excluded.paused", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_OnChainPaperSignalCandidateQuery_LimitsPendingBeforeMetadataJoins()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetPendingOnChainPaperSignalCandidatesAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("AddOnChainPaperSignalResultAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var pendingQuery = source[start..end];
        Assert.Contains("WITH pending_captures AS MATERIALIZED", pendingQuery, StringComparison.Ordinal);
        Assert.Contains("participants AS MATERIALIZED", pendingQuery, StringComparison.Ordinal);
        Assert.Contains("maker_processed.participant_role = 'Maker'", pendingQuery, StringComparison.Ordinal);
        Assert.Contains("taker_processed.participant_role = 'Taker'", pendingQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE processed.id IS NULL", pendingQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_GetRecentPaperOrders_UsesCreatedTimeIndexAndLightweightProjection()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetRecentPaperOrdersAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("AddPaperFillAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("RecentPaperOrderSelectColumns", method, StringComparison.Ordinal);
        Assert.Contains("strategy_id = @StrategyId", method, StringComparison.Ordinal);
        Assert.Contains("created_at_utc >= @CreatedAfterUtc", method, StringComparison.Ordinal);
        Assert.Contains("NpgsqlDbType.TimestampTz", method, StringComparison.Ordinal);
        Assert.Contains("ORDER BY created_at_utc DESC", method, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SELECT \" + PaperOrderSelectColumns", method, StringComparison.Ordinal);
        Assert.Contains("NULL::text", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_UpdatePaperOrderPersistsFillEconomics()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("UpdatePaperOrderAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetOpenPaperOrdersAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("price = @Price", method, StringComparison.Ordinal);
        Assert.Contains("size_shares = @SizeShares", method, StringComparison.Ordinal);
        Assert.Contains("notional_usd = @NotionalUsd", method, StringComparison.Ordinal);
        Assert.Contains("AddWithValue(\"Price\", order.Price)", method, StringComparison.Ordinal);
        Assert.Contains("AddWithValue(\"SizeShares\", order.SizeShares)", method, StringComparison.Ordinal);
        Assert.Contains("AddWithValue(\"NotionalUsd\", order.NotionalUsd)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_GetRecentLiveOrders_SupportsStrategyFilteredFirstPage()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetRecentLiveOrdersAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("AddLiveTradingEventAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("strategy_id = @StrategyId", method, StringComparison.Ordinal);
        Assert.Contains("created_at_utc >= @CreatedAfterUtc", method, StringComparison.Ordinal);
        Assert.Contains("NpgsqlDbType.TimestampTz", method, StringComparison.Ordinal);
        Assert.Contains("ORDER BY created_at_utc DESC", method, StringComparison.Ordinal);
        Assert.Contains("LIMIT @Limit", method, StringComparison.Ordinal);
        Assert.Contains("OFFSET @Offset", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardDataService_LoadOrderRows_DoesNotRefreshStrategyPerformance()
    {
        var source = ReadDashboardDataServiceSource();
        var start = source.IndexOf("LoadOrderRowsAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("InvalidateStrategyPerformanceCache", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("GetOrderStrategyNamesById", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetStrategyPerformanceAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardDataService_LoadOrderRows_LoadsPaperRunAndFillAccountingRows()
    {
        var source = ReadDashboardDataServiceSource();
        var start = source.IndexOf("LoadOrderRowsAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("InvalidateStrategyPerformanceCache", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("GetPaperRunsByOrderIdAsync", method, StringComparison.Ordinal);
        Assert.Contains("GetPaperFillsByOrderIdAsync", method, StringComparison.Ordinal);
        Assert.Contains("ToPaperOrderRow(order, strategyNamesById, paperRunsByOrderId, paperFillsByOrderId)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardDataService_LoadLiveOrderRows_DoesNotLoadPaperOrders()
    {
        var source = ReadDashboardDataServiceSource();
        var start = source.IndexOf("public async Task<DashboardLiveOrderSnapshot> LoadLiveOrderRowsAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("private async Task<(IReadOnlyList<LiveOrder> Orders, bool HasNextPage)> GetRecentLiveOrderPageAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("GetRecentLiveOrderPageAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRecentPaperOrdersAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPaperRunsByOrderIdAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_MainViewModel_UsesConfiguredDefaultDatabaseSource()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "ViewModels", "MainViewModel.cs");

        Assert.Contains("DashboardRepositoryFactory.GetDefaultDatabaseSource()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RebuildRuntime(DashboardDatabaseSource.Local)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_MainViewModel_BigSettlesFilterStartsAtOneThousand()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "ViewModels", "MainViewModel.cs");

        Assert.Contains("private const int BigSettlesThreshold = 1000;", source, StringComparison.Ordinal);
        Assert.Contains("strategy.SettledPositionsCount >= BigSettlesThreshold", source, StringComparison.Ordinal);
        Assert.Contains("strategy.SettledRunsCount >= BigSettlesThreshold", source, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy.SettledPositionsCount > BigSettlesThreshold", source, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy.SettledRunsCount > BigSettlesThreshold", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardRepositoryFactory_ReadsConfiguredDefaultDatabaseSource()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "Services", "DashboardRepositoryFactory.cs");

        Assert.Contains("GetDefaultDatabaseSource", source, StringComparison.Ordinal);
        Assert.Contains("DashboardDatabaseSources.FromConfiguredValue(dashboardOptions.DefaultDatabaseSource)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardAppSettings_DefaultsToRemoteDatabase()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "appsettings.json");

        Assert.Contains("\"DefaultDatabaseSource\": \"Remote database\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StorageAppSettings_CapServiceAndDashboardConnectionPools()
    {
        using var service = JsonDocument.Parse(
            ReadRepositorySource("src", "PolyCopyTrader.Service", "appsettings.json"));
        using var dashboard = JsonDocument.Parse(
            ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "appsettings.json"));

        Assert.Equal(64, service.RootElement.GetProperty("Storage").GetProperty("MaxPoolSize").GetInt32());
        Assert.Equal(8, dashboard.RootElement.GetProperty("Storage").GetProperty("MaxPoolSize").GetInt32());
    }

    [Fact]
    public void ServiceAppSettings_DisablesPersistenceThatCurrentStrategiesDoNotConsume()
    {
        using var service = JsonDocument.Parse(
            ReadRepositorySource("src", "PolyCopyTrader.Service", "appsettings.json"));
        var root = service.RootElement;

        Assert.False(root.GetProperty("PolymarketHttpLogging").GetProperty("Enabled").GetBoolean());
        Assert.False(root.GetProperty("BtcUpDown5mArbitrageScanner").GetProperty("Enabled").GetBoolean());
        Assert.False(root.GetProperty("MarketDataWebSocket").GetProperty("PersistFrameDiagnostics").GetBoolean());
        Assert.False(root.GetProperty("MarketDataWebSocket").GetProperty("PersistMarketResolvedDiagnostics").GetBoolean());
        Assert.Equal(0, root.GetProperty("MarketDataWebSocket").GetProperty("CriticalFrameDiagnosticSampleEvery").GetInt32());
        Assert.False(root.GetProperty("BtcUpDown5mStrategy").GetProperty("PersistStageTimings").GetBoolean());
        Assert.False(root.GetProperty("BtcUpDown5mStrategy").GetProperty("PersistResultStreakDiagnostics").GetBoolean());
        Assert.False(root.GetProperty("BtcUpDown5mStrategy").GetProperty("PersistDiffCounterSnapshots").GetBoolean());
        Assert.Equal(
            "CryptoUpDown5mOnly",
            root.GetProperty("GammaMarketIngestion").GetProperty("PersistenceScope").GetString());
    }

    [Fact]
    public void StorageWriters_DoNotDuplicateStructuredRowsIntoUnusedJsonPayloads()
    {
        var source = ReadStorageRepositorySource();

        Assert.DoesNotContain("JsonSerializer.Serialize(signal)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Serialize(snapshot)", source, StringComparison.Ordinal);
        Assert.Contains("GetPersistedSkipDiagnosticsJson(run)", source, StringComparison.Ordinal);
        Assert.Contains("AddWithValue(\"OrderBookSnapshotJson\", \"{}\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyProcessor_GatesUnusedDiagnosticPersistence()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "Strategies",
            "BtcUpDown5mPaperStrategyProcessor.cs");

        Assert.Contains("if (!options.PersistDiffCounterSnapshots)", source, StringComparison.Ordinal);
        Assert.Contains("if (!options.PersistResultStreakDiagnostics)", source, StringComparison.Ordinal);
        Assert.Contains("if (!options.PersistStageTimings)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_GetPaperRunsByPaperOrderIds_UsesOrderIndex()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetStrategyMarketPaperRunsByPaperOrderIdsAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("AddPaperFillAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("WHERE paper_order_id = ANY(@PaperOrderIds)", method, StringComparison.Ordinal);
        Assert.Contains("NpgsqlDbType.Array | NpgsqlDbType.Uuid", method, StringComparison.Ordinal);
        Assert.Contains("ReadStrategyMarketPaperRun", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestRepository_GetRecentOrders_FiltersByStrategyBeforeLimit()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var firstStrategy = StrategyIds.FollowLeader;
        var secondStrategy = StrategyIds.BtcUpDown5mUpSimple;
        repository.PaperOrders.Add(CreatePaperOrder(secondStrategy, now.AddMinutes(-1)));
        repository.PaperOrders.Add(CreatePaperOrder(firstStrategy, now.AddMinutes(-2)));
        repository.PaperOrders.Add(CreatePaperOrder(firstStrategy, now.AddMinutes(-3)));
        repository.LiveOrders.Add(CreateLiveOrder(secondStrategy, now.AddMinutes(-1)));
        repository.LiveOrders.Add(CreateLiveOrder(firstStrategy, now.AddMinutes(-2)));
        repository.LiveOrders.Add(CreateLiveOrder(firstStrategy, now.AddMinutes(-3)));

        var paperAll = await repository.GetRecentPaperOrdersAsync(1);
        var paperFiltered = await repository.GetRecentPaperOrdersAsync(1, strategyId: firstStrategy);
        var paperFilteredWindow = await repository.GetRecentPaperOrdersAsync(10, strategyId: firstStrategy, createdAfterUtc: now.AddMinutes(-2.5));
        var liveAll = await repository.GetRecentLiveOrdersAsync(1);
        var liveFiltered = await repository.GetRecentLiveOrdersAsync(1, strategyId: firstStrategy);
        var liveFilteredSecondPage = await repository.GetRecentLiveOrdersAsync(1, strategyId: firstStrategy, offset: 1);
        var liveFilteredWindow = await repository.GetRecentLiveOrdersAsync(10, strategyId: firstStrategy, createdAfterUtc: now.AddMinutes(-2.5));
        var liveFilteredWindowSecondPage = await repository.GetRecentLiveOrdersAsync(1, strategyId: firstStrategy, offset: 1, createdAfterUtc: now.AddMinutes(-2.5));

        Assert.Equal(secondStrategy, Assert.Single(paperAll).StrategyId);
        Assert.Equal(firstStrategy, Assert.Single(paperFiltered).StrategyId);
        Assert.Equal(now.AddMinutes(-2), Assert.Single(paperFilteredWindow).CreatedAtUtc);
        Assert.Equal(secondStrategy, Assert.Single(liveAll).StrategyId);
        Assert.Equal(firstStrategy, Assert.Single(liveFiltered).StrategyId);
        Assert.Equal(now.AddMinutes(-3), Assert.Single(liveFilteredSecondPage).CreatedAtUtc);
        Assert.Equal(now.AddMinutes(-2), Assert.Single(liveFilteredWindow).CreatedAtUtc);
        Assert.Empty(liveFilteredWindowSecondPage);
    }

    [Fact]
    public void PostgresRepository_OnChainPaperSignalHotQuery_UsesInMemoryCaptures()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetOnChainPaperSignalCandidatesForCapturesAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetPolymarketOnChainSignalCandidateSourcesAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var hotQuery = source[start..end];
        Assert.Contains("jsonb_to_recordset(CAST(@CapturesJson AS jsonb))", hotQuery, StringComparison.Ordinal);
        Assert.Contains("WITH hot_captures AS MATERIALIZED", hotQuery, StringComparison.Ordinal);
        Assert.Contains("maker_processed.participant_role = 'Maker'", hotQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM polymarket_onchain_trade_captures capture", hotQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_AcceptedOnChainPaperOrder_UsesSingleTransaction()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("AddAcceptedOnChainPaperOrderAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetOnChainPaperSignalCandidatesForCapturesAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("BeginTransactionAsync", method, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO signals", method, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_orders", method, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_copied_leader_positions", method, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO polymarket_onchain_paper_signal_results", method, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_GammaMarketUpsert_SkipsUnchangedPayloadRows()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("INSERT INTO polymarket_gamma_markets", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("AddPolymarketGammaMarketParameters", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var gammaUpsertSql = source[start..end];
        Assert.Contains("ON CONFLICT (market_id) DO UPDATE SET", gammaUpsertSql, StringComparison.Ordinal);
        Assert.Contains(
            "WHERE\\n    polymarket_gamma_markets.condition_id IS DISTINCT FROM excluded.condition_id",
            gammaUpsertSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_gamma_markets.raw_json IS DISTINCT FROM excluded.raw_json",
            gammaUpsertSql,
            StringComparison.Ordinal);
        Assert.Contains("fetched_at_utc = excluded.fetched_at_utc\\nWHERE", gammaUpsertSql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "polymarket_gamma_markets.fetched_at_utc IS DISTINCT FROM excluded.fetched_at_utc",
            gammaUpsertSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_DataApiTraderUpsert_ThrottlesSeenOnlyRows()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("INSERT INTO polymarket_data_api_traders", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetPolymarketDataApiTradersForSyncAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var traderUpsertSql = source[start..end];
        Assert.Contains("ON CONFLICT (wallet) DO UPDATE SET", traderUpsertSql, StringComparison.Ordinal);
        Assert.Contains("updated_at_utc = excluded.updated_at_utc\\nWHERE", traderUpsertSql, StringComparison.Ordinal);
        Assert.Contains(
            "excluded.last_trade_timestamp_utc > polymarket_data_api_traders.last_trade_timestamp_utc",
            traderUpsertSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_data_api_traders.last_seen_at_utc <= excluded.last_seen_at_utc - interval '5 minutes'",
            traderUpsertSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_data_api_traders.last_global_seen_at_utc <= excluded.last_global_seen_at_utc - interval '5 minutes'",
            traderUpsertSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_StatusUpserts_ThrottleClockOnlyRows()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains(
            "market_data_status.updated_at_utc <= excluded.updated_at_utc - interval '60 seconds'",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "scanner_status.updated_at_utc <= excluded.updated_at_utc - interval '60 seconds'",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "service_heartbeats.last_heartbeat_utc <= excluded.last_heartbeat_utc - interval '60 seconds'",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_DataApiPerformance_PopulatesPolymarketPositionBenchmarks()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains(
            "UPDATE polymarket_data_api_wallet_performance",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "UPDATE polymarket_data_api_wallet_category_performance",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_positions_total_pnl_usd = total_pnl_usd",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_positions_refreshed_at_utc = refreshed_at_utc",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_CategoryMappingLookup_FindsMissingDataApiCategories()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains(
            "GetMissingPolymarketLeaderboardCategoryMappingsAsync",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "LEFT JOIN polymarket_category_mappings mapping",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "mapping.local_category IS NULL",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_PolymarketOnlyRatings_UsesDedicatedTableAndRefreshCursor()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains(
            "GetPolymarketDataApiTradersForRatingRefreshAsync",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "polymarket_rating_next_refresh_at_utc <= @DueBeforeUtc",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "INSERT INTO polymarket_data_api_wallet_category_ratings",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "current_positions_count",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "leaderboard_pnl_to_volume_pct",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "LeaderboardPnlToVolumePct",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "PositionsTotalPnlUsd",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "positions_refreshed_at_utc",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "MarkPolymarketDataApiTraderRatingRefreshedAsync",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_PaperCopiedTraderPerformance_UsesBoundedWalletProjection()
    {
        var source = ReadStorageRepositorySource().Replace("\r\n", "\n", StringComparison.Ordinal);
        var telemetrySource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresPaperPositionsScanTelemetry.cs");
        var refreshStart = source.IndexOf(
            "public async Task<PaperCopiedTraderPerformanceRefreshResult> RefreshPaperCopiedTraderPerformanceProjectionAsync",
            StringComparison.Ordinal);
        Assert.True(refreshStart >= 0);
        var refreshEnd = source.IndexOf(
            "public async Task<IReadOnlyList<PaperCopiedTraderPerformance>> GetPaperCopiedTraderPerformanceAsync",
            refreshStart,
            StringComparison.Ordinal);
        Assert.True(refreshEnd > refreshStart);
        var helpersStart = source.IndexOf(
            "private static async Task<int> RecoverPaperCopiedTraderPerformanceInflightAsync",
            refreshEnd,
            StringComparison.Ordinal);
        Assert.True(helpersStart > refreshEnd);
        var helpersEnd = source.IndexOf(
            "private static DateTime UtcDateTime",
            helpersStart,
            StringComparison.Ordinal);
        Assert.True(helpersEnd > helpersStart);
        var refreshSource = source[refreshStart..refreshEnd];
        var helperSource = source[helpersStart..helpersEnd];

        Assert.Contains("TryAddPaperPositionSettlementAsync", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_position_settlements", source, StringComparison.Ordinal);
        Assert.Contains("TryAcquirePaperCopiedTraderPerformanceRefreshLockAsync", refreshSource, StringComparison.Ordinal);
        Assert.Contains("ReleasePaperCopiedTraderPerformanceRefreshLockAsync", refreshSource, StringComparison.Ordinal);
        Assert.Contains("RecoverPaperCopiedTraderPerformanceInflightAsync", refreshSource, StringComparison.Ordinal);
        Assert.Contains("ClaimPaperCopiedTraderPerformanceQueueAsync", refreshSource, StringComparison.Ordinal);
        Assert.Equal(2, refreshSource.Split("IsolationLevel.ReadCommitted", StringSplitOptions.None).Length - 1);
        var claimCommit = refreshSource.IndexOf("await claimTransaction.CommitAsync", StringComparison.Ordinal);
        var projectionTransaction = refreshSource.IndexOf("await using NpgsqlTransaction transaction", StringComparison.Ordinal);
        var highRecovery = refreshSource.IndexOf("highPriorityWalletsProcessed = await Recover", StringComparison.Ordinal);
        var highClaim = refreshSource.IndexOf("highPriorityWalletsProcessed += await Claim", StringComparison.Ordinal);
        var reconciliationRecovery = refreshSource.IndexOf("reconciliationWalletsProcessed = await Recover", StringComparison.Ordinal);
        var reconciliationClaim = refreshSource.IndexOf("reconciliationWalletsProcessed += await Claim", StringComparison.Ordinal);
        Assert.True(claimCommit >= 0 && projectionTransaction > claimCommit);
        Assert.True(highRecovery >= 0 && highClaim > highRecovery);
        Assert.True(reconciliationRecovery >= 0 && reconciliationClaim > reconciliationRecovery);
        Assert.Contains("pg_try_advisory_lock", helperSource, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_unlock", helperSource, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE OF queue SKIP LOCKED", helperSource, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM paper_copied_trader_performance_refresh_queue queue", helperSource, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_copied_trader_performance_refresh_inflight", helperSource, StringComparison.Ordinal);
        Assert.Contains("\"high_priority\" => \"queue.priority > 0\"", helperSource, StringComparison.Ordinal);
        Assert.Contains("\"reconciliation\" => \"queue.priority <= 0\"", helperSource, StringComparison.Ordinal);
        Assert.Contains("Math.Min(reconciliationSeedWalletBatchSize, reconciliationCapacity)", refreshSource, StringComparison.Ordinal);
        Assert.Contains("reconciliationWalletBatchSize - reconciliationWalletsProcessed", refreshSource, StringComparison.Ordinal);
        Assert.Contains("queued_wallet.copied_trader_wallet = paper_order.copied_trader_wallet", refreshSource, StringComparison.Ordinal);
        Assert.Contains("queued_wallet.copied_trader_wallet = paper_position.copied_trader_wallet", refreshSource, StringComparison.Ordinal);
        Assert.Contains("queued_wallet.copied_trader_wallet = paper_settlement.copied_trader_wallet", refreshSource, StringComparison.Ordinal);
        Assert.Contains("queued_wallet.copied_trader_wallet = performance.copied_trader_wallet", refreshSource, StringComparison.Ordinal);
        Assert.Contains("SELECT copied_trader_wallet, 'reconciliation'\n    FROM claimed", refreshSource, StringComparison.Ordinal);
        Assert.Contains("(SELECT count(*)::integer FROM selected) AS wallets_selected", refreshSource, StringComparison.Ordinal);
        Assert.DoesNotContain("pickSeededReconciliationCommand", refreshSource, StringComparison.Ordinal);
        Assert.Contains("highPriorityWalletsProcessed + reconciliationWalletsProcessed", refreshSource, StringComparison.Ordinal);
        var selectedWalletCount = refreshSource.IndexOf(
            "(SELECT count(*)::integer FROM selected) AS wallets_selected",
            StringComparison.Ordinal);
        var selectionAnalyze = refreshSource.IndexOf(
            "ANALYZE pg_temp.temp_paper_copied_trader_performance_wallets;",
            StringComparison.Ordinal);
        var projectionDelete = refreshSource.IndexOf(
            "DELETE FROM paper_copied_trader_performance performance",
            StringComparison.Ordinal);
        var projectionAggregate = refreshSource.IndexOf("WITH event_rows AS (", StringComparison.Ordinal);
        var projectionAggregateTimeout = refreshSource.IndexOf(
            "command.CommandTimeout = PaperCopiedTraderPerformanceCommandTimeoutSeconds;",
            projectionAggregate,
            StringComparison.Ordinal);
        var projectionAggregateExecute = refreshSource.IndexOf(
            "performanceRowsWritten = Convert.ToInt32(await command.ExecuteScalarAsync",
            projectionAggregate,
            StringComparison.Ordinal);
        var positionBranchFrom = refreshSource.IndexOf(
            "    FROM paper_positions pp",
            projectionAggregate,
            StringComparison.Ordinal);
        var positionBranchStart = refreshSource.LastIndexOf(
            "    SELECT\n",
            positionBranchFrom,
            StringComparison.Ordinal);
        var positionBranchEnd = refreshSource.IndexOf(
            "\n\n    UNION ALL",
            positionBranchFrom,
            StringComparison.Ordinal);
        var statsBeforeSeed = refreshSource.IndexOf(
            "paperPositionsStatsBeforeSeed = await PostgresPaperPositionsScanTelemetry.ReadAsync",
            StringComparison.Ordinal);
        var seedProjection = refreshSource.IndexOf(
            "WITH source_wallets AS (",
            statsBeforeSeed,
            StringComparison.Ordinal);
        var statsAfterSeed = refreshSource.IndexOf(
            "paperPositionsStatsAfterSeed = await PostgresPaperPositionsScanTelemetry.ReadAsync",
            StringComparison.Ordinal);
        var statsAfterAggregation = refreshSource.IndexOf(
            "paperPositionsStatsAfterAggregation = await PostgresPaperPositionsScanTelemetry.ReadAsync",
            StringComparison.Ordinal);
        var projectionCommit = refreshSource.IndexOf(
            "await transaction.CommitAsync",
            statsAfterAggregation,
            StringComparison.Ordinal);
        Assert.True(
            selectedWalletCount >= 0
            && selectionAnalyze > selectedWalletCount
            && projectionDelete > selectionAnalyze
            && projectionAggregate > projectionDelete);
        Assert.True(
            projectionAggregateTimeout > projectionAggregate
            && projectionAggregateExecute > projectionAggregateTimeout);
        Assert.Equal(
            1,
            source.Split(
                "private const int PaperCopiedTraderPerformanceCommandTimeoutSeconds = 180;",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            refreshSource.Split(
                "command.CommandTimeout = PaperCopiedTraderPerformanceCommandTimeoutSeconds;",
                StringSplitOptions.None).Length - 1);
        Assert.True(
            positionBranchFrom > projectionAggregate
            && positionBranchStart >= projectionAggregate
            && positionBranchEnd > positionBranchFrom);
        var positionBranch = refreshSource[positionBranchStart..positionBranchEnd];
        Assert.Contains(
            """
                    0, 0, 0, 0,
                    1,
                    0, 0, 0,
                    0, 0, 0, 0,
                    pp.unrealized_pnl_usd,
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            positionBranch,
            StringComparison.Ordinal);
        Assert.Contains(
            """
                WHERE pp.copied_trader_wallet <> ''
                  AND pp.size_shares > 0
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            positionBranch,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CASE WHEN pp.size_shares > 0", positionBranch, StringComparison.Ordinal);
        Assert.True(
            statsBeforeSeed >= 0
            && seedProjection > statsBeforeSeed
            && statsAfterSeed > seedProjection
            && projectionAggregate > statsAfterSeed
            && statsAfterAggregation > projectionAggregate
            && projectionCommit > statsAfterAggregation);
        Assert.Equal(
            1,
            refreshSource.Split(
                "ANALYZE pg_temp.temp_paper_copied_trader_performance_wallets;",
                StringSplitOptions.None).Length - 1);
        Assert.Contains("HighPriorityQueueRemaining", refreshSource, StringComparison.Ordinal);
        Assert.Contains("ReconciliationQueueRemaining", refreshSource, StringComparison.Ordinal);
        Assert.Contains("temp_paper_copied_trader_performance_wallets", refreshSource, StringComparison.Ordinal);
        Assert.Contains("work_kind text NOT NULL CHECK (work_kind IN ('high_priority', 'reconciliation'))", refreshSource, StringComparison.Ordinal);
        Assert.Contains("USING temp_paper_copied_trader_performance_wallets selected", refreshSource, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM paper_copied_trader_performance_refresh_inflight inflight", refreshSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM paper_copied_trader_performance;", refreshSource, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_copied_trader_performance", refreshSource, StringComparison.Ordinal);
        Assert.Contains("Task<PaperCopiedTraderPerformance?> GetPaperCopiedTraderPerformanceAsync", source, StringComparison.Ordinal);
        Assert.Contains("greatest(0, least(100", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH deleted AS", source, StringComparison.Ordinal);
        Assert.Contains("FROM paper_position_settlements ps", source, StringComparison.Ordinal);
        Assert.Contains("JOIN temp_paper_copied_trader_performance_wallets selected", source, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN LATERAL", source, StringComparison.Ordinal);
        Assert.Contains("ORDER BY market.fetched_at_utc DESC, market.market_id", source, StringComparison.Ordinal);
        Assert.Contains("WHERE NULLIF(ps.category, '') IS NULL", refreshSource, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN GROUPING(category) = 1 THEN 'OVERALL' ELSE category END AS category", refreshSource, StringComparison.Ordinal);
        Assert.Contains("GROUP BY GROUPING SETS (", refreshSource, StringComparison.Ordinal);
        Assert.Equal(1, refreshSource.Split("FROM event_rows", StringSplitOptions.None).Length - 1);
        Assert.Contains("COALESCE((SELECT sum(ps.realized_pnl_usd) FROM paper_position_settlements ps), 0) AS paper_pnl", source, StringComparison.Ordinal);
        Assert.Contains("PaperPositionsSeedSequentialScans", refreshSource, StringComparison.Ordinal);
        Assert.Contains("PaperPositionsSeedSequentialTuplesRead", refreshSource, StringComparison.Ordinal);
        Assert.Contains("PaperPositionsAggregationSequentialScans", refreshSource, StringComparison.Ordinal);
        Assert.Contains("PaperPositionsAggregationSequentialTuplesRead", refreshSource, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_stat_xact_user_tables", telemetrySource, StringComparison.Ordinal);
        Assert.Contains("'public.paper_positions'::regclass", telemetrySource, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_PaperPositionsUseStablePagingOrder()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetPaperPositionsAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("TryAddPaperPositionSettlementAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains(
            "ORDER BY updated_at_utc DESC, copied_trader_wallet ASC, asset_id ASC;",
            method,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_OpenPaperPositionsAndPointLookupAreServerSide()
    {
        var source = ReadStorageRepositorySource();
        var openStart = source.IndexOf("GetOpenPaperPositionsAsync", StringComparison.Ordinal);
        var pointStart = source.IndexOf("GetPaperPositionAsync", openStart, StringComparison.Ordinal);
        var end = source.IndexOf("TryAddPaperPositionSettlementAsync", pointStart, StringComparison.Ordinal);
        Assert.True(openStart >= 0);
        Assert.True(pointStart > openStart);
        Assert.True(end > pointStart);

        var openMethod = source[openStart..pointStart];
        Assert.Contains("WHERE size_shares > 0", openMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPaperPositionsAsync", openMethod, StringComparison.Ordinal);

        var pointMethod = source[pointStart..end];
        Assert.Contains("copied_trader_wallet = @CopiedTraderWallet", pointMethod, StringComparison.Ordinal);
        Assert.Contains("asset_id = @AssetId", pointMethod, StringComparison.Ordinal);
        Assert.Contains("LIMIT 1", pointMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_MarketPaperPositionsAndSettlementPersistenceAreSetBased()
    {
        var source = ReadStorageRepositorySource();
        var marketStart = source.IndexOf("GetOpenPaperPositionsForMarketAsync", StringComparison.Ordinal);
        var marketEnd = source.IndexOf("GetPaperPositionAsync", marketStart, StringComparison.Ordinal);
        Assert.True(marketStart >= 0);
        Assert.True(marketEnd > marketStart);

        var marketMethod = source[marketStart..marketEnd];
        Assert.Contains("WHERE size_shares > 0", marketMethod, StringComparison.Ordinal);
        Assert.Contains("lower(condition_id) = lower(@ConditionId)", marketMethod, StringComparison.Ordinal);
        Assert.Contains("lower(asset_id) = lower(@AssetId)", marketMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOpenPaperPositionsAsync", marketMethod, StringComparison.Ordinal);

        var batchStart = source.IndexOf("PersistPaperPositionSettlementBatchAsync", StringComparison.Ordinal);
        var batchEnd = source.IndexOf("GetRecentPaperPositionSettlementsAsync", batchStart, StringComparison.Ordinal);
        Assert.True(batchStart >= 0);
        Assert.True(batchEnd > batchStart);

        var batchMethod = source[batchStart..batchEnd];
        Assert.Contains("BeginTransactionAsync", batchMethod, StringComparison.Ordinal);
        Assert.Contains("jsonb_to_recordset", batchMethod, StringComparison.Ordinal);
        Assert.Contains("LockPaperPositionKeysAsync", batchMethod, StringComparison.Ordinal);
        Assert.Contains("UpsertPaperPositionsBatchAsync", batchMethod, StringComparison.Ordinal);
        Assert.Contains("transaction.CommitAsync", batchMethod, StringComparison.Ordinal);
        Assert.True(
            batchMethod.IndexOf("LockPaperPositionKeysAsync", StringComparison.Ordinal) <
            batchMethod.IndexOf("UpsertPaperPositionsBatchAsync", StringComparison.Ordinal));
        Assert.True(
            batchMethod.IndexOf("UpsertPaperPositionsBatchAsync", StringComparison.Ordinal) <
            batchMethod.IndexOf("AddPaperPositionSettlementsBatchAsync", StringComparison.Ordinal));

        var entryStart = source.IndexOf("AddPaperEntryPersistenceBatchAsync", StringComparison.Ordinal);
        var entryEnd = source.IndexOf("private static async Task AddSignalsBatchAsync", entryStart, StringComparison.Ordinal);
        Assert.True(entryStart >= 0);
        Assert.True(entryEnd > entryStart);

        var entryMethod = source[entryStart..entryEnd];
        var entryLock = entryMethod.IndexOf("LockPaperPositionKeysAsync", StringComparison.Ordinal);
        var entrySignal = entryMethod.IndexOf("AddSignalsBatchAsync", StringComparison.Ordinal);
        var entryPosition = entryMethod.IndexOf("UpsertPaperPositionsBatchAsync", StringComparison.Ordinal);
        var entryOrder = entryMethod.IndexOf("AddPaperOrdersBatchAsync", StringComparison.Ordinal);
        var entryFill = entryMethod.IndexOf("AddPaperFillsBatchAsync", StringComparison.Ordinal);
        Assert.True(entryLock >= 0 && entrySignal > entryLock);
        Assert.True(entryPosition > entrySignal);
        Assert.True(entryOrder > entryPosition);
        Assert.True(entryFill > entryOrder);

        Assert.Contains("target_position.copied_trader_wallet COLLATE \"C\"", source, StringComparison.Ordinal);
        Assert.Contains("paper_order.copied_trader_wallet COLLATE \"C\"", source, StringComparison.Ordinal);
        Assert.Contains("settlement.copied_trader_wallet COLLATE \"C\"", source, StringComparison.Ordinal);
        Assert.Contains("hashtextextended(copied_trader_wallet, 4937427318840178337)", source, StringComparison.Ordinal);
        Assert.Contains("ORDER BY lock_key", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_SingleSettlementUsesWalletLockTransactionContract()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("public async Task<bool> TryAddPaperPositionSettlementAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("public async Task<int> PersistPaperPositionSettlementBatchAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var method = source[start..end];
        var begin = method.IndexOf("await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken)", StringComparison.Ordinal);
        var walletLock = method.IndexOf("await LockPaperWalletsAsync(connection, transaction, [settlement.CopiedTraderWallet], cancellationToken)", StringComparison.Ordinal);
        var insert = method.IndexOf("INSERT INTO paper_position_settlements", StringComparison.Ordinal);
        var binding = method.IndexOf("command.Transaction = transaction", StringComparison.Ordinal);
        var execute = method.IndexOf("bool inserted = await command.ExecuteScalarAsync(cancellationToken) is not null", StringComparison.Ordinal);
        var commit = method.IndexOf("await transaction.CommitAsync(cancellationToken)", StringComparison.Ordinal);
        var result = method.IndexOf("return inserted", StringComparison.Ordinal);
        Assert.True(begin >= 0 && walletLock > begin && insert > walletLock);
        Assert.True(binding > insert && execute > binding && commit > execute && result > commit);
        Assert.Contains("ON CONFLICT (copied_trader_wallet, asset_id) DO NOTHING", method, StringComparison.Ordinal);
        Assert.Contains("NullableDecimal(settlement.NetRealizedPnlUsd)", method, StringComparison.Ordinal);
        Assert.Contains("NullableDateTime(settlement.FeeCalculatedAtUtc)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_positions", method, StringComparison.Ordinal);
        Assert.DoesNotContain("LockPaperPositionKeysAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("catch", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_SinglePaperPositionMutationsUseWalletLockTransactionContract()
    {
        var source = ReadStorageRepositorySource();
        var upsertStart = source.IndexOf("UpsertPaperPositionAsync", StringComparison.Ordinal);
        var markStart = source.IndexOf("TryUpdatePaperPositionMarkAsync", upsertStart, StringComparison.Ordinal);
        var batchMarkStart = source.IndexOf("TryUpdatePaperPositionMarksAsync", markStart, StringComparison.Ordinal);
        Assert.True(upsertStart >= 0);
        Assert.True(markStart > upsertStart);
        Assert.True(batchMarkStart > markStart);

        var upsertMethod = source[upsertStart..markStart];
        var upsertTransaction = upsertMethod.IndexOf("BeginTransactionAsync", StringComparison.Ordinal);
        var upsertLock = upsertMethod.IndexOf("LockPaperPositionKeysAsync", StringComparison.Ordinal);
        var upsertWrite = upsertMethod.IndexOf("INSERT INTO paper_positions", StringComparison.Ordinal);
        var upsertCommit = upsertMethod.IndexOf("transaction.CommitAsync", StringComparison.Ordinal);
        Assert.True(upsertTransaction >= 0 && upsertLock > upsertTransaction);
        Assert.True(upsertWrite > upsertLock && upsertCommit > upsertWrite);
        Assert.Contains("command.Transaction = transaction", upsertMethod, StringComparison.Ordinal);

        var markMethod = source[markStart..batchMarkStart];
        var markTransaction = markMethod.IndexOf("BeginTransactionAsync", StringComparison.Ordinal);
        var markLock = markMethod.IndexOf("LockPaperPositionKeysAsync", StringComparison.Ordinal);
        var markWrite = markMethod.IndexOf("UPDATE paper_positions", StringComparison.Ordinal);
        var markCommit = markMethod.IndexOf("transaction.CommitAsync", StringComparison.Ordinal);
        Assert.True(markTransaction >= 0 && markLock > markTransaction);
        Assert.True(markWrite > markLock && markCommit > markWrite);
        Assert.Contains("command.Transaction = transaction", markMethod, StringComparison.Ordinal);
        Assert.Contains("net_unrealized_pnl_usd = @NetUnrealizedPnlUsd", markMethod, StringComparison.Ordinal);
        Assert.Contains("IS NOT DISTINCT FROM @ExpectedNetUnrealizedPnlUsd", markMethod, StringComparison.Ordinal);

        var batchMarkEnd = source.IndexOf("UpsertPaperPositionsAsync", batchMarkStart, StringComparison.Ordinal);
        Assert.True(batchMarkEnd > batchMarkStart);
        var batchMarkMethod = source[batchMarkStart..batchMarkEnd];
        Assert.DoesNotContain("BeginTransactionAsync", batchMarkMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("LockPaperPositionKeysAsync", batchMarkMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("command.Transaction = transaction", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("eligible_positions AS MATERIALIZED", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE OF target_position SKIP LOCKED", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("target_position.copied_trader_wallet COLLATE \"C\"", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("target_position.asset_id COLLATE \"C\"", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("target_position.condition_id = mark_update.expected_condition_id", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("target_position.outcome = mark_update.expected_outcome", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("target_position.size_shares = mark_update.expected_size_shares", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("target_position.average_price = mark_update.expected_average_price", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("target_position.estimated_value_usd = mark_update.expected_estimated_value_usd", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("target_position.unrealized_pnl_usd = mark_update.expected_unrealized_pnl_usd", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("target_position.updated_at_utc = mark_update.expected_updated_at_utc", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("net_unrealized_pnl_usd = update.NetUnrealizedPnlUsd", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("net_unrealized_pnl_usd = eligible_position.net_unrealized_pnl_usd", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("expected_net_unrealized_pnl_usd numeric", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("IS NOT DISTINCT FROM mark_update.expected_net_unrealized_pnl_usd", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("PaperPositionMarkPersistenceStages.OpenConnection", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("PaperPositionMarkPersistenceStages.SerializeUpdates", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("PaperPositionMarkPersistenceStages.ExecuteCommand", batchMarkMethod, StringComparison.Ordinal);
        Assert.Contains("PaperPositionMarkPersistenceStages.ReadResults", batchMarkMethod, StringComparison.Ordinal);
        Assert.True(
            batchMarkMethod.IndexOf("PaperPositionMarkPersistenceStages.OpenConnection", StringComparison.Ordinal) <
            batchMarkMethod.IndexOf("PaperPositionMarkPersistenceStages.SerializeUpdates", StringComparison.Ordinal));
        Assert.True(
            batchMarkMethod.IndexOf("PaperPositionMarkPersistenceStages.SerializeUpdates", StringComparison.Ordinal) <
            batchMarkMethod.IndexOf("PaperPositionMarkPersistenceStages.ExecuteCommand", StringComparison.Ordinal));
        Assert.True(
            batchMarkMethod.IndexOf("PaperPositionMarkPersistenceStages.ExecuteCommand", StringComparison.Ordinal) <
            batchMarkMethod.IndexOf("PaperPositionMarkPersistenceStages.ReadResults", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PaperPositionMarkStageAwareDefaultOverload_PreservesLegacyRepositoryBehavior()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 31, 6, 0, 0, TimeSpan.Zero);
        var original = new PaperPosition(
            "asset-stage-default",
            "condition-stage-default",
            "Up",
            2m,
            0.40m,
            0.80m,
            0m,
            nowUtc,
            "wallet-stage-default");
        var update = new PaperPositionMarkUpdate(
            original,
            EstimatedValueUsd: 1m,
            UnrealizedPnlUsd: 0.20m,
            UpdatedAtUtc: nowUtc.AddSeconds(1));
        var legacyRepository = new TestAppRepository();
        legacyRepository.PaperPositions.Add(original);
        IAppRepository repository = legacyRepository;
        var observedStages = new List<string>();

        var updated = Assert.Single(await repository.TryUpdatePaperPositionMarksAsync(
            [update],
            observedStages.Add));

        Assert.Empty(observedStages);
        Assert.Equal(1m, updated.EstimatedValueUsd);
        Assert.Equal(0.20m, updated.UnrealizedPnlUsd);
        Assert.Equal(updated, Assert.Single(legacyRepository.PaperPositions));
    }

    [Fact]
    public async Task PaperPositionMarkUpdate_PropagatesNullableNetPnlInTestAndNoOpRepositories()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);
        var original = new PaperPosition(
            "asset-net-mark",
            "condition-net-mark",
            "Up",
            2m,
            0.40m,
            1m,
            0.20m,
            nowUtc,
            "wallet-net-mark",
            FeeUsd: 0.01m,
            FeeAccountingStatus: "Calculated",
            NetUnrealizedPnlUsd: 0.19m);
        var update = new PaperPositionMarkUpdate(
            original,
            EstimatedValueUsd: 1.20m,
            UnrealizedPnlUsd: 0.40m,
            UpdatedAtUtc: nowUtc.AddSeconds(1),
            NetUnrealizedPnlUsd: 0.39m);
        var repository = new TestAppRepository();
        repository.PaperPositions.Add(original);

        var updated = Assert.Single(await repository.TryUpdatePaperPositionMarksAsync([update]));

        Assert.Equal(0.39m, updated.NetUnrealizedPnlUsd);
        Assert.Equal(0.39m, Assert.Single(repository.PaperPositions).NetUnrealizedPnlUsd);
        Assert.True(await repository.TryUpdatePaperPositionMarkAsync(
            updated,
            estimatedValueUsd: 1.40m,
            unrealizedPnlUsd: 0.60m,
            netUnrealizedPnlUsd: 0.59m,
            updatedAtUtc: nowUtc.AddSeconds(2)));
        Assert.Equal(0.59m, Assert.Single(repository.PaperPositions).NetUnrealizedPnlUsd);
        Assert.Empty(await new NoOpAppRepository().TryUpdatePaperPositionMarksAsync([update]));
        Assert.False(await new NoOpAppRepository().TryUpdatePaperPositionMarkAsync(
            original,
            estimatedValueUsd: 1.20m,
            unrealizedPnlUsd: 0.40m,
            netUnrealizedPnlUsd: 0.39m,
            updatedAtUtc: nowUtc.AddSeconds(1)));
    }

    [Fact]
    public void PaperCopiedTraderPerformancePositionTriggers_OrderWalletQueueLocks()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PaperCopiedTraderPerformanceProjectionSchema.cs");

        Assert.Equal(
            3,
            source.Split(
                "ORDER BY wallets.copied_trader_wallet COLLATE \"C\"",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task PostgresRepository_SettlementBatchRejectsMismatchedPositionKeyBeforeOpeningConnection()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var settlement = new PaperPositionSettlement(
            Guid.NewGuid(),
            "settlement-wallet",
            "settlement-asset",
            "settlement-condition",
            "Yes",
            "settlement-asset",
            "Yes",
            "IntegrationTest",
            2m,
            0.40m,
            0.80m,
            2m,
            1.20m,
            true,
            "IntegrationTest",
            nowUtc,
            nowUtc);
        var mismatchedPosition = new PaperPosition(
            "different-asset",
            settlement.ConditionId,
            settlement.Outcome,
            0m,
            0m,
            0m,
            0m,
            nowUtc,
            settlement.CopiedTraderWallet);
        var repository = new PostgresAppRepository(new PostgresConnectionFactory(new StorageOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1"
        }));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.PersistPaperPositionSettlementBatchAsync(
                [new PaperPositionSettlementWrite(settlement, mismatchedPosition)]));

        Assert.Equal("writes", exception.ParamName);
    }

    [Fact]
    public async Task PostgresRepository_EntryBatchRejectsFillOutsideBatchBeforeOpeningConnection()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var repository = CreateUnreachablePostgresRepository();
        var batch = new PaperEntryPersistenceBatch(
            [],
            [],
            [new PaperFill(Guid.NewGuid(), Guid.NewGuid(), 0.40m, 2m, nowUtc, "contract test")],
            [],
            [],
            []);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.AddPaperEntryPersistenceBatchAsync(batch));

        Assert.Equal("batch", exception.ParamName);
    }

    [Fact]
    public async Task PostgresRepository_EntryBatchRejectsActivationOutsideBatchBeforeOpeningConnection()
    {
        var repository = CreateUnreachablePostgresRepository();
        var batch = new PaperEntryPersistenceBatch(
            [],
            [],
            [],
            [],
            [new PaperCopiedLeaderPositionActivation(Guid.NewGuid(), 2m, DateTimeOffset.UtcNow)],
            []);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.AddPaperEntryPersistenceBatchAsync(batch));

        Assert.Equal("batch", exception.ParamName);
    }

    [Fact]
    public void PostgresRepository_LeaderExitLocksWalletBeforeEventLeaderAndQueueWrites()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("ApplyPaperCopiedLeaderExitAsync", StringComparison.Ordinal);
        var end = source.IndexOf("AddDryRunOrderAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);

        var method = source[start..end];
        var walletLock = method.IndexOf("LockPaperWalletsAsync", StringComparison.Ordinal);
        var eventInsert = method.IndexOf("INSERT INTO paper_copied_leader_activity_events", StringComparison.Ordinal);
        var positionValidation = method.IndexOf("Every copied-leader position update", StringComparison.Ordinal);
        var leaderUpdate = method.IndexOf("UPDATE paper_copied_leader_positions", StringComparison.Ordinal);
        var queueWrite = method.IndexOf("INSERT INTO paper_orders", StringComparison.Ordinal);
        Assert.True(walletLock >= 0 && eventInsert > walletLock);
        Assert.True(positionValidation > eventInsert);
        Assert.Contains(
            "bool_and(lower(copied_trader_wallet) = lower(@CopiedTraderWallet))",
            method,
            StringComparison.Ordinal);
        Assert.True(leaderUpdate > positionValidation);
        Assert.True(queueWrite > leaderUpdate);
    }

    [Fact]
    public void PostgresRepository_RecentSignalsLimitsBeforeRejectionAggregation()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetRecentSignalsAsync", StringComparison.Ordinal);
        var end = source.IndexOf("AddSignalRejectionAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("WITH recent_signals AS MATERIALIZED", method, StringComparison.Ordinal);
        Assert.Contains("ORDER BY created_at_utc DESC, id DESC", method, StringComparison.Ordinal);
        Assert.True(
            method.IndexOf("LIMIT @Limit", StringComparison.Ordinal) <
            method.IndexOf("FROM signal_rejections", StringComparison.Ordinal));
        Assert.DoesNotContain("LEFT JOIN signal_rejections sr ON sr.signal_id = s.id", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_HasOpenExposureAndRecentSignalIndexes()
    {
        var source = ReadRepositorySource("src", "PolyCopyTrader.Storage", "PostgresSchema.cs");
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var openWalletIndex = Assert.Single(statements, statement =>
            statement.StartsWith(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_positions_open_wallet",
                StringComparison.Ordinal));
        openWalletIndex = openWalletIndex.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("ix_signals_created_time", source, StringComparison.Ordinal);
        Assert.Contains("ON signals(created_at_utc DESC, id DESC)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ix_signals_trader_wallet_id", source, StringComparison.Ordinal);
        Assert.Contains("ix_paper_orders_open_time_asset", source, StringComparison.Ordinal);
        Assert.Contains("WHERE status IN ('Pending', 'PartiallyFilled')", source, StringComparison.Ordinal);
        Assert.Contains("ix_paper_positions_open_updated_cover", source, StringComparison.Ordinal);
        Assert.Contains("WHERE size_shares > 0", source, StringComparison.Ordinal);
        Assert.Equal(
            """
            CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_positions_open_wallet
            ON paper_positions(copied_trader_wallet)
            WHERE size_shares > 0;
            """,
            openWalletIndex);
        Assert.DoesNotContain("INCLUDE", openWalletIndex, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgresSchema_HasCaseInsensitiveOpenPositionSettlementLookupIndexes()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
        var conditionIndex = Assert.Single(statements, statement =>
            statement.StartsWith(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_positions_open_condition_lookup",
                StringComparison.Ordinal));
        var assetIndex = Assert.Single(statements, statement =>
            statement.StartsWith(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_positions_open_asset_lookup",
                StringComparison.Ordinal));

        Assert.Contains(
            "ON paper_positions(lower(condition_id), updated_at_utc DESC, copied_trader_wallet, asset_id)",
            conditionIndex,
            StringComparison.Ordinal);
        Assert.Contains(
            "ON paper_positions(lower(asset_id), updated_at_utc DESC, copied_trader_wallet, asset_id)",
            assetIndex,
            StringComparison.Ordinal);
        Assert.Contains(
            "INCLUDE (condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd)",
            conditionIndex,
            StringComparison.Ordinal);
        Assert.Contains(
            "INCLUDE (condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd)",
            assetIndex,
            StringComparison.Ordinal);
        Assert.Contains("WHERE size_shares > 0", conditionIndex, StringComparison.Ordinal);
        Assert.Contains("WHERE size_shares > 0", assetIndex, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_HasIncrementalPaperCopiedTraderPerformanceProjectionContract()
    {
        var source = PaperCopiedTraderPerformanceProjectionSchema.SchemaSql;
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(source);

        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_copied_trader_performance_refresh_queue", source, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_copied_trader_performance_refresh_inflight", source, StringComparison.Ordinal);
        Assert.Contains("CHECK (btrim(copied_trader_wallet) <> '')", source, StringComparison.Ordinal);
        Assert.Contains("CHECK (work_kind IN ('high_priority', 'reconciliation'))", source, StringComparison.Ordinal);
        Assert.Contains("work_kind = 'high_priority' AND priority > 0", source, StringComparison.Ordinal);
        Assert.Contains("work_kind = 'reconciliation' AND priority <= 0", source, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS paper_copied_trader_performance_projection_control", source, StringComparison.Ordinal);
        Assert.Contains("reconciliation_cursor_wallet text NULL", source, StringComparison.Ordinal);
        Assert.Contains("priority = EXCLUDED.priority", source, StringComparison.Ordinal);
        Assert.Contains("WHERE paper_copied_trader_performance_refresh_queue.priority < EXCLUDED.priority", source, StringComparison.Ordinal);
        Assert.Contains("REFERENCING NEW TABLE AS new_paper_positions", source, StringComparison.Ordinal);
        Assert.Contains("REFERENCING OLD TABLE AS old_paper_positions NEW TABLE AS new_paper_positions", source, StringComparison.Ordinal);
        Assert.Contains("SELECT DISTINCT changed.copied_trader_wallet", source, StringComparison.Ordinal);
        Assert.Contains("id,", source, StringComparison.Ordinal);
        Assert.Contains(statements, statement => statement.Contains(
            "AFTER INSERT OR UPDATE OR DELETE ON public.paper_orders",
            StringComparison.Ordinal));
        Assert.Contains(statements, statement => statement.Contains(
            "AFTER INSERT OR UPDATE OR DELETE ON public.paper_fills",
            StringComparison.Ordinal));
        Assert.Contains(statements, statement => statement.Contains(
            "AFTER INSERT OR UPDATE OR DELETE ON public.paper_position_settlements",
            StringComparison.Ordinal));
        Assert.DoesNotContain("SELECT DISTINCT\n        position_row.copied_trader_wallet,\n        100,\n        clock_timestamp()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_StrategyRecentPerformanceUsesLiveFlagAsEffectiveLive()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetStrategyRecentPerformanceAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetStrategyEnabledStatesAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains(
            "SELECT id, code, name, live_stakes, live_enabled_at_utc, live_stakes AS effective_live_stakes",
            method,
            StringComparison.Ordinal);
        Assert.Contains("sw.live_stakes,", method, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN sw.effective_live_stakes", method, StringComparison.Ordinal);
        Assert.Contains("run.live_enabled_at_utc IS NOT NULL", method, StringComparison.Ordinal);
        Assert.Contains("run.updated_at_utc >= run.live_enabled_at_utc", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_StrategyPerformanceRoundsCountertrendBpsForDecimalReader()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetStrategyPerformanceAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetLiveRealizedPnlByStrategyAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("round((raw_decision_json ->> 'previous_score_bps')::numeric, 8)::numeric(28,8)", method, StringComparison.Ordinal);
        Assert.Contains("round((raw_decision_json ->> 'previous_score')::numeric * 10000, 8)::numeric(28,8)", method, StringComparison.Ordinal);
        Assert.Contains("round((raw_decision_json ->> 'selected_signal_bps')::numeric, 8)::numeric(28,8)", method, StringComparison.Ordinal);
        Assert.Contains("round(COALESCE(selected_signal_bps, abs(previous_score_bps)), 8)::numeric(28,8) AS signal_bps", method, StringComparison.Ordinal);
        Assert.Contains("COALESCE(round(avg(previous_score_bps), 8), 0)::numeric(28,8) AS avg_countertrend_score_bps", method, StringComparison.Ordinal);
        Assert.Contains("COALESCE(round(avg(signal_bps), 8), 0)::numeric(28,8) AS avg_countertrend_signal_bps", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_LiveRealizedPriorityQueryUsesSettledLiveOrders()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("GetLiveRealizedPnlByStrategyAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("GetStrategyRecentPerformanceAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("FROM live_orders", method, StringComparison.Ordinal);
        Assert.Contains("COALESCE(sum(realized_pnl_usd), 0)", method, StringComparison.Ordinal);
        Assert.Contains("strategy_id = ANY(@StrategyIds)", method, StringComparison.Ordinal);
        Assert.Contains("settled_at_utc IS NOT NULL", method, StringComparison.Ordinal);
        Assert.Contains("realized_pnl_usd IS NOT NULL", method, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy_performance", method, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgresRepository_SetStrategyLiveStakesMaintainsLiveEnabledBoundary()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("SetStrategyLiveStakesAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("SetStrategyPausedAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("live_enabled_at_utc = CASE", method, StringComparison.Ordinal);
        Assert.Contains("WHEN @LiveStakes AND NOT live_stakes THEN @UpdatedAtUtc", method, StringComparison.Ordinal);
        Assert.Contains("WHEN @LiveStakes AND live_enabled_at_utc IS NULL THEN @UpdatedAtUtc", method, StringComparison.Ordinal);
        Assert.Contains("WHEN NOT @LiveStakes THEN NULL", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_SetStrategyStakeAmountsSavesLostCoefficients()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("SetStrategyStakeAmountsAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("SetStrategyLiveAvailableBalanceAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("paper_lost_coeff = @PaperLostCoeff", method, StringComparison.Ordinal);
        Assert.Contains("live_lost_coeff = @LiveLostCoeff", method, StringComparison.Ordinal);
        Assert.Contains("paper_lost_counter = @PaperLostCounter", method, StringComparison.Ordinal);
        Assert.Contains("live_lost_counter = @LiveLostCounter", method, StringComparison.Ordinal);
        Assert.Contains("AND @PaperLostCoeff >= 1", method, StringComparison.Ordinal);
        Assert.Contains("AND @LiveLostCoeff >= 1", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AND @PaperLostCounter >= 0", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AND @LiveLostCounter >= 0", method, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"PaperLostCoeff\", paperLostCoeff)", method, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"LiveLostCoeff\", liveLostCoeff)", method, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"PaperLostCounter\", paperLostCounter)", method, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"LiveLostCounter\", liveLostCounter)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_StrategyLiveAvailableBalanceIsCappedAtOneHundred()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("SetStrategyLiveAvailableBalanceAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("UpdateStrategyLostCounterAfterSettlementAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("live_available_balance = LEAST(100.00, @LiveAvailableBalance)", method, StringComparison.Ordinal);

        start = source.IndexOf("ApplyLiveOrderSettlementToStrategyBalanceAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        end = source.IndexOf("GetRecentLiveOrdersAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        method = source[start..end];
        Assert.Contains("LEAST(100.00, GREATEST(0, live_available_balance + @BalanceRealizedPnlUsd))", method, StringComparison.Ordinal);
        Assert.Contains("realized_pnl_usd = @GrossRealizedPnlUsd", method, StringComparison.Ordinal);
        Assert.Contains("net_realized_pnl_usd = @NetRealizedPnlUsd", method, StringComparison.Ordinal);
        Assert.Contains("balance_effect_applied = @NetRealizedPnlUsd IS NOT NULL", method, StringComparison.Ordinal);
        Assert.Contains("netRealizedPnlUsd!.Value", method, StringComparison.Ordinal);
        Assert.DoesNotContain("netRealizedPnlUsd ?? grossRealizedPnlUsd", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_GrossLiveRoiDenominatorExcludesFees()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains("WHEN filled_notional_usd > 0 THEN filled_notional_usd", source, StringComparison.Ordinal);
        Assert.Contains("WHEN filled_size > 0 THEN price * filled_size", source, StringComparison.Ordinal);
        Assert.Contains("WHEN cost_basis_usd > 0 THEN GREATEST(0, cost_basis_usd - fee_usd)", source, StringComparison.Ordinal);
        Assert.Contains("WHEN live_order.filled_notional_usd > 0 THEN live_order.filled_notional_usd", source, StringComparison.Ordinal);
        Assert.Contains("WHEN live_order.filled_size > 0 THEN live_order.price * live_order.filled_size", source, StringComparison.Ordinal);
        Assert.Contains("WHEN live_order.cost_basis_usd > 0 THEN GREATEST(0, live_order.cost_basis_usd - live_order.fee_usd)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filled_notional_usd + fee_usd", source, StringComparison.Ordinal);
        Assert.DoesNotContain("live_order.filled_notional_usd + live_order.fee_usd", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_UpdateStrategyLostCounterAfterSettlementUpdatesSelectedModeOnly()
    {
        var source = ReadStorageRepositorySource();
        var start = source.IndexOf("UpdateStrategyLostCounterAfterSettlementAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("TryAddPaperCopiedLeaderPositionAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("paper_lost_counter = CASE", method, StringComparison.Ordinal);
        Assert.Contains("WHEN @IsLive THEN paper_lost_counter", method, StringComparison.Ordinal);
        Assert.Contains("WHEN NOT @CounterEnabled THEN 0", method, StringComparison.Ordinal);
        Assert.Contains("WHEN @Won THEN paper_lost_counter - 1", method, StringComparison.Ordinal);
        Assert.Contains("ELSE paper_lost_counter + 1", method, StringComparison.Ordinal);
        Assert.Contains("live_lost_counter = CASE", method, StringComparison.Ordinal);
        Assert.Contains("WHEN NOT @IsLive THEN live_lost_counter", method, StringComparison.Ordinal);
        Assert.Contains("WHEN @Won THEN live_lost_counter - 1", method, StringComparison.Ordinal);
        Assert.Contains("ELSE live_lost_counter + 1", method, StringComparison.Ordinal);
        Assert.Contains("RETURNING paper_lost_counter, live_lost_counter", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresRepository_PaperCopiedLeaderExitTracking_StoresLinksAndDedupedActivity()
    {
        var source = ReadStorageRepositorySource();

        Assert.Contains("TryAddPaperCopiedLeaderPositionAsync", source, StringComparison.Ordinal);
        Assert.Contains("ActivatePaperCopiedLeaderPositionAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetPaperCopiedLeaderPositionsForExitTrackingAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPaperCopiedLeaderExitAsync", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_copied_leader_positions", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_copied_leader_activity_events", source, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (dedup_key) DO NOTHING", source, StringComparison.Ordinal);
        Assert.Contains("UPDATE paper_copied_leader_positions", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO signals", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO paper_orders", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchemaInitializer_SplitsSchemaSqlIntoDebuggableStatements()
    {
        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(PostgresSchema.SchemaSql);

        Assert.True(statements.Count > PostgresSchema.RequiredTables.Count);
        Assert.All(statements, statement => Assert.False(string.IsNullOrWhiteSpace(statement)));
        Assert.Contains(statements, statement =>
            statement.Contains("CREATE TABLE IF NOT EXISTS polymarket_onchain_trade_details", StringComparison.Ordinal));
        Assert.Contains(statements, statement =>
            statement.StartsWith("DO $$", StringComparison.Ordinal) &&
            statement.Contains("DROP VIEW public.polymarket_onchain_trade_details", StringComparison.Ordinal));
    }

    [Fact]
    public void PostgresSchemaInitializer_KeepsDollarQuotedBlocksTogether()
    {
        const string sql = """
CREATE TABLE first_table (id integer);
DO $$
BEGIN
    EXECUTE 'CREATE TABLE second_table (id integer);';
END $$;
CREATE INDEX first_table_id_idx ON first_table(id);
""";

        var statements = PostgresSchemaInitializer.SplitSchemaSqlStatements(sql);

        Assert.Equal(3, statements.Count);
        Assert.StartsWith("CREATE TABLE first_table", statements[0], StringComparison.Ordinal);
        Assert.StartsWith("DO $$", statements[1], StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE second_table", statements[1], StringComparison.Ordinal);
        Assert.StartsWith("CREATE INDEX first_table_id_idx", statements[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CREATE INDEX IF NOT EXISTS ix_demo ON demo_table(id)", "ix_demo")]
    [InlineData("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_demo_concurrent ON demo_table(id)", "ix_demo_concurrent")]
    [InlineData("CREATE UNIQUE INDEX IF NOT EXISTS ux_demo ON demo_table(id)", "ux_demo")]
    [InlineData("CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_demo_concurrent ON demo_table(id)", "ux_demo_concurrent")]
    [InlineData("  create index if not exists ix_lower ON demo_table(id)", "ix_lower")]
    [InlineData("CREATE TABLE demo_table (id integer)", null)]
    public void PostgresSchemaInitializer_ReadsCreateIndexIfNotExistsName(string statement, string? expected)
    {
        var actual = PostgresSchemaInitializer.TryReadCreateIndexIfNotExistsName(statement);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConnectionFactory_RequiresConfiguredConnectionString()
    {
        var options = new StorageOptions
        {
            ConnectionString = string.Empty,
            ConnectionStringEnvironmentVariable = "POLYCOPYTRADER_TEST_MISSING_CONNECTION"
        };

        Assert.Throws<InvalidOperationException>(() => new PostgresConnectionFactory(options));
    }

    [Fact]
    public void ConnectionFactory_AppliesConfiguredMaximumPoolSize()
    {
        var factory = new PostgresConnectionFactory(new StorageOptions
        {
            ConnectionString = "Host=localhost;Database=polycopytrader;Username=test;Password=test",
            MaxPoolSize = 64
        });

        var connectionString = new NpgsqlConnectionStringBuilder(factory.ConnectionString);

        Assert.Equal(64, connectionString.MaxPoolSize);
    }

    [Fact]
    public void ConnectionFactory_PreservesConnectionStringMaximumPoolSizeWhenOptionIsUnset()
    {
        var factory = new PostgresConnectionFactory(new StorageOptions
        {
            ConnectionString = "Host=localhost;Database=polycopytrader;Username=test;Password=test;Maximum Pool Size=23"
        });

        var connectionString = new NpgsqlConnectionStringBuilder(factory.ConnectionString);

        Assert.Equal(23, connectionString.MaxPoolSize);
    }

    [Fact]
    public void ConnectionFactory_AppliesExplicitApplicationName()
    {
        var factory = new PostgresConnectionFactory(
            new StorageOptions
            {
                ConnectionString = "Host=localhost;Database=polycopytrader;Username=test;Password=test"
            },
            "PolyCopyTrader.Service");

        var connectionString = new NpgsqlConnectionStringBuilder(factory.ConnectionString);

        Assert.Equal("PolyCopyTrader.Service", connectionString.ApplicationName);
    }

    [Fact]
    public void ServiceAndDashboard_SetDistinctPostgresApplicationNames()
    {
        var service = ReadRepositorySource("src", "PolyCopyTrader.Service", "Program.cs");
        var dashboard = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Dashboard",
            "Services",
            "DashboardRepositoryFactory.cs");

        Assert.Contains("\"PolyCopyTrader.Service\"", service, StringComparison.Ordinal);
        Assert.Contains("\"PolyCopyTrader.Dashboard\"", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoOpRepository_IsSafeWhenDatabaseIsNotConfigured()
    {
        var repository = new NoOpAppRepository();
        var heartbeat = new ServiceHeartbeat(
            "PolyCopyTrader.Service",
            "Running",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "1.0.0",
            BotMode.ReadOnly,
            "Test",
            null);

        await repository.UpsertServiceHeartbeatAsync(heartbeat);
        await repository.AddPolymarketHttpLogAsync(new PolymarketHttpLogEntry(
            Guid.NewGuid(),
            "PolymarketDataApiClient",
            "GetUserTrades",
            "GET",
            "https://data-api.polymarket.com/trades",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            10,
            1,
            200,
            true,
            "{}",
            null));
        await repository.AddBtcUpDown5mStrategyStageTimingAsync(new BtcUpDown5mStrategyStageTiming(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test",
            "TestFlow",
            "test_stage",
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            1,
            1,
            0,
            0,
            0,
            0,
            null,
            null,
            true,
            null,
            DateTimeOffset.UtcNow));
        await repository.TryAddPolymarketWebSocketTradeTickAsync(new PolymarketWebSocketTradeTick(
            Guid.NewGuid(),
            "tick-1",
            "asset-1",
            "condition-1",
            TradeSide.Buy,
            0.45m,
            10m,
            DateTimeOffset.UtcNow,
            "0xabc",
            true,
            TradeTickTraderMatchStatus.NotFound,
            null,
            DateTimeOffset.UtcNow,
            null,
            0,
            null,
            null,
            null,
            null,
            "{}",
            DateTimeOffset.UtcNow));
        var heartbeats = await repository.GetServiceHeartbeatsAsync();
        var httpLogs = await repository.GetRecentPolymarketHttpLogsAsync();
        var ticks = await repository.GetRecentPolymarketWebSocketTradeTicksAsync();

        Assert.Empty(heartbeats);
        Assert.Empty(httpLogs);
        Assert.Empty(ticks);
    }

    [Fact]
    public async Task PostgresRepository_InitializesSchema_WhenTestConnectionIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new StorageOptions { ConnectionString = connectionString };
        var factory = new PostgresConnectionFactory(options);
        var initializer = new PostgresSchemaInitializer(factory);
        await initializer.InitializeAsync();

        var repository = new PostgresAppRepository(factory);
        var heartbeat = new ServiceHeartbeat(
            "PolyCopyTrader.Tests",
            "Running",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "1.0.0",
            BotMode.ReadOnly,
            "IntegrationTest",
            null);

        await repository.UpsertServiceHeartbeatAsync(heartbeat);
        var httpLog = new PolymarketHttpLogEntry(
            Guid.NewGuid(),
            "PolymarketDataApiClient",
            "GetTraderLeaderboard",
            "GET",
            "https://data-api.polymarket.com/v1/leaderboard",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            25,
            1,
            200,
            true,
            "[]",
            null);
        await repository.AddPolymarketHttpLogAsync(httpLog);

        var heartbeats = await repository.GetServiceHeartbeatsAsync();
        var httpLogs = await repository.GetRecentPolymarketHttpLogsAsync();

        Assert.Contains(heartbeats, item => item.ServiceName == "PolyCopyTrader.Tests");
        Assert.Contains(httpLogs, item => item.Id == httpLog.Id);
    }

    private static StrategyMarketPaperRun CreateSettledStrategyRun(Guid strategyId, DateTimeOffset settledAtUtc, decimal realizedPnlUsd)
    {
        return CreateSettledStrategyRun(
            strategyId,
            Guid.NewGuid(),
            selectedOutcome: "Up",
            settledAtUtc,
            realizedPnlUsd);
    }

    private static StrategyMarketPaperRun CreateSettledStrategyRun(
        Guid strategyId,
        Guid paperOrderId,
        string selectedOutcome,
        DateTimeOffset settledAtUtc,
        decimal realizedPnlUsd)
    {
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            strategyId,
            "market-" + Guid.NewGuid().ToString("N"),
            "condition-" + Guid.NewGuid().ToString("N"),
            "btc-updown-5m-test",
            "Bitcoin Up or Down - test",
            "Crypto",
            settledAtUtc.AddMinutes(-5),
            settledAtUtc,
            settledAtUtc.AddMinutes(-5),
            settledAtUtc.AddMinutes(-4),
            StrategyMarketPaperRunStatuses.Settled,
            "asset-up",
            selectedOutcome,
            0.50m,
            1m,
            2m,
            Guid.NewGuid(),
            paperOrderId,
            settledAtUtc.AddMinutes(-3),
            SettlementPrice: realizedPnlUsd < 0m ? 0m : 1m,
            SettlementValueUsd: 1m + realizedPnlUsd,
            RealizedPnlUsd: realizedPnlUsd,
            SettledAtUtc: settledAtUtc,
            SkipReason: null,
            settledAtUtc.AddMinutes(-5),
            settledAtUtc);
    }

    [Fact]
    public async Task TestRepository_LiveSettlementWaitsForNetFeeAccountingAndAppliesExactlyOnce()
    {
        var repository = new TestAppRepository();
        var strategyId = StrategyIds.FollowLeader;
        var now = DateTimeOffset.UtcNow;
        repository.StrategySettings[strategyId] = StrategyRuntimeSettings.Default(strategyId) with
        {
            LiveAvailableBalance = 50m,
            LiveStakes = true
        };
        var order = CreateLiveOrder(strategyId, now.AddMinutes(-5)) with
        {
            Status = LiveOrderStatus.Matched,
            FilledSize = 2m,
            RemainingSize = 0m,
            AverageFillPrice = 0.50m,
            FilledNotionalUsd = 1m,
            FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString()
        };
        repository.LiveOrders.Add(order);

        var pending = await repository.ApplyLiveOrderSettlementToStrategyBalanceAsync(
            order.Id,
            strategyId,
            settlementValueUsd: 2m,
            grossRealizedPnlUsd: 1m,
            netRealizedPnlUsd: null,
            winningAssetId: order.AssetId,
            winningOutcome: order.Outcome,
            settledAtUtc: now,
            updatedAtUtc: now);

        Assert.False(pending.Applied);
        Assert.Equal(50m, repository.StrategySettings[strategyId].LiveAvailableBalance);
        var pendingOrder = Assert.Single(repository.LiveOrders);
        Assert.False(pendingOrder.BalanceEffectApplied);
        Assert.Equal(1m, pendingOrder.RealizedPnlUsd);
        Assert.Null(pendingOrder.NetRealizedPnlUsd);

        var applied = await repository.ApplyLiveOrderSettlementToStrategyBalanceAsync(
            order.Id,
            strategyId,
            settlementValueUsd: 2m,
            grossRealizedPnlUsd: 1m,
            netRealizedPnlUsd: 0.90m,
            winningAssetId: order.AssetId,
            winningOutcome: order.Outcome,
            settledAtUtc: now,
            updatedAtUtc: now.AddSeconds(1));
        var duplicate = await repository.ApplyLiveOrderSettlementToStrategyBalanceAsync(
            order.Id,
            strategyId,
            settlementValueUsd: 2m,
            grossRealizedPnlUsd: 1m,
            netRealizedPnlUsd: 0.90m,
            winningAssetId: order.AssetId,
            winningOutcome: order.Outcome,
            settledAtUtc: now,
            updatedAtUtc: now.AddSeconds(2));

        Assert.True(applied.Applied);
        Assert.False(duplicate.Applied);
        Assert.Equal(50.90m, repository.StrategySettings[strategyId].LiveAvailableBalance);
        var settledOrder = Assert.Single(repository.LiveOrders);
        Assert.True(settledOrder.BalanceEffectApplied);
        Assert.Equal(0.90m, settledOrder.NetRealizedPnlUsd);
    }

    private static PaperOrder CreatePaperOrder(Guid strategyId, DateTimeOffset createdAtUtc)
    {
        return new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xleader",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-" + Guid.NewGuid().ToString("N"),
            "condition-" + Guid.NewGuid().ToString("N"),
            "Up",
            0.50m,
            2m,
            1m,
            createdAtUtc,
            createdAtUtc.AddMinutes(10),
            StrategyId: strategyId);
    }

    private static LiveOrder CreateLiveOrder(Guid strategyId, DateTimeOffset createdAtUtc)
    {
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Submitted,
            "0x" + Guid.NewGuid().ToString("N"),
            TradeSide.Buy,
            "asset-" + Guid.NewGuid().ToString("N"),
            "condition-" + Guid.NewGuid().ToString("N"),
            "Up",
            0.50m,
            2m,
            1m,
            "GTD",
            createdAtUtc,
            createdAtUtc.AddMinutes(10),
            createdAtUtc,
            "submitted",
            0m,
            2m,
            string.Empty,
            "{}",
            string.Empty,
            createdAtUtc,
            StrategyId: strategyId);
    }

    private static LiveOrder CreateSettledLiveOrder(Guid strategyId, DateTimeOffset settledAtUtc, decimal realizedPnlUsd)
    {
        var won = realizedPnlUsd > 0m;
        var costBasisUsd = 1m;
        var settlementValueUsd = costBasisUsd + realizedPnlUsd;
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched,
            "0x" + Guid.NewGuid().ToString("N"),
            TradeSide.Buy,
            "asset-up",
            "condition-" + Guid.NewGuid().ToString("N"),
            "Up",
            0.50m,
            2m,
            costBasisUsd,
            "GTD",
            settledAtUtc.AddMinutes(-5),
            settledAtUtc,
            settledAtUtc.AddMinutes(-4),
            "matched",
            2m,
            0m,
            string.Empty,
            "{}",
            string.Empty,
            settledAtUtc,
            StrategyId: strategyId,
            BalanceEffectApplied: true,
            SettlementValueUsd: settlementValueUsd,
            RealizedPnlUsd: realizedPnlUsd,
            SettledAtUtc: settledAtUtc,
            WinningAssetId: won ? "asset-up" : "asset-down",
            WinningOutcome: won ? "Up" : "Down",
            AverageFillPrice: 0.50m,
            FilledNotionalUsd: costBasisUsd,
            CostBasisUsd: costBasisUsd,
            Won: won,
            SettlementSource: "test");
    }

    private static string ReadStorageRepositorySource()
    {
        return ReadRepositorySource("src", "PolyCopyTrader.Storage", "PostgresAppRepository.cs");
    }

    private static PostgresAppRepository CreateUnreachablePostgresRepository()
    {
        return new PostgresAppRepository(new PostgresConnectionFactory(new StorageOptions
        {
            ConnectionString = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1"
        }));
    }

    private static string ReadDashboardDataServiceSource()
    {
        return ReadRepositorySource("src", "PolyCopyTrader.Dashboard", "Services", "DashboardDataService.cs");
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
            var candidateParts = new string[pathParts.Length + 1];
            candidateParts[0] = directory.FullName;
            Array.Copy(pathParts, 0, candidateParts, 1, pathParts.Length);
            var candidate = Path.Combine(candidateParts);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository source file from the test output directory.");
    }
}
