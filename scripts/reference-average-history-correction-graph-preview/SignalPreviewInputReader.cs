using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal static class SignalPreviewInputReader
{
    private static readonly string[] RequiredColumns =
    [
        "scope", "asset", "family", "location", "kind", "trigger",
        "catalog_threshold_bps", "strategy_id", "strategy_code", "run_id",
        "paper_order_id", "market_id", "entry_due_at_utc", "settled_at_utc",
        "run_outcome", "order_outcome", "action", "reason", "current_price_usd",
        "minimum_average_price_usd", "minimum_average_window",
        "minimum_average_window_seconds", "maximum_average_price_usd",
        "maximum_average_window", "maximum_average_window_seconds",
        "json_threshold_bps", "move_below_minimum_bps", "move_above_maximum_bps",
        "required_window", "assumed_fill_price", "legacy_v1_outcome",
        "corrected_v2_outcome"
    ];

    private static readonly string[] RequiredCatalogColumns =
    [
        "asset", "family", "location", "kind", "trigger", "catalog_threshold_bps",
        "reference_threshold_bps", "strategy_id", "code", "name", "uses_low_enter_price"
    ];

    public static async Task<SignalPreviewInput> LoadAsync(
        string inputDirectory,
        DateTimeOffset requiredCutoffUtc,
        CancellationToken cancellationToken)
    {
        var directory = RequireBelowCodexTemp(inputDirectory, "--signal-preview-dir");
        var manifestPath = Path.Combine(directory, "manifest.json");
        var removePath = Path.Combine(directory, "remove.csv");
        var addPath = Path.Combine(directory, "add.csv");
        var catalogPath = Path.Combine(directory, "catalog.csv");
        foreach (var path in new[] { manifestPath, removePath, addPath, catalogPath })
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required signal-preview input is missing: {path}", path);
            }
        }

        var partial = Directory.EnumerateFiles(directory, "*.partial", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (partial is not null)
        {
            throw new InvalidOperationException($"Signal preview is incomplete because a partial file exists: {partial}");
        }

        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
        if (!string.Equals(
                manifestHash,
                CorrectionContract.RequiredInputManifestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Signal-preview manifest SHA-256 must be {CorrectionContract.RequiredInputManifestSha256}, actual {manifestHash}.");
        }
        using var document = JsonDocument.Parse(manifestBytes);
        var root = document.RootElement;
        RequireNumber(root, "schema_version", CorrectionContract.RequiredInputSchemaVersion);
        RequireString(root, "tool", CorrectionContract.RequiredInputTool);
        var cutoff = ReadRequiredTimestamp(root, "cutoff_utc");
        if (cutoff != requiredCutoffUtc.ToUniversalTime())
        {
            throw new InvalidOperationException(
                $"Signal-preview cutoff {cutoff:O} does not equal required cutoff {requiredCutoffUtc:O}.");
        }

        RequireNumber(root, "invariant_error_count", 0);
        var snapshot = RequireObject(root, "database_snapshot");
        RequireString(snapshot, "host", CorrectionContract.RequiredHost);
        RequireNumber(snapshot, "port", CorrectionContract.RequiredPort);
        RequireString(snapshot, "database", CorrectionContract.RequiredDatabase);
        RequireString(snapshot, "server_address", CorrectionContract.RequiredHost);
        RequireStringIgnoreCase(snapshot, "transaction_isolation", "repeatable read");
        RequireBoolean(snapshot, "transaction_read_only", true);
        RequireStringIgnoreCase(snapshot, "time_zone", "UTC");

        var safety = RequireObject(root, "safety");
        RequireString(safety, "host_parameter", CorrectionContract.RequiredHost);
        RequireString(safety, "required_host", CorrectionContract.RequiredHost);
        RequireBoolean(safety, "transaction_read_only", true);
        RequireBoolean(safety, "transaction_rolled_back", true);
        RequireNumber(safety, "database_write_statements_issued", 0);
        RequireBoolean(safety, "safe_to_continue_to_mutation_design", true);

        var catalogManifest = RequireObject(root, "catalog");
        RequireString(catalogManifest, "path", CorrectionContract.RequiredInputCatalogPath);
        RequireStringIgnoreCase(
            catalogManifest,
            "sha256",
            CorrectionContract.RequiredInputCatalogSourceSha256);
        RequireNumber(
            catalogManifest,
            "strategy_count",
            CorrectionContract.RequiredCatalogStrategyCount);
        RequireNumber(
            catalogManifest,
            "potential_add_strategy_count",
            CorrectionContract.RequiredPotentialAddStrategyCount);

        var actionCounts = RequireObject(root, "action_counts");
        RequireNumber(actionCounts, "Remove", CorrectionContract.RequiredRemoveCount);
        RequireNumber(actionCounts, "Add", CorrectionContract.RequiredAddCount);
        RequireNumber(actionCounts, "Retain", CorrectionContract.RequiredRetainCount);
        RequireNumber(actionCounts, "StillSkip", CorrectionContract.RequiredStillSkipCount);
        RequireNumber(actionCounts, "Unreplayable", CorrectionContract.RequiredUnreplayableCount);
        RequireNumber(actionCounts, "InvariantError", 0);

        var reclassification = RequireObject(root, "offline_reclassification");
        RequireStringIgnoreCase(
            reclassification,
            "source_manifest_sha256",
            CorrectionContract.RequiredInputSourceManifestSha256);
        RequireStringIgnoreCase(
            reclassification,
            "replay_classifier_sha256",
            CorrectionContract.RequiredInputReplayClassifierSha256);
        RequireNumber(reclassification, "remaining_invariant_count", 0);

        var evidence = ReadFileEvidence(root);
        await ValidateEvidenceAsync(removePath, "remove.csv", evidence, cancellationToken);
        await ValidateEvidenceAsync(addPath, "add.csv", evidence, cancellationToken);
        await ValidateEvidenceAsync(catalogPath, "catalog.csv", evidence, cancellationToken);
        var catalogEvidence = evidence.Single(item => item.FileName == "catalog.csv");
        if (!string.Equals(
                catalogEvidence.Sha256,
                CorrectionContract.RequiredInputCatalogCsvSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Signal-preview catalog.csv SHA-256 is not the pinned catalog hash.");
        }

        var catalog = await ReadCatalogCsvAsync(catalogPath, cancellationToken);
        var removes = await ReadClassificationCsvAsync(removePath, cancellationToken);
        var adds = await ReadClassificationCsvAsync(addPath, cancellationToken);
        ValidateRows(removes, adds, catalog, requiredCutoffUtc);
        ValidateEvidenceCount("remove.csv", removes.Count, evidence);
        ValidateEvidenceCount("add.csv", adds.Count, evidence);
        ValidateEvidenceCount("catalog.csv", catalog.Count, evidence);
        if (catalog.Count != CorrectionContract.RequiredCatalogStrategyCount ||
            removes.Count != CorrectionContract.RequiredRemoveCount ||
            adds.Count != CorrectionContract.RequiredAddCount)
        {
            throw new InvalidDataException("Pinned signal-preview action/catalog counts do not match parsed rows.");
        }

        return new SignalPreviewInput(
            directory,
            manifestPath,
            manifestHash,
            cutoff,
            CorrectionContract.RequiredHost,
            removes,
            adds,
            catalog,
            evidence);
    }

    private static void ValidateRows(
        IReadOnlyList<SignalPreviewRow> removes,
        IReadOnlyList<SignalPreviewRow> adds,
        IReadOnlyList<SignalPreviewCatalogRow> catalog,
        DateTimeOffset cutoffUtc)
    {
        var catalogByStrategy = catalog.ToDictionary(item => item.StrategyId);
        var runIds = new HashSet<Guid>();
        var orderIds = new HashSet<Guid>();
        foreach (var row in removes)
        {
            var catalogRow = RequireMatchingCatalogRow(row, catalogByStrategy);
            if (!runIds.Add(row.RunId))
            {
                throw new InvalidOperationException($"Duplicate remove run_id: {row.RunId:D}");
            }

            if (row.PaperOrderId is not { } orderId || !orderIds.Add(orderId))
            {
                throw new InvalidOperationException(
                    row.PaperOrderId is null
                        ? $"Remove row {row.RunId:D} has no paper_order_id."
                        : $"Duplicate remove paper_order_id: {row.PaperOrderId:D}");
            }

            if (!string.Equals(row.Action, "Remove", StringComparison.Ordinal) ||
                !string.Equals(row.CorrectedV2Outcome, "Skip", StringComparison.Ordinal) ||
                !string.Equals(row.RunOutcome, "Up", StringComparison.Ordinal) ||
                !string.Equals(row.OrderOutcome, "Up", StringComparison.Ordinal) ||
                row.EntryDueAtUtc >= cutoffUtc)
            {
                throw new InvalidOperationException($"Remove row {row.RunId:D} violates the input contract.");
            }
        }

        foreach (var row in adds)
        {
            var catalogRow = RequireMatchingCatalogRow(row, catalogByStrategy);
            if (!runIds.Add(row.RunId))
            {
                throw new InvalidOperationException($"Duplicate or cross-file run_id: {row.RunId:D}");
            }

            var expectedFillPrice = catalogRow.UsesLowEnterPrice
                ? CorrectionContract.LowerEnterFillPrice
                : CorrectionContract.RegularFillPrice;
            if (row.PaperOrderId is not null ||
                !string.Equals(row.Action, "Add", StringComparison.Ordinal) ||
                !string.Equals(row.LegacyV1Outcome, "Skip", StringComparison.Ordinal) ||
                !string.Equals(row.CorrectedV2Outcome, "Up", StringComparison.Ordinal) ||
                row.AssumedFillPrice != expectedFillPrice ||
                row.EntryDueAtUtc >= cutoffUtc)
            {
                throw new InvalidOperationException($"Add row {row.RunId:D} violates the input contract.");
            }
        }
    }

    internal static bool IsLowerEnter(string kind) =>
        kind.Contains("LowerEnter", StringComparison.OrdinalIgnoreCase) ||
        kind.Contains("LowEnter", StringComparison.OrdinalIgnoreCase);

    private static SignalPreviewCatalogRow RequireMatchingCatalogRow(
        SignalPreviewRow row,
        IReadOnlyDictionary<Guid, SignalPreviewCatalogRow> catalogByStrategy)
    {
        if (!catalogByStrategy.TryGetValue(row.StrategyId, out var catalog) ||
            !string.Equals(row.Asset, catalog.Asset, StringComparison.Ordinal) ||
            !string.Equals(row.Family, catalog.Family, StringComparison.Ordinal) ||
            !string.Equals(row.Location, catalog.Location, StringComparison.Ordinal) ||
            !string.Equals(row.Kind, catalog.Kind, StringComparison.Ordinal) ||
            !string.Equals(row.Trigger, catalog.Trigger, StringComparison.Ordinal) ||
            row.CatalogThresholdBps != catalog.CatalogThresholdBps ||
            !string.Equals(row.StrategyCode, catalog.StrategyCode, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Classification row {row.RunId:D} does not match pinned catalog strategy {row.StrategyId:D}.");
        }

        return catalog;
    }

    private static async Task<IReadOnlyList<SignalPreviewCatalogRow>> ReadCatalogCsvAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var rows = new List<SignalPreviewCatalogRow>();
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 64 * 1024);
        var csv = StrictCsv.Read(reader).GetEnumerator();
        if (!csv.MoveNext() || !csv.Current.SequenceEqual(RequiredCatalogColumns, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"Unexpected catalog CSV header: {path}");
        }

        var ids = new HashSet<Guid>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        var line = 1;
        while (csv.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            line++;
            var values = csv.Current;
            if (values.Length != RequiredCatalogColumns.Length)
            {
                throw new InvalidDataException($"CSV row {line} in {path} has {values.Length} fields.");
            }

            var id = ParseGuid(values[7], path, line, "strategy_id");
            if (!ids.Add(id) || !codes.Add(values[8]))
            {
                throw new InvalidDataException($"Duplicate catalog strategy at {path}:{line}.");
            }

            var usesLowEnter = values[10] switch
            {
                "true" => true,
                "false" => false,
                _ => throw Invalid(path, line, "uses_low_enter_price", values[10])
            };
            rows.Add(new SignalPreviewCatalogRow(
                values[0], values[1], values[2], values[3], values[4],
                ParseInt(values[5], path, line, "catalog_threshold_bps"),
                ParseInt(values[6], path, line, "reference_threshold_bps"),
                id, values[8], values[9], usesLowEnter));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<SignalPreviewRow>> ReadClassificationCsvAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var rows = new List<SignalPreviewRow>();
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 64 * 1024);
        var csv = StrictCsv.Read(reader).GetEnumerator();
        if (!csv.MoveNext())
        {
            throw new InvalidDataException($"CSV is empty: {path}");
        }

        var header = csv.Current;
        if (!header.SequenceEqual(RequiredColumns, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"Unexpected classification CSV header: {path}");
        }

        var line = 1;
        while (csv.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            line++;
            var values = csv.Current;
            if (values.Length != RequiredColumns.Length)
            {
                throw new InvalidDataException($"CSV row {line} in {path} has {values.Length} fields.");
            }

            var replayEvidence = BuildReplayEvidence(values);
            rows.Add(new SignalPreviewRow(
                values[0], values[1], values[2], values[3], values[4], values[5],
                ParseInt(values[6], path, line, "catalog_threshold_bps"),
                ParseGuid(values[7], path, line, "strategy_id"),
                values[8],
                ParseGuid(values[9], path, line, "run_id"),
                ParseNullableGuid(values[10], path, line, "paper_order_id"),
                values[11],
                ParseTimestamp(values[12], path, line, "entry_due_at_utc"),
                ParseNullableTimestamp(values[13], path, line, "settled_at_utc"),
                values[14], values[15], values[16], values[17],
                ParseNullableDecimal(values[29], path, line, "assumed_fill_price"),
                values[30], values[31], replayEvidence.Json, replayEvidence.Sha256));
        }

        return rows;
    }

    private static (string Json, string Sha256) BuildReplayEvidence(IReadOnlyList<string> values)
    {
        var json = JsonSerializer.Serialize(new
        {
            schema_version = 2,
            signal_preview_manifest_sha256 = CorrectionContract.RequiredInputManifestSha256,
            replay_classifier_sha256 = CorrectionContract.RequiredInputReplayClassifierSha256,
            scope = values[0],
            asset = values[1],
            family = values[2],
            location = values[3],
            kind = values[4],
            trigger = values[5],
            catalog_threshold_bps = values[6],
            strategy_id = values[7],
            strategy_code = values[8],
            run_id = values[9],
            paper_order_id = values[10],
            market_id = values[11],
            entry_due_at_utc = values[12],
            action = values[16],
            reason = values[17],
            current_price_usd = values[18],
            minimum_average_price_usd = values[19],
            minimum_average_window = values[20],
            minimum_average_window_seconds = values[21],
            maximum_average_price_usd = values[22],
            maximum_average_window = values[23],
            maximum_average_window_seconds = values[24],
            json_threshold_bps = values[25],
            move_below_minimum_bps = values[26],
            move_above_maximum_bps = values[27],
            required_window = values[28],
            legacy_v1_outcome = values[30],
            corrected_v2_outcome = values[31]
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        return (json, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static IReadOnlyList<SignalPreviewFileEvidence> ReadFileEvidence(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Signal-preview manifest has no files array.");
        }

        var result = new List<SignalPreviewFileEvidence>();
        foreach (var item in files.EnumerateArray())
        {
            result.Add(new SignalPreviewFileEvidence(
                ReadRequiredString(item, "file_name"),
                ReadRequiredInt64(item, "row_count"),
                ReadRequiredString(item, "sha256")));
        }

        if (result.Select(item => item.FileName).Distinct(StringComparer.Ordinal).Count() != result.Count)
        {
            throw new InvalidDataException("Signal-preview manifest contains duplicate file evidence.");
        }

        return result;
    }

    private static async Task ValidateEvidenceAsync(
        string path,
        string fileName,
        IReadOnlyList<SignalPreviewFileEvidence> evidence,
        CancellationToken cancellationToken)
    {
        var expected = evidence.SingleOrDefault(item => string.Equals(item.FileName, fileName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Signal-preview manifest has no evidence for {fileName}.");
        await using var stream = File.OpenRead(path);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"SHA-256 mismatch for {fileName}: expected {expected.Sha256}, actual {actualHash}.");
        }
    }

    private static void ValidateEvidenceCount(
        string fileName,
        long actual,
        IReadOnlyList<SignalPreviewFileEvidence> evidence)
    {
        var expected = evidence.Single(item => string.Equals(item.FileName, fileName, StringComparison.Ordinal));
        if (actual != expected.RowCount)
        {
            throw new InvalidDataException(
                $"Row count mismatch for {fileName}: expected {expected.RowCount}, actual {actual}.");
        }
    }

    internal static string RequireBelowCodexTemp(string path, string argumentName)
    {
        var root = Path.GetFullPath(@"D:\CodexTemp").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{argumentName} must resolve below {root}");
        }

        return resolved;
    }

    private static int ParseInt(string value, string path, int line, string field) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw Invalid(path, line, field, value);

    private static Guid ParseGuid(string value, string path, int line, string field) =>
        System.Guid.TryParseExact(value, "D", out var parsed)
            ? parsed
            : throw Invalid(path, line, field, value);

    private static Guid? ParseNullableGuid(string value, string path, int line, string field) =>
        string.IsNullOrEmpty(value) ? null : ParseGuid(value, path, line, field);

    private static decimal? ParseNullableDecimal(string value, string path, int line, string field) =>
        string.IsNullOrEmpty(value)
            ? null
            : decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw Invalid(path, line, field, value);

    private static DateTimeOffset ParseTimestamp(string value, string path, int line, string field) =>
        DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : throw Invalid(path, line, field, value);

    private static DateTimeOffset? ParseNullableTimestamp(string value, string path, int line, string field) =>
        string.IsNullOrEmpty(value) ? null : ParseTimestamp(value, path, line, field);

    private static InvalidDataException Invalid(string path, int line, string field, string value) =>
        new($"Invalid {field} at {path}:{line}: '{value}'.");

    private static JsonElement RequireObject(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Manifest property {property} is missing or not an object.");
        }

        return value;
    }

    private static void RequireString(JsonElement parent, string property, string expected)
    {
        var actual = ReadRequiredString(parent, property);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Manifest {property} expected '{expected}', actual '{actual}'.");
        }
    }

    private static void RequireStringIgnoreCase(JsonElement parent, string property, string expected)
    {
        var actual = ReadRequiredString(parent, property);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Manifest {property} expected '{expected}', actual '{actual}'.");
        }
    }

    private static string ReadRequiredString(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"Manifest string property {property} is missing.");

    private static long ReadRequiredInt64(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.TryGetInt64(out var parsed)
            ? parsed
            : throw new InvalidDataException($"Manifest integer property {property} is missing.");

    private static void RequireNumber(JsonElement parent, string property, long expected)
    {
        var actual = ReadRequiredInt64(parent, property);
        if (actual != expected)
        {
            throw new InvalidDataException($"Manifest {property} expected {expected}, actual {actual}.");
        }
    }

    private static void RequireBoolean(JsonElement parent, string property, bool expected)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            value.GetBoolean() != expected)
        {
            throw new InvalidDataException($"Manifest {property} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static DateTimeOffset ReadRequiredTimestamp(JsonElement parent, string property)
    {
        var raw = ReadRequiredString(parent, property);
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new InvalidDataException($"Manifest {property} is not an ISO-8601 timestamp.");
        }

        return parsed.ToUniversalTime();
    }
}

internal static class StrictCsv
{
    public static IEnumerable<string[]> Read(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var afterQuote = false;
        var sawAny = false;
        while (true)
        {
            var next = reader.Read();
            if (next < 0)
            {
                if (quoted)
                {
                    throw new InvalidDataException("CSV ended inside a quoted field.");
                }

                if (sawAny || field.Length > 0 || row.Count > 0)
                {
                    row.Add(field.ToString());
                    yield return row.ToArray();
                }

                yield break;
            }

            sawAny = true;
            var character = (char)next;
            if (quoted)
            {
                if (character == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        quoted = false;
                        afterQuote = true;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (afterQuote && character is not (',' or '\r' or '\n'))
            {
                throw new InvalidDataException("Unexpected character after a quoted CSV field.");
            }

            if (character == '"')
            {
                if (field.Length != 0 || afterQuote)
                {
                    throw new InvalidDataException("Unexpected quote inside an unquoted CSV field.");
                }

                quoted = true;
                continue;
            }

            if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                afterQuote = false;
                continue;
            }

            if (character is '\r' or '\n')
            {
                if (character == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }

                row.Add(field.ToString());
                field.Clear();
                afterQuote = false;
                yield return row.ToArray();
                row.Clear();
                sawAny = false;
                continue;
            }

            field.Append(character);
        }
    }
}
