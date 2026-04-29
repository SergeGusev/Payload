using System.Numerics;
using Nethereum.ABI.EIP712;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using Eip712Domain = Nethereum.ABI.EIP712.Domain;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class ClobV2OrderSigner
{
    private const string DomainName = "Polymarket CTF Exchange";
    private const string DomainVersion = "2";

    private readonly Eip712TypedDataSigner typedDataSigner = new();

    public string GetAddress(string privateKey)
    {
        return new EthECKey(NormalizePrivateKey(privateKey)).GetPublicAddress();
    }

    public string Sign(ClobV2Order order, string privateKey, int chainId = ClobV2ExchangeContracts.PolygonChainId)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);

        var key = new EthECKey(NormalizePrivateKey(privateKey));
        var typedData = BuildTypedData(order, chainId);
        return typedDataSigner.SignTypedDataV4(ToTypedMessage(order), typedData, key);
    }

    public bool Verify(
        ClobV2Order order,
        string signature,
        string expectedSignerAddress,
        int chainId = ClobV2ExchangeContracts.PolygonChainId)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSignerAddress);

        var recovered = typedDataSigner.RecoverFromSignatureV4(
            ToTypedMessage(order),
            BuildTypedData(order, chainId),
            signature);

        return string.Equals(recovered, expectedSignerAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static TypedData<Eip712Domain> BuildTypedData(ClobV2Order order, int chainId)
    {
        return new TypedData<Eip712Domain>
        {
            Domain = new Eip712Domain
            {
                Name = DomainName,
                Version = DomainVersion,
                ChainId = chainId,
                VerifyingContract = ClobV2ExchangeContracts.GetVerifyingContract(order.NegativeRisk)
            },
            Types = MemberDescriptionFactory.GetTypesMemberDescription(typeof(Eip712Domain), typeof(OrderTypedMessage)),
            PrimaryType = "Order"
        };
    }

    private static OrderTypedMessage ToTypedMessage(ClobV2Order order)
    {
        return new OrderTypedMessage
        {
            Salt = ClobV2OrderBuilder.ParseUInt256(order.Salt),
            Maker = order.Maker,
            Signer = order.Signer,
            TokenId = ClobV2OrderBuilder.ParseUInt256(order.TokenId),
            MakerAmount = ClobV2OrderBuilder.ParseUInt256(order.MakerAmount),
            TakerAmount = ClobV2OrderBuilder.ParseUInt256(order.TakerAmount),
            Side = ClobV2OrderBuilder.SideToTypedValue(order.Side),
            SignatureType = (int)order.SignatureType,
            Timestamp = ClobV2OrderBuilder.ParseUInt256(order.Timestamp),
            Metadata = HexToBytes32(order.Metadata),
            Builder = HexToBytes32(order.Builder)
        };
    }

    private static byte[] HexToBytes32(string value)
    {
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (hex.Length > 64 || !hex.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Value must be a bytes32 hex string.", nameof(value));
        }

        return Convert.FromHexString(hex.PadLeft(64, '0'));
    }

    private static string NormalizePrivateKey(string privateKey)
    {
        return privateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? privateKey[2..]
            : privateKey;
    }

    [Struct("Order")]
    public sealed class OrderTypedMessage
    {
        [Parameter("uint256", "salt", 1)]
        public BigInteger Salt { get; init; }

        [Parameter("address", "maker", 2)]
        public string Maker { get; init; } = string.Empty;

        [Parameter("address", "signer", 3)]
        public string Signer { get; init; } = string.Empty;

        [Parameter("uint256", "tokenId", 4)]
        public BigInteger TokenId { get; init; }

        [Parameter("uint256", "makerAmount", 5)]
        public BigInteger MakerAmount { get; init; }

        [Parameter("uint256", "takerAmount", 6)]
        public BigInteger TakerAmount { get; init; }

        [Parameter("uint8", "side", 7)]
        public int Side { get; init; }

        [Parameter("uint8", "signatureType", 8)]
        public int SignatureType { get; init; }

        [Parameter("uint256", "timestamp", 9)]
        public BigInteger Timestamp { get; init; }

        [Parameter("bytes32", "metadata", 10)]
        public byte[] Metadata { get; init; } = new byte[32];

        [Parameter("bytes32", "builder", 11)]
        public byte[] Builder { get; init; } = new byte[32];
    }
}
