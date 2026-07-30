using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal static class ReconciliationContract
{
    public const int SchemaVersion = 1;
    public const string Algorithm = "SHA-256";
    public const string Serialization =
        "System.Text.Json UTF-8; fixed snake_case property names/order; targets sorted ordinal by target_id";
    public const string ApplyHandshake =
        "Require exact set equality on target_id, method_id, blocks_mutation and target_contract_sha256; " +
        "require this complete contract SHA-256; any missing, extra or changed target fails closed.";

    private static readonly IReadOnlyList<ReconciliationTarget> targets =
    [
        new(
            "dashboard_projection_events.global_bootstrap_visible_event_cleanup.v1",
            "dashboard_projection_events",
            "apply deletes affected-strategy rows; bootstrap deletes every event visible in its global REPEATABLE READ snapshot",
            "stopped_service_maintenance_host_global_bootstrap_delete_snapshot_visible_events_v1",
            "delete affected-strategy rows in apply, then use the maintenance host while the Windows service is stopped to globally discard bootstrap-snapshot-visible events",
            "The stopped-service maintenance bootstrap rebuilds every strategy from one snapshot; only events not visible in that snapshot remain for incremental processing.",
            true),
        new(
            "dashboard_strategy_lifetime_projection_states.global_bootstrap_rebuild.v1",
            "dashboard_strategy_lifetime_projection_states",
            "entire table and all strategies",
            "stopped_service_maintenance_host_clear_then_rebuild_all_lifetime_states_v1",
            "global deterministic full bootstrap on the maintenance host while the Windows service is stopped",
            "Maintenance bootstrap clears the complete table and rebuilds every strategy from a global REPEATABLE READ snapshot.",
            true),
        new(
            "dashboard_strategy_recent_projection_states.global_bootstrap_rebuild.v1",
            "dashboard_strategy_recent_projection_states",
            "entire table, all strategies, and 1h/6h/24h windows",
            "stopped_service_maintenance_host_clear_then_rebuild_all_recent_states_1_6_24_v1",
            "global deterministic full bootstrap on the maintenance host while the Windows service is stopped",
            "Maintenance bootstrap clears the complete table and rebuilds all three windows for every strategy from one snapshot.",
            true),
        new(
            "dashboard_strategy_recent_projection_facts.global_bootstrap_rebuild.v1",
            "dashboard_strategy_recent_projection_facts",
            "entire table and all snapshot-visible source rows",
            "stopped_service_maintenance_host_clear_then_copy_all_recent_facts_v1",
            "global deterministic full bootstrap on the maintenance host while the Windows service is stopped",
            "Maintenance bootstrap clears the complete table and binary-copies all recent facts built from its global snapshot.",
            true),
        new(
            "dashboard_strategy_position_projection_facts.global_bootstrap_rebuild.v1",
            "dashboard_strategy_position_projection_facts",
            "entire table and all snapshot-visible PaperPosition rows",
            "stopped_service_maintenance_host_clear_then_copy_all_position_facts_v1",
            "global deterministic full bootstrap on the maintenance host while the Windows service is stopped",
            "Maintenance bootstrap clears the complete table and binary-copies position facts for every strategy from its global snapshot.",
            true),
        new(
            "dashboard_strategy_performance_snapshots.global_bootstrap_upsert.v1",
            "dashboard_strategy_performance_snapshots",
            "all strategies; remove rows whose strategy no longer exists",
            "stopped_service_maintenance_host_upsert_all_lifetime_snapshots_v1",
            "global deterministic full bootstrap on the maintenance host while the Windows service is stopped",
            "Maintenance bootstrap upserts a lifetime snapshot for every rebuilt strategy and deletes snapshots for removed strategies.",
            true),
        new(
            "dashboard_strategy_recent_performance_snapshots.global_bootstrap_upsert.v1",
            "dashboard_strategy_recent_performance_snapshots",
            "all strategies and 1h/6h/24h windows; remove rows whose strategy no longer exists",
            "stopped_service_maintenance_host_upsert_all_recent_snapshots_v1",
            "global deterministic full bootstrap on the maintenance host while the Windows service is stopped",
            "Maintenance bootstrap upserts all three recent snapshots for every rebuilt strategy and deletes rows for removed strategies.",
            true),
        new(
            "date_dependent_strategy_hourly_paper_pnl.stopped_service_maintenance_refresh.v1",
            "date_dependent_strategy_hourly_paper_pnl",
            "affected strategy_id/hour",
            "stopped_service_maintenance_host_full_hourly_refresh_and_verify_v1",
            "run explicit full hourly refresh and verify corrected buckets on the maintenance host before normal restart",
            "The Windows service must remain stopped so this refresh completes inside the immediate rollback window.",
            false),
        new(
            "strategy_child_parent_assignments.post_normal_restart_refresh.v1",
            "strategy_child_parent_assignments",
            "affected child/parent strategy and lookback",
            "post_normal_restart_5m_assignment_refresh_and_verify_v1",
            "after the normal service restart, wait for a fresh 5-minute assignment refresh, then verify",
            "This is the only reviewed post-normal-restart refresh gate unless a separately reviewed one-shot refresher is supplied.",
            false),
        new(
            "paper_copied_trader_performance.stopped_service_maintenance_recalculation.v1",
            "paper_copied_trader_performance",
            "affected strategy wallet/category",
            "stopped_service_maintenance_host_recalculate_affected_wallets_v1",
            "recalculate affected wallets from corrected orders/fills/positions/settlements before normal restart",
            "The Windows service remains stopped so copied-trader performance is verified inside the immediate rollback window.",
            true),
        new(
            "strategies.paper_lost_counter_preserve.v1",
            "strategies",
            "affected strategy_id paper_lost_counter",
            "preserve_current_paper_lost_counter_v1",
            "leave current paper_lost_counter unchanged",
            "Intentional history-only correction limitation: do not mutate future staking state.",
            false),
        new(
            "dashboard_projection_reconciliation_queue.enqueue_then_global_bootstrap_clear.v1",
            "dashboard_projection_reconciliation_queue",
            "apply upserts affected strategy_id rows; successful global bootstrap clears the entire queue",
            "enqueue_affected_then_stopped_service_maintenance_bootstrap_clear_all_queue_v1",
            "enqueue affected strategies, then require successful maintenance-host global bootstrap and an empty queue before normal restart",
            "The apply transaction schedules reconciliation; the stopped-service maintenance bootstrap supersedes per-strategy work and clears the whole queue.",
            true),
        new(
            "dashboard_projection_control.pending_then_global_bootstrap_running.v1",
            "dashboard_projection_control",
            "singleton",
            "transition_pending_then_stopped_service_maintenance_global_bootstrap_v1",
            "apply requires Running/version 2 and sets PendingHistoryCorrectionBootstrap; maintenance host returns Running/version 2 before normal restart",
            "The singleton is the explicit handoff from the physical correction to the stopped-service maintenance full-bootstrap algorithm.",
            true),
        new(
            "daily_reports.final_zero_gate.v1",
            "daily_reports",
            "entire table",
            "assert_zero_rows_immediately_before_apply_v1",
            "require exact zero rows immediately before apply; do not mutate or rebuild",
            "Any row at the final pre-apply gate is a hard blocker.",
            false)
    ];

    public static IReadOnlyList<ReconciliationTarget> Targets => targets;

    public static string ContractSha256 { get; } = HashTargets(targets);

    public static string HashTarget(ReconciliationTarget target) => CanonicalEvidence.HashUtf8Text(
        JsonSerializer.Serialize(TargetProjection(target)));

    private static string HashTargets(IEnumerable<ReconciliationTarget> values) =>
        CanonicalEvidence.HashUtf8Text(JsonSerializer.Serialize(new
        {
            schema_version = SchemaVersion,
            targets = values.OrderBy(item => item.TargetId, StringComparer.Ordinal)
                .Select(TargetProjection)
        }));

    private static object TargetProjection(ReconciliationTarget target) => new
    {
        target_id = target.TargetId,
        table_name = target.TableName,
        key_scope = target.KeyScope,
        method_id = target.MethodId,
        required_action = target.RequiredAction,
        reason = target.Reason,
        blocks_mutation = target.BlocksMutation
    };
}
