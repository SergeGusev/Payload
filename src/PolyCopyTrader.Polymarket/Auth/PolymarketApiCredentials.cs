namespace PolyCopyTrader.Polymarket.Auth;

public sealed record PolymarketApiCredentials(
    string ApiKey,
    string ApiSecret,
    string ApiPassphrase);
