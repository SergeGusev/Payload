using PolyCopyTrader.Dashboard.Models;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Dashboard.Services;

public static class DashboardServiceAvailabilityEvaluator
{
    public const string PrimaryServiceName = "PolyCopyTrader.Service";

    public static ServiceAvailability Evaluate(
        IReadOnlyList<ServiceHeartbeat> heartbeats,
        DateTimeOffset nowUtc,
        TimeSpan staleAfter)
    {
        var heartbeat = heartbeats.FirstOrDefault(item =>
            string.Equals(item.ServiceName, PrimaryServiceName, StringComparison.OrdinalIgnoreCase));

        if (heartbeat is null)
        {
            return new ServiceAvailability(
                PrimaryServiceName,
                "No heartbeat",
                string.Empty,
                null,
                null,
                null,
                string.Empty,
                null,
                false,
                false);
        }

        var age = nowUtc - heartbeat.LastHeartbeatUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return new ServiceAvailability(
            heartbeat.ServiceName,
            heartbeat.Status,
            heartbeat.Mode.ToString(),
            heartbeat.StartedAtUtc,
            heartbeat.LastHeartbeatUtc,
            age,
            heartbeat.CurrentLoop,
            heartbeat.LastError,
            true,
            age <= staleAfter);
    }

    public static string FormatHeartbeatAge(TimeSpan? age)
    {
        if (age is null)
        {
            return "n/a";
        }

        return age.Value.TotalMinutes >= 1
            ? $"{age.Value.TotalMinutes:0.0}m"
            : $"{age.Value.TotalSeconds:0.0}s";
    }
}
