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

        string secret = value ?? string.Empty;
        return secret.Length <= 8
            ? Redacted
            : $"{secret.Substring(0, 4)}...{secret.Substring(secret.Length - 4, 4)}";
    }
}
