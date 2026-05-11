<# 
Sets PolyCopyTrader.Net48 machine configuration and secrets on a new Windows server.

Run from an elevated PowerShell session on the target server.
No secret values are stored in this file. The script prompts for them at runtime.

Default secret storage:
  MachineEnvironment
    Stores secrets as machine-level environment variables and configures the app
    with PolymarketAuth:SecretProvider=Environment. This works when the app runs
    as a Windows Service under LocalSystem or another service account.

Alternative:
  CredentialManager
    Stores secrets as Windows Credential Manager generic credentials for the
    CURRENT Windows user and configures the app with
    PolymarketAuth:SecretProvider=CredentialManager. Use this only when the
    service/application runs under the same Windows account that runs this script.

Examples:
  .\Setup-Net48-NewServerSecrets.ps1
  .\Setup-Net48-NewServerSecrets.ps1 -SecretStore CredentialManager
  .\Setup-Net48-NewServerSecrets.ps1 -AlsoWriteCredentialManager
  .\Setup-Net48-NewServerSecrets.ps1 -SkipApiCredentials
#>

param(
    [ValidateSet("MachineEnvironment", "CredentialManager")]
    [string]$SecretStore = "MachineEnvironment",

    [switch]$AlsoWriteCredentialManager,

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

function Read-SecretText {
    param(
        [string]$Prompt,
        [bool]$Required
    )

    while ($true) {
        $secure = Read-Host -Prompt $Prompt -AsSecureString
        $value = Convert-SecureStringToPlainText $secure
        if (-not (Test-IsBlank $value)) {
            return $value.Trim()
        }

        if (-not $Required) {
            return ""
        }

        Write-Host "Value is required. Try again." -ForegroundColor Yellow
    }
}

function Set-MachineEnvironmentVariable {
    param(
        [string]$Name,
        [string]$Value
    )

    if (Test-IsBlank $Name) {
        throw "Environment variable name is empty."
    }

    if ($Value -eq $null) {
        $Value = ""
    }

    [Environment]::SetEnvironmentVariable($Name, $Value, "Machine")
    [Environment]::SetEnvironmentVariable($Name, $Value, "Process")
    Write-Host ("Machine environment set: {0}" -f $Name)
}

if ([type]::GetType("PolyCopyTraderSetup.WindowsCredentialWriter", $false) -eq $null) {
Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PolyCopyTraderSetup
{
    public static class WindowsCredentialWriter
    {
        private const uint GenericCredentialType = 1;
        private const uint PersistLocalMachine = 2;

        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref NativeCredential credential, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        public static void Write(string targetName, string value)
        {
            if (targetName == null || targetName.Trim().Length == 0)
            {
                throw new ArgumentException("Credential target name is empty.", "targetName");
            }

            if (value == null)
            {
                value = String.Empty;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            IntPtr blob = IntPtr.Zero;
            try
            {
                blob = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, blob, bytes.Length);

                NativeCredential credential = new NativeCredential();
                credential.Type = GenericCredentialType;
                credential.TargetName = targetName;
                credential.CredentialBlobSize = (uint)bytes.Length;
                credential.CredentialBlob = blob;
                credential.Persist = PersistLocalMachine;
                credential.UserName = "polycopytrader";

                if (!CredWrite(ref credential, 0))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                if (blob != IntPtr.Zero)
                {
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        Marshal.WriteByte(blob, i, 0);
                    }

                    Marshal.FreeHGlobal(blob);
                }
            }
        }
    }
}
"@
}

function Set-CredentialManagerSecret {
    param(
        [string]$TargetName,
        [string]$Value
    )

    [PolyCopyTraderSetup.WindowsCredentialWriter]::Write($TargetName, $Value)
    Write-Host ("Credential Manager target set: {0}" -f $TargetName)
}

function Set-SecretValue {
    param(
        [string]$Name,
        [string]$Value
    )

    if ($SecretStore -eq "MachineEnvironment") {
        Set-MachineEnvironmentVariable $Name $Value
    }
    else {
        Set-CredentialManagerSecret $Name $Value
    }

    if ($AlsoWriteCredentialManager -and $SecretStore -ne "CredentialManager") {
        Set-CredentialManagerSecret $Name $Value
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Run this script from an elevated PowerShell session. Machine environment variables require Administrator rights."
}

Write-Host "PolyCopyTrader.Net48 new-server setup"
Write-Host ("Secret store: {0}" -f $SecretStore)
Write-Host "Secret values will not be printed."
Write-Host ""

$postgresConnection = Read-SecretText "PostgreSQL connection string for $PostgresConnectionName" $true
$orderSigningPrivateKey = Read-SecretText "Polymarket order-signing private key for $OrderSigningPrivateKeyName" $true

$apiKey = ""
$apiKeyOwner = ""
$apiSecret = ""
$apiPassphrase = ""
if (-not $SkipApiCredentials) {
    $apiKey = Read-SecretText "Polymarket CLOB API key for $ApiKeyName" $true
    $apiKeyOwner = Read-SecretText "Polymarket CLOB API key owner for $ApiKeyOwnerName (press Enter to reuse API key)" $false
    if (Test-IsBlank $apiKeyOwner) {
        $apiKeyOwner = $apiKey
    }

    $apiSecret = Read-SecretText "Polymarket CLOB API secret for $ApiSecretName" $true
    $apiPassphrase = Read-SecretText "Polymarket CLOB API passphrase for $ApiPassphraseName" $true
}

$polygonRpcUrl = Read-SecretText "Optional Polygon RPC URL for $PolygonRpcUrlName (press Enter to skip)" $false

$effectiveSecretProvider = "Environment"
if ($SecretStore -eq "CredentialManager") {
    $effectiveSecretProvider = "CredentialManager"
}

Write-Host ""
Write-Host "Writing machine configuration..."

Set-MachineEnvironmentVariable $PostgresConnectionName $postgresConnection
Set-MachineEnvironmentVariable "POLYCOPYTRADER_Storage__Provider" "PostgreSQL"
Set-MachineEnvironmentVariable "POLYCOPYTRADER_Storage__ConnectionStringEnvironmentVariable" $PostgresConnectionName
Set-MachineEnvironmentVariable "POLYCOPYTRADER_Storage__RequireConfiguredDatabase" "true"

# Safe default. Enable Live explicitly later only after smoke checks pass.
Set-MachineEnvironmentVariable "POLYCOPYTRADER_Bot__Mode" "Paper"
Set-MachineEnvironmentVariable "POLYCOPYTRADER_Bot__EnableLiveTrading" "false"

Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__Enabled" "true"
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__SecretProvider" $effectiveSecretProvider
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__SigningAddress" $SigningAddress
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__FunderAddress" $FunderAddress
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__ChainId" "137"
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__SignatureType" "POLY_1271"
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__DryRunSigningEnabled" "true"
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__DryRunPrivateKeyName" $OrderSigningPrivateKeyName
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__OrderSigningPrivateKeyName" $OrderSigningPrivateKeyName
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__ApiKeyName" $ApiKeyName
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__ApiKeyOwnerName" $ApiKeyOwnerName
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__ApiSecretName" $ApiSecretName
Set-MachineEnvironmentVariable "POLYCOPYTRADER_PolymarketAuth__ApiPassphraseName" $ApiPassphraseName

if (-not (Test-IsBlank $polygonRpcUrl)) {
    Set-MachineEnvironmentVariable $PolygonRpcUrlName $polygonRpcUrl
}

Write-Host ""
Write-Host "Writing secrets..."

Set-SecretValue $OrderSigningPrivateKeyName $orderSigningPrivateKey

if (-not $SkipApiCredentials) {
    Set-SecretValue $ApiKeyName $apiKey
    Set-SecretValue $ApiKeyOwnerName $apiKeyOwner
    Set-SecretValue $ApiSecretName $apiSecret
    Set-SecretValue $ApiPassphraseName $apiPassphrase
}

Write-Host ""
Write-Host "Done."
Write-Host "Restart PowerShell and restart PolyCopyTrader.Net48.Service before testing."
Write-Host "Recommended checks from the service output directory:"
Write-Host "  .\PolyCopyTrader.Net48.Service.exe --print-config"
Write-Host "  .\PolyCopyTrader.Net48.Service.exe --storage-smoke"
Write-Host "Live trading remains disabled by this script: Bot:Mode=Paper, Bot:EnableLiveTrading=false."

if ($SecretStore -eq "CredentialManager") {
    Write-Host ""
    Write-Host "Credential Manager note:" -ForegroundColor Yellow
    Write-Host "The credentials were written for the CURRENT Windows account."
    Write-Host "Run the app/service under this same account, or use -SecretStore MachineEnvironment."
}
