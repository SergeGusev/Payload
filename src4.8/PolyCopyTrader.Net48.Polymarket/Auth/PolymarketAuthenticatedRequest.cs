namespace PolyCopyTrader.Polymarket.Auth;

public sealed record PolymarketAuthenticatedRequest(
    string Method,
    string RequestPath,
    string? Body = null);
