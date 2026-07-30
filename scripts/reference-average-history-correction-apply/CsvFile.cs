using System.Text;

namespace ReferenceAverageHistoryCorrectionApply;

internal static class CsvFile
{
    public static IReadOnlyList<T> Read<T>(string path, Func<CsvRow, T> map)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);
        using var rows = Parse(reader).GetEnumerator();
        if (!rows.MoveNext())
        {
            throw new InvalidDataException($"CSV is empty: {path}.");
        }

        var headers = rows.Current;
        if (headers.Count == 0 || headers.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException($"CSV has an invalid header: {path}.");
        }

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var column = 0; column < headers.Count; column++)
        {
            if (!index.TryAdd(headers[column], column))
            {
                throw new InvalidDataException($"Duplicate CSV column '{headers[column]}' in {path}.");
            }
        }

        var result = new List<T>();
        var line = 1L;
        while (rows.MoveNext())
        {
            line++;
            var cells = rows.Current;
            if (cells.Count != headers.Count)
            {
                throw new InvalidDataException(
                    $"CSV column count mismatch in {path} at logical row {line}: expected {headers.Count}, got {cells.Count}.");
            }

            result.Add(map(new CsvRow(path, line, index, cells)));
        }

        return result;
    }

    public static long CountDataRows(string path)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);
        long count = -1;
        foreach (var unused in Parse(reader))
        {
            count++;
        }

        return Math.Max(0, count);
    }

    private static IEnumerable<IReadOnlyList<string>> Parse(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var justClosedQuote = false;
        while (true)
        {
            var next = reader.Read();
            if (next < 0)
            {
                if (inQuotes)
                {
                    throw new InvalidDataException("CSV ended inside a quoted field.");
                }

                if (field.Length > 0 || row.Count > 0 || justClosedQuote)
                {
                    row.Add(field.ToString());
                    yield return row;
                }

                yield break;
            }

            var character = (char)next;
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        _ = reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                        justClosedQuote = true;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (justClosedQuote && character is not (',' or '\r' or '\n'))
            {
                throw new InvalidDataException("Unexpected character after a closing CSV quote.");
            }

            if (character == '"')
            {
                if (field.Length != 0)
                {
                    throw new InvalidDataException("CSV quote appeared inside an unquoted field.");
                }

                inQuotes = true;
                justClosedQuote = false;
            }
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                justClosedQuote = false;
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && reader.Peek() == '\n')
                {
                    _ = reader.Read();
                }

                row.Add(field.ToString());
                field.Clear();
                yield return row;
                row = [];
                justClosedQuote = false;
            }
            else
            {
                field.Append(character);
            }
        }
    }
}

internal readonly struct CsvRow(
    string path,
    long line,
    IReadOnlyDictionary<string, int> index,
    IReadOnlyList<string> cells)
{
    public string Required(string column)
    {
        if (!index.TryGetValue(column, out var position))
        {
            throw new InvalidDataException($"Required CSV column '{column}' is missing in {path}.");
        }

        var value = cells[position];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"CSV {path}, row {line}, column '{column}' is empty.");
        }

        return value;
    }

    public string Optional(string column) =>
        index.TryGetValue(column, out var position) ? cells[position] : string.Empty;
}
