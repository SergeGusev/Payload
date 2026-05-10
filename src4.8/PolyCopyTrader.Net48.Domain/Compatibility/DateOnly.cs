using System.Globalization;

namespace PolyCopyTrader.Domain;

public readonly struct DateOnly : IComparable<DateOnly>, IEquatable<DateOnly>, IFormattable
{
    private readonly DateTime value;

    public DateOnly(int year, int month, int day)
    {
        value = new DateTime(year, month, day);
    }

    private DateOnly(DateTime value)
    {
        this.value = value.Date;
    }

    public int Year => value.Year;

    public int Month => value.Month;

    public int Day => value.Day;

    public static DateOnly FromDateTime(DateTime dateTime)
    {
        return new DateOnly(dateTime.Date);
    }

    public DateOnly AddDays(int value)
    {
        return new DateOnly(this.value.AddDays(value));
    }

    public DateTime ToDateTime()
    {
        return value;
    }

    public int CompareTo(DateOnly other)
    {
        return value.CompareTo(other.value);
    }

    public bool Equals(DateOnly other)
    {
        return value.Equals(other.value);
    }

    public override bool Equals(object? obj)
    {
        return obj is DateOnly other && Equals(other);
    }

    public override int GetHashCode()
    {
        return value.GetHashCode();
    }

    public override string ToString()
    {
        return ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public string ToString(string? format)
    {
        return ToString(format, CultureInfo.InvariantCulture);
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return value.ToString(format, formatProvider);
    }

    public static bool operator ==(DateOnly left, DateOnly right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(DateOnly left, DateOnly right)
    {
        return !left.Equals(right);
    }
}
