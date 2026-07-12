using System.Net;
using System.Text.Json;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Tests;

public sealed class PolymarketRelayerClientTests
{
    private const string DeterministicUnfundedTestPrivateKey =
        "0x0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task SubmitDepositWalletBatchAsync_SendsWalletBatchWithRelayerHeaders()
    {
        var signer = new PolymarketDepositWalletBatchSigner();
        var ownerAddress = signer.GetAddress(DeterministicUnfundedTestPrivateKey);
        const string depositWalletAddress = "0x49d6fEE74b294951668a4160f450Ff1C92E94cEC";
        var handler = new RelayerHandler();
        var client = new PolymarketRelayerClient(
            new HttpClient(handler),
            new PolymarketAutoRedeemOptions
            {
                RelayerBaseUrl = "https://relayer-v2.polymarket.com",
                RelayerApiKeyName = "relayer-key",
                RelayerApiKeyAddressName = "relayer-key-address",
                RelayerSigningPrivateKeyName = "signing-key",
                RelayerSubmissionDeadlineSeconds = 600
            },
            new PolymarketAuthOptions { ChainId = 137 },
            new FakeSecretProvider(new Dictionary<string, string>
            {
                ["relayer-key"] = "api-key-fixture",
                ["relayer-key-address"] = ownerAddress,
                ["signing-key"] = DeterministicUnfundedTestPrivateKey
            }),
            signer);

        var result = await client.SubmitDepositWalletBatchAsync(
            ownerAddress,
            depositWalletAddress,
            [new PolymarketDepositWalletCall("0x4D97DCd97eC945f40cF65F87097ACe5EA0476045", "0", "0x01b7037c")],
            "auto-redeem:test",
            CancellationToken.None);

        Assert.Equal("relayer-transaction-1", result.TransactionId);
        Assert.Equal("STATE_NEW", result.State);
        Assert.Equal(["/nonce?address=" + ownerAddress + "&type=WALLET", "/submit"], handler.Paths);
        Assert.Equal("api-key-fixture", handler.SubmitRequest!.Headers.GetValues("RELAYER_API_KEY").Single());
        Assert.Equal(ownerAddress, handler.SubmitRequest.Headers.GetValues("RELAYER_API_KEY_ADDRESS").Single());

        using var json = JsonDocument.Parse(handler.SubmitBody!);
        var root = json.RootElement;
        Assert.Equal("WALLET", root.GetProperty("type").GetString());
        Assert.Equal(ownerAddress, root.GetProperty("from").GetString());
        Assert.Equal("0x00000000000Fb5C9ADea0298D729A0CB3823Cc07", root.GetProperty("to").GetString());
        Assert.Equal("7", root.GetProperty("nonce").GetString());
        Assert.StartsWith("0x", root.GetProperty("signature").GetString(), StringComparison.Ordinal);
        Assert.Equal(132, root.GetProperty("signature").GetString()!.Length);
        var walletParams = root.GetProperty("depositWalletParams");
        Assert.Equal(depositWalletAddress, walletParams.GetProperty("depositWallet").GetString());
        Assert.Equal("0x4D97DCd97eC945f40cF65F87097ACe5EA0476045", walletParams.GetProperty("calls")[0].GetProperty("target").GetString());
        Assert.Equal("0x01b7037c", walletParams.GetProperty("calls")[0].GetProperty("data").GetString());
    }

    private sealed class RelayerHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public HttpRequestMessage? SubmitRequest { get; private set; }

        public string? SubmitBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            if (request.RequestUri.PathAndQuery.StartsWith("/nonce", StringComparison.Ordinal))
            {
                return JsonResponse("""{"nonce":"7"}""");
            }

            SubmitRequest = request;
            SubmitBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return JsonResponse("""{"transactionID":"relayer-transaction-1","state":"STATE_NEW"}""");
        }

        private static HttpResponseMessage JsonResponse(string body)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
        }
    }

    private sealed class FakeSecretProvider(IReadOnlyDictionary<string, string> values) : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken ct)
        {
            return Task.FromResult(values.GetValueOrDefault(name));
        }
    }
}
