namespace PolyCopyTrader.Domain.Security;

public static class SecretRedactor
{
    private const string Redacted = "[REDACTED]";

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= 8
            ? Redacted
            : $"{value[..4]}...{value[^4..]}";
    }
}
