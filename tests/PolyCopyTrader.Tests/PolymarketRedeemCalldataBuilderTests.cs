using PolyCopyTrader.Polymarket;

namespace PolyCopyTrader.Tests;

public sealed class PolymarketRedeemCalldataBuilderTests
{
    [Fact]
    public void BuildRedeemPositions_EncodesStandardBinaryClaim()
    {
        var builder = new PolymarketRedeemCalldataBuilder();
        var collateral = "0xC011a7E12a19f7B1f670d46F03B03f3342E82DFB";
        var parentCollectionId = "0x" + new string('0', 64);
        var conditionId = "0x" + new string('a', 64);

        var calldata = builder.BuildRedeemPositions(collateral, parentCollectionId, conditionId, [1, 2]);

        Assert.StartsWith("0x01b7037c", calldata, StringComparison.Ordinal);
        Assert.Equal(2 + 8 + 7 * 64, calldata.Length);
        Assert.Equal(collateral[2..].ToLowerInvariant().PadLeft(64, '0'), Word(calldata, 0));
        Assert.Equal(parentCollectionId[2..], Word(calldata, 1));
        Assert.Equal(conditionId[2..], Word(calldata, 2));
        Assert.Equal(new string('0', 62) + "80", Word(calldata, 3));
        Assert.Equal(new string('0', 63) + "2", Word(calldata, 4));
        Assert.Equal(new string('0', 63) + "1", Word(calldata, 5));
        Assert.Equal(new string('0', 63) + "2", Word(calldata, 6));
    }

    [Fact]
    public void BuildRedeemPositions_RejectsInvalidConditionId()
    {
        var builder = new PolymarketRedeemCalldataBuilder();

        Assert.Throws<ArgumentException>(() => builder.BuildRedeemPositions(
            "0xC011a7E12a19f7B1f670d46F03B03f3342E82DFB",
            "0x" + new string('0', 64),
            "0x1234",
            [1, 2]));
    }

    private static string Word(string calldata, int index)
    {
        return calldata.Substring(10 + index * 64, 64);
    }
}
