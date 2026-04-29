param(
    [string]$ConnectionStringEnvironmentVariable = "POLYCOPYTRADER_POSTGRES_CONNECTION",
    [string]$BackupDirectory = "..\backups",
    [string]$PgDumpPath = "pg_dump",
    [int]$RetentionDays = 14
)

$ErrorActionPreference = "Stop"

if ($RetentionDays -lt 1) {
    throw "RetentionDays must be at least 1."
}

$connectionString = [Environment]::GetEnvironmentVariable($ConnectionStringEnvironmentVariable)
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "Environment variable $ConnectionStringEnvironmentVariable is not configured."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$backupFullPath = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot $BackupDirectory))
New-Item -ItemType Directory -Path $backupFullPath -Force | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupFile = Join-Path $backupFullPath "polycopytrader-$timestamp.dump"

Write-Host "Writing PostgreSQL backup to $backupFile"
& $PgDumpPath "--dbname=$connectionString" "--format=custom" "--file=$backupFile"
if ($LASTEXITCODE -ne 0) {
    throw "pg_dump failed with exit code $LASTEXITCODE."
}

$cutoff = (Get-Date).AddDays(-$RetentionDays)
$backupRoot = (Resolve-Path -LiteralPath $backupFullPath).Path
$backupRootPrefix = $backupRoot.TrimEnd([char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)) + [System.IO.Path]::DirectorySeparatorChar
Get-ChildItem -LiteralPath $backupRoot -Filter "polycopytrader-*.dump" -File |
    Where-Object { $_.LastWriteTime -lt $cutoff } |
    ForEach-Object {
        $candidate = (Resolve-Path -LiteralPath $_.FullName).Path
        if (-not $candidate.StartsWith($backupRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to delete backup outside ${backupRoot}: $candidate"
        }

        Remove-Item -LiteralPath $candidate
    }

Write-Host "Backup completed."
