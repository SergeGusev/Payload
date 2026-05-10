namespace PolyCopyTrader.Domain.Configuration;

public static class StorageConnectionResolver
{
    public static string? Resolve(StorageOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return options.ConnectionString;
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionStringEnvironmentVariable))
        {
            return null;
        }

        var value = Environment.GetEnvironmentVariable(options.ConnectionStringEnvironmentVariable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static bool IsConfigured(StorageOptions options)
    {
        return !string.IsNullOrWhiteSpace(Resolve(options));
    }
}
