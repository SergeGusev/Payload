[CmdletBinding()]
param(
    [int] $Port = 54328,
    [string] $Database = "polycopytrader",
    [string] $Username = "polycopytrader",
    [string] $Password = "polycopytrader_local_password",
    [int] $DockerStartupTimeoutSeconds = 120,
    [switch] $PrintConnectionString
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$composeFile = Join-Path $root "docker-compose.local.yml"
$containerName = "polycopytrader-local-postgres"

function Test-DockerReady {
    $nativePreferenceVariable = Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue
    $previousNativePreference = if ($nativePreferenceVariable) { $global:PSNativeCommandUseErrorActionPreference } else { $null }

    try {
        if ($nativePreferenceVariable) {
            $global:PSNativeCommandUseErrorActionPreference = $false
        }

        & docker info 1>$null 2>$null
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
    finally {
        if ($nativePreferenceVariable) {
            $global:PSNativeCommandUseErrorActionPreference = $previousNativePreference
        }
    }
}

function Start-DockerDesktopIfPresent {
    $dockerDesktop = Join-Path $env:ProgramFiles "Docker\Docker\Docker Desktop.exe"
    if (Test-Path -LiteralPath $dockerDesktop) {
        Write-Host "Starting Docker Desktop..."
        Start-Process -FilePath $dockerDesktop -WindowStyle Hidden | Out-Null
        return
    }

    throw "Docker is installed, but the Docker daemon is not running and Docker Desktop was not found at '$dockerDesktop'."
}

if (-not (Test-DockerReady)) {
    Start-DockerDesktopIfPresent

    $deadline = (Get-Date).AddSeconds($DockerStartupTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        if (Test-DockerReady) {
            break
        }
    }
}

if (-not (Test-DockerReady)) {
    throw "Docker daemon did not become ready within $DockerStartupTimeoutSeconds seconds."
}

$env:POLYCOPYTRADER_LOCAL_POSTGRES_PORT = "$Port"
$env:POLYCOPYTRADER_LOCAL_POSTGRES_DB = $Database
$env:POLYCOPYTRADER_LOCAL_POSTGRES_USER = $Username
$env:POLYCOPYTRADER_LOCAL_POSTGRES_PASSWORD = $Password

docker compose -f $composeFile up -d postgres
if ($LASTEXITCODE -ne 0) {
    throw "docker compose failed to start local PostgreSQL."
}

$healthy = $false
foreach ($attempt in 1..60) {
    $status = docker inspect -f "{{.State.Health.Status}}" $containerName 2>$null
    if ($LASTEXITCODE -eq 0 -and $status -eq "healthy") {
        $healthy = $true
        break
    }

    Start-Sleep -Seconds 2
}

if (-not $healthy) {
    docker logs --tail 100 $containerName
    throw "Local PostgreSQL container did not become healthy."
}

$connectionString = "Host=127.0.0.1;Port=$Port;Database=$Database;Username=$Username;Password=$Password;SSL Mode=Disable;Include Error Detail=true"
Write-Host "Local PostgreSQL is healthy on 127.0.0.1:$Port."

if ($PrintConnectionString) {
    Write-Output $connectionString
}
