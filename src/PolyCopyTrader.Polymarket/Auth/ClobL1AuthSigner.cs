using System.Numerics;
using Nethereum.ABI.EIP712;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class ClobL1AuthSigner
{
    private const string DomainName = "ClobAuthDomain";
    private const string DomainVersion = "1";
    public const string AttestationMessage = "This message attests that I control the given wallet";

    private readonly Eip712TypedDataSigner typedDataSigner = new();

    public string GetAddress(string privateKey)
    {
        return new EthECKey(NormalizePrivateKey(privateKey)).GetPublicAddress();
    }

    public string Sign(
        string signingAddress,
        string timestamp,
        BigInteger nonce,
        string privateKey,
        int chainId = ClobV2ExchangeContracts.PolygonChainId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(timestamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);

        var key = new EthECKey(NormalizePrivateKey(privateKey));
        var message = new ClobAuthTypedMessage
        {
            Address = signingAddress,
            Timestamp = timestamp,
            Nonce = nonce,
            Message = AttestationMessage
        };

        return typedDataSigner.SignTypedDataV4(message, BuildTypedData(chainId), key);
    }

    public bool Verify(
        string signingAddress,
        string timestamp,
        BigInteger nonce,
        string signature,
        int chainId = ClobV2ExchangeContracts.PolygonChainId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(timestamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);

        var message = new ClobAuthTypedMessage
        {
            Address = signingAddress,
            Timestamp = timestamp,
            Nonce = nonce,
            Message = AttestationMessage
        };

        var recovered = typedDataSigner.RecoverFromSignatureV4(message, BuildTypedData(chainId), signature);
        return string.Equals(recovered, signingAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static TypedData<ClobAuthDomain> BuildTypedData(int chainId)
    {
        return new TypedData<ClobAuthDomain>
        {
            Domain = new ClobAuthDomain
            {
                Name = DomainName,
                Version = DomainVersion,
                ChainId = chainId
            },
            Types = MemberDescriptionFactory.GetTypesMemberDescription(typeof(ClobAuthDomain), typeof(ClobAuthTypedMessage)),
            PrimaryType = "ClobAuth"
        };
    }

    private static string NormalizePrivateKey(string privateKey)
    {
        return privateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? privateKey[2..]
            : privateKey;
    }

    [Struct("EIP712Domain")]
    public sealed class ClobAuthDomain
    {
        [Parameter("string", "name", 1)]
        public string Name { get; init; } = string.Empty;

        [Parameter("string", "version", 2)]
        public string Version { get; init; } = string.Empty;

        [Parameter("uint256", "chainId", 3)]
        public BigInteger ChainId { get; init; }
    }

    [Struct("ClobAuth")]
    public sealed class ClobAuthTypedMessage
    {
        [Parameter("address", "address", 1)]
        public string Address { get; init; } = string.Empty;

        [Parameter("string", "timestamp", 2)]
        public string Timestamp { get; init; } = string.Empty;

        [Parameter("uint256", "nonce", 3)]
        public BigInteger Nonce { get; init; }

        [Parameter("string", "message", 4)]
        public string Message { get; init; } = string.Empty;
    }
}
