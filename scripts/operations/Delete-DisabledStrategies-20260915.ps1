# REQ-001 / REQ-002: one-shot operation for the exact approved September 15 allowlist.
[CmdletBinding()]
param(
    [ValidateSet('Preview','Apply','Verify')][string]$Mode = 'Preview',
    [Parameter(Mandatory)][string]$RunRoot,
    [ValidateRange(1,100)][int]$FinancialBatchSize = 1,
    [switch]$Canary,
    [switch]$Calibration
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ContractPath = Join-Path $RepoRoot 'Codex/Requirements/Contracts/RC-20260915-delete-disabled-production-strategies.json'
$ReportPath = Join-Path $RepoRoot 'Codex/Reports/2026-09-15-disabled-strategies-deletion.json'
$Digest = 'sha256:7a12408763ae709a7095e3262023407ad30627c9808a1b05781eda07591c1e8c'
$RunRoot = [IO.Path]::GetFullPath($RunRoot).TrimEnd('\')
if (-not $RunRoot.StartsWith('D:\CodexTemp\runs\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Run root outside protected temporary location.' }
$marker = Get-Content -LiteralPath (Join-Path $RunRoot '.codex-ephemeral.json') -Raw | ConvertFrom-Json
if ($marker.owner -ne 'OpenAI Codex' -or $marker.kind -ne 'ephemeral-session' -or $marker.runPath -ne $RunRoot) { throw 'Invalid temporary ownership marker.' }
$env:TEMP = $env:TMP = $env:TMPDIR = Join-Path $RunRoot 'temp'
$contract = Get-Content -LiteralPath $ContractPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($contract.approval.status -ne 'approved' -or $contract.approval.semanticDigest -ne $Digest) { throw 'Exact approval missing.' }
$report = Get-Content -LiteralPath $ReportPath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable -DateKind String
$targets = @($contract.scope.inScope | Where-Object { $_ -match '^Target UUID=' } | ForEach-Object {
    if ($_ -notmatch '^Target UUID=([0-9a-f-]{36}); code=([a-z0-9_]+); name=(.+); preview Enabled=false, LiveStakes=false\.$') { throw 'Invalid frozen target identity.' }
    [pscustomobject]@{ id=$Matches[1]; code=$Matches[2]; name=$Matches[3]; wallet='strategy:'+$Matches[2] }
})
$idText = (($targets.id | Sort-Object) -join ',')
$idHash = [Convert]::ToHexString([Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes($idText))).ToLowerInvariant()
if ($targets.Count -ne 276 -or $idHash -ne '293def10788fe14d05af1b908ac97d49') { throw 'Frozen allowlist mismatch.' }
$b = [System.Data.Common.DbConnectionStringBuilder]::new()
$b.set_ConnectionString([Environment]::GetEnvironmentVariable('POLYCOPYTRADER_POSTGRES_CONNECTION'))
$Psql = 'D:\Program Files\PostgreSQL\17\bin\psql.exe'
$UserName = [string]$b['username']
$Password = [string]$b['password']
$ExpectedStart = '2026-09-15T19:18:53.764860Z'
$ExpectedVersion = 'info=1.0.0+fce6b1d070b87a0c1fa84d084c9a9c645ace9465; assembly=1.0.0.0; mvid=613426b7f265'
function Save-Report {
    # Atomic local journal replacement; this is audit metadata, never a data backup.
    $json = $script:report | ConvertTo-Json -Depth 35
    $scratch = Join-Path $script:RunRoot 'scratch/report-next.json'
    [IO.File]::WriteAllText($scratch, $json, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($scratch, $script:ReportPath, $true)
}
function Invoke-Sql([string]$Sql, [switch]$Mutation) {
    if($Mutation -and ($Sql -notmatch '^BEGIN READ WRITE;' -or $Sql.Trim() -notmatch '\\echo COMMITTED$')) { throw 'Invalid one-shot mutation transaction envelope.' }
    $p = [Diagnostics.Process]::new()
    $started=$false
    $p.StartInfo.FileName = $script:Psql
    $p.StartInfo.UseShellExecute = $false
    $p.StartInfo.CreateNoWindow = $true
    $p.StartInfo.RedirectStandardInput = $true
    $p.StartInfo.RedirectStandardOutput = $true
    $p.StartInfo.RedirectStandardError = $true
    foreach ($arg in @('-X','-q','-w','-A','-t','-h','192.168.0.101','-p','5432','-U',$script:UserName,'-d','polycopytrader','-v','ON_ERROR_STOP=1')) { $p.StartInfo.ArgumentList.Add($arg) }
    $p.StartInfo.Environment['PGPASSWORD'] = $script:Password
    $p.StartInfo.Environment['PGCONNECT_TIMEOUT'] = '5'
    $p.StartInfo.Environment['PGOPTIONS'] = '-c default_transaction_read_only=on -c statement_timeout=15000 -c lock_timeout=1000 -c timezone=UTC -c max_parallel_workers_per_gather=0 -c jit=off'
    try {
        if (-not $p.Start()) { throw 'Cannot start native PostgreSQL client.' }
        $started=$true
        $stdout = $p.StandardOutput.ReadToEndAsync()
        $stderr = $p.StandardError.ReadToEndAsync()
        if($Mutation) {
            $p.StandardInput.WriteLine($Sql)
        } else {
            $p.StandardInput.WriteLine("BEGIN READ ONLY; SET LOCAL statement_timeout='15s'; SET LOCAL lock_timeout='1s';")
            $p.StandardInput.WriteLine($Sql)
            $p.StandardInput.WriteLine('; ROLLBACK;')
        }
        $p.StandardInput.Close()
        if(-not $p.WaitForExit(55000)) { throw 'Bounded PostgreSQL client deadline exceeded; reconcile transaction outcome.' }
        $result = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($p.ExitCode -ne 0) { throw "SQL failed; verify transaction outcome before continuing: $errorText" }
        if($Mutation) {
            if($result.Trim() -ne 'COMMITTED') { throw 'Missing post-COMMIT acknowledgement; reconcile before continuing.' }
            return @{ committed=$true; acknowledgedAtUtc=[DateTimeOffset]::UtcNow.ToString('o') }
        }
        return $result.Trim() | ConvertFrom-Json -AsHashtable -DateKind String
    } finally {
        if($started -and -not $p.HasExited) {
            try { $p.StandardInput.Close() } catch { }
            if(-not $p.WaitForExit(1000)) { $p.Kill($true); $p.WaitForExit() }
        }
        $p.Dispose()
    }
}
function Invoke-ReadOnly([string]$Sql) { return Invoke-Sql $Sql }
function Test-Health {
    $s = Invoke-ReadOnly @'
SELECT json_build_object(
 'capturedAtUtc',clock_timestamp(),'server',inet_server_addr(),'database',current_database(),
 'status',s.status,'startedAtUtc',s.started_at_utc,'heartbeatAtUtc',s.last_heartbeat_utc,
 'heartbeatAgeSeconds',extract(epoch FROM clock_timestamp()-s.last_heartbeat_utc),
 'lastError',s.last_error,'mode',s.mode,'version',s.version,
 'disabledCount',(SELECT count(*) FROM strategies WHERE NOT enabled),
 'disabledIdHash',(SELECT md5(coalesce(string_agg(id::text,',' ORDER BY id),'')) FROM strategies WHERE NOT enabled),
 'waitingLocks',(SELECT count(*) FROM pg_stat_activity WHERE wait_event_type='Lock'))
FROM service_heartbeats s WHERE service_name='PolyCopyTrader.Service'
'@
    $expectedIds=@($script:targets.id | Where-Object {$_ -notin $script:report.committedStrategies} | Sort-Object)
    $expectedHash=[Convert]::ToHexString([Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes(($expectedIds -join ',')))).ToLowerInvariant()
    if($s.disabledCount -ne $expectedIds.Count -or $s.disabledIdHash -ne $expectedHash) {
        $script:report.status='StoppedScopeChanged'; $script:report['lastHealth']=$s; Save-Report
        throw 'The frozen disabled target set changed. No next batch allowed.'
    }
    if ($null -eq $s -or $s.server -ne '192.168.0.101' -or $s.database -ne 'polycopytrader' -or $s.status -ne 'Running' -or $s.heartbeatAgeSeconds -gt 180 -or $null -ne $s.lastError -or $s.waitingLocks -ne 0 -or [DateTimeOffset]::Parse($s.startedAtUtc) -ne [DateTimeOffset]::Parse($script:ExpectedStart) -or $s.version -ne $script:ExpectedVersion) {
        $script:report.status = 'StoppedHealthGate'
        $script:report['lastHealth'] = $s
        Save-Report
        throw "Production health gate failed; no next batch allowed: $($s | ConvertTo-Json -Compress)"
    }
    return $s
}
function Read-CoreCounts($t) {
    $id = $t.id
    $wallet = $t.wallet
    return Invoke-ReadOnly @"
SELECT json_build_object(
 'id','$id','capturedAtUtc',clock_timestamp(),
 'orders',(SELECT count(*) FROM paper_orders WHERE strategy_id='$id'),
 'fills',(SELECT count(*) FROM paper_orders o JOIN paper_fills f ON f.paper_order_id=o.id WHERE o.strategy_id='$id'),
 'runs',(SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id='$id'),
 'signals',(SELECT count(*) FROM signals WHERE trader_wallet='$wallet'),
 'signalRejections',(SELECT count(*) FROM signals s JOIN signal_rejections r ON r.signal_id=s.id WHERE s.trader_wallet='$wallet'),
 'positions',(SELECT count(*) FROM paper_positions WHERE copied_trader_wallet='$wallet'),
 'settlements',(SELECT count(*) FROM paper_position_settlements WHERE copied_trader_wallet='$wallet'),
 'archiveV1',(SELECT count(*) FROM strategy_market_paper_skip_tombstones WHERE strategy_id='$id'),
 'archiveV2',(SELECT count(*) FROM strategy_market_paper_skip_tombstones_v2 WHERE strategy_id='$id'),
 'rollups',(SELECT count(*) FROM strategy_paper_skip_rollups WHERE strategy_id='$id'),
 'openOrders',(SELECT count(*) FROM paper_orders WHERE strategy_id='$id' AND status IN ('Pending','PartiallyFilled')),
 'openPositions',(SELECT count(*) FROM paper_positions WHERE copied_trader_wallet='$wallet' AND size_shares>0),
 'liveOrders',(SELECT count(*) FROM live_orders WHERE strategy_id='$id'))
"@
}
function Read-Identities {
    return Invoke-ReadOnly @'
SELECT json_agg(x ORDER BY id) FROM
 (SELECT id,code,name,enabled,live_stakes,paused,auto_live_paused FROM strategies) x
'@
}
function Read-ChainShape($t) {
    $id=$t.id
    return Invoke-ReadOnly @"
SELECT json_build_object('id','$id','capturedAtUtc',clock_timestamp(),
 'ordersShape',(SELECT json_build_object('assetCount',count(*),'maxOrdersPerAsset',max(n),'assetsAcrossConditions',count(*) FILTER(WHERE conditions>1))
 FROM (SELECT asset_id,count(*) n,count(DISTINCT condition_id) conditions FROM paper_orders WHERE strategy_id='$id' GROUP BY asset_id) a),
 'unlinkedEnteredRuns',(SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id='$id' AND paper_order_id IS NULL AND (entered_at_utc IS NOT NULL OR settled_at_utc IS NOT NULL OR size_shares>0)),
 'unexpectedRunStates',(SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id='$id' AND status NOT IN('Observed','Skipped','Settled')),
 'futureRuns',(SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id='$id' AND market_end_utc>=clock_timestamp()))
"@
}
function Sql-Ids($Rows, [string]$Property='id') {
    $ids = @($Rows | ForEach-Object { [string]$_[$Property] } | Where-Object { $_ } | Sort-Object -Unique)
    foreach ($id in $ids) { if ($id -notmatch '^[0-9a-f-]{36}$') { throw 'Invalid dependent UUID.' } }
    if ($ids.Count -eq 0) { return 'ARRAY[]::uuid[]' }
    return "ARRAY[" + (($ids | ForEach-Object { "'$_'::uuid" }) -join ',') + "]"
}
function Read-FinancialChain($t, [int]$Limit) {
    $id=$t.id; $wallet=$t.wallet
    $sql = @"
WITH conditions AS MATERIALIZED (
 SELECT DISTINCT condition_id FROM (
 SELECT condition_id FROM paper_orders WHERE strategy_id='$id' ORDER BY created_at_utc,id LIMIT $Limit
 ) page),
o AS MATERIALIZED (SELECT id,signal_id,asset_id,condition_id,status,copied_trader_wallet
 FROM paper_orders WHERE strategy_id='$id' AND condition_id IN(SELECT condition_id FROM conditions)),
r AS MATERIALIZED (SELECT id,paper_order_id,signal_id,status,condition_id,selected_asset_id,market_end_utc
 FROM strategy_market_paper_runs WHERE strategy_id='$id' AND condition_id IN(SELECT condition_id FROM conditions)),
s AS MATERIALIZED (SELECT id,trader_wallet FROM signals WHERE id IN(SELECT signal_id FROM o UNION SELECT signal_id FROM r)),
p AS MATERIALIZED (SELECT id,asset_id,condition_id,size_shares FROM paper_positions
 WHERE lower(copied_trader_wallet)=lower('$wallet') AND copied_trader_wallet='$wallet'
 AND condition_id IN(SELECT condition_id FROM conditions)),
q AS MATERIALIZED (SELECT id,asset_id,condition_id FROM paper_position_settlements
 WHERE copied_trader_wallet='$wallet' AND condition_id IN(SELECT condition_id FROM conditions)),
f AS MATERIALIZED (SELECT f.id,f.paper_order_id FROM o JOIN paper_fills f ON f.paper_order_id=o.id),
a AS MATERIALIZED (SELECT a.audit_id id,a.source_kind,a.source_id,a.strategy_id
 FROM (SELECT 'PaperRun'::text kind,id FROM r UNION ALL SELECT 'PaperPosition',id FROM p
 UNION ALL SELECT 'PaperSettlement',id FROM q UNION ALL SELECT 'PaperSellFill',id FROM f) k
 CROSS JOIN LATERAL (SELECT audit_id,source_kind,source_id,strategy_id
 FROM historical_gross_net_parity_audit WHERE source_kind=k.kind AND source_id=k.id OFFSET 0) a)
SELECT json_build_object('capturedAtUtc',clock_timestamp(),
 'conditions',(SELECT json_agg(condition_id) FROM conditions),
 'orders',(SELECT json_agg(o) FROM o),'runs',(SELECT json_agg(r) FROM r),
 'signals',(SELECT json_agg(s) FROM s),'positions',(SELECT json_agg(p) FROM p),
 'settlements',(SELECT json_agg(q) FROM q),'audit',(SELECT json_agg(a) FROM a),
 'fills',(SELECT json_agg(f) FROM f),
 'rejections',(SELECT json_agg(json_build_object('id',v.id,'signal_id',v.signal_id))
 FROM s JOIN signal_rejections v ON v.signal_id=s.id))
"@
    $g=Invoke-ReadOnly $sql
    foreach($key in @('conditions','orders','runs','signals','positions','settlements','audit','fills','rejections')) {
        if($null -eq $g[$key]) { $g[$key]=@() }
    }
    if($g.conditions.Count -gt $Limit -or $g.conditions.Count -gt 100) { throw 'Financial chain cap exceeded.' }
    $chainAssets=@(@($g.orders | ForEach-Object {$_.asset_id}) + @($g.positions | ForEach-Object {$_.asset_id}) + @($g.settlements | ForEach-Object {$_.asset_id}) | Sort-Object -Unique)
    if($chainAssets.Count -gt $Limit) {
        if($Limit -le 1) { throw 'Canary condition contains more than one wallet/asset financial chain.' }
        return Read-FinancialChain $t ([Math]::Max(1,[Math]::Floor($Limit/2)))
    }
    $g['chainCount']=$chainAssets.Count
    if(@($g.orders | Where-Object {$_.copied_trader_wallet -ne $wallet -or $_.status -ne 'Filled'}).Count) { throw 'Unexpected financial order ownership/status.' }
    if(@($g.signals | Where-Object {$_.trader_wallet -ne $wallet}).Count) { throw 'Shared/foreign signal.' }
    if(@($g.positions | Where-Object {$_.size_shares -ne 0}).Count) { throw 'Nonzero position.' }
    if(@($g.audit | Where-Object {$_.strategy_id -ne $id}).Count) { throw 'Conflicting audit ownership.' }
    $orderIds=@($g.orders | ForEach-Object {$_.id})
    foreach($r in $g.runs) {
        if($r.status -notin @('Settled','Skipped','Observed') -or [DateTimeOffset]::Parse($r.market_end_utc) -gt [DateTimeOffset]::UtcNow) { throw 'Active target run.' }
        if($r.paper_order_id -and $r.paper_order_id -notin $orderIds) { throw 'Run points outside complete chain.' }
    }
    return $g
}
function New-FinancialDeleteSql($t,$g) {
    $id=$t.id; $wallet=$t.wallet; $code=$t.code
    $orders=Sql-Ids $g.orders; $runs=Sql-Ids $g.runs; $signals=Sql-Ids $g.signals
    $positions=Sql-Ids $g.positions; $settlements=Sql-Ids $g.settlements
    $fills=Sql-Ids $g.fills; $rejections=Sql-Ids $g.rejections; $audit=Sql-Ids $g.audit
    $conditions = "ARRAY["+(($g.conditions | ForEach-Object {
        if($_ -notmatch '^0x[0-9a-f]+$') { throw 'Unexpected condition identity.' }
        "'$_'"
    }) -join ',')+"]::text[]"
    $countOrders=$g.orders.Count; $countRuns=$g.runs.Count; $countSignals=$g.signals.Count
    $countPositions=$g.positions.Count; $countSettlements=$g.settlements.Count
    $countFills=$g.fills.Count; $countRejections=$g.rejections.Count; $countAudit=$g.audit.Count
    $overlapCutoff='2026-09-15T19:26:00Z'
    return @"
BEGIN READ WRITE;
SET LOCAL statement_timeout='15s';
SET LOCAL lock_timeout='1s';
SET LOCAL idle_in_transaction_session_timeout='20s';
DO `$guard`$
BEGIN
 IF host(inet_server_addr())<>'192.168.0.101' OR current_database()<>'polycopytrader' THEN RAISE EXCEPTION 'Wrong database'; END IF;
 IF NOT EXISTS(SELECT 1 FROM service_heartbeats WHERE service_name='PolyCopyTrader.Service'
 AND status='Running' AND last_error IS NULL AND clock_timestamp()-last_heartbeat_utc<=interval '180 seconds'
 AND started_at_utc='$ExpectedStart' AND version='$ExpectedVersion') THEN RAISE EXCEPTION 'Health changed'; END IF;
 IF EXISTS(SELECT 1 FROM pg_stat_activity WHERE wait_event_type='Lock' AND pid<>pg_backend_pid()) THEN RAISE EXCEPTION 'Waiting locks'; END IF;
 PERFORM id FROM strategies WHERE id='$id' AND code='$code' AND NOT enabled AND NOT live_stakes FOR NO KEY UPDATE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Frozen strategy changed'; END IF;
 IF NOT pg_try_advisory_xact_lock(hashtextextended('$wallet',4937427318840178337)) THEN RAISE EXCEPTION 'Wallet busy'; END IF;
 PERFORM id FROM strategy_market_paper_runs WHERE id=ANY($runs) ORDER BY id FOR UPDATE;
 PERFORM id FROM paper_orders WHERE id=ANY($orders) ORDER BY id FOR UPDATE;
 PERFORM id FROM paper_fills WHERE id=ANY($fills) ORDER BY id FOR UPDATE;
 PERFORM id FROM signals WHERE id=ANY($signals) ORDER BY id FOR UPDATE;
 PERFORM id FROM paper_positions WHERE id=ANY($positions) ORDER BY id FOR UPDATE;
 PERFORM id FROM paper_position_settlements WHERE id=ANY($settlements) ORDER BY id FOR UPDATE;
 IF EXISTS(SELECT 1 FROM paper_orders WHERE strategy_id='$id' AND status IN('Pending','PartiallyFilled')) THEN RAISE EXCEPTION 'Open order'; END IF;
 IF EXISTS(SELECT 1 FROM paper_positions WHERE copied_trader_wallet='$wallet' AND size_shares>0) THEN RAISE EXCEPTION 'Open position'; END IF;
 IF EXISTS(SELECT 1 FROM live_orders WHERE strategy_id='$id') THEN RAISE EXCEPTION 'Live history'; END IF;
 IF (SELECT count(*) FROM paper_orders WHERE id=ANY($orders) AND strategy_id='$id' AND copied_trader_wallet='$wallet' AND status='Filled')<>$countOrders THEN RAISE EXCEPTION 'Order count/ownership changed'; END IF;
 IF (SELECT count(*) FROM paper_orders WHERE strategy_id='$id' AND condition_id=ANY($conditions))<>$countOrders THEN RAISE EXCEPTION 'Incomplete order chain'; END IF;
 IF EXISTS(SELECT id FROM paper_orders WHERE strategy_id='$id' AND condition_id=ANY($conditions) EXCEPT SELECT unnest($orders))
 OR EXISTS(SELECT unnest($orders) EXCEPT SELECT id FROM paper_orders WHERE strategy_id='$id' AND condition_id=ANY($conditions)) THEN RAISE EXCEPTION 'Order ID closure changed'; END IF;
 IF (SELECT count(*) FROM strategy_market_paper_runs WHERE id=ANY($runs) AND strategy_id='$id' AND status IN('Settled','Skipped','Observed') AND market_end_utc<clock_timestamp())<>$countRuns THEN RAISE EXCEPTION 'Run count/state changed'; END IF;
 IF EXISTS(SELECT id FROM strategy_market_paper_runs WHERE strategy_id='$id' AND condition_id=ANY($conditions) EXCEPT SELECT unnest($runs))
 OR EXISTS(SELECT unnest($runs) EXCEPT SELECT id FROM strategy_market_paper_runs WHERE strategy_id='$id' AND condition_id=ANY($conditions)) THEN RAISE EXCEPTION 'Run closure changed'; END IF;
 IF EXISTS(SELECT 1 FROM strategy_market_paper_runs WHERE paper_order_id=ANY($orders) AND NOT(id=ANY($runs))) THEN RAISE EXCEPTION 'External run/order reference'; END IF;
 IF EXISTS(SELECT 1 FROM strategy_market_paper_runs WHERE signal_id=ANY($signals) AND NOT(id=ANY($runs))) THEN RAISE EXCEPTION 'External run/signal reference'; END IF;
 IF EXISTS(SELECT 1 FROM live_orders WHERE paper_order_id=ANY($orders)) THEN RAISE EXCEPTION 'Live reference'; END IF;
 IF (SELECT count(*) FROM (SELECT id FROM live_orders ORDER BY id LIMIT 10001) v)>10000
 OR (SELECT count(*) FROM (SELECT id FROM dry_run_orders ORDER BY id LIMIT 10001) v)>10000 THEN RAISE EXCEPTION 'Live/dry-run reference scan cap exceeded'; END IF;
 IF EXISTS(SELECT 1 FROM (SELECT signal_id FROM live_orders ORDER BY id LIMIT 10001) v WHERE signal_id=ANY($signals))
 OR EXISTS(SELECT 1 FROM (SELECT signal_id FROM dry_run_orders ORDER BY id LIMIT 10001) v WHERE signal_id=ANY($signals)) THEN RAISE EXCEPTION 'Live/dry-run signal-only reference'; END IF;
 IF EXISTS(SELECT 1 FROM paper_live_shadow_decisions WHERE paper_order_id=ANY($orders))
 OR EXISTS(SELECT 1 FROM paper_live_shadow_decisions WHERE signal_id=ANY($signals)) THEN RAISE EXCEPTION 'Shadow reference'; END IF;
 IF EXISTS(SELECT 1 FROM polymarket_onchain_paper_signal_results WHERE paper_order_id=ANY($orders))
 OR EXISTS(SELECT 1 FROM polymarket_onchain_paper_signal_results WHERE signal_id=ANY($signals)) THEN RAISE EXCEPTION 'Onchain reference'; END IF;
 IF (SELECT count(*) FROM signals WHERE id=ANY($signals) AND trader_wallet='$wallet')<>$countSignals THEN RAISE EXCEPTION 'Signal ownership changed'; END IF;
 IF EXISTS(SELECT paper_order_id FROM strategy_market_paper_runs WHERE id=ANY($runs) AND paper_order_id IS NOT NULL EXCEPT SELECT unnest($orders)) THEN RAISE EXCEPTION 'Current run order link changed'; END IF;
 IF EXISTS((SELECT signal_id FROM paper_orders WHERE id=ANY($orders) UNION SELECT signal_id FROM strategy_market_paper_runs WHERE id=ANY($runs) AND signal_id IS NOT NULL) EXCEPT SELECT unnest($signals))
 OR EXISTS(SELECT unnest($signals) EXCEPT (SELECT signal_id FROM paper_orders WHERE id=ANY($orders) UNION SELECT signal_id FROM strategy_market_paper_runs WHERE id=ANY($runs) AND signal_id IS NOT NULL)) THEN RAISE EXCEPTION 'Current signal link set changed'; END IF;
 IF EXISTS(SELECT 1 FROM (SELECT 'StrategyRun'::text kind,unnest($runs) id UNION ALL SELECT 'PaperOrder',unnest($orders) UNION ALL SELECT 'PaperFill',unnest($fills)) k CROSS JOIN LATERAL (SELECT strategy_id FROM dashboard_strategy_recent_projection_facts WHERE source_kind=k.kind AND source_id=k.id OFFSET 0) f WHERE f.strategy_id<>'$id') THEN RAISE EXCEPTION 'Retained strategy projection references target source'; END IF;
 IF EXISTS(SELECT 1 FROM dashboard_strategy_position_projection_facts WHERE source_id=ANY($positions) AND strategy_id<>'$id') THEN RAISE EXCEPTION 'Retained strategy position projection'; END IF;
 IF (SELECT count(*) FROM paper_positions WHERE id=ANY($positions) AND copied_trader_wallet='$wallet' AND size_shares=0)<>$countPositions THEN RAISE EXCEPTION 'Position ownership/state changed'; END IF;
 IF (SELECT count(*) FROM paper_position_settlements WHERE id=ANY($settlements) AND copied_trader_wallet='$wallet')<>$countSettlements THEN RAISE EXCEPTION 'Settlement ownership changed'; END IF;
 IF EXISTS(SELECT id FROM paper_positions WHERE lower(copied_trader_wallet)=lower('$wallet') AND copied_trader_wallet='$wallet' AND condition_id=ANY($conditions) EXCEPT SELECT unnest($positions)) THEN RAISE EXCEPTION 'Position closure changed'; END IF;
 IF EXISTS(SELECT unnest($positions) EXCEPT SELECT id FROM paper_positions WHERE lower(copied_trader_wallet)=lower('$wallet') AND copied_trader_wallet='$wallet' AND condition_id=ANY($conditions)) THEN RAISE EXCEPTION 'Captured position condition changed'; END IF;
 IF EXISTS(SELECT id FROM paper_position_settlements WHERE copied_trader_wallet='$wallet' AND condition_id=ANY($conditions) EXCEPT SELECT unnest($settlements)) THEN RAISE EXCEPTION 'Settlement closure changed'; END IF;
 IF EXISTS(SELECT unnest($settlements) EXCEPT SELECT id FROM paper_position_settlements WHERE copied_trader_wallet='$wallet' AND condition_id=ANY($conditions)) THEN RAISE EXCEPTION 'Captured settlement condition changed'; END IF;
 IF EXISTS(SELECT id FROM paper_fills WHERE paper_order_id=ANY($orders) EXCEPT SELECT unnest($fills)) THEN RAISE EXCEPTION 'Fill closure changed'; END IF;
 IF EXISTS(SELECT id FROM signal_rejections WHERE signal_id=ANY($signals) EXCEPT SELECT unnest($rejections)) THEN RAISE EXCEPTION 'Rejection closure changed'; END IF;
 IF (SELECT count(*) FROM (SELECT id FROM paper_orders WHERE created_at_utc>='$overlapCutoff' ORDER BY created_at_utc DESC LIMIT 10001) recent)>10000 THEN RAISE EXCEPTION 'Recent overlap cap exceeded'; END IF;
 IF EXISTS(SELECT 1 FROM (SELECT id,signal_id FROM paper_orders WHERE created_at_utc>='$overlapCutoff' ORDER BY created_at_utc DESC LIMIT 10001) recent WHERE signal_id=ANY($signals) AND NOT(id=ANY($orders))) THEN RAISE EXCEPTION 'New external order/signal reference'; END IF;
 IF EXISTS(SELECT a.audit_id FROM (SELECT 'PaperRun'::text kind,unnest($runs) id UNION ALL SELECT 'PaperPosition',unnest($positions) UNION ALL SELECT 'PaperSettlement',unnest($settlements) UNION ALL SELECT 'PaperSellFill',unnest($fills)) k CROSS JOIN LATERAL (SELECT audit_id FROM historical_gross_net_parity_audit WHERE source_kind=k.kind AND source_id=k.id OFFSET 0) a EXCEPT SELECT unnest($audit)) THEN RAISE EXCEPTION 'Audit closure changed'; END IF;
END;
`$guard`$;
DO `$delete`$
DECLARE n bigint;
BEGIN
 DELETE FROM strategy_market_paper_runs WHERE id=ANY($runs) AND strategy_id='$id';
 GET DIAGNOSTICS n=ROW_COUNT; IF n<>$countRuns THEN RAISE EXCEPTION 'Run delete count %',n; END IF;
 DELETE FROM paper_fills WHERE id=ANY($fills) AND paper_order_id=ANY($orders);
 GET DIAGNOSTICS n=ROW_COUNT; IF n<>$countFills THEN RAISE EXCEPTION 'Fill delete count %',n; END IF;
 DELETE FROM paper_orders WHERE id=ANY($orders) AND strategy_id='$id' AND copied_trader_wallet='$wallet';
 GET DIAGNOSTICS n=ROW_COUNT; IF n<>$countOrders THEN RAISE EXCEPTION 'Order delete count %',n; END IF;
 DELETE FROM signal_rejections WHERE $countRejections>0 AND id=ANY($rejections) AND signal_id=ANY($signals);
 GET DIAGNOSTICS n=ROW_COUNT; IF n<>$countRejections THEN RAISE EXCEPTION 'Rejection delete count %',n; END IF;
 DELETE FROM signals WHERE id=ANY($signals) AND trader_wallet='$wallet';
 GET DIAGNOSTICS n=ROW_COUNT; IF n<>$countSignals THEN RAISE EXCEPTION 'Signal delete count %',n; END IF;
 DELETE FROM paper_positions WHERE id=ANY($positions) AND copied_trader_wallet='$wallet' AND size_shares=0;
 GET DIAGNOSTICS n=ROW_COUNT; IF n<>$countPositions THEN RAISE EXCEPTION 'Position delete count %',n; END IF;
 DELETE FROM paper_position_settlements WHERE id=ANY($settlements) AND copied_trader_wallet='$wallet';
 GET DIAGNOSTICS n=ROW_COUNT; IF n<>$countSettlements THEN RAISE EXCEPTION 'Settlement delete count %',n; END IF;
 DELETE FROM historical_gross_net_parity_audit WHERE $countAudit>0 AND audit_id=ANY($audit) AND strategy_id='$id';
 GET DIAGNOSTICS n=ROW_COUNT; IF n<>$countAudit THEN RAISE EXCEPTION 'Audit delete count %',n; END IF;
 IF EXISTS(SELECT 1 FROM paper_orders WHERE strategy_id='$id' AND condition_id=ANY($conditions))
 OR EXISTS(SELECT 1 FROM strategy_market_paper_runs WHERE strategy_id='$id' AND condition_id=ANY($conditions))
 OR EXISTS(SELECT 1 FROM paper_positions WHERE lower(copied_trader_wallet)=lower('$wallet') AND copied_trader_wallet='$wallet' AND condition_id=ANY($conditions))
 OR EXISTS(SELECT 1 FROM paper_position_settlements WHERE copied_trader_wallet='$wallet' AND condition_id=ANY($conditions)) THEN RAISE EXCEPTION 'Post-delete financial chain not empty'; END IF;
 IF EXISTS(SELECT 1 FROM paper_fills WHERE paper_order_id=ANY($orders))
 OR EXISTS(SELECT 1 FROM signals WHERE id=ANY($signals))
 OR EXISTS(SELECT 1 FROM signal_rejections WHERE signal_id=ANY($signals)) THEN RAISE EXCEPTION 'Post-delete source dependency remains'; END IF;
 IF EXISTS(SELECT 1 FROM (SELECT 'PaperRun'::text kind,unnest($runs) id UNION ALL SELECT 'PaperPosition',unnest($positions) UNION ALL SELECT 'PaperSettlement',unnest($settlements) UNION ALL SELECT 'PaperSellFill',unnest($fills)) k CROSS JOIN LATERAL (SELECT audit_id FROM historical_gross_net_parity_audit WHERE source_kind=k.kind AND source_id=k.id OFFSET 0) a) THEN RAISE EXCEPTION 'Post-delete source audit remains'; END IF;
END;
`$delete`$;
COMMIT;
\echo COMMITTED
"@
}
function Test-GraphAbsent($g) {
    $orders=Sql-Ids $g.orders; $runs=Sql-Ids $g.runs; $signals=Sql-Ids $g.signals
    $positions=Sql-Ids $g.positions; $settlements=Sql-Ids $g.settlements
    $fills=Sql-Ids $g.fills; $rejections=Sql-Ids $g.rejections; $audit=Sql-Ids $g.audit
    $v=Invoke-ReadOnly @"
SELECT json_build_object('capturedAtUtc',clock_timestamp(),
 'orders',(SELECT count(*) FROM paper_orders WHERE id=ANY($orders)),
 'fills',(SELECT count(*) FROM paper_fills WHERE id=ANY($fills)),
 'runs',(SELECT count(*) FROM strategy_market_paper_runs WHERE id=ANY($runs)),
 'signals',(SELECT count(*) FROM signals WHERE id=ANY($signals)),
 'positions',(SELECT count(*) FROM paper_positions WHERE id=ANY($positions)),
 'settlements',(SELECT count(*) FROM paper_position_settlements WHERE id=ANY($settlements)),
 'rejections',(SELECT count(*) FROM signal_rejections WHERE id=ANY($rejections)),
 'audit',(SELECT count(*) FROM historical_gross_net_parity_audit WHERE audit_id=ANY($audit)))
"@
    foreach($key in @('orders','fills','runs','signals','positions','settlements','rejections','audit')) {
        if($v[$key] -ne 0) { throw "Post-commit source remains: $key" }
    }
    return $v
}
function Test-CoreReconciliation($t) {
    $initial=@($script:report.coreBaseline | Where-Object {$_.id -eq $t.id})
    if($initial.Count -ne 1) { throw 'Missing unique target baseline.' }
    $current=Read-CoreCounts $t
    foreach($key in @('orders','fills','runs','signals','positions','settlements')) {
        $deleted=0L
        foreach($batch in $script:report.batches) {
            if($batch.targetId -eq $t.id -and $batch.status -in @('Committed','Verified')) {
                if($batch.counts.Contains($key)) { $deleted += [long]$batch.counts[$key] }
            }
        }
        if([long]$initial[0][$key]-$deleted -ne [long]$current[$key]) { throw "Initial/committed/remaining mismatch: $($t.id) $key" }
    }
    return $current
}
function Invoke-FinancialBatch($t,[int]$Limit) {
    $script:report.lastHealth=Test-Health
    $before=Test-CoreReconciliation $t
    $g=Read-FinancialChain $t $Limit
    if($g.orders.Count -eq 0) { return $false }
    $sql=New-FinancialDeleteSql $t $g
    $counts=[ordered]@{}
    foreach($key in @('orders','fills','runs','signals','positions','settlements','rejections','audit')) { $counts[$key]=$g[$key].Count }
    $batch=[ordered]@{
        sequence=$script:report.batches.Count+1; targetId=$t.id; phase='Financial'
        chainCount=$g.chainCount; status='IntentRecorded'; preparedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
        sqlSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sql))).ToLowerInvariant()
        counts=$counts; before=$before
        # Exact dependent allowlist: identifiers only, no financial payload/raw decisions.
        ids=[ordered]@{}
    }
    foreach($key in @('orders','fills','runs','signals','positions','settlements','rejections','audit')) {
        $batch.ids[$key]=@($g[$key] | ForEach-Object {$_.id})
    }
    $script:report.batches+=@($batch)
    $script:report.mutationsStarted=$true
    Save-Report
    $sw=[Diagnostics.Stopwatch]::StartNew()
    try {
        $ack=Invoke-Sql $sql -Mutation
        $sw.Stop()
        $batch.status='Committed'
        $batch['acknowledgedAtUtc']=$ack.acknowledgedAtUtc
        $batch['durationMilliseconds']=$sw.ElapsedMilliseconds
        Save-Report
        $batch['absenceVerification']=Test-GraphAbsent $g
        $batch['reconciliation']=Test-CoreReconciliation $t
        $script:report.lastHealth=Test-Health
        $batch.status='Verified'
        if(-not $Canary -and -not $Calibration) {
            $batch['dependentIdsSha256']=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($batch.ids|ConvertTo-Json -Depth 5 -Compress)))).ToLowerInvariant()
            $batch.Remove('ids')
        }
        Save-Report
        Write-Output ("COMMITTED batch={0} target={1} chains={2} orders={3} ms={4}" -f $batch.sequence,$t.id,$g.chainCount,$g.orders.Count,$sw.ElapsedMilliseconds)
        return $true
    } catch {
        $sw.Stop()
        if($batch.status -eq 'IntentRecorded') { $batch.status='UnknownOrRolledBack' }
        $batch['error']=$_.Exception.Message
        $script:report.status='StoppedRequiresReconciliation'
        Save-Report
        throw
    }
}
function Test-ApplyGate {
    & (Join-Path $script:RepoRoot 'scripts/requirements/Validate-RequirementContract.ps1') -Mode Contract -ContractPath $script:ContractPath -AllowPendingEvidence -PrintSemanticDigest | Out-Null
    if($LASTEXITCODE -ne 0) { throw 'Requirement semantic validation failed.' }
    & git -C $script:RepoRoot merge-base --is-ancestor f5fe7d9d HEAD
    if($LASTEXITCODE -ne 0) { throw 'Approval-only ancestor is missing.' }
    if(-not $script:report.preflight.complete -or $script:report.independentPreApplyReview.status -ne 'Passed') { throw 'Complete independent preflight is required.' }
    $scriptHash=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if($script:report.independentPreApplyReview.scriptSha256 -ne $scriptHash) { throw 'Actual script differs from reviewed script.' }
    if($script:report.chainShapeBaseline.Count -ne 276 -or @($script:report.chainShapeBaseline | Where-Object {$_.ordersShape.assetsAcrossConditions -or $_.unlinkedEnteredRuns -or $_.unexpectedRunStates -or $_.futureRuns}).Count) { throw 'Incomplete or failed chain-shape gate.' }
    if(@($script:report.batches | Where-Object {$_.status -notin @('Verified','RolledBackVerified')}).Count) { throw 'Unreconciled prior batch.' }
}
$report['lastHealth'] = Test-Health
if ($Mode -eq 'Apply') {
    Test-ApplyGate
    if($Calibration) {
        if($Canary -or $FinancialBatchSize -ne 10 -or $report.canary.status -ne 'Passed') { throw 'Calibration requires an independently passed canary and exactly ten complete chains at most.' }
        if($report.Contains('calibration') -and $report.calibration.status -eq 'Passed') { throw 'The bounded calibration was already completed.' }
        $report.calibration=@{status='InProgress';limit=10}; Save-Report
        Invoke-FinancialBatch $targets[0] 10
        $report.calibration=@{status='AwaitingIndependentVerification';limit=10;sequence=$report.batches[-1].sequence}; Save-Report
        return
    }
    if(-not $Canary) { throw 'Full run is still blocked until the canary is independently verified and residual execution is reviewed.' }
    if(@($report.batches | Where-Object {$_.status -ne 'RolledBackVerified'}).Count -ne 0 -or $FinancialBatchSize -ne 1) { throw 'Canary is exactly the first committed one-chain batch; prior attempts must be independently verified rolled back.' }
    $report.status='CanaryInProgress'; Save-Report
    Invoke-FinancialBatch $targets[0] 1
    $report.canary=@{status='AwaitingIndependentVerification';sequence=$report.batches[-1].sequence}; Save-Report
    return
}
$identities = @(Read-Identities)
$byId = @{}
foreach ($s in $identities) { $byId[[string]$s.id] = $s }
if ($Mode -eq 'Preview') {
    foreach ($t in $targets) {
        if (-not $byId.ContainsKey($t.id)) { throw "Target missing: $($t.id)" }
        $s = $byId[$t.id]
        if ($s.code -ne $t.code -or $s.name -ne $t.name -or $s.enabled -or $s.live_stakes) { throw "Target changed: $($t.id)" }
    }
    $disabledIds = @($identities | Where-Object { -not $_.enabled } | ForEach-Object { [string]$_.id } | Sort-Object)
    if (($disabledIds -join ',') -ne $idText) { throw 'Current disabled set differs from the frozen allowlist.' }
    $report['retainedIdentityBaseline'] = @($identities | Where-Object { -not $targets.id.Contains([string]$_.id) })
    $report['coreBaseline'] = @()
    $report.status = 'PreflightIncomplete'
    for ($i=0; $i -lt $targets.Count; $i++) {
        if ($i % 5 -eq 0) { $report.lastHealth = Test-Health }
        $counts = Read-CoreCounts $targets[$i]
        if ($counts.openOrders -ne 0 -or $counts.openPositions -ne 0 -or $counts.liveOrders -ne 0) { throw "Active or Live target history requires scope review: $($counts.id)" }
        $report.coreBaseline += $counts
        if (($i+1) % 5 -eq 0 -or $i+1 -eq $targets.Count) {
            Save-Report
            Write-Output ("PREVIEW {0}/{1} at {2}" -f ($i+1),$targets.Count,$counts.capturedAtUtc)
        }
    }
    $report.lastHealth = Test-Health
    $report.preflight['coreCountsComplete'] = $true
    Save-Report
    Write-Output 'Core preview complete. Full auxiliary/ownership/reviewer gates remain separate and mandatory.'
} else {
    $remaining = @($targets | Where-Object { $byId.ContainsKey($_.id) })
    $report['verification'] = @{ capturedAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); remainingStrategies=$remaining.Count; completed=$false }
    Save-Report
    Write-Output "Remaining frozen strategies: $($remaining.Count). Full history verification not yet implemented."
}
