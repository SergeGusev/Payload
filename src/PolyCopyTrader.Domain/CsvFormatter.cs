using System.Globalization;
using System.Text;

namespace PolyCopyTrader.Domain;

public static class CsvFormatter
{
    public static string FormatRow(IEnumerable<object?> values)
    {
        return string.Join(",", values.Select(FormatValue));
    }

    public static string FormatValue(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTimeOffset timestamp => timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\n') && !text.Contains('\r'))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        builder.Append(text.Replace("\"", "\"\"", StringComparison.Ordinal));
        builder.Append('"');
        return builder.ToString();
    }
}
