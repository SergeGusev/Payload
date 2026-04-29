using System.Numerics;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class OrderAmountCalculator
{
    private const decimal TokenScale = 1_000_000m;

    public ClobV2OrderAmounts Calculate(TradeSide side, decimal price, decimal sizeShares)
    {
        ValidateSide(side);
        ValidatePositive(price, nameof(price));
        ValidatePositive(sizeShares, nameof(sizeShares));

        var sizeUnits = ToTokenUnits(sizeShares);
        var notionalUnits = ToTokenUnits(sizeShares * price);

        return side switch
        {
            TradeSide.Buy => new ClobV2OrderAmounts(side, notionalUnits, sizeUnits),
            TradeSide.Sell => new ClobV2OrderAmounts(side, sizeUnits, notionalUnits),
            _ => throw new ArgumentOutOfRangeException(nameof(side), "Unsupported CLOB order side.")
        };
    }

    public IReadOnlyList<string> ValidateLimitOrder(
        TradeSide side,
        decimal price,
        decimal sizeShares,
        decimal tickSize,
        decimal minOrderSize)
    {
        var errors = new List<string>();
        if (side is not (TradeSide.Buy or TradeSide.Sell))
        {
            errors.Add("Side must be Buy or Sell.");
        }

        if (price <= 0m || price >= 1m)
        {
            errors.Add("Price must be greater than 0 and less than 1.");
        }

        if (sizeShares <= 0m)
        {
            errors.Add("Size must be greater than 0.");
        }

        if (tickSize <= 0m)
        {
            errors.Add("Tick size must be greater than 0.");
        }
        else if (price > 0m && !IsMultiple(price, tickSize))
        {
            errors.Add("Price must align to the configured tick size.");
        }

        if (minOrderSize <= 0m)
        {
            errors.Add("Minimum order size must be greater than 0.");
        }
        else if (sizeShares < minOrderSize)
        {
            errors.Add("Size must be at least the configured minimum order size.");
        }

        if (price > 0m && sizeShares > 0m)
        {
            AddTokenUnitValidationError(errors, sizeShares, "Size");
            AddTokenUnitValidationError(errors, sizeShares * price, "Notional");
        }

        return errors;
    }

    public static BigInteger ToTokenUnits(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Token amount must not be negative.");
        }

        var scaled = value * TokenScale;
        if (scaled != decimal.Truncate(scaled))
        {
            throw new ArgumentException("Token amount has more than 6 decimal places.", nameof(value));
        }

        return new BigInteger(scaled);
    }

    private static bool IsMultiple(decimal value, decimal increment)
    {
        var units = value / increment;
        return units == decimal.Truncate(units);
    }

    private static void AddTokenUnitValidationError(List<string> errors, decimal value, string name)
    {
        try
        {
            _ = ToTokenUnits(value);
        }
        catch (ArgumentException)
        {
            errors.Add($"{name} must fit exactly into 6-decimal token units.");
        }
    }

    private static void ValidateSide(TradeSide side)
    {
        if (side is not (TradeSide.Buy or TradeSide.Sell))
        {
            throw new ArgumentOutOfRangeException(nameof(side), "Unsupported CLOB order side.");
        }
    }

    private static void ValidatePositive(decimal value, string name)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be greater than zero.");
        }
    }
}
