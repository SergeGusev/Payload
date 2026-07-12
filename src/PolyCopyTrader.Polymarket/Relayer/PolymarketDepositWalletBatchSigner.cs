using System.Globalization;
using System.Numerics;
using System.Text;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Util;

namespace PolyCopyTrader.Polymarket;

public sealed class PolymarketDepositWalletBatchSigner
{
    private const string DomainName = "DepositWallet";
    private const string DomainVersion = "1";
    private const string Eip712DomainType = "EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)";
    private const string CallType = "Call(address target,uint256 value,bytes data)";
    private const string BatchType = "Batch(address wallet,uint256 nonce,uint256 deadline,Call[] calls)Call(address target,uint256 value,bytes data)";

    public string Sign(PolymarketDepositWalletBatch batch, string privateKey, int chainId)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);

        if (batch.Calls.Count == 0)
        {
            throw new ArgumentException("Deposit wallet batch must contain at least one call.", nameof(batch));
        }

        var key = new EthECKey(NormalizePrivateKey(privateKey));
        var signingHash = BuildSigningHash(batch, chainId);
        return SignHash(signingHash, key);
    }

    public string GetAddress(string privateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);
        return new EthECKey(NormalizePrivateKey(privateKey)).GetPublicAddress();
    }

    internal static byte[] BuildSigningHash(PolymarketDepositWalletBatch batch, int chainId)
    {
        return Keccak(
            [0x19, 0x01],
            BuildDomainSeparator(batch.DepositWalletAddress, chainId),
            BuildBatchHash(batch));
    }

    private static byte[] BuildDomainSeparator(string depositWalletAddress, int chainId)
    {
        return Keccak(
            KeccakUtf8(Eip712DomainType),
            KeccakUtf8(DomainName),
            KeccakUtf8(DomainVersion),
            UInt256Slot(new BigInteger(chainId)),
            AddressSlot(depositWalletAddress));
    }

    private static byte[] BuildBatchHash(PolymarketDepositWalletBatch batch)
    {
        return Keccak(
            KeccakUtf8(BatchType),
            AddressSlot(batch.DepositWalletAddress),
            UInt256Slot(ParseUInt256(batch.Nonce, nameof(batch.Nonce))),
            UInt256Slot(ParseUInt256(batch.Deadline, nameof(batch.Deadline))),
            BuildCallsArrayHash(batch.Calls));
    }

    private static byte[] BuildCallsArrayHash(IReadOnlyList<PolymarketDepositWalletCall> calls)
    {
        var hashes = calls.Select(BuildCallHash).ToArray();
        return Keccak(Concat(hashes));
    }

    private static byte[] BuildCallHash(PolymarketDepositWalletCall call)
    {
        return Keccak(
            KeccakUtf8(CallType),
            AddressSlot(call.Target),
            UInt256Slot(ParseUInt256(call.Value, nameof(call.Value))),
            Keccak(HexToBytes(call.Data)));
    }

    private static BigInteger ParseUInt256(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must be a uint256 decimal or hex string.", parameterName);
        }

        var trimmed = value.Trim();
        var style = NumberStyles.None;
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "0" + trimmed[2..];
            style = NumberStyles.AllowHexSpecifier;
        }

        return BigInteger.TryParse(trimmed, style, CultureInfo.InvariantCulture, out var parsed) &&
               parsed >= BigInteger.Zero &&
               parsed < (BigInteger.One << 256)
            ? parsed
            : throw new ArgumentException("Value must be a uint256 decimal or hex string.", parameterName);
    }

    private static string SignHash(byte[] hash, EthECKey key)
    {
        var signature = key.SignAndCalculateV(hash);
        return "0x" + Concat(FixedLength32(signature.R), FixedLength32(signature.S), OneByteV(signature.V)).ToHex();
    }

    private static string NormalizePrivateKey(string privateKey)
    {
        return privateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? privateKey[2..]
            : privateKey;
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

    private static byte[] HexToBytes(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Hex value is required.", nameof(value));
        }

        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Value must be 0x-prefixed hex.", nameof(value));
        }

        return Convert.FromHexString(hex);
    }
}
