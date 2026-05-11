<#
Copies PolyCopyTrader.Net48 runtime configuration and secrets from this machine
to a new Windows server without writing secret values into this repository.

Run this script on the CURRENT machine, from an elevated PowerShell session.
It reads current machine values from environment variables and Windows
Credential Manager, then writes them to the target server as machine-level
environment variables.

Requirements:
  - PowerShell Remoting/WinRM is enabled on the target server.
  - The account running this command has Administrator rights on the target.
  - The target service will be restarted after this script completes.

Example:
  .\scripts\Copy-Net48-SecretsToNewServer.ps1
  .\scripts\Copy-Net48-SecretsToNewServer.ps1 -ComputerName 192.168.0.101
  .\scripts\Copy-Net48-SecretsToNewServer.ps1 -ComputerName 192.168.0.101 -Credential (Get-Credential)
#>

param(
    [string]$ComputerName = "192.168.0.101",

    [System.Management.Automation.PSCredential]$Credential,

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

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-IsBlank {
    param([string]$Value)

    return $Value -eq $null -or $Value.Trim().Length -eq 0
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

if (-not (Test-IsAdministrator)) {
    throw "Run this script from an elevated PowerShell session."
}

if (Test-IsBlank $ComputerName) {
    throw "ComputerName is required."
}

Write-Host "PolyCopyTrader.Net48 secret copy"
Write-Host ("Target server: {0}" -f $ComputerName)
Write-Host "Secret values will not be printed or written to a script file."
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

# Safe target default. Enable Live explicitly later only after smoke checks pass.
Add-Setting $settings "POLYCOPYTRADER_Bot__Mode" "Paper" $false
Add-Setting $settings "POLYCOPYTRADER_Bot__EnableLiveTrading" "false" $false

# Target stores secrets as machine environment variables so Windows Service
# accounts can read them without a per-user Credential Manager dependency.
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

$remoteScript = {
    param([object[]]$IncomingSettings)

    foreach ($setting in $IncomingSettings) {
        [Environment]::SetEnvironmentVariable($setting.Name, $setting.Value, "Machine")
        [Environment]::SetEnvironmentVariable($setting.Name, $setting.Value, "Process")
        if ($setting.Secret) {
            Write-Host ("Remote machine secret set: {0}" -f $setting.Name)
        }
        else {
            Write-Host ("Remote machine setting set: {0}" -f $setting.Name)
        }
    }

    "Remote machine environment updated. Restart PowerShell and PolyCopyTrader.Net48.Service on the target server."
}

Write-Host ""
Write-Host "Writing values to target machine environment..."

$invokeParameters = @{
    ComputerName = $ComputerName
    ScriptBlock = $remoteScript
    ArgumentList = @(, $settings.ToArray())
}

if ($Credential -ne $null) {
    $invokeParameters.Credential = $Credential
}

Invoke-Command @invokeParameters

Write-Host ""
Write-Host "Done."
Write-Host "On the target server, restart PowerShell/service and run from the service output directory:"
Write-Host "  .\PolyCopyTrader.Net48.Service.exe --print-config"
Write-Host "  .\PolyCopyTrader.Net48.Service.exe --storage-smoke"
Write-Host "Live remains disabled on the target: Bot:Mode=Paper, Bot:EnableLiveTrading=false."
