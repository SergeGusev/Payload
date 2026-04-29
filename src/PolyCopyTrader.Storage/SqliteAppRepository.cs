using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed class SqliteAppRepository(SqliteConnectionFactory connectionFactory) : IAppRepository
{
    public async Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT OR IGNORE INTO LeaderTrades (
    Id, TraderWallet, TraderName, ConditionId, AssetId, MarketSlug, MarketTitle, Outcome,
    Side, Price, Size, CashValueUsd, TimestampUtc, TransactionHash, RawJson, CreatedAtUtc
) VALUES (
    $Id, $TraderWallet, $TraderName, $ConditionId, $AssetId, $MarketSlug, $MarketTitle, $Outcome,
    $Side, $Price, $Size, $CashValueUsd, $TimestampUtc, $TransactionHash, $RawJson, $CreatedAtUtc
);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("$Id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$TraderWallet", trade.TraderWallet);
        command.Parameters.AddWithValue("$TraderName", trade.TraderName);
        command.Parameters.AddWithValue("$ConditionId", trade.ConditionId);
        command.Parameters.AddWithValue("$AssetId", trade.AssetId);
        command.Parameters.AddWithValue("$MarketSlug", trade.MarketSlug);
        command.Parameters.AddWithValue("$MarketTitle", trade.MarketTitle);
        command.Parameters.AddWithValue("$Outcome", trade.Outcome);
        command.Parameters.AddWithValue("$Side", trade.Side.ToString());
        command.Parameters.AddWithValue("$Price", trade.Price);
        command.Parameters.AddWithValue("$Size", trade.Size);
        command.Parameters.AddWithValue("$CashValueUsd", trade.CashValueUsd);
        command.Parameters.AddWithValue("$TimestampUtc", ToSqlTimestamp(trade.TimestampUtc));
        command.Parameters.AddWithValue("$TransactionHash", (object?)trade.TransactionHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$RawJson", JsonSerializer.Serialize(trade));
        command.Parameters.AddWithValue("$CreatedAtUtc", ToSqlTimestamp(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT TraderWallet, TraderName, ConditionId, AssetId, MarketSlug, MarketTitle, Outcome, Side,
       Price, Size, CashValueUsd, TimestampUtc, TransactionHash
FROM LeaderTrades
ORDER BY TimestampUtc DESC
LIMIT 100;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<LeaderTrade>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new LeaderTrade(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                Enum.Parse<TradeSide>(reader.GetString(7)),
                Decimal(reader, 8),
                Decimal(reader, 9),
                Decimal(reader, 10),
                DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return results;
    }

    public async Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO Signals (
    Id, LeaderTradeId, TraderWallet, ConditionId, AssetId, Outcome, LeaderPrice,
    BestBid, BestAsk, SpreadAbs, SpreadPct, LagSeconds, Score, Decision,
    ProposedPaperPrice, CreatedAtUtc, RawContextJson
) VALUES (
    $Id, $LeaderTradeId, $TraderWallet, $ConditionId, $AssetId, $Outcome, $LeaderPrice,
    $BestBid, $BestAsk, $SpreadAbs, $SpreadPct, $LagSeconds, $Score, $Decision,
    $ProposedPaperPrice, $CreatedAtUtc, $RawContextJson
);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("$Id", signal.Id.ToString("N"));
        command.Parameters.AddWithValue("$LeaderTradeId", DBNull.Value);
        command.Parameters.AddWithValue("$TraderWallet", signal.LeaderTrade.TraderWallet);
        command.Parameters.AddWithValue("$ConditionId", signal.LeaderTrade.ConditionId);
        command.Parameters.AddWithValue("$AssetId", signal.LeaderTrade.AssetId);
        command.Parameters.AddWithValue("$Outcome", signal.LeaderTrade.Outcome);
        command.Parameters.AddWithValue("$LeaderPrice", signal.LeaderTrade.Price);
        command.Parameters.AddWithValue("$BestBid", DBNull.Value);
        command.Parameters.AddWithValue("$BestAsk", DBNull.Value);
        command.Parameters.AddWithValue("$SpreadAbs", DBNull.Value);
        command.Parameters.AddWithValue("$SpreadPct", DBNull.Value);
        command.Parameters.AddWithValue("$LagSeconds", DBNull.Value);
        command.Parameters.AddWithValue("$Score", signal.Score);
        command.Parameters.AddWithValue("$Decision", signal.DecisionCode);
        command.Parameters.AddWithValue("$ProposedPaperPrice", (object?)signal.ProposedPaperPrice ?? DBNull.Value);
        command.Parameters.AddWithValue("$CreatedAtUtc", ToSqlTimestamp(signal.CreatedAtUtc));
        command.Parameters.AddWithValue("$RawContextJson", JsonSerializer.Serialize(signal));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO SignalRejections (Id, SignalId, ReasonCode, ReasonDetails, CreatedAtUtc)
VALUES ($Id, $SignalId, $ReasonCode, $ReasonDetails, $CreatedAtUtc);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("$Id", rejection.Id.ToString("N"));
        command.Parameters.AddWithValue("$SignalId", rejection.SignalId.ToString("N"));
        command.Parameters.AddWithValue("$ReasonCode", rejection.ReasonCode);
        command.Parameters.AddWithValue("$ReasonDetails", rejection.ReasonDetails);
        command.Parameters.AddWithValue("$CreatedAtUtc", ToSqlTimestamp(rejection.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO PaperOrders (
    Id, SignalId, Status, Side, AssetId, ConditionId, Price, SizeShares, NotionalUsd,
    CreatedAtUtc, ExpiresAtUtc, FilledAtUtc, CancelledAtUtc, RawDecisionJson
) VALUES (
    $Id, $SignalId, $Status, $Side, $AssetId, $ConditionId, $Price, $SizeShares, $NotionalUsd,
    $CreatedAtUtc, $ExpiresAtUtc, $FilledAtUtc, $CancelledAtUtc, $RawDecisionJson
);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("$Id", order.Id.ToString("N"));
        command.Parameters.AddWithValue("$SignalId", order.SignalId.ToString("N"));
        command.Parameters.AddWithValue("$Status", order.Status.ToString());
        command.Parameters.AddWithValue("$Side", order.Side.ToString());
        command.Parameters.AddWithValue("$AssetId", order.AssetId);
        command.Parameters.AddWithValue("$ConditionId", order.ConditionId);
        command.Parameters.AddWithValue("$Price", order.Price);
        command.Parameters.AddWithValue("$SizeShares", order.SizeShares);
        command.Parameters.AddWithValue("$NotionalUsd", order.NotionalUsd);
        command.Parameters.AddWithValue("$CreatedAtUtc", ToSqlTimestamp(order.CreatedAtUtc));
        command.Parameters.AddWithValue("$ExpiresAtUtc", ToSqlTimestamp(order.ExpiresAtUtc));
        command.Parameters.AddWithValue("$FilledAtUtc", DBNull.Value);
        command.Parameters.AddWithValue("$CancelledAtUtc", DBNull.Value);
        command.Parameters.AddWithValue("$RawDecisionJson", JsonSerializer.Serialize(order));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT Id, SignalId, Status, Side, AssetId, ConditionId, Price, SizeShares, NotionalUsd,
       CreatedAtUtc, ExpiresAtUtc
FROM PaperOrders
WHERE Status IN ('Pending', 'PartiallyFilled')
ORDER BY CreatedAtUtc DESC;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PaperOrder>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PaperOrder(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Enum.Parse<PaperOrderStatus>(reader.GetString(2)),
                Enum.Parse<TradeSide>(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                Decimal(reader, 6),
                Decimal(reader, 7),
                Decimal(reader, 8),
                DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture)));
        }

        return results;
    }

    public async Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO ApiErrors (Id, Component, Operation, Message, CreatedAtUtc)
VALUES ($Id, $Component, $Operation, $Message, $CreatedAtUtc);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("$Id", error.Id.ToString("N"));
        command.Parameters.AddWithValue("$Component", error.Component);
        command.Parameters.AddWithValue("$Operation", error.Operation);
        command.Parameters.AddWithValue("$Message", error.Message);
        command.Parameters.AddWithValue("$CreatedAtUtc", ToSqlTimestamp(error.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertServiceHeartbeatAsync(ServiceHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO ServiceHeartbeats (
    ServiceName, Status, StartedAtUtc, LastHeartbeatUtc, Version, Mode, CurrentLoop, LastError
) VALUES (
    $ServiceName, $Status, $StartedAtUtc, $LastHeartbeatUtc, $Version, $Mode, $CurrentLoop, $LastError
)
ON CONFLICT(ServiceName) DO UPDATE SET
    Status = excluded.Status,
    StartedAtUtc = excluded.StartedAtUtc,
    LastHeartbeatUtc = excluded.LastHeartbeatUtc,
    Version = excluded.Version,
    Mode = excluded.Mode,
    CurrentLoop = excluded.CurrentLoop,
    LastError = excluded.LastError;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("$ServiceName", heartbeat.ServiceName);
        command.Parameters.AddWithValue("$Status", heartbeat.Status);
        command.Parameters.AddWithValue("$StartedAtUtc", ToSqlTimestamp(heartbeat.StartedAtUtc));
        command.Parameters.AddWithValue("$LastHeartbeatUtc", ToSqlTimestamp(heartbeat.LastHeartbeatUtc));
        command.Parameters.AddWithValue("$Version", heartbeat.Version);
        command.Parameters.AddWithValue("$Mode", heartbeat.Mode.ToString());
        command.Parameters.AddWithValue("$CurrentLoop", heartbeat.CurrentLoop);
        command.Parameters.AddWithValue("$LastError", (object?)heartbeat.LastError ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceHeartbeat>> GetServiceHeartbeatsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT ServiceName, Status, StartedAtUtc, LastHeartbeatUtc, Version, Mode, CurrentLoop, LastError
FROM ServiceHeartbeats
ORDER BY ServiceName;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<ServiceHeartbeat>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ServiceHeartbeat(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.GetString(4),
                Enum.Parse<BotMode>(reader.GetString(5)),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return results;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static SqliteCommand CreateCommand(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static decimal Decimal(SqliteDataReader reader, int ordinal)
    {
        return Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static string ToSqlTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
