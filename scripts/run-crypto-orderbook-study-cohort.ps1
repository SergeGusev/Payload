[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServiceExecutable,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [ValidateRange(10, 604800)]
    [int]$DurationSeconds = 259200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$servicePath = [System.IO.Path]::GetFullPath($ServiceExecutable)
if (-not (Test-Path -LiteralPath $servicePath -PathType Leaf)) {
    throw "PolyCopyTrader service executable was not found: $servicePath"
}

$outputPathRoot = [System.IO.Path]::GetPathRoot($OutputRoot)
if (-not [System.IO.Path]::IsPathRooted($OutputRoot) -or
    [string]::IsNullOrWhiteSpace($outputPathRoot) -or
    $outputPathRoot -eq [System.IO.Path]::DirectorySeparatorChar.ToString()) {
    throw 'OutputRoot must be an absolute path.'
}

$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$null = New-Item -ItemType Directory -Path $resolvedOutputRoot -Force
$startedAtUtc = [DateTimeOffset]::UtcNow
$cohortId = 'crypto-orderbook-cohort-' +
    $startedAtUtc.ToString('yyyyMMdd-HHmmss') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$cohortDirectory = Join-Path $resolvedOutputRoot (Join-Path 'cohorts' $cohortId)
$logDirectory = Join-Path $cohortDirectory 'logs'
$null = New-Item -ItemType Directory -Path $logDirectory -Force
$manifestPath = Join-Path $cohortDirectory 'cohort.json'
$assets = @('btc', 'eth', 'sol')
$children = @()
$cohortStatus = 'in_progress'
$failureReason = $null
$exitCode = 1
$utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false

function Complete-ChildOutput {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Child
    )

    if ($Child.OutputCaptured -or -not $Child.Process.HasExited) {
        return
    }

    $Child.Process.WaitForExit()
    $standardOutput = $Child.StandardOutputTask.GetAwaiter().GetResult()
    $standardError = $Child.StandardErrorTask.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText($Child.StandardOutput, $standardOutput, $utf8NoBom)
    [System.IO.File]::WriteAllText($Child.StandardError, $standardError, $utf8NoBom)
    $Child.OutputCaptured = $true
}

function Write-CohortManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Status,

        [AllowNull()]
        [string]$Failure
    )

    $childRows = @($children | ForEach-Object {
        $_.Process.Refresh()
        if ($_.Process.HasExited) {
            Complete-ChildOutput -Child $_
        }

        [ordered]@{
            Asset = $_.Asset.ToUpperInvariant()
            ProcessId = $_.Process.Id
            HasExited = $_.Process.HasExited
            ExitCode = if ($_.Process.HasExited) { $_.Process.ExitCode } else { $null }
            OutputDirectory = $_.OutputDirectory
            StandardOutput = $_.StandardOutput
            StandardError = $_.StandardError
        }
    })
    $manifest = [ordered]@{
        SchemaVersion = 1
        CohortId = $cohortId
        Status = $Status
        StartedAtUtc = $startedAtUtc.ToString('O')
        UpdatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        PlannedDurationSeconds = $DurationSeconds
        ServiceExecutable = $servicePath
        OutputRoot = $resolvedOutputRoot
        FailureReason = $Failure
        Children = $childRows
    }
    $partialPath = $manifestPath + '.partial'
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $partialPath -Encoding UTF8
    Move-Item -LiteralPath $partialPath -Destination $manifestPath -Force
}

try {
    foreach ($asset in $assets) {
        $assetOutputDirectory = Join-Path $resolvedOutputRoot (Join-Path $asset 'runs')
        $null = New-Item -ItemType Directory -Path $assetOutputDirectory -Force
        $stdoutPath = Join-Path $logDirectory ($asset + '.stdout.log')
        $stderrPath = Join-Path $logDirectory ($asset + '.stderr.log')
        $argumentLine = @(
            '--crypto-orderbook-prediction-study',
            '--crypto-orderbook-study-mode', 'collect',
            '--crypto-orderbook-study-asset', $asset,
            '--crypto-orderbook-study-source', 'json',
            '--crypto-orderbook-study-output-dir', ('"' + $assetOutputDirectory + '"'),
            '--crypto-orderbook-study-duration-seconds', $DurationSeconds
        ) -join ' '
        [System.IO.File]::WriteAllText($stdoutPath, '', $utf8NoBom)
        [System.IO.File]::WriteAllText($stderrPath, '', $utf8NoBom)
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $servicePath
        $startInfo.Arguments = $argumentLine
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            throw "Failed to start $asset collector."
        }

        $children += [pscustomobject]@{
            Asset = $asset
            Process = $process
            StandardOutputTask = $process.StandardOutput.ReadToEndAsync()
            StandardErrorTask = $process.StandardError.ReadToEndAsync()
            OutputCaptured = $false
            OutputDirectory = $assetOutputDirectory
            StandardOutput = $stdoutPath
            StandardError = $stderrPath
        }
    }

    Write-CohortManifest -Status $cohortStatus -Failure $null
    while ($true) {
        Start-Sleep -Seconds 5
        foreach ($child in $children) {
            $child.Process.Refresh()
            if ($child.Process.HasExited) {
                Complete-ChildOutput -Child $child
            }
        }

        $failedChildren = @($children | Where-Object {
            $_.Process.HasExited -and $_.Process.ExitCode -ne 0
        })
        if ($failedChildren.Count -gt 0) {
            $cohortStatus = 'failed'
            $failureReason = ($failedChildren | ForEach-Object {
                $_.Asset.ToUpperInvariant() + ' exited with code ' + $_.Process.ExitCode
            }) -join '; '
            break
        }

        $runningChildren = @($children | Where-Object { -not $_.Process.HasExited })
        if ($runningChildren.Count -eq 0) {
            $cohortStatus = 'completed'
            $exitCode = 0
            break
        }
    }
}
catch {
    $cohortStatus = 'failed'
    $failureReason = $_.Exception.GetType().Name + ': ' + $_.Exception.Message
}
finally {
    if ($cohortStatus -ne 'completed') {
        foreach ($child in $children) {
            $child.Process.Refresh()
            if (-not $child.Process.HasExited) {
                Stop-Process -Id $child.Process.Id -Force -ErrorAction SilentlyContinue
                $child.Process.WaitForExit()
            }

            Complete-ChildOutput -Child $child
        }
    }

    Write-CohortManifest -Status $cohortStatus -Failure $failureReason
}

if ($failureReason) {
    [Console]::Error.WriteLine($failureReason)
}

exit $exitCode
