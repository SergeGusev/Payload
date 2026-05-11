using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class PolymarketL2HmacSigner
{
    public string Sign(
        string apiSecret,
        DateTimeOffset timestamp,
        string method,
        string requestPath,
        string? body = null)
    {
        return Sign(apiSecret, timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), method, requestPath, body);
    }

    public string Sign(
        string apiSecret,
        string timestamp,
        string method,
        string requestPath,
        string? body = null)
    {
        Guard.NotNullOrWhiteSpace(apiSecret, nameof(apiSecret));
        Guard.NotNullOrWhiteSpace(timestamp, nameof(timestamp));
        Guard.NotNullOrWhiteSpace(method, nameof(method));
        Guard.NotNullOrWhiteSpace(requestPath, nameof(requestPath));

        if (!requestPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Request path must start with '/'.", nameof(requestPath));
        }

        var key = DecodeBase64Url(apiSecret);
        var message = timestamp + method + requestPath + (string.IsNullOrEmpty(body) ? string.Empty : body);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return EncodeBase64Url(hash);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => throw new FormatException("API secret is not valid base64url.")
        };

        return Convert.FromBase64String(normalized);
    }

    private static string EncodeBase64Url(byte[] value)
    {
        return Convert.ToBase64String(value).Replace('+', '-').Replace('/', '_');
    }
}
