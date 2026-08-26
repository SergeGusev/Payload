using System.Data;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    private sealed record HistoricalGrossNetParityRawDonorCursor(
        string EconomicKey,
        string SourceKind,
        Guid SourceId);

    private sealed record HistoricalGrossNetParityRawDonorRow(
        HistoricalGrossNetParitySourceKind SourceKind,
        Guid SourceId,
        string EconomicKey,
        long RepresentationPrecedence,
        HistoricalGrossNetParityDonorContributionKind ContributionKind,
        decimal Gross,
        decimal Basis,
        decimal StoredFee,
        decimal? EffectiveFee,
        decimal? Net,
        string Status,
        string CalculationSource,
        string LiquidityRole,
        decimal? FeeRate,
        int? FeeExponent,
        bool? FeeTakerOnly,
        DateTimeOffset? CalculatedAt,
        string? VenueEvidenceVersion,
        Guid? PaperOrderId,
        string? PoolWallet,
        string? PoolAsset,
        DateTimeOffset? BoundaryAt,
        Guid? BoundaryOrderId,
        Guid? BoundaryFillId,
        string CanonicalPayloadJson);

    private sealed record HistoricalGrossNetParityFreshDonorProof(
        bool Proved,
        string ComponentHash,
        bool ExcludeAfterLineage);

    private sealed record HistoricalGrossNetParityProcessedDonor(
        bool IsExact,
        bool ExcludeAfterLineage,
        HistoricalGrossNetDonorMembershipRecordV1? Membership);

    private sealed record HistoricalGrossNetParityStreamingFold(
        long RawCount,
        long ExactCount,
        long SelectedCount,
        decimal AggregateStake,
        decimal Numerator,
        decimal Denominator);

    private sealed record HistoricalGrossNetParityReplayCursor(
        DateTimeOffset FilledAtUtc,
        Guid PaperOrderId,
        Guid FillId);

    private sealed record HistoricalGrossNetParityCompactReplayState(
        decimal SizeShares,
        decimal AveragePrice,
        decimal FeeUsd,
        bool EntryFeesExact,
        bool HasPriorSell,
        long ActiveChargeCount,
        HistoricalGrossNetParityReplayCursor? ActiveAfter)
    {
        public static HistoricalGrossNetParityCompactReplayState Empty { get; } =
            new(0m, 0m, 0m, true, false, 0, null);
    }

    private sealed record HistoricalGrossNetParitySellProof(
        decimal Gross,
        decimal Net,
        decimal EffectiveEntryFee,
        decimal RawAllocation,
        decimal RemainingFee,
        decimal Decrement,
        decimal Residual,
        HistoricalGrossNetParityCompactReplayState Before);

    private sealed record HistoricalGrossNetParityReplayProof(
        HistoricalGrossNetParityCompactReplayState State,
        HistoricalGrossNetParitySellProof? TargetSell,
        bool HasBoundaryTimestampCollision);

    private static async Task<HistoricalGrossNetParityDonorCandidateAggregate>
        LoadHistoricalGrossNetParityDonorAggregateStreamingAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParitySourceKind targetSourceKind,
            HistoricalGrossNetDonorCandidateDescriptorV1 candidate,
            int rowPageSize,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        rowPageSize = Math.Clamp(rowPageSize, 1, HistoricalGrossNetParityMaximumPageSize);
        var first = await FoldHistoricalGrossNetParityDonorsAsync(
            connection,
            transaction,
            targetSourceKind,
            candidate.StrategyId,
            rowPageSize,
            commandTimeoutSeconds,
            membership: null,
            cancellationToken);

        using var membership = HistoricalGrossNetDonorHashV1.CreateMembershipHashBuilder(
            checked((uint)first.SelectedCount));
        var second = await FoldHistoricalGrossNetParityDonorsAsync(
            connection,
            transaction,
            targetSourceKind,
            candidate.StrategyId,
            rowPageSize,
            commandTimeoutSeconds,
            membership,
            cancellationToken);
        if (first != second)
        {
            throw new InvalidOperationException(
                "The repeatable donor stream changed between aggregate and membership passes.");
        }

        return new HistoricalGrossNetParityDonorCandidateAggregate(
            candidate.StrategyId,
            candidate.MatcherOrder,
            candidate.Tier,
            candidate.DistanceComponents,
            first.RawCount,
            first.ExactCount,
            first.SelectedCount,
            first.AggregateStake,
            first.Numerator,
            first.Denominator,
            membership.Complete());
    }

    private static async Task<HistoricalGrossNetParityStreamingFold>
        FoldHistoricalGrossNetParityDonorsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParitySourceKind targetSourceKind,
            Guid strategyId,
            int rowPageSize,
            int commandTimeoutSeconds,
            HistoricalGrossNetDonorMembershipHashBuilderV1? membership,
            CancellationToken cancellationToken)
    {
        long rawCount = 0;
        long exactCount = 0;
        long selectedCount = 0;
        decimal aggregateStake = 0m;
        decimal numerator = 0m;
        decimal denominator = 0m;
        HistoricalGrossNetParityRawDonorCursor? cursor = null;
        string? currentEconomicKey = null;
        HistoricalGrossNetDonorMembershipRecordV1? currentWinner = null;

        void FlushWinner()
        {
            if (currentWinner is null)
            {
                return;
            }

            selectedCount++;
            aggregateStake += currentWinner.Basis.ToDecimal();
            denominator += currentWinner.Basis.ToDecimal();
            numerator += currentWinner.Fee.ToDecimal();
            membership?.Append(currentWinner);
            currentWinner = null;
        }

        while (true)
        {
            var page = await LoadHistoricalGrossNetParityRawDonorPageAsync(
                connection,
                transaction,
                targetSourceKind,
                strategyId,
                cursor,
                rowPageSize,
                commandTimeoutSeconds,
                cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var raw in page)
            {
                rawCount++;
                var processed = await ProcessHistoricalGrossNetParityRawDonorAsync(
                    connection,
                    transaction,
                    targetSourceKind,
                    strategyId,
                    raw,
                    rowPageSize,
                    commandTimeoutSeconds,
                    cancellationToken);
                if (processed.IsExact)
                {
                    exactCount++;
                }

                if (!StringComparer.Ordinal.Equals(currentEconomicKey, raw.EconomicKey))
                {
                    FlushWinner();
                    currentEconomicKey = raw.EconomicKey;
                }

                if (!processed.IsExact || processed.ExcludeAfterLineage || processed.Membership is null)
                {
                    continue;
                }

                if (currentWinner is null ||
                    IsHistoricalGrossNetParityPreferredRepresentation(
                        processed.Membership,
                        currentWinner))
                {
                    currentWinner = processed.Membership;
                }
            }

            var last = page[^1];
            cursor = new HistoricalGrossNetParityRawDonorCursor(
                last.EconomicKey,
                last.SourceKind.ToString(),
                last.SourceId);
            if (page.Count < rowPageSize)
            {
                break;
            }
        }

        FlushWinner();
        return new HistoricalGrossNetParityStreamingFold(
            rawCount,
            exactCount,
            selectedCount,
            aggregateStake,
            numerator,
            denominator);
    }

    private static bool IsHistoricalGrossNetParityPreferredRepresentation(
        HistoricalGrossNetDonorMembershipRecordV1 candidate,
        HistoricalGrossNetDonorMembershipRecordV1 current)
    {
        var precedence = candidate.RepresentationPrecedence.CompareTo(current.RepresentationPrecedence);
        if (precedence != 0)
        {
            return precedence > 0;
        }

        var candidateKind = candidate.SourceKind.ToString();
        var currentKind = current.SourceKind.ToString();
        var kind = candidateKind.Length.CompareTo(currentKind.Length);
        if (kind == 0)
        {
            kind = StringComparer.Ordinal.Compare(candidateKind, currentKind);
        }

        if (kind != 0)
        {
            return kind < 0;
        }

        return StringComparer.Ordinal.Compare(
            candidate.SourceId.UuidValue?.ToString("D"),
            current.SourceId.UuidValue?.ToString("D")) < 0;
    }

    private static async Task<IReadOnlyList<HistoricalGrossNetParityRawDonorRow>>
        LoadHistoricalGrossNetParityRawDonorPageAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParitySourceKind targetSourceKind,
            Guid strategyId,
            HistoricalGrossNetParityRawDonorCursor? after,
            int pageSize,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        var positionWalletPredicate = strategyId == StrategyIds.FollowLeader
            ? "lower(position.copied_trader_wallet) NOT LIKE 'strategy:%'"
            : "position.copied_trader_wallet=(SELECT 'strategy:'||code FROM candidate_strategy)";
        var settlementWalletPredicate = strategyId == StrategyIds.FollowLeader
            ? "lower(settlement.copied_trader_wallet) NOT LIKE 'strategy:%'"
            : "settlement.copied_trader_wallet=(SELECT 'strategy:'||code FROM candidate_strategy)";
        await using var command = new NpgsqlCommand(
            $$"""
WITH candidate_strategy AS MATERIALIZED (
    SELECT strategy.id, strategy.code FROM strategies strategy WHERE strategy.id=@StrategyId
), uses_runs AS MATERIALIZED (
    SELECT (
        (SELECT run.updated_at_utc
         FROM strategy_market_paper_runs run
         WHERE run.strategy_id=@StrategyId
         ORDER BY run.updated_at_utc DESC
         LIMIT 1) IS NOT NULL
        OR (SELECT rollup.last_updated_at_utc
            FROM strategy_paper_skip_rollups rollup
            WHERE rollup.strategy_id=@StrategyId
            ORDER BY rollup.last_updated_at_utc DESC
            LIMIT 1) IS NOT NULL
    ) AS value
), raw AS (
    SELECT 'PaperRun'::text AS source_kind, run.id AS source_id,
           CASE WHEN run.paper_order_id IS NULL THEN 'paper-run:'||lower(run.id::text)
                ELSE 'paper-order:'||lower(run.paper_order_id::text) END AS economic_key,
           100::bigint AS representation_precedence, 'ClosedRealized'::text AS contribution_kind,
           run.realized_pnl_usd AS gross, run.stake_usd AS basis, run.fee_usd AS stored_fee,
           run.realized_pnl_usd-run.net_realized_pnl_usd AS effective_fee,
           run.net_realized_pnl_usd AS net, run.fee_accounting_status AS status,
           run.fee_calculation_source AS calculation_source, run.fee_liquidity_role AS liquidity_role,
           run.fee_rate, run.fee_exponent, run.fee_taker_only, run.fee_calculated_at_utc AS calculated_at,
           NULL::text AS venue_evidence_version, run.paper_order_id,
           NULL::text AS pool_wallet, NULL::text AS pool_asset,
           NULL::timestamptz AS boundary_at, NULL::uuid AS boundary_order_id,
           NULL::uuid AS boundary_fill_id, to_jsonb(run)::text AS canonical_payload
    FROM strategy_market_paper_runs run
    WHERE run.strategy_id=@StrategyId AND (SELECT value FROM uses_runs)
      AND run.status='Settled' AND run.realized_pnl_usd IS NOT NULL

    UNION ALL
    SELECT 'PaperPosition', position.id, 'paper-position:'||lower(position.id::text),
           100, 'OpenMarkToMarket', position.unrealized_pnl_usd,
           position.average_price*position.size_shares, position.fee_usd,
           position.unrealized_pnl_usd-position.net_unrealized_pnl_usd,
           position.net_unrealized_pnl_usd, position.fee_accounting_status,
           position.fee_calculation_source, position.fee_liquidity_role,
           position.fee_rate, position.fee_exponent, position.fee_taker_only,
           position.fee_calculated_at_utc, NULL, NULL::uuid,
           lower(position.copied_trader_wallet), position.asset_id,
           NULL::timestamptz, NULL::uuid, NULL::uuid, to_jsonb(position)::text
    FROM paper_positions position
    WHERE position.size_shares>0
      AND EXISTS (SELECT 1 FROM candidate_strategy)
      AND {{positionWalletPredicate}}

    UNION ALL
    SELECT 'PaperSettlement', settlement.id, 'paper-settlement:'||lower(settlement.id::text),
           100, 'ClosedRealized', settlement.realized_pnl_usd, settlement.cost_basis_usd,
           settlement.fee_usd, settlement.realized_pnl_usd-settlement.net_realized_pnl_usd,
           settlement.net_realized_pnl_usd, settlement.fee_accounting_status,
           settlement.fee_calculation_source, settlement.fee_liquidity_role,
           settlement.fee_rate, settlement.fee_exponent, settlement.fee_taker_only,
           settlement.fee_calculated_at_utc, NULL, NULL::uuid,
           lower(settlement.copied_trader_wallet), settlement.asset_id,
           settlement.settled_at_utc, NULL::uuid, NULL::uuid, to_jsonb(settlement)::text
    FROM paper_position_settlements settlement
    WHERE NOT (SELECT value FROM uses_runs)
      AND EXISTS (SELECT 1 FROM candidate_strategy)
      AND {{settlementWalletPredicate}}

    UNION ALL
    SELECT 'PaperSellFill', sell_fill.id,
           CASE WHEN order_fill_count.fill_count=1 THEN 'paper-order:'||lower(sell_order.id::text)
                ELSE 'paper-fill:'||lower(sell_fill.id::text) END,
           100, 'ClosedRealized', sell_fill.realized_pnl_usd,
           (sell_fill.price*sell_fill.size_shares)-sell_fill.realized_pnl_usd,
           sell_fill.fee_usd, sell_fill.realized_pnl_usd-sell_fill.net_realized_pnl_usd,
           sell_fill.net_realized_pnl_usd, sell_fill.fee_accounting_status,
           sell_fill.fee_calculation_source, sell_fill.fee_liquidity_role,
           sell_fill.fee_rate, sell_fill.fee_exponent, sell_fill.fee_taker_only,
           sell_fill.fee_calculated_at_utc, NULL, sell_order.id,
           lower(sell_order.copied_trader_wallet), sell_order.asset_id,
           sell_fill.filled_at_utc, sell_order.id, sell_fill.id,
           jsonb_build_object('order',to_jsonb(sell_order),'fill',to_jsonb(sell_fill))::text
    FROM paper_orders sell_order
    INNER JOIN paper_fills sell_fill ON sell_fill.paper_order_id=sell_order.id
    INNER JOIN LATERAL (
        SELECT count(*)::bigint AS fill_count FROM paper_fills sibling
        WHERE sibling.paper_order_id=sell_order.id) order_fill_count ON true
    WHERE sell_order.strategy_id=@StrategyId AND sell_order.side='Sell'
      AND NOT (SELECT value FROM uses_runs)

    UNION ALL
    SELECT 'LiveOrder', live_order.id,
           CASE WHEN live_order.paper_order_id IS NOT NULL
                THEN 'paper-order:'||lower(live_order.paper_order_id::text)
                ELSE 'live-order:'||lower(live_order.id::text) END,
           CASE WHEN live_order.fee_accounting_status='VenueReported' THEN 300 ELSE 200 END,
           'ClosedRealized', live_order.realized_pnl_usd,
           CASE WHEN live_order.filled_notional_usd>0 THEN live_order.filled_notional_usd
                WHEN live_order.filled_size>0 THEN live_order.price*live_order.filled_size
                WHEN live_order.cost_basis_usd>0 THEN GREATEST(0,live_order.cost_basis_usd-live_order.fee_usd)
                ELSE 0 END,
           live_order.fee_usd, live_order.realized_pnl_usd-live_order.net_realized_pnl_usd,
           live_order.net_realized_pnl_usd, live_order.fee_accounting_status,
           live_order.fee_calculation_source, live_order.fee_liquidity_role,
           live_order.fee_rate, live_order.fee_exponent, live_order.fee_taker_only,
           live_order.fee_calculated_at_utc, venue.evidence_version, live_order.paper_order_id,
           lower(linked_order.copied_trader_wallet), linked_order.asset_id,
           NULL::timestamptz, NULL::uuid, NULL::uuid, to_jsonb(live_order)::text
    FROM live_orders live_order
    LEFT JOIN paper_orders linked_order ON linked_order.id=live_order.paper_order_id
    LEFT JOIN LATERAL (
        SELECT audit.evidence_version
        FROM historical_gross_net_parity_audit audit
        WHERE audit.source_kind='LiveOrder' AND audit.source_id=live_order.id
          AND audit.calculation_version=@CalculationVersion
          AND audit.operation_kind IN ('AccountingDecision','VenueReportedRevision')
          AND audit.new_payload_json->>'fee_accounting_status'='VenueReported'
        ORDER BY CASE WHEN audit.operation_kind='VenueReportedRevision' THEN 1 ELSE 0 END DESC,
                 audit.authority_order_key DESC NULLS LAST, audit.occurred_at_utc DESC
        LIMIT 1) venue ON true
    WHERE @IncludeLive AND live_order.strategy_id=@StrategyId
      AND live_order.settled_at_utc IS NOT NULL AND live_order.realized_pnl_usd IS NOT NULL
), page AS (
    SELECT * FROM raw
    WHERE @AfterEconomicKey IS NULL
       OR octet_length(economic_key)>octet_length(@AfterEconomicKey)
       OR (octet_length(economic_key)=octet_length(@AfterEconomicKey)
           AND economic_key COLLATE "C">@AfterEconomicKey COLLATE "C")
       OR (economic_key=@AfterEconomicKey
           AND (octet_length(source_kind)>octet_length(@AfterSourceKind)
                OR (octet_length(source_kind)=octet_length(@AfterSourceKind)
                    AND source_kind COLLATE "C">@AfterSourceKind COLLATE "C")
                OR (source_kind=@AfterSourceKind
                    AND lower(source_id::text)>@AfterSourceId)))
    ORDER BY octet_length(economic_key), economic_key COLLATE "C",
             octet_length(source_kind), source_kind COLLATE "C", lower(source_id::text)
    LIMIT @PageSize
)
SELECT source_kind,source_id,economic_key,representation_precedence,contribution_kind,
       gross,basis,stored_fee,effective_fee,net,status,calculation_source,liquidity_role,
       fee_rate,fee_exponent,fee_taker_only,calculated_at,venue_evidence_version,
       paper_order_id,pool_wallet,pool_asset,boundary_at,boundary_order_id,boundary_fill_id,
       canonical_payload
FROM page
ORDER BY octet_length(economic_key), economic_key COLLATE "C",
         octet_length(source_kind), source_kind COLLATE "C", lower(source_id::text);
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue(
            "IncludeLive",
            targetSourceKind == HistoricalGrossNetParitySourceKind.LiveOrder);
        command.Parameters.AddWithValue("CalculationVersion", HistoricalGrossNetParityConstants.CalculationVersion);
        command.Parameters.Add("AfterEconomicKey", NpgsqlDbType.Text).Value =
            after is null ? DBNull.Value : after.EconomicKey;
        command.Parameters.Add("AfterSourceKind", NpgsqlDbType.Text).Value =
            after is null ? DBNull.Value : after.SourceKind;
        command.Parameters.Add("AfterSourceId", NpgsqlDbType.Text).Value =
            after is null ? DBNull.Value : after.SourceId.ToString("D").ToLowerInvariant();
        command.Parameters.AddWithValue("PageSize", pageSize);
        await EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(command, cancellationToken);

        var result = new List<HistoricalGrossNetParityRawDonorRow>(pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HistoricalGrossNetParityRawDonorRow(
                Enum.Parse<HistoricalGrossNetParitySourceKind>(reader.GetString(0), false),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetInt64(3),
                Enum.Parse<HistoricalGrossNetParityDonorContributionKind>(reader.GetString(4), false),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                reader.IsDBNull(14) ? null : reader.GetInt32(14),
                reader.IsDBNull(15) ? null : reader.GetBoolean(15),
                reader.IsDBNull(16) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(16)),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetGuid(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.IsDBNull(21) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(21)),
                reader.IsDBNull(22) ? null : reader.GetGuid(22),
                reader.IsDBNull(23) ? null : reader.GetGuid(23),
                NormalizeHistoricalGrossNetParityJson(reader.GetString(24))));
        }

        return result;
    }

    private static async Task<HistoricalGrossNetParityProcessedDonor>
        ProcessHistoricalGrossNetParityRawDonorAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParitySourceKind targetSourceKind,
            Guid strategyId,
            HistoricalGrossNetParityRawDonorRow raw,
            int rowPageSize,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        var emptyComponentHash = HistoricalGrossNetDonorHashV1.ComputeComponentAllocationHash([]);
        HistoricalGrossNetParityFreshDonorProof proof;
        if (raw.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder ||
            (raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperRun &&
             !string.Equals(raw.CalculationSource, "mixed", StringComparison.Ordinal)))
        {
            proof = new HistoricalGrossNetParityFreshDonorProof(true, emptyComponentHash, false);
        }
        else
        {
            proof = await ProveHistoricalGrossNetParityFreshPaperDonorAsync(
                connection,
                transaction,
                targetSourceKind,
                strategyId,
                raw,
                rowPageSize,
                commandTimeoutSeconds,
                cancellationToken);
        }

        if (raw.EffectiveFee is not { } effectiveFee || raw.Net is not { } net)
        {
            return new HistoricalGrossNetParityProcessedDonor(false, false, null);
        }

        var exact = raw.Basis > 0m &&
                    raw.StoredFee >= 0m &&
                    effectiveFee >= 0m &&
                    net == raw.Gross - effectiveFee &&
                    (raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill ||
                     effectiveFee == raw.StoredFee) &&
                    IsHistoricalGrossNetParityRawDonorAuthoritative(raw, proof) &&
                    (raw.SourceKind != HistoricalGrossNetParitySourceKind.PaperSellFill ||
                     (effectiveFee >= raw.StoredFee && proof.Proved));
        if (!exact)
        {
            return new HistoricalGrossNetParityProcessedDonor(false, false, null);
        }

        var evidenceVersion = string.Equals(raw.Status, "VenueReported", StringComparison.Ordinal)
            ? raw.VenueEvidenceVersion
            : raw.CalculationSource;
        var membership = new HistoricalGrossNetDonorMembershipRecordV1(
            raw.EconomicKey,
            raw.SourceKind,
            HistoricalGrossNetDonorSourceIdV1.FromUuid(raw.SourceId),
            null,
            new BigInteger(raw.RepresentationPrecedence),
            raw.ContributionKind,
            HistoricalGrossNetHashDecimalV1.FromDecimal(raw.Gross),
            HistoricalGrossNetHashDecimalV1.FromDecimal(raw.Basis),
            HistoricalGrossNetHashDecimalV1.FromDecimal(effectiveFee),
            HistoricalGrossNetHashDecimalV1.FromDecimal(net),
            raw.Status,
            raw.CalculationSource,
            evidenceVersion,
            raw.LiquidityRole,
            raw.FeeRate is null ? null : HistoricalGrossNetHashDecimalV1.FromDecimal(raw.FeeRate.Value),
            raw.FeeExponent is null ? null : new BigInteger(raw.FeeExponent.Value),
            raw.FeeTakerOnly,
            raw.CalculatedAt,
            proof.ComponentHash);
        return new HistoricalGrossNetParityProcessedDonor(true, proof.ExcludeAfterLineage, membership);
    }

    private static bool IsHistoricalGrossNetParityRawDonorAuthoritative(
        HistoricalGrossNetParityRawDonorRow raw,
        HistoricalGrossNetParityFreshDonorProof proof)
    {
        if (string.Equals(raw.Status, "VenueReported", StringComparison.Ordinal))
        {
            return raw.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder &&
                   !string.IsNullOrWhiteSpace(raw.VenueEvidenceVersion);
        }

        if (!string.Equals(raw.Status, "Calculated", StringComparison.Ordinal) ||
            raw.CalculatedAt is null)
        {
            return false;
        }

        if (string.Equals(raw.CalculationSource, "mixed", StringComparison.Ordinal))
        {
            return raw.SourceKind != HistoricalGrossNetParitySourceKind.LiveOrder && proof.Proved;
        }

        return IsHistoricalGrossNetParityExactLocalSource(
            raw.CalculationSource,
            raw.EffectiveFee ?? -1m,
            raw.LiquidityRole,
            raw.FeeRate,
            raw.FeeExponent,
            raw.FeeTakerOnly);
    }

    private static async Task<HistoricalGrossNetParityFreshDonorProof>
        ProveHistoricalGrossNetParityFreshPaperDonorAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParitySourceKind targetSourceKind,
            Guid strategyId,
            HistoricalGrossNetParityRawDonorRow raw,
            int rowPageSize,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        var emptyHash = HistoricalGrossNetDonorHashV1.ComputeComponentAllocationHash([]);
        if (raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperRun)
        {
            if (raw.PaperOrderId is null)
            {
                return new HistoricalGrossNetParityFreshDonorProof(false, emptyHash, false);
            }

            return await ProveHistoricalGrossNetParityMixedRunAsync(
                connection,
                transaction,
                raw,
                commandTimeoutSeconds,
                cancellationToken);
        }

        if (raw.PoolWallet is null || raw.PoolAsset is null)
        {
            return new HistoricalGrossNetParityFreshDonorProof(false, emptyHash, false);
        }

        var replay = await ReplayHistoricalGrossNetParityDonorStreamAsync(
            connection,
            transaction,
            strategyId,
            raw,
            commandTimeoutSeconds,
            cancellationToken);
        var state = raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill
            ? replay.TargetSell?.Before
            : replay.State;
        if (state is null || !state.EntryFeesExact || state.ActiveChargeCount <= 0)
        {
            return new HistoricalGrossNetParityFreshDonorProof(false, emptyHash, false);
        }

        if (raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperPosition &&
            (RoundHistoricalGrossNetParity8(state.SizeShares) != ReadJsonDecimal(raw.CanonicalPayloadJson, "size_shares") ||
             RoundHistoricalGrossNetParity8(state.AveragePrice) != ReadJsonDecimal(raw.CanonicalPayloadJson, "average_price") ||
             RoundHistoricalGrossNetParity8(state.FeeUsd) != raw.StoredFee))
        {
            return new HistoricalGrossNetParityFreshDonorProof(false, emptyHash, false);
        }

        if (raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperSettlement &&
            (replay.HasBoundaryTimestampCollision ||
             RoundHistoricalGrossNetParity8(state.SizeShares) != ReadJsonDecimal(raw.CanonicalPayloadJson, "settled_size_shares") ||
             RoundHistoricalGrossNetParity8(state.AveragePrice) != ReadJsonDecimal(raw.CanonicalPayloadJson, "average_price") ||
             RoundHistoricalGrossNetParity8(state.AveragePrice * state.SizeShares) != raw.Basis ||
             RoundHistoricalGrossNetParity8(state.FeeUsd) != raw.StoredFee))
        {
            return new HistoricalGrossNetParityFreshDonorProof(false, emptyHash, false);
        }

        if (raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill &&
            (replay.TargetSell is null || raw.Net is null ||
             raw.Gross != replay.TargetSell.Gross || raw.Net.Value != replay.TargetSell.Net ||
             replay.TargetSell.EffectiveEntryFee < 0m))
        {
            return new HistoricalGrossNetParityFreshDonorProof(false, emptyHash, false);
        }

        var overlap = targetSourceKind == HistoricalGrossNetParitySourceKind.LiveOrder &&
                      await HasHistoricalGrossNetParityExactLinkedLiveAsync(
                          connection,
                          transaction,
                          strategyId,
                          raw,
                          state.ActiveAfter,
                          rowPageSize,
                          commandTimeoutSeconds,
                          cancellationToken);
        var componentHash = await ComputeHistoricalGrossNetParityPoolComponentHashAsync(
            connection,
            transaction,
            strategyId,
            raw,
            state,
            replay.TargetSell,
            commandTimeoutSeconds,
            cancellationToken);
        return new HistoricalGrossNetParityFreshDonorProof(true, componentHash, overlap);
    }

    private static decimal ReadJsonDecimal(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(propertyName, out var value) ||
            !value.TryGetDecimal(out var result))
        {
            throw new InvalidOperationException($"Required donor replay field {propertyName} is absent.");
        }

        return result;
    }

    private static async Task<HistoricalGrossNetParityFreshDonorProof>
        ProveHistoricalGrossNetParityMixedRunAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParityRawDonorRow raw,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        long fillCount = 0;
        decimal fee = 0m;
        await foreach (var fill in ReadHistoricalGrossNetParityDonorFillsAsync(
                           connection,
                           transaction,
                           null,
                           raw,
                           orderByFillId: true,
                           commandTimeoutSeconds,
                           cancellationToken))
        {
            if (!IsHistoricalGrossNetParityExactFill(fill))
            {
                return new HistoricalGrossNetParityFreshDonorProof(
                    false,
                    HistoricalGrossNetDonorHashV1.ComputeComponentAllocationHash([]),
                    false);
            }

            fillCount++;
            fee += fill.FeeUsd;
        }

        if (fillCount <= 0 || RoundHistoricalGrossNetParity8(fee) != raw.StoredFee)
        {
            return new HistoricalGrossNetParityFreshDonorProof(
                false,
                HistoricalGrossNetDonorHashV1.ComputeComponentAllocationHash([]),
                false);
        }

        using var builder = HistoricalGrossNetDonorHashV1.CreateComponentEvidenceHashBuilder(
            checked((uint)(fillCount * 3)));
        foreach (var kind in new[]
                 {
                     HistoricalGrossNetComponentEvidenceRecordKind.CoverageEdge,
                     HistoricalGrossNetComponentEvidenceRecordKind.SourceCharge,
                     HistoricalGrossNetComponentEvidenceRecordKind.EffectiveAllocation
                 })
        {
            await foreach (var fill in ReadHistoricalGrossNetParityDonorFillsAsync(
                               connection,
                               transaction,
                               null,
                               raw,
                               orderByFillId: true,
                               commandTimeoutSeconds,
                               cancellationToken))
            {
                var allocationId = $"paper-run-entry:{raw.SourceId:D}:{fill.FillId:D}";
                var sourceChargeId = $"paper-fill:{fill.FillId:D}:entry";
                var component = CreateHistoricalGrossNetParityDirectComponent(
                    allocationId,
                    sourceChargeId,
                    fill.FeeUsd,
                    fill.CanonicalPayloadJson);
                builder.Append(HistoricalGrossNetParityComponentGraphV1
                    .ToEvidenceRecords([component])
                    .Single(value => value.RecordKind == kind));
            }
        }

        return new HistoricalGrossNetParityFreshDonorProof(true, builder.Complete(), false);
    }

    private static async Task<HistoricalGrossNetParityReplayProof>
        ReplayHistoricalGrossNetParityDonorStreamAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid strategyId,
            HistoricalGrossNetParityRawDonorRow raw,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        var state = HistoricalGrossNetParityCompactReplayState.Empty;
        HistoricalGrossNetParitySellProof? targetSell = null;
        var boundaryCollision = false;
        await foreach (var fill in ReadHistoricalGrossNetParityDonorFillsAsync(
                           connection,
                           transaction,
                           strategyId,
                           raw,
                           orderByFillId: false,
                           commandTimeoutSeconds,
                           cancellationToken))
        {
            if (raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperSettlement &&
                raw.BoundaryAt is not null && fill.FilledAtUtc == raw.BoundaryAt.Value)
            {
                boundaryCollision = true;
                continue;
            }

            var eventCursor = new HistoricalGrossNetParityReplayCursor(
                fill.FilledAtUtc,
                fill.PaperOrderId,
                fill.FillId);
            if (string.Equals(fill.OrderSide, "Buy", StringComparison.Ordinal))
            {
                var buyNewSize = RoundHistoricalGrossNetParity8(state.SizeShares + fill.FillSizeShares);
                if (buyNewSize <= 0m)
                {
                    continue;
                }

                var average = RoundHistoricalGrossNetParity8(
                    ((state.SizeShares * state.AveragePrice) +
                     (fill.FillPrice * fill.FillSizeShares)) / buyNewSize);
                state = state with
                {
                    SizeShares = buyNewSize,
                    AveragePrice = average,
                    FeeUsd = RoundHistoricalGrossNetParity8(state.FeeUsd + fill.FeeUsd),
                    EntryFeesExact = state.EntryFeesExact && IsHistoricalGrossNetParityExactFill(fill),
                    ActiveChargeCount = checked(state.ActiveChargeCount + 1)
                };
                continue;
            }

            if (!string.Equals(fill.OrderSide, "Sell", StringComparison.Ordinal) ||
                state.SizeShares <= 0m)
            {
                continue;
            }

            var before = state;
            var sellSize = RoundHistoricalGrossNetParity8(fill.FillSizeShares);
            var currentSize = RoundHistoricalGrossNetParity8(state.SizeShares);
            var sellFraction = Math.Min(1m, sellSize / currentSize);
            var rawAllocation = state.FeeUsd * sellFraction;
            var grossRaw = (fill.FillPrice - state.AveragePrice) * sellSize;
            var netRaw = grossRaw - rawAllocation - fill.FeeUsd;
            var gross8 = RoundHistoricalGrossNetParity8(grossRaw);
            var net8 = RoundHistoricalGrossNetParity8(netRaw);
            var effectiveEntry = (gross8 - net8) - fill.FeeUsd;
            var newSize = RoundHistoricalGrossNetParity8(Math.Max(0m, currentSize - sellSize));
            var remainingFraction = Math.Max(0m, Math.Min(1m, newSize / currentSize));
            var remainingFee = RoundHistoricalGrossNetParity8(state.FeeUsd * remainingFraction);
            var decrement = state.FeeUsd - remainingFee;
            var residual = effectiveEntry - decrement;
            if (fill.FillId == raw.SourceId &&
                raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill)
            {
                targetSell = new HistoricalGrossNetParitySellProof(
                    gross8,
                    net8,
                    effectiveEntry,
                    rawAllocation,
                    remainingFee,
                    decrement,
                    residual,
                    before);
            }

            state = state with
            {
                SizeShares = newSize,
                AveragePrice = newSize == 0m ? 0m : state.AveragePrice,
                FeeUsd = newSize == 0m ? 0m : remainingFee,
                EntryFeesExact = newSize == 0m || state.EntryFeesExact,
                HasPriorSell = true,
                ActiveChargeCount = newSize == 0m ? 0 : state.ActiveChargeCount,
                ActiveAfter = newSize == 0m ? eventCursor : state.ActiveAfter
            };
        }

        return new HistoricalGrossNetParityReplayProof(state, targetSell, boundaryCollision);
    }

    private static async Task<string> ComputeHistoricalGrossNetParityPoolComponentHashAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        HistoricalGrossNetParityRawDonorRow raw,
        HistoricalGrossNetParityCompactReplayState state,
        HistoricalGrossNetParitySellProof? sell,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var entryAllocationId = sell is null
            ? $"paper-entry-remaining:{raw.SourceKind}:{raw.SourceId:D}"
            : $"paper-entry-allocation:{raw.SourceId:D}";
        var entryAmount = sell?.EffectiveEntryFee ?? RoundHistoricalGrossNetParity8(state.FeeUsd);
        var hasMovement = sell is not null || state.HasPriorSell;
        var exitAllocationId = sell is null ? null : $"paper-exit-allocation:{raw.SourceId:D}";
        var exitSourceId = sell is null ? null : $"paper-fill:{raw.SourceId:D}:exit";
        var recordCount = checked(state.ActiveChargeCount * 2 + 1 + (hasMovement ? 1 : 0) +
                                  (sell is null ? 0 : 3));
        using var builder = HistoricalGrossNetDonorHashV1.CreateComponentEvidenceHashBuilder(
            checked((uint)recordCount));

        long observedCharges = 0;
        if (sell is not null)
        {
            builder.Append(HistoricalGrossNetComponentEvidenceRecordV1.CoverageEdge(
                exitAllocationId!,
                exitSourceId!));
        }
        await foreach (var fill in ReadHistoricalGrossNetParityActiveBuyFillsAsync(
                           connection,
                           transaction,
                           strategyId,
                           raw,
                           state.ActiveAfter,
                           commandTimeoutSeconds,
                           cancellationToken))
        {
            builder.Append(HistoricalGrossNetComponentEvidenceRecordV1.CoverageEdge(
                entryAllocationId,
                $"paper-fill:{fill.FillId:D}:entry"));
            observedCharges++;
        }

        if (observedCharges != state.ActiveChargeCount)
        {
            throw new InvalidOperationException("The streamed active Paper charge count changed during component hashing.");
        }

        if (hasMovement)
        {
            var rawAllocation = sell?.RawAllocation ?? state.FeeUsd;
            var remaining = sell?.RemainingFee ?? 0m;
            var decrement = sell?.Decrement ?? state.FeeUsd;
            var residual = sell?.Residual ?? 0m;
            builder.Append(HistoricalGrossNetComponentEvidenceRecordV1.PoolMovement(
                entryAllocationId,
                HistoricalGrossNetHashDecimalV1.FromDecimal(rawAllocation),
                HistoricalGrossNetHashDecimalV1.FromDecimal(remaining),
                HistoricalGrossNetHashDecimalV1.FromDecimal(decrement),
                HistoricalGrossNetHashDecimalV1.FromDecimal(residual)));
        }

        if (exitSourceId is not null)
        {
            builder.Append(CreateHistoricalGrossNetParityDirectEvidenceRecord(
                HistoricalGrossNetComponentEvidenceRecordKind.SourceCharge,
                exitAllocationId!,
                exitSourceId,
                raw.StoredFee,
                raw.CanonicalPayloadJson));
        }
        await foreach (var fill in ReadHistoricalGrossNetParityActiveBuyFillsAsync(
                           connection,
                           transaction,
                           strategyId,
                           raw,
                           state.ActiveAfter,
                           commandTimeoutSeconds,
                           cancellationToken))
        {
            var sourceId = $"paper-fill:{fill.FillId:D}:entry";
            builder.Append(HistoricalGrossNetComponentEvidenceRecordV1.SourceCharge(
                sourceId,
                HistoricalGrossNetHashDecimalV1.FromDecimal(fill.FeeUsd),
                CreateHistoricalGrossNetParityBuyChargeEvidenceHash(fill)));
        }

        if (sell is not null)
        {
            builder.Append(HistoricalGrossNetComponentEvidenceRecordV1.EffectiveAllocation(
                exitAllocationId!,
                HistoricalGrossNetHashDecimalV1.FromDecimal(raw.StoredFee),
                HistoricalGrossNetParityComponentGraphV1.ComputeAllocationHash(
                    exitAllocationId!,
                    raw.StoredFee)));
        }
        builder.Append(HistoricalGrossNetComponentEvidenceRecordV1.EffectiveAllocation(
            entryAllocationId,
            HistoricalGrossNetHashDecimalV1.FromDecimal(entryAmount),
            HistoricalGrossNetParityComponentGraphV1.ComputeAllocationHash(entryAllocationId, entryAmount)));

        return builder.Complete();
    }

    private static HistoricalGrossNetComponentEvidenceRecordV1
        CreateHistoricalGrossNetParityDirectEvidenceRecord(
            HistoricalGrossNetComponentEvidenceRecordKind kind,
            string allocationId,
            string sourceChargeId,
            decimal amountUsd,
            string payload)
    {
        var component = CreateHistoricalGrossNetParityDirectComponent(
            allocationId,
            sourceChargeId,
            amountUsd,
            payload);
        return HistoricalGrossNetParityComponentGraphV1.ToEvidenceRecords([component])
            .Single(value => value.RecordKind == kind);
    }

    private static string CreateHistoricalGrossNetParityBuyChargeEvidenceHash(
        HistoricalGrossNetParityPaperFillObservation fill)
    {
        var sourceChargeId = $"paper-fill:{fill.FillId:D}:entry";
        var sourceEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityPaperBuySourceChargeV1",
            fill.FillId,
            fill.PaperOrderId,
            sourceChargeId,
            amountUsd = fill.FeeUsd,
            fill.CanonicalPayloadJson
        });
        return HashHistoricalGrossNetParityPayload(sourceEvidence);
    }

    private static async IAsyncEnumerable<HistoricalGrossNetParityPaperFillObservation>
        ReadHistoricalGrossNetParityActiveBuyFillsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid strategyId,
            HistoricalGrossNetParityRawDonorRow raw,
            HistoricalGrossNetParityReplayCursor? activeAfter,
            int commandTimeoutSeconds,
            [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var fill in ReadHistoricalGrossNetParityDonorFillsAsync(
                           connection,
                           transaction,
                           strategyId,
                           raw,
                           orderByFillId: true,
                           commandTimeoutSeconds,
                           cancellationToken))
        {
            if (!string.Equals(fill.OrderSide, "Buy", StringComparison.Ordinal))
            {
                continue;
            }

            if (activeAfter is not null && CompareHistoricalGrossNetParityReplayCursor(
                    new HistoricalGrossNetParityReplayCursor(
                        fill.FilledAtUtc,
                        fill.PaperOrderId,
                        fill.FillId),
                    activeAfter) <= 0)
            {
                continue;
            }

            yield return fill;
        }
    }

    private static async IAsyncEnumerable<HistoricalGrossNetParityPaperFillObservation>
        ReadHistoricalGrossNetParityDonorFillsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid? strategyId,
            HistoricalGrossNetParityRawDonorRow raw,
            bool orderByFillId,
            int commandTimeoutSeconds,
            [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var orderBy = orderByFillId
            ? "lower(fill.id::text), fill.filled_at_utc, lower(paper_order.id::text)"
            : "fill.filled_at_utc, lower(paper_order.id::text), lower(fill.id::text)";
        var poolWalletPredicate = strategyId == StrategyIds.FollowLeader
            ? "lower(paper_order.copied_trader_wallet)=@PoolWallet"
            : "paper_order.copied_trader_wallet=@PoolWallet";
        var sql = $$"""
SELECT fill.id, fill.xmin::text::bigint, paper_order.id, paper_order.xmin::text::bigint,
       paper_order.strategy_id, paper_order.copied_trader_wallet, paper_order.status,
       paper_order.side, paper_order.execution_source, paper_order.asset_id,
       paper_order.condition_id, paper_order.outcome, paper_order.price,
       paper_order.size_shares, paper_order.created_at_utc, fill.price,
       fill.size_shares, fill.filled_at_utc, fill.realized_pnl_usd, fill.fee_usd,
       fill.fee_accounting_status, fill.fee_liquidity_role, fill.fee_calculation_source,
       fill.fee_rate, fill.fee_exponent, fill.fee_taker_only,
       fill.fee_calculated_at_utc, fill.net_realized_pnl_usd,
       jsonb_build_object(
           'fill_id',lower(fill.id::text),'paper_order_id',lower(paper_order.id::text),
           'strategy_id',lower(paper_order.strategy_id::text),'wallet',paper_order.copied_trader_wallet,
           'status',paper_order.status,'side',paper_order.side,
           'execution_source',paper_order.execution_source,'asset_id',paper_order.asset_id,
           'condition_id',paper_order.condition_id,'outcome',paper_order.outcome,
           'order_price',paper_order.price,'order_size_shares',paper_order.size_shares,
           'order_created_at_utc',paper_order.created_at_utc,'fill_price',fill.price,
           'fill_size_shares',fill.size_shares,'filled_at_utc',fill.filled_at_utc,
           'realized_pnl_usd',fill.realized_pnl_usd,'fee_usd',fill.fee_usd,
           'fee_accounting_status',fill.fee_accounting_status,
           'fee_liquidity_role',fill.fee_liquidity_role,
           'fee_calculation_source',fill.fee_calculation_source,
           'fee_rate',fill.fee_rate,'fee_exponent',fill.fee_exponent,
           'fee_taker_only',fill.fee_taker_only,'fee_calculated_at_utc',fill.fee_calculated_at_utc,
           'net_realized_pnl_usd',fill.net_realized_pnl_usd)::text
FROM paper_orders paper_order
INNER JOIN paper_fills fill ON fill.paper_order_id=paper_order.id
WHERE ((@DirectOrderId IS NOT NULL AND paper_order.id=@DirectOrderId)
       OR (@DirectOrderId IS NULL
           AND {{poolWalletPredicate}}
           AND paper_order.asset_id=@PoolAsset))
  AND (@BoundaryAt IS NULL
       OR fill.filled_at_utc<@BoundaryAt
       OR (fill.filled_at_utc=@BoundaryAt
           AND (@BoundaryFillId IS NULL
                OR ROW(lower(paper_order.id::text),lower(fill.id::text))<=
                   ROW(lower(@BoundaryOrderId::text),lower(@BoundaryFillId::text)))))
ORDER BY {{orderBy}};
""";
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("DirectOrderId", NpgsqlDbType.Uuid).Value =
            raw.SourceKind == HistoricalGrossNetParitySourceKind.PaperRun && raw.PaperOrderId is not null
                ? raw.PaperOrderId.Value
                : DBNull.Value;
        command.Parameters.Add("PoolWallet", NpgsqlDbType.Text).Value = raw.PoolWallet ?? (object)DBNull.Value;
        command.Parameters.Add("PoolAsset", NpgsqlDbType.Text).Value = raw.PoolAsset ?? (object)DBNull.Value;
        command.Parameters.Add("BoundaryAt", NpgsqlDbType.TimestampTz).Value =
            raw.BoundaryAt?.UtcDateTime ?? (object)DBNull.Value;
        command.Parameters.Add("BoundaryOrderId", NpgsqlDbType.Uuid).Value =
            raw.BoundaryOrderId ?? (object)DBNull.Value;
        command.Parameters.Add("BoundaryFillId", NpgsqlDbType.Uuid).Value =
            raw.BoundaryFillId ?? (object)DBNull.Value;
        await EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(command, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fillId = reader.GetGuid(0);
            var paperOrderId = reader.GetGuid(2);
            var filledAt = DateTimeOffsetFromUtc(reader.GetDateTime(17));
            yield return new HistoricalGrossNetParityPaperFillObservation(
                fillId, reader.GetInt64(1), paperOrderId, reader.GetInt64(3), reader.GetGuid(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
                reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetDecimal(12),
                reader.GetDecimal(13), DateTimeOffsetFromUtc(reader.GetDateTime(14)), reader.GetDecimal(15),
                reader.GetDecimal(16), filledAt, reader.GetDecimal(18), reader.GetDecimal(19),
                reader.GetString(20), reader.GetString(21), reader.GetString(22),
                reader.IsDBNull(23) ? null : reader.GetDecimal(23),
                reader.IsDBNull(24) ? null : reader.GetInt32(24),
                reader.IsDBNull(25) ? null : reader.GetBoolean(25),
                reader.IsDBNull(26) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(26)),
                reader.IsDBNull(27) ? null : reader.GetDecimal(27),
                string.Create(CultureInfo.InvariantCulture, $"{filledAt:O}|{paperOrderId:D}|{fillId:D}"),
                NormalizeHistoricalGrossNetParityJson(reader.GetString(28)));
        }
    }

    private static int CompareHistoricalGrossNetParityReplayCursor(
        HistoricalGrossNetParityReplayCursor left,
        HistoricalGrossNetParityReplayCursor right)
    {
        var comparison = left.FilledAtUtc.CompareTo(right.FilledAtUtc);
        if (comparison == 0)
        {
            comparison = StringComparer.Ordinal.Compare(
                left.PaperOrderId.ToString("D"),
                right.PaperOrderId.ToString("D"));
        }
        if (comparison == 0)
        {
            comparison = StringComparer.Ordinal.Compare(left.FillId.ToString("D"), right.FillId.ToString("D"));
        }
        return comparison;
    }

    private static async Task<bool> HasHistoricalGrossNetParityExactLinkedLiveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        HistoricalGrossNetParityRawDonorRow raw,
        HistoricalGrossNetParityReplayCursor? activeAfter,
        int rowPageSize,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        HistoricalGrossNetParityReplayCursor? pageAfter = null;
        while (true)
        {
            var page = await LoadHistoricalGrossNetParityActiveBuyOrderPageAsync(
                connection,
                transaction,
                strategyId,
                raw,
                activeAfter,
                pageAfter,
                rowPageSize,
                commandTimeoutSeconds,
                cancellationToken);
            if (page.Count == 0)
            {
                return false;
            }

            if (await HasHistoricalGrossNetParityExactLinkedLivePageAsync(
                    connection,
                    transaction,
                    strategyId,
                    page.Select(value => value.PaperOrderId).Distinct().ToArray(),
                    commandTimeoutSeconds,
                    cancellationToken))
            {
                return true;
            }

            pageAfter = page[^1].Cursor;
            if (page.Count < rowPageSize)
            {
                return false;
            }
        }
    }

    private sealed record HistoricalGrossNetParityActiveBuyOrder(
        HistoricalGrossNetParityReplayCursor Cursor,
        Guid PaperOrderId);

    private static async Task<IReadOnlyList<HistoricalGrossNetParityActiveBuyOrder>>
        LoadHistoricalGrossNetParityActiveBuyOrderPageAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid strategyId,
            HistoricalGrossNetParityRawDonorRow raw,
            HistoricalGrossNetParityReplayCursor? activeAfter,
            HistoricalGrossNetParityReplayCursor? pageAfter,
            int pageSize,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        var poolWalletPredicate = strategyId == StrategyIds.FollowLeader
            ? "lower(paper_order.copied_trader_wallet)=@PoolWallet"
            : "paper_order.copied_trader_wallet=@PoolWallet";
        await using var command = new NpgsqlCommand(
            $$"""
SELECT fill.filled_at_utc,paper_order.id,fill.id
FROM paper_orders paper_order
INNER JOIN paper_fills fill ON fill.paper_order_id=paper_order.id
WHERE {{poolWalletPredicate}}
  AND paper_order.asset_id=@PoolAsset AND paper_order.side='Buy'
  AND (@BoundaryAt IS NULL OR fill.filled_at_utc<@BoundaryAt
       OR (fill.filled_at_utc=@BoundaryAt
           AND (@BoundaryFillId IS NULL
                OR ROW(lower(paper_order.id::text),lower(fill.id::text))<=
                   ROW(lower(@BoundaryOrderId::text),lower(@BoundaryFillId::text)))))
  AND (@ActiveAfterAt IS NULL OR fill.filled_at_utc>@ActiveAfterAt
       OR (fill.filled_at_utc=@ActiveAfterAt
           AND ROW(lower(paper_order.id::text),lower(fill.id::text))>
               ROW(lower(@ActiveAfterOrderId::text),lower(@ActiveAfterFillId::text))))
  AND (@PageAfterAt IS NULL OR fill.filled_at_utc>@PageAfterAt
       OR (fill.filled_at_utc=@PageAfterAt
           AND ROW(lower(paper_order.id::text),lower(fill.id::text))>
               ROW(lower(@PageAfterOrderId::text),lower(@PageAfterFillId::text))))
ORDER BY fill.filled_at_utc,lower(paper_order.id::text),lower(fill.id::text)
LIMIT @PageSize;
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("PoolWallet", raw.PoolWallet!);
        command.Parameters.AddWithValue("PoolAsset", raw.PoolAsset!);
        command.Parameters.Add("BoundaryAt", NpgsqlDbType.TimestampTz).Value =
            raw.BoundaryAt?.UtcDateTime ?? (object)DBNull.Value;
        command.Parameters.Add("BoundaryOrderId", NpgsqlDbType.Uuid).Value =
            raw.BoundaryOrderId ?? (object)DBNull.Value;
        command.Parameters.Add("BoundaryFillId", NpgsqlDbType.Uuid).Value =
            raw.BoundaryFillId ?? (object)DBNull.Value;
        AddHistoricalGrossNetParityReplayCursorParameters(command, "ActiveAfter", activeAfter);
        AddHistoricalGrossNetParityReplayCursorParameters(command, "PageAfter", pageAfter);
        command.Parameters.AddWithValue("PageSize", pageSize);
        await EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(command, cancellationToken);
        var result = new List<HistoricalGrossNetParityActiveBuyOrder>(pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderId = reader.GetGuid(1);
            result.Add(new HistoricalGrossNetParityActiveBuyOrder(
                new HistoricalGrossNetParityReplayCursor(
                    DateTimeOffsetFromUtc(reader.GetDateTime(0)),
                    orderId,
                    reader.GetGuid(2)),
                orderId));
        }
        return result;
    }

    private static void AddHistoricalGrossNetParityReplayCursorParameters(
        NpgsqlCommand command,
        string prefix,
        HistoricalGrossNetParityReplayCursor? cursor)
    {
        command.Parameters.Add(prefix + "At", NpgsqlDbType.TimestampTz).Value =
            cursor?.FilledAtUtc.UtcDateTime ?? (object)DBNull.Value;
        command.Parameters.Add(prefix + "OrderId", NpgsqlDbType.Uuid).Value =
            cursor?.PaperOrderId ?? (object)DBNull.Value;
        command.Parameters.Add(prefix + "FillId", NpgsqlDbType.Uuid).Value =
            cursor?.FillId ?? (object)DBNull.Value;
    }

    private static async Task<bool> HasHistoricalGrossNetParityExactLinkedLivePageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        Guid[] paperOrderIds,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT EXISTS (
    SELECT 1
    FROM live_orders live_order
    LEFT JOIN LATERAL (
        SELECT audit.evidence_version
        FROM historical_gross_net_parity_audit audit
        WHERE audit.source_kind='LiveOrder' AND audit.source_id=live_order.id
          AND audit.calculation_version=@CalculationVersion
          AND audit.operation_kind IN ('AccountingDecision','VenueReportedRevision')
          AND audit.new_payload_json->>'fee_accounting_status'='VenueReported'
        ORDER BY CASE WHEN audit.operation_kind='VenueReportedRevision' THEN 1 ELSE 0 END DESC,
                 audit.authority_order_key DESC NULLS LAST, audit.occurred_at_utc DESC
        LIMIT 1) venue ON true
    WHERE live_order.strategy_id=@StrategyId
      AND live_order.paper_order_id=ANY(@PaperOrderIds)
      AND live_order.settled_at_utc IS NOT NULL
      AND live_order.realized_pnl_usd IS NOT NULL
      AND live_order.net_realized_pnl_usd IS NOT NULL
      AND (CASE WHEN live_order.filled_notional_usd>0 THEN live_order.filled_notional_usd
                WHEN live_order.filled_size>0 THEN live_order.price*live_order.filled_size
                WHEN live_order.cost_basis_usd>0 THEN GREATEST(0,live_order.cost_basis_usd-live_order.fee_usd)
                ELSE 0 END)>0
      AND live_order.fee_usd>=0
      AND live_order.net_realized_pnl_usd=live_order.realized_pnl_usd-live_order.fee_usd
      AND ((live_order.fee_accounting_status='VenueReported' AND venue.evidence_version IS NOT NULL)
           OR (live_order.fee_accounting_status='Calculated'
               AND live_order.fee_calculated_at_utc IS NOT NULL
               AND (((live_order.fee_calculation_source=@ExactCurveSource
                      OR live_order.fee_calculation_source=@HistoricalPrefix||@ExactCurveSource)
                     AND live_order.fee_liquidity_role<>'Unknown'
                     AND live_order.fee_rate IS NOT NULL
                     AND live_order.fee_exponent IS NOT NULL
                     AND live_order.fee_taker_only IS NOT NULL)
                    OR ((live_order.fee_calculation_source=@ExactNoFeeSource
                         OR live_order.fee_calculation_source=@HistoricalPrefix||@ExactNoFeeSource)
                        AND live_order.fee_usd=0))))
    LIMIT 1);
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.Add("PaperOrderIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = paperOrderIds;
        command.Parameters.AddWithValue("CalculationVersion", HistoricalGrossNetParityConstants.CalculationVersion);
        command.Parameters.AddWithValue("ExactCurveSource", HistoricalGrossNetParityExactCurveSource);
        command.Parameters.AddWithValue("ExactNoFeeSource", HistoricalGrossNetParityExactNoFeeSource);
        command.Parameters.AddWithValue("HistoricalPrefix", HistoricalGrossNetParityHistoricalModelPrefix);
        await EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(command, cancellationToken);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
}
