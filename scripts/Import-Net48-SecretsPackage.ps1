<#
Imports a password-encrypted PolyCopyTrader.Net48 secret transfer package on
the target machine.

Run from an elevated PowerShell session on the TARGET machine. The script
prompts for the transfer package password, decrypts the package in memory, and
writes machine-level environment variables. Secret values are not printed.

Example:
  .\Import-Net48-SecretsPackage.ps1 -PackagePath .\polycopytrader-net48-secrets.enc.json
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-IsBlank {
    param([string]$Value)

    return $Value -eq $null -or $Value.Trim().Length -eq 0
}

function Convert-SecureStringToPlainText {
    param([System.Security.SecureString]$SecureValue)

    if ($SecureValue -eq $null -or $SecureValue.Length -eq 0) {
        return ""
    }

    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    }
    finally {
        if ($ptr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
        }
    }
}

function Read-TransferPassword {
    while ($true) {
        $password = Convert-SecureStringToPlainText (Read-Host -Prompt "Transfer package password" -AsSecureString)
        if (-not (Test-IsBlank $password)) {
            return $password
        }

        Write-Host "Password is required." -ForegroundColor Yellow
    }
}

function Get-KeyMaterial {
    param(
        [string]$Password,
        [byte[]]$Salt,
        [int]$Length,
        [int]$Iterations
    )

    $kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes($Password, $Salt, $Iterations)
    try {
        return $kdf.GetBytes($Length)
    }
    finally {
        $kdf.Dispose()
    }
}

function Join-Bytes {
    param(
        [byte[]]$First,
        [byte[]]$Second
    )

    $joined = New-Object byte[] ($First.Length + $Second.Length)
    [Buffer]::BlockCopy($First, 0, $joined, 0, $First.Length)
    [Buffer]::BlockCopy($Second, 0, $joined, $First.Length, $Second.Length)
    return $joined
}

function Compare-Bytes {
    param(
        [byte[]]$First,
        [byte[]]$Second
    )

    if ($First -eq $null -or $Second -eq $null -or $First.Length -ne $Second.Length) {
        return $false
    }

    $diff = 0
    for ($i = 0; $i -lt $First.Length; $i++) {
        $diff = $diff -bor ($First[$i] -bxor $Second[$i])
    }

    return $diff -eq 0
}

function Unprotect-Package {
    param(
        [object]$Package,
        [string]$Password
    )

    if ($Package.Version -ne 1 -or $Package.Algorithm -ne "AES-256-CBC-HMACSHA256") {
        throw "Unsupported transfer package format."
    }

    $salt = [Convert]::FromBase64String([string]$Package.Salt)
    $iv = [Convert]::FromBase64String([string]$Package.IV)
    $expectedTag = [Convert]::FromBase64String([string]$Package.Hmac)
    $cipherBytes = [Convert]::FromBase64String([string]$Package.CipherText)
    $iterations = [int]$Package.KdfIterations

    $keyMaterial = Get-KeyMaterial $Password $salt 64 $iterations
    $aesKey = New-Object byte[] 32
    $hmacKey = New-Object byte[] 32
    [Buffer]::BlockCopy($keyMaterial, 0, $aesKey, 0, 32)
    [Buffer]::BlockCopy($keyMaterial, 32, $hmacKey, 0, 32)

    $hmacInput = Join-Bytes $iv $cipherBytes
    $hmac = New-Object Security.Cryptography.HMACSHA256 -ArgumentList @(, $hmacKey)
    try {
        $actualTag = $hmac.ComputeHash($hmacInput)
    }
    finally {
        $hmac.Dispose()
    }

    if (-not (Compare-Bytes $expectedTag $actualTag)) {
        throw "Transfer package password is wrong or package integrity check failed."
    }

    $aes = [Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256
        $aes.Mode = [Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $aesKey
        $aes.IV = $iv

        $decryptor = $aes.CreateDecryptor()
        try {
            $plainBytes = $decryptor.TransformFinalBlock($cipherBytes, 0, $cipherBytes.Length)
        }
        finally {
            $decryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    return [Text.Encoding]::UTF8.GetString($plainBytes)
}

function Set-MachineEnvironmentVariable {
    param(
        [string]$Name,
        [string]$Value,
        [bool]$Secret
    )

    if (Test-IsBlank $Name) {
        throw "Environment variable name is empty."
    }

    if ($Value -eq $null) {
        $Value = ""
    }

    [Environment]::SetEnvironmentVariable($Name, $Value, "Machine")
    [Environment]::SetEnvironmentVariable($Name, $Value, "Process")

    if ($Secret) {
        Write-Host ("Machine secret set: {0}" -f $Name)
    }
    else {
        Write-Host ("Machine setting set: {0}" -f $Name)
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Run this script from an elevated PowerShell session. Machine environment variables require Administrator rights."
}

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw ("Transfer package not found: {0}" -f $PackagePath)
}

Write-Host "PolyCopyTrader.Net48 encrypted secret package import"
Write-Host "Secret values will not be printed."
Write-Host ""

$package = Get-Content -Raw -LiteralPath $PackagePath | ConvertFrom-Json
$transferPassword = Read-TransferPassword
$payloadJson = Unprotect-Package $package $transferPassword
$payload = $payloadJson | ConvertFrom-Json

if ($payload.Application -ne "PolyCopyTrader.Net48") {
    throw "Transfer package is not for PolyCopyTrader.Net48."
}

foreach ($setting in $payload.Settings) {
    Set-MachineEnvironmentVariable ([string]$setting.Name) ([string]$setting.Value) ([bool]$setting.Secret)
}

Write-Host ""
Write-Host "Done."
Write-Host "Restart PowerShell and restart PolyCopyTrader.Net48.Service before testing."
Write-Host "Recommended checks from the service output directory:"
Write-Host "  .\PolyCopyTrader.Net48.Service.exe --print-config"
Write-Host "  .\PolyCopyTrader.Net48.Service.exe --storage-smoke"
Write-Host "Live remains disabled by this import: Bot:Mode=Paper, Bot:EnableLiveTrading=false."
