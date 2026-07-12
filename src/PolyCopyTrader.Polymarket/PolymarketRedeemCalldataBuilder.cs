using System.Globalization;
using System.Numerics;
using System.Text;
using Nethereum.Util;

namespace PolyCopyTrader.Polymarket;

public sealed class PolymarketRedeemCalldataBuilder
{
    private const string RedeemPositionsSignature = "redeemPositions(address,bytes32,bytes32,uint256[])";

    public string BuildRedeemPositions(
        string collateralToken,
        string parentCollectionId,
        string conditionId,
        IReadOnlyList<int> indexSets)
    {
        if (!IsAddressLike(collateralToken))
        {
            throw new ArgumentException("Collateral token must be a 0x-prefixed Ethereum address.", nameof(collateralToken));
        }

        if (!IsBytes32(parentCollectionId))
        {
            throw new ArgumentException("Parent collection id must be a 0x-prefixed bytes32 value.", nameof(parentCollectionId));
        }

        if (!IsBytes32(conditionId))
        {
            throw new ArgumentException("Condition id must be a 0x-prefixed bytes32 value.", nameof(conditionId));
        }

        if (indexSets.Count == 0)
        {
            throw new ArgumentException("At least one index set is required.", nameof(indexSets));
        }

        var builder = new StringBuilder("0x");
        builder.Append(Sha3Keccack.Current.CalculateHash(RedeemPositionsSignature)[..8]);
        builder.Append(AddressSlot(collateralToken));
        builder.Append(Bytes32Slot(parentCollectionId));
        builder.Append(Bytes32Slot(conditionId));
        builder.Append(UInt256Slot(new BigInteger(4 * 32)));
        builder.Append(UInt256Slot(new BigInteger(indexSets.Count)));

        foreach (var indexSet in indexSets)
        {
            if (indexSet <= 0)
            {
                throw new ArgumentException("Index sets must be positive integers.", nameof(indexSets));
            }

            builder.Append(UInt256Slot(new BigInteger(indexSet)));
        }

        return builder.ToString();
    }

    private static string AddressSlot(string value)
    {
        return value[2..].ToLowerInvariant().PadLeft(64, '0');
    }

    private static string Bytes32Slot(string value)
    {
        return value[2..].ToLowerInvariant();
    }

    private static string UInt256Slot(BigInteger value)
    {
        if (value < BigInteger.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "UInt256 value cannot be negative.");
        }

        return value.ToString("x", CultureInfo.InvariantCulture).PadLeft(64, '0');
    }

    private static bool IsAddressLike(string value)
    {
        return value.Length == 42 &&
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            value.Skip(2).All(Uri.IsHexDigit);
    }

    private static bool IsBytes32(string value)
    {
        return value.Length == 66 &&
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            value.Skip(2).All(Uri.IsHexDigit);
    }
}
