[CmdletBinding()]
param(
    [string]$ConnectionString = $env:POLYCOPYTRADER_POSTGRES_CONNECTION,
    [string]$HostOverride,
    [string]$ExpectedCommit,
    [decimal]$MaxDelaySeconds = 3,
    [int]$LookbackMinutes = 30,
    [int]$MaxHeartbeatAgeSeconds = 180,
    [int]$MinRows = 1,
    [switch]$AllowNoRows,
    [switch]$RequireSplitCycleKinds,
    [string]$PsqlPath = "psql"
)

$ErrorActionPreference = "Stop"

function ConvertFrom-NpgsqlConnectionString {
    param([Parameter(Mandatory = $true)][string]$Value)

    $parts = @{}
    foreach ($part in ($Value -split ';')) {
        if ([string]::IsNullOrWhiteSpace($part)) {
            continue
        }

        $separator = $part.IndexOf('=')
        if ($separator -le 0) {
            continue
        }

        $key = $part.Substring(0, $separator).Trim().ToLowerInvariant()
        $parts[$key] = $part.Substring($separator + 1).Trim()
    }

    return $parts
}

function Get-ConnectionValue {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Parts,
        [Parameter(Mandatory = $true)][string[]]$Keys,
        [string]$DefaultValue
    )

    foreach ($key in $Keys) {
        $normalizedKey = $key.ToLowerInvariant()
        if ($Parts.ContainsKey($normalizedKey) -and -not [string]::IsNullOrWhiteSpace($Parts[$normalizedKey])) {
            return $Parts[$normalizedKey]
        }
    }

    return $DefaultValue
}

function ConvertTo-PgSslMode {
    param([string]$Value)

    switch -Regex ($Value) {
        '^(?i:disable)$' { return 'disable' }
        '^(?i:require)$' { return 'require' }
        '^(?i:prefer)$' { return 'prefer' }
        '^(?i:verifyca|verify-ca)$' { return 'verify-ca' }
        '^(?i:verifyfull|verify-full)$' { return 'verify-full' }
        default { return 'prefer' }
    }
}

function Invoke-PsqlJson {
    param([Parameter(Mandatory = $true)][string]$Sql)

    $output = $Sql | & $script:PsqlCommand.Source `
        -h $script:DbHost `
        -p $script:DbPort `
        -U $script:DbUser `
        -d $script:DbName `
        -X `
        -v ON_ERROR_STOP=1 `
        -A `
        -t 2>&1

    if ($LASTEXITCODE -ne 0) {
        $message = ($output | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($message)) {
            $message = "psql failed with exit code $LASTEXITCODE."
        }

        throw $message
    }

    $json = ($output | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw "psql returned an empty result."
    }

    return $json | ConvertFrom-Json
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Set POLYCOPYTRADER_POSTGRES_CONNECTION or pass -ConnectionString."
}

if ($LookbackMinutes -lt 1) {
    throw "-LookbackMinutes must be at least 1."
}

if ($MaxDelaySeconds -le 0) {
    throw "-MaxDelaySeconds must be greater than 0."
}

if ($MaxHeartbeatAgeSeconds -lt 1) {
    throw "-MaxHeartbeatAgeSeconds must be at least 1."
}

if ($MinRows -lt 0) {
    throw "-MinRows must be non-negative."
}

$script:PsqlCommand = Get-Command $PsqlPath -ErrorAction SilentlyContinue
if (-not $script:PsqlCommand) {
    throw "psql was not found. Install PostgreSQL client tools or pass -PsqlPath."
}

$connectionParts = ConvertFrom-NpgsqlConnectionString -Value $ConnectionString
$script:DbHost = if ([string]::IsNullOrWhiteSpace($HostOverride)) {
    Get-ConnectionValue -Parts $connectionParts -Keys @('host', 'server') -DefaultValue '127.0.0.1'
}
else {
    $HostOverride
}
$script:DbPort = Get-ConnectionValue -Parts $connectionParts -Keys @('port') -DefaultValue '5432'
$script:DbName = Get-ConnectionValue -Parts $connectionParts -Keys @('database', 'db') -DefaultValue 'polycopytrader'
$script:DbUser = Get-ConnectionValue -Parts $connectionParts -Keys @('username', 'user id', 'userid', 'user') -DefaultValue 'postgres'
$dbPassword = Get-ConnectionValue -Parts $connectionParts -Keys @('password', 'pwd') -DefaultValue ''
$sslMode = ConvertTo-PgSslMode -Value (Get-ConnectionValue -Parts $connectionParts -Keys @('ssl mode', 'sslmode') -DefaultValue 'Prefer')

$previousPgPassword = $env:PGPASSWORD
$previousPgSslMode = $env:PGSSLMODE
$previousPgConnectTimeout = $env:PGCONNECT_TIMEOUT

$failures = [System.Collections.Generic.List[string]]::new()

try {
    $env:PGPASSWORD = $dbPassword
    $env:PGSSLMODE = $sslMode
    $env:PGCONNECT_TIMEOUT = '5'

    $heartbeatSql = @"
WITH heartbeat AS (
    SELECT
        service_name,
        status,
        started_at_utc,
        last_heartbeat_utc,
        version,
        mode,
        current_loop,
        last_error,
        round(extract(epoch from (now() - last_heartbeat_utc))::numeric, 3) AS heartbeat_age_seconds
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
)
SELECT jsonb_build_object(
    'db_now_utc', now(),
    'heartbeat', COALESCE((SELECT to_jsonb(heartbeat) FROM heartbeat), 'null'::jsonb)
)::text;
"@

    $heartbeatResult = Invoke-PsqlJson -Sql $heartbeatSql
    $heartbeat = $heartbeatResult.heartbeat

    if ($null -eq $heartbeat) {
        $failures.Add("service heartbeat row is missing")
        Write-Output "Heartbeat: missing"
    }
    else {
        Write-Output ("Heartbeat: status={0}; mode={1}; version={2}; age={3}s; started={4}; loop={5}" -f `
            $heartbeat.status,
            $heartbeat.mode,
            $heartbeat.version,
            $heartbeat.heartbeat_age_seconds,
            $heartbeat.started_at_utc,
            $heartbeat.current_loop)

        if ([decimal]$heartbeat.heartbeat_age_seconds -gt $MaxHeartbeatAgeSeconds) {
            $failures.Add("service heartbeat is stale: $($heartbeat.heartbeat_age_seconds)s > ${MaxHeartbeatAgeSeconds}s")
        }

        if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit) -and
            ($heartbeat.version -notlike "*$ExpectedCommit*")) {
            $failures.Add("service version '$($heartbeat.version)' does not contain expected commit '$ExpectedCommit'")
        }

        if (-not [string]::IsNullOrWhiteSpace($heartbeat.last_error)) {
            Write-Output ("Heartbeat last_error: {0}" -f $heartbeat.last_error)
        }
    }

    $maxDelayLiteral = $MaxDelaySeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $entrySql = @"
WITH service AS (
    SELECT started_at_utc
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
), bounds AS (
    SELECT
        now() AS db_now_utc,
        greatest(
            COALESCE((SELECT started_at_utc FROM service), now() - interval '$LookbackMinutes minutes'),
            now() - interval '$LookbackMinutes minutes'
        ) AS window_start_utc
), recent_entered AS MATERIALIZED (
    SELECT
        run.id,
        run.strategy_id,
        run.paper_order_id,
        run.market_slug,
        run.status,
        run.skip_reason,
        run.entry_due_at_utc,
        run.entered_at_utc,
        run.updated_at_utc
    FROM strategy_market_paper_runs run
    WHERE run.entered_at_utc IS NOT NULL
      AND run.entry_due_at_utc IS NOT NULL
      AND run.status IN ('Entered', 'Settled')
    ORDER BY run.entered_at_utc DESC
    LIMIT 20000
), recent_skipped AS MATERIALIZED (
    SELECT
        run.id,
        run.strategy_id,
        run.paper_order_id,
        run.market_slug,
        run.status,
        run.skip_reason,
        run.entry_due_at_utc,
        run.entered_at_utc,
        run.updated_at_utc
    FROM strategy_market_paper_runs run
    WHERE run.entry_due_at_utc IS NOT NULL
      AND run.status = 'Skipped'
    ORDER BY run.updated_at_utc DESC
    LIMIT 50000
), runs AS MATERIALIZED (
    SELECT * FROM recent_entered
    UNION ALL
    SELECT * FROM recent_skipped
), events_raw AS (
    SELECT
        run.id,
        run.strategy_id,
        strategy.code,
        strategy.name,
        strategy.live_stakes,
        strategy.auto_live_paused,
        run.market_slug,
        run.status,
        run.skip_reason,
        run.entry_due_at_utc,
        CASE
            WHEN run.status IN ('Entered', 'Settled') THEN COALESCE(run.entered_at_utc, run.updated_at_utc)
            WHEN run.paper_order_id IS NOT NULL THEN COALESCE(paper_order.created_at_utc, run.updated_at_utc)
            WHEN run.status = 'Skipped' THEN run.updated_at_utc
            ELSE COALESCE(run.entered_at_utc, run.updated_at_utc)
        END AS event_at_utc
    FROM runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
    LEFT JOIN paper_orders paper_order ON paper_order.id = run.paper_order_id
    WHERE strategy.enabled
      AND lower(strategy.code) LIKE '%\_up\_down\_5m\_%' ESCAPE '\'
), events AS (
    SELECT
        events_raw.*,
        round(extract(epoch from (events_raw.event_at_utc - events_raw.entry_due_at_utc))::numeric, 3) AS delay_seconds
    FROM events_raw, bounds
    WHERE events_raw.event_at_utc IS NOT NULL
      AND events_raw.event_at_utc >= bounds.window_start_utc
)
SELECT jsonb_build_object(
    'db_now_utc', (SELECT db_now_utc FROM bounds),
    'window_start_utc', (SELECT window_start_utc FROM bounds),
    'rows_total', (SELECT count(*) FROM events),
    'strategies_total', (SELECT count(DISTINCT strategy_id) FROM events),
    'over_limit_total', (SELECT count(*) FROM events WHERE delay_seconds > $maxDelayLiteral),
    'by_strategy', COALESCE((
        SELECT jsonb_agg(to_jsonb(summary) ORDER BY summary.max_delay_seconds DESC, summary.code)
        FROM (
            SELECT
                code,
                name,
                live_stakes,
                auto_live_paused,
                count(*)::integer AS rows_total,
                count(*) FILTER (WHERE delay_seconds > $maxDelayLiteral)::integer AS over_limit,
                min(delay_seconds) AS min_delay_seconds,
                round(avg(delay_seconds)::numeric, 3) AS avg_delay_seconds,
                percentile_cont(0.95) WITHIN GROUP (ORDER BY delay_seconds)::numeric(18,3) AS p95_delay_seconds,
                max(delay_seconds) AS max_delay_seconds
            FROM events
            GROUP BY code, name, live_stakes, auto_live_paused
        ) summary
    ), '[]'::jsonb),
    'worst_runs', COALESCE((
        SELECT jsonb_agg(to_jsonb(worst) ORDER BY worst.delay_seconds DESC, worst.code)
        FROM (
            SELECT
                code,
                name,
                live_stakes,
                auto_live_paused,
                status,
                skip_reason,
                market_slug,
                entry_due_at_utc,
                event_at_utc,
                delay_seconds
            FROM events
            ORDER BY delay_seconds DESC, code
            LIMIT 20
        ) worst
    ), '[]'::jsonb)
)::text;
"@

    $entryResult = Invoke-PsqlJson -Sql $entrySql
    Write-Output ("Entry window: {0} .. {1}" -f $entryResult.window_start_utc, $entryResult.db_now_utc)
    Write-Output ("Entry rows: rows={0}; strategies={1}; over_{2}s={3}" -f `
        $entryResult.rows_total,
        $entryResult.strategies_total,
        $MaxDelaySeconds,
        $entryResult.over_limit_total)

    $byStrategy = @($entryResult.by_strategy)
    if ($byStrategy.Count -gt 0) {
        $byStrategy |
            Select-Object code, rows_total, over_limit, max_delay_seconds, p95_delay_seconds, avg_delay_seconds, live_stakes, auto_live_paused |
            Format-Table -AutoSize | Out-String -Width 240 |
            Write-Output
    }

    if ([int]$entryResult.rows_total -lt $MinRows) {
        if ($AllowNoRows) {
            Write-Output "No-row check: allowed by -AllowNoRows."
        }
        else {
            $failures.Add("entry window has $($entryResult.rows_total) rows, expected at least $MinRows")
        }
    }

    if ([int]$entryResult.over_limit_total -gt 0) {
        $failures.Add("$($entryResult.over_limit_total) entry rows exceeded ${MaxDelaySeconds}s")
        Write-Output "Worst over-limit runs:"
        @($entryResult.worst_runs) |
            Where-Object { [decimal]$_.delay_seconds -gt $MaxDelaySeconds } |
            Select-Object code, status, skip_reason, market_slug, entry_due_at_utc, event_at_utc, delay_seconds |
            Format-Table -AutoSize | Out-String -Width 260 |
            Write-Output
    }

    $stageTableSql = @"
SELECT jsonb_build_object(
    'table_present', to_regclass('public.btc_up_down_5m_strategy_stage_timings') IS NOT NULL
)::text;
"@
    $stageTableResult = Invoke-PsqlJson -Sql $stageTableSql

    if (-not [bool]$stageTableResult.table_present) {
        Write-Output "Stage timings: table btc_up_down_5m_strategy_stage_timings is missing."
        if ($RequireSplitCycleKinds) {
            $failures.Add("stage timing table is missing")
        }
    }
    else {
        $stageSql = @"
WITH service AS (
    SELECT started_at_utc
    FROM service_heartbeats
    WHERE service_name = 'PolyCopyTrader.Service'
), bounds AS (
    SELECT
        now() AS db_now_utc,
        greatest(
            COALESCE((SELECT started_at_utc FROM service), now() - interval '$LookbackMinutes minutes'),
            now() - interval '$LookbackMinutes minutes'
        ) AS window_start_utc
), stages AS (
    SELECT *
    FROM btc_up_down_5m_strategy_stage_timings, bounds
    WHERE started_at_utc >= bounds.window_start_utc
)
SELECT jsonb_build_object(
    'rows_total', (SELECT count(*) FROM stages),
    'cycle_kinds', COALESCE((
        SELECT jsonb_agg(cycle_kind ORDER BY cycle_kind)
        FROM (SELECT DISTINCT cycle_kind FROM stages) kinds
    ), '[]'::jsonb),
    'by_stage', COALESCE((
        SELECT jsonb_agg(to_jsonb(summary) ORDER BY summary.max_ms DESC, summary.cycle_kind, summary.stage_name)
        FROM (
            SELECT
                cycle_kind,
                COALESCE(flow_name, '') AS flow_name,
                stage_name,
                count(*)::integer AS rows_total,
                max(duration_ms)::bigint AS max_ms,
                round(avg(duration_ms)::numeric, 1) AS avg_ms,
                count(*) FILTER (WHERE duration_ms > 3000)::integer AS over_3000ms
            FROM stages
            GROUP BY cycle_kind, COALESCE(flow_name, ''), stage_name
            ORDER BY max(duration_ms) DESC
            LIMIT 20
        ) summary
    ), '[]'::jsonb)
)::text;
"@

        $stageResult = Invoke-PsqlJson -Sql $stageSql
        Write-Output ("Stage timings: rows={0}; cycle_kinds={1}" -f `
            $stageResult.rows_total,
            (@($stageResult.cycle_kinds) -join ', '))

        if ([int]$stageResult.rows_total -gt 0) {
            @($stageResult.by_stage) |
                Select-Object cycle_kind, flow_name, stage_name, rows_total, max_ms, avg_ms, over_3000ms |
                Format-Table -AutoSize | Out-String -Width 260 |
                Write-Output
        }

        $expectedCycleKinds = @(
            'main_due',
            'fast_diff_due',
            'fast_diff_observe',
            'previous_result_due',
            'previous_result_observe'
        )
        $presentCycleKinds = @($stageResult.cycle_kinds)
        $missingCycleKinds = @($expectedCycleKinds | Where-Object { $presentCycleKinds -notcontains $_ })
        if ($missingCycleKinds.Count -gt 0) {
            $message = "split-lane cycle kinds missing in the timing window: $($missingCycleKinds -join ', ')"
            if ($RequireSplitCycleKinds) {
                $failures.Add($message)
            }
            else {
                Write-Output "Warning: $message"
            }
        }
    }

    if ($failures.Count -gt 0) {
        Write-Output "FAIL"
        foreach ($failure in $failures) {
            Write-Output ("- {0}" -f $failure)
        }

        exit 1
    }

    Write-Output ("PASS: no enabled Up/Down 5m strategy entry exceeded {0}s in the checked window." -f $MaxDelaySeconds)
}
catch {
    Write-Output ("ERROR: {0}" -f $_.Exception.Message)
    exit 2
}
finally {
    if ($null -eq $previousPgPassword) {
        Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    }
    else {
        $env:PGPASSWORD = $previousPgPassword
    }

    if ($null -eq $previousPgSslMode) {
        Remove-Item Env:PGSSLMODE -ErrorAction SilentlyContinue
    }
    else {
        $env:PGSSLMODE = $previousPgSslMode
    }

    if ($null -eq $previousPgConnectTimeout) {
        Remove-Item Env:PGCONNECT_TIMEOUT -ErrorAction SilentlyContinue
    }
    else {
        $env:PGCONNECT_TIMEOUT = $previousPgConnectTimeout
    }
}
