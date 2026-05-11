using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class ClobV2OrderBuilder(OrderAmountCalculator amountCalculator)
{
    private static readonly BigInteger MaxUInt256 = (BigInteger.One << 256) - BigInteger.One;
    private const long MaxJsonSafeInteger = 9_007_199_254_740_991L;

    public ClobV2Order Build(ClobV2OrderRequest request)
    {
        Guard.NotNull(request, nameof(request));

        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", validationErrors), nameof(request));
        }

        var amounts = amountCalculator.Calculate(request.Side, request.Price, request.SizeShares, request.TickSize);

        var signerAddress = request.SignatureType == ClobV2SignatureType.POLY_1271
            ? request.MakerAddress
            : request.SignerAddress;

        return new ClobV2Order(
            request.Salt ?? GenerateSalt(),
            request.MakerAddress,
            signerAddress,
            request.TokenId,
            amounts.MakerAmount.ToString(CultureInfo.InvariantCulture),
            amounts.TakerAmount.ToString(CultureInfo.InvariantCulture),
            request.Side,
            request.SignatureType,
            request.CreatedAtUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            NormalizeBytes32(request.Metadata),
            NormalizeBytes32(request.Builder),
            ResolveExpiration(request),
            request.OrderType,
            request.PostOnly,
            request.DeferExec,
            request.NegativeRisk);
    }

    public IReadOnlyList<string> Validate(ClobV2OrderRequest request)
    {
        var errors = amountCalculator.ValidateLimitOrder(
            request.Side,
            request.Price,
            request.SizeShares,
            request.TickSize,
            request.MinOrderSize).ToList();

        if (!IsAddressLike(request.MakerAddress))
        {
            errors.Add("Maker address must be a 0x-prefixed Ethereum address.");
        }

        if (!IsAddressLike(request.SignerAddress))
        {
            errors.Add("Signer address must be a 0x-prefixed Ethereum address.");
        }

        if (!TryParseUInt256(request.TokenId, out _))
        {
            errors.Add("Token id must be a uint256 decimal or hex string.");
        }

        if (request.Salt is not null && !TryParseUInt256(request.Salt, out _))
        {
            errors.Add("Salt must be a uint256 decimal or hex string.");
        }

        if (!IsBytes32OrEmpty(request.Metadata))
        {
            errors.Add("Metadata must be a 0x-prefixed bytes32 value.");
        }

        if (!IsBytes32OrEmpty(request.Builder))
        {
            errors.Add("Builder must be a 0x-prefixed bytes32 value.");
        }

        if (request.OrderType == ClobV2OrderType.GTD)
        {
            if (request.GtdExpirationUtc is null)
            {
                errors.Add("GTD orders require an expiration timestamp.");
            }
            else if (request.GtdExpirationUtc <= request.CreatedAtUtc.AddSeconds(30))
            {
                errors.Add("GTD expiration must be at least 30 seconds after order creation.");
            }
        }
        else if (request.GtdExpirationUtc is not null)
        {
            errors.Add("Only GTD orders may include an expiration timestamp.");
        }

        if (request.PostOnly && request.OrderType is ClobV2OrderType.FOK or ClobV2OrderType.FAK)
        {
            errors.Add("Post-only dry-run orders cannot use FOK or FAK.");
        }

        return errors;
    }

    public static BigInteger ParseUInt256(string value)
    {
        return TryParseUInt256(value, out var result)
            ? result
            : throw new ArgumentException("Value is not a valid uint256.", nameof(value));
    }

    public static string SideToWire(TradeSide side)
    {
        return side switch
        {
            TradeSide.Buy => "BUY",
            TradeSide.Sell => "SELL",
            _ => throw new ArgumentOutOfRangeException(nameof(side), "Unsupported CLOB order side.")
        };
    }

    public static int SideToTypedValue(TradeSide side)
    {
        return side switch
        {
            TradeSide.Buy => 0,
            TradeSide.Sell => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(side), "Unsupported CLOB order side.")
        };
    }

    public static string NormalizeBytes32(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? ClobV2ExchangeContracts.ZeroBytes32 : value;
    }

    private static string ResolveExpiration(ClobV2OrderRequest request)
    {
        return request.OrderType == ClobV2OrderType.GTD
            ? request.GtdExpirationUtc!.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            : "0";
    }

    private static string GenerateSalt()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var upperExclusive = Math.Min(Math.Max(nowMs, 2L), MaxJsonSafeInteger);
        var bytes = new byte[8];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }

        var random = BitConverter.ToUInt64(bytes, 0) & 0x1F_FF_FF_FF_FF_FF_FF;
        var salt = (long)(random % (ulong)(upperExclusive - 1L)) + 1L;
        return salt.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryParseUInt256(string value, out BigInteger result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = BigInteger.Zero;
            return false;
        }

        var trimmed = value.Trim();
        var style = NumberStyles.None;
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "0" + trimmed.Substring(2);
            style = NumberStyles.AllowHexSpecifier;
        }

        return BigInteger.TryParse(trimmed, style, CultureInfo.InvariantCulture, out result) &&
            result >= BigInteger.Zero &&
            result <= MaxUInt256;
    }

    private static bool IsAddressLike(string value)
    {
        return value.Length == 42 &&
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            value.Skip(2).All(Uri.IsHexDigit);
    }

    private static bool IsBytes32OrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Length == 66 &&
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            value.Skip(2).All(Uri.IsHexDigit);
    }
}
