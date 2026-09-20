using Npgsql;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    public async Task RecordPaperAlgorithmOutcomeAsync(PaperAlgorithmOutcome outcome, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // The strategy lock also serializes explicit counter resets and final settlement.
        await using (var gate = new NpgsqlCommand("SELECT id FROM strategies WHERE id=@Strategy FOR UPDATE", connection, transaction))
        {
            gate.Parameters.AddWithValue("Strategy", outcome.StrategyId);
            await gate.ExecuteScalarAsync(cancellationToken);
        }
        await using var command = new NpgsqlCommand("""
            INSERT INTO paper_algorithm_outcomes(run_id,strategy_id,market_id,condition_id,asset_id,outcome,
                winning_asset_id,winning_outcome,source,evidence,observed_at_utc,contribution,reset_excluded)
            SELECT r.id,r.strategy_id,r.market_id,r.condition_id,r.selected_asset_id,r.selected_outcome,
                @Winner,@WinningOutcome,@Source,CAST(@Evidence AS jsonb),@Observed,@Contribution,s.paper_lost_coeff<=1
            FROM strategy_market_paper_runs r JOIN strategies s ON s.id=r.strategy_id
            WHERE r.id=@Run AND r.strategy_id=@Strategy AND r.market_id=@Market AND r.condition_id=@Condition
                AND r.selected_asset_id=@Asset AND r.selected_outcome=@Outcome AND r.status='Entered'
                AND r.size_shares>0 AND r.entered_at_utc IS NOT NULL
                AND EXISTS(SELECT 1 FROM paper_fills f WHERE f.paper_order_id=r.paper_order_id AND f.size_shares>0)
            ON CONFLICT(run_id) DO UPDATE SET winning_asset_id=excluded.winning_asset_id,
                winning_outcome=excluded.winning_outcome,source=excluded.source,evidence=excluded.evidence,
                observed_at_utc=excluded.observed_at_utc,contribution=excluded.contribution
            WHERE NOT paper_algorithm_outcomes.retired AND NOT paper_algorithm_outcomes.reset_excluded
                AND excluded.observed_at_utc>=paper_algorithm_outcomes.observed_at_utc;
            """, connection, transaction);
        command.Parameters.AddWithValue("Run", outcome.RunId);
        command.Parameters.AddWithValue("Strategy", outcome.StrategyId);
        command.Parameters.AddWithValue("Market", outcome.MarketId);
        command.Parameters.AddWithValue("Condition", outcome.ConditionId);
        command.Parameters.AddWithValue("Asset", outcome.AssetId);
        command.Parameters.AddWithValue("Outcome", outcome.Outcome);
        command.Parameters.AddWithValue("Winner", outcome.WinningAssetId);
        command.Parameters.AddWithValue("WinningOutcome", outcome.WinningOutcome);
        command.Parameters.AddWithValue("Source", outcome.Source);
        command.Parameters.AddWithValue("Evidence", outcome.EvidenceJson);
        command.Parameters.AddWithValue("Observed", outcome.ObservedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("Contribution", outcome.Contribution);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> GetEffectivePaperLostCounterAsync(Guid strategyId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT CASE WHEN s.paper_lost_coeff<=1 THEN 0 ELSE s.paper_lost_counter + COALESCE((
                SELECT sum(a.contribution) FROM paper_algorithm_outcomes a
                JOIN strategy_market_paper_runs r ON r.id=a.run_id
                WHERE a.strategy_id=s.id AND NOT a.retired AND NOT a.reset_excluded
                    AND r.status='Entered' AND r.size_shares>0),0) END
            FROM strategies s WHERE s.id=@Strategy;
            """, connection);
        command.Parameters.AddWithValue("Strategy", StrategyIds.Normalize(strategyId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
