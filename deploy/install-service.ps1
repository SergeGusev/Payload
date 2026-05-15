param(
    [string]$ServiceName = "PolyCopyTrader.Service",
    [string]$ProjectPath = "..\src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj",
    [string]$PublishDirectory = "..\publish\service",
    [string]$Configuration = "Release",
    [switch]$Start
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script from an elevated PowerShell session."
    }
}

Assert-Administrator

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$projectFullPath = Resolve-Path (Join-Path $scriptRoot $ProjectPath)
$publishFullPath = Join-Path $scriptRoot $PublishDirectory
$publishFullPath = [System.IO.Path]::GetFullPath($publishFullPath)
$gitCommit = $null
try {
    $gitCommit = (& git -C $repoRoot rev-parse --short=12 HEAD 2>$null).Trim()
}
catch {
    $gitCommit = $null
}

Write-Host "Publishing $projectFullPath to $publishFullPath"
$publishArgs = @("publish", $projectFullPath, "-c", $Configuration, "-o", $publishFullPath)
if (-not [string]::IsNullOrWhiteSpace($gitCommit)) {
    Write-Host "Embedding Git revision $gitCommit in service informational version."
    $publishArgs += "-p:SourceRevisionId=$gitCommit"
    $publishArgs += "-p:InformationalVersion=1.0.0+$gitCommit"
}

dotnet @publishArgs

$exePath = Join-Path $publishFullPath "PolyCopyTrader.Service.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Published service executable not found: $exePath"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service $ServiceName already exists. Updating binary path."
    sc.exe config $ServiceName binPath= "`"$exePath`"" start= delayed-auto | Out-Host
}
else {
    Write-Host "Creating service $ServiceName"
    New-Service -Name $ServiceName -BinaryPathName "`"$exePath`"" -DisplayName $ServiceName -StartupType Automatic | Out-Null
    sc.exe config $ServiceName start= delayed-auto | Out-Host
}

Write-Host "Service installed."
Write-Host "Repository root: $repoRoot"
Write-Host "Publish directory: $publishFullPath"
if (-not [string]::IsNullOrWhiteSpace($gitCommit)) {
    Write-Host "Expected heartbeat version marker includes: info=1.0.0+$gitCommit"
}
Write-Host "Configure secrets through environment variables or Windows Credential Manager before starting."

if ($Start) {
    Start-Service -Name $ServiceName
    Write-Host "Service started."
}
