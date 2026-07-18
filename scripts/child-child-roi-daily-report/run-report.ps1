[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$FileName,
    [ValidateRange(30, 900)]
    [int]$TotalTimeoutSeconds = 180,
    [ValidateRange(10, 300)]
    [int]$DatabaseTimeoutSeconds = 100,
    [ValidateRange(10, 120)]
    [int]$WorkbookTimeoutSeconds = 45,
    [ValidateRange(5, 60)]
    [int]$ExcelVerificationTimeoutSeconds = 30,
    [ValidateRange(10, 120)]
    [int]$RenderVerificationTimeoutSeconds = 35
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..\..'))
$runStamp = Get-Date -Format 'yyyy-MM-dd-HHmmss'
$reportDate = Get-Date -Format 'yyyy-MM-dd'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "outputs\child-child-roi-best-daily-paper-pnl-report-$runStamp"
}
else {
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
}

if ([string]::IsNullOrWhiteSpace($FileName)) {
    $FileName = "child-child-roi-best-daily-paper-pnl-$reportDate.xlsx"
}

if ([IO.Path]::GetExtension($FileName) -ne '.xlsx') {
    throw "FileName must end with .xlsx: $FileName"
}

$nodeModules = Join-Path $scriptRoot 'node_modules'
if (-not (Test-Path -LiteralPath $nodeModules -PathType Container)) {
    throw @"
Spreadsheet runtime is not connected.
Expected a node_modules junction at:
  $nodeModules
Create it once so that it points to the node_modules directory supplied by the Codex workspace dependency loader.
"@
}

$reportsDirectory = Join-Path $OutputDirectory 'reports'
$workbookPath = Join-Path $reportsDirectory $FileName
$reportDataPath = Join-Path $OutputDirectory 'report-data.json'
$layoutPath = Join-Path $OutputDirectory 'workbook-layout.json'
$excelVerificationPath = Join-Path $OutputDirectory 'excel-verification.json'
$runSummaryPath = Join-Path $OutputDirectory 'run-summary.json'

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$overallStopwatch = [Diagnostics.Stopwatch]::StartNew()
$stageResults = [Collections.Generic.List[object]]::new()

function ConvertTo-CommandLineArgument {
    param([Parameter(Mandatory)][string]$Value)

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Get-RemainingSeconds {
    $remaining = $TotalTimeoutSeconds - [int][Math]::Ceiling($overallStopwatch.Elapsed.TotalSeconds)
    if ($remaining -le 0) {
        throw "REPORT_TIMEOUT: total time budget of $TotalTimeoutSeconds seconds was exhausted."
    }

    return $remaining
}

function Invoke-ReportStage {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $remainingSeconds = Get-RemainingSeconds
    $effectiveTimeoutSeconds = [Math]::Min($TimeoutSeconds, $remainingSeconds)
    $stageStopwatch = [Diagnostics.Stopwatch]::StartNew()
    $stdoutPath = Join-Path $OutputDirectory ("stage-{0:00}-{1}.stdout.log" -f ($stageResults.Count + 1), ($Name -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant())
    $stderrPath = Join-Path $OutputDirectory ("stage-{0:00}-{1}.stderr.log" -f ($stageResults.Count + 1), ($Name -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant())

    $argumentLine = ($Arguments | ForEach-Object { ConvertTo-CommandLineArgument -Value $_ }) -join ' '
    Write-Host ("[{0:HH:mm:ss}] START {1} (timeout {2}s)" -f (Get-Date), $Name, $effectiveTimeoutSeconds)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.Arguments = $argumentLine
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    try {
        if (-not $process.Start()) {
            throw "STAGE_FAILED: '$Name' could not start executable '$Executable'."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        if (-not $process.WaitForExit($effectiveTimeoutSeconds * 1000)) {
            & taskkill.exe /PID $process.Id /T /F 2>$null | Out-Null
            $terminated = $process.WaitForExit(5000)
            $stdoutOnTimeout = if ($stdoutTask.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion) { $stdoutTask.Result } else { "Output stream did not close after taskkill; terminated=$terminated" }
            $stderrOnTimeout = if ($stderrTask.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion) { $stderrTask.Result } else { "Error stream did not close after taskkill; terminated=$terminated" }
            [IO.File]::WriteAllText($stdoutPath, $stdoutOnTimeout, [Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllText($stderrPath, $stderrOnTimeout, [Text.UTF8Encoding]::new($false))
            throw "STAGE_TIMEOUT: '$Name' exceeded $effectiveTimeoutSeconds seconds. See $stdoutPath and $stderrPath"
        }

        $process.WaitForExit()
        $stdout = $stdoutTask.Result
        $stderr = $stderrTask.Result
        [IO.File]::WriteAllText($stdoutPath, $stdout, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($stderrPath, $stderr, [Text.UTF8Encoding]::new($false))

        $exitCode = $process.ExitCode
        if ($exitCode -ne 0) {
            $stderrTail = @(($stderr -split '\r?\n') | Select-Object -Last 30)
            $stdoutTail = @(($stdout -split '\r?\n') | Select-Object -Last 30)
            throw "STAGE_FAILED: '$Name' exited with code $exitCode.`nSTDOUT:`n$($stdoutTail -join [Environment]::NewLine)`nSTDERR:`n$($stderrTail -join [Environment]::NewLine)"
        }
        $stageStopwatch.Stop()
        $stageResult = [pscustomobject]@{
            name = $Name
            elapsed_seconds = [Math]::Round($stageStopwatch.Elapsed.TotalSeconds, 3)
            timeout_seconds = $effectiveTimeoutSeconds
            status = 'OK'
        }
        $stageResults.Add($stageResult)
        Write-Host ("[{0:HH:mm:ss}] OK    {1} ({2:N3}s)" -f (Get-Date), $Name, $stageStopwatch.Elapsed.TotalSeconds)
    }
    finally {
        $process.Dispose()
    }
}

$status = 'FAILED'
$failure = $null

try {
    Invoke-ReportStage `
        -Name 'Production snapshot and reconciliation' `
        -Executable 'dotnet' `
        -Arguments @('run', '--project', (Join-Path $scriptRoot 'ChildDailyReportExport.csproj'), '--configuration', 'Release', '--no-restore', '--', $OutputDirectory) `
        -TimeoutSeconds $DatabaseTimeoutSeconds

    Invoke-ReportStage `
        -Name 'Workbook build' `
        -Executable 'node' `
        -Arguments @((Join-Path $scriptRoot 'build-report.mjs'), $OutputDirectory, 'build', $FileName) `
        -TimeoutSeconds $WorkbookTimeoutSeconds

    Invoke-ReportStage `
        -Name 'Freeze panes' `
        -Executable 'powershell' `
        -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $scriptRoot 'ensure-freeze-panes.ps1'), '-WorkbookPath', $workbookPath) `
        -TimeoutSeconds 10

    Invoke-ReportStage `
        -Name 'Excel verification' `
        -Executable 'powershell' `
        -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $scriptRoot 'verify-workbook.ps1'), '-WorkbookPath', $workbookPath, '-ReportDataPath', $reportDataPath, '-LayoutPath', $layoutPath, '-OutputPath', $excelVerificationPath) `
        -TimeoutSeconds $ExcelVerificationTimeoutSeconds

    Invoke-ReportStage `
        -Name 'Final import and render verification' `
        -Executable 'node' `
        -Arguments @((Join-Path $scriptRoot 'build-report.mjs'), $OutputDirectory, 'verify', $FileName) `
        -TimeoutSeconds $RenderVerificationTimeoutSeconds

    if (-not (Test-Path -LiteralPath $workbookPath -PathType Leaf)) {
        throw "Workbook was not created: $workbookPath"
    }

    $status = 'OK'
}
catch {
    $failure = $_.Exception.Message
    throw
}
finally {
    $overallStopwatch.Stop()
    $summary = [ordered]@{
        status = $status
        started_at_local = (Get-Date).Subtract($overallStopwatch.Elapsed).ToString('o')
        finished_at_local = (Get-Date).ToString('o')
        elapsed_seconds = [Math]::Round($overallStopwatch.Elapsed.TotalSeconds, 3)
        total_timeout_seconds = $TotalTimeoutSeconds
        workbook_path = $workbookPath
        failure = $failure
        stages = @($stageResults)
    }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $runSummaryPath -Encoding UTF8
}

Write-Host ("REPORT_OK elapsed={0:N3}s workbook={1}" -f $overallStopwatch.Elapsed.TotalSeconds, $workbookPath)
Write-Output $workbookPath
