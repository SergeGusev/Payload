using System.Globalization;
using System.Numerics;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class PolymarketL1AuthHeaderFactory(ClobL1AuthSigner signer)
{
    public const string PolyAddress = "POLY_ADDRESS";
    public const string PolySignature = "POLY_SIGNATURE";
    public const string PolyTimestamp = "POLY_TIMESTAMP";
    public const string PolyNonce = "POLY_NONCE";

    public IReadOnlyDictionary<string, string> CreateL1Headers(
        string signingAddress,
        string timestamp,
        BigInteger nonce,
        string privateKey,
        int chainId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(timestamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PolyAddress] = signingAddress,
            [PolySignature] = signer.Sign(signingAddress, timestamp, nonce, privateKey, chainId),
            [PolyTimestamp] = timestamp,
            [PolyNonce] = nonce.ToString(CultureInfo.InvariantCulture)
        };
    }
}
