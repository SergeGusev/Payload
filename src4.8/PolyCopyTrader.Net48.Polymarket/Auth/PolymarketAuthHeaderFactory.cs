using System.Globalization;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class PolymarketAuthHeaderFactory(PolymarketL2HmacSigner signer)
{
    public const string PolyAddress = "POLY_ADDRESS";
    public const string PolySignature = "POLY_SIGNATURE";
    public const string PolyTimestamp = "POLY_TIMESTAMP";
    public const string PolyApiKey = "POLY_API_KEY";
    public const string PolyPassphrase = "POLY_PASSPHRASE";

    public IReadOnlyDictionary<string, string> CreateL2Headers(
        string signingAddress,
        PolymarketApiCredentials credentials,
        PolymarketAuthenticatedRequest request,
        DateTimeOffset timestamp)
    {
        Guard.NotNullOrWhiteSpace(signingAddress, nameof(signingAddress));
        Guard.NotNull(credentials, nameof(credentials));
        Guard.NotNull(request, nameof(request));

        var unixTimestamp = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var method = request.Method.ToUpperInvariant();
        var signature = signer.Sign(
            credentials.ApiSecret,
            unixTimestamp,
            method,
            request.RequestPath,
            request.Body);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PolyAddress] = signingAddress,
            [PolySignature] = signature,
            [PolyTimestamp] = unixTimestamp,
            [PolyApiKey] = credentials.ApiKey,
            [PolyPassphrase] = credentials.ApiPassphrase
        };
    }
}
