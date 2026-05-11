namespace PolyCopyTrader.Polymarket.Auth;

public static class ClobV2ExchangeContracts
{
    public const int PolygonChainId = 137;
    public const string Exchange = "0xE111180000d2663C0091e4f400237545B87B996B";
    public const string NegativeRiskExchange = "0xe2222d279d744050d28e00520010520000310F59";
    public const string ZeroAddress = "0x0000000000000000000000000000000000000000";
    public const string ZeroBytes32 = "0x0000000000000000000000000000000000000000000000000000000000000000";

    public static string GetVerifyingContract(bool negativeRisk)
    {
        return negativeRisk ? NegativeRiskExchange : Exchange;
    }
}
