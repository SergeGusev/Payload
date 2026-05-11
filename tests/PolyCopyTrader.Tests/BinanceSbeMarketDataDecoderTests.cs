using System.Buffers.Binary;
using System.Text;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Tests;

public sealed class BinanceSbeMarketDataDecoderTests
{
    [Fact]
    public void TryDecode_DecodesBestBidAsk()
    {
        var receivedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_100);
        byte[] payload = BuildBestBidAskPayload();

        var decoded = BinanceSbeMarketDataDecoder.TryDecode(
            payload,
            receivedAtUtc,
            out var message,
            out string? error);

        Assert.True(decoded, error);
        var best = Assert.IsType<BinanceSbeBestBidAskEvent>(message);
        Assert.Equal("BTCUSDT", best.Symbol);
        Assert.Equal(123456789L, best.BookUpdateId);
        Assert.Equal(70000.12m, best.BidPrice);
        Assert.Equal(0.12345678m, best.BidQty);
        Assert.Equal(70000.13m, best.AskPrice);
        Assert.Equal(0.87654321m, best.AskQty);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000), best.EventTimeUtc);
        Assert.Equal(receivedAtUtc, best.ReceivedAtUtc);
    }

    [Fact]
    public void TryDecode_DecodesTrades()
    {
        var receivedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_200);
        byte[] payload = BuildTradesPayload();

        var decoded = BinanceSbeMarketDataDecoder.TryDecode(
            payload,
            receivedAtUtc,
            out var message,
            out string? error);

        Assert.True(decoded, error);
        var tradeEvent = Assert.IsType<BinanceSbeTradeEvent>(message);
        Assert.Equal("BTCUSDT", tradeEvent.Symbol);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000), tradeEvent.EventTimeUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_001), tradeEvent.TransactTimeUtc);
        var trade = Assert.Single(tradeEvent.Trades);
        Assert.Equal(987654321L, trade.Id);
        Assert.Equal(70001.23m, trade.Price);
        Assert.Equal(0.25m, trade.Quantity);
        Assert.True(trade.IsBuyerMaker);
    }

    [Fact]
    public void TryDecode_RejectsUnsupportedTemplate()
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 9999);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 0);

        var decoded = BinanceSbeMarketDataDecoder.TryDecode(
            payload,
            DateTimeOffset.UtcNow,
            out var message,
            out string? error);

        Assert.False(decoded);
        Assert.Null(message);
        Assert.Contains("Unsupported SBE template id", error);
    }

    private static byte[] BuildBestBidAskPayload()
    {
        const ushort blockLength = 50;
        byte[] symbol = Encoding.UTF8.GetBytes("BTCUSDT");
        byte[] payload = new byte[8 + blockLength + 1 + symbol.Length];
        WriteHeader(payload, blockLength, BinanceSbeMarketDataDecoder.BestBidAskTemplateId);

        int offset = 8;
        WriteInt64(payload, offset, 1_700_000_000_000_000L);
        WriteInt64(payload, offset + 8, 123456789L);
        payload[offset + 16] = unchecked((byte)-2);
        payload[offset + 17] = unchecked((byte)-8);
        WriteInt64(payload, offset + 18, 7000012L);
        WriteInt64(payload, offset + 26, 12345678L);
        WriteInt64(payload, offset + 34, 7000013L);
        WriteInt64(payload, offset + 42, 87654321L);
        WriteSymbol(payload, offset + blockLength, symbol);
        return payload;
    }

    private static byte[] BuildTradesPayload()
    {
        const ushort rootBlockLength = 18;
        const ushort tradeBlockLength = 25;
        byte[] symbol = Encoding.UTF8.GetBytes("BTCUSDT");
        byte[] payload = new byte[8 + rootBlockLength + 6 + tradeBlockLength + 1 + symbol.Length];
        WriteHeader(payload, rootBlockLength, BinanceSbeMarketDataDecoder.TradesTemplateId);

        int offset = 8;
        WriteInt64(payload, offset, 1_700_000_000_000_000L);
        WriteInt64(payload, offset + 8, 1_700_000_000_001_000L);
        payload[offset + 16] = unchecked((byte)-2);
        payload[offset + 17] = unchecked((byte)-8);

        int groupOffset = offset + rootBlockLength;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(groupOffset, 2), tradeBlockLength);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(groupOffset + 2, 4), 1);

        int tradeOffset = groupOffset + 6;
        WriteInt64(payload, tradeOffset, 987654321L);
        WriteInt64(payload, tradeOffset + 8, 7000123L);
        WriteInt64(payload, tradeOffset + 16, 25000000L);
        payload[tradeOffset + 24] = 1;
        WriteSymbol(payload, tradeOffset + tradeBlockLength, symbol);
        return payload;
    }

    private static void WriteHeader(byte[] payload, ushort blockLength, ushort templateId)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), blockLength);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), templateId);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), 0);
    }

    private static void WriteInt64(byte[] payload, int offset, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset, 8), value);
    }

    private static void WriteSymbol(byte[] payload, int offset, byte[] symbol)
    {
        payload[offset] = (byte)symbol.Length;
        symbol.CopyTo(payload.AsSpan(offset + 1));
    }
}
