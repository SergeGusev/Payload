param(
    [switch]$SkipRuntimeSmoke
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "== PolyCopyTrader QA check =="
Write-Host "Root: $root"

Write-Host "== dotnet build =="
dotnet build PolyCopyTrader.sln

Write-Host "== dotnet test =="
dotnet test PolyCopyTrader.sln --no-build

Write-Host "== sanitized config =="
dotnet run --project src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -- --print-config

if ($SkipRuntimeSmoke) {
    Write-Host "== runtime smoke skipped =="
    exit 0
}

Write-Host "== runtime IPC smoke =="
$process = Start-Process `
    -FilePath dotnet `
    -ArgumentList @("run", "--project", "src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj", "--no-build") `
    -WorkingDirectory $root `
    -WindowStyle Hidden `
    -PassThru

try {
    Start-Sleep -Seconds 5
    $status = Invoke-RestMethod -Uri "http://127.0.0.1:5118/status" -Method Get
    $status | ConvertTo-Json -Compress
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
}
