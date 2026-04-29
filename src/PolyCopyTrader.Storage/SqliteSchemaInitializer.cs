using Microsoft.Data.Sqlite;

namespace PolyCopyTrader.Storage;

public sealed class SqliteSchemaInitializer(SqliteConnectionFactory connectionFactory) : ISqliteSchemaInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
        await ExecuteAsync(connection, SchemaSql, cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SchemaSql = """
CREATE TABLE IF NOT EXISTS Traders (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Wallet TEXT NOT NULL UNIQUE,
    Enabled INTEGER NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS TraderRules (
    Id TEXT PRIMARY KEY,
    TraderWallet TEXT NOT NULL,
    AllowedCategories TEXT NOT NULL,
    MaxLagSeconds INTEGER NOT NULL,
    MaxSlippageCents REAL NOT NULL,
    MaxSpreadCents REAL NOT NULL,
    MaxSpreadPct REAL NOT NULL,
    MinLeaderTradeUsd REAL NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS LeaderTrades (
    Id TEXT PRIMARY KEY,
    TraderWallet TEXT NOT NULL,
    TraderName TEXT NOT NULL,
    ConditionId TEXT NOT NULL,
    AssetId TEXT NOT NULL,
    MarketSlug TEXT NOT NULL,
    MarketTitle TEXT NOT NULL,
    Outcome TEXT NOT NULL,
    Side TEXT NOT NULL,
    Price REAL NOT NULL,
    Size REAL NOT NULL,
    CashValueUsd REAL NOT NULL,
    TimestampUtc TEXT NOT NULL,
    TransactionHash TEXT NULL,
    RawJson TEXT NULL,
    CreatedAtUtc TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_LeaderTrades_Dedup
ON LeaderTrades(TraderWallet, TransactionHash, AssetId, Side, TimestampUtc);

CREATE TABLE IF NOT EXISTS LeaderPositions (
    Id TEXT PRIMARY KEY,
    TraderWallet TEXT NOT NULL,
    ConditionId TEXT NOT NULL,
    AssetId TEXT NOT NULL,
    Outcome TEXT NOT NULL,
    Size REAL NOT NULL,
    AvgPrice REAL NOT NULL,
    CurrentValue REAL NOT NULL,
    CashPnl REAL NOT NULL,
    CurPrice REAL NOT NULL,
    SnapshotAtUtc TEXT NOT NULL,
    RawJson TEXT NULL
);

CREATE TABLE IF NOT EXISTS Markets (
    Id TEXT PRIMARY KEY,
    ConditionId TEXT NOT NULL UNIQUE,
    MarketSlug TEXT NOT NULL,
    MarketTitle TEXT NOT NULL,
    Category TEXT NULL,
    EndDateUtc TEXT NULL,
    RawJson TEXT NULL,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS OrderBookSnapshots (
    Id TEXT PRIMARY KEY,
    AssetId TEXT NOT NULL,
    ConditionId TEXT NULL,
    BestBid REAL NULL,
    BestAsk REAL NULL,
    SpreadAbs REAL NULL,
    SpreadPct REAL NULL,
    RawJson TEXT NULL,
    SnapshotAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Signals (
    Id TEXT PRIMARY KEY,
    LeaderTradeId TEXT NULL,
    TraderWallet TEXT NOT NULL,
    ConditionId TEXT NOT NULL,
    AssetId TEXT NOT NULL,
    Outcome TEXT NOT NULL,
    LeaderPrice REAL NOT NULL,
    BestBid REAL NULL,
    BestAsk REAL NULL,
    SpreadAbs REAL NULL,
    SpreadPct REAL NULL,
    LagSeconds INTEGER NULL,
    Score INTEGER NOT NULL,
    Decision TEXT NOT NULL,
    ProposedPaperPrice REAL NULL,
    CreatedAtUtc TEXT NOT NULL,
    RawContextJson TEXT NULL
);

CREATE TABLE IF NOT EXISTS SignalRejections (
    Id TEXT PRIMARY KEY,
    SignalId TEXT NOT NULL,
    ReasonCode TEXT NOT NULL,
    ReasonDetails TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    FOREIGN KEY (SignalId) REFERENCES Signals(Id)
);

CREATE TABLE IF NOT EXISTS PaperOrders (
    Id TEXT PRIMARY KEY,
    SignalId TEXT NOT NULL,
    Status TEXT NOT NULL,
    Side TEXT NOT NULL,
    AssetId TEXT NOT NULL,
    ConditionId TEXT NOT NULL,
    Price REAL NOT NULL,
    SizeShares REAL NOT NULL,
    NotionalUsd REAL NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    ExpiresAtUtc TEXT NOT NULL,
    FilledAtUtc TEXT NULL,
    CancelledAtUtc TEXT NULL,
    RawDecisionJson TEXT NULL
);

CREATE TABLE IF NOT EXISTS PaperFills (
    Id TEXT PRIMARY KEY,
    PaperOrderId TEXT NOT NULL,
    Price REAL NOT NULL,
    SizeShares REAL NOT NULL,
    FilledAtUtc TEXT NOT NULL,
    Evidence TEXT NOT NULL,
    FOREIGN KEY (PaperOrderId) REFERENCES PaperOrders(Id)
);

CREATE TABLE IF NOT EXISTS PaperPositions (
    Id TEXT PRIMARY KEY,
    AssetId TEXT NOT NULL,
    ConditionId TEXT NOT NULL,
    Outcome TEXT NOT NULL,
    SizeShares REAL NOT NULL,
    AveragePrice REAL NOT NULL,
    EstimatedValueUsd REAL NOT NULL,
    UnrealizedPnlUsd REAL NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS RiskEvents (
    Id TEXT PRIMARY KEY,
    ReasonCode TEXT NOT NULL,
    Details TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS BotSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ApiErrors (
    Id TEXT PRIMARY KEY,
    Component TEXT NOT NULL,
    Operation TEXT NOT NULL,
    Message TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ServiceHeartbeats (
    ServiceName TEXT PRIMARY KEY,
    Status TEXT NOT NULL,
    StartedAtUtc TEXT NOT NULL,
    LastHeartbeatUtc TEXT NOT NULL,
    Version TEXT NOT NULL,
    Mode TEXT NOT NULL,
    CurrentLoop TEXT NOT NULL,
    LastError TEXT NULL
);
""";
}
