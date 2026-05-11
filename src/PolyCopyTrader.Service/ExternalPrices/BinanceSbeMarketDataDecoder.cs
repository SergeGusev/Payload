using System.Buffers.Binary;
using System.Text;

namespace PolyCopyTrader.Service.ExternalPrices;

public abstract record BinanceSbeMarketDataEvent(
    ushort TemplateId,
    string MessageName,
    string Symbol,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset ReceivedAtUtc);

public sealed record BinanceSbeBestBidAskEvent(
    string Symbol,
    long BookUpdateId,
    decimal BidPrice,
    decimal BidQty,
    decimal AskPrice,
    decimal AskQty,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset ReceivedAtUtc) : BinanceSbeMarketDataEvent(
    BinanceSbeMarketDataDecoder.BestBidAskTemplateId,
    nameof(BinanceSbeBestBidAskEvent),
    Symbol,
    EventTimeUtc,
    ReceivedAtUtc);

public sealed record BinanceSbeTradeEvent(
    string Symbol,
    DateTimeOffset EventTimeUtc,
    DateTimeOffset TransactTimeUtc,
    IReadOnlyList<BinanceSbeTrade> Trades,
    DateTimeOffset ReceivedAtUtc) : BinanceSbeMarketDataEvent(
    BinanceSbeMarketDataDecoder.TradesTemplateId,
    nameof(BinanceSbeTradeEvent),
    Symbol,
    EventTimeUtc,
    ReceivedAtUtc);

public sealed record BinanceSbeTrade(
    long Id,
    decimal Price,
    decimal Quantity,
    bool IsBuyerMaker);

public static class BinanceSbeMarketDataDecoder
{
    public const ushort TradesTemplateId = 10000;
    public const ushort BestBidAskTemplateId = 10001;

    private const int MessageHeaderLength = 8;
    private const ushort ExpectedSchemaId = 1;
    private const int BestBidAskRootBlockLength = 50;
    private const int TradesRootBlockLength = 18;
    private const int GroupSizeEncodingLength = 6;

    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        DateTimeOffset receivedAtUtc,
        out BinanceSbeMarketDataEvent? message,
        out string? error)
    {
        message = null;
        error = null;

        if (payload.Length < MessageHeaderLength)
        {
            error = $"SBE payload is too short for message header. Length={payload.Length}.";
            return false;
        }

        ushort blockLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2]);
        ushort templateId = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]);
        ushort schemaId = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..6]);
        if (schemaId != ExpectedSchemaId)
        {
            error = $"Unexpected SBE schema id {schemaId}. Expected {ExpectedSchemaId}.";
            return false;
        }

        return templateId switch
        {
            BestBidAskTemplateId => TryDecodeBestBidAsk(payload, blockLength, receivedAtUtc, out message, out error),
            TradesTemplateId => TryDecodeTrades(payload, blockLength, receivedAtUtc, out message, out error),
            _ => Fail($"Unsupported SBE template id {templateId}.", out message, out error)
        };
    }

    private static bool TryDecodeBestBidAsk(
        ReadOnlySpan<byte> payload,
        ushort blockLength,
        DateTimeOffset receivedAtUtc,
        out BinanceSbeMarketDataEvent? message,
        out string? error)
    {
        message = null;
        error = null;

        if (blockLength < BestBidAskRootBlockLength)
        {
            error = $"BestBidAsk block length {blockLength} is shorter than expected {BestBidAskRootBlockLength}.";
            return false;
        }

        int rootOffset = MessageHeaderLength;
        int symbolOffset = rootOffset + blockLength;
        if (payload.Length < symbolOffset + 1)
        {
            error = $"BestBidAsk payload is too short. Length={payload.Length}; SymbolOffset={symbolOffset}.";
            return false;
        }

        long eventTimeUs = ReadInt64(payload, rootOffset);
        long bookUpdateId = ReadInt64(payload, rootOffset + 8);
        sbyte priceExponent = unchecked((sbyte)payload[rootOffset + 16]);
        sbyte qtyExponent = unchecked((sbyte)payload[rootOffset + 17]);
        decimal bidPrice = ToDecimal(ReadInt64(payload, rootOffset + 18), priceExponent);
        decimal bidQty = ToDecimal(ReadInt64(payload, rootOffset + 26), qtyExponent);
        decimal askPrice = ToDecimal(ReadInt64(payload, rootOffset + 34), priceExponent);
        decimal askQty = ToDecimal(ReadInt64(payload, rootOffset + 42), qtyExponent);

        if (!TryReadVarString8(payload, symbolOffset, out string symbol, out error))
        {
            return false;
        }

        message = new BinanceSbeBestBidAskEvent(
            symbol,
            bookUpdateId,
            bidPrice,
            bidQty,
            askPrice,
            askQty,
            ToDateTimeOffsetUs(eventTimeUs),
            receivedAtUtc);
        return true;
    }

    private static bool TryDecodeTrades(
        ReadOnlySpan<byte> payload,
        ushort blockLength,
        DateTimeOffset receivedAtUtc,
        out BinanceSbeMarketDataEvent? message,
        out string? error)
    {
        message = null;
        error = null;

        if (blockLength < TradesRootBlockLength)
        {
            error = $"Trades block length {blockLength} is shorter than expected {TradesRootBlockLength}.";
            return false;
        }

        int rootOffset = MessageHeaderLength;
        int groupOffset = rootOffset + blockLength;
        if (payload.Length < groupOffset + GroupSizeEncodingLength)
        {
            error = $"Trades payload is too short for group dimensions. Length={payload.Length}; GroupOffset={groupOffset}.";
            return false;
        }

        long eventTimeUs = ReadInt64(payload, rootOffset);
        long transactTimeUs = ReadInt64(payload, rootOffset + 8);
        sbyte priceExponent = unchecked((sbyte)payload[rootOffset + 16]);
        sbyte qtyExponent = unchecked((sbyte)payload[rootOffset + 17]);

        ushort tradeBlockLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[groupOffset..(groupOffset + 2)]);
        uint tradeCountRaw = BinaryPrimitives.ReadUInt32LittleEndian(payload[(groupOffset + 2)..(groupOffset + 6)]);
        if (tradeBlockLength < 25)
        {
            error = $"Trades group block length {tradeBlockLength} is shorter than expected 25.";
            return false;
        }

        if (tradeCountRaw > 10_000)
        {
            error = $"Trades group is unexpectedly large. Count={tradeCountRaw}.";
            return false;
        }

        int tradeCount = checked((int)tradeCountRaw);
        int tradesOffset = groupOffset + GroupSizeEncodingLength;
        int symbolOffset = tradesOffset + tradeCount * tradeBlockLength;
        if (payload.Length < symbolOffset + 1)
        {
            error = $"Trades payload is too short for {tradeCount} trade entries. Length={payload.Length}; SymbolOffset={symbolOffset}.";
            return false;
        }

        var trades = new List<BinanceSbeTrade>(tradeCount);
        for (int index = 0; index < tradeCount; index++)
        {
            int tradeOffset = tradesOffset + index * tradeBlockLength;
            trades.Add(new BinanceSbeTrade(
                ReadInt64(payload, tradeOffset),
                ToDecimal(ReadInt64(payload, tradeOffset + 8), priceExponent),
                ToDecimal(ReadInt64(payload, tradeOffset + 16), qtyExponent),
                payload[tradeOffset + 24] != 0));
        }

        if (!TryReadVarString8(payload, symbolOffset, out string symbol, out error))
        {
            return false;
        }

        message = new BinanceSbeTradeEvent(
            symbol,
            ToDateTimeOffsetUs(eventTimeUs),
            ToDateTimeOffsetUs(transactTimeUs),
            trades,
            receivedAtUtc);
        return true;
    }

    private static long ReadInt64(ReadOnlySpan<byte> payload, int offset)
    {
        return BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(offset, sizeof(long)));
    }

    private static decimal ToDecimal(long mantissa, sbyte exponent)
    {
        decimal value = mantissa;
        if (exponent == 0)
        {
            return value;
        }

        decimal scale = 1m;
        int steps = Math.Abs(exponent);
        for (int index = 0; index < steps; index++)
        {
            scale *= 10m;
        }

        return exponent < 0 ? value / scale : value * scale;
    }

    private static DateTimeOffset ToDateTimeOffsetUs(long microseconds)
    {
        long seconds = Math.DivRem(microseconds, 1_000_000, out long remainingMicroseconds);
        return DateTimeOffset
            .FromUnixTimeSeconds(seconds)
            .AddTicks(remainingMicroseconds * 10);
    }

    private static bool TryReadVarString8(
        ReadOnlySpan<byte> payload,
        int offset,
        out string value,
        out string? error)
    {
        value = string.Empty;
        error = null;

        if (payload.Length <= offset)
        {
            error = $"SBE varString8 offset {offset} is outside payload length {payload.Length}.";
            return false;
        }

        int length = payload[offset];
        int valueOffset = offset + 1;
        if (payload.Length < valueOffset + length)
        {
            error = $"SBE varString8 is truncated. Offset={offset}; Length={length}; PayloadLength={payload.Length}.";
            return false;
        }

        value = Encoding.UTF8.GetString(payload.Slice(valueOffset, length));
        return true;
    }

    private static bool Fail(
        string failure,
        out BinanceSbeMarketDataEvent? message,
        out string? error)
    {
        message = null;
        error = failure;
        return false;
    }
}
