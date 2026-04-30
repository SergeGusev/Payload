using System.Globalization;
using System.Numerics;
using Nethereum.Util;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket.OnChain;

public static class PolymarketOnChainOrderFilledParser
{
    private const decimal TokenScale = 1_000_000m;
    private const string CollateralAssetId = "0";
    private const string V1Signature = "OrderFilled(bytes32,address,address,uint256,uint256,uint256,uint256,uint256)";
    private const string V2Signature = "OrderFilled(bytes32,address,address,uint8,uint256,uint256,uint256,uint256,bytes32,bytes32)";

    public static readonly string V1OrderFilledTopic = Topic(V1Signature);

    public static readonly string V2OrderFilledTopic = Topic(V2Signature);

    public static string GetOrderFilledTopic(string exchangeVersion)
    {
        if (string.Equals(exchangeVersion, "V1", StringComparison.OrdinalIgnoreCase))
        {
            return V1OrderFilledTopic;
        }

        if (string.Equals(exchangeVersion, "V2", StringComparison.OrdinalIgnoreCase))
        {
            return V2OrderFilledTopic;
        }

        throw new ArgumentOutOfRangeException(nameof(exchangeVersion), "Exchange version must be V1 or V2.");
    }

    public static PolymarketOnChainFill Parse(
        PolygonRpcLog log,
        OnChainExchangeContractOptions contract,
        DateTimeOffset blockTimestampUtc)
    {
        if (log.Topics.Count < 4)
        {
            throw new FormatException("OrderFilled log must contain four topics.");
        }

        var topic0 = NormalizeHex(log.Topics[0]);
        var expectedTopic = GetOrderFilledTopic(contract.Version);
        if (!string.Equals(topic0, expectedTopic, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Log topic does not match the configured OrderFilled event version.");
        }

        var orderHash = NormalizeHex(log.Topics[1]);
        var maker = AddressFromTopic(log.Topics[2]);
        var taker = AddressFromTopic(log.Topics[3]);
        var words = SplitDataWords(log.Data);

        return string.Equals(contract.Version, "V2", StringComparison.OrdinalIgnoreCase)
            ? ParseV2(log, contract, blockTimestampUtc, orderHash, maker, taker, words)
            : ParseV1(log, contract, blockTimestampUtc, orderHash, maker, taker, words);
    }

    public static PolymarketOnChainLog ToDomainLog(
        PolygonRpcLog log,
        OnChainExchangeContractOptions contract,
        DateTimeOffset observedAtUtc)
    {
        return new PolymarketOnChainLog(
            Guid.NewGuid(),
            contract.Name,
            NormalizeHex(contract.Address),
            contract.Version.ToUpperInvariant(),
            log.BlockNumber,
            NormalizeHex(log.BlockHash),
            NormalizeHex(log.TransactionHash),
            log.TransactionIndex,
            log.LogIndex,
            log.Topics.Count == 0 ? string.Empty : NormalizeHex(log.Topics[0]),
            log.Topics.Select(NormalizeHex).ToArray(),
            NormalizeHex(log.Data),
            log.Removed,
            observedAtUtc);
    }

    private static PolymarketOnChainFill ParseV1(
        PolygonRpcLog log,
        OnChainExchangeContractOptions contract,
        DateTimeOffset blockTimestampUtc,
        string orderHash,
        string maker,
        string taker,
        IReadOnlyList<string> words)
    {
        if (words.Count != 5)
        {
            throw new FormatException("V1 OrderFilled log data must contain five words.");
        }

        var makerAssetId = UInt256(words[0]).ToString(CultureInfo.InvariantCulture);
        var takerAssetId = UInt256(words[1]).ToString(CultureInfo.InvariantCulture);
        var makerAmountRaw = UInt256(words[2]);
        var takerAmountRaw = UInt256(words[3]);
        var feeRaw = UInt256(words[4]);
        var side = makerAssetId == CollateralAssetId ? TradeSide.Buy : TradeSide.Sell;
        var tokenId = side == TradeSide.Buy ? takerAssetId : makerAssetId;

        return BuildFill(
            log,
            contract,
            blockTimestampUtc,
            orderHash,
            maker,
            taker,
            side,
            tokenId,
            makerAssetId,
            takerAssetId,
            makerAmountRaw,
            takerAmountRaw,
            feeRaw,
            takerAssetId,
            null,
            null);
    }

    private static PolymarketOnChainFill ParseV2(
        PolygonRpcLog log,
        OnChainExchangeContractOptions contract,
        DateTimeOffset blockTimestampUtc,
        string orderHash,
        string maker,
        string taker,
        IReadOnlyList<string> words)
    {
        if (words.Count != 7)
        {
            throw new FormatException("V2 OrderFilled log data must contain seven words.");
        }

        var sideValue = UInt256(words[0]);
        var side = sideValue == BigInteger.Zero ? TradeSide.Buy : TradeSide.Sell;
        if (sideValue != BigInteger.Zero && sideValue != BigInteger.One)
        {
            throw new FormatException("V2 OrderFilled side must be 0 or 1.");
        }

        var tokenId = UInt256(words[1]).ToString(CultureInfo.InvariantCulture);
        var makerAmountRaw = UInt256(words[2]);
        var takerAmountRaw = UInt256(words[3]);
        var feeRaw = UInt256(words[4]);
        var makerAssetId = side == TradeSide.Buy ? CollateralAssetId : tokenId;
        var takerAssetId = side == TradeSide.Buy ? tokenId : CollateralAssetId;
        var feeAssetId = takerAssetId;

        return BuildFill(
            log,
            contract,
            blockTimestampUtc,
            orderHash,
            maker,
            taker,
            side,
            tokenId,
            makerAssetId,
            takerAssetId,
            makerAmountRaw,
            takerAmountRaw,
            feeRaw,
            feeAssetId,
            NormalizeHex(words[5]),
            NormalizeHex(words[6]));
    }

    private static PolymarketOnChainFill BuildFill(
        PolygonRpcLog log,
        OnChainExchangeContractOptions contract,
        DateTimeOffset blockTimestampUtc,
        string orderHash,
        string maker,
        string taker,
        TradeSide side,
        string tokenId,
        string makerAssetId,
        string takerAssetId,
        BigInteger makerAmountRaw,
        BigInteger takerAmountRaw,
        BigInteger feeRaw,
        string feeAssetId,
        string? builder,
        string? metadata)
    {
        var makerAmount = Scale(makerAmountRaw);
        var takerAmount = Scale(takerAmountRaw);
        var (sizeShares, notionalUsd) = side == TradeSide.Buy
            ? (takerAmount, makerAmount)
            : (makerAmount, takerAmount);

        if (sizeShares <= 0m || notionalUsd < 0m)
        {
            throw new FormatException("OrderFilled size and notional must be usable positive token-unit amounts.");
        }

        var price = notionalUsd / sizeShares;
        return new PolymarketOnChainFill(
            Guid.NewGuid(),
            contract.Name,
            NormalizeHex(contract.Address),
            contract.Version.ToUpperInvariant(),
            log.BlockNumber,
            blockTimestampUtc.ToUniversalTime(),
            NormalizeHex(log.TransactionHash),
            log.LogIndex,
            orderHash,
            maker,
            taker,
            maker,
            side,
            tokenId,
            makerAssetId,
            takerAssetId,
            makerAmountRaw.ToString(CultureInfo.InvariantCulture),
            takerAmountRaw.ToString(CultureInfo.InvariantCulture),
            makerAmount,
            takerAmount,
            price,
            sizeShares,
            notionalUsd,
            feeRaw.ToString(CultureInfo.InvariantCulture),
            Scale(feeRaw),
            feeAssetId,
            builder,
            metadata,
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<string> SplitDataWords(string data)
    {
        var hex = StripHexPrefix(data);
        if (hex.Length % 64 != 0)
        {
            throw new FormatException("Ethereum log data must be 32-byte aligned.");
        }

        var words = new List<string>();
        for (var offset = 0; offset < hex.Length; offset += 64)
        {
            words.Add("0x" + hex.Substring(offset, 64));
        }

        return words;
    }

    private static BigInteger UInt256(string value)
    {
        var hex = StripHexPrefix(value).PadLeft(64, '0');
        return new BigInteger(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: true);
    }

    private static decimal Scale(BigInteger value)
    {
        return decimal.Parse(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) / TokenScale;
    }

    private static string AddressFromTopic(string topic)
    {
        var hex = StripHexPrefix(topic).PadLeft(64, '0');
        return "0x" + hex[24..].ToLowerInvariant();
    }

    private static string Topic(string signature)
    {
        return "0x" + Sha3Keccack.Current.CalculateHash(signature).ToLowerInvariant();
    }

    private static string NormalizeHex(string value)
    {
        var hex = StripHexPrefix(value).ToLowerInvariant();
        return "0x" + hex;
    }

    private static string StripHexPrefix(string value)
    {
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
    }
}
