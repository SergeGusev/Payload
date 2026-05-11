namespace PolyCopyTrader.Service.MarketData;

public sealed record MarketDataWebSocketShardPlan(
    int Index,
    string Component,
    IReadOnlyList<string> AssetIds);
