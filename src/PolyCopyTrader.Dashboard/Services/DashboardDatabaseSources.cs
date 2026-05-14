namespace PolyCopyTrader.Dashboard.Services;

public enum DashboardDatabaseSource
{
    Local,
    Remote
}

public static class DashboardDatabaseSources
{
    public const string LocalDisplayName = "Local database";
    public const string RemoteDisplayName = "Remote database";
    public const string RemoteHost = "192.168.0.101";

    public static readonly IReadOnlyList<string> DisplayNames =
    [
        LocalDisplayName,
        RemoteDisplayName
    ];

    public static DashboardDatabaseSource FromDisplayName(string? displayName)
    {
        return string.Equals(displayName, RemoteDisplayName, StringComparison.Ordinal)
            ? DashboardDatabaseSource.Remote
            : DashboardDatabaseSource.Local;
    }

    public static string ToDisplayName(DashboardDatabaseSource source)
    {
        return source == DashboardDatabaseSource.Remote
            ? RemoteDisplayName
            : LocalDisplayName;
    }
}
