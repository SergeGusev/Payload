using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Startup;

namespace PolyCopyTrader.Tests;

public sealed class DryRunSigningSmokeCommandTests
{
    [Fact]
    public async Task ExecuteAsync_SignsLocallyWithoutPrintingSecrets()
    {
        var privateKey = DeterministicUnfundedTestPrivateKey;
        var signerAddress = new ClobV2OrderSigner().GetAddress(privateKey);
        var output = new StringWriter();

        var exitCode = await DryRunSigningSmokeCommand.ExecuteAsync(
            CreateConfiguration(signerAddress),
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["dry-run-private-key"] = privateKey
            }),
            output,
            CancellationToken.None);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Dry-run signing smoke status: DryRunSigned", text, StringComparison.Ordinal);
        Assert.Contains("Signature present: True", text, StringComparison.Ordinal);
        Assert.Contains("Local signature verified: True", text, StringComparison.Ordinal);
        Assert.DoesNotContain(privateKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain(@"""signature"":""0x", text, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailureWhenSecretIsMissing()
    {
        var signerAddress = new ClobV2OrderSigner().GetAddress(DeterministicUnfundedTestPrivateKey);
        var output = new StringWriter();

        var exitCode = await DryRunSigningSmokeCommand.ExecuteAsync(
            CreateConfiguration(signerAddress),
            new FakeSecretProvider(new Dictionary<string, string>()),
            output,
            CancellationToken.None);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("Dry-run signing smoke status: DryRunUnsigned", text, StringComparison.Ordinal);
        Assert.Contains("Signature present: False", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailureWhenKeyDoesNotMatchSigner()
    {
        var otherSignerAddress = "0x1111111111111111111111111111111111111111";
        var output = new StringWriter();

        var exitCode = await DryRunSigningSmokeCommand.ExecuteAsync(
            CreateConfiguration(otherSignerAddress),
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["dry-run-private-key"] = DeterministicUnfundedTestPrivateKey
            }),
            output,
            CancellationToken.None);

        var text = output.ToString();
        Assert.Equal(1, exitCode);
        Assert.Contains("Dry-run signing smoke status: DryRunRejected", text, StringComparison.Ordinal);
        Assert.Contains("private key does not match", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DeterministicUnfundedTestPrivateKey, text, StringComparison.Ordinal);
    }

    private const string DeterministicUnfundedTestPrivateKey =
        "0x0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static AppConfiguration CreateConfiguration(string signingAddress)
    {
        return new AppConfiguration
        {
            Bot = new BotOptions
            {
                Mode = BotMode.DryRun,
                EnableLiveTrading = false
            },
            Polymarket = new PolymarketOptions
            {
                ClobBaseUrl = "https://clob.polymarket.com",
                TimeoutSeconds = 30
            },
            PolymarketAuth = new PolymarketAuthOptions
            {
                Enabled = false,
                SecretProvider = "CredentialManager",
                SigningAddress = signingAddress,
                FunderAddress = "0x2222222222222222222222222222222222222222",
                SignatureType = "POLY_GNOSIS_SAFE",
                DryRunSigningEnabled = true,
                DryRunPrivateKeyName = "dry-run-private-key"
            }
        };
    }

    private sealed class FakeSecretProvider(IReadOnlyDictionary<string, string> values) : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(values.TryGetValue(name, out var value) ? value : null);
        }
    }
}
