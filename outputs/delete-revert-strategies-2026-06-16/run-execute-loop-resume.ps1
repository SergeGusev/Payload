$ErrorActionPreference = 'Continue'
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$Project = 'outputs/delete-revert-strategies-2026-06-16/DeleteRevertStrategies.csproj'
$OutputDir = 'outputs/delete-revert-strategies-2026-06-16'
$SummaryPath = Join-Path $OutputDir 'execute-loop-resume-summary.txt'
$StrategyLimit = 100
$BatchSize = 100
$PauseMs = 150
$ResidualSignalTimeoutMs = 100
$MaxAttempts = 3

$existingIndexes = Get-ChildItem -Path $OutputDir -Filter 'execute-loop-*.stdout.txt' |
    ForEach-Object {
        if ($_.Name -match '^execute-loop-(\d+)\.stdout\.txt$') {
            [int]$Matches[1]
        }
    }

$startIteration = 2
if ($existingIndexes) {
    $startIteration = (($existingIndexes | Measure-Object -Maximum).Maximum + 1)
}

Set-Content -Path $SummaryPath -Value "Revert cleanup resume loop started $(Get-Date -Format o); startIteration=$startIteration"

for ($iteration = $startIteration; $iteration -le 250; $iteration++) {
    $stamp = '{0:000}' -f $iteration

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $stdout = Join-Path $OutputDir "execute-loop-$stamp-attempt-$attempt.stdout.txt"
        $stderr = Join-Path $OutputDir "execute-loop-$stamp-attempt-$attempt.stderr.txt"
        Remove-Item -LiteralPath $stdout, $stderr -ErrorAction SilentlyContinue

        Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] iteration=$iteration attempt=$attempt start"

        $arguments = @(
            'run',
            '--project',
            $Project,
            '--configuration',
            'Release',
            '--',
            '--execute',
            '--strategy-limit',
            $StrategyLimit,
            '--batch-size',
            $BatchSize,
            '--pause-ms',
            $PauseMs,
            '--residual-signal-timeout-ms',
            $ResidualSignalTimeoutMs
        )

        $process = Start-Process -FilePath 'dotnet' `
            -ArgumentList $arguments `
            -WorkingDirectory (Get-Location) `
            -RedirectStandardOutput $stdout `
            -RedirectStandardError $stderr `
            -PassThru `
            -Wait `
            -WindowStyle Hidden

        $exitCode = $process.ExitCode
        $remainingLine = Select-String -Path $stdout -Pattern 'remaining strategies by code/name regex: ' | Select-Object -Last 1
        $selectedLine = Select-String -Path $stdout -Pattern 'target strategy rows selected: ' | Select-Object -Last 1
        $finishedLine = Select-String -Path $stdout -Pattern 'Revert strategy cleanup finished' | Select-Object -Last 1

        $remaining = if ($remainingLine) { ($remainingLine.Line -replace '^.*remaining strategies by code/name regex: ', '').Trim() } else { '<unknown>' }
        $selected = if ($selectedLine) { ($selectedLine.Line -replace '^.*target strategy rows selected: ', '').Trim() } else { '<unknown>' }
        $finished = if ($finishedLine) { 'yes' } else { 'no' }

        Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] iteration=$iteration attempt=$attempt exit=$exitCode selected=$selected remaining=$remaining finished=$finished stdout=$stdout stderr=$stderr"

        if ($exitCode -eq 0) {
            if ($remaining -eq '0' -or $selected -eq '0') {
                Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] complete"
                exit 0
            }

            break
        }

        if ($attempt -lt $MaxAttempts) {
            Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] iteration=$iteration retrying after non-zero exit"
            Start-Sleep -Seconds 5
        }
        else {
            Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] stopped: non-zero exit after $MaxAttempts attempts"
            exit $exitCode
        }
    }
}

Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] stopped: iteration limit reached"
exit 2
