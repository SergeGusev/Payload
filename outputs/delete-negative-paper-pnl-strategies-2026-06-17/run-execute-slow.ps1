param(
    [int]$StrategyLimit = 10,
    [int]$BatchSize = 50,
    [int]$PauseMs = 1000,
    [int]$SleepBetweenIterationsSeconds = 10,
    [int]$MaxIterations = 50,
    [int]$MaxAttempts = 3,
    [string]$RunLabel = 'slow'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'DeleteNegativePaperPnlStrategies.csproj'
$summary = Join-Path $root 'execute-slow-summary.txt'

Add-Content -LiteralPath $summary -Value "started=$(Get-Date -Format o) label=$RunLabel strategyLimit=$StrategyLimit batchSize=$BatchSize pauseMs=$PauseMs"

for ($iteration = 1; $iteration -le $MaxIterations; $iteration++) {
    $iterationText = '{0:D3}' -f $iteration
    $completed = $false

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $prefix = Join-Path $root "execute-$RunLabel-$iterationText-attempt-$attempt"
        $stdout = "$prefix.stdout.txt"
        $stderr = "$prefix.stderr.txt"
        $resultCopy = "$prefix.result.txt"

        Add-Content -LiteralPath $summary -Value "iteration=$iteration attempt=$attempt start=$(Get-Date -Format o)"

        & dotnet run --project $project --configuration Release --no-build -- `
            --execute `
            --strategy-limit $StrategyLimit `
            --batch-size $BatchSize `
            --pause-ms $PauseMs `
            --paper-pnl-threshold -100 `
            --residual-signal-timeout-ms 1000 `
            > $stdout 2> $stderr
        $exitCode = $LASTEXITCODE

        $resultPath = Join-Path $root 'result.txt'
        if (Test-Path -LiteralPath $resultPath) {
            Copy-Item -LiteralPath $resultPath -Destination $resultCopy -Force
        }

        $selected = '<unknown>'
        $remaining = '<unknown>'
        if (Test-Path -LiteralPath $resultCopy) {
            $selectedMatch = Select-String -LiteralPath $resultCopy -Pattern 'target strategy rows selected: ([0-9]+)' | Select-Object -Last 1
            if ($selectedMatch) {
                $selected = $selectedMatch.Matches[0].Groups[1].Value
            }

            $remainingMatch = Select-String -LiteralPath $resultCopy -Pattern 'strategies with Paper total PnL < -100: ([0-9]+)' | Select-Object -Last 1
            if ($remainingMatch) {
                $remaining = $remainingMatch.Matches[0].Groups[1].Value
            }
        }

        Add-Content -LiteralPath $summary -Value "iteration=$iteration attempt=$attempt exit=$exitCode selected=$selected remaining=$remaining end=$(Get-Date -Format o)"

        if ($exitCode -eq 0) {
            $completed = $true
            if ($selected -eq '0' -or $remaining -eq '0') {
                Add-Content -LiteralPath $summary -Value "complete iteration=$iteration selected=$selected remaining=$remaining"
                exit 0
            }

            Start-Sleep -Seconds $SleepBetweenIterationsSeconds
            break
        }

        Start-Sleep -Seconds ([Math]::Max(5, $SleepBetweenIterationsSeconds))
    }

    if (-not $completed) {
        Add-Content -LiteralPath $summary -Value "failed iteration=$iteration attempts=$MaxAttempts"
        exit 1
    }
}

Add-Content -LiteralPath $summary -Value "stopped maxIterations=$MaxIterations"
exit 2
