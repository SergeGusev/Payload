namespace PolyCopyTrader.Domain;

public enum PaperConfirmationLane { Recent, Archive }

public sealed record PaperConfirmationMarketEvidence(string ConditionId,
    IReadOnlyList<PolymarketOnChainTokenMetadata> Tokens, DateTimeOffset CheckedAtUtc);

public sealed record PaperConfirmationProgress(DateTimeOffset CapturedAtUtc, bool Initialized,
    long Orders, long Confirmed, long Corrected, long Deferred, long? Arrivals = null, long? UniqueConfirmations = null)
{
    public long Remaining => Math.Max(0, Orders - Confirmed);
}

public sealed record PaperConfirmationRates(double ConfirmedPerMinute, double ArrivalsPerMinute,
    double? FixedBacklogMinutes, double? CatchUpMinutes)
{
    public static PaperConfirmationRates? Between(PaperConfirmationProgress? previous, PaperConfirmationProgress current)
    {
        if (previous is not { Initialized: true } || !current.Initialized ||
            current.CapturedAtUtc <= previous.CapturedAtUtc ||
            (current.UniqueConfirmations??current.Confirmed) < (previous.UniqueConfirmations??previous.Confirmed) ||
            (current.Arrivals??current.Orders) < (previous.Arrivals??previous.Orders)) return null;
        var minutes = (current.CapturedAtUtc - previous.CapturedAtUtc).TotalMinutes;
        var speed = ((current.UniqueConfirmations??current.Confirmed) - (previous.UniqueConfirmations??previous.Confirmed)) / minutes;
        var arrivals = ((current.Arrivals??current.Orders) - (previous.Arrivals??previous.Orders)) / minutes;
        return new(speed, arrivals, speed > 0 ? current.Remaining / speed : null,
            speed > arrivals ? current.Remaining / (speed - arrivals) : null);
    }
}

public sealed record PaperConfirmationCoverage(bool Initialized, long Closed, long Confirmed,
    decimal? NetRealizedPnlUsd, decimal? NetClosedRoiPct, DateTimeOffset? RefreshedAtUtc, string? Error = null)
{
    public long Remaining => Math.Max(0, Closed - Confirmed);
    public decimal? Percent => Initialized && Closed > 0 ? 100m * Confirmed / Closed : null;
    public string Status => !Initialized ? "Unknown" : Closed == 0 ? "No closed records" :
        Confirmed == Closed ? "Outcome confirmed" : "Provisional";
    public string Display => !Initialized ? "Unknown" :
        $"{Confirmed:N0}/{Closed:N0} ({Percent:N1}%), remaining {Remaining:N0}";
}
