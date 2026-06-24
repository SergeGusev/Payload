$ErrorActionPreference = 'Stop'

$Project = 'outputs/delete-revert-strategies-2026-06-16/DeleteRevertStrategies.csproj'
$OutputDir = 'outputs/delete-revert-strategies-2026-06-16'
$SummaryPath = Join-Path $OutputDir 'execute-loop-summary.txt'
$StrategyLimit = 100
$BatchSize = 100
$PauseMs = 150
$ResidualSignalTimeoutMs = 100

Set-Content -Path $SummaryPath -Value "Revert cleanup loop started $(Get-Date -Format o)"

for ($iteration = 2; $iteration -le 200; $iteration++) {
    $stamp = '{0:000}' -f $iteration
    $stdout = Join-Path $OutputDir "execute-loop-$stamp.stdout.txt"
    $stderr = Join-Path $OutputDir "execute-loop-$stamp.stderr.txt"
    Remove-Item -LiteralPath $stdout, $stderr -ErrorAction SilentlyContinue

    Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] iteration=$iteration start"

    & dotnet run --project $Project --configuration Release -- `
        --execute `
        --strategy-limit $StrategyLimit `
        --batch-size $BatchSize `
        --pause-ms $PauseMs `
        --residual-signal-timeout-ms $ResidualSignalTimeoutMs `
        > $stdout 2> $stderr

    $exitCode = $LASTEXITCODE
    $remainingLine = Select-String -Path $stdout -Pattern 'remaining strategies by code/name regex: ' | Select-Object -Last 1
    $selectedLine = Select-String -Path $stdout -Pattern 'target strategy rows selected: ' | Select-Object -Last 1
    $finishedLine = Select-String -Path $stdout -Pattern 'Revert strategy cleanup finished' | Select-Object -Last 1

    $remaining = if ($remainingLine) { ($remainingLine.Line -replace '^.*remaining strategies by code/name regex: ', '').Trim() } else { '<unknown>' }
    $selected = if ($selectedLine) { ($selectedLine.Line -replace '^.*target strategy rows selected: ', '').Trim() } else { '<unknown>' }
    $finished = if ($finishedLine) { 'yes' } else { 'no' }

    Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] iteration=$iteration exit=$exitCode selected=$selected remaining=$remaining finished=$finished stdout=$stdout stderr=$stderr"

    if ($exitCode -ne 0) {
        Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] stopped: non-zero exit"
        exit $exitCode
    }

    if ($remaining -eq '0' -or $selected -eq '0') {
        Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] complete"
        exit 0
    }
}

Add-Content -Path $SummaryPath -Value "[$(Get-Date -Format o)] stopped: iteration limit reached"
exit 2
