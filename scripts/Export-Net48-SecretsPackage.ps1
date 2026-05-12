<#
Exports PolyCopyTrader.Net48 runtime configuration and secrets from this
machine into a password-encrypted transfer package.

Run on the CURRENT machine from an elevated PowerShell session. The script reads
source values from environment variables and Windows Credential Manager, then
writes only an encrypted package. It does not print secret values and does not
write plaintext secrets into this repository.

Copy the generated package and scripts\Import-Net48-SecretsPackage.ps1 to the
target machine. Run the importer there from an elevated PowerShell session.

Example:
  .\scripts\Export-Net48-SecretsPackage.ps1
  .\scripts\Export-Net48-SecretsPackage.ps1 -SkipApiCredentials
#>

param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts\net48-secret-transfer"),

    [string]$PackageName = "polycopytrader-net48-secrets.enc.json",

    [switch]$SkipApiCredentials
)

$ErrorActionPreference = "Stop"

$SigningAddress = "0x799ea2c976e59C3fa42Bd670282bCf5129487B7c"
$FunderAddress = "0x49d6fEE74b294951668a4160f450Ff1C92E94cEC"

$PostgresConnectionName = "POLYCOPYTRADER_POSTGRES_CONNECTION"
$PolygonRpcUrlName = "POLYCOPYTRADER_POLYGON_RPC_URL"

$OrderSigningPrivateKeyName = "POLYCOPYTRADER_POLYMARKET_ORDER_SIGNING_PRIVATE_KEY"
$ApiKeyName = "POLYCOPYTRADER_POLYMARKET_API_KEY"
$ApiKeyOwnerName = "POLYCOPYTRADER_POLYMARKET_API_KEY_OWNER"
$ApiSecretName = "POLYCOPYTRADER_POLYMARKET_API_SECRET"
$ApiPassphraseName = "POLYCOPYTRADER_POLYMARKET_API_PASSPHRASE"

$KdfIterations = 200000

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
        $first = Convert-SecureStringToPlainText (Read-Host -Prompt "Transfer package password" -AsSecureString)
        if (Test-IsBlank $first) {
            Write-Host "Password is required." -ForegroundColor Yellow
            continue
        }

        $second = Convert-SecureStringToPlainText (Read-Host -Prompt "Confirm transfer package password" -AsSecureString)
        if ($first -eq $second) {
            return $first
        }

        Write-Host "Passwords did not match. Try again." -ForegroundColor Yellow
    }
}

if ([type]::GetType("PolyCopyTraderSetup.WindowsCredentialReader", $false) -eq $null) {
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PolyCopyTraderSetup
{
    public static class WindowsCredentialReader
    {
        private const uint GenericCredentialType = 1;

        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string targetName, uint type, uint reservedFlag, out IntPtr credentialPtr);

        [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
        private static extern void CredFree(IntPtr credentialPtr);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        public static string Read(string targetName)
        {
            if (targetName == null || targetName.Trim().Length == 0)
            {
                return null;
            }

            IntPtr credentialPtr;
            if (!CredRead(targetName, GenericCredentialType, 0, out credentialPtr))
            {
                return null;
            }

            try
            {
                NativeCredential credential = (NativeCredential)Marshal.PtrToStructure(credentialPtr, typeof(NativeCredential));
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                {
                    return null;
                }

                byte[] bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                string utf8 = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                if (IsMostlyPrintable(utf8))
                {
                    return utf8;
                }

                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        private static bool IsMostlyPrintable(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                return false;
            }

            int controls = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (Char.IsControl(value[i]))
                {
                    controls++;
                }
            }

            return controls <= Math.Max(1, value.Length / 10);
        }
    }
}
"@
}

function Get-EnvironmentValue {
    param([string]$Name)

    foreach ($scope in @("Process", "User", "Machine")) {
        $value = [Environment]::GetEnvironmentVariable($Name, $scope)
        if (-not (Test-IsBlank $value)) {
            return $value.Trim()
        }
    }

    return ""
}

function Get-CurrentSecret {
    param(
        [string]$Name,
        [bool]$Required
    )

    $value = Get-EnvironmentValue $Name
    if (-not (Test-IsBlank $value)) {
        Write-Host ("Read source secret from environment: {0}" -f $Name)
        return $value
    }

    $credentialValue = [PolyCopyTraderSetup.WindowsCredentialReader]::Read($Name)
    if (-not (Test-IsBlank $credentialValue)) {
        Write-Host ("Read source secret from Credential Manager: {0}" -f $Name)
        return $credentialValue.Trim()
    }

    if ($Required) {
        throw ("Required source value not found in environment or Credential Manager: {0}" -f $Name)
    }

    return ""
}

function Add-Setting {
    param(
        [System.Collections.ArrayList]$Settings,
        [string]$Name,
        [string]$Value,
        [bool]$Secret
    )

    if (Test-IsBlank $Name) {
        throw "Setting name is empty."
    }

    if ($Value -eq $null) {
        $Value = ""
    }

    [void]$Settings.Add([pscustomobject]@{
        Name = $Name
        Value = $Value
        Secret = $Secret
    })
}

function New-RandomBytes {
    param([int]$Length)

    $bytes = New-Object byte[] $Length
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
        return $bytes
    }
    finally {
        $rng.Dispose()
    }
}

function Get-KeyMaterial {
    param(
        [string]$Password,
        [byte[]]$Salt,
        [int]$Length
    )

    $kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes($Password, $Salt, $KdfIterations)
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

function Protect-PlainText {
    param(
        [string]$PlainText,
        [string]$Password
    )

    $salt = New-RandomBytes 16
    $iv = New-RandomBytes 16
    $keyMaterial = Get-KeyMaterial $Password $salt 64

    $aesKey = New-Object byte[] 32
    $hmacKey = New-Object byte[] 32
    [Buffer]::BlockCopy($keyMaterial, 0, $aesKey, 0, 32)
    [Buffer]::BlockCopy($keyMaterial, 32, $hmacKey, 0, 32)

    $aes = [Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256
        $aes.Mode = [Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $aesKey
        $aes.IV = $iv

        $plainBytes = [Text.Encoding]::UTF8.GetBytes($PlainText)
        $encryptor = $aes.CreateEncryptor()
        try {
            $cipherBytes = $encryptor.TransformFinalBlock($plainBytes, 0, $plainBytes.Length)
        }
        finally {
            $encryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    $hmacInput = Join-Bytes $iv $cipherBytes
    $hmac = New-Object Security.Cryptography.HMACSHA256 -ArgumentList @(, $hmacKey)
    try {
        $tag = $hmac.ComputeHash($hmacInput)
    }
    finally {
        $hmac.Dispose()
    }

    return [pscustomobject]@{
        Version = 1
        Algorithm = "AES-256-CBC-HMACSHA256"
        Kdf = "PBKDF2"
        KdfIterations = $KdfIterations
        CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        Salt = [Convert]::ToBase64String($salt)
        IV = [Convert]::ToBase64String($iv)
        Hmac = [Convert]::ToBase64String($tag)
        CipherText = [Convert]::ToBase64String($cipherBytes)
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Run this script from an elevated PowerShell session."
}

Write-Host "PolyCopyTrader.Net48 encrypted secret package export"
Write-Host "Secret values will not be printed or written as plaintext."
Write-Host ""

$postgresConnection = Get-CurrentSecret $PostgresConnectionName $true
$orderSigningPrivateKey = Get-CurrentSecret $OrderSigningPrivateKeyName $true

$apiKey = ""
$apiKeyOwner = ""
$apiSecret = ""
$apiPassphrase = ""
if (-not $SkipApiCredentials) {
    $apiKey = Get-CurrentSecret $ApiKeyName $true
    $apiKeyOwner = Get-CurrentSecret $ApiKeyOwnerName $false
    if (Test-IsBlank $apiKeyOwner) {
        $apiKeyOwner = $apiKey
    }

    $apiSecret = Get-CurrentSecret $ApiSecretName $true
    $apiPassphrase = Get-CurrentSecret $ApiPassphraseName $true
}

$polygonRpcUrl = Get-CurrentSecret $PolygonRpcUrlName $false

$settings = New-Object System.Collections.ArrayList

Add-Setting $settings $PostgresConnectionName $postgresConnection $true
Add-Setting $settings "POLYCOPYTRADER_Storage__Provider" "PostgreSQL" $false
Add-Setting $settings "POLYCOPYTRADER_Storage__ConnectionStringEnvironmentVariable" $PostgresConnectionName $false
Add-Setting $settings "POLYCOPYTRADER_Storage__RequireConfiguredDatabase" "true" $false

Add-Setting $settings "POLYCOPYTRADER_Bot__Mode" "Paper" $false
Add-Setting $settings "POLYCOPYTRADER_Bot__EnableLiveTrading" "false" $false

Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__Enabled" "true" $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__SecretProvider" "Environment" $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__SigningAddress" $SigningAddress $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__FunderAddress" $FunderAddress $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__ChainId" "137" $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__SignatureType" "POLY_1271" $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__DryRunSigningEnabled" "true" $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__DryRunPrivateKeyName" $OrderSigningPrivateKeyName $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__OrderSigningPrivateKeyName" $OrderSigningPrivateKeyName $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__ApiKeyName" $ApiKeyName $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__ApiKeyOwnerName" $ApiKeyOwnerName $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__ApiSecretName" $ApiSecretName $false
Add-Setting $settings "POLYCOPYTRADER_PolymarketAuth__ApiPassphraseName" $ApiPassphraseName $false

Add-Setting $settings $OrderSigningPrivateKeyName $orderSigningPrivateKey $true
if (-not $SkipApiCredentials) {
    Add-Setting $settings $ApiKeyName $apiKey $true
    Add-Setting $settings $ApiKeyOwnerName $apiKeyOwner $true
    Add-Setting $settings $ApiSecretName $apiSecret $true
    Add-Setting $settings $ApiPassphraseName $apiPassphrase $true
}

if (-not (Test-IsBlank $polygonRpcUrl)) {
    Add-Setting $settings $PolygonRpcUrlName $polygonRpcUrl $true
}

$payload = [pscustomobject]@{
    Application = "PolyCopyTrader.Net48"
    CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    Settings = $settings.ToArray()
}

$transferPassword = Read-TransferPassword
$payloadJson = $payload | ConvertTo-Json -Depth 6 -Compress
$protectedPackage = Protect-PlainText $payloadJson $transferPassword

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$packagePath = Join-Path $OutputDirectory $PackageName
$protectedPackage | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $packagePath -Encoding UTF8

Write-Host ""
Write-Host ("Encrypted package written: {0}" -f $packagePath)
Write-Host "Copy this file and scripts\Import-Net48-SecretsPackage.ps1 to the target machine."
Write-Host "On the target, run PowerShell as Administrator and import with:"
Write-Host ("  .\Import-Net48-SecretsPackage.ps1 -PackagePath .\{0}" -f $PackageName)
Write-Host "Live remains disabled in the exported target config."
