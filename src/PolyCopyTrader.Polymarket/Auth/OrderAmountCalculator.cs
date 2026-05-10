using System.Numerics;
using System.Globalization;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class OrderAmountCalculator
{
    private const decimal TokenScale = 1_000_000m;

    public ClobV2OrderAmounts Calculate(TradeSide side, decimal price, decimal sizeShares, decimal tickSize = 0.01m)
    {
        ValidateSide(side);
        ValidatePositive(price, nameof(price));
        ValidatePositive(sizeShares, nameof(sizeShares));

        var roundConfig = GetLimitRoundConfig(tickSize) ??
            throw new ArgumentOutOfRangeException(nameof(tickSize), "Unsupported CLOB tick size.");
        var rawPrice = RoundNormal(price, roundConfig.PriceDecimals);

        if (side == TradeSide.Buy)
        {
            var rawTakerAmount = RoundDown(sizeShares, roundConfig.SizeDecimals);
            var rawMakerAmount = RoundLimitAmount(rawTakerAmount * rawPrice, roundConfig.AmountDecimals);
            return new ClobV2OrderAmounts(
                side,
                ToTokenUnitsRounded(rawMakerAmount),
                ToTokenUnitsRounded(rawTakerAmount));
        }

        var sellRawMakerAmount = RoundDown(sizeShares, roundConfig.SizeDecimals);
        var sellRawTakerAmount = RoundLimitAmount(sellRawMakerAmount * rawPrice, roundConfig.AmountDecimals);
        return new ClobV2OrderAmounts(
            side,
            ToTokenUnitsRounded(sellRawMakerAmount),
            ToTokenUnitsRounded(sellRawTakerAmount));
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
            var roundConfig = GetLimitRoundConfig(tickSize);
            if (roundConfig is { } config)
            {
                var roundedSize = RoundDown(sizeShares, config.SizeDecimals);
                if (roundedSize <= 0m)
                {
                    errors.Add("Size rounds to zero at the CLOB size precision for the configured tick size.");
                }

                if (minOrderSize > 0m && roundedSize < minOrderSize)
                {
                    errors.Add("Rounded size must be at least the configured minimum order size.");
                }

                if (side is TradeSide.Buy or TradeSide.Sell)
                {
                    var amounts = Calculate(side, price, sizeShares, tickSize);
                    if (amounts.MakerAmount <= BigInteger.Zero || amounts.TakerAmount <= BigInteger.Zero)
                    {
                        errors.Add("Rounded CLOB maker and taker amounts must be greater than zero.");
                    }
                }
            }
            else
            {
                AddTokenUnitValidationError(errors, sizeShares, "Size");
                AddTokenUnitValidationError(errors, sizeShares * price, "Notional");
            }
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

    public static BigInteger ToTokenUnitsRounded(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Token amount must not be negative.");
        }

        return new BigInteger(RoundNormal(value * TokenScale, 0));
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

    private static LimitRoundConfig? GetLimitRoundConfig(decimal tickSize)
    {
        return tickSize switch
        {
            0.1m => new LimitRoundConfig(1, 2, 3),
            0.01m => new LimitRoundConfig(2, 2, 4),
            0.001m => new LimitRoundConfig(3, 2, 5),
            0.0001m => new LimitRoundConfig(4, 2, 6),
            _ => null
        };
    }

    private static decimal RoundLimitAmount(decimal value, int amountDecimals)
    {
        if (DecimalPlaces(value) <= amountDecimals)
        {
            return value;
        }

        var roundedUp = RoundUp(value, amountDecimals + 4);
        return DecimalPlaces(roundedUp) > amountDecimals
            ? RoundDown(roundedUp, amountDecimals)
            : roundedUp;
    }

    private static decimal RoundNormal(decimal value, int decimals)
    {
        if (DecimalPlaces(value) <= decimals)
        {
            return value;
        }

        return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    }

    private static decimal RoundDown(decimal value, int decimals)
    {
        if (DecimalPlaces(value) <= decimals)
        {
            return value;
        }

        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Floor(value * factor) / factor;
    }

    private static decimal RoundUp(decimal value, int decimals)
    {
        if (DecimalPlaces(value) <= decimals)
        {
            return value;
        }

        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Ceiling(value * factor) / factor;
    }

    private static int DecimalPlaces(decimal value)
    {
        var text = Math.Abs(value).ToString("0.#############################", CultureInfo.InvariantCulture);
        var separator = text.IndexOf('.', StringComparison.Ordinal);
        return separator < 0 ? 0 : text.Length - separator - 1;
    }

    private sealed record LimitRoundConfig(int PriceDecimals, int SizeDecimals, int AmountDecimals);

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
