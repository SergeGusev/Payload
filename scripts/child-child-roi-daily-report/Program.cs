using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;

const string ProductionHost = "192.168.0.101";
const string ProductionDatabase = "polycopytrader";

var outputDirectory = Path.GetFullPath(args.FirstOrDefault() ?? ".");
Directory.CreateDirectory(outputDirectory);

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = Environment.GetEnvironmentVariable(
        "POLYCOPYTRADER_POSTGRES_CONNECTION",
        EnvironmentVariableTarget.User);
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not configured.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    Host = ProductionHost,
    Database = ProductionDatabase,
    ApplicationName = "CodexChildDailyReport20260715",
    Pooling = false,
    Timeout = 10,
    CommandTimeout = 180,
    KeepAlive = 30
};

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '180s';
SET LOCAL lock_timeout = '500ms';
SET LOCAL idle_in_transaction_session_timeout = '240s';
SET LOCAL TIME ZONE 'UTC';
""");

var cutoffDateTime = await ScalarAsync<DateTime>("SELECT now();");
var cutoffUtc = new DateTimeOffset(DateTime.SpecifyKind(cutoffDateTime, DateTimeKind.Utc));
var endpoint = await ScalarAsync<string>("SELECT host(inet_server_addr());");
var database = await ScalarAsync<string>("SELECT current_database();");
if (!string.Equals(endpoint, ProductionHost, StringComparison.Ordinal) ||
    !string.Equals(database, ProductionDatabase, StringComparison.Ordinal))
{
    throw new InvalidOperationException($"Unexpected endpoint {endpoint}/{database}.");
}

const string TargetsCte = """
WITH targets AS MATERIALIZED (
    SELECT
        strategy.id,
        strategy.code,
        strategy.name,
        strategy.enabled,
        strategy.paused,
        strategy.live_stakes,
        upper(substring(strategy.code FROM '^(btc|eth|sol)')) AS asset,
        CASE
            WHEN strategy.code ~ '_child_roi$' THEN 'Child ROI'
            ELSE 'Child'
        END AS family,
        substring(strategy.code FROM '_5m_([0-9]+)_child')::integer AS lookback_hours
    FROM strategies strategy
    WHERE strategy.code ~ '^(btc|eth|sol)_up_down_5m_[0-9]+_child(_roi)?$'
)
""";

var candidates = await LoadCandidatesAsync();
ValidateCandidateInventory(candidates);
var statsById = candidates.ToDictionary(
    candidate => candidate.Id,
    candidate => new StrategyStats(candidate));

var rawRowsPath = Path.Combine(outputDirectory, "settled-paper-runs.csv");
var rawRowCount = await ExportAndAggregateRawRowsAsync(rawRowsPath, statsById);
if (rawRowCount == 0)
{
    throw new InvalidOperationException("No settled Paper rows were returned.");
}

var serverAggregates = await LoadServerAggregatesAsync();
VerifyPerStrategyAggregates(statsById, serverAggregates);

var localWinners = SelectUniqueWinners(statsById.Values);
var serverWinners = await LoadServerWinnersAsync();
VerifyWinnerSelection(localWinners, serverWinners);

var serverDaily = await LoadServerWinnerDailyAsync();
VerifyWinnerDaily(localWinners, serverDaily);

var orderedWinners = localWinners.Values
    .OrderBy(strategy => strategy.Pnl)
    .ToArray();
for (var index = 1; index < orderedWinners.Length; index++)
{
    if (orderedWinners[index - 1].Pnl == orderedWinners[index].Pnl)
    {
        throw new InvalidOperationException(
            $"Report column order is ambiguous because '{orderedWinners[index - 1].Candidate.Name}' and " +
            $"'{orderedWinners[index].Candidate.Name}' have equal PnL {orderedWinners[index].Pnl}.");
    }
}

var firstDate = orderedWinners
    .SelectMany(strategy => strategy.DailyPnl.Keys)
    .Min();
var lastDate = orderedWinners
    .SelectMany(strategy => strategy.DailyPnl.Keys)
    .Max();
var dates = new List<DateOnly>();
for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
{
    dates.Add(date);
}

var reportData = new
{
    capturedAtUtc = DateTimeOffset.UtcNow,
    databaseCutoffUtc = cutoffUtc,
    sourceEndpoint = endpoint,
    sourceDatabase = database,
    timezone = "UTC",
    sourceTable = "strategy_market_paper_runs",
    sourceFilter = "status = Settled; realized_pnl_usd IS NOT NULL; settled_at_utc <= cutoff",
    candidateScope = "current BTC/ETH/SOL N=1..24 Child and Child ROI strategies; Progress excluded",
    selection = "unique maximum all-history settled Paper realized PnL within each asset and family",
    rawSettledPaperRows = rawRowCount,
    candidateStrategies = candidates.Count,
    dates = dates.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray(),
    strategies = orderedWinners.Select((strategy, index) => new
    {
        order = index + 1,
        asset = strategy.Candidate.Asset,
        family = strategy.Candidate.Family,
        lookbackHours = strategy.Candidate.LookbackHours,
        strategyId = strategy.Candidate.Id,
        code = strategy.Candidate.Code,
        name = strategy.Candidate.Name,
        settledRuns = strategy.Runs,
        wins = strategy.Wins,
        losses = strategy.Losses,
        flat = strategy.Flat,
        stakeUsd = strategy.Stake,
        totalPnlUsd = strategy.Pnl,
        roiPct = strategy.Stake == 0 ? 0 : strategy.Pnl * 100 / strategy.Stake,
        firstSettledAtUtc = strategy.FirstSettledAtUtc,
        lastSettledAtUtc = strategy.LastSettledAtUtc,
        dailyPnlUsd = dates
            .Select(date => strategy.DailyPnl.GetValueOrDefault(date))
            .ToArray()
    }).ToArray(),
    grandTotalPnlUsd = orderedWinners.Sum(strategy => strategy.Pnl)
};

await File.WriteAllTextAsync(
    Path.Combine(outputDirectory, "report-data.json"),
    JsonSerializer.Serialize(reportData, new JsonSerializerOptions { WriteIndented = true }),
    new UTF8Encoding(false));
await WriteCandidateAggregatesCsvAsync(statsById.Values);
await WriteSelectedStrategiesCsvAsync(orderedWinners);
await WriteServerDailyCsvAsync(serverDaily);

var heartbeat = await LoadHeartbeatAsync();
var metadata = new
{
    capturedAtUtc = DateTimeOffset.UtcNow,
    databaseCutoffUtc = cutoffUtc,
    endpoint,
    database,
    transactionIsolation = "repeatable read",
    transactionAccess = "read only",
    heartbeat
};
await File.WriteAllTextAsync(
    Path.Combine(outputDirectory, "metadata.json"),
    JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
    new UTF8Encoding(false));

var verificationLines = new List<string>
{
    $"Cutoff UTC: {cutoffUtc:O}",
    $"Endpoint: {endpoint}/{database}",
    $"Candidate strategies: {candidates.Count}",
    $"Raw settled Paper rows: {rawRowCount}",
    $"Continuous UTC dates: {firstDate:yyyy-MM-dd} to {lastDate:yyyy-MM-dd} ({dates.Count})",
    "Cross-check: local raw-row aggregates matched independent server SQL for all 144 candidates.",
    "Cross-check: six local maximum-PnL selections matched independent server SQL ranking.",
    "Cross-check: every selected strategy/date PnL matched independent server SQL daily aggregation.",
    "Selected strategies in ascending total PnL:"
};
verificationLines.AddRange(orderedWinners.Select(strategy =>
    $"  {strategy.Candidate.Name}: runs={strategy.Runs}; PnL={strategy.Pnl.ToString(CultureInfo.InvariantCulture)}"));
verificationLines.Add($"Grand total PnL: {orderedWinners.Sum(strategy => strategy.Pnl).ToString(CultureInfo.InvariantCulture)}");
await File.WriteAllLinesAsync(
    Path.Combine(outputDirectory, "data-verification.txt"),
    verificationLines,
    new UTF8Encoding(false));

await transaction.CommitAsync();

Console.WriteLine($"Cutoff: {cutoffUtc:O}");
Console.WriteLine($"Rows: {rawRowCount}");
Console.WriteLine($"Dates: {dates.Count}");
foreach (var winner in orderedWinners)
{
    Console.WriteLine($"{winner.Candidate.Name}: {winner.Pnl.ToString(CultureInfo.InvariantCulture)}");
}

async Task<List<StrategyCandidate>> LoadCandidatesAsync()
{
    const string sql = TargetsCte + """
SELECT id, code, name, enabled, paused, live_stakes, asset, family, lookback_hours
FROM targets
ORDER BY asset, family, lookback_hours;
""";
    var result = new List<StrategyCandidate>();
    await using var command = CreateCommand(sql);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new StrategyCandidate(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetInt32(8)));
    }

    return result;
}

async Task<long> ExportAndAggregateRawRowsAsync(
    string outputPath,
    IReadOnlyDictionary<Guid, StrategyStats> stats)
{
    const string sql = TargetsCte + """
SELECT
    target.id AS strategy_id,
    target.asset,
    target.family,
    target.lookback_hours,
    target.code,
    target.name,
    run.id AS run_id,
    run.stake_usd,
    run.realized_pnl_usd,
    run.settled_at_utc
FROM targets target
JOIN strategy_market_paper_runs run ON run.strategy_id = target.id
WHERE run.status = 'Settled'
  AND run.realized_pnl_usd IS NOT NULL
  AND run.settled_at_utc <= @Cutoff
ORDER BY run.settled_at_utc, run.id;
""";
    await using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
    await writer.WriteLineAsync(
        "strategy_id,asset,family,lookback_hours,code,name,run_id,stake_usd,realized_pnl_usd,settled_at_utc");

    long count = 0;
    await using var command = CreateCommand(sql);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        count++;
        var strategyId = reader.GetGuid(0);
        if (!stats.TryGetValue(strategyId, out var strategy))
        {
            throw new InvalidOperationException($"Unexpected raw strategy ID {strategyId}.");
        }

        var asset = reader.GetString(1);
        var family = reader.GetString(2);
        var lookbackHours = reader.GetInt32(3);
        var code = reader.GetString(4);
        var name = reader.GetString(5);
        if (!string.Equals(asset, strategy.Candidate.Asset, StringComparison.Ordinal) ||
            !string.Equals(family, strategy.Candidate.Family, StringComparison.Ordinal) ||
            lookbackHours != strategy.Candidate.LookbackHours ||
            !string.Equals(code, strategy.Candidate.Code, StringComparison.Ordinal) ||
            !string.Equals(name, strategy.Candidate.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Raw strategy metadata mismatch for {strategyId}.");
        }

        var runId = reader.GetGuid(6);
        var stake = reader.GetDecimal(7);
        var pnl = reader.GetDecimal(8);
        var settledAtUtc = ReadUtc(reader, 9);
        strategy.Add(stake, pnl, settledAtUtc);

        await writer.WriteLineAsync(string.Join(',', new[]
        {
            Csv(strategyId.ToString()),
            Csv(asset),
            Csv(family),
            lookbackHours.ToString(CultureInfo.InvariantCulture),
            Csv(code),
            Csv(name),
            Csv(runId.ToString()),
            stake.ToString(CultureInfo.InvariantCulture),
            pnl.ToString(CultureInfo.InvariantCulture),
            settledAtUtc.ToString("O", CultureInfo.InvariantCulture)
        }));
    }

    return count;
}

async Task<Dictionary<Guid, ServerAggregate>> LoadServerAggregatesAsync()
{
    const string sql = TargetsCte + """
SELECT
    target.id AS strategy_id,
    count(run.id) AS settled_runs,
    count(run.id) FILTER (WHERE run.realized_pnl_usd > 0) AS wins,
    count(run.id) FILTER (WHERE run.realized_pnl_usd < 0) AS losses,
    count(run.id) FILTER (WHERE run.realized_pnl_usd = 0) AS flat,
    coalesce(sum(run.stake_usd), 0) AS stake_usd,
    coalesce(sum(run.realized_pnl_usd), 0) AS pnl_usd,
    min(run.settled_at_utc) AS first_settled_at_utc,
    max(run.settled_at_utc) AS last_settled_at_utc
FROM targets target
LEFT JOIN strategy_market_paper_runs run
    ON run.strategy_id = target.id
   AND run.status = 'Settled'
   AND run.realized_pnl_usd IS NOT NULL
   AND run.settled_at_utc <= @Cutoff
GROUP BY target.id;
""";
    var result = new Dictionary<Guid, ServerAggregate>();
    await using var command = CreateCommand(sql);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var strategyId = reader.GetGuid(0);
        result.Add(strategyId, new ServerAggregate(
            strategyId,
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : ReadUtc(reader, 7),
            reader.IsDBNull(8) ? null : ReadUtc(reader, 8)));
    }
    return result;
}

async Task<Dictionary<string, Guid>> LoadServerWinnersAsync()
{
    const string sql = TargetsCte + """
, aggregates AS (
    SELECT
        target.id,
        target.asset,
        target.family,
        coalesce(sum(run.realized_pnl_usd), 0) AS pnl_usd
    FROM targets target
    LEFT JOIN strategy_market_paper_runs run
        ON run.strategy_id = target.id
       AND run.status = 'Settled'
       AND run.realized_pnl_usd IS NOT NULL
       AND run.settled_at_utc <= @Cutoff
    GROUP BY target.id, target.asset, target.family
), ranked AS (
    SELECT *, dense_rank() OVER (PARTITION BY asset, family ORDER BY pnl_usd DESC) AS pnl_rank
    FROM aggregates
)
SELECT asset, family, id, pnl_usd
FROM ranked
WHERE pnl_rank = 1
ORDER BY asset, family;
""";
    var rows = new List<(string Key, Guid Id, decimal Pnl)>();
    await using var command = CreateCommand(sql);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(($"{reader.GetString(0)}|{reader.GetString(1)}", reader.GetGuid(2), reader.GetDecimal(3)));
    }

    if (rows.Count != 6 || rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != 6)
    {
        throw new InvalidOperationException(
            $"Server winner selection returned {rows.Count} rows; a maximum-PnL tie or scope mismatch exists.");
    }
    return rows.ToDictionary(row => row.Key, row => row.Id, StringComparer.Ordinal);
}

async Task<Dictionary<(Guid StrategyId, DateOnly Date), decimal>> LoadServerWinnerDailyAsync()
{
    const string sql = TargetsCte + """
, aggregates AS (
    SELECT
        target.id,
        target.asset,
        target.family,
        coalesce(sum(run.realized_pnl_usd), 0) AS pnl_usd
    FROM targets target
    LEFT JOIN strategy_market_paper_runs run
        ON run.strategy_id = target.id
       AND run.status = 'Settled'
       AND run.realized_pnl_usd IS NOT NULL
       AND run.settled_at_utc <= @Cutoff
    GROUP BY target.id, target.asset, target.family
), ranked AS (
    SELECT *, dense_rank() OVER (PARTITION BY asset, family ORDER BY pnl_usd DESC) AS pnl_rank
    FROM aggregates
), winners AS (
    SELECT id FROM ranked WHERE pnl_rank = 1
)
SELECT
    run.strategy_id,
    (run.settled_at_utc AT TIME ZONE 'UTC')::date AS utc_date,
    sum(run.realized_pnl_usd) AS pnl_usd
FROM winners
JOIN strategy_market_paper_runs run ON run.strategy_id = winners.id
WHERE run.status = 'Settled'
  AND run.realized_pnl_usd IS NOT NULL
  AND run.settled_at_utc <= @Cutoff
GROUP BY run.strategy_id, (run.settled_at_utc AT TIME ZONE 'UTC')::date
ORDER BY run.strategy_id, utc_date;
""";
    var result = new Dictionary<(Guid StrategyId, DateOnly Date), decimal>();
    await using var command = CreateCommand(sql);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var key = (reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1));
        result.Add(key, reader.GetDecimal(2));
    }
    return result;
}

async Task<object?> LoadHeartbeatAsync()
{
    const string sql = """
SELECT
    service_name,
    status,
    mode,
    version,
    started_at_utc,
    last_heartbeat_utc,
    round(extract(epoch FROM (now() - last_heartbeat_utc))::numeric, 3) AS heartbeat_age_seconds,
    current_loop,
    last_error
FROM service_heartbeats
WHERE service_name = 'PolyCopyTrader.Service';
""";
    await using var command = CreateCommand(sql);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }
    return new
    {
        serviceName = reader.GetString(0),
        status = reader.GetString(1),
        mode = reader.GetString(2),
        version = reader.GetString(3),
        startedAtUtc = ReadUtc(reader, 4),
        lastHeartbeatUtc = ReadUtc(reader, 5),
        heartbeatAgeSeconds = reader.GetDecimal(6),
        currentLoop = reader.IsDBNull(7) ? null : reader.GetString(7),
        lastError = reader.IsDBNull(8) ? null : reader.GetString(8)
    };
}

async Task WriteCandidateAggregatesCsvAsync(IEnumerable<StrategyStats> stats)
{
    await using var writer = new StreamWriter(
        Path.Combine(outputDirectory, "candidate-aggregates.csv"),
        false,
        new UTF8Encoding(false));
    await writer.WriteLineAsync(
        "asset,family,lookback_hours,strategy_id,code,name,settled_runs,wins,losses,flat,stake_usd,pnl_usd,first_settled_at_utc,last_settled_at_utc");
    foreach (var strategy in stats
        .OrderBy(value => value.Candidate.Asset, StringComparer.Ordinal)
        .ThenBy(value => value.Candidate.Family, StringComparer.Ordinal)
        .ThenBy(value => value.Candidate.LookbackHours))
    {
        await writer.WriteLineAsync(string.Join(',', new[]
        {
            Csv(strategy.Candidate.Asset),
            Csv(strategy.Candidate.Family),
            strategy.Candidate.LookbackHours.ToString(CultureInfo.InvariantCulture),
            strategy.Candidate.Id.ToString(),
            Csv(strategy.Candidate.Code),
            Csv(strategy.Candidate.Name),
            strategy.Runs.ToString(CultureInfo.InvariantCulture),
            strategy.Wins.ToString(CultureInfo.InvariantCulture),
            strategy.Losses.ToString(CultureInfo.InvariantCulture),
            strategy.Flat.ToString(CultureInfo.InvariantCulture),
            strategy.Stake.ToString(CultureInfo.InvariantCulture),
            strategy.Pnl.ToString(CultureInfo.InvariantCulture),
            strategy.FirstSettledAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            strategy.LastSettledAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
        }));
    }
}

async Task WriteSelectedStrategiesCsvAsync(IEnumerable<StrategyStats> winners)
{
    await using var writer = new StreamWriter(
        Path.Combine(outputDirectory, "selected-strategies.csv"),
        false,
        new UTF8Encoding(false));
    await writer.WriteLineAsync(
        "order,asset,family,lookback_hours,strategy_id,code,name,settled_runs,stake_usd,pnl_usd,roi_pct");
    var index = 0;
    foreach (var strategy in winners)
    {
        index++;
        var roi = strategy.Stake == 0 ? 0 : strategy.Pnl * 100 / strategy.Stake;
        await writer.WriteLineAsync(string.Join(',', new[]
        {
            index.ToString(CultureInfo.InvariantCulture),
            Csv(strategy.Candidate.Asset),
            Csv(strategy.Candidate.Family),
            strategy.Candidate.LookbackHours.ToString(CultureInfo.InvariantCulture),
            strategy.Candidate.Id.ToString(),
            Csv(strategy.Candidate.Code),
            Csv(strategy.Candidate.Name),
            strategy.Runs.ToString(CultureInfo.InvariantCulture),
            strategy.Stake.ToString(CultureInfo.InvariantCulture),
            strategy.Pnl.ToString(CultureInfo.InvariantCulture),
            roi.ToString(CultureInfo.InvariantCulture)
        }));
    }
}

async Task WriteServerDailyCsvAsync(
    IReadOnlyDictionary<(Guid StrategyId, DateOnly Date), decimal> rows)
{
    await using var writer = new StreamWriter(
        Path.Combine(outputDirectory, "server-selected-daily.csv"),
        false,
        new UTF8Encoding(false));
    await writer.WriteLineAsync("strategy_id,utc_date,pnl_usd");
    foreach (var row in rows.OrderBy(item => item.Key.StrategyId).ThenBy(item => item.Key.Date))
    {
        await writer.WriteLineAsync(string.Join(',', new[]
        {
            row.Key.StrategyId.ToString(),
            row.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.Value.ToString(CultureInfo.InvariantCulture)
        }));
    }
}

void ValidateCandidateInventory(IReadOnlyCollection<StrategyCandidate> values)
{
    if (values.Count != 144)
    {
        throw new InvalidOperationException($"Expected 144 Child/Child ROI candidates, found {values.Count}.");
    }

    var expectedGroups = new[]
    {
        "BTC|Child", "BTC|Child ROI",
        "ETH|Child", "ETH|Child ROI",
        "SOL|Child", "SOL|Child ROI"
    };
    var actualGroups = values
        .GroupBy(value => $"{value.Asset}|{value.Family}", StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    if (!actualGroups.Keys.OrderBy(key => key, StringComparer.Ordinal)
        .SequenceEqual(expectedGroups.OrderBy(key => key, StringComparer.Ordinal), StringComparer.Ordinal))
    {
        throw new InvalidOperationException("Unexpected Child/Child ROI group set.");
    }

    foreach (var group in actualGroups)
    {
        var lookbacks = group.Value.Select(value => value.LookbackHours).OrderBy(value => value).ToArray();
        if (group.Value.Length != 24 || !lookbacks.SequenceEqual(Enumerable.Range(1, 24)))
        {
            throw new InvalidOperationException($"Unexpected N inventory for {group.Key}.");
        }
    }
}

void VerifyPerStrategyAggregates(
    IReadOnlyDictionary<Guid, StrategyStats> local,
    IReadOnlyDictionary<Guid, ServerAggregate> server)
{
    if (local.Count != server.Count || local.Keys.Except(server.Keys).Any())
    {
        throw new InvalidOperationException("Local and server aggregate strategy sets differ.");
    }

    foreach (var item in local)
    {
        var left = item.Value;
        var right = server[item.Key];
        if (left.Runs != right.Runs ||
            left.Wins != right.Wins ||
            left.Losses != right.Losses ||
            left.Flat != right.Flat ||
            left.Stake != right.Stake ||
            left.Pnl != right.Pnl ||
            left.FirstSettledAtUtc != right.FirstSettledAtUtc ||
            left.LastSettledAtUtc != right.LastSettledAtUtc)
        {
            throw new InvalidOperationException($"Independent aggregate mismatch for {left.Candidate.Name}.");
        }
    }
}

Dictionary<string, StrategyStats> SelectUniqueWinners(IEnumerable<StrategyStats> allStats)
{
    var result = new Dictionary<string, StrategyStats>(StringComparer.Ordinal);
    foreach (var group in allStats.GroupBy(
        strategy => $"{strategy.Candidate.Asset}|{strategy.Candidate.Family}",
        StringComparer.Ordinal))
    {
        var maximum = group.Max(strategy => strategy.Pnl);
        var winners = group.Where(strategy => strategy.Pnl == maximum).ToArray();
        if (winners.Length != 1)
        {
            throw new InvalidOperationException(
                $"Maximum-PnL selection is ambiguous for {group.Key}: {string.Join("; ", winners.Select(winner => winner.Candidate.Name))}.");
        }
        result.Add(group.Key, winners[0]);
    }
    if (result.Count != 6)
    {
        throw new InvalidOperationException($"Expected six local winners, found {result.Count}.");
    }
    return result;
}

void VerifyWinnerSelection(
    IReadOnlyDictionary<string, StrategyStats> local,
    IReadOnlyDictionary<string, Guid> server)
{
    if (local.Count != server.Count || local.Keys.Except(server.Keys, StringComparer.Ordinal).Any())
    {
        throw new InvalidOperationException("Local and server winner group sets differ.");
    }
    foreach (var item in local)
    {
        if (item.Value.Candidate.Id != server[item.Key])
        {
            throw new InvalidOperationException($"Independent winner mismatch for {item.Key}.");
        }
    }
}

void VerifyWinnerDaily(
    IReadOnlyDictionary<string, StrategyStats> winners,
    IReadOnlyDictionary<(Guid StrategyId, DateOnly Date), decimal> server)
{
    var winnerIds = winners.Values.Select(strategy => strategy.Candidate.Id).ToHashSet();
    if (server.Keys.Any(key => !winnerIds.Contains(key.StrategyId)))
    {
        throw new InvalidOperationException("Server daily aggregation returned a non-winner strategy.");
    }

    foreach (var winner in winners.Values)
    {
        var dates = winner.DailyPnl.Keys
            .Union(server.Keys.Where(key => key.StrategyId == winner.Candidate.Id).Select(key => key.Date))
            .ToArray();
        foreach (var date in dates)
        {
            var localValue = winner.DailyPnl.GetValueOrDefault(date);
            var serverValue = server.GetValueOrDefault((winner.Candidate.Id, date));
            if (localValue != serverValue)
            {
                throw new InvalidOperationException(
                    $"Independent daily PnL mismatch for {winner.Candidate.Name} on {date:yyyy-MM-dd}: {localValue} != {serverValue}.");
            }
        }
    }
}

NpgsqlCommand CreateCommand(string sql)
{
    var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("Cutoff", cutoffUtc);
    return command;
}

async Task ExecuteAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    await command.ExecuteNonQueryAsync();
}

async Task<T> ScalarAsync<T>(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    var value = await command.ExecuteScalarAsync();
    return (T)value!;
}

static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal)
{
    var value = reader.GetDateTime(ordinal);
    return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

static string Csv(string value)
{
    if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
    {
        return value;
    }
    return '"' + value.Replace("\"", "\"\"") + '"';
}

sealed record StrategyCandidate(
    Guid Id,
    string Code,
    string Name,
    bool Enabled,
    bool Paused,
    bool LiveStakes,
    string Asset,
    string Family,
    int LookbackHours);

sealed class StrategyStats(StrategyCandidate candidate)
{
    public StrategyCandidate Candidate { get; } = candidate;
    public long Runs { get; private set; }
    public long Wins { get; private set; }
    public long Losses { get; private set; }
    public long Flat { get; private set; }
    public decimal Stake { get; private set; }
    public decimal Pnl { get; private set; }
    public DateTimeOffset? FirstSettledAtUtc { get; private set; }
    public DateTimeOffset? LastSettledAtUtc { get; private set; }
    public Dictionary<DateOnly, decimal> DailyPnl { get; } = [];

    public void Add(decimal stake, decimal pnl, DateTimeOffset settledAtUtc)
    {
        Runs++;
        if (pnl > 0) Wins++;
        else if (pnl < 0) Losses++;
        else Flat++;
        Stake += stake;
        Pnl += pnl;
        FirstSettledAtUtc = FirstSettledAtUtc is null || settledAtUtc < FirstSettledAtUtc
            ? settledAtUtc
            : FirstSettledAtUtc;
        LastSettledAtUtc = LastSettledAtUtc is null || settledAtUtc > LastSettledAtUtc
            ? settledAtUtc
            : LastSettledAtUtc;
        var date = DateOnly.FromDateTime(settledAtUtc.UtcDateTime);
        DailyPnl[date] = DailyPnl.GetValueOrDefault(date) + pnl;
    }
}

sealed record ServerAggregate(
    Guid StrategyId,
    long Runs,
    long Wins,
    long Losses,
    long Flat,
    decimal Stake,
    decimal Pnl,
    DateTimeOffset? FirstSettledAtUtc,
    DateTimeOffset? LastSettledAtUtc);
