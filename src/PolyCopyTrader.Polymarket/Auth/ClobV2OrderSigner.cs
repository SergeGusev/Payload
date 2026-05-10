using System.Numerics;
using System.Text;
using Nethereum.ABI.EIP712;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using Nethereum.Util;
using BouncyBigInteger = Org.BouncyCastle.Math.BigInteger;
using Eip712Domain = Nethereum.ABI.EIP712.Domain;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class ClobV2OrderSigner
{
    private const string DomainName = "Polymarket CTF Exchange";
    private const string DomainVersion = "2";
    private const string DepositWalletDomainName = "DepositWallet";
    private const string DepositWalletDomainVersion = "1";
    private const string Eip712DomainType = "EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)";
    private const string OrderType =
        "Order(uint256 salt,address maker,address signer,uint256 tokenId,uint256 makerAmount,uint256 takerAmount,uint8 side,uint8 signatureType,uint256 timestamp,bytes32 metadata,bytes32 builder)";

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
        if (order.SignatureType == ClobV2SignatureType.POLY_1271)
        {
            return SignPoly1271(order, key, chainId);
        }

        return typedDataSigner.SignTypedDataV4(ToTypedMessage(order), BuildTypedData(order, chainId), key);
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

        var recovered = order.SignatureType == ClobV2SignatureType.POLY_1271
            ? RecoverHashSigner(BuildPoly1271SigningHash(order, chainId), ExtractPoly1271InnerSignature(signature))
            : typedDataSigner.RecoverFromSignatureV4(
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

    private string SignPoly1271(ClobV2Order order, EthECKey key, int chainId)
    {
        var innerSignature = SignHash(BuildPoly1271SigningHash(order, chainId), key);
        var appDomainSeparator = BuildAppDomainSeparator(order, chainId);
        var contentsHash = BuildContentsHash(order);
        var contentsDescription = Encoding.UTF8.GetBytes(OrderType);
        if (contentsDescription.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException("POLY_1271 order type description is too long.");
        }

        var length = (ushort)contentsDescription.Length;
        var wrapped = Concat(
            HexToBytes(innerSignature),
            appDomainSeparator,
            contentsHash,
            contentsDescription,
            [(byte)(length >> 8), (byte)(length & 0xFF)]);

        return "0x" + wrapped.ToHex();
    }

    private static string ExtractPoly1271InnerSignature(string signature)
    {
        var bytes = HexToBytes(signature);
        if (bytes.Length < 65)
        {
            throw new ArgumentException("POLY_1271 signature is too short.", nameof(signature));
        }

        return "0x" + bytes[..65].ToHex();
    }

    private static string NormalizePrivateKey(string privateKey)
    {
        return privateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? privateKey[2..]
            : privateKey;
    }

    private static byte[] BuildAppDomainSeparator(ClobV2Order order, int chainId)
    {
        return Keccak(
            KeccakUtf8(Eip712DomainType),
            KeccakUtf8(DomainName),
            KeccakUtf8(DomainVersion),
            UInt256Slot(new BigInteger(chainId)),
            AddressSlot(ClobV2ExchangeContracts.GetVerifyingContract(order.NegativeRisk)));
    }

    private static byte[] BuildContentsHash(ClobV2Order order)
    {
        return Keccak(
            KeccakUtf8(OrderType),
            UInt256Slot(ClobV2OrderBuilder.ParseUInt256(order.Salt)),
            AddressSlot(order.Maker),
            AddressSlot(order.Signer),
            UInt256Slot(ClobV2OrderBuilder.ParseUInt256(order.TokenId)),
            UInt256Slot(ClobV2OrderBuilder.ParseUInt256(order.MakerAmount)),
            UInt256Slot(ClobV2OrderBuilder.ParseUInt256(order.TakerAmount)),
            UInt256Slot(new BigInteger(ClobV2OrderBuilder.SideToTypedValue(order.Side))),
            UInt256Slot(new BigInteger((int)order.SignatureType)),
            UInt256Slot(ClobV2OrderBuilder.ParseUInt256(order.Timestamp)),
            Bytes32Slot(HexToBytes32(order.Metadata)),
            Bytes32Slot(HexToBytes32(order.Builder)));
    }

    private static byte[] BuildPoly1271SigningHash(ClobV2Order order, int chainId)
    {
        var typeHash = KeccakUtf8(
            "TypedDataSign(Order contents,string name,string version,uint256 chainId,address verifyingContract,bytes32 salt)" +
            OrderType);
        var typedDataSignStructHash = Keccak(
            typeHash,
            BuildContentsHash(order),
            KeccakUtf8(DepositWalletDomainName),
            KeccakUtf8(DepositWalletDomainVersion),
            UInt256Slot(new BigInteger(chainId)),
            AddressSlot(order.Signer),
            new byte[32]);

        return Keccak([0x19, 0x01], BuildAppDomainSeparator(order, chainId), typedDataSignStructHash);
    }

    private static string SignHash(byte[] hash, EthECKey key)
    {
        var signature = key.SignAndCalculateV(hash);
        return "0x" + Concat(FixedLength32(signature.R), FixedLength32(signature.S), OneByteV(signature.V)).ToHex();
    }

    private static string RecoverHashSigner(byte[] hash, string signature)
    {
        var bytes = HexToBytes(signature);
        if (bytes.Length != 65)
        {
            throw new ArgumentException("ECDSA signature must be 65 bytes.", nameof(signature));
        }

        var r = new BouncyBigInteger(1, bytes[..32]);
        var s = new BouncyBigInteger(1, bytes[32..64]);
        var recovered = EthECKey.RecoverFromSignature(new EthECDSASignature(r, s, [bytes[64]]), hash);
        return recovered.GetPublicAddress();
    }

    private static byte[] FixedLength32(byte[] value)
    {
        if (value.Length > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Signature component exceeds 32 bytes.");
        }

        var bytes = new byte[32];
        Buffer.BlockCopy(value, 0, bytes, 32 - value.Length, value.Length);
        return bytes;
    }

    private static byte[] OneByteV(byte[] value)
    {
        if (value.Length == 0)
        {
            throw new ArgumentException("Signature v component is missing.", nameof(value));
        }

        return [value[^1]];
    }

    private static byte[] KeccakUtf8(string value)
    {
        return Keccak(Encoding.UTF8.GetBytes(value));
    }

    private static byte[] Keccak(params byte[][] values)
    {
        return Sha3Keccack.Current.CalculateHash(Concat(values));
    }

    private static byte[] Concat(params byte[][] values)
    {
        var length = values.Sum(value => value.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var value in values)
        {
            Buffer.BlockCopy(value, 0, result, offset, value.Length);
            offset += value.Length;
        }

        return result;
    }

    private static byte[] UInt256Slot(BigInteger value)
    {
        if (value < BigInteger.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "uint256 value cannot be negative.");
        }

        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "uint256 value exceeds 32 bytes.");
        }

        var slot = new byte[32];
        Buffer.BlockCopy(bytes, 0, slot, 32 - bytes.Length, bytes.Length);
        return slot;
    }

    private static byte[] AddressSlot(string address)
    {
        var bytes = HexToBytes(address);
        if (bytes.Length != 20)
        {
            throw new ArgumentException("Address must be 20 bytes.", nameof(address));
        }

        var slot = new byte[32];
        Buffer.BlockCopy(bytes, 0, slot, 12, 20);
        return slot;
    }

    private static byte[] Bytes32Slot(byte[] value)
    {
        if (value.Length != 32)
        {
            throw new ArgumentException("bytes32 value must be exactly 32 bytes.", nameof(value));
        }

        return value.ToArray();
    }

    private static byte[] HexToBytes32(string value)
    {
        var bytes = HexToBytes(value);
        if (bytes.Length != 32)
        {
            throw new ArgumentException("Value must be a 0x-prefixed bytes32 value.", nameof(value));
        }

        return bytes;
    }

    private static byte[] HexToBytes(string value)
    {
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return Convert.FromHexString(hex);
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
        public byte[] Metadata { get; init; } = [];

        [Parameter("bytes32", "builder", 11)]
        public byte[] Builder { get; init; } = [];
    }
}
