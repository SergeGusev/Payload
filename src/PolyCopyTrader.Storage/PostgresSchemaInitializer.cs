using System.Text;
using Npgsql;

namespace PolyCopyTrader.Storage;

public sealed class PostgresSchemaInitializer(PostgresConnectionFactory connectionFactory) : IStorageSchemaInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            var statements = SplitSchemaSqlStatements(PostgresSchema.SchemaSql);
            for (var statementIndex = 0; statementIndex < statements.Count; statementIndex++)
            {
                var commandText = statements[statementIndex];
                Console.WriteLine(
                    $"Running PostgreSQL schema statement {statementIndex + 1}/{statements.Count}: {GetStatementPreview(commandText)}");

                await using var command = new NpgsqlCommand(commandText, connection);
                command.CommandTimeout = 0;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }

    public static IReadOnlyList<string> SplitSchemaSqlStatements(string schemaSql)
    {
        var statements = new List<string>();
        var currentStatement = new StringBuilder();
        var inSingleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;
        string? dollarQuoteTag = null;

        for (var index = 0; index < schemaSql.Length; index++)
        {
            var current = schemaSql[index];
            var next = index + 1 < schemaSql.Length ? schemaSql[index + 1] : '\0';
            currentStatement.Append(current);

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    currentStatement.Append(next);
                    index++;
                    inBlockComment = false;
                }

                continue;
            }

            if (dollarQuoteTag is not null)
            {
                if (current == '$' && MatchesAt(schemaSql, index, dollarQuoteTag))
                {
                    AppendTagRemainder(schemaSql, currentStatement, index, dollarQuoteTag);
                    index += dollarQuoteTag.Length - 1;
                    dollarQuoteTag = null;
                }

                continue;
            }

            if (inSingleQuote)
            {
                if (current == '\'' && next == '\'')
                {
                    currentStatement.Append(next);
                    index++;
                }
                else if (current == '\'')
                {
                    inSingleQuote = false;
                }

                continue;
            }

            if (current == '-' && next == '-')
            {
                currentStatement.Append(next);
                index++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                currentStatement.Append(next);
                index++;
                inBlockComment = true;
                continue;
            }

            if (current == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (current == '$')
            {
                var tag = TryReadDollarQuoteTag(schemaSql, index);
                if (tag is not null)
                {
                    AppendTagRemainder(schemaSql, currentStatement, index, tag);
                    index += tag.Length - 1;
                    dollarQuoteTag = tag;
                    continue;
                }
            }

            if (current == ';')
            {
                AddStatement(statements, currentStatement);
            }
        }

        AddStatement(statements, currentStatement);
        return statements;
    }

    private static void AddStatement(List<string> statements, StringBuilder currentStatement)
    {
        var statement = currentStatement.ToString().Trim();
        currentStatement.Clear();
        if (!string.IsNullOrWhiteSpace(statement))
        {
            statements.Add(statement);
        }
    }

    private static void AppendTagRemainder(
        string schemaSql,
        StringBuilder currentStatement,
        int tagStartIndex,
        string tag)
    {
        for (var offset = 1; offset < tag.Length; offset++)
        {
            currentStatement.Append(schemaSql[tagStartIndex + offset]);
        }
    }

    private static bool MatchesAt(string value, int startIndex, string expected)
    {
        if (startIndex + expected.Length > value.Length)
        {
            return false;
        }

        for (var offset = 0; offset < expected.Length; offset++)
        {
            if (value[startIndex + offset] != expected[offset])
            {
                return false;
            }
        }

        return true;
    }

    private static string? TryReadDollarQuoteTag(string value, int startIndex)
    {
        if (value[startIndex] != '$')
        {
            return null;
        }

        for (var index = startIndex + 1; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '$')
            {
                return value[startIndex..(index + 1)];
            }

            if (!char.IsLetterOrDigit(current) && current != '_')
            {
                return null;
            }
        }

        return null;
    }

    private static string GetStatementPreview(string commandText)
    {
        var firstLine = commandText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();
        return firstLine is null || firstLine.Length <= 120
            ? firstLine ?? string.Empty
            : string.Concat(firstLine.AsSpan(0, 120), "...");
    }
}
