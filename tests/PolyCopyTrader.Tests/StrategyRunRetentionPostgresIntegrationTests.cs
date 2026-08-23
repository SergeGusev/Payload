using System.Data;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

internal sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public PostgresIntegrationFactAttribute()
    {
        var validationError = DisposablePostgresIntegrationGuard
            .GetConfiguredConnectionValidationError();
        if (validationError is not null)
        {
            Skip = validationError;
        }
    }
}

internal sealed class PostgresStoragePrototypeFactAttribute : FactAttribute
{
    public PostgresStoragePrototypeFactAttribute()
    {
        Skip = DisposablePostgresIntegrationGuard.GetConfiguredConnectionValidationError()
            ?? DisposablePostgresIntegrationGuard.GetConfiguredEvidenceDirectoryValidationError();
    }
}

internal static partial class DisposablePostgresIntegrationGuard
{
    private const string ConnectionEnvironmentVariable =
        "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION";
    internal const string EvidenceDirectoryEnvironmentVariable =
        "POLYCOPYTRADER_TEST_SKIP_V2_EVIDENCE_DIRECTORY";

    [GeneratedRegex(
        "^pct_codex_skip_v2_[0-9]{14}_[0-9a-f]{8}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex AllowedDatabaseNameRegex();

    internal static string? GetConfiguredConnectionValidationError()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return $"{ConnectionEnvironmentVariable} is not configured.";
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            return $"{ConnectionEnvironmentVariable} is invalid: {exception.Message}";
        }

        if (!IsConfiguredLoopbackHost(builder.Host ?? string.Empty))
        {
            return $"{ConnectionEnvironmentVariable} must target one loopback host.";
        }

        if (!AllowedDatabaseNameRegex().IsMatch(builder.Database ?? string.Empty))
        {
            return $"{ConnectionEnvironmentVariable} database is not an allowlisted disposable database.";
        }

        return null;
    }

    internal static string? GetConfiguredEvidenceDirectoryValidationError()
    {
        var configuredPath = Environment.GetEnvironmentVariable(
            EvidenceDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return $"{EvidenceDirectoryEnvironmentVariable} is not configured.";
        }

        string evidencePath;
        try
        {
            evidencePath = Path.GetFullPath(configuredPath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"{EvidenceDirectoryEnvironmentVariable} is invalid: {exception.Message}";
        }

        var runsRoot = Path.GetFullPath(@"D:\CodexTemp\runs").TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (!evidencePath.StartsWith(
                runsRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"{EvidenceDirectoryEnvironmentVariable} must be inside D:\\CodexTemp\\runs.";
        }

        var relativePath = Path.GetRelativePath(runsRoot, evidencePath);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return $"{EvidenceDirectoryEnvironmentVariable} must be a child of one marked run.";
        }

        if (!segments[1].Equals("results", StringComparison.OrdinalIgnoreCase))
        {
            return $"{EvidenceDirectoryEnvironmentVariable} must be inside the marked run's results directory.";
        }

        var runPath = Path.Combine(runsRoot, segments[0]);
        var markerPath = Path.Combine(runPath, ".codex-ephemeral.json");
        if (!File.Exists(markerPath))
        {
            return $"Codex ownership marker is missing: {markerPath}";
        }

        try
        {
            var runDirectory = new DirectoryInfo(runPath);
            if (!runDirectory.Exists)
            {
                return $"Codex run directory does not exist: {runPath}";
            }

            if ((runDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return $"Codex run directory must not be a reparse point: {runPath}";
            }

            var markerFile = new FileInfo(markerPath);
            if ((markerFile.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return $"Codex ownership marker must not be a reparse point: {markerPath}";
            }

            using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
            var root = marker.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1
                || !string.Equals(
                    root.GetProperty("owner").GetString(),
                    "OpenAI Codex",
                    StringComparison.Ordinal)
                || !string.Equals(
                    root.GetProperty("kind").GetString(),
                    "ephemeral-session",
                    StringComparison.Ordinal)
                || !string.Equals(
                    Path.GetFullPath(root.GetProperty("runPath").GetString() ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    runPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return $"Codex ownership marker does not identify the selected run: {markerPath}";
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or KeyNotFoundException
                or InvalidOperationException
                or ArgumentException)
        {
            return $"Codex ownership marker is invalid: {markerPath}. {exception.Message}";
        }

        if (!Directory.Exists(evidencePath))
        {
            return $"Evidence directory does not exist: {evidencePath}";
        }

        var current = new DirectoryInfo(evidencePath);
        while (current is not null
               && !current.FullName.Equals(runPath, StringComparison.OrdinalIgnoreCase))
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return $"Evidence path must not traverse a reparse point: {current.FullName}";
            }

            current = current.Parent;
        }

        return current is null
            ? $"Evidence directory is not beneath its marked run: {evidencePath}"
            : null;
    }

    internal static string GetConfiguredEvidenceDirectory()
    {
        var validationError = GetConfiguredEvidenceDirectoryValidationError();
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        return Path.GetFullPath(Environment.GetEnvironmentVariable(
            EvidenceDirectoryEnvironmentVariable)!);
    }

    internal static async Task<PostgresConnectionFactory> CreateInitializedFactoryAsync()
    {
        var validationError = GetConfiguredConnectionValidationError();
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)!;
        var factory = new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString });
        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
SELECT
    current_database(),
    current_setting('server_version_num')::integer,
    inet_server_addr();
""",
                connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            var databaseName = reader.GetString(0);
            var serverVersionNumber = reader.GetInt32(1);
            var serverAddress = reader.GetFieldValue<IPAddress>(2);
            Assert.Matches(AllowedDatabaseNameRegex(), databaseName);
            Assert.Equal(18, serverVersionNumber / 10_000);
            Assert.NotEqual(IPAddress.Any, serverAddress);
            Assert.NotEqual(IPAddress.IPv6Any, serverAddress);
            Assert.NotEqual(IPAddress.Parse("192.168.0.101"), serverAddress);
            Assert.False(await reader.ReadAsync());
        }

        await new PostgresSchemaInitializer(factory).InitializeAsync();
        return factory;
    }

    private static bool IsConfiguredLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class StrategyRunRetentionPostgresIntegrationTests
{
    [PostgresStoragePrototypeFact]
    [Trait("Category", "PostgresStoragePrototype")]
    public async Task CompactSkipArchiveV2_StoragePrototype_IsSmallestAndUsesBoundedIndexes()
    {
        const string generatorVersion = "compact-skip-archive-v2-prototype-v2";
        var factory = await CreateFactoryAsync();
        var evidenceDirectory = DisposablePostgresIntegrationGuard
            .GetConfiguredEvidenceDirectory();
        var schemaName = $"skip_v2_prototype_{Guid.NewGuid():N}";
        var quotedSchemaName = new NpgsqlCommandBuilder().QuoteIdentifier(schemaName);
        var layouts = CreatePrototypeLayouts();

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        var schemaCreated = false;
        try
        {
            var databaseEvidence = await ReadPrototypeDatabaseEvidenceAsync(connection);
            Assert.Equal(18, databaseEvidence.ServerVersionNumber / 10_000);
            Assert.Equal("UTF8", databaseEvidence.ServerEncoding);

            await ExecutePrototypeSqlAsync(
                connection,
                $"CREATE SCHEMA {quotedSchemaName}; SET search_path TO {quotedSchemaName}, pg_catalog;");
            schemaCreated = true;

            WritePrototypeEvidenceFile(
                evidenceDirectory,
                "fixture-generator-v2.sql",
                PrototypeFixtureSql);
            WritePrototypeEvidenceFile(
                evidenceDirectory,
                "candidate-layouts-v2.sql",
                PrototypeCandidateLayoutsSql);
            WritePrototypeEvidenceFile(
                evidenceDirectory,
                "candidate-population-v2.sql",
                PrototypePopulationSql);

            await ExecutePrototypeSqlAsync(connection, PrototypeCandidateLayoutsSql);
            await VacuumPrototypeTablesAsync(connection, layouts);
            var emptyMeasurements = await MeasurePrototypeLayoutsAsync(
                connection,
                schemaName,
                layouts);
            WritePrototypeJsonEvidenceFile(
                evidenceDirectory,
                "empty-layout-sizes.json",
                emptyMeasurements);

            await ExecutePrototypeSqlAsync(connection, PrototypeFixtureSql);
            await ExecutePrototypeSqlAsync(connection, PrototypePopulationSql);
            await VacuumPrototypeTablesAsync(connection, layouts);

            var fixtureCounts = await ReadPrototypeFixtureCountsAsync(connection);
            Assert.Equal(262_144, fixtureCounts.Rows);
            Assert.Equal(256, fixtureCounts.Strategies);
            Assert.Equal(1_024, fixtureCounts.MarketIdentities);
            Assert.Equal(1_032, fixtureCounts.MetadataVersions);
            Assert.Equal(1_032, fixtureCounts.MetadataDimensionRows);
            Assert.Equal(8, fixtureCounts.DualVersionMarkets);
            Assert.Equal(1_016, fixtureCounts.SingleVersionMarkets);
            Assert.Equal(37, fixtureCounts.Reasons);
            Assert.Equal(30, fixtureCounts.UtcDays);
            Assert.True(fixtureCounts.NullableStatesAllCovered);
            Assert.InRange(fixtureCounts.AverageConditionBytes, 66.0, 68.0);
            Assert.InRange(fixtureCounts.AverageSlugBytes, 24.0, 26.0);
            Assert.InRange(fixtureCounts.AverageTitleBytes, 47.0, 49.0);
            Assert.InRange(fixtureCounts.AverageCategoryBytes, 6.0, 8.0);
            Assert.InRange(fixtureCounts.AverageReasonBytes, 42.0, 44.0);

            var restorationEvidence = await ReadPrototypeCanonicalRestorationEvidenceAsync(
                connection,
                layouts);
            var fixtureRestoration = Assert.Single(
                restorationEvidence,
                evidence => evidence.Layout == "fixture");
            Assert.Equal(262_144, fixtureRestoration.Rows);
            foreach (var layout in layouts)
            {
                var layoutRestoration = Assert.Single(
                    restorationEvidence,
                    evidence => evidence.Layout == layout.Name);
                Assert.Equal(fixtureRestoration.Rows, layoutRestoration.Rows);
                Assert.Equal(fixtureRestoration.Sha256, layoutRestoration.Sha256);
            }

            WritePrototypeJsonEvidenceFile(
                evidenceDirectory,
                "canonical-restoration-hashes.json",
                restorationEvidence);

            var populatedMeasurements = await MeasurePrototypeLayoutsAsync(
                connection,
                schemaName,
                layouts);
            AssertPrototypeLayoutRowCounts(populatedMeasurements);
            WritePrototypeJsonEvidenceFile(
                evidenceDirectory,
                "populated-layout-sizes.json",
                populatedMeasurements);

            var comparisons = ComparePrototypeLayoutSizes(
                emptyMeasurements,
                populatedMeasurements);
            var proposed = Assert.Single(
                comparisons,
                comparison => comparison.Layout == "proposed_normalized_v2");
            var currentV1 = Assert.Single(
                comparisons,
                comparison => comparison.Layout == "current_v1");
            foreach (var alternative in comparisons.Where(
                         comparison => comparison.Layout != proposed.Layout))
            {
                Assert.True(
                    proposed.PopulatedTotalBytes < alternative.PopulatedTotalBytes,
                    $"Proposed populated bytes {proposed.PopulatedTotalBytes} must be below " +
                    $"{alternative.Layout} bytes {alternative.PopulatedTotalBytes}.");
                Assert.True(
                    proposed.DeltaTotalBytes < alternative.DeltaTotalBytes,
                    $"Proposed delta bytes {proposed.DeltaTotalBytes} must be below " +
                    $"{alternative.Layout} delta bytes {alternative.DeltaTotalBytes}.");
            }

            Assert.True(
                proposed.PopulatedTotalBytes * 100L
                    <= currentV1.PopulatedTotalBytes * 65L,
                $"Proposed populated bytes {proposed.PopulatedTotalBytes} are not at least " +
                $"35% below v1 bytes {currentV1.PopulatedTotalBytes}.");

            var querySpecs = await CreatePrototypeQuerySpecsAsync(connection);
            Assert.Equal(6, querySpecs.Count);
            var queryEvidence = new List<PrototypeQueryEvidence>(querySpecs.Count);
            foreach (var spec in querySpecs)
            {
                WritePrototypeEvidenceFile(
                    evidenceDirectory,
                    $"query-{spec.Name}.sql",
                    FormatPrototypeQueryEvidenceSql(spec));

                var expectedResult = await ReadPrototypeQueryResultAsync(
                    connection,
                    spec.ExpectedSql,
                    spec.Parameters);
                var actualResult = await ReadPrototypeQueryResultAsync(
                    connection,
                    spec.ActualSql,
                    spec.Parameters);
                Assert.Equal(expectedResult.Cardinality, actualResult.Cardinality);
                Assert.Equal(expectedResult.Sha256, actualResult.Sha256);
                Assert.InRange(actualResult.Cardinality, 0, spec.ResultCardinalityLimit);

                var plan = await ReadPrototypePlanAsync(
                    connection,
                    spec.ActualSql,
                    spec.Parameters);
                var planEvidence = InspectPrototypePlan(plan, spec);
                Assert.False(
                    planEvidence.HasProposedTombstoneSequentialScan,
                    $"{spec.Name} used a sequential scan of proposed_tombstones.");
                Assert.Equal(spec.RequiredIndexName, planEvidence.DrivingIndexName);
                Assert.InRange(
                    planEvidence.DrivingExaminedRows,
                    0,
                    spec.ExaminedRowsLimit(expectedResult.Cardinality));
                Assert.All(
                    planEvidence.SequentialDimensionRelations,
                    relation => Assert.InRange(relation.RelationRows, 0, 1_032));

                var planFileName = $"plan-{spec.Name}.json";
                WritePrototypeJsonEvidenceFile(
                    evidenceDirectory,
                    planFileName,
                    plan.RootElement);
                queryEvidence.Add(new PrototypeQueryEvidence(
                    spec.Name,
                    expectedResult.Cardinality,
                    expectedResult.Sha256,
                    actualResult.Sha256,
                    planEvidence.DrivingIndexName,
                    planEvidence.DrivingNodeType,
                    planEvidence.DrivingExaminedRows,
                    planFileName));
            }

            var report = new PrototypeCompletionReport(
                generatorVersion,
                databaseEvidence.Version,
                databaseEvidence.DatabaseName,
                fixtureCounts,
                restorationEvidence,
                comparisons,
                queryEvidence,
                PrototypeFixtureSql,
                PrototypeCandidateLayoutsSql,
                PrototypePopulationSql,
                "Prototype-only measurements from an isolated PostgreSQL 18 database; " +
                "deployed savings remain unknown until separately approved activation and measurement.");
            WritePrototypeJsonEvidenceFile(
                evidenceDirectory,
                "storage-prototype-summary.json",
                report);
            Console.WriteLine(
                $"Compact skipped-run v2 storage prototype evidence: {evidenceDirectory}");
        }
        finally
        {
            if (schemaCreated)
            {
                await ExecutePrototypeSqlAsync(
                    connection,
                    $"RESET search_path; DROP SCHEMA {quotedSchemaName} CASCADE;");
            }
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task V2Dimensions_ResolveExactValuesConcurrentlyAndRemainImmutable()
    {
        var factory = await CreateFactoryAsync();
        var detectedAtUtc = DateTimeOffset.UtcNow.AddDays(-3);
        var exactRun = CreateSkippedRun(Guid.NewGuid(), detectedAtUtc.AddMinutes(10)) with
        {
            MarketId = $"v2-dimension-market-{Guid.NewGuid():N}",
            ConditionId = $"v2-dimension-condition-{Guid.NewGuid():N}",
            MarketSlug = $"v2-dimension-slug-{Guid.NewGuid():N}",
            MarketTitle = "V2 dimension exact metadata",
            Category = "Тест",
            MarketStartUtc = detectedAtUtc,
            MarketEndUtc = detectedAtUtc.AddMinutes(5),
            DetectedAtUtc = detectedAtUtc,
            CreatedAtUtc = detectedAtUtc,
            SkipReason = "ByteExactReason"
        };

        var concurrent = await Task.WhenAll(
            Enumerable.Range(0, 4)
                .Select(_ => ResolveV2DimensionsAsync(factory, exactRun)));
        Assert.Single(concurrent.Select(value => value.MarketIdentityId).Distinct());
        Assert.Single(concurrent.Select(value => value.MetadataVersionId).Distinct());
        Assert.Single(concurrent.Select(value => value.SkipReasonId).Distinct());

        var nullableVersion = exactRun with
        {
            MarketTitle = "V2 dimension nullable metadata version",
            Category = null,
            MarketStartUtc = null,
            MarketEndUtc = null
        };
        var secondVersion = await ResolveV2DimensionsAsync(factory, nullableVersion);
        Assert.Equal(concurrent[0].MarketIdentityId, secondVersion.MarketIdentityId);
        Assert.NotEqual(concurrent[0].MetadataVersionId, secondVersion.MetadataVersionId);
        Assert.Equal(concurrent[0].SkipReasonId, secondVersion.SkipReasonId);

        var independentNullableVersions = new[]
        {
            exactRun with { Category = null },
            exactRun with { MarketStartUtc = null },
            exactRun with { MarketEndUtc = null }
        };
        var independentNullableIds = new List<int>();
        foreach (var variant in independentNullableVersions)
        {
            var first = await ResolveV2DimensionsAsync(factory, variant);
            var retry = await ResolveV2DimensionsAsync(factory, variant);
            Assert.Equal(first, retry);
            independentNullableIds.Add(first.MetadataVersionId);
        }
        Assert.Equal(3, independentNullableIds.Distinct().Count());
        Assert.DoesNotContain(concurrent[0].MetadataVersionId, independentNullableIds);
        Assert.DoesNotContain(secondVersion.MetadataVersionId, independentNullableIds);

        var byteDistinctReason = await ResolveV2DimensionsAsync(
            factory,
            exactRun with { SkipReason = exactRun.SkipReason + " " });
        Assert.NotEqual(concurrent[0].SkipReasonId, byteDistinctReason.SkipReasonId);

        var counts = await ReadV2DimensionCountsAsync(factory, exactRun.MarketId);
        Assert.Equal((1L, 5L), (counts.MarketIdentities, counts.MetadataVersions));

        await AssertV2DimensionMutationRejectedAsync(
            factory,
            "UPDATE strategy_skip_archive_market_identities " +
            "SET market_id = market_id || '-changed' WHERE market_identity_id = @Id;",
            concurrent[0].MarketIdentityId);
        await AssertV2DimensionMutationRejectedAsync(
            factory,
            "DELETE FROM strategy_skip_archive_market_metadata_versions " +
            "WHERE metadata_version_id = @Id;",
            concurrent[0].MetadataVersionId);
        await AssertV2DimensionMutationRejectedAsync(
            factory,
            "UPDATE strategy_skip_archive_reasons " +
            "SET skip_reason = skip_reason || '-changed' WHERE skip_reason_id = @Id;",
            concurrent[0].SkipReasonId);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task V2DormantDirectWriter_ConcurrentEqualTuplesReuseRealDimensions()
    {
        var factory = await CreateFactoryAsync();
        var firstStrategyId = Guid.NewGuid();
        var secondStrategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-3);
        var shared = CreateSkippedRun(firstStrategyId, oldUtc) with
        {
            MarketId = $"v2-real-dimension-{Guid.NewGuid():N}",
            ConditionId = $"v2-real-condition-{Guid.NewGuid():N}",
            MarketSlug = $"v2-real-slug-{Guid.NewGuid():N}",
            MarketTitle = "V2 dormant writer concurrent dimension reuse",
            Category = null,
            MarketStartUtc = null,
            MarketEndUtc = null,
            SelectedAssetId = null,
            SelectedOutcome = null,
            SkipReason = $"v2-real-reason-{Guid.NewGuid():N}"
        };
        var second = shared with
        {
            Id = Guid.NewGuid(),
            StrategyId = secondStrategyId
        };

        await InsertStrategyAsync(
            factory,
            firstStrategyId,
            $"v2_dimension_first_{Guid.NewGuid():N}",
            liveStakes: false);
        await InsertStrategyAsync(
            factory,
            secondStrategyId,
            $"v2_dimension_second_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            var writes = await Task.WhenAll(
                new PostgresAppRepository(factory)
                    .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync([shared]),
                new PostgresAppRepository(factory)
                    .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync([second]));
            Assert.Equal([shared.Id], writes[0].ToArray());
            Assert.Equal([second.Id], writes[1].ToArray());

            var dimensions = await ReadV2DimensionReferenceCountsAsync(
                factory,
                shared.MarketId,
                shared.SkipReason!);
            Assert.Equal(new V2DimensionReferenceCounts(1, 1, 1, 2), dimensions);
            Assert.Equal(2, (await ReadArchiveStorageVersionsAsync(factory, firstStrategyId))[shared.Id]);
            Assert.Equal(2, (await ReadArchiveStorageVersionsAsync(factory, secondStrategyId))[second.Id]);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, firstStrategyId);
            await DeleteTestStrategyAsync(factory, secondStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task V2DirectWriter_UsesV2WhenExactlyRepresentableAndV1FallbackOtherwise()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"v2_direct_{Guid.NewGuid():N}";
        var nowUtc = DateTimeOffset.UtcNow.AddDays(-3);
        var v2Run = CreateSkippedRun(strategyId, nowUtc);
        var v1FallbackRun = CreateSkippedRun(strategyId, nowUtc.AddMinutes(1)) with
        {
            CreatedAtUtc = nowUtc.AddMinutes(-30)
        };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            var inserted = await repository
                .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
                    [v2Run, v1FallbackRun]);
            Assert.Equal(
                new[] { v2Run.Id, v1FallbackRun.Id }.OrderBy(id => id),
                inserted.OrderBy(id => id));

            var storageVersions = await ReadArchiveStorageVersionsAsync(factory, strategyId);
            Assert.Equal(2, storageVersions[v2Run.Id]);
            Assert.Equal(1, storageVersions[v1FallbackRun.Id]);
            Assert.Equal(
                new RetentionCounts(0, 2, 2, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));

            var retry = await repository
                .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
                    [v2Run, v1FallbackRun]);
            Assert.Empty(retry);
            Assert.Equal(
                new RetentionCounts(0, 2, 2, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));

            var archivedPayload = await ReadRestorableArchivePayloadAsync(factory, v2Run.Id);
            await repository.AddPaperOrderAsync(CreatePaperOrder(
                strategyId,
                v2Run.ConditionId,
                nowUtc.AddHours(1)));

            Assert.Equal(
                archivedPayload,
                await ReadRestorableRawPayloadAsync(factory, v2Run.Id));
            Assert.Equal(
                StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, v2Run.Id));
            var restoredCounts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((1L, 1L, 1L, 1L),
                (restoredCounts.RawRuns,
                    restoredCounts.RollupRuns,
                    restoredCounts.Tombstones,
                    restoredCounts.ReconciliationQueueRows));
            Assert.Equal([v1FallbackRun.Id], await ReadArchivedRunIdsAsync(factory, strategyId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveEnable_RestoresV1AndV2AtBoundaryAndKeepsStrictlyEarlierV2Archived()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"v2_live_boundary_{Guid.NewGuid():N}";
        var dayUtc = new DateTimeOffset(
            DateTime.UtcNow.Date.AddDays(-3).AddHours(12),
            TimeSpan.Zero);
        var preLiveV2 = CreateSkippedRun(strategyId, dayUtc);
        var boundaryV2 = CreateSkippedRun(strategyId, dayUtc.AddMinutes(1));
        var postBoundaryV1 = CreateSkippedRun(strategyId, dayUtc.AddMinutes(2)) with
        {
            CreatedAtUtc = dayUtc.AddHours(-1)
        };
        var allRuns = new[] { preLiveV2, boundaryV2, postBoundaryV1 };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.Equal(
                allRuns.Select(run => run.Id).OrderBy(id => id),
                (await repository
                    .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(allRuns))
                    .OrderBy(id => id));
            var before = await ReadArchiveStorageVersionsAsync(factory, strategyId);
            Assert.Equal(2, before[preLiveV2.Id]);
            Assert.Equal(2, before[boundaryV2.Id]);
            Assert.Equal(1, before[postBoundaryV1.Id]);

            Assert.True(await repository.SetStrategyLiveStakesAsync(
                strategyId,
                liveStakes: true,
                updatedAtUtc: boundaryV2.UpdatedAtUtc));

            Assert.Equal(
                new[] { boundaryV2.Id, postBoundaryV1.Id }.OrderBy(id => id),
                (await ReadRunIdsAsync(factory, allRuns.Select(run => run.Id).ToArray()))
                .OrderBy(id => id));
            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, boundaryV2.Id));
            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, postBoundaryV1.Id));
            var remaining = await ReadArchiveStorageVersionsAsync(factory, strategyId);
            var remainingArchive = Assert.Single(remaining);
            Assert.Equal(preLiveV2.Id, remainingArchive.Key);
            Assert.Equal(2, remainingArchive.Value);
            var liveCounts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((2L, 1L, 1L, 1L),
                (liveCounts.RawRuns,
                    liveCounts.RollupRuns,
                    liveCounts.Tombstones,
                    liveCounts.ReconciliationQueueRows));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveEnable_WinningSerializationBlocksV2ArchiveAndRetainsLiveRunRaw()
    {
        var factory = await CreateFactoryAsync();
        var strategyId = Guid.NewGuid();
        var strategyCode = $"v2_live_first_{Guid.NewGuid():N}";
        var boundaryUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var run = CreateSkippedRun(strategyId, boundaryUtc.AddSeconds(1));
        var writerApplicationName = $"v2_live_first_writer_{Guid.NewGuid():N}";
        var writerFactory = WithApplicationName(factory, writerApplicationName);
        using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<IReadOnlySet<Guid>>? writerTask = null;

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await using var liveConnection = factory.CreateConnection();
            await liveConnection.OpenAsync();
            await using var liveTransaction = await liveConnection.BeginTransactionAsync();
            await using (var liveCommand = new NpgsqlCommand(
                             """
UPDATE public.strategies
SET live_stakes = true,
    live_enabled_at_utc = @BoundaryUtc,
    updated_at_utc = @BoundaryUtc
WHERE id = @StrategyId;
""",
                             liveConnection,
                             liveTransaction))
            {
                liveCommand.Parameters.AddWithValue("BoundaryUtc", boundaryUtc.UtcDateTime);
                liveCommand.Parameters.AddWithValue("StrategyId", strategyId);
                Assert.Equal(1, await liveCommand.ExecuteNonQueryAsync());
            }

            writerTask = new PostgresAppRepository(writerFactory)
                .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
                    [run],
                    raceCancellation.Token);
            var writerPid = await WaitForBlockedApplicationAsync(
                factory,
                writerApplicationName,
                "advisory");
            await AssertBlockedByAsync(factory, writerPid, liveConnection.ProcessID);

            await liveTransaction.CommitAsync();
            Assert.Equal(
                [run.Id],
                (await writerTask.WaitAsync(TimeSpan.FromSeconds(15))).ToArray());

            Assert.Empty(await ReadArchiveStorageVersionsAsync(factory, strategyId));
            Assert.Equal(
                StrategyMarketPaperRunStatuses.Skipped,
                await ReadRunStatusAsync(factory, run.Id));
            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, run.Id));
        }
        finally
        {
            await DrainRaceTaskAsync(writerTask);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveEnableAndArchive_SerializeBothCommitOrdersForV1AndV2()
    {
        foreach (var archiveVersion in new short[] { 1, 2 })
        {
            await AssertLiveArchiveSerializationAsync(
                archiveVersion,
                liveEnableWins: true);
            await AssertLiveArchiveSerializationAsync(
                archiveVersion,
                liveEnableWins: false);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task V2ExistingRawAndAgeWriters_ArchiveEligibleRowsThroughTheirDormantSeams()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"v2_existing_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var observed = CreateSkippedRun(strategyId, oldUtc) with
        {
            Status = StrategyMarketPaperRunStatuses.Observed,
            SkipReason = null
        };
        var finalized = observed with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = "v2_existing_raw_skip",
            DetectedAtUtc = oldUtc.AddMinutes(-10),
            CreatedAtUtc = oldUtc.AddMinutes(-10),
            UpdatedAtUtc = oldUtc.AddMinutes(1)
        };
        var ageRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(2)) with
        {
            SkipReason = "v2_age_transfer_skip"
        };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(observed));
            await repository.FinalizeStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
                [finalized]);
            Assert.Null(await ReadRunStatusAsync(factory, finalized.Id));
            Assert.Equal(
                2,
                (await ReadArchiveStorageVersionsAsync(factory, strategyId))[finalized.Id]);

            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(ageRun));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            var ageResult = await repository
                .TransferPaperOnlySkippedRunsToCompactArchiveV2ForTestsAsync(
                    [ageRun.Id],
                    DateTimeOffset.UtcNow.AddHours(-48));
            Assert.Equal(1, ageResult.SelectedRows);
            Assert.Equal(1, ageResult.DeletedRows);
            Assert.Equal(1, ageResult.TombstonesChanged);
            Assert.Null(await ReadRunStatusAsync(factory, ageRun.Id));

            var versions = await ReadArchiveStorageVersionsAsync(factory, strategyId);
            Assert.Equal(2, versions[finalized.Id]);
            Assert.Equal(2, versions[ageRun.Id]);
            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((0L, 2L, 2L),
                (counts.RawRuns, counts.RollupRuns, counts.Tombstones));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task V2Writers_IndependentlyPreserveIntrinsicFeeLivePersistedScopeDependencyAndAgeOnlyBlockers()
    {
        var factory = await CreateFactoryAsync();

        foreach (var writer in Enum.GetValues<V2WriterKind>())
        {
            await AssertV2WriterBlockerMatrixAsync(factory, writer);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task V2CapacityFallbackAndDeleteFailureAreWholeTransactionSafe()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var capacityStrategyId = Guid.NewGuid();
        var rollbackStrategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var capacityRun = CreateSkippedRun(capacityStrategyId, oldUtc) with
        {
            MarketId = $"v2-capacity-market-{Guid.NewGuid():N}",
            ConditionId = $"v2-capacity-condition-{Guid.NewGuid():N}",
            MarketSlug = $"v2-capacity-slug-{Guid.NewGuid():N}",
            SkipReason = $"v2-capacity-reason-{Guid.NewGuid():N}"
        };
        var rollbackRun = CreateSkippedRun(rollbackStrategyId, oldUtc.AddMinutes(1)) with
        {
            MarketId = $"v2-rollback-market-{Guid.NewGuid():N}",
            ConditionId = $"v2-rollback-condition-{Guid.NewGuid():N}",
            MarketSlug = $"v2-rollback-slug-{Guid.NewGuid():N}",
            SkipReason = $"v2-rollback-reason-{Guid.NewGuid():N}"
        };
        var triggerSuffix = Guid.NewGuid().ToString("N");
        var triggerName = $"trg_v2_delete_failure_{triggerSuffix}";
        var functionName = $"v2_delete_failure_{triggerSuffix}";

        await InsertStrategyAsync(
            factory,
            capacityStrategyId,
            $"v2_capacity_{Guid.NewGuid():N}",
            liveStakes: false);
        await InsertStrategyAsync(
            factory,
            rollbackStrategyId,
            $"v2_rollback_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            await SetIdentitySequenceAtLimitAsync(
                factory,
                "strategy_skip_archive_reasons",
                "skip_reason_id");
            try
            {
                Assert.Equal(
                    [capacityRun.Id],
                    (await repository
                        .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
                            [capacityRun]))
                    .ToArray());
            }
            finally
            {
                await ResetIdentitySequenceToTableMaximumAsync(
                    factory,
                    "strategy_skip_archive_market_identities",
                    "market_identity_id");
                await ResetIdentitySequenceToTableMaximumAsync(
                    factory,
                    "strategy_skip_archive_market_metadata_versions",
                    "metadata_version_id");
                await ResetIdentitySequenceToTableMaximumAsync(
                    factory,
                    "strategy_skip_archive_reasons",
                    "skip_reason_id");
            }

            Assert.Equal(1, (await ReadArchiveStorageVersionsAsync(
                factory,
                capacityStrategyId))[capacityRun.Id]);
            Assert.Equal(
                new V2DimensionReferenceCounts(0, 0, 0, 0),
                await ReadV2DimensionReferenceCountsAsync(
                    factory,
                    capacityRun.MarketId,
                    capacityRun.SkipReason!));
            Assert.Equal(
                new RetentionCounts(0, 1, 1, 1, 1),
                await ReadRetentionCountsAsync(factory, capacityStrategyId));

            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(rollbackRun));
            await DeleteProjectionBlockersAsync(factory, rollbackStrategyId);
            await CreateDeleteFailureTriggerAsync(
                factory,
                triggerName,
                functionName,
                rollbackRun.Id);
            try
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                    repository.TransferPaperOnlySkippedRunsToCompactArchiveV2ForTestsAsync(
                        [rollbackRun.Id],
                        DateTimeOffset.UtcNow.AddHours(-48)));
                Assert.Equal("P0001", exception.SqlState);
            }
            finally
            {
                await DropDeleteFailureTriggerAsync(factory, triggerName, functionName);
            }

            Assert.Equal(
                await ReadRestorableRawPayloadAsync(factory, rollbackRun.Id),
                await ReadRunPayloadWithoutScopeAsync(factory, rollbackRun.Id));
            Assert.Empty(await ReadArchiveStorageVersionsAsync(factory, rollbackStrategyId));
            var rollbackCounts = await ReadRetentionCountsAsync(factory, rollbackStrategyId);
            Assert.Equal(
                new RetentionCounts(1, 0, 0, 0, 0),
                rollbackCounts);
            Assert.Equal(
                new V2DimensionReferenceCounts(0, 0, 0, 0),
                await ReadV2DimensionReferenceCountsAsync(
                    factory,
                    rollbackRun.MarketId,
                    rollbackRun.SkipReason!));
        }
        finally
        {
            await DropDeleteFailureTriggerAsync(factory, triggerName, functionName);
            await DeleteTestStrategyAsync(factory, capacityStrategyId);
            await DeleteTestStrategyAsync(factory, rollbackStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Bootstrap_MixedV1V2ArchivesFeedEqualLifetimeAndRecentContributions()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"v2_dashboard_{Guid.NewGuid():N}";
        var capturedNowUtc = DateTimeOffset.UtcNow;
        var alignedNowUtc = capturedNowUtc.AddTicks(
            -(capturedNowUtc.Ticks % TimeSpan.TicksPerSecond));
        var updatedAtUtc = alignedNowUtc.AddMinutes(-30);
        var v1RunId = Guid.NewGuid();
        var v2Run = CreateSkippedRun(strategyId, updatedAtUtc) with
        {
            SkipReason = "mixed_v2_skip"
        };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await InsertArchivedPaperSkipAsync(
                factory,
                strategyId,
                v1RunId,
                updatedAtUtc,
                "mixed_v1_skip");
            Assert.Equal(
                [v2Run.Id],
                (await repository
                    .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync([v2Run]))
                .ToArray());

            await projection.BootstrapAsync();

            var lifetime = (await snapshots.GetStrategyPerformanceSnapshotAsync())
                .Single(row => row.StrategyId == strategyId);
            Assert.Equal(2, lifetime.SkippedRunsCount);
            Assert.Equal(2, lifetime.PaperConditionSkippedRunsCount);
            Assert.Equal(updatedAtUtc, lifetime.LastRunUtc);

            var recentRows = (await snapshots.GetStrategyRecentPerformanceSnapshotAsync())
                .Where(row => row.StrategyId == strategyId)
                .ToDictionary(row => row.WindowHours);
            var directRecentRows = (await repository.GetStrategyRecentPerformanceAsync())
                .Where(row => row.StrategyId == strategyId)
                .ToDictionary(row => row.WindowHours);
            foreach (var windowHours in new[] { 1, 6, 24 })
            {
                Assert.Equal(2, recentRows[windowHours].SkippedRunsCount);
                Assert.Equal(2, recentRows[windowHours].PaperConditionSkippedRunsCount);
                Assert.Equal(updatedAtUtc, recentRows[windowHours].LastRunUtc);
                Assert.Equal(2, directRecentRows[windowHours].SkippedRunsCount);
                Assert.Equal(2, directRecentRows[windowHours].PaperConditionSkippedRunsCount);
                Assert.Equal(updatedAtUtc, directRecentRows[windowHours].LastRunUtc);
            }

            Assert.Equal(2, await ReadRecentStrategyRunFactCountAsync(factory, v1RunId));
            Assert.Equal(2, await ReadRecentStrategyRunFactCountAsync(factory, v2Run.Id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task CompatibilityInitializationTwicePreservesV1AndAllPublicWritersStayV1()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var terminalRun = CreateSkippedRun(strategyId, oldUtc);
        var observed = CreateSkippedRun(strategyId, oldUtc.AddMinutes(1)) with
        {
            Status = StrategyMarketPaperRunStatuses.Observed,
            SkipReason = null
        };
        var finalized = observed with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = "compatibility-finalized-v1",
            UpdatedAtUtc = oldUtc.AddMinutes(2)
        };
        var agedRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(3));

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"compatibility_v1_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            Assert.Equal(
                [terminalRun.Id],
                (await repository.TryAddStrategyMarketPaperRunsAsync(
                    [terminalRun],
                    directPaperSkipCompactionEnabled: true)).ToArray());
            await DeleteProjectionBlockersAsync(factory, strategyId);

            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(observed));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            await repository.FinalizeStrategyMarketPaperRunsAsync(
                [finalized],
                directPaperSkipCompactionEnabled: true);
            await DeleteProjectionBlockersAsync(factory, strategyId);

            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(agedRun));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [agedRun.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);

            var expectedVersions = new[] { terminalRun.Id, finalized.Id, agedRun.Id }
                .ToDictionary(id => id, _ => (short)1);
            Assert.Equal(expectedVersions, await ReadArchiveStorageVersionsAsync(factory, strategyId));
            Assert.Equal(0, await ReadV2TombstoneCountAsync(factory, strategyId));
            var physicalV1RowsBefore = await ReadPhysicalV1ArchiveRowsAsync(factory, strategyId);
            Assert.Equal(expectedVersions.Count, physicalV1RowsBefore.Count);
            var dimensionsBefore = await ReadV2DimensionTableSnapshotAsync(factory);

            var initializer = new PostgresSchemaInitializer(factory);
            await initializer.InitializeAsync();
            await initializer.InitializeAsync();

            Assert.Equal(expectedVersions, await ReadArchiveStorageVersionsAsync(factory, strategyId));
            Assert.Equal(
                physicalV1RowsBefore,
                await ReadPhysicalV1ArchiveRowsAsync(factory, strategyId));
            Assert.Equal(
                dimensionsBefore,
                await ReadV2DimensionTableSnapshotAsync(factory));
            Assert.Equal(0, await ReadV2TombstoneCountAsync(factory, strategyId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task V1V2CollisionsAndEveryRawWriterFailClosedWithoutReplacingArchive()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var v2StrategyId = Guid.NewGuid();
        var v1StrategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-3);
        var v2Run = CreateSkippedRun(v2StrategyId, oldUtc);
        var v1Run = CreateSkippedRun(v1StrategyId, oldUtc.AddMinutes(1));

        await InsertStrategyAsync(
            factory,
            v2StrategyId,
            $"v2_collision_{Guid.NewGuid():N}",
            liveStakes: false);
        await InsertStrategyAsync(
            factory,
            v1StrategyId,
            $"v1_collision_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            Assert.Equal(
                [v2Run.Id],
                (await repository
                    .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync([v2Run]))
                .ToArray());
            Assert.Equal(
                [v1Run.Id],
                (await repository.TryAddStrategyMarketPaperRunsAsync(
                    [v1Run],
                    directPaperSkipCompactionEnabled: true)).ToArray());

            var v2SameId = v2Run with
            {
                MarketId = $"v2-collision-other-{Guid.NewGuid():N}",
                ConditionId = $"v2-collision-other-{Guid.NewGuid():N}",
                MarketSlug = $"v2-collision-other-{Guid.NewGuid():N}"
            };
            var v2SameMarket = v2Run with { Id = Guid.NewGuid() };
            Assert.False(await repository.TryAddStrategyMarketPaperRunAsync(v2SameId));
            Assert.False(await repository.TryAddStrategyMarketPaperRunAsync(v2SameMarket));
            Assert.Empty(await repository.TryAddStrategyMarketPaperRunsAsync(
                [v2SameId, v2SameMarket]));
            await repository.UpdateStrategyMarketPaperRunsAsync([v2SameId, v2SameMarket]);
            Assert.Empty(await repository.TryAddStrategyMarketPaperRunsAsync(
                [v2SameId, v2SameMarket],
                directPaperSkipCompactionEnabled: true));

            var v1SameId = v1Run with
            {
                MarketId = $"v1-collision-other-{Guid.NewGuid():N}",
                ConditionId = $"v1-collision-other-{Guid.NewGuid():N}",
                MarketSlug = $"v1-collision-other-{Guid.NewGuid():N}"
            };
            var v1SameMarket = v1Run with { Id = Guid.NewGuid() };
            Assert.Empty(await repository
                .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
                    [v1SameId, v1SameMarket]));

            var attemptedRawIds = new[]
            {
                v2SameId.Id,
                v2SameMarket.Id,
                v1SameId.Id,
                v1SameMarket.Id
            };
            Assert.Empty(await ReadRunIdsAsync(factory, attemptedRawIds));
            Assert.Equal(2, (await ReadArchiveStorageVersionsAsync(factory, v2StrategyId))[v2Run.Id]);
            Assert.Equal(1, (await ReadArchiveStorageVersionsAsync(factory, v1StrategyId))[v1Run.Id]);

            var malformed = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertMalformedV2TombstoneAsync(factory, v2StrategyId));
            Assert.Equal("23503", malformed.SqlState);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, v2StrategyId);
            await DeleteTestStrategyAsync(factory, v1StrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task V2EveryLateDependencyRestoresExactRunAndMixedRollupRecomputesCountMinMax()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldCode = $"v2_restore_old_{Guid.NewGuid():N}";
        var newCode = $"v2_restore_new_{Guid.NewGuid():N}";
        var prefix = $"v2-all-restore-{Guid.NewGuid():N}";
        var oldUtc = new DateTimeOffset(
            DateTime.UtcNow.Date.AddDays(-4).AddHours(12),
            TimeSpan.Zero);
        var reason = $"v2-shared-rollup-reason-{Guid.NewGuid():N}";

        StrategyMarketPaperRun Fixture(string name, int minute) => WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(minute)) with
            {
                SkipReason = reason
            },
            $"{prefix}-{name}");

        var firstV1 = Fixture("first-v1", 0) with
        {
            CreatedAtUtc = oldUtc.AddMinutes(-30)
        };
        var paper = Fixture("paper", -10);
        var dryRun = Fixture("dry-run", 2);
        var live = Fixture("live", 3);
        var shadowMarket = Fixture("shadow-market", 4);
        var shadowCondition = Fixture("shadow-condition", 5);
        var position = Fixture("position", 6);
        var settlement = Fixture("settlement", 7);
        var copiedPosition = Fixture("copied-position", 8);
        var copiedActivity = Fixture("copied-activity", 9);
        var onchain = Fixture("onchain", 10);
        var codeRemap = Fixture("code-remap", 30);
        var lastV1 = Fixture("last-v1", 20) with
        {
            CreatedAtUtc = oldUtc.AddMinutes(-29)
        };
        var v2Runs = new[]
        {
            paper,
            dryRun,
            live,
            shadowMarket,
            shadowCondition,
            position,
            settlement,
            copiedPosition,
            copiedActivity,
            onchain,
            codeRemap
        };

        await InsertStrategyAsync(factory, strategyId, oldCode, liveStakes: false);
        try
        {
            await repository.UpsertPaperPositionAsync(new PaperPosition(
                $"asset-{Guid.NewGuid():N}",
                codeRemap.ConditionId,
                "Yes",
                2m,
                0.50m,
                1m,
                0m,
                oldUtc,
                $"strategy:{newCode}"));

            Assert.Equal(
                new[] { firstV1.Id, lastV1.Id }.OrderBy(id => id),
                (await repository.TryAddStrategyMarketPaperRunsAsync(
                    [firstV1, lastV1],
                    directPaperSkipCompactionEnabled: true)).OrderBy(id => id));
            Assert.Equal(
                v2Runs.Select(run => run.Id).OrderBy(id => id),
                (await repository
                    .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(v2Runs))
                .OrderBy(id => id));

            var archivedPayloads = new Dictionary<Guid, string>();
            foreach (var run in v2Runs)
            {
                archivedPayloads.Add(
                    run.Id,
                    await ReadRestorableArchivePayloadAsync(factory, run.Id));
            }
            Assert.Equal(
                new RollupGroup(13, paper.UpdatedAtUtc, codeRemap.UpdatedAtUtc),
                await ReadRollupGroupAsync(factory, strategyId, oldUtc, reason));

            var restoredV2RunIds = new HashSet<Guid>();
            async Task AssertOnlyTargetRestoredAsync(StrategyMarketPaperRun target)
            {
                Assert.True(restoredV2RunIds.Add(target.Id));
                Assert.Equal(
                    archivedPayloads[target.Id],
                    await ReadRestorableRawPayloadAsync(factory, target.Id));
                Assert.Equal(
                    restoredV2RunIds.OrderBy(id => id),
                    (await ReadRunIdsAsync(factory, v2Runs.Select(run => run.Id).ToArray()))
                    .OrderBy(id => id));

                var remainingVersions = await ReadArchiveStorageVersionsAsync(factory, strategyId);
                Assert.Equal(1, remainingVersions[firstV1.Id]);
                Assert.Equal(1, remainingVersions[lastV1.Id]);
                foreach (var run in v2Runs)
                {
                    if (restoredV2RunIds.Contains(run.Id))
                    {
                        Assert.DoesNotContain(run.Id, remainingVersions.Keys);
                    }
                    else
                    {
                        Assert.Equal(2, remainingVersions[run.Id]);
                    }
                }

                var remainingArchivedRuns = new[] { firstV1, lastV1 }
                    .Concat(v2Runs.Where(run => !restoredV2RunIds.Contains(run.Id)))
                    .ToArray();
                Assert.Equal(
                    new RollupGroup(
                        remainingArchivedRuns.Length,
                        remainingArchivedRuns.Min(run => run.UpdatedAtUtc),
                        remainingArchivedRuns.Max(run => run.UpdatedAtUtc)),
                    await ReadRollupGroupAsync(factory, strategyId, oldUtc, reason));
            }

            await repository.AddPaperOrderAsync(CreatePaperOrder(
                strategyId,
                paper.ConditionId,
                oldUtc.AddHours(1)));
            await AssertOnlyTargetRestoredAsync(paper);
            Assert.Equal(
                new RollupGroup(12, firstV1.UpdatedAtUtc, codeRemap.UpdatedAtUtc),
                await ReadRollupGroupAsync(factory, strategyId, oldUtc, reason));

            await InsertDirectExternalDependencyAsync(
                factory,
                strategyId,
                oldUtc.AddHours(1),
                dryRun,
                DirectExternalDependencyKind.DryRunOrder);
            await AssertOnlyTargetRestoredAsync(dryRun);

            await repository.AddLiveOrderAsync(CreateLiveOrder(
                strategyId,
                live.ConditionId,
                oldUtc.AddHours(1)));
            await AssertOnlyTargetRestoredAsync(live);

            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                strategyId,
                shadowMarket.MarketId,
                $"{prefix}-shadow-market-unrelated-condition",
                oldUtc.AddHours(1)));
            await AssertOnlyTargetRestoredAsync(shadowMarket);

            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                strategyId,
                $"{prefix}-shadow-condition-unrelated-market",
                shadowCondition.ConditionId,
                oldUtc.AddHours(1)));
            await AssertOnlyTargetRestoredAsync(shadowCondition);

            await repository.UpsertPaperPositionAsync(new PaperPosition(
                $"asset-{Guid.NewGuid():N}",
                position.ConditionId,
                "Yes",
                2m,
                0.50m,
                1m,
                0m,
                oldUtc.AddHours(1),
                $"strategy:{oldCode}"));
            await AssertOnlyTargetRestoredAsync(position);

            Assert.True(await repository.TryAddPaperPositionSettlementAsync(
                CreateSettlement(
                    $"strategy:{oldCode}",
                    settlement.ConditionId,
                    oldUtc.AddHours(1))));
            await AssertOnlyTargetRestoredAsync(settlement);

            await InsertDirectExternalDependencyAsync(
                factory,
                strategyId,
                oldUtc.AddHours(1),
                copiedPosition,
                DirectExternalDependencyKind.CopiedLeaderPosition);
            await AssertOnlyTargetRestoredAsync(copiedPosition);

            await InsertDirectExternalDependencyAsync(
                factory,
                strategyId,
                oldUtc.AddHours(1),
                copiedActivity,
                DirectExternalDependencyKind.CopiedLeaderActivity);
            await AssertOnlyTargetRestoredAsync(copiedActivity);

            await InsertDirectExternalDependencyAsync(
                factory,
                strategyId,
                oldUtc.AddHours(1),
                onchain,
                DirectExternalDependencyKind.OnchainPaperSignalResult);
            await AssertOnlyTargetRestoredAsync(onchain);

            await UpdateStrategyCodeAsync(
                factory,
                strategyId,
                newCode,
                CancellationToken.None);
            await AssertOnlyTargetRestoredAsync(codeRemap);
            Assert.Equal(
                new RollupGroup(2, firstV1.UpdatedAtUtc, lastV1.UpdatedAtUtc),
                await ReadRollupGroupAsync(factory, strategyId, oldUtc, reason));

            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, live.Id));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, shadowMarket.Id));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, shadowCondition.Id));
            foreach (var run in v2Runs.Except([live, shadowMarket, shadowCondition]))
            {
                Assert.Equal(StrategyRunRetentionScopes.PaperOnly,
                    await ReadRetentionScopeAsync(factory, run.Id));
            }

            var remainingVersions = await ReadArchiveStorageVersionsAsync(factory, strategyId);
            Assert.Equal(2, remainingVersions.Count);
            Assert.Equal(1, remainingVersions[firstV1.Id]);
            Assert.Equal(1, remainingVersions[lastV1.Id]);
            Assert.Equal(
                new RollupGroup(2, firstV1.UpdatedAtUtc, lastV1.UpdatedAtUtc),
                await ReadRollupGroupAsync(factory, strategyId, oldUtc, reason));
            var restoredCounts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((11L, 2L, 2L, 1L),
                (restoredCounts.RawRuns,
                    restoredCounts.RollupRuns,
                    restoredCounts.Tombstones,
                    restoredCounts.ReconciliationQueueRows));
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, prefix);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_BulkInsertReturnsLogicalIdsAndRetryIsIdempotent()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"direct_skip_{Guid.NewGuid():N}";
        var nowUtc = DateTimeOffset.UtcNow;
        var skipped = CreateSkippedRun(strategyId, nowUtc) with
        {
            SkipDiagnosticsJson = "{\"discarded\":true}"
        };
        var observed = CreateSkippedRun(strategyId, nowUtc.AddSeconds(1)) with
        {
            Status = StrategyMarketPaperRunStatuses.Observed,
            SkipReason = null
        };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            var inserted = await repository.TryAddStrategyMarketPaperRunsAsync(
                [skipped, observed],
                directPaperSkipCompactionEnabled: true);

            Assert.Equal(
                new[] { skipped.Id, observed.Id }.OrderBy(id => id),
                inserted.OrderBy(id => id));
            Assert.Equal(
                new RetentionCounts(1, 1, 1, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Equal(
                StrategyMarketPaperRunStatuses.Observed,
                await ReadRunStatusAsync(factory, observed.Id));
            Assert.Null(await ReadRunStatusAsync(factory, skipped.Id));

            var directProjectionEvent = await ReadStrategyRunProjectionEventAsync(
                factory,
                skipped.Id);
            Assert.Equal(DashboardProjectionOperations.Insert, directProjectionEvent.Operation);
            Assert.Null(directProjectionEvent.OldPayloadJson);
            var directPayload = PostgresDashboardProjectionRepository
                .Deserialize<StrategyRunProjectionPayload>(
                    Assert.IsType<string>(directProjectionEvent.NewPayloadJson));
            Assert.Equal(skipped.Id, directPayload.Id);
            Assert.Equal(StrategyIds.Normalize(skipped.StrategyId), directPayload.StrategyId);
            Assert.Equal(skipped.Status, directPayload.Status);
            Assert.Equal(skipped.StakeUsd, directPayload.StakeUsd);
            Assert.Equal(skipped.SkipReason, directPayload.SkipReason);
            Assert.Equal(
                skipped.UpdatedAtUtc.ToUnixTimeMilliseconds(),
                directPayload.UpdatedAtUtc.ToUnixTimeMilliseconds());
            Assert.Null(directPayload.LiveEnabledAtUtc);
            Assert.Equal(0m, directPayload.FeeUsd);
            Assert.Equal("LegacyUnknown", directPayload.FeeAccountingStatus);
            Assert.Null(directPayload.NetRealizedPnlUsd);

            var retry = await repository.TryAddStrategyMarketPaperRunsAsync(
                [skipped, observed],
                directPaperSkipCompactionEnabled: true);

            Assert.Empty(retry);
            Assert.Equal(
                new RetentionCounts(1, 1, 1, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_TwoThousandUniquePureSkipsCompleteBelowCommandTimeout()
    {
        const int runCount = 2_000;
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"direct_skip_2000_{Guid.NewGuid():N}";
        var nowUtc = DateTimeOffset.UtcNow;
        var skippedRuns = Enumerable.Range(0, runCount)
            .Select(index => CreateSkippedRun(strategyId, nowUtc.AddMilliseconds(index)))
            .ToArray();

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            var stopwatch = Stopwatch.StartNew();
            var inserted = await repository.TryAddStrategyMarketPaperRunsAsync(
                skippedRuns,
                directPaperSkipCompactionEnabled: true);
            stopwatch.Stop();

            Assert.Equal(
                skippedRuns.Select(run => run.Id).OrderBy(id => id),
                inserted.OrderBy(id => id));
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(30),
                $"The 2,000-run direct compaction took {stopwatch.Elapsed.TotalSeconds:F3} seconds.");
            Assert.Equal(
                new RetentionCounts(0, runCount, runCount, runCount, 1),
                await ReadRetentionCountsAsync(factory, strategyId));

            var retry = await repository.TryAddStrategyMarketPaperRunsAsync(
                skippedRuns,
                directPaperSkipCompactionEnabled: true);

            Assert.Empty(retry);
            Assert.Equal(
                new RetentionCounts(0, runCount, runCount, runCount, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_FinalizeObservedToPureSkippedCompactsAtomically()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"direct_finalize_{Guid.NewGuid():N}";
        var nowUtc = DateTimeOffset.UtcNow;
        var observed = CreateSkippedRun(strategyId, nowUtc) with
        {
            Status = StrategyMarketPaperRunStatuses.Observed,
            SkipReason = null
        };
        var skipped = observed with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = "direct_finalize_skip",
            SkipDiagnosticsJson = "{\"discarded\":true}",
            UpdatedAtUtc = nowUtc.AddSeconds(1)
        };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(observed));

            await repository.FinalizeStrategyMarketPaperRunAsync(
                skipped,
                directPaperSkipCompactionEnabled: true);

            Assert.Equal(
                new RetentionCounts(0, 1, 1, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Null(await ReadRunStatusAsync(factory, observed.Id));

            await repository.FinalizeStrategyMarketPaperRunAsync(
                skipped,
                directPaperSkipCompactionEnabled: true);
            Assert.Equal(
                new RetentionCounts(0, 1, 1, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_PaperDependencyAndLiveScopeRemainRaw()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var paperStrategyId = Guid.NewGuid();
        var liveStrategyId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow;
        var paperRun = CreateSkippedRun(paperStrategyId, nowUtc);
        var liveRun = CreateSkippedRun(liveStrategyId, nowUtc.AddSeconds(1));

        await InsertStrategyAsync(
            factory,
            paperStrategyId,
            $"direct_paper_guard_{Guid.NewGuid():N}",
            liveStakes: false);
        await InsertStrategyAsync(
            factory,
            liveStrategyId,
            $"direct_live_guard_{Guid.NewGuid():N}",
            liveStakes: true);
        try
        {
            await DeleteProjectionBlockersAsync(factory, paperStrategyId);
            await DeleteProjectionBlockersAsync(factory, liveStrategyId);
            await repository.AddPaperOrderAsync(CreatePaperOrder(
                paperStrategyId,
                paperRun.ConditionId,
                nowUtc));

            var inserted = await repository.TryAddStrategyMarketPaperRunsAsync(
                [paperRun, liveRun],
                directPaperSkipCompactionEnabled: true);

            Assert.Equal(2, inserted.Count);
            Assert.Equal(
                new RetentionCounts(1, 0, 0, 2, 0),
                await ReadRetentionCountsAsync(factory, paperStrategyId));
            Assert.Equal(
                new RetentionCounts(1, 0, 0, 1, 0),
                await ReadRetentionCountsAsync(factory, liveStrategyId));
            Assert.Equal(
                StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, paperRun.Id));
            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, liveRun.Id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, paperStrategyId);
            await DeleteTestStrategyAsync(factory, liveStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_NonNeutralFeeShapeRemainsRaw()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow;
        var expectations = new[]
        {
            (Run: CreateSkippedRun(strategyId, nowUtc) with { FeeUsd = 0.01m }, Blocker: "fee_usd"),
            (Run: CreateSkippedRun(strategyId, nowUtc) with { FeeAccountingStatus = "Calculated" }, Blocker: "fee_accounting_status"),
            (Run: CreateSkippedRun(strategyId, nowUtc) with { FeeLiquidityRole = "Taker" }, Blocker: "fee_liquidity_role"),
            (Run: CreateSkippedRun(strategyId, nowUtc) with { FeeCalculationSource = "retention_test" }, Blocker: "fee_calculation_source"),
            (Run: CreateSkippedRun(strategyId, nowUtc) with { FeeRate = 0.01m }, Blocker: "fee_rate"),
            (Run: CreateSkippedRun(strategyId, nowUtc) with { FeeExponent = 2 }, Blocker: "fee_exponent"),
            (Run: CreateSkippedRun(strategyId, nowUtc) with { FeeTakerOnly = true }, Blocker: "fee_taker_only"),
            (Run: CreateSkippedRun(strategyId, nowUtc) with { FeeCalculatedAtUtc = nowUtc }, Blocker: "fee_calculated_at_utc"),
            (Run: CreateSkippedRun(strategyId, nowUtc) with { NetRealizedPnlUsd = -0.01m }, Blocker: "net_realized_pnl_usd")
        };
        var runs = expectations.Select(expectation => expectation.Run).ToArray();

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"direct_fee_guard_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            var inserted = await repository.TryAddStrategyMarketPaperRunsAsync(
                runs,
                directPaperSkipCompactionEnabled: true);

            Assert.Equal(
                runs.Select(run => run.Id).OrderBy(id => id),
                inserted.OrderBy(id => id));
            Assert.Equal(
                new RetentionCounts(9, 0, 0, 9, 0),
                await ReadRetentionCountsAsync(factory, strategyId));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            foreach (var expectation in expectations)
            {
                Assert.Equal(
                    StrategyMarketPaperRunStatuses.Skipped,
                    await ReadRunStatusAsync(factory, expectation.Run.Id));
                Assert.Equal(
                    [expectation.Blocker],
                    await ReadRunBlockersAsync(factory, expectation.Run.Id));
            }
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_AmbiguousInputFailsBeforeAnyPersistence()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);

        foreach (var sameStrategyMarketKey in new[] { true, false })
        {
            foreach (var observedFirst in new[] { true, false })
            {
                var strategyId = Guid.NewGuid();
                var nowUtc = DateTimeOffset.UtcNow;
                var skipped = CreateSkippedRun(strategyId, nowUtc);
                var observed = skipped with
                {
                    Id = sameStrategyMarketKey ? Guid.NewGuid() : skipped.Id,
                    MarketId = sameStrategyMarketKey
                        ? skipped.MarketId
                        : $"{skipped.MarketId}-observed",
                    Status = StrategyMarketPaperRunStatuses.Observed,
                    SkipReason = null
                };
                var batch = observedFirst
                    ? new[] { observed, skipped }
                    : new[] { skipped, observed };

                await InsertStrategyAsync(
                    factory,
                    strategyId,
                    $"direct_duplicate_{Guid.NewGuid():N}",
                    liveStakes: false);
                try
                {
                    await DeleteProjectionBlockersAsync(factory, strategyId);
                    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        repository.TryAddStrategyMarketPaperRunsAsync(
                            batch,
                            directPaperSkipCompactionEnabled: true));

                    Assert.Contains("unique input", exception.Message, StringComparison.Ordinal);
                    Assert.Equal(
                        new RetentionCounts(0, 0, 0, 0, 0),
                        await ReadRetentionCountsAsync(factory, strategyId));
                }
                finally
                {
                    await DeleteTestStrategyAsync(factory, strategyId);
                }
            }
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_EquivalentQueueRequestDoesNotRewriteRow()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow;
        var firstRun = CreateSkippedRun(strategyId, nowUtc);
        var secondRun = CreateSkippedRun(strategyId, nowUtc.AddSeconds(1));

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"direct_queue_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(
                [firstRun.Id],
                await repository.TryAddStrategyMarketPaperRunsAsync(
                    [firstRun],
                    directPaperSkipCompactionEnabled: true));
            var firstVersion = await ReadReconciliationQueueVersionAsync(factory, strategyId);

            Assert.Equal(
                [secondRun.Id],
                await repository.TryAddStrategyMarketPaperRunsAsync(
                    [secondRun],
                    directPaperSkipCompactionEnabled: true));
            var secondVersion = await ReadReconciliationQueueVersionAsync(factory, strategyId);

            Assert.Equal(firstVersion, secondVersion);
            Assert.Equal(
                new RetentionCounts(0, 2, 2, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_QueueRequestReactivatesFailureAndPreservesHigherPriority()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow;
        var firstRun = CreateSkippedRun(strategyId, nowUtc);
        var secondRun = CreateSkippedRun(strategyId, nowUtc.AddSeconds(1));

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"direct_queue_retry_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(
                [firstRun.Id],
                await repository.TryAddStrategyMarketPaperRunsAsync(
                    [firstRun],
                    directPaperSkipCompactionEnabled: true));
            await ExecuteForStrategyAsync(
                factory,
                """
UPDATE public.dashboard_projection_reconciliation_queue
SET priority = 75,
    reason = 'retention_test_failure',
    attempt_count = 3,
    next_attempt_at_utc = clock_timestamp() + interval '1 hour',
    last_error = 'retention_test_failure'
WHERE strategy_id = @StrategyId;
""",
                strategyId);
            var failedVersion = await ReadReconciliationQueueVersionAsync(factory, strategyId);

            Assert.Equal(
                [secondRun.Id],
                await repository.TryAddStrategyMarketPaperRunsAsync(
                    [secondRun],
                    directPaperSkipCompactionEnabled: true));
            var reactivatedVersion = await ReadReconciliationQueueVersionAsync(factory, strategyId);

            Assert.NotEqual(failedVersion.TransactionId, reactivatedVersion.TransactionId);
            Assert.Equal(75, reactivatedVersion.Priority);
            Assert.Equal("direct_paper_skip_compaction", reactivatedVersion.Reason);
            Assert.Equal(3, reactivatedVersion.AttemptCount);
            Assert.Equal(failedVersion.RequestedAtUtc, reactivatedVersion.RequestedAtUtc);
            Assert.True(reactivatedVersion.NextAttemptAtUtc <= DateTimeOffset.UtcNow);
            Assert.Null(reactivatedVersion.LastError);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_EntryBatchCompactsPureSkipAndPreservesActualPaperRun()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"direct_batch_{Guid.NewGuid():N}";
        var nowUtc = DateTimeOffset.UtcNow;
        var skipped = CreateSkippedRun(strategyId, nowUtc);
        var enteredBase = CreateSkippedRun(strategyId, nowUtc.AddSeconds(1));
        var order = CreatePaperOrder(strategyId, enteredBase.ConditionId, nowUtc);
        var leaderTrade = new LeaderTrade(
            $"strategy:{strategyCode}",
            strategyCode,
            order.ConditionId,
            order.AssetId,
            enteredBase.MarketSlug,
            enteredBase.MarketTitle,
            order.Outcome,
            order.Side,
            order.Price,
            order.SizeShares,
            order.NotionalUsd,
            nowUtc);
        var signal = new Signal(
            order.SignalId,
            leaderTrade,
            100,
            true,
            "direct_batch_entry",
            [],
            order.Price,
            order.SizeShares,
            order.NotionalUsd,
            nowUtc);
        var entered = enteredBase with
        {
            Status = StrategyMarketPaperRunStatuses.Entered,
            SelectedAssetId = order.AssetId,
            SelectedOutcome = order.Outcome,
            EntryPrice = order.Price,
            StakeUsd = order.NotionalUsd,
            SizeShares = order.SizeShares,
            SignalId = signal.Id,
            PaperOrderId = order.Id,
            EnteredAtUtc = nowUtc,
            SkipReason = null,
            UpdatedAtUtc = nowUtc
        };
        var batch = new PaperEntryPersistenceBatch(
            [signal],
            [order],
            [],
            [],
            [],
            [skipped, entered])
        {
            DirectPaperSkipCompactionEnabled = true
        };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await repository.AddPaperEntryPersistenceBatchAsync(batch, timeout.Token);

            Assert.Equal(
                new RetentionCounts(1, 1, 1, 3, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Equal(
                StrategyMarketPaperRunStatuses.Entered,
                await ReadRunStatusAsync(factory, entered.Id));
            Assert.Null(await ReadRunStatusAsync(factory, skipped.Id));
            Assert.Equal(1, await ReadPaperOrderCountAsync(factory, order.Id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_EntryBatchLocksWalletBeforeExclusiveRetentionGate()
    {
        var factory = await CreateFactoryAsync();
        var strategyId = Guid.NewGuid();
        var strategyCode = $"direct_lock_order_{Guid.NewGuid():N}";
        var wallet = $"strategy:{strategyCode}";
        var nowUtc = DateTimeOffset.UtcNow;
        var skipped = CreateSkippedRun(strategyId, nowUtc);
        var position = new PaperPosition(
            $"asset-{Guid.NewGuid():N}",
            skipped.ConditionId,
            "Yes",
            2m,
            0.50m,
            1m,
            0m,
            nowUtc,
            wallet);
        var batch = new PaperEntryPersistenceBatch(
            [],
            [],
            [],
            [position],
            [],
            [skipped])
        {
            DirectPaperSkipCompactionEnabled = true
        };
        var directApplicationName = $"direct_lock_order_{Guid.NewGuid():N}";
        var directFactory = WithApplicationName(factory, directApplicationName);
        using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task? directTask = null;

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            await using (var blockerConnection = factory.CreateConnection())
            {
                await blockerConnection.OpenAsync();
                await using (var blockerTransaction = await blockerConnection.BeginTransactionAsync())
                {
                    await using (var walletLockCommand = new NpgsqlCommand(
                                     "SELECT pg_advisory_xact_lock(" +
                                     "hashtextextended(@Wallet, 4937427318840178337));",
                                     blockerConnection,
                                     blockerTransaction))
                    {
                        walletLockCommand.Parameters.AddWithValue("Wallet", wallet);
                        await walletLockCommand.ExecuteNonQueryAsync();
                    }

                    directTask = new PostgresAppRepository(directFactory)
                        .AddPaperEntryPersistenceBatchAsync(batch, raceCancellation.Token);
                    var directPid = await WaitForBlockedApplicationAsync(
                        factory,
                        directApplicationName,
                        "advisory");
                    await AssertBlockedByAsync(factory, directPid, blockerConnection.ProcessID);
                    Assert.False(await HoldsExclusiveRetentionGateAsync(factory, directPid));

                    await using var sharedGateCommand = new NpgsqlCommand(
                        "SELECT public.lock_strategy_run_retention_dependency();",
                        blockerConnection,
                        blockerTransaction)
                    {
                        CommandTimeout = 5
                    };
                    await sharedGateCommand.ExecuteNonQueryAsync();
                    await blockerTransaction.CommitAsync();
                }
            }

            await directTask.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(
                StrategyMarketPaperRunStatuses.Skipped,
                await ReadRunStatusAsync(factory, skipped.Id));
        }
        finally
        {
            await DrainRaceTaskAsync(directTask);
            await DeletePaperPositionAsync(factory, position.CopiedTraderWallet, position.AssetId);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Transfer_CompactsOnlyPreviewedPaperSkipAndPreservesLifetimeTotals()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var run = CreateSkippedRun(strategyId, DateTimeOffset.UtcNow.AddDays(-4));
        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            Assert.Equal(StrategyRunRetentionScopes.PaperOnly, await ReadRetentionScopeAsync(factory, run.Id));
            await projection.BootstrapAsync();

            var before = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            var dashboardBefore = await ReadDashboardSnapshotAsync(snapshots, strategyId);
            var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10);
            Assert.Contains(run.Id, preview.CandidateRunIds);

            var result = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                cutoffUtc);

            Assert.Equal(1, result.SelectedRows);
            Assert.Equal(1, result.DeletedRows);
            Assert.Equal(1, result.RollupRowsChanged);
            Assert.Equal(1, result.TombstonesChanged);
            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(0, counts.RawRuns);
            Assert.Equal(1, counts.RollupRuns);
            Assert.Equal(1, counts.Tombstones);
            Assert.Equal(0, counts.ProjectionEvents);

            var reconciliation = await projection.ReconcileNextStrategyAsync();
            Assert.True(reconciliation.Reconciled, reconciliation.Error);
            Assert.Equal(strategyId, reconciliation.StrategyId);

            var after = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            var dashboardAfter = await ReadDashboardSnapshotAsync(snapshots, strategyId);
            Assert.Equal(before.SkippedRunsCount, after.SkippedRunsCount);
            Assert.Equal(before.PaperConditionSkippedRunsCount, after.PaperConditionSkippedRunsCount);
            Assert.Equal(before.LastRunUtc, after.LastRunUtc);
            Assert.Equal(dashboardBefore, dashboardAfter);

            Assert.False(await repository.TryAddStrategyMarketPaperRunAsync(run with { Id = Guid.NewGuid() }));
            var bulkInserted = await repository.TryAddStrategyMarketPaperRunsAsync(
                [run with { Id = Guid.NewGuid() }]);
            Assert.Empty(bulkInserted);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Bootstrap_FreshV1TombstoneFeedsRecentWhileRollupAloneFeedsLifetime()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var capturedNowUtc = DateTimeOffset.UtcNow;
        var secondAlignedNowUtc = capturedNowUtc.AddTicks(
            -(capturedNowUtc.Ticks % TimeSpan.TicksPerSecond));
        var recentUpdatedAtUtc = secondAlignedNowUtc.AddMinutes(-30);
        var expiredUpdatedAtUtc = secondAlignedNowUtc.AddHours(-25);
        var recentRunId = Guid.NewGuid();
        var expiredRunId = Guid.NewGuid();

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await InsertArchivedPaperSkipAsync(
                factory,
                strategyId,
                recentRunId,
                recentUpdatedAtUtc,
                "recent_compact_skip");
            await InsertArchivedPaperSkipAsync(
                factory,
                strategyId,
                expiredRunId,
                expiredUpdatedAtUtc,
                "expired_compact_skip");

            await projection.BootstrapAsync();

            var lifetime = (await snapshots.GetStrategyPerformanceSnapshotAsync())
                .Single(row => row.StrategyId == strategyId);
            Assert.Equal(0, lifetime.ObservedRunsCount);
            Assert.Equal(2, lifetime.SkippedRunsCount);
            Assert.Equal(2, lifetime.PaperConditionSkippedRunsCount);
            Assert.Equal(0, lifetime.PaperNotAcceptedRunsCount);
            Assert.Equal(recentUpdatedAtUtc, lifetime.LastRunUtc);

            var recentRows = (await snapshots.GetStrategyRecentPerformanceSnapshotAsync())
                .Where(row => row.StrategyId == strategyId)
                .ToDictionary(row => row.WindowHours);
            Assert.Equal([1, 6, 24], recentRows.Keys.OrderBy(value => value).ToArray());
            foreach (var windowHours in new[] { 1, 6, 24 })
            {
                var row = recentRows[windowHours];
                Assert.Equal(1, row.SkippedRunsCount);
                Assert.Equal(1, row.PaperConditionSkippedRunsCount);
                Assert.Equal(0, row.PaperNotAcceptedRunsCount);
                Assert.Equal("recent_compact_skip:1", row.TopSkipReason);
                Assert.Equal(recentUpdatedAtUtc, row.LastRunUtc);
            }

            var directRecentRows = (await repository.GetStrategyRecentPerformanceAsync())
                .Where(row => row.StrategyId == strategyId)
                .ToDictionary(row => row.WindowHours);
            foreach (var windowHours in new[] { 1, 6, 24 })
            {
                var row = directRecentRows[windowHours];
                Assert.Equal(1, row.SkippedRunsCount);
                Assert.Equal(1, row.PaperConditionSkippedRunsCount);
                Assert.Equal(0, row.PaperNotAcceptedRunsCount);
                Assert.Equal(0, row.LiveSkippedOrdersCount);
                Assert.Equal("recent_compact_skip:1", row.TopSkipReason);
                Assert.Equal(recentUpdatedAtUtc, row.LastRunUtc);
            }

            Assert.Equal(2, await ReadRecentStrategyRunFactCountAsync(factory, recentRunId));
            Assert.Equal(0, await ReadRecentStrategyRunFactCountAsync(factory, expiredRunId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Transfer_WhenPaperOrderMakesAllowlistStale_RollsBackEntireBatch()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var firstRun = CreateSkippedRun(strategyId, oldUtc);
        var secondRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(1));
        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(firstRun));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(secondRun));
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10);
            Assert.Contains(firstRun.Id, preview.CandidateRunIds);
            Assert.Contains(secondRun.Id, preview.CandidateRunIds);
            await repository.AddPaperOrderAsync(CreatePaperOrder(
                strategyId,
                secondRun.ConditionId,
                oldUtc.AddMinutes(2)));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    [firstRun.Id, secondRun.Id],
                    cutoffUtc));

            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(2, counts.RawRuns);
            Assert.Equal(0, counts.RollupRuns);
            Assert.Equal(0, counts.Tombstones);
            Assert.Equal(0, counts.ReconciliationQueueRows);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveGuard_MakesCurrentAndFutureRunsPermanentlyIneligible()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var liveRun = CreateSkippedRun(strategyId, oldUtc);
        var laterPaperRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(1));
        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: true);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(liveRun));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow, await ReadRetentionScopeAsync(factory, liveRun.Id));

            await SetStrategyLiveStakesAsync(factory, strategyId, liveStakes: false);
            Assert.Equal(
                [laterPaperRun.Id],
                await repository.TryAddStrategyMarketPaperRunsAsync(
                    [laterPaperRun],
                    directPaperSkipCompactionEnabled: true));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow, await ReadRetentionScopeAsync(factory, laterPaperRun.Id));

            await TryDemoteRetentionScopeAsync(factory, liveRun.Id);
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow, await ReadRetentionScopeAsync(factory, liveRun.Id));
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(
                DateTimeOffset.UtcNow.AddHours(-48),
                10);
            Assert.DoesNotContain(liveRun.Id, preview.CandidateRunIds);
            Assert.DoesNotContain(laterPaperRun.Id, preview.CandidateRunIds);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveSkipProjectionBoundary_CompactsOnlyPreLiveRunAndKeepsLiveSkipRowsRaw()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var baseline = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 0);
        var preLiveRun = CreateSkippedRun(strategyId, oldUtc);
        var boundaryRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(1));
        var postLiveRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(2));
        var allRuns = new[] { preLiveRun, boundaryRun, postLiveRun };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.Equal(
                allRuns.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(allRuns)).Count);
            Assert.True(await repository.SetStrategyLiveStakesAsync(
                strategyId,
                liveStakes: true,
                updatedAtUtc: boundaryRun.UpdatedAtUtc));
            foreach (var run in allRuns)
            {
                Assert.Equal(
                    StrategyRunRetentionScopes.PaperOnly,
                    await ReadRetentionScopeAsync(factory, run.Id));
            }

            await DeleteProjectionBlockersAsync(factory, strategyId);

            var blockers = await ReadLegacyBlockersAsync(
                factory,
                allRuns.Select(run => run.Id).ToArray());
            Assert.Empty(blockers[preLiveRun.Id]);
            Assert.Equal(["live_skip_projection_dependency"], blockers[boundaryRun.Id]);
            Assert.Equal(["live_skip_projection_dependency"], blockers[postLiveRun.Id]);

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            var fixtureIds = allRuns.Select(run => run.Id).ToHashSet();
            Assert.Equal(
                [preLiveRun.Id],
                preview.CandidateRunIds.Where(fixtureIds.Contains).ToArray());
            var summary = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 0);
            Assert.Equal(baseline.TotalCandidateRows + 1, summary.TotalCandidateRows);

            var performanceBefore = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            Assert.Equal(3, performanceBefore.SkippedRunsCount);
            Assert.Equal(3, performanceBefore.PaperConditionSkippedRunsCount);
            Assert.Equal(2, performanceBefore.LiveSkippedOrdersCount);
            Assert.Equal(0, performanceBefore.LiveConditionSkippedOrdersCount);
            Assert.Equal(2, performanceBefore.LiveTechnicalSkippedOrdersCount);
            Assert.Equal(0, performanceBefore.LiveIgnoredOrdersCount);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    [preLiveRun.Id, boundaryRun.Id],
                    cutoffUtc));
            Assert.Equal(
                new RetentionCounts(3, 0, 0, 0, 0),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Equal(
                allRuns.Select(run => run.Id).OrderBy(id => id),
                (await ReadRunIdsAsync(factory, allRuns.Select(run => run.Id).ToArray())).OrderBy(id => id));

            var transfer = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [preLiveRun.Id],
                cutoffUtc);
            Assert.Equal(1, transfer.SelectedRows);
            Assert.Equal(1, transfer.DeletedRows);
            Assert.Equal(1, transfer.RollupRowsChanged);
            Assert.Equal(1, transfer.TombstonesChanged);
            Assert.Equal(
                new RetentionCounts(2, 1, 1, 0, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Equal(
                new[] { boundaryRun.Id, postLiveRun.Id }.OrderBy(id => id),
                (await ReadRunIdsAsync(factory, allRuns.Select(run => run.Id).ToArray())).OrderBy(id => id));

            var performanceAfter = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            Assert.Equal(performanceBefore.SkippedRunsCount, performanceAfter.SkippedRunsCount);
            Assert.Equal(
                performanceBefore.PaperConditionSkippedRunsCount,
                performanceAfter.PaperConditionSkippedRunsCount);
            Assert.Equal(performanceBefore.LiveSkippedOrdersCount, performanceAfter.LiveSkippedOrdersCount);
            Assert.Equal(
                performanceBefore.LiveConditionSkippedOrdersCount,
                performanceAfter.LiveConditionSkippedOrdersCount);
            Assert.Equal(
                performanceBefore.LiveTechnicalSkippedOrdersCount,
                performanceAfter.LiveTechnicalSkippedOrdersCount);
            Assert.Equal(
                performanceBefore.LiveIgnoredOrdersCount,
                performanceAfter.LiveIgnoredOrdersCount);
            Assert.Equal(performanceBefore.LastRunUtc, performanceAfter.LastRunUtc);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task SetBasedEligibility_MatchesLegacyBlockersAndPreservesPaperAndLiveHistory()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var paperStrategyId = Guid.NewGuid();
        var liveStrategyId = Guid.NewGuid();
        var paperStrategyCode = $"retention_{Guid.NewGuid():N}";
        var liveStrategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-eligibility-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var baseline = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 0);
        var controlRun = WithRetentionKey(
            CreateSkippedRun(paperStrategyId, oldUtc),
            $"{prefix}-control");
        var dependencyOrder = CreatePaperOrder(
            paperStrategyId,
            $"{prefix}-paper-dependency",
            oldUtc);
        var paperDependencyRun = WithRetentionKey(
            CreateSkippedRun(paperStrategyId, oldUtc.AddMinutes(1)),
            dependencyOrder.ConditionId);
        var enteredOrder = CreatePaperOrder(
            paperStrategyId,
            $"{prefix}-entered",
            oldUtc.AddMinutes(2));
        var enteredRun = WithRetentionKey(
            CreateSkippedRun(paperStrategyId, oldUtc.AddMinutes(2)) with
            {
                Status = StrategyMarketPaperRunStatuses.Entered,
                SelectedAssetId = enteredOrder.AssetId,
                SelectedOutcome = enteredOrder.Outcome,
                EntryPrice = enteredOrder.Price,
                SizeShares = enteredOrder.SizeShares,
                PaperOrderId = enteredOrder.Id,
                EnteredAtUtc = oldUtc.AddMinutes(2),
                SkipReason = null
            },
            enteredOrder.ConditionId);
        var settledOrder = CreatePaperOrder(
            paperStrategyId,
            $"{prefix}-settled",
            oldUtc.AddMinutes(3));
        var settledRun = WithRetentionKey(
            CreateSkippedRun(paperStrategyId, oldUtc.AddMinutes(3)) with
            {
                Status = StrategyMarketPaperRunStatuses.Settled,
                SelectedAssetId = settledOrder.AssetId,
                SelectedOutcome = settledOrder.Outcome,
                EntryPrice = settledOrder.Price,
                SizeShares = settledOrder.SizeShares,
                PaperOrderId = settledOrder.Id,
                EnteredAtUtc = oldUtc.AddMinutes(3),
                SettlementPrice = 1m,
                SettlementValueUsd = settledOrder.SizeShares,
                RealizedPnlUsd = settledOrder.SizeShares - settledOrder.NotionalUsd,
                SettledAtUtc = oldUtc.AddHours(1),
                SkipReason = null
            },
            settledOrder.ConditionId);
        var diagnosticRun = WithRetentionKey(
            CreateSkippedRun(paperStrategyId, oldUtc.AddMinutes(4)) with
            {
                SkipDiagnosticsJson = "{}"
            },
            $"{prefix}-diagnostic");
        var liveRun = WithRetentionKey(
            CreateSkippedRun(liveStrategyId, oldUtc.AddMinutes(5)),
            $"{prefix}-live");
        var allRuns = new[]
        {
            controlRun,
            paperDependencyRun,
            enteredRun,
            settledRun,
            diagnosticRun,
            liveRun
        };

        try
        {
            await InsertStrategyAsync(factory, paperStrategyId, paperStrategyCode, liveStakes: false);
            await InsertStrategyAsync(factory, liveStrategyId, liveStrategyCode, liveStakes: true);
            await repository.AddPaperOrderAsync(dependencyOrder);
            await repository.AddPaperOrderAsync(enteredOrder);
            await repository.AddPaperOrderAsync(settledOrder);
            Assert.Equal(
                allRuns.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(allRuns)).Count);
            await AddSkipDiagnosticsAsync(factory, diagnosticRun.Id);

            await repository.AddPaperFillAsync(new PaperFill(
                Guid.NewGuid(),
                enteredOrder.Id,
                enteredOrder.Price,
                enteredOrder.SizeShares,
                oldUtc.AddMinutes(3),
                "retention integration test"));
            await repository.UpsertPaperPositionAsync(new PaperPosition(
                enteredOrder.AssetId,
                enteredOrder.ConditionId,
                enteredOrder.Outcome,
                enteredOrder.SizeShares,
                enteredOrder.Price,
                enteredOrder.NotionalUsd,
                0m,
                oldUtc.AddMinutes(3),
                $"strategy:{paperStrategyCode}"));
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(new PaperPositionSettlement(
                Guid.NewGuid(),
                $"strategy:{paperStrategyCode}",
                settledOrder.AssetId,
                settledOrder.ConditionId,
                settledOrder.Outcome,
                settledOrder.AssetId,
                settledOrder.Outcome,
                "IntegrationTest",
                settledOrder.SizeShares,
                settledOrder.Price,
                settledOrder.NotionalUsd,
                settledOrder.SizeShares,
                settledOrder.SizeShares - settledOrder.NotionalUsd,
                true,
                "IntegrationTest",
                oldUtc.AddHours(1),
                oldUtc.AddHours(1))));

            await DeleteProjectionBlockersAsync(factory, paperStrategyId);
            await DeleteProjectionBlockersAsync(factory, liveStrategyId);

            foreach (var run in allRuns.Where(run => run.StrategyId == paperStrategyId))
            {
                Assert.Equal(
                    StrategyRunRetentionScopes.PaperOnly,
                    await ReadRetentionScopeAsync(factory, run.Id));
            }
            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, liveRun.Id));

            var legacyEligibleIds = await ReadLegacyEligibleRunIdsAsync(
                factory,
                cutoffUtc,
                allRuns.Select(run => run.Id).ToArray());
            Assert.Equal([controlRun.Id], legacyEligibleIds);

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            var fixturePreviewIds = preview.CandidateRunIds
                .Where(id => allRuns.Any(run => run.Id == id))
                .ToArray();
            Assert.Equal(legacyEligibleIds, fixturePreviewIds);

            var summary = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 25_000);
            Assert.Equal(baseline.TotalCandidateRows + 1, summary.TotalCandidateRows);
            Assert.Contains(controlRun.Id, summary.SampleRunIds);

            var historyCountsBefore = await ReadPaperHistoryCountsAsync(factory, paperStrategyId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    [controlRun.Id, enteredRun.Id, settledRun.Id, liveRun.Id],
                    cutoffUtc));
            Assert.Equal(
                allRuns.Select(run => run.Id).OrderBy(id => id),
                (await ReadRunIdsAsync(factory, allRuns.Select(run => run.Id).ToArray())).OrderBy(id => id));
            Assert.Equal(historyCountsBefore, await ReadPaperHistoryCountsAsync(factory, paperStrategyId));
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, prefix);
            await DeleteTestStrategyAsync(factory, paperStrategyId);
            await DeleteTestStrategyAsync(factory, liveStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task EveryExternalBlocker_MatchesLegacyEligibilityAndKeepsRunsRaw()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var queueStrategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var queueStrategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-blockers-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var runIndex = 0;

        StrategyMarketPaperRun CreateFixtureRun(Guid targetStrategyId, string blockerName)
        {
            var run = CreateSkippedRun(targetStrategyId, oldUtc.AddMinutes(runIndex++));
            return WithRetentionKey(run, $"{prefix}-{blockerName}");
        }

        var controlRun = CreateFixtureRun(strategyId, "control");
        var paperOrderRun = CreateFixtureRun(strategyId, "paper-order");
        var dryRun = CreateFixtureRun(strategyId, "dry-run");
        var liveOrderRun = CreateFixtureRun(strategyId, "live-order");
        var shadowMarketRun = CreateFixtureRun(strategyId, "shadow-market");
        var shadowConditionRun = CreateFixtureRun(strategyId, "shadow-condition");
        var paperPositionRun = CreateFixtureRun(strategyId, "paper-position");
        var paperSettlementRun = CreateFixtureRun(strategyId, "paper-settlement");
        var copiedLeaderPositionRun = CreateFixtureRun(strategyId, "copied-position");
        var copiedLeaderActivityRun = CreateFixtureRun(strategyId, "copied-activity");
        var onchainRun = CreateFixtureRun(strategyId, "onchain");
        var tombstoneRun = CreateFixtureRun(strategyId, "tombstone");
        var projectionEventRun = CreateFixtureRun(strategyId, "projection-event");
        var recentFactRun = CreateFixtureRun(strategyId, "recent-fact");
        var reconciliationRun = CreateFixtureRun(queueStrategyId, "reconciliation");
        var allRuns = new[]
        {
            controlRun,
            paperOrderRun,
            dryRun,
            liveOrderRun,
            shadowMarketRun,
            shadowConditionRun,
            paperPositionRun,
            paperSettlementRun,
            copiedLeaderPositionRun,
            copiedLeaderActivityRun,
            onchainRun,
            tombstoneRun,
            projectionEventRun,
            recentFactRun,
            reconciliationRun
        };
        var expectedBlockers = new Dictionary<Guid, string>
        {
            [paperOrderRun.Id] = "paper_order_dependency",
            [dryRun.Id] = "dry_run_dependency",
            [liveOrderRun.Id] = "live_order_dependency",
            [shadowMarketRun.Id] = "live_shadow_dependency",
            [shadowConditionRun.Id] = "live_shadow_dependency",
            [paperPositionRun.Id] = "paper_position_dependency",
            [paperSettlementRun.Id] = "paper_settlement_dependency",
            [copiedLeaderPositionRun.Id] = "copied_leader_position_dependency",
            [copiedLeaderActivityRun.Id] = "copied_leader_activity_dependency",
            [onchainRun.Id] = "onchain_paper_dependency",
            [tombstoneRun.Id] = "existing_tombstone",
            [projectionEventRun.Id] = "pending_projection_event",
            [recentFactRun.Id] = "recent_projection_fact",
            [reconciliationRun.Id] = "pending_projection_reconciliation"
        };

        try
        {
            await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
            await InsertStrategyAsync(factory, queueStrategyId, queueStrategyCode, liveStakes: false);

            await repository.AddPaperOrderAsync(CreatePaperOrder(
                strategyId,
                paperOrderRun.ConditionId,
                oldUtc));
            await repository.AddLiveOrderAsync(CreateLiveOrder(
                strategyId,
                liveOrderRun.ConditionId,
                oldUtc));
            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                strategyId,
                shadowMarketRun.MarketId,
                $"{prefix}-unrelated-shadow-condition",
                oldUtc));
            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                strategyId,
                $"{prefix}-unrelated-shadow-market",
                shadowConditionRun.ConditionId,
                oldUtc));
            await repository.UpsertPaperPositionAsync(new PaperPosition(
                $"asset-{Guid.NewGuid():N}",
                paperPositionRun.ConditionId,
                "Yes",
                2m,
                0.50m,
                1m,
                0m,
                oldUtc,
                $"strategy:{strategyCode}"));
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(new PaperPositionSettlement(
                Guid.NewGuid(),
                $"strategy:{strategyCode}",
                $"asset-{Guid.NewGuid():N}",
                paperSettlementRun.ConditionId,
                "Yes",
                null,
                "No",
                "IntegrationTest",
                2m,
                0.50m,
                1m,
                0m,
                -1m,
                false,
                "IntegrationTest",
                oldUtc,
                oldUtc)));
            await InsertDirectExternalDependenciesAsync(
                factory,
                strategyId,
                oldUtc,
                dryRun,
                copiedLeaderPositionRun,
                copiedLeaderActivityRun,
                onchainRun);

            Assert.Equal(
                allRuns.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(allRuns)).Count);
            await DeleteProjectionBlockersAsync(factory, strategyId);
            await DeleteProjectionBlockersAsync(factory, queueStrategyId);
            await InsertPostRunExternalBlockersAsync(
                factory,
                strategyId,
                queueStrategyId,
                oldUtc,
                tombstoneRun,
                projectionEventRun,
                recentFactRun);

            foreach (var run in allRuns)
            {
                Assert.Equal(
                    StrategyRunRetentionScopes.PaperOnly,
                    await ReadRetentionScopeAsync(factory, run.Id));
            }

            var legacyBlockers = await ReadLegacyBlockersAsync(
                factory,
                allRuns.Select(run => run.Id).ToArray());
            Assert.Empty(legacyBlockers[controlRun.Id]);
            foreach (var (runId, expectedBlocker) in expectedBlockers)
            {
                Assert.Equal([expectedBlocker], legacyBlockers[runId]);
            }

            var legacyEligibleIds = await ReadLegacyEligibleRunIdsAsync(
                factory,
                cutoffUtc,
                allRuns.Select(run => run.Id).ToArray());
            Assert.Equal([controlRun.Id], legacyEligibleIds);

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            var fixtureIds = allRuns.Select(run => run.Id).ToHashSet();
            Assert.Equal(
                legacyEligibleIds,
                preview.CandidateRunIds.Where(fixtureIds.Contains).ToArray());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    allRuns.Select(run => run.Id).ToArray(),
                    cutoffUtc));
            Assert.Equal(
                allRuns.Select(run => run.Id).OrderBy(id => id),
                (await ReadRunIdsAsync(factory, allRuns.Select(run => run.Id).ToArray())).OrderBy(id => id));
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, prefix);
            await DeleteTestStrategyAsync(factory, strategyId);
            await DeleteTestStrategyAsync(factory, queueStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Eligibility_UsesPublicDependenciesWhenSearchPathIsShadowed()
    {
        var factory = await CreateFactoryAsync();
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var shadowSchema = $"retention_shadow_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-search-path-{Guid.NewGuid():N}");

        try
        {
            await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
            var repository = new PostgresAppRepository(factory);
            await repository.AddPaperOrderAsync(CreatePaperOrder(
                strategyId,
                run.ConditionId,
                oldUtc));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            await CreateShadowPaperOrdersSchemaAsync(factory, shadowSchema);

            var connectionStringBuilder = new NpgsqlConnectionStringBuilder(factory.ConnectionString)
            {
                SearchPath = $"{shadowSchema},public"
            };
            var shadowFactory = new PostgresConnectionFactory(new StorageOptions
            {
                ConnectionString = connectionStringBuilder.ConnectionString
            });
            await using (var connection = shadowFactory.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("SHOW search_path;", connection);
                Assert.Equal($"{shadowSchema},public", await command.ExecuteScalarAsync());
            }

            var preview = await new PostgresAppRepository(shadowFactory)
                .PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            Assert.DoesNotContain(run.Id, preview.CandidateRunIds);
            Assert.Equal(
                [run.Id],
                await ReadRunIdsAsync(factory, [run.Id]));
        }
        finally
        {
            await DropShadowSchemaAsync(factory, shadowSchema);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveOrderAndShadowDecision_PromoteRunsAndKeepThemRaw()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var liveOrderStrategyId = Guid.NewGuid();
        var shadowStrategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var prefix = $"retention-promotion-{Guid.NewGuid():N}";
        var liveOrderRun = WithRetentionKey(
            CreateSkippedRun(liveOrderStrategyId, oldUtc),
            $"{prefix}-live-order");
        var shadowRun = WithRetentionKey(
            CreateSkippedRun(shadowStrategyId, oldUtc.AddMinutes(1)),
            $"{prefix}-shadow");

        try
        {
            await InsertStrategyAsync(
                factory,
                liveOrderStrategyId,
                $"retention_{Guid.NewGuid():N}",
                liveStakes: false);
            await InsertStrategyAsync(
                factory,
                shadowStrategyId,
                $"retention_{Guid.NewGuid():N}",
                liveStakes: false);
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(liveOrderRun));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(shadowRun));
            await DeleteProjectionBlockersAsync(factory, liveOrderStrategyId);
            await DeleteProjectionBlockersAsync(factory, shadowStrategyId);

            var before = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            Assert.Contains(liveOrderRun.Id, before.CandidateRunIds);
            Assert.Contains(shadowRun.Id, before.CandidateRunIds);

            await repository.AddLiveOrderAsync(CreateLiveOrder(
                liveOrderStrategyId,
                liveOrderRun.ConditionId,
                oldUtc.AddMinutes(2)));
            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                shadowStrategyId,
                shadowRun.MarketId,
                shadowRun.ConditionId,
                oldUtc.AddMinutes(2)));
            await DeleteProjectionBlockersAsync(factory, liveOrderStrategyId);
            await DeleteProjectionBlockersAsync(factory, shadowStrategyId);

            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, liveOrderRun.Id));
            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, shadowRun.Id));

            var after = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            Assert.DoesNotContain(liveOrderRun.Id, after.CandidateRunIds);
            Assert.DoesNotContain(shadowRun.Id, after.CandidateRunIds);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    [liveOrderRun.Id, shadowRun.Id],
                    cutoffUtc));
            Assert.Equal(
                new[] { liveOrderRun.Id, shadowRun.Id }.OrderBy(id => id),
                (await ReadRunIdsAsync(factory, [liveOrderRun.Id, shadowRun.Id])).OrderBy(id => id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, liveOrderStrategyId);
            await DeleteTestStrategyAsync(factory, shadowStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ArchivedRun_PaperOrderRestoresExactRawRunAndReversesRollup()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-paper-restore-{Guid.NewGuid():N}");

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await projection.BootstrapAsync();
            var originalPayload = await ReadRunPayloadWithoutScopeAsync(factory, run.Id);
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var transfer = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                cutoffUtc);
            Assert.Equal(1, transfer.DeletedRows);
            Assert.Equal(new RetentionCounts(0, 1, 1, 0, 1),
                await ReadRetentionCountsAsync(factory, strategyId));

            await repository.AddPaperOrderAsync(CreatePaperOrder(
                strategyId,
                run.ConditionId,
                oldUtc.AddMinutes(1)));

            Assert.Equal(originalPayload, await ReadRunPayloadWithoutScopeAsync(factory, run.Id));
            Assert.Equal(StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, run.Id));
            var restored = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(1, restored.RawRuns);
            Assert.Equal(0, restored.RollupRuns);
            Assert.Equal(0, restored.Tombstones);
            Assert.Equal(1, restored.ReconciliationQueueRows);
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, run.Id));

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10);
            Assert.DoesNotContain(run.Id, preview.CandidateRunIds);
            Assert.Contains(
                "paper_order_dependency",
                await ReadRunBlockersAsync(factory, run.Id));

            var reconciliation = await projection.ReconcileNextStrategyAsync();
            Assert.True(reconciliation.Reconciled, reconciliation.Error);
            Assert.Equal(strategyId, reconciliation.StrategyId);
            var authoritative = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            var dashboard = await ReadDashboardSnapshotAsync(snapshots, strategyId);
            AssertStrategyLifetimeMetricsEqual(authoritative, dashboard);
            var reconciledCounts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(0, reconciledCounts.ProjectionEvents);
            Assert.Equal(0, reconciledCounts.ReconciliationQueueRows);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Transfer_RacingPaperOrderWaitsAndRestoresRawRunAfterCommit()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-paper-race-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var anchor = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"{prefix}-anchor");
        var candidate = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(1)),
            $"{prefix}-candidate");
        var retentionApplicationName = $"retention_race_{Guid.NewGuid():N}";
        var dependencyApplicationName = $"paper_race_{Guid.NewGuid():N}";
        var retentionFactory = WithApplicationName(factory, retentionApplicationName);
        var dependencyFactory = WithApplicationName(factory, dependencyApplicationName);
        using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<StrategyRunRetentionBatchResult>? retentionTask = null;
        Task? dependencyTask = null;

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(anchor));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [anchor.Id],
                cutoffUtc)).DeletedRows);
            var anchorRollup = await ReadRollupGroupAsync(
                factory,
                strategyId,
                anchor.UpdatedAtUtc,
                anchor.SkipReason!);
            await DeleteProjectionBlockersAsync(factory, strategyId);

            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(candidate));
            var originalPayload = await ReadRunPayloadWithoutScopeAsync(factory, candidate.Id);
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Contains(
                candidate.Id,
                (await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10))
                    .CandidateRunIds);

            await using var blockerConnection = factory.CreateConnection();
            await blockerConnection.OpenAsync();
            await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
            await LockRollupGroupAsync(
                blockerConnection,
                blockerTransaction,
                strategyId,
                anchor.UpdatedAtUtc,
                anchor.SkipReason!);

            retentionTask = new PostgresAppRepository(retentionFactory)
                .TransferPaperOnlySkippedRunsToRollupsAsync(
                    [candidate.Id],
                    cutoffUtc,
                    raceCancellation.Token);
            var retentionPid = await WaitForBlockedApplicationAsync(
                factory,
                retentionApplicationName,
                "transactionid");
            await AssertBlockedByAsync(factory, retentionPid, blockerConnection.ProcessID);
            Assert.True(await HoldsExclusiveRetentionGateAsync(factory, retentionPid));
            await AssertRunRowIsLockedAsync(factory, candidate.Id);

            var order = CreatePaperOrder(
                strategyId,
                candidate.ConditionId,
                oldUtc.AddMinutes(2));
            dependencyTask = new PostgresAppRepository(dependencyFactory)
                .AddPaperOrderAsync(order, raceCancellation.Token);
            var dependencyPid = await WaitForBlockedApplicationAsync(
                factory,
                dependencyApplicationName,
                "advisory");
            await AssertBlockedByAsync(factory, dependencyPid, retentionPid);
            Assert.False(dependencyTask.IsCompleted);
            Assert.False(retentionTask.IsCompleted);

            await blockerTransaction.CommitAsync();
            var transfer = await retentionTask.WaitAsync(TimeSpan.FromSeconds(15));
            await dependencyTask.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(1, transfer.SelectedRows);
            Assert.Equal(1, transfer.DeletedRows);
            Assert.Equal(1, transfer.TombstonesChanged);

            Assert.Equal(originalPayload,
                await ReadRunPayloadWithoutScopeAsync(factory, candidate.Id));
            Assert.Equal(StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, candidate.Id));
            Assert.Equal(new RetentionCounts(1, 1, 1, 1, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            var rollup = await ReadRollupGroupAsync(
                factory,
                strategyId,
                anchor.UpdatedAtUtc,
                anchor.SkipReason!);
            Assert.Equal(anchorRollup, rollup);
            Assert.Equal([anchor.Id], await ReadArchivedRunIdsAsync(factory, strategyId));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, candidate.Id));
        }
        finally
        {
            raceCancellation.Cancel();
            await DrainRaceTaskAsync(dependencyTask);
            await DrainRaceTaskAsync(retentionTask);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Transfer_RacingCommittedPaperOrderFirstRejectsStaleAllowlist()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-paper-first-race-{Guid.NewGuid():N}");
        var retentionApplicationName = $"retention_dep_first_{Guid.NewGuid():N}";
        var retentionFactory = WithApplicationName(factory, retentionApplicationName);
        var order = CreatePaperOrder(strategyId, run.ConditionId, oldUtc.AddMinutes(1));
        using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<StrategyRunRetentionBatchResult>? retentionTask = null;

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Contains(
                run.Id,
                (await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10))
                    .CandidateRunIds);

            await using var dependencyConnection = factory.CreateConnection();
            await dependencyConnection.OpenAsync();
            await using var dependencyTransaction =
                await dependencyConnection.BeginTransactionAsync();
            Assert.Equal(1, await InsertPaperOrderAsync(
                dependencyConnection,
                dependencyTransaction,
                order));

            retentionTask = new PostgresAppRepository(retentionFactory)
                .TransferPaperOnlySkippedRunsToRollupsAsync(
                    [run.Id],
                    cutoffUtc,
                    raceCancellation.Token);
            var retentionPid = await WaitForBlockedApplicationAsync(
                factory,
                retentionApplicationName,
                "advisory");
            await AssertBlockedByAsync(factory, retentionPid, dependencyConnection.ProcessID);
            Assert.False(retentionTask.IsCompleted);

            await dependencyTransaction.CommitAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await retentionTask.WaitAsync(TimeSpan.FromSeconds(15)));

            Assert.Equal([run.Id], await ReadRunIdsAsync(factory, [run.Id]));
            Assert.Equal(1, await ReadPaperOrderCountAsync(factory, order.Id));
            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(1, counts.RawRuns);
            Assert.Equal(0, counts.RollupRuns);
            Assert.Equal(0, counts.Tombstones);
        }
        finally
        {
            raceCancellation.Cancel();
            await DrainRaceTaskAsync(retentionTask);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ArchivedRuns_LiveOrderAndShadowDecisionRestoreExactPromotedRawRuns()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var liveStrategyId = Guid.NewGuid();
        var shadowStrategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var prefix = $"retention-live-restore-{Guid.NewGuid():N}";
        var liveRun = WithRetentionKey(
            CreateSkippedRun(liveStrategyId, oldUtc),
            $"{prefix}-live");
        var shadowRun = WithRetentionKey(
            CreateSkippedRun(shadowStrategyId, oldUtc.AddMinutes(1)),
            $"{prefix}-shadow");

        await InsertStrategyAsync(
            factory,
            liveStrategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        await InsertStrategyAsync(
            factory,
            shadowStrategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(liveRun));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(shadowRun));
            var livePayload = await ReadRunPayloadWithoutScopeAsync(factory, liveRun.Id);
            var shadowPayload = await ReadRunPayloadWithoutScopeAsync(factory, shadowRun.Id);
            await DeleteProjectionBlockersAsync(factory, liveStrategyId);
            await DeleteProjectionBlockersAsync(factory, shadowStrategyId);

            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [liveRun.Id],
                cutoffUtc)).DeletedRows);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [shadowRun.Id],
                cutoffUtc)).DeletedRows);

            await repository.AddLiveOrderAsync(CreateLiveOrder(
                liveStrategyId,
                liveRun.ConditionId,
                oldUtc.AddMinutes(2)));
            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                shadowStrategyId,
                shadowRun.MarketId,
                shadowRun.ConditionId,
                oldUtc.AddMinutes(2)));

            Assert.Equal(livePayload, await ReadRunPayloadWithoutScopeAsync(factory, liveRun.Id));
            Assert.Equal(shadowPayload, await ReadRunPayloadWithoutScopeAsync(factory, shadowRun.Id));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, liveRun.Id));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, shadowRun.Id));
            Assert.Equal(0, (await ReadRetentionCountsAsync(factory, liveStrategyId)).RollupRuns);
            Assert.Equal(0, (await ReadRetentionCountsAsync(factory, shadowStrategyId)).RollupRuns);
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, liveRun.Id));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, shadowRun.Id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, liveStrategyId);
            await DeleteTestStrategyAsync(factory, shadowStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_RolledBackPaperOrderRollsBackRawRunAndRollupCompensation()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-rollback-restore-{Guid.NewGuid():N}");

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);
            await DeleteProjectionBlockersAsync(factory, strategyId);
            var before = await ReadRetentionCountsAsync(factory, strategyId);
            var order = CreatePaperOrder(strategyId, run.ConditionId, oldUtc.AddMinutes(1));

            await using (var connection = factory.CreateConnection())
            {
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                Assert.Equal(1, await InsertPaperOrderAsync(connection, transaction, order));
                Assert.Equal(1, await ReadRunCountAsync(connection, transaction, run.Id));
                Assert.Equal(0, await ReadTombstoneCountAsync(connection, transaction, run.Id));
                await transaction.RollbackAsync();
            }

            Assert.Equal(before, await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Empty(await ReadRunIdsAsync(factory, [run.Id]));
            Assert.Equal(0, await ReadPaperOrderCountAsync(factory, order.Id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_ConflictDoNothingDoesNotRestoreArchivedRun()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-conflict-restore-{Guid.NewGuid():N}");
        var existingOrder = CreatePaperOrder(
            strategyId,
            $"retention-unrelated-{Guid.NewGuid():N}",
            oldUtc);

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            await repository.AddPaperOrderAsync(existingOrder);
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);
            var before = await ReadRetentionCountsAsync(factory, strategyId);

            Assert.Equal(0, await InsertConflictingPaperOrderAsync(
                factory,
                existingOrder,
                run.ConditionId));

            Assert.Equal(before, await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Empty(await ReadRunIdsAsync(factory, [run.Id]));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_LegacyTombstoneRejectsDependencyWriteFailClosed()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var archivedRunId = Guid.NewGuid();
        var order = CreatePaperOrder(
            strategyId,
            $"retention-legacy-condition-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow.AddDays(-4));

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            await using (var connection = factory.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "INSERT INTO public.strategy_market_paper_skip_tombstones " +
                    "(strategy_id, market_id, archived_run_id, archived_at_utc) " +
                    "VALUES (@StrategyId, @MarketId, @ArchivedRunId, @ArchivedAtUtc);",
                    connection);
                command.Parameters.AddWithValue("StrategyId", strategyId);
                command.Parameters.AddWithValue("MarketId", $"retention-legacy-market-{Guid.NewGuid():N}");
                command.Parameters.AddWithValue("ArchivedRunId", archivedRunId);
                command.Parameters.AddWithValue("ArchivedAtUtc", DateTime.UtcNow.AddDays(-3));
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => repository.AddPaperOrderAsync(order));
            Assert.Equal("55000", exception.SqlState);
            Assert.Contains("legacy/incomplete tombstone", exception.MessageText);
            Assert.Equal(0, await ReadPaperOrderCountAsync(factory, order.Id));
            Assert.Empty(await ReadRunIdsAsync(factory, [archivedRunId]));
            Assert.Equal([archivedRunId], await ReadArchivedRunIdsAsync(factory, strategyId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_PositionWalletMatchingCaseVariantCodesRestoresEveryRun()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var firstStrategyId = Guid.NewGuid();
        var secondStrategyId = Guid.NewGuid();
        var codeSuffix = Guid.NewGuid().ToString("N");
        var firstStrategyCode = $"RetentionCase_{codeSuffix}";
        var secondStrategyCode = $"retentioncase_{codeSuffix}";
        var conditionId = $"retention-case-wallet-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var firstRun = WithRetentionKey(
            CreateSkippedRun(firstStrategyId, oldUtc),
            conditionId);
        var secondRun = WithRetentionKey(
            CreateSkippedRun(secondStrategyId, oldUtc.AddMinutes(1)),
            conditionId);

        await InsertStrategyAsync(factory, firstStrategyId, firstStrategyCode, liveStakes: false);
        await InsertStrategyAsync(factory, secondStrategyId, secondStrategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(firstRun));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(secondRun));
            await DeleteProjectionBlockersAsync(factory, firstStrategyId);
            await DeleteProjectionBlockersAsync(factory, secondStrategyId);
            var transfer = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [firstRun.Id, secondRun.Id],
                DateTimeOffset.UtcNow.AddHours(-48));
            Assert.Equal(2, transfer.DeletedRows);

            await repository.UpsertPaperPositionAsync(new PaperPosition(
                $"asset-{Guid.NewGuid():N}",
                conditionId,
                "Yes",
                2m,
                0.50m,
                1m,
                0m,
                oldUtc,
                $"strategy:{firstStrategyCode.ToUpperInvariant()}"));

            Assert.Equal(
                new[] { firstRun.Id, secondRun.Id }.OrderBy(id => id),
                (await ReadRunIdsAsync(factory, [firstRun.Id, secondRun.Id])).OrderBy(id => id));
            var firstCounts = await ReadRetentionCountsAsync(factory, firstStrategyId);
            var secondCounts = await ReadRetentionCountsAsync(factory, secondStrategyId);
            Assert.Equal((1L, 0L, 0L, 1L),
                (firstCounts.RawRuns, firstCounts.RollupRuns,
                    firstCounts.Tombstones, firstCounts.ReconciliationQueueRows));
            Assert.Equal((1L, 0L, 0L, 1L),
                (secondCounts.RawRuns, secondCounts.RollupRuns,
                    secondCounts.Tombstones, secondCounts.ReconciliationQueueRows));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, firstRun.Id));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, secondRun.Id));
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, conditionId);
            await DeleteTestStrategyAsync(factory, firstStrategyId);
            await DeleteTestStrategyAsync(factory, secondStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_StrategyCodeUpdateThatCreatesPositionMatchRestoresRun()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldCode = $"retention_old_{Guid.NewGuid():N}";
        var newCode = $"retention_new_{Guid.NewGuid():N}";
        var conditionId = $"retention-code-update-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var run = WithRetentionKey(CreateSkippedRun(strategyId, oldUtc), conditionId);

        await InsertStrategyAsync(factory, strategyId, oldCode, liveStakes: false);
        try
        {
            await repository.UpsertPaperPositionAsync(new PaperPosition(
                $"asset-{Guid.NewGuid():N}",
                conditionId,
                "Yes",
                2m,
                0.50m,
                1m,
                0m,
                oldUtc,
                $"strategy:{newCode}"));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);

            await using (var connection = factory.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "UPDATE public.strategies SET code = @Code, updated_at_utc = clock_timestamp() " +
                    "WHERE id = @StrategyId;",
                    connection);
                command.Parameters.AddWithValue("Code", newCode);
                command.Parameters.AddWithValue("StrategyId", strategyId);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            Assert.Equal([run.Id], await ReadRunIdsAsync(factory, [run.Id]));
            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((1L, 0L, 0L, 1L),
                (counts.RawRuns, counts.RollupRuns,
                    counts.Tombstones, counts.ReconciliationQueueRows));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, run.Id));
            Assert.Contains("paper_position_dependency", await ReadRunBlockersAsync(factory, run.Id));
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, conditionId);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_RacingStrategyCodeAndPositionWritesPreserveRunInBothOrders()
    {
        await AssertStrategyCodePositionRaceAsync(codeUpdateFirst: true);
        await AssertStrategyCodePositionRaceAsync(codeUpdateFirst: false);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ArchivedRuns_RemainingPaperDependenciesRestoreExactRawRunsAndReverseRollup()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-remaining-restore-{Guid.NewGuid():N}";
        var oldUtc = new DateTimeOffset(
            DateTime.UtcNow.Date.AddDays(-4).AddHours(12),
            TimeSpan.Zero);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var dryRun = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"{prefix}-dry-run");
        var copiedPositionRun = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(1)),
            $"{prefix}-copied-position");
        var copiedActivityRun = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(2)),
            $"{prefix}-copied-activity");
        var onchainRun = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(3)),
            $"{prefix}-onchain");
        var settlementRun = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(4)),
            $"{prefix}-settlement");
        var runs = new[]
        {
            dryRun,
            copiedPositionRun,
            copiedActivityRun,
            onchainRun,
            settlementRun
        };
        var settlement = new PaperPositionSettlement(
            Guid.NewGuid(),
            $"strategy:{strategyCode}",
            $"asset-{Guid.NewGuid():N}",
            settlementRun.ConditionId,
            "Yes",
            $"asset-{Guid.NewGuid():N}",
            "Yes",
            "IntegrationTest",
            2m,
            0.50m,
            1m,
            2m,
            1m,
            true,
            "IntegrationTest",
            oldUtc.AddHours(1),
            oldUtc.AddHours(1));

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.Equal(runs.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(runs)).Count);
            var originalPayloads = new Dictionary<Guid, string>();
            foreach (var run in runs)
            {
                originalPayloads.Add(
                    run.Id,
                    await ReadRunPayloadWithoutScopeAsync(factory, run.Id));
            }

            await DeleteProjectionBlockersAsync(factory, strategyId);
            var transfer = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                runs.Select(run => run.Id).ToArray(),
                cutoffUtc);
            Assert.Equal(runs.Length, transfer.SelectedRows);
            Assert.Equal(runs.Length, transfer.DeletedRows);
            Assert.Equal(1, transfer.RollupRowsChanged);
            Assert.Equal(runs.Length, transfer.TombstonesChanged);
            Assert.Equal(new RetentionCounts(0, runs.Length, runs.Length, 0, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Equal(
                runs.Select(run => run.Id).OrderBy(id => id),
                await ReadArchivedRunIdsAsync(factory, strategyId));

            await DeleteProjectionBlockersAsync(factory, strategyId);
            await InsertDirectExternalDependenciesAsync(
                factory,
                strategyId,
                oldUtc.AddHours(2),
                dryRun,
                copiedPositionRun,
                copiedActivityRun,
                onchainRun);

            var afterFourRestores = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((4L, 1L, 1L, 1L),
                (afterFourRestores.RawRuns,
                    afterFourRestores.RollupRuns,
                    afterFourRestores.Tombstones,
                    afterFourRestores.ReconciliationQueueRows));
            Assert.Equal([settlementRun.Id],
                await ReadArchivedRunIdsAsync(factory, strategyId));
            Assert.Equal(
                new RollupGroup(
                    1,
                    settlementRun.UpdatedAtUtc,
                    settlementRun.UpdatedAtUtc),
                await ReadRollupGroupAsync(
                    factory,
                    strategyId,
                    settlementRun.UpdatedAtUtc,
                    settlementRun.SkipReason!));

            var firstRestoreExpectations = new[]
            {
                (Run: dryRun, Blocker: "dry_run_dependency"),
                (Run: copiedPositionRun, Blocker: "copied_leader_position_dependency"),
                (Run: copiedActivityRun, Blocker: "copied_leader_activity_dependency"),
                (Run: onchainRun, Blocker: "onchain_paper_dependency")
            };
            foreach (var expectation in firstRestoreExpectations)
            {
                Assert.Equal(
                    originalPayloads[expectation.Run.Id],
                    await ReadRunPayloadWithoutScopeAsync(factory, expectation.Run.Id));
                Assert.Equal(
                    StrategyRunRetentionScopes.PaperOnly,
                    await ReadRetentionScopeAsync(factory, expectation.Run.Id));
                Assert.Contains(
                    expectation.Blocker,
                    await ReadRunBlockersAsync(factory, expectation.Run.Id));
                Assert.Equal(
                    0,
                    await ReadStrategyRunProjectionEventCountAsync(factory, expectation.Run.Id));
            }

            Assert.True(await repository.TryAddPaperPositionSettlementAsync(settlement));

            Assert.Equal(
                originalPayloads[settlementRun.Id],
                await ReadRunPayloadWithoutScopeAsync(factory, settlementRun.Id));
            Assert.Equal(
                StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, settlementRun.Id));
            Assert.Contains(
                "paper_settlement_dependency",
                await ReadRunBlockersAsync(factory, settlementRun.Id));
            Assert.Equal(
                0,
                await ReadStrategyRunProjectionEventCountAsync(factory, settlementRun.Id));
            var restored = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((5L, 0L, 0L, 1L),
                (restored.RawRuns,
                    restored.RollupRuns,
                    restored.Tombstones,
                    restored.ReconciliationQueueRows));
            Assert.Empty(await ReadArchivedRunIdsAsync(factory, strategyId));
            var restoredRunIds = runs.Select(run => run.Id).ToHashSet();
            Assert.DoesNotContain(
                (await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25))
                    .CandidateRunIds,
                restoredRunIds.Contains);
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, prefix);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_HigherIsolationDependencyWriteFailsClosedWithoutChangingArchive()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-isolation-restore-{Guid.NewGuid():N}");

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);
            await DeleteProjectionBlockersAsync(factory, strategyId);
            var archived = await ReadRetentionCountsAsync(factory, strategyId);

            foreach (var isolationLevel in new[]
                     {
                         IsolationLevel.RepeatableRead,
                         IsolationLevel.Serializable
                     })
            {
                var order = CreatePaperOrder(
                    strategyId,
                    run.ConditionId,
                    oldUtc.AddMinutes(1));
                await using var connection = factory.CreateConnection();
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(isolationLevel);
                var exception = await Assert.ThrowsAsync<PostgresException>(
                    async () => await InsertPaperOrderAsync(connection, transaction, order));
                Assert.Equal("0A000", exception.SqlState);
                Assert.Contains("requires READ COMMITTED", exception.MessageText);
                await transaction.RollbackAsync();

                Assert.Equal(0, await ReadPaperOrderCountAsync(factory, order.Id));
                Assert.Equal(archived, await ReadRetentionCountsAsync(factory, strategyId));
                Assert.Empty(await ReadRunIdsAsync(factory, [run.Id]));
                Assert.Equal([run.Id], await ReadArchivedRunIdsAsync(factory, strategyId));
            }
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task CandidateFirstEligibility_FullyBlockedFirstPageAdvancesToEligibleTailAndReportsDuration()
    {
        var factory = await CreateFactoryAsync();

        const int pageSize = 500;
        const int eligibleCount = 500;
        const int blockedCount = 500;
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-benchmark-{Guid.NewGuid():N}";
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var priorIntrinsicCursor = await ReadNewestIntrinsicRunCursorAsync(factory, cutoffUtc);
        var firstUpdatedAtUtc = priorIntrinsicCursor?.UpdatedAtUtc.AddMilliseconds(1)
            ?? new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var lastUpdatedAtUtc = firstUpdatedAtUtc.AddTicks(
            (eligibleCount + blockedCount - 1L) * TimeSpan.TicksPerMicrosecond);
        Assert.True(
            lastUpdatedAtUtc < cutoffUtc,
            $"The retention paging fixture needs an intrinsic timestamp gap before {cutoffUtc:O}, " +
            $"but the newest existing cursor is {priorIntrinsicCursor?.UpdatedAtUtc:O}.");
        var baseline = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 0);
        var runs = Enumerable.Range(0, eligibleCount + blockedCount)
            .Select(index => WithRetentionKey(
                CreateSkippedRun(
                    strategyId,
                    firstUpdatedAtUtc.AddTicks(index * TimeSpan.TicksPerMicrosecond)),
                $"{prefix}-{index:D4}"))
            .ToArray();
        var blockedRuns = runs.Take(blockedCount).ToArray();
        var eligibleRuns = runs.Skip(blockedCount).ToArray();

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await InsertPaperOrderDependenciesAsync(
                factory,
                strategyId,
                firstUpdatedAtUtc,
                blockedRuns.Select(run => run.ConditionId).ToArray());
            Assert.Equal(
                runs.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(runs)).Count);
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var summaryTimer = Stopwatch.StartNew();
            var summary = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 10);
            summaryTimer.Stop();
            var previewTimer = Stopwatch.StartNew();
            var firstPage = await repository.PreviewPaperOnlySkippedRunRetentionAsync(
                cutoffUtc,
                pageSize,
                priorIntrinsicCursor);
            var firstPageCursor = Assert.IsType<StrategyRunRetentionCursor>(
                firstPage.ContinuationCursor);
            var secondPage = await repository.PreviewPaperOnlySkippedRunRetentionAsync(
                cutoffUtc,
                pageSize,
                firstPageCursor);
            previewTimer.Stop();

            Assert.Empty(firstPage.CandidateRunIds);
            Assert.Equal(0, firstPage.DistinctStrategies);
            Assert.Null(firstPage.OldestUpdatedAtUtc);
            Assert.Null(firstPage.NewestUpdatedAtUtc);
            Assert.Equal(pageSize, firstPage.IntrinsicRowsScanned);
            Assert.False(firstPage.ReachedIntrinsicEnd);
            Assert.Equal(
                new StrategyRunRetentionCursor(
                    blockedRuns[^1].UpdatedAtUtc,
                    blockedRuns[^1].Id),
                firstPageCursor);

            Assert.Equal(
                eligibleRuns.Select(run => run.Id),
                secondPage.CandidateRunIds);
            Assert.Equal(1, secondPage.DistinctStrategies);
            Assert.Equal(eligibleRuns[0].UpdatedAtUtc, secondPage.OldestUpdatedAtUtc);
            Assert.Equal(eligibleRuns[^1].UpdatedAtUtc, secondPage.NewestUpdatedAtUtc);
            Assert.Equal(pageSize, secondPage.IntrinsicRowsScanned);
            Assert.True(secondPage.ReachedIntrinsicEnd);
            Assert.Equal(
                new StrategyRunRetentionCursor(
                    eligibleRuns[^1].UpdatedAtUtc,
                    eligibleRuns[^1].Id),
                secondPage.ContinuationCursor);
            Assert.Equal(baseline.TotalCandidateRows + eligibleCount, summary.TotalCandidateRows);
            Console.WriteLine(
                $"Set-based retention benchmark: rows={runs.Length}, eligible={eligibleCount}, " +
                $"paperOrderBlocked={blockedCount}, summaryMs={summaryTimer.Elapsed.TotalMilliseconds:F2}, " +
                $"pagedPreviewMs={previewTimer.Elapsed.TotalMilliseconds:F2}");
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    private static async Task<V2DimensionIds> ResolveV2DimensionsAsync(
        PostgresConnectionFactory factory,
        StrategyMarketPaperRun run)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var insertIdentity = new NpgsqlCommand(
                         "INSERT INTO strategy_skip_archive_market_identities (market_id) " +
                         "VALUES (@MarketId) ON CONFLICT (market_id) DO NOTHING;",
                         connection,
                         transaction))
        {
            insertIdentity.Parameters.AddWithValue("MarketId", run.MarketId);
            await insertIdentity.ExecuteNonQueryAsync();
        }

        int marketIdentityId;
        await using (var readIdentity = new NpgsqlCommand(
                         "SELECT market_identity_id " +
                         "FROM strategy_skip_archive_market_identities WHERE market_id = @MarketId;",
                         connection,
                         transaction))
        {
            readIdentity.Parameters.AddWithValue("MarketId", run.MarketId);
            marketIdentityId = Convert.ToInt32(await readIdentity.ExecuteScalarAsync());
        }

        await using (var insertMetadata = new NpgsqlCommand(
                         """
INSERT INTO strategy_skip_archive_market_metadata_versions (
    market_identity_id, condition_id, market_slug, market_title,
    category, market_start_utc, market_end_utc)
VALUES (
    @MarketIdentityId, @ConditionId, @MarketSlug, @MarketTitle,
    @Category, @MarketStartUtc, @MarketEndUtc)
ON CONFLICT DO NOTHING;
""",
                         connection,
                         transaction))
        {
            AddV2MetadataParameters(insertMetadata, marketIdentityId, run);
            await insertMetadata.ExecuteNonQueryAsync();
        }

        int metadataVersionId;
        await using (var readMetadata = new NpgsqlCommand(
                         """
SELECT metadata_version_id
FROM strategy_skip_archive_market_metadata_versions
WHERE market_identity_id = @MarketIdentityId
  AND condition_id = @ConditionId
  AND market_slug = @MarketSlug
  AND market_title = @MarketTitle
  AND category IS NOT DISTINCT FROM @Category
  AND market_start_utc IS NOT DISTINCT FROM @MarketStartUtc
  AND market_end_utc IS NOT DISTINCT FROM @MarketEndUtc;
""",
                         connection,
                         transaction))
        {
            AddV2MetadataParameters(readMetadata, marketIdentityId, run);
            metadataVersionId = Convert.ToInt32(await readMetadata.ExecuteScalarAsync());
        }

        await using (var insertReason = new NpgsqlCommand(
                         "INSERT INTO strategy_skip_archive_reasons (skip_reason) " +
                         "VALUES (@SkipReason) ON CONFLICT (skip_reason) DO NOTHING;",
                         connection,
                         transaction))
        {
            insertReason.Parameters.AddWithValue("SkipReason", run.SkipReason!);
            await insertReason.ExecuteNonQueryAsync();
        }

        short skipReasonId;
        await using (var readReason = new NpgsqlCommand(
                         "SELECT skip_reason_id FROM strategy_skip_archive_reasons " +
                         "WHERE skip_reason = @SkipReason;",
                         connection,
                         transaction))
        {
            readReason.Parameters.AddWithValue("SkipReason", run.SkipReason!);
            skipReasonId = Convert.ToInt16(await readReason.ExecuteScalarAsync());
        }

        await transaction.CommitAsync();
        return new V2DimensionIds(marketIdentityId, metadataVersionId, skipReasonId);
    }

    private static void AddV2MetadataParameters(
        NpgsqlCommand command,
        int marketIdentityId,
        StrategyMarketPaperRun run)
    {
        command.Parameters.AddWithValue("MarketIdentityId", marketIdentityId);
        command.Parameters.AddWithValue("ConditionId", run.ConditionId);
        command.Parameters.AddWithValue("MarketSlug", run.MarketSlug);
        command.Parameters.AddWithValue("MarketTitle", run.MarketTitle);
        command.Parameters.Add("Category", NpgsqlDbType.Text).Value =
            (object?)run.Category ?? DBNull.Value;
        command.Parameters.Add("MarketStartUtc", NpgsqlDbType.TimestampTz).Value =
            run.MarketStartUtc is null
                ? DBNull.Value
                : run.MarketStartUtc.Value.UtcDateTime;
        command.Parameters.Add("MarketEndUtc", NpgsqlDbType.TimestampTz).Value =
            run.MarketEndUtc is null
                ? DBNull.Value
                : run.MarketEndUtc.Value.UtcDateTime;
    }

    private static async Task<V2DimensionCounts> ReadV2DimensionCountsAsync(
        PostgresConnectionFactory factory,
        string marketId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    count(DISTINCT market_identity.market_identity_id),
    count(metadata.metadata_version_id)
FROM strategy_skip_archive_market_identities market_identity
LEFT JOIN strategy_skip_archive_market_metadata_versions metadata
    ON metadata.market_identity_id = market_identity.market_identity_id
WHERE market_identity.market_id = @MarketId;
""",
            connection);
        command.Parameters.AddWithValue("MarketId", marketId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new V2DimensionCounts(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static async Task<V2DimensionReferenceCounts> ReadV2DimensionReferenceCountsAsync(
        PostgresConnectionFactory factory,
        string marketId,
        string skipReason)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*)
     FROM public.strategy_skip_archive_market_identities
     WHERE market_id = @MarketId COLLATE "C"),
    (SELECT count(*)
     FROM public.strategy_skip_archive_market_metadata_versions metadata
     INNER JOIN public.strategy_skip_archive_market_identities market_identity
         ON market_identity.market_identity_id = metadata.market_identity_id
     WHERE market_identity.market_id = @MarketId COLLATE "C"),
    (SELECT count(*)
     FROM public.strategy_skip_archive_reasons
     WHERE skip_reason = @SkipReason COLLATE "C"),
    (SELECT count(*)
     FROM public.strategy_market_paper_skip_tombstones_v2 tombstone
     INNER JOIN public.strategy_skip_archive_market_identities market_identity
         ON market_identity.market_identity_id = tombstone.market_identity_id
     WHERE market_identity.market_id = @MarketId COLLATE "C");
""",
            connection);
        command.Parameters.AddWithValue("MarketId", marketId);
        command.Parameters.AddWithValue("SkipReason", skipReason);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new V2DimensionReferenceCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    private static async Task<long> ReadV2TombstoneCountAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM public.strategy_market_paper_skip_tombstones_v2 " +
            "WHERE strategy_id = @StrategyId;",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task SetIdentitySequenceAtLimitAsync(
        PostgresConnectionFactory factory,
        string tableName,
        string columnName)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence(@TableName, @ColumnName), 32767, true);",
            connection);
        command.Parameters.AddWithValue("TableName", $"public.{tableName}");
        command.Parameters.AddWithValue("ColumnName", columnName);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ResetIdentitySequenceToTableMaximumAsync(
        PostgresConnectionFactory factory,
        string tableName,
        string columnName)
    {
        var quotedTable = new NpgsqlCommandBuilder().QuoteIdentifier(tableName);
        var quotedColumn = new NpgsqlCommandBuilder().QuoteIdentifier(columnName);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
SELECT setval(
    pg_get_serial_sequence(@TableName, @ColumnName),
    GREATEST(COALESCE((SELECT max({quotedColumn}) FROM public.{quotedTable}), 0) + 1, 1),
    false);
""",
            connection);
        command.Parameters.AddWithValue("TableName", $"public.{tableName}");
        command.Parameters.AddWithValue("ColumnName", columnName);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateDeleteFailureTriggerAsync(
        PostgresConnectionFactory factory,
        string triggerName,
        string functionName,
        Guid runId)
    {
        var builder = new NpgsqlCommandBuilder();
        var quotedTrigger = builder.QuoteIdentifier(triggerName);
        var quotedFunction = builder.QuoteIdentifier(functionName);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
CREATE FUNCTION public.{quotedFunction}()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.id = '{runId:D}'::uuid THEN
        RAISE EXCEPTION 'forced v2 raw delete failure';
    END IF;
    RETURN OLD;
END;
$$;
CREATE TRIGGER {quotedTrigger}
BEFORE DELETE ON public.strategy_market_paper_runs
FOR EACH ROW
EXECUTE FUNCTION public.{quotedFunction}();
""",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDeleteFailureTriggerAsync(
        PostgresConnectionFactory factory,
        string triggerName,
        string functionName)
    {
        var builder = new NpgsqlCommandBuilder();
        var quotedTrigger = builder.QuoteIdentifier(triggerName);
        var quotedFunction = builder.QuoteIdentifier(functionName);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
DROP TRIGGER IF EXISTS {quotedTrigger} ON public.strategy_market_paper_runs;
DROP FUNCTION IF EXISTS public.{quotedFunction}();
""",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertMalformedV2TombstoneAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.strategy_market_paper_skip_tombstones_v2 (
    strategy_id, market_identity_id, metadata_version_id, archived_run_id,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome,
    stake_usd, skip_reason_id, run_updated_at_utc)
VALUES (
    @StrategyId, 2147483647, 2147483647, @RunId,
    clock_timestamp(), clock_timestamp(), NULL, NULL,
    1, 32767, clock_timestamp());
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("RunId", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertV2DimensionMutationRejectedAsync(
        PostgresConnectionFactory factory,
        string sql,
        object id)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("Id", id);
        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync());
        Assert.Equal("55000", exception.SqlState);
    }

    private static async Task<Dictionary<Guid, short>> ReadArchiveStorageVersionsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT archived_run_id, archive_storage_version
FROM strategy_market_paper_skip_archive_rows
WHERE strategy_id = @StrategyId
ORDER BY archived_run_id;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        var results = new Dictionary<Guid, short>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetGuid(0), reader.GetInt16(1));
        }

        return results;
    }

    private static Task<string> ReadRestorableArchivePayloadAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        return ReadRestorablePayloadAsync(
            factory,
            """
SELECT to_jsonb(payload)::text
FROM (
    SELECT
        archived_run_id AS id,
        strategy_id,
        market_id,
        condition_id,
        market_slug,
        market_title,
        category,
        market_start_utc,
        market_end_utc,
        detected_at_utc,
        entry_due_at_utc,
        'Skipped'::text AS status,
        selected_asset_id,
        selected_outcome,
        NULL::numeric AS entry_price,
        stake_usd,
        NULL::numeric AS size_shares,
        NULL::uuid AS signal_id,
        NULL::uuid AS paper_order_id,
        NULL::timestamptz AS entered_at_utc,
        NULL::numeric AS settlement_price,
        NULL::numeric AS settlement_value_usd,
        NULL::numeric AS realized_pnl_usd,
        NULL::timestamptz AS settled_at_utc,
        skip_reason,
        NULL::jsonb AS skip_diagnostics_json,
        run_created_at_utc AS created_at_utc,
        run_updated_at_utc AS updated_at_utc,
        0::numeric AS fee_usd,
        'LegacyUnknown'::text AS fee_accounting_status,
        'Unknown'::text AS fee_liquidity_role,
        ''::text AS fee_calculation_source,
        NULL::numeric AS fee_rate,
        NULL::integer AS fee_exponent,
        NULL::boolean AS fee_taker_only,
        NULL::timestamptz AS fee_calculated_at_utc,
        NULL::numeric AS net_realized_pnl_usd
    FROM strategy_market_paper_skip_archive_rows
    WHERE archived_run_id = @RunId
) payload;
""",
            runId);
    }

    private static Task<string> ReadRestorableRawPayloadAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        return ReadRestorablePayloadAsync(
            factory,
            """
SELECT to_jsonb(payload)::text
FROM (
    SELECT
        id,
        strategy_id,
        market_id,
        condition_id,
        market_slug,
        market_title,
        category,
        market_start_utc,
        market_end_utc,
        detected_at_utc,
        entry_due_at_utc,
        status,
        selected_asset_id,
        selected_outcome,
        entry_price,
        stake_usd,
        size_shares,
        signal_id,
        paper_order_id,
        entered_at_utc,
        settlement_price,
        settlement_value_usd,
        realized_pnl_usd,
        settled_at_utc,
        skip_reason,
        skip_diagnostics_json,
        created_at_utc,
        updated_at_utc,
        fee_usd,
        fee_accounting_status,
        fee_liquidity_role,
        fee_calculation_source,
        fee_rate,
        fee_exponent,
        fee_taker_only,
        fee_calculated_at_utc,
        net_realized_pnl_usd
    FROM strategy_market_paper_runs
    WHERE id = @RunId
) payload;
""",
            runId);
    }

    private static async Task<string> ReadRestorablePayloadAsync(
        PostgresConnectionFactory factory,
        string sql,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("RunId", runId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task<PostgresConnectionFactory> CreateFactoryAsync()
    {
        return await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
    }

    private static async Task InsertArchivedPaperSkipAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        Guid runId,
        DateTimeOffset updatedAtUtc,
        string skipReason)
    {
        var marketId = $"retention-archive-{runId:N}";
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
WITH archive_values AS (
    SELECT
        date_trunc('day', @UpdatedAtUtc::timestamptz AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            AS bucket_start_utc
), rollup AS (
    INSERT INTO strategy_paper_skip_rollups (
        strategy_id, bucket_start_utc, skip_reason, run_count,
        first_updated_at_utc, last_updated_at_utc, created_at_utc, updated_at_utc)
    SELECT
        @StrategyId, archive_values.bucket_start_utc, @SkipReason, 1,
        @UpdatedAtUtc, @UpdatedAtUtc, clock_timestamp(), clock_timestamp()
    FROM archive_values
    RETURNING bucket_start_utc
)
INSERT INTO strategy_market_paper_skip_tombstones (
    strategy_id, market_id, archived_run_id, archived_at_utc, archive_format_version,
    condition_id, market_slug, market_title, category, market_start_utc, market_end_utc,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome, stake_usd,
    skip_reason, run_created_at_utc, run_updated_at_utc, rollup_bucket_start_utc)
SELECT
    @StrategyId, @MarketId, @RunId, clock_timestamp(), 1,
    @ConditionId, @MarketId, 'Dashboard compact skip integration test', 'Test',
    @UpdatedAtUtc - interval '5 minutes', @UpdatedAtUtc,
    @UpdatedAtUtc - interval '10 minutes', @UpdatedAtUtc - interval '5 minutes',
    NULL, NULL, 6.00,
    @SkipReason, @UpdatedAtUtc - interval '10 minutes', @UpdatedAtUtc,
    rollup.bucket_start_utc
FROM rollup;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("RunId", runId);
        command.Parameters.AddWithValue("MarketId", marketId);
        command.Parameters.AddWithValue("ConditionId", $"condition-{marketId}");
        command.Parameters.AddWithValue("SkipReason", skipReason);
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<int> ReadRecentStrategyRunFactCountAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT count(*)::integer
FROM dashboard_strategy_recent_projection_facts
WHERE source_kind = @SourceKind
  AND source_id = @RunId;
""",
            connection);
        command.Parameters.AddWithValue("SourceKind", DashboardProjectionSourceKinds.StrategyRun);
        command.Parameters.AddWithValue("RunId", runId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static StrategyMarketPaperRun CreateSkippedRun(Guid strategyId, DateTimeOffset updatedAtUtc)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            strategyId,
            $"retention-market-{suffix}",
            $"retention-condition-{suffix}",
            $"retention-market-{suffix}",
            "Strategy run retention integration test",
            "Test",
            updatedAtUtc.AddMinutes(-5),
            updatedAtUtc,
            updatedAtUtc.AddMinutes(-10),
            updatedAtUtc.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Skipped,
            null,
            null,
            null,
            1m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "retention_test_skip",
            updatedAtUtc.AddMinutes(-10),
            updatedAtUtc);
    }

    private static StrategyMarketPaperRun WithRetentionKey(
        StrategyMarketPaperRun run,
        string key)
    {
        return run with
        {
            MarketId = key,
            ConditionId = key,
            MarketSlug = key,
            MarketTitle = key
        };
    }

    private static async Task AssertV2WriterBlockerMatrixAsync(
        PostgresConnectionFactory factory,
        V2WriterKind writer)
    {
        var repository = new PostgresAppRepository(factory);
        var paperStrategyId = Guid.NewGuid();
        var liveStrategyId = Guid.NewGuid();
        var guardedStrategyId = Guid.NewGuid();
        var boundaryStrategyId = Guid.NewGuid();
        var queueStrategyId = Guid.NewGuid();
        var paperCode = $"v2_blockers_{writer}_{Guid.NewGuid():N}";
        var prefix = $"v2-blockers-{writer}-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var index = 0;

        StrategyMarketPaperRun Fixture(Guid strategyId, string name)
        {
            return WithRetentionKey(
                CreateSkippedRun(strategyId, oldUtc.AddMinutes(index++)),
                $"{prefix}-{name}");
        }

        var signalRun = Fixture(paperStrategyId, "signal");
        var signalId = Guid.NewGuid();
        signalRun = signalRun with { SignalId = signalId };
        var paperOrderRun = Fixture(paperStrategyId, "paper-order-field");
        var fieldOrder = CreatePaperOrder(
            paperStrategyId,
            paperOrderRun.ConditionId,
            oldUtc);
        paperOrderRun = paperOrderRun with { PaperOrderId = fieldOrder.Id };

        var intrinsicRuns = new[]
        {
            Fixture(paperStrategyId, "status") with
            {
                Status = StrategyMarketPaperRunStatuses.Observed
            },
            Fixture(paperStrategyId, "empty-reason") with { SkipReason = " " },
            signalRun,
            paperOrderRun,
            Fixture(paperStrategyId, "entered-at") with { EnteredAtUtc = oldUtc },
            Fixture(paperStrategyId, "entry-price") with { EntryPrice = 0.50m },
            Fixture(paperStrategyId, "size") with { SizeShares = 2m },
            Fixture(paperStrategyId, "settlement-price") with { SettlementPrice = 1m },
            Fixture(paperStrategyId, "settlement-value") with { SettlementValueUsd = 2m },
            Fixture(paperStrategyId, "realized-pnl") with { RealizedPnlUsd = 1m },
            Fixture(paperStrategyId, "settled-at") with { SettledAtUtc = oldUtc },
            Fixture(paperStrategyId, "diagnostics") with
            {
                SkipReason = "maker_gtd_post_only_attempts_exhausted",
                SkipDiagnosticsJson =
                    "{\"execution_source\":\"eth_reference_average_maker_gtd_paper\",\"skip_reason\":\"maker_gtd_post_only_attempts_exhausted\",\"maker_gtd\":{\"execution_source\":\"eth_reference_average_maker_gtd_paper\",\"terminal_outcome\":\"skipped\",\"terminal_reason\":\"maker_gtd_post_only_attempts_exhausted\"}}"
            }
        };
        var feeRuns = new[]
        {
            Fixture(paperStrategyId, "fee-usd") with { FeeUsd = 0.01m },
            Fixture(paperStrategyId, "fee-status") with { FeeAccountingStatus = "Calculated" },
            Fixture(paperStrategyId, "fee-role") with { FeeLiquidityRole = "Taker" },
            Fixture(paperStrategyId, "fee-source") with { FeeCalculationSource = "fixture" },
            Fixture(paperStrategyId, "fee-rate") with { FeeRate = 0.01m },
            Fixture(paperStrategyId, "fee-exponent") with { FeeExponent = 2 },
            Fixture(paperStrategyId, "fee-taker-only") with { FeeTakerOnly = false },
            Fixture(paperStrategyId, "fee-calculated-at") with { FeeCalculatedAtUtc = oldUtc },
            Fixture(paperStrategyId, "net-pnl") with { NetRealizedPnlUsd = -0.01m }
        };
        var liveRun = Fixture(liveStrategyId, "current-live");
        var guardedRun = Fixture(guardedStrategyId, "permanent-live-guard");
        var boundaryRun = Fixture(boundaryStrategyId, "live-boundary");
        var persistedScopeRun = Fixture(paperStrategyId, "persisted-live-or-shadow-scope");
        var persistedScopeRuns = writer == V2WriterKind.TerminalAtInsert
            ? []
            : new[] { persistedScopeRun };

        var externalPaper = Fixture(paperStrategyId, "dependency-paper");
        var externalDry = Fixture(paperStrategyId, "dependency-dry");
        var externalLive = Fixture(paperStrategyId, "dependency-live");
        var externalShadowMarket = Fixture(paperStrategyId, "dependency-shadow-market");
        var externalShadowCondition = Fixture(paperStrategyId, "dependency-shadow-condition");
        var externalPosition = Fixture(paperStrategyId, "dependency-position");
        var externalSettlement = Fixture(paperStrategyId, "dependency-settlement");
        var externalCopiedPosition = Fixture(paperStrategyId, "dependency-copied-position");
        var externalCopiedActivity = Fixture(paperStrategyId, "dependency-copied-activity");
        var externalOnchain = Fixture(paperStrategyId, "dependency-onchain");
        var dependencyRuns = new[]
        {
            externalPaper,
            externalDry,
            externalLive,
            externalShadowMarket,
            externalShadowCondition,
            externalPosition,
            externalSettlement,
            externalCopiedPosition,
            externalCopiedActivity,
            externalOnchain
        };

        var ageMissingEnd = Fixture(paperStrategyId, "age-missing-end") with
        {
            MarketEndUtc = null
        };
        var ageFreshRun = Fixture(paperStrategyId, "age-fresh-run") with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var ageFreshEnd = Fixture(paperStrategyId, "age-fresh-end") with
        {
            MarketEndUtc = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var ageProjectionEvent = Fixture(paperStrategyId, "age-projection-event");
        var ageRecentFact = Fixture(paperStrategyId, "age-recent-fact");
        var ageReconciliation = Fixture(queueStrategyId, "age-reconciliation");
        var ageOnlyRuns = writer == V2WriterKind.AgeBased
            ? new[]
            {
                ageMissingEnd,
                ageFreshRun,
                ageFreshEnd,
                ageProjectionEvent,
                ageRecentFact,
                ageReconciliation
            }
            : [];
        var allRuns = intrinsicRuns
            .Concat(feeRuns)
            .Concat([liveRun, guardedRun, boundaryRun])
            .Concat(persistedScopeRuns)
            .Concat(dependencyRuns)
            .Concat(ageOnlyRuns)
            .ToArray();
        var strategyIds = new[]
        {
            paperStrategyId,
            liveStrategyId,
            guardedStrategyId,
            boundaryStrategyId,
            queueStrategyId
        };

        await InsertStrategyAsync(factory, paperStrategyId, paperCode, liveStakes: false);
        await InsertStrategyAsync(
            factory,
            liveStrategyId,
            $"v2_blockers_live_{Guid.NewGuid():N}",
            liveStakes: true);
        await InsertStrategyAsync(
            factory,
            guardedStrategyId,
            $"v2_blockers_guard_{Guid.NewGuid():N}",
            liveStakes: false);
        await InsertStrategyAsync(
            factory,
            boundaryStrategyId,
            $"v2_blockers_boundary_{Guid.NewGuid():N}",
            liveStakes: false);
        await InsertStrategyAsync(
            factory,
            queueStrategyId,
            $"v2_blockers_queue_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            await InsertSignalAsync(factory, signalId, signalRun.ConditionId, oldUtc);
            await repository.AddPaperOrderAsync(fieldOrder);
            await repository.AddPaperOrderAsync(CreatePaperOrder(
                paperStrategyId,
                externalPaper.ConditionId,
                oldUtc));
            await InsertDirectExternalDependenciesAsync(
                factory,
                paperStrategyId,
                oldUtc,
                externalDry,
                externalCopiedPosition,
                externalCopiedActivity,
                externalOnchain);
            await repository.AddLiveOrderAsync(CreateLiveOrder(
                paperStrategyId,
                externalLive.ConditionId,
                oldUtc));
            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                paperStrategyId,
                externalShadowMarket.MarketId,
                $"{prefix}-unrelated-condition",
                oldUtc));
            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                paperStrategyId,
                $"{prefix}-unrelated-market",
                externalShadowCondition.ConditionId,
                oldUtc));
            await repository.UpsertPaperPositionAsync(new PaperPosition(
                $"asset-{Guid.NewGuid():N}",
                externalPosition.ConditionId,
                "Yes",
                2m,
                0.50m,
                1m,
                0m,
                oldUtc,
                $"strategy:{paperCode}"));
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(
                CreateSettlement(
                    $"strategy:{paperCode}",
                    externalSettlement.ConditionId,
                    oldUtc)));
            await InsertLiveRetentionGuardAsync(factory, guardedStrategyId, oldUtc);
            await SetStrategyLiveBoundaryWithoutCurrentLiveAsync(
                factory,
                boundaryStrategyId,
                boundaryRun.UpdatedAtUtc.AddSeconds(-1));

            if (writer != V2WriterKind.TerminalAtInsert)
            {
                var initialRuns = writer == V2WriterKind.ExistingRawFinalize
                    ? allRuns.Select(run => run with
                    {
                        Status = StrategyMarketPaperRunStatuses.Observed,
                        SkipReason = null
                    }).ToArray()
                    : allRuns;
                Assert.Equal(
                    allRuns.Length,
                    (await repository.TryAddStrategyMarketPaperRunsAsync(initialRuns)).Count);
                foreach (var strategyId in strategyIds)
                {
                    await DeleteProjectionBlockersAsync(factory, strategyId);
                }

                if (persistedScopeRuns.Length > 0)
                {
                    await SetRunRetentionScopeAsync(
                        factory,
                        persistedScopeRun.Id,
                        StrategyRunRetentionScopes.LiveOrShadow);
                    Assert.Equal(
                        StrategyRunRetentionScopes.LiveOrShadow,
                        await ReadRetentionScopeAsync(factory, persistedScopeRun.Id));
                    await DeleteProjectionBlockersAsync(factory, paperStrategyId);
                }

                if (writer == V2WriterKind.AgeBased)
                {
                    await InsertAgeOnlyProjectionBlockersAsync(
                        factory,
                        paperStrategyId,
                        queueStrategyId,
                        oldUtc,
                        ageProjectionEvent.Id,
                        ageRecentFact.Id);
                }
            }

            foreach (var run in allRuns)
            {
                switch (writer)
                {
                    case V2WriterKind.TerminalAtInsert:
                        Assert.Equal(
                            [run.Id],
                            (await repository
                                .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
                                    [run]))
                            .ToArray());
                        break;
                    case V2WriterKind.ExistingRawFinalize:
                        await repository
                            .FinalizeStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
                                [run]);
                        break;
                    case V2WriterKind.AgeBased:
                        await Assert.ThrowsAsync<InvalidOperationException>(() =>
                            repository.TransferPaperOnlySkippedRunsToCompactArchiveV2ForTestsAsync(
                                [run.Id],
                                DateTimeOffset.UtcNow.AddHours(-48)));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(writer), writer, null);
                }

                Assert.Equal([run.Id], await ReadRunIdsAsync(factory, [run.Id]));
                Assert.DoesNotContain(
                    run.Id,
                    (await ReadArchiveStorageVersionsAsync(factory, run.StrategyId)).Keys);
            }

            Assert.Equal(
                allRuns.Select(run => run.Id).OrderBy(id => id),
                (await ReadRunIdsAsync(factory, allRuns.Select(run => run.Id).ToArray()))
                .OrderBy(id => id));
            foreach (var strategyId in strategyIds)
            {
                Assert.Empty(await ReadArchiveStorageVersionsAsync(factory, strategyId));
                var counts = await ReadRetentionCountsAsync(factory, strategyId);
                Assert.Equal(0, counts.RollupRuns);
                Assert.Equal(0, counts.Tombstones);
            }
        }
        finally
        {
            foreach (var strategyId in strategyIds)
            {
                await DeleteTestStrategyAsync(factory, strategyId);
            }
            await DeleteConditionDependenciesAsync(factory, prefix);
            await DeleteSignalAsync(factory, signalId);
        }
    }

    private static async Task InsertSignalAsync(
        PostgresConnectionFactory factory,
        Guid signalId,
        string conditionId,
        DateTimeOffset createdAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.signals (
    id, leader_trade_id, trader_wallet, condition_id, asset_id, outcome,
    leader_price, best_bid, best_ask, spread_abs, spread_pct, lag_seconds,
    score, accepted, decision, proposed_paper_price,
    proposed_size_shares, proposed_notional_usd, created_at_utc, raw_context_json)
VALUES (
    @Id, NULL, @Wallet, @ConditionId, @AssetId, 'Yes',
    0.50, NULL, NULL, NULL, NULL, NULL,
    0, false, 'retention_fixture', NULL,
    NULL, NULL, @CreatedAtUtc, NULL);
""",
            connection);
        command.Parameters.AddWithValue("Id", signalId);
        command.Parameters.AddWithValue("Wallet", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ConditionId", conditionId);
        command.Parameters.AddWithValue("AssetId", $"asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("CreatedAtUtc", createdAtUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteSignalAsync(
        PostgresConnectionFactory factory,
        Guid signalId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM public.signals WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", signalId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertLiveRetentionGuardAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        DateTimeOffset observedAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.strategy_live_retention_guards (
    strategy_id, first_live_observed_at_utc, last_live_observed_at_utc)
VALUES (@StrategyId, @ObservedAtUtc, @ObservedAtUtc);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("ObservedAtUtc", observedAtUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SetStrategyLiveBoundaryWithoutCurrentLiveAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        DateTimeOffset boundaryUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE public.strategies
SET live_stakes = false,
    live_enabled_at_utc = @BoundaryUtc,
    updated_at_utc = @BoundaryUtc
WHERE id = @StrategyId;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("BoundaryUtc", boundaryUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertAgeOnlyProjectionBlockersAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        Guid queueStrategyId,
        DateTimeOffset occurredAtUtc,
        Guid projectionEventRunId,
        Guid recentFactRunId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.dashboard_projection_events (
    source_kind, source_id, strategy_id, operation,
    old_payload, new_payload, transaction_id)
VALUES (
    'StrategyRun', @ProjectionEventRunId, @StrategyId, 'Update',
    NULL, NULL, pg_current_xact_id());

INSERT INTO public.dashboard_strategy_recent_projection_facts (
    source_kind, source_id, fact_kind, strategy_id,
    occurred_at_utc, contribution_json,
    applied_1h, applied_6h, applied_24h, updated_at_utc)
VALUES (
    'StrategyRun', @RecentFactRunId, 'RetentionFixture', @StrategyId,
    @OccurredAtUtc, '{}'::jsonb,
    false, false, false, @OccurredAtUtc);

INSERT INTO public.dashboard_projection_reconciliation_queue (
    strategy_id, priority, reason, requested_at_utc,
    attempt_count, next_attempt_at_utc, last_error)
VALUES (
    @QueueStrategyId, 0, 'retention_fixture', @OccurredAtUtc,
    0, @OccurredAtUtc, NULL);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("QueueStrategyId", queueStrategyId);
        command.Parameters.AddWithValue("OccurredAtUtc", occurredAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("ProjectionEventRunId", projectionEventRunId);
        command.Parameters.AddWithValue("RecentFactRunId", recentFactRunId);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());
    }

    private static LiveOrder CreateLiveOrder(
        Guid strategyId,
        string conditionId,
        DateTimeOffset createdAtUtc)
    {
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Submitted,
            null,
            TradeSide.Buy,
            $"asset-{Guid.NewGuid():N}",
            conditionId,
            "Yes",
            0.50m,
            2m,
            1m,
            "FAK",
            createdAtUtc,
            createdAtUtc.AddMinutes(5),
            createdAtUtc.AddSeconds(1),
            "submitted",
            0m,
            2m,
            string.Empty,
            "{}",
            "retention integration test",
            createdAtUtc.AddSeconds(1),
            StrategyId: strategyId,
            ExecutionSource: "retention_integration_test",
            PostOnly: false);
    }

    private static PaperLiveShadowDecision CreateShadowDecision(
        Guid strategyId,
        string marketId,
        string conditionId,
        DateTimeOffset createdAtUtc)
    {
        return new PaperLiveShadowDecision(
            Guid.NewGuid(),
            strategyId,
            marketId,
            conditionId,
            $"asset-{Guid.NewGuid():N}",
            "Yes",
            TradeSide.Buy,
            0.50m,
            1m,
            2m,
            1m,
            "FAK",
            false,
            "{}",
            0,
            "retention_integration_test",
            createdAtUtc,
            createdAtUtc,
            createdAtUtc.AddMinutes(-5),
            createdAtUtc.AddMinutes(5),
            createdAtUtc.AddSeconds(10),
            createdAtUtc.AddMinutes(5),
            Status: "decision_created",
            UpdatedAtUtc: createdAtUtc);
    }

    private static async Task<Guid[]> ReadLegacyEligibleRunIdsAsync(
        PostgresConnectionFactory factory,
        DateTimeOffset updatedBeforeUtc,
        Guid[] runIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT run.id
FROM strategy_market_paper_runs run
WHERE run.id = ANY(@RunIds)
  AND run.status = 'Skipped'
  AND run.retention_scope = 'PaperOnly'
  AND run.updated_at_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
  AND run.market_end_utc IS NOT NULL
  AND run.market_end_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
  AND cardinality(public.strategy_market_paper_run_retention_blockers(run)) = 0
ORDER BY run.updated_at_utc, run.id;
""",
            connection);
        command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIds;
        command.Parameters.AddWithValue("UpdatedBeforeUtc", updatedBeforeUtc.UtcDateTime);
        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetGuid(0));
        }

        return results.ToArray();
    }

    private static async Task<StrategyRunRetentionCursor?> ReadNewestIntrinsicRunCursorAsync(
        PostgresConnectionFactory factory,
        DateTimeOffset updatedBeforeUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT run.updated_at_utc, run.id
FROM public.strategy_market_paper_runs run
WHERE run.status = 'Skipped'
  AND run.retention_scope = 'PaperOnly'
  AND run.updated_at_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
  AND run.market_end_utc IS NOT NULL
  AND run.market_end_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
  AND NULLIF(btrim(COALESCE(run.skip_reason, '')), '') IS NOT NULL
  AND run.signal_id IS NULL
  AND run.paper_order_id IS NULL
  AND run.entered_at_utc IS NULL
  AND run.entry_price IS NULL
  AND run.size_shares IS NULL
  AND run.settlement_price IS NULL
  AND run.settlement_value_usd IS NULL
  AND run.realized_pnl_usd IS NULL
  AND run.settled_at_utc IS NULL
  AND run.skip_diagnostics_json IS NULL
ORDER BY run.updated_at_utc DESC, run.id DESC
LIMIT 1;
""",
            connection);
        command.Parameters.AddWithValue("UpdatedBeforeUtc", updatedBeforeUtc.UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new StrategyRunRetentionCursor(
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc)),
            reader.GetGuid(1));
    }

    private static async Task<IReadOnlyDictionary<Guid, string[]>> ReadLegacyBlockersAsync(
        PostgresConnectionFactory factory,
        Guid[] runIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT run.id, public.strategy_market_paper_run_retention_blockers(run)
FROM public.strategy_market_paper_runs run
WHERE run.id = ANY(@RunIds);
""",
            connection);
        command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIds;
        var results = new Dictionary<Guid, string[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetGuid(0), reader.GetFieldValue<string[]>(1));
        }

        return results;
    }

    private static async Task InsertDirectExternalDependencyAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        DateTimeOffset oldUtc,
        StrategyMarketPaperRun run,
        DirectExternalDependencyKind dependencyKind)
    {
        var commandText = dependencyKind switch
        {
            DirectExternalDependencyKind.DryRunOrder =>
                """
                INSERT INTO public.dry_run_orders (
                    id, signal_id, strategy_id, status, side, asset_id, condition_id,
                    outcome, price, size_shares, notional_usd, order_type,
                    payload_json, validation_summary, created_at_utc)
                VALUES (
                    @Id, @SignalId, @StrategyId, 'Validated', 'Buy', @AssetId, @ConditionId,
                    'Yes', 0.50, 2, 1, 'FAK',
                    '{}'::jsonb, 'retention fixture', @OldUtc);
                """,
            DirectExternalDependencyKind.CopiedLeaderPosition =>
                """
                INSERT INTO public.paper_copied_leader_positions (
                    id, entry_signal_id, entry_paper_order_id,
                    copied_trader_wallet, asset_id, condition_id, outcome,
                    entry_timestamp_utc, leader_entry_price,
                    leader_initial_size_shares, status,
                    next_activity_sync_at_utc, created_at_utc, updated_at_utc)
                VALUES (
                    @Id, @SignalId, @OrderId,
                    @Wallet, @AssetId, @ConditionId, 'Yes',
                    @OldUtc, 0.50,
                    2, 'Active',
                    @OldUtc, @OldUtc, @OldUtc);
                """,
            DirectExternalDependencyKind.CopiedLeaderActivity =>
                """
                INSERT INTO public.paper_copied_leader_activity_events (
                    id, dedup_key, copied_trader_wallet, asset_id, condition_id,
                    side, price, size_shares, usdc_size,
                    activity_timestamp_utc, raw_json, observed_at_utc)
                VALUES (
                    @Id, @DedupKey, @Wallet, @AssetId, @ConditionId,
                    'Buy', 0.50, 2, 1,
                    @OldUtc, '{}'::jsonb, @OldUtc);
                """,
            DirectExternalDependencyKind.OnchainPaperSignalResult =>
                """
                INSERT INTO public.polymarket_onchain_paper_signal_results (
                    id, capture_id, transaction_hash, log_index, participant_role,
                    copied_trader_wallet, counterparty_wallet, side, token_id,
                    condition_id, market_slug, outcome,
                    status, decision_code, reason_details, processed_at_utc)
                VALUES (
                    @Id, @CaptureId, @TransactionHash, 0, 'maker',
                    @Wallet, @Counterparty, 'Buy', @AssetId,
                    @ConditionId, @MarketSlug, 'Yes',
                    'Skipped', 'retention_fixture', 'retention fixture', @OldUtc);
                """,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dependencyKind),
                dependencyKind,
                null)
        };

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddWithValue("Id", Guid.NewGuid());
        command.Parameters.AddWithValue("AssetId", $"asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ConditionId", run.ConditionId);
        command.Parameters.AddWithValue("OldUtc", oldUtc.UtcDateTime);
        switch (dependencyKind)
        {
            case DirectExternalDependencyKind.DryRunOrder:
                command.Parameters.AddWithValue("SignalId", Guid.NewGuid());
                command.Parameters.AddWithValue("StrategyId", strategyId);
                break;
            case DirectExternalDependencyKind.CopiedLeaderPosition:
                command.Parameters.AddWithValue("SignalId", Guid.NewGuid());
                command.Parameters.AddWithValue("OrderId", Guid.NewGuid());
                command.Parameters.AddWithValue("Wallet", $"0x{Guid.NewGuid():N}");
                break;
            case DirectExternalDependencyKind.CopiedLeaderActivity:
                command.Parameters.AddWithValue("DedupKey", $"retention-{Guid.NewGuid():N}");
                command.Parameters.AddWithValue("Wallet", $"0x{Guid.NewGuid():N}");
                break;
            case DirectExternalDependencyKind.OnchainPaperSignalResult:
                command.Parameters.AddWithValue("CaptureId", Guid.NewGuid());
                command.Parameters.AddWithValue("TransactionHash", $"0x{Guid.NewGuid():N}");
                command.Parameters.AddWithValue("Wallet", $"0x{Guid.NewGuid():N}");
                command.Parameters.AddWithValue("Counterparty", $"0x{Guid.NewGuid():N}");
                command.Parameters.AddWithValue("MarketSlug", run.MarketSlug);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(dependencyKind),
                    dependencyKind,
                    null);
        }

        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertDirectExternalDependenciesAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        DateTimeOffset oldUtc,
        StrategyMarketPaperRun dryRun,
        StrategyMarketPaperRun copiedLeaderPositionRun,
        StrategyMarketPaperRun copiedLeaderActivityRun,
        StrategyMarketPaperRun onchainRun)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.dry_run_orders (
    id, signal_id, strategy_id, status, side, asset_id, condition_id,
    outcome, price, size_shares, notional_usd, order_type,
    payload_json, validation_summary, created_at_utc)
VALUES (
    @DryOrderId, @DrySignalId, @StrategyId, 'Validated', 'Buy', @DryAssetId, @DryConditionId,
    'Yes', 0.50, 2, 1, 'FAK',
    '{}'::jsonb, 'retention fixture', @OldUtc);

INSERT INTO public.paper_copied_leader_positions (
    id, entry_signal_id, entry_paper_order_id,
    copied_trader_wallet, asset_id, condition_id, outcome,
    entry_timestamp_utc, leader_entry_price,
    leader_initial_size_shares, status,
    next_activity_sync_at_utc, created_at_utc, updated_at_utc)
VALUES (
    @CopiedPositionId, @CopiedPositionSignalId, @CopiedPositionOrderId,
    @CopiedPositionWallet, @CopiedPositionAssetId, @CopiedPositionConditionId, 'Yes',
    @OldUtc, 0.50,
    2, 'Active',
    @OldUtc, @OldUtc, @OldUtc);

INSERT INTO public.paper_copied_leader_activity_events (
    id, dedup_key, copied_trader_wallet, asset_id, condition_id,
    side, price, size_shares, usdc_size,
    activity_timestamp_utc, raw_json, observed_at_utc)
VALUES (
    @ActivityId, @ActivityDedupKey, @ActivityWallet, @ActivityAssetId, @ActivityConditionId,
    'Buy', 0.50, 2, 1,
    @OldUtc, '{}'::jsonb, @OldUtc);

INSERT INTO public.polymarket_onchain_paper_signal_results (
    id, capture_id, transaction_hash, log_index, participant_role,
    copied_trader_wallet, counterparty_wallet, side, token_id,
    condition_id, market_slug, outcome,
    status, decision_code, reason_details, processed_at_utc)
VALUES (
    @OnchainId, @OnchainCaptureId, @OnchainTransactionHash, 0, 'maker',
    @OnchainWallet, @OnchainCounterparty, 'Buy', @OnchainAssetId,
    @OnchainConditionId, @OnchainMarketSlug, 'Yes',
    'Skipped', 'retention_fixture', 'retention fixture', @OldUtc);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("OldUtc", oldUtc.UtcDateTime);
        command.Parameters.AddWithValue("DryOrderId", Guid.NewGuid());
        command.Parameters.AddWithValue("DrySignalId", Guid.NewGuid());
        command.Parameters.AddWithValue("DryAssetId", $"asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("DryConditionId", dryRun.ConditionId);
        command.Parameters.AddWithValue("CopiedPositionId", Guid.NewGuid());
        command.Parameters.AddWithValue("CopiedPositionSignalId", Guid.NewGuid());
        command.Parameters.AddWithValue("CopiedPositionOrderId", Guid.NewGuid());
        command.Parameters.AddWithValue("CopiedPositionWallet", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("CopiedPositionAssetId", $"asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("CopiedPositionConditionId", copiedLeaderPositionRun.ConditionId);
        command.Parameters.AddWithValue("ActivityId", Guid.NewGuid());
        command.Parameters.AddWithValue("ActivityDedupKey", $"retention-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ActivityWallet", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ActivityAssetId", $"asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ActivityConditionId", copiedLeaderActivityRun.ConditionId);
        command.Parameters.AddWithValue("OnchainId", Guid.NewGuid());
        command.Parameters.AddWithValue("OnchainCaptureId", Guid.NewGuid());
        command.Parameters.AddWithValue("OnchainTransactionHash", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("OnchainWallet", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("OnchainCounterparty", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("OnchainAssetId", $"asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("OnchainConditionId", onchainRun.ConditionId);
        command.Parameters.AddWithValue("OnchainMarketSlug", onchainRun.MarketSlug);
        Assert.Equal(4, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertPostRunExternalBlockersAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        Guid queueStrategyId,
        DateTimeOffset oldUtc,
        StrategyMarketPaperRun tombstoneRun,
        StrategyMarketPaperRun projectionEventRun,
        StrategyMarketPaperRun recentFactRun)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.strategy_market_paper_skip_tombstones (
    strategy_id, market_id, archived_run_id, archived_at_utc)
VALUES (@StrategyId, @TombstoneMarketId, @TombstoneRunId, @OldUtc);

INSERT INTO public.dashboard_projection_events (
    source_kind, source_id, strategy_id, operation,
    old_payload, new_payload, transaction_id)
VALUES (
    'StrategyRun', @ProjectionEventRunId, @StrategyId, 'Update',
    NULL, NULL, pg_current_xact_id());

INSERT INTO public.dashboard_strategy_recent_projection_facts (
    source_kind, source_id, fact_kind, strategy_id,
    occurred_at_utc, contribution_json,
    applied_1h, applied_6h, applied_24h, updated_at_utc)
VALUES (
    'StrategyRun', @RecentFactRunId, 'RetentionFixture', @StrategyId,
    @OldUtc, '{}'::jsonb,
    false, false, false, @OldUtc);

INSERT INTO public.dashboard_projection_reconciliation_queue (
    strategy_id, priority, reason, requested_at_utc,
    attempt_count, next_attempt_at_utc, last_error)
VALUES (
    @QueueStrategyId, 0, 'retention_fixture', @OldUtc,
    0, @OldUtc, NULL);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("QueueStrategyId", queueStrategyId);
        command.Parameters.AddWithValue("OldUtc", oldUtc.UtcDateTime);
        command.Parameters.AddWithValue("TombstoneMarketId", tombstoneRun.MarketId);
        command.Parameters.AddWithValue("TombstoneRunId", tombstoneRun.Id);
        command.Parameters.AddWithValue("ProjectionEventRunId", projectionEventRun.Id);
        command.Parameters.AddWithValue("RecentFactRunId", recentFactRun.Id);
        Assert.Equal(4, await command.ExecuteNonQueryAsync());
    }

    private static async Task CreateShadowPaperOrdersSchemaAsync(
        PostgresConnectionFactory factory,
        string schemaName)
    {
        var quotedSchemaName = new NpgsqlCommandBuilder().QuoteIdentifier(schemaName);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
CREATE SCHEMA {quotedSchemaName};
CREATE TABLE {quotedSchemaName}.paper_orders (
    strategy_id uuid NOT NULL,
    condition_id text NOT NULL);
""",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropShadowSchemaAsync(
        PostgresConnectionFactory factory,
        string schemaName)
    {
        var quotedSchemaName = new NpgsqlCommandBuilder().QuoteIdentifier(schemaName);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS {quotedSchemaName} CASCADE;",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid[]> ReadRunIdsAsync(
        PostgresConnectionFactory factory,
        Guid[] runIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM strategy_market_paper_runs WHERE id = ANY(@RunIds);",
            connection);
        command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIds;
        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetGuid(0));
        }

        return results.ToArray();
    }

    private static async Task<Dictionary<Guid, string>> ReadPhysicalV1ArchiveRowsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT archive_row.archived_run_id,
       to_jsonb(archive_row)::text
FROM public.strategy_market_paper_skip_tombstones archive_row
WHERE archive_row.strategy_id = @StrategyId
ORDER BY archive_row.archived_run_id;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        var rows = new Dictionary<Guid, string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetGuid(0), reader.GetString(1));
        }

        return rows;
    }

    private static async Task<V2DimensionTableSnapshot> ReadV2DimensionTableSnapshotAsync(
        PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*) FROM public.strategy_skip_archive_market_identities),
    (SELECT COALESCE(
        jsonb_agg(to_jsonb(identity_row) ORDER BY identity_row.market_identity_id),
        '[]'::jsonb)::text
     FROM public.strategy_skip_archive_market_identities identity_row),
    (SELECT count(*) FROM public.strategy_skip_archive_market_metadata_versions),
    (SELECT COALESCE(
        jsonb_agg(to_jsonb(metadata_row) ORDER BY metadata_row.metadata_version_id),
        '[]'::jsonb)::text
     FROM public.strategy_skip_archive_market_metadata_versions metadata_row),
    (SELECT count(*) FROM public.strategy_skip_archive_reasons),
    (SELECT COALESCE(
        jsonb_agg(to_jsonb(reason_row) ORDER BY reason_row.skip_reason_id),
        '[]'::jsonb)::text
     FROM public.strategy_skip_archive_reasons reason_row);
""",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var snapshot = new V2DimensionTableSnapshot(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetString(5));
        Assert.False(await reader.ReadAsync());
        return snapshot;
    }

    private static async Task<string> ReadRunPayloadWithoutScopeAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (to_jsonb(run) - 'retention_scope')::text " +
            "FROM public.strategy_market_paper_runs run WHERE run.id = @RunId;",
            connection);
        command.Parameters.AddWithValue("RunId", runId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Strategy run payload was not found."));
    }

    private static PostgresConnectionFactory WithApplicationName(
        PostgresConnectionFactory factory,
        string applicationName)
    {
        var builder = new NpgsqlConnectionStringBuilder(factory.ConnectionString)
        {
            ApplicationName = applicationName
        };
        return new PostgresConnectionFactory(new StorageOptions
        {
            ConnectionString = builder.ConnectionString
        });
    }

    private static async Task AssertLiveArchiveSerializationAsync(
        short archiveVersion,
        bool liveEnableWins)
    {
        var factory = await CreateFactoryAsync();
        var strategyId = Guid.NewGuid();
        var strategyCode = $"v{archiveVersion}_live_archive_{Guid.NewGuid():N}";
        var dayUtc = new DateTimeOffset(
            DateTime.UtcNow.Date.AddDays(-3).AddHours(12),
            TimeSpan.Zero);
        var boundaryUtc = dayUtc.AddMinutes(10);
        var candidate = CreateSkippedRun(strategyId, boundaryUtc.AddSeconds(1)) with
        {
            SkipReason = "live_archive_serialization"
        };
        var archiveApplicationName = $"v{archiveVersion}_archive_{Guid.NewGuid():N}";
        var liveApplicationName = $"v{archiveVersion}_live_{Guid.NewGuid():N}";
        var archiveFactory = WithApplicationName(factory, archiveApplicationName);
        var liveFactory = WithApplicationName(factory, liveApplicationName);
        using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<IReadOnlySet<Guid>>? archiveTask = null;
        Task<bool>? liveTask = null;

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            if (liveEnableWins)
            {
                await using var liveConnection = liveFactory.CreateConnection();
                await liveConnection.OpenAsync();
                await using var liveTransaction = await liveConnection.BeginTransactionAsync();
                await using (var liveCommand = new NpgsqlCommand(
                                 """
UPDATE public.strategies
SET live_stakes = true,
    live_enabled_at_utc = @BoundaryUtc,
    updated_at_utc = @BoundaryUtc
WHERE id = @StrategyId;
""",
                                 liveConnection,
                                 liveTransaction))
                {
                    liveCommand.Parameters.AddWithValue("BoundaryUtc", boundaryUtc.UtcDateTime);
                    liveCommand.Parameters.AddWithValue("StrategyId", strategyId);
                    Assert.Equal(1, await liveCommand.ExecuteNonQueryAsync());
                }

                archiveTask = ArchiveTerminalSkipForVersionAsync(
                    archiveFactory,
                    archiveVersion,
                    candidate,
                    raceCancellation.Token);
                var archivePid = await WaitForBlockedApplicationAsync(
                    factory,
                    archiveApplicationName,
                    "advisory");
                await AssertBlockedByAsync(factory, archivePid, liveConnection.ProcessID);

                await liveTransaction.CommitAsync();
                Assert.Equal(
                    [candidate.Id],
                    (await archiveTask.WaitAsync(TimeSpan.FromSeconds(15))).ToArray());
            }
            else
            {
                var anchor = CreateSkippedRun(strategyId, boundaryUtc.AddMinutes(-5)) with
                {
                    SkipReason = candidate.SkipReason
                };
                Assert.Equal(
                    [anchor.Id],
                    (await new PostgresAppRepository(factory)
                        .TryAddStrategyMarketPaperRunsAsync(
                            [anchor],
                            directPaperSkipCompactionEnabled: true))
                    .ToArray());

                await using var blockerConnection = factory.CreateConnection();
                await blockerConnection.OpenAsync();
                await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
                await LockRollupGroupAsync(
                    blockerConnection,
                    blockerTransaction,
                    strategyId,
                    anchor.UpdatedAtUtc,
                    anchor.SkipReason!);

                archiveTask = ArchiveTerminalSkipForVersionAsync(
                    archiveFactory,
                    archiveVersion,
                    candidate,
                    raceCancellation.Token);
                var archivePid = await WaitForBlockedApplicationAsync(
                    factory,
                    archiveApplicationName,
                    "transactionid");
                await AssertBlockedByAsync(factory, archivePid, blockerConnection.ProcessID);
                Assert.True(await HoldsExclusiveRetentionGateAsync(factory, archivePid));

                liveTask = new PostgresAppRepository(liveFactory).SetStrategyLiveStakesAsync(
                    strategyId,
                    liveStakes: true,
                    updatedAtUtc: boundaryUtc,
                    cancellationToken: raceCancellation.Token);
                var livePid = await WaitForBlockedApplicationAsync(
                    factory,
                    liveApplicationName,
                    "advisory");
                await AssertBlockedByAsync(factory, livePid, archivePid);

                await blockerTransaction.CommitAsync();
                Assert.Equal(
                    [candidate.Id],
                    (await archiveTask.WaitAsync(TimeSpan.FromSeconds(15))).ToArray());
                Assert.True(await liveTask.WaitAsync(TimeSpan.FromSeconds(15)));

                var remainingArchives = await ReadArchiveStorageVersionsAsync(factory, strategyId);
                var remaining = Assert.Single(remainingArchives);
                Assert.Equal(anchor.Id, remaining.Key);
                Assert.Equal(1, remaining.Value);
                var rollup = await ReadRollupGroupAsync(
                    factory,
                    strategyId,
                    anchor.UpdatedAtUtc,
                    anchor.SkipReason!);
                Assert.Equal(1, rollup.RunCount);
                Assert.Equal(anchor.UpdatedAtUtc, rollup.FirstUpdatedAtUtc);
                Assert.Equal(anchor.UpdatedAtUtc, rollup.LastUpdatedAtUtc);
            }

            Assert.Equal(
                StrategyMarketPaperRunStatuses.Skipped,
                await ReadRunStatusAsync(factory, candidate.Id));
            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, candidate.Id));
            Assert.DoesNotContain(
                candidate.Id,
                (await ReadArchiveStorageVersionsAsync(factory, strategyId)).Keys);
        }
        finally
        {
            raceCancellation.Cancel();
            await DrainRaceTaskAsync(liveTask);
            await DrainRaceTaskAsync(archiveTask);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    private static Task<IReadOnlySet<Guid>> ArchiveTerminalSkipForVersionAsync(
        PostgresConnectionFactory factory,
        short archiveVersion,
        StrategyMarketPaperRun run,
        CancellationToken cancellationToken)
    {
        var repository = new PostgresAppRepository(factory);
        return archiveVersion switch
        {
            1 => repository.TryAddStrategyMarketPaperRunsAsync(
                [run],
                directPaperSkipCompactionEnabled: true,
                cancellationToken: cancellationToken),
            2 => repository.TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(
                [run],
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(archiveVersion),
                archiveVersion,
                "Only archive versions 1 and 2 are supported by this test.")
        };
    }

    private static async Task AssertStrategyCodePositionRaceAsync(bool codeUpdateFirst)
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldCode = $"retention_mapping_old_{Guid.NewGuid():N}";
        var newCode = $"retention_mapping_new_{Guid.NewGuid():N}";
        var conditionId = $"retention-mapping-race-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var run = WithRetentionKey(CreateSkippedRun(strategyId, oldUtc), conditionId);
        var positionId = Guid.NewGuid();
        var position = new PaperPosition(
            $"asset-{Guid.NewGuid():N}",
            conditionId,
            "Yes",
            2m,
            0.50m,
            1m,
            0m,
            oldUtc,
            $"strategy:{newCode}");
        var ownerApplicationName = $"mapping_owner_{Guid.NewGuid():N}";
        var blockedApplicationName = $"mapping_blocked_{Guid.NewGuid():N}";
        var ownerFactory = WithApplicationName(factory, ownerApplicationName);
        var blockedFactory = WithApplicationName(factory, blockedApplicationName);
        using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<int>? blockedTask = null;

        await InsertStrategyAsync(factory, strategyId, oldCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            var originalPayload = await ReadRunPayloadWithoutScopeAsync(factory, run.Id);
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);
            await DeleteProjectionBlockersAsync(factory, strategyId);

            if (codeUpdateFirst)
            {
                await using var ownerConnection = ownerFactory.CreateConnection();
                await ownerConnection.OpenAsync();
                await using var ownerTransaction = await ownerConnection.BeginTransactionAsync();
                var ownerCommitted = false;
                try
                {
                    Assert.Equal(1, await UpdateStrategyCodeAsync(
                        ownerConnection,
                        ownerTransaction,
                        strategyId,
                        newCode));
                    blockedTask = InsertPaperPositionAsync(
                        blockedFactory,
                        positionId,
                        position,
                        raceCancellation.Token);
                    var blockedPid = await WaitForBlockedApplicationAsync(
                        factory,
                        blockedApplicationName,
                        "advisory");
                    await AssertBlockedByAsync(factory, blockedPid, ownerConnection.ProcessID);

                    await ownerTransaction.CommitAsync();
                    ownerCommitted = true;
                    Assert.Equal(1, await blockedTask.WaitAsync(TimeSpan.FromSeconds(15)));
                }
                finally
                {
                    if (!ownerCommitted)
                    {
                        await ownerTransaction.RollbackAsync(CancellationToken.None);
                    }

                    raceCancellation.Cancel();
                    await DrainRaceTaskAsync(blockedTask);
                }
            }
            else
            {
                await using var ownerConnection = ownerFactory.CreateConnection();
                await ownerConnection.OpenAsync();
                await using var ownerTransaction = await ownerConnection.BeginTransactionAsync();
                var ownerCommitted = false;
                try
                {
                    Assert.Equal(1, await InsertPaperPositionAsync(
                        ownerConnection,
                        ownerTransaction,
                        positionId,
                        position,
                        CancellationToken.None));
                    blockedTask = UpdateStrategyCodeAsync(
                        blockedFactory,
                        strategyId,
                        newCode,
                        raceCancellation.Token);
                    var blockedPid = await WaitForBlockedApplicationAsync(
                        factory,
                        blockedApplicationName,
                        "advisory");
                    await AssertBlockedByAsync(factory, blockedPid, ownerConnection.ProcessID);

                    await ownerTransaction.CommitAsync();
                    ownerCommitted = true;
                    Assert.Equal(1, await blockedTask.WaitAsync(TimeSpan.FromSeconds(15)));
                }
                finally
                {
                    if (!ownerCommitted)
                    {
                        await ownerTransaction.RollbackAsync(CancellationToken.None);
                    }

                    raceCancellation.Cancel();
                    await DrainRaceTaskAsync(blockedTask);
                }
            }

            Assert.Equal(originalPayload, await ReadRunPayloadWithoutScopeAsync(factory, run.Id));
            Assert.Equal(StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, run.Id));
            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((1L, 0L, 0L, 1L),
                (counts.RawRuns, counts.RollupRuns,
                    counts.Tombstones, counts.ReconciliationQueueRows));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, run.Id));
            Assert.Contains("paper_position_dependency", await ReadRunBlockersAsync(factory, run.Id));
        }
        finally
        {
            raceCancellation.Cancel();
            await DrainRaceTaskAsync(blockedTask);
            await DeleteConditionDependenciesAsync(factory, conditionId);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    private static async Task<int> UpdateStrategyCodeAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        string code,
        CancellationToken cancellationToken)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await UpdateStrategyCodeAsync(
            connection,
            null,
            strategyId,
            code,
            cancellationToken);
    }

    private static async Task<int> UpdateStrategyCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid strategyId,
        string code,
        CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE public.strategies SET code = @Code, updated_at_utc = clock_timestamp() " +
            "WHERE id = @StrategyId;",
            connection,
            transaction);
        command.Parameters.AddWithValue("Code", code);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> InsertPaperPositionAsync(
        PostgresConnectionFactory factory,
        Guid positionId,
        PaperPosition position,
        CancellationToken cancellationToken)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await InsertPaperPositionAsync(
            connection,
            null,
            positionId,
            position,
            cancellationToken);
    }

    private static async Task<int> InsertPaperPositionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid positionId,
        PaperPosition position,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.paper_positions (
    id, copied_trader_wallet, asset_id, condition_id, outcome,
    size_shares, average_price, estimated_value_usd,
    unrealized_pnl_usd, updated_at_utc)
VALUES (
    @Id, @CopiedTraderWallet, @AssetId, @ConditionId, @Outcome,
    @SizeShares, @AveragePrice, @EstimatedValueUsd,
    @UnrealizedPnlUsd, @UpdatedAtUtc);
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("Id", positionId);
        command.Parameters.AddWithValue("CopiedTraderWallet", position.CopiedTraderWallet);
        command.Parameters.AddWithValue("AssetId", position.AssetId);
        command.Parameters.AddWithValue("ConditionId", position.ConditionId);
        command.Parameters.AddWithValue("Outcome", position.Outcome);
        command.Parameters.AddWithValue("SizeShares", position.SizeShares);
        command.Parameters.AddWithValue("AveragePrice", position.AveragePrice);
        command.Parameters.AddWithValue("EstimatedValueUsd", position.EstimatedValueUsd);
        command.Parameters.AddWithValue("UnrealizedPnlUsd", position.UnrealizedPnlUsd);
        command.Parameters.AddWithValue("UpdatedAtUtc", position.UpdatedAtUtc.UtcDateTime);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockRollupGroupAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        DateTimeOffset updatedAtUtc,
        string skipReason)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT run_count
FROM public.strategy_paper_skip_rollups
WHERE strategy_id = @StrategyId
  AND bucket_start_utc =
      date_trunc('day', @UpdatedAtUtc::timestamptz AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
  AND skip_reason = @SkipReason
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("SkipReason", skipReason);
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private static async Task<int> WaitForBlockedApplicationAsync(
        PostgresConnectionFactory factory,
        string applicationName,
        string waitEvent)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            await using var connection = factory.CreateConnection();
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
SELECT pid
FROM pg_stat_activity
WHERE application_name = @ApplicationName
  AND state = 'active'
  AND wait_event_type = 'Lock'
  AND lower(COALESCE(wait_event, '')) = lower(@WaitEvent)
LIMIT 1;
""",
                connection);
            command.Parameters.AddWithValue("ApplicationName", applicationName);
            command.Parameters.AddWithValue("WaitEvent", waitEvent);
            var result = await command.ExecuteScalarAsync();
            if (result is int pid)
            {
                return pid;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(
            $"PostgreSQL application {applicationName} did not wait on {waitEvent} within 10 seconds.");
    }

    private static async Task AssertBlockedByAsync(
        PostgresConnectionFactory factory,
        int blockedPid,
        int blockerPid)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT @BlockerPid = ANY(pg_blocking_pids(@BlockedPid));",
            connection);
        command.Parameters.AddWithValue("BlockedPid", blockedPid);
        command.Parameters.AddWithValue("BlockerPid", blockerPid);
        Assert.True((bool)(await command.ExecuteScalarAsync() ?? false));
    }

    private static async Task DrainRaceTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (OperationCanceledException)
        {
        }
        catch (PostgresException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (TimeoutException)
        {
        }
    }

    private static async Task<bool> HoldsExclusiveRetentionGateAsync(
        PostgresConnectionFactory factory,
        int pid)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT EXISTS (
    SELECT 1
    FROM pg_locks
    WHERE pid = @Pid
      AND locktype = 'advisory'
      AND classid = 1346589778
      AND objid = 1
      AND mode = 'ExclusiveLock'
      AND granted);
""",
            connection);
        command.Parameters.AddWithValue("Pid", pid);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task AssertRunRowIsLockedAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM public.strategy_market_paper_runs " +
            "WHERE id = @RunId FOR UPDATE NOWAIT;",
            connection,
            transaction);
        command.Parameters.AddWithValue("RunId", runId);
        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
        await transaction.RollbackAsync();
    }

    private static async Task<RollupGroup> ReadRollupGroupAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        DateTimeOffset updatedAtUtc,
        string skipReason)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT run_count, first_updated_at_utc, last_updated_at_utc
FROM public.strategy_paper_skip_rollups
WHERE strategy_id = @StrategyId
  AND bucket_start_utc =
      date_trunc('day', @UpdatedAtUtc::timestamptz AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
  AND skip_reason = @SkipReason;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("SkipReason", skipReason);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new RollupGroup(
            reader.GetInt32(0),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)));
    }

    private static async Task<Guid[]> ReadArchivedRunIdsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT archived_run_id FROM public.strategy_market_paper_skip_archive_rows " +
            "WHERE strategy_id = @StrategyId ORDER BY archived_run_id;",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetGuid(0));
        }

        return results.ToArray();
    }

    private static async Task<string[]> ReadRunBlockersAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT public.strategy_market_paper_run_retention_blockers(run) " +
            "FROM public.strategy_market_paper_runs run WHERE run.id = @RunId;",
            connection);
        command.Parameters.AddWithValue("RunId", runId);
        return (string[])(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Strategy run blockers were not found."));
    }

    private static async Task<long> ReadStrategyRunProjectionEventCountAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM public.dashboard_projection_events " +
            "WHERE source_kind = 'StrategyRun' AND source_id = @RunId;",
            connection);
        command.Parameters.AddWithValue("RunId", runId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<ProjectionEventPayload> ReadStrategyRunProjectionEventAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT operation,
       old_payload::text,
       new_payload::text
FROM public.dashboard_projection_events
WHERE source_kind = 'StrategyRun'
  AND source_id = @RunId;
""",
            connection);
        command.Parameters.AddWithValue("RunId", runId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new ProjectionEventPayload(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task<ReconciliationQueueVersion> ReadReconciliationQueueVersionAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT xmin::text,
       ctid::text,
       priority,
       reason,
       attempt_count,
       requested_at_utc,
       next_attempt_at_utc,
       last_error
FROM public.dashboard_projection_reconciliation_queue
WHERE strategy_id = @StrategyId;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new ReconciliationQueueVersion(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetInt32(4),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)),
            reader.IsDBNull(7) ? null : reader.GetString(7));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task<int> InsertPaperOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        PaperOrder order)
    {
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side,
    asset_id, condition_id, outcome, price, size_shares, notional_usd,
    created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc,
    raw_decision_json, correlation_id, execution_source)
VALUES (
    @Id, @SignalId, @StrategyId, @CopiedTraderWallet, @Status, @Side,
    @AssetId, @ConditionId, @Outcome, @Price, @SizeShares, @NotionalUsd,
    @CreatedAtUtc, @ExpiresAtUtc, @FilledAtUtc, @CancelledAtUtc,
    CAST(@RawDecisionJson AS jsonb), @CorrelationId, @ExecutionSource)
ON CONFLICT (id) DO NOTHING;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("Id", order.Id);
        command.Parameters.AddWithValue("SignalId", order.SignalId);
        command.Parameters.AddWithValue("StrategyId", order.StrategyId);
        command.Parameters.AddWithValue("CopiedTraderWallet", order.CopiedTraderWallet);
        command.Parameters.AddWithValue("Status", order.Status.ToString());
        command.Parameters.AddWithValue("Side", order.Side.ToString());
        command.Parameters.AddWithValue("AssetId", order.AssetId);
        command.Parameters.AddWithValue("ConditionId", order.ConditionId);
        command.Parameters.AddWithValue("Outcome", order.Outcome);
        command.Parameters.AddWithValue("Price", order.Price);
        command.Parameters.AddWithValue("SizeShares", order.SizeShares);
        command.Parameters.AddWithValue("NotionalUsd", order.NotionalUsd);
        command.Parameters.AddWithValue("CreatedAtUtc", order.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("ExpiresAtUtc", order.ExpiresAtUtc.UtcDateTime);
        command.Parameters.Add("FilledAtUtc", NpgsqlDbType.TimestampTz).Value =
            order.FilledAtUtc is null ? DBNull.Value : order.FilledAtUtc.Value.UtcDateTime;
        command.Parameters.Add("CancelledAtUtc", NpgsqlDbType.TimestampTz).Value =
            order.CancelledAtUtc is null ? DBNull.Value : order.CancelledAtUtc.Value.UtcDateTime;
        command.Parameters.AddWithValue("RawDecisionJson", order.RawDecisionJson ?? "{}");
        command.Parameters.Add("CorrelationId", NpgsqlDbType.Uuid).Value =
            order.CorrelationId is null ? DBNull.Value : order.CorrelationId.Value;
        command.Parameters.AddWithValue("ExecutionSource", order.ExecutionSource);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> InsertConflictingPaperOrderAsync(
        PostgresConnectionFactory factory,
        PaperOrder existingOrder,
        string conflictingConditionId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        return await InsertPaperOrderAsync(
            connection,
            null,
            existingOrder with { ConditionId = conflictingConditionId });
    }

    private static async Task<long> ReadRunCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM public.strategy_market_paper_runs WHERE id = @RunId;",
            connection,
            transaction);
        command.Parameters.AddWithValue("RunId", runId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<long> ReadTombstoneCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM public.strategy_market_paper_skip_tombstones " +
            "WHERE archived_run_id = @RunId;",
            connection,
            transaction);
        command.Parameters.AddWithValue("RunId", runId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<long> ReadPaperOrderCountAsync(
        PostgresConnectionFactory factory,
        Guid orderId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM public.paper_orders WHERE id = @OrderId;",
            connection);
        command.Parameters.AddWithValue("OrderId", orderId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<PaperHistoryCounts> ReadPaperHistoryCountsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*) FROM paper_orders WHERE strategy_id = @StrategyId),
    (SELECT count(*)
     FROM paper_fills fill_row
     INNER JOIN paper_orders order_row ON order_row.id = fill_row.paper_order_id
     WHERE order_row.strategy_id = @StrategyId),
    (SELECT count(*)
     FROM paper_positions position_row
     INNER JOIN strategies strategy
         ON lower(position_row.copied_trader_wallet) = lower('strategy:' || strategy.code)
     WHERE strategy.id = @StrategyId),
    (SELECT count(*)
     FROM paper_position_settlements settlement
     INNER JOIN strategies strategy
         ON lower(settlement.copied_trader_wallet) = lower('strategy:' || strategy.code)
     WHERE strategy.id = @StrategyId);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PaperHistoryCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    private static async Task InsertPaperOrderDependenciesAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        DateTimeOffset createdAtUtc,
        string[] conditionIds)
    {
        if (conditionIds.Length == 0)
        {
            return;
        }

        var orderIds = conditionIds.Select(_ => Guid.NewGuid()).ToArray();
        var signalIds = conditionIds.Select(_ => Guid.NewGuid()).ToArray();
        var assetIds = conditionIds.Select(_ => $"asset-{Guid.NewGuid():N}").ToArray();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side,
    asset_id, condition_id, outcome, price, size_shares, notional_usd,
    created_at_utc, expires_at_utc, filled_at_utc, execution_source)
SELECT
    dependency.id,
    dependency.signal_id,
    @StrategyId,
    @Wallet,
    'Filled',
    'Buy',
    dependency.asset_id,
    dependency.condition_id,
    'Yes',
    0.50,
    2,
    1,
    @CreatedAtUtc,
    @CreatedAtUtc + interval '5 minutes',
    @CreatedAtUtc + interval '1 minute',
    'retention_integration_benchmark'
FROM unnest(@OrderIds, @SignalIds, @AssetIds, @ConditionIds)
    AS dependency(id, signal_id, asset_id, condition_id);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("Wallet", $"strategy-retention-{strategyId:N}");
        command.Parameters.AddWithValue("CreatedAtUtc", createdAtUtc.UtcDateTime);
        command.Parameters.Add("OrderIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = orderIds;
        command.Parameters.Add("SignalIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = signalIds;
        command.Parameters.Add("AssetIds", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = assetIds;
        command.Parameters.Add("ConditionIds", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = conditionIds;
        Assert.Equal(conditionIds.Length, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteConditionDependenciesAsync(
        PostgresConnectionFactory factory,
        string conditionPrefix)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
DELETE FROM public.paper_copied_leader_activity_events WHERE condition_id LIKE @ConditionPattern;
DELETE FROM public.paper_copied_leader_positions WHERE condition_id LIKE @ConditionPattern;
DELETE FROM public.polymarket_onchain_paper_signal_results WHERE condition_id LIKE @ConditionPattern;
DELETE FROM public.paper_positions WHERE condition_id LIKE @ConditionPattern;
DELETE FROM public.paper_position_settlements WHERE condition_id LIKE @ConditionPattern;
""",
            connection);
        command.Parameters.AddWithValue("ConditionPattern", $"{conditionPrefix}%");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertStrategyAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        string strategyCode,
        bool liveStakes)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO strategies (
    id, code, name, description, enabled, live_stakes,
    live_enabled_at_utc, created_at_utc, updated_at_utc)
VALUES (
    @Id, @Code, @Name, 'retention integration test', true, @LiveStakes,
    CASE WHEN @LiveStakes THEN clock_timestamp() ELSE NULL END,
    clock_timestamp(), clock_timestamp());
""",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        command.Parameters.AddWithValue("Code", strategyCode);
        command.Parameters.AddWithValue("Name", strategyCode);
        command.Parameters.AddWithValue("LiveStakes", liveStakes);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetStrategyLiveStakesAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        bool liveStakes)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE strategies
SET live_stakes = @LiveStakes,
    live_enabled_at_utc = CASE WHEN @LiveStakes THEN clock_timestamp() ELSE NULL END,
    updated_at_utc = clock_timestamp()
WHERE id = @Id;
""",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        command.Parameters.AddWithValue("LiveStakes", liveStakes);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task TryDemoteRetentionScopeAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE strategy_market_paper_runs SET retention_scope = 'PaperOnly' WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetRunRetentionScopeAsync(
        PostgresConnectionFactory factory,
        Guid runId,
        string retentionScope)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE strategy_market_paper_runs SET retention_scope = @RetentionScope WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        command.Parameters.AddWithValue("RetentionScope", retentionScope);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string> ReadRetentionScopeAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT retention_scope FROM strategy_market_paper_runs WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Strategy run was not found."));
    }

    private static async Task<string?> ReadRunStatusAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT status FROM strategy_market_paper_runs WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task AddSkipDiagnosticsAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE strategy_market_paper_runs SET skip_diagnostics_json = '{}'::jsonb WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeletePaperPositionAsync(
        PostgresConnectionFactory factory,
        string copiedTraderWallet,
        string assetId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM paper_positions " +
            "WHERE copied_trader_wallet = @CopiedTraderWallet AND asset_id = @AssetId;",
            connection);
        command.Parameters.AddWithValue("CopiedTraderWallet", copiedTraderWallet);
        command.Parameters.AddWithValue("AssetId", assetId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteProjectionBlockersAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_projection_events WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_recent_projection_facts WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @StrategyId;",
            strategyId);
    }

    private static async Task<RetentionCounts> ReadRetentionCountsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id = @StrategyId),
    (SELECT COALESCE(sum(run_count), 0) FROM strategy_paper_skip_rollups WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM strategy_market_paper_skip_archive_rows WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM dashboard_projection_events WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @StrategyId);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new RetentionCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    private static async Task<StrategyPerformance> ReadDashboardSnapshotAsync(
        PostgresDashboardSnapshotRepository snapshots,
        Guid strategyId)
    {
        return (await snapshots.GetStrategyPerformanceSnapshotAsync())
            .Single(row => row.StrategyId == strategyId);
    }

    private static void AssertStrategyLifetimeMetricsEqual(
        StrategyPerformance expected,
        StrategyPerformance actual)
    {
        Assert.Equal(expected.OrdersCount, actual.OrdersCount);
        Assert.Equal(expected.FilledOrdersCount, actual.FilledOrdersCount);
        Assert.Equal(expected.OpenOrdersCount, actual.OpenOrdersCount);
        Assert.Equal(expected.OpenPositionsCount, actual.OpenPositionsCount);
        Assert.Equal(expected.ObservedRunsCount, actual.ObservedRunsCount);
        Assert.Equal(expected.EnteredRunsCount, actual.EnteredRunsCount);
        Assert.Equal(expected.SkippedRunsCount, actual.SkippedRunsCount);
        Assert.Equal(expected.PaperConditionSkippedRunsCount, actual.PaperConditionSkippedRunsCount);
        Assert.Equal(expected.PaperNotAcceptedRunsCount, actual.PaperNotAcceptedRunsCount);
        Assert.Equal(expected.SettledRunsCount, actual.SettledRunsCount);
        Assert.Equal(expected.StakeUsd, actual.StakeUsd);
        Assert.Equal(expected.RealizedPnlUsd, actual.RealizedPnlUsd);
        Assert.Equal(expected.UnrealizedPnlUsd, actual.UnrealizedPnlUsd);
        Assert.Equal(expected.TotalPnlUsd, actual.TotalPnlUsd);
        Assert.Equal(expected.LastOrderUtc, actual.LastOrderUtc);
        Assert.Equal(expected.LastRunUtc, actual.LastRunUtc);
    }

    private static async Task DeleteTestStrategyAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await DeleteProjectionBlockersAsync(factory, strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM paper_live_shadow_decisions WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM live_orders WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dry_run_orders WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            """
DELETE FROM paper_fills fill_row
USING paper_orders order_row
WHERE fill_row.paper_order_id = order_row.id
  AND order_row.strategy_id = @StrategyId;
""",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_lifetime_projection_states WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_recent_projection_states WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_performance_snapshots WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_recent_performance_snapshots WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_market_paper_runs WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM paper_orders WHERE strategy_id = @StrategyId;",
            strategyId);
        await DeleteProjectionBlockersAsync(factory, strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_paper_skip_rollups WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_market_paper_skip_tombstones WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_live_retention_guards WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            """
DELETE FROM paper_copied_trader_performance_refresh_queue
WHERE lower(copied_trader_wallet) IN (
    lower('strategy-retention-' || replace(@StrategyId::text, '-', '')),
    lower(COALESCE((SELECT 'strategy:' || code FROM strategies WHERE id = @StrategyId), '')));
""",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            """
DELETE FROM paper_copied_trader_performance_refresh_inflight
WHERE lower(copied_trader_wallet) IN (
    lower('strategy-retention-' || replace(@StrategyId::text, '-', '')),
    lower(COALESCE((SELECT 'strategy:' || code FROM strategies WHERE id = @StrategyId), '')));
""",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            """
DELETE FROM paper_copied_trader_performance
WHERE lower(copied_trader_wallet) IN (
    lower('strategy-retention-' || replace(@StrategyId::text, '-', '')),
    lower(COALESCE((SELECT 'strategy:' || code FROM strategies WHERE id = @StrategyId), '')));
""",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategies WHERE id = @StrategyId;",
            strategyId);
        await DeleteProjectionBlockersAsync(factory, strategyId);
    }

    private static PaperOrder CreatePaperOrder(
        Guid strategyId,
        string conditionId,
        DateTimeOffset createdAtUtc)
    {
        return new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"strategy-retention-{strategyId:N}",
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            $"asset-{Guid.NewGuid():N}",
            conditionId,
            "Yes",
            0.50m,
            2m,
            1m,
            createdAtUtc,
            createdAtUtc.AddMinutes(5),
            FilledAtUtc: createdAtUtc.AddMinutes(1),
            StrategyId: strategyId,
            ExecutionSource: "retention_integration_test");
    }

    private static PaperPositionSettlement CreateSettlement(
        string copiedTraderWallet,
        string conditionId,
        DateTimeOffset createdAtUtc)
    {
        var assetId = $"asset-{Guid.NewGuid():N}";
        return new PaperPositionSettlement(
            Guid.NewGuid(),
            copiedTraderWallet,
            assetId,
            conditionId,
            "Yes",
            assetId,
            "Yes",
            "IntegrationTest",
            2m,
            0.50m,
            1m,
            2m,
            1m,
            true,
            "IntegrationTest",
            createdAtUtc,
            createdAtUtc);
    }

    private static async Task ExecuteForStrategyAsync(
        PostgresConnectionFactory factory,
        string sql,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await command.ExecuteNonQueryAsync();
    }

    private enum V2WriterKind
    {
        TerminalAtInsert,
        ExistingRawFinalize,
        AgeBased
    }

    private enum DirectExternalDependencyKind
    {
        DryRunOrder,
        CopiedLeaderPosition,
        CopiedLeaderActivity,
        OnchainPaperSignalResult
    }

    private sealed record V2DimensionReferenceCounts(
        long MarketIdentities,
        long MetadataVersions,
        long Reasons,
        long Tombstones);

    private const string PrototypeCandidateLayoutsSql =
        """
CREATE TABLE v1_tombstones (
    strategy_id uuid NOT NULL,
    market_id text NOT NULL,
    archived_run_id uuid NOT NULL,
    archived_at_utc timestamptz NOT NULL,
    archive_format_version smallint NULL,
    condition_id text NULL,
    market_slug text NULL,
    market_title text NULL,
    category text NULL,
    market_start_utc timestamptz NULL,
    market_end_utc timestamptz NULL,
    detected_at_utc timestamptz NULL,
    entry_due_at_utc timestamptz NULL,
    selected_asset_id text NULL,
    selected_outcome text NULL,
    stake_usd numeric(28,8) NULL,
    skip_reason text NULL,
    run_created_at_utc timestamptz NULL,
    run_updated_at_utc timestamptz NULL,
    rollup_bucket_start_utc timestamptz NULL,
    PRIMARY KEY (strategy_id, market_id),
    UNIQUE (archived_run_id)
);
CREATE INDEX v1_condition_strategy_idx
ON v1_tombstones(condition_id, strategy_id)
WHERE archive_format_version = 1;
CREATE INDEX v1_rollup_group_idx
ON v1_tombstones(strategy_id, rollup_bucket_start_utc, skip_reason, run_updated_at_utc)
WHERE archive_format_version = 1;
CREATE INDEX v1_strategy_recent_idx
ON v1_tombstones(strategy_id, run_updated_at_utc, archived_run_id)
WHERE archive_format_version = 1;
CREATE INDEX v1_global_recent_idx
ON v1_tombstones(run_updated_at_utc, strategy_id, archived_run_id)
WHERE archive_format_version = 1;
CREATE INDEX v1_incomplete_idx
ON v1_tombstones(strategy_id, market_id)
WHERE archive_format_version IS DISTINCT FROM 1;

CREATE TABLE same_market_identities (
    market_identity_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    market_id text COLLATE "C" NOT NULL UNIQUE
);
CREATE TABLE same_metadata_versions (
    metadata_version_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    market_identity_id integer NOT NULL REFERENCES same_market_identities,
    condition_id text COLLATE "C" NOT NULL,
    market_slug text COLLATE "C" NOT NULL,
    market_title text COLLATE "C" NOT NULL,
    category text COLLATE "C" NULL,
    market_start_utc timestamptz NULL,
    market_end_utc timestamptz NULL,
    UNIQUE (metadata_version_id, market_identity_id),
    UNIQUE NULLS NOT DISTINCT (
        market_identity_id, condition_id, market_slug, market_title,
        category, market_start_utc, market_end_utc)
);
CREATE TABLE same_reasons (
    skip_reason_id smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    skip_reason text COLLATE "C" NOT NULL UNIQUE
);
CREATE TABLE same_tombstones (
    strategy_id uuid NOT NULL,
    market_id text NULL,
    market_identity_id integer NULL REFERENCES same_market_identities,
    metadata_version_id integer NULL,
    archived_run_id uuid NOT NULL,
    archived_at_utc timestamptz NULL,
    archive_format_version smallint NULL,
    condition_id text NULL,
    market_slug text NULL,
    market_title text NULL,
    category text NULL,
    market_start_utc timestamptz NULL,
    market_end_utc timestamptz NULL,
    detected_at_utc timestamptz NULL,
    entry_due_at_utc timestamptz NULL,
    selected_asset_id text NULL,
    selected_outcome text NULL,
    stake_usd numeric(28,8) NULL,
    skip_reason text NULL,
    skip_reason_id smallint NULL REFERENCES same_reasons,
    run_created_at_utc timestamptz NULL,
    run_updated_at_utc timestamptz NULL,
    rollup_bucket_start_utc timestamptz NULL,
    UNIQUE (archived_run_id),
    FOREIGN KEY (metadata_version_id, market_identity_id)
        REFERENCES same_metadata_versions(metadata_version_id, market_identity_id),
    CHECK (
        (archive_format_version IS NULL
            AND market_id IS NOT NULL
            AND archived_at_utc IS NOT NULL)
        OR
        (archive_format_version = 1
            AND market_id IS NOT NULL
            AND archived_at_utc IS NOT NULL
            AND condition_id IS NOT NULL
            AND market_slug IS NOT NULL
            AND market_title IS NOT NULL
            AND detected_at_utc IS NOT NULL
            AND entry_due_at_utc IS NOT NULL
            AND stake_usd IS NOT NULL
            AND NULLIF(btrim(COALESCE(skip_reason, '')), '') IS NOT NULL
            AND run_created_at_utc IS NOT NULL
            AND run_updated_at_utc IS NOT NULL
            AND rollup_bucket_start_utc IS NOT NULL)
        OR
        (archive_format_version = 2 AND market_identity_id IS NOT NULL
            AND metadata_version_id IS NOT NULL
            AND detected_at_utc IS NOT NULL
            AND entry_due_at_utc IS NOT NULL
            AND stake_usd IS NOT NULL
            AND skip_reason_id IS NOT NULL
            AND run_updated_at_utc IS NOT NULL))
);
CREATE UNIQUE INDEX same_v1_identity_idx
ON same_tombstones(strategy_id, market_id)
WHERE archive_format_version IS DISTINCT FROM 2;
CREATE UNIQUE INDEX same_v2_identity_idx
ON same_tombstones(strategy_id, market_identity_id)
WHERE archive_format_version = 2;
CREATE INDEX same_v1_condition_idx
ON same_tombstones(condition_id, strategy_id)
WHERE archive_format_version = 1;
CREATE INDEX same_v1_rollup_idx
ON same_tombstones(strategy_id, rollup_bucket_start_utc, skip_reason, run_updated_at_utc)
WHERE archive_format_version = 1;
CREATE INDEX same_v1_strategy_recent_idx
ON same_tombstones(strategy_id, run_updated_at_utc, archived_run_id)
WHERE archive_format_version = 1;
CREATE INDEX same_v1_global_recent_idx
ON same_tombstones(run_updated_at_utc, strategy_id, archived_run_id)
WHERE archive_format_version = 1;
CREATE INDEX same_metadata_condition_idx
ON same_metadata_versions(condition_id, metadata_version_id, market_identity_id);
CREATE INDEX same_v2_metadata_strategy_idx
ON same_tombstones(metadata_version_id, strategy_id, archived_run_id)
WHERE archive_format_version = 2;
CREATE INDEX same_v2_rollup_idx
ON same_tombstones(
    strategy_id, ((run_updated_at_utc AT TIME ZONE 'UTC')::date),
    skip_reason_id, run_updated_at_utc, archived_run_id)
WHERE archive_format_version = 2;
CREATE INDEX same_v2_strategy_recent_idx
ON same_tombstones(strategy_id, run_updated_at_utc DESC, archived_run_id)
INCLUDE (stake_usd, entry_due_at_utc, skip_reason_id)
WHERE archive_format_version = 2;
CREATE INDEX same_v2_global_recent_idx
ON same_tombstones(run_updated_at_utc DESC, strategy_id, archived_run_id)
INCLUDE (stake_usd, entry_due_at_utc, skip_reason_id)
WHERE archive_format_version = 2;
CREATE INDEX same_incomplete_idx
ON same_tombstones(strategy_id, market_id)
WHERE archive_format_version IS DISTINCT FROM 1
  AND archive_format_version IS DISTINCT FROM 2;

CREATE TABLE per_row_tombstones (
    strategy_id uuid NOT NULL,
    market_id text COLLATE "C" NOT NULL,
    archived_run_id uuid NOT NULL,
    condition_id text COLLATE "C" NOT NULL,
    market_slug text COLLATE "C" NOT NULL,
    market_title text COLLATE "C" NOT NULL,
    category text COLLATE "C" NULL,
    market_start_utc timestamptz NULL,
    market_end_utc timestamptz NULL,
    detected_at_utc timestamptz NOT NULL,
    entry_due_at_utc timestamptz NOT NULL,
    selected_asset_id text NULL,
    selected_outcome text NULL,
    stake_usd numeric(28,8) NOT NULL,
    skip_reason text COLLATE "C" NOT NULL,
    run_updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (strategy_id, market_id),
    UNIQUE (archived_run_id)
);
CREATE INDEX per_row_condition_idx
ON per_row_tombstones(condition_id, strategy_id, archived_run_id);
CREATE INDEX per_row_rollup_idx
ON per_row_tombstones(
    strategy_id, ((run_updated_at_utc AT TIME ZONE 'UTC')::date),
    skip_reason, run_updated_at_utc, archived_run_id);
CREATE INDEX per_row_strategy_recent_idx
ON per_row_tombstones(strategy_id, run_updated_at_utc DESC, archived_run_id)
INCLUDE (stake_usd, entry_due_at_utc, skip_reason);
CREATE INDEX per_row_global_recent_idx
ON per_row_tombstones(run_updated_at_utc DESC, strategy_id, archived_run_id)
INCLUDE (stake_usd, entry_due_at_utc, skip_reason);

CREATE TABLE normalized_market_identities (
    market_identity_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    market_id text COLLATE "C" NOT NULL UNIQUE
);
CREATE TABLE normalized_metadata_versions (
    metadata_version_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    market_identity_id integer NOT NULL REFERENCES normalized_market_identities,
    condition_id text COLLATE "C" NOT NULL,
    market_slug text COLLATE "C" NOT NULL,
    market_title text COLLATE "C" NOT NULL,
    category text COLLATE "C" NULL,
    market_start_utc timestamptz NULL,
    market_end_utc timestamptz NULL,
    UNIQUE (metadata_version_id, market_identity_id),
    UNIQUE NULLS NOT DISTINCT (
        market_identity_id, condition_id, market_slug, market_title,
        category, market_start_utc, market_end_utc)
);
CREATE TABLE normalized_market_tombstones (
    strategy_id uuid NOT NULL,
    market_identity_id integer NOT NULL REFERENCES normalized_market_identities,
    metadata_version_id integer NOT NULL,
    archived_run_id uuid NOT NULL UNIQUE,
    detected_at_utc timestamptz NOT NULL,
    entry_due_at_utc timestamptz NOT NULL,
    selected_asset_id text NULL,
    selected_outcome text NULL,
    stake_usd numeric(28,8) NOT NULL,
    skip_reason text COLLATE "C" NOT NULL,
    run_updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (strategy_id, market_identity_id),
    FOREIGN KEY (metadata_version_id, market_identity_id)
        REFERENCES normalized_metadata_versions(metadata_version_id, market_identity_id)
);
CREATE INDEX normalized_metadata_condition_idx
ON normalized_metadata_versions(condition_id, metadata_version_id, market_identity_id);
CREATE INDEX normalized_tombstones_metadata_strategy_idx
ON normalized_market_tombstones(metadata_version_id, strategy_id, archived_run_id);
CREATE INDEX normalized_tombstones_rollup_idx
ON normalized_market_tombstones(
    strategy_id, ((run_updated_at_utc AT TIME ZONE 'UTC')::date),
    skip_reason, run_updated_at_utc, archived_run_id);
CREATE INDEX normalized_tombstones_strategy_recent_idx
ON normalized_market_tombstones(strategy_id, run_updated_at_utc DESC, archived_run_id)
INCLUDE (stake_usd, entry_due_at_utc, skip_reason);
CREATE INDEX normalized_tombstones_global_recent_idx
ON normalized_market_tombstones(run_updated_at_utc DESC, strategy_id, archived_run_id)
INCLUDE (stake_usd, entry_due_at_utc, skip_reason);

CREATE TABLE proposed_market_identities (
    market_identity_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    market_id text COLLATE "C" NOT NULL UNIQUE
);
CREATE TABLE proposed_metadata_versions (
    metadata_version_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    market_identity_id integer NOT NULL REFERENCES proposed_market_identities,
    condition_id text COLLATE "C" NOT NULL,
    market_slug text COLLATE "C" NOT NULL,
    market_title text COLLATE "C" NOT NULL,
    category text COLLATE "C" NULL,
    market_start_utc timestamptz NULL,
    market_end_utc timestamptz NULL,
    UNIQUE (metadata_version_id, market_identity_id),
    UNIQUE NULLS NOT DISTINCT (
        market_identity_id, condition_id, market_slug, market_title,
        category, market_start_utc, market_end_utc)
);
CREATE TABLE proposed_reasons (
    skip_reason_id smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    skip_reason text COLLATE "C" NOT NULL UNIQUE
);
CREATE TABLE proposed_tombstones (
    strategy_id uuid NOT NULL,
    market_identity_id integer NOT NULL REFERENCES proposed_market_identities,
    metadata_version_id integer NOT NULL,
    archived_run_id uuid NOT NULL UNIQUE,
    detected_at_utc timestamptz NOT NULL,
    entry_due_at_utc timestamptz NOT NULL,
    selected_asset_id text NULL,
    selected_outcome text NULL,
    stake_usd numeric(28,8) NOT NULL,
    skip_reason_id smallint NOT NULL REFERENCES proposed_reasons,
    run_updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (strategy_id, market_identity_id),
    FOREIGN KEY (metadata_version_id, market_identity_id)
        REFERENCES proposed_metadata_versions(metadata_version_id, market_identity_id)
);
CREATE INDEX proposed_metadata_condition_idx
ON proposed_metadata_versions(condition_id, metadata_version_id, market_identity_id);
CREATE INDEX proposed_tombstones_metadata_strategy_idx
ON proposed_tombstones(metadata_version_id, strategy_id, archived_run_id);
CREATE INDEX proposed_tombstones_rollup_idx
ON proposed_tombstones(
    strategy_id, ((run_updated_at_utc AT TIME ZONE 'UTC')::date),
    skip_reason_id, run_updated_at_utc, archived_run_id);
CREATE INDEX proposed_tombstones_strategy_recent_idx
ON proposed_tombstones(strategy_id, run_updated_at_utc DESC, archived_run_id)
INCLUDE (stake_usd, entry_due_at_utc, skip_reason_id);
CREATE INDEX proposed_tombstones_global_recent_idx
ON proposed_tombstones(run_updated_at_utc DESC, strategy_id, archived_run_id)
INCLUDE (stake_usd, entry_due_at_utc, skip_reason_id);
""";

    private const string PrototypeFixtureSql =
        """
CREATE TABLE fixture_market_identities (
    market_identity_id integer PRIMARY KEY,
    market_id text COLLATE "C" NOT NULL UNIQUE
);
INSERT INTO fixture_market_identities (market_identity_id, market_id)
SELECT
    market_ordinal + 1,
    'm-' || lpad(market_ordinal::text, 4, '0') || repeat('x', 26)
FROM generate_series(0, 1023) AS market(market_ordinal);

CREATE TABLE fixture_metadata_versions (
    metadata_version_id integer PRIMARY KEY,
    market_identity_id integer NOT NULL,
    condition_id text COLLATE "C" NOT NULL,
    market_slug text COLLATE "C" NOT NULL,
    market_title text COLLATE "C" NOT NULL,
    category text COLLATE "C" NULL,
    market_start_utc timestamptz NULL,
    market_end_utc timestamptz NULL
);
WITH metadata_keys AS (
    SELECT market_ordinal + 1 AS metadata_version_id, market_ordinal + 1 AS market_identity_id
    FROM generate_series(0, 1023) AS market(market_ordinal)
    UNION ALL
    SELECT 1025 + market_ordinal, market_ordinal + 1
    FROM generate_series(0, 7) AS market(market_ordinal)
), metadata_widths AS (
    SELECT
        metadata_version_id,
        market_identity_id,
        65 + ((metadata_version_id - 1) % 3) * 2 AS condition_bytes,
        23 + ((metadata_version_id - 1) % 3) * 2 AS slug_bytes,
        44 + ((metadata_version_id - 1) % 3) * 4 AS title_bytes,
        5 + ((metadata_version_id - 1) % 3) * 2 AS category_bytes
    FROM metadata_keys
)
INSERT INTO fixture_metadata_versions (
    metadata_version_id, market_identity_id, condition_id, market_slug, market_title,
    category, market_start_utc, market_end_utc)
SELECT
    metadata_version_id,
    market_identity_id,
    'Ж' || lpad(metadata_version_id::text, 6, '0') || repeat('c', condition_bytes - 8),
    'Ж' || lpad(metadata_version_id::text, 6, '0') || repeat('s', slug_bytes - 8),
    '界' || lpad(metadata_version_id::text, 6, '0') || repeat('t', title_bytes - 9),
    CASE WHEN metadata_version_id % 4 = 0 THEN NULL
         ELSE 'Ж' || lpad((metadata_version_id % 100)::text, 2, '0')
              || repeat('g', category_bytes - 4)
    END,
    CASE WHEN metadata_version_id % 4 IN (1, 3)
         THEN timestamptz '2026-06-01 00:00:00+00'
              + market_identity_id * interval '5 minutes'
         ELSE NULL
    END,
    CASE WHEN metadata_version_id % 4 IN (2, 3)
         THEN timestamptz '2026-06-01 00:05:00+00'
              + market_identity_id * interval '5 minutes'
         ELSE NULL
    END
FROM metadata_widths;

CREATE TABLE fixture_reasons (
    skip_reason_id smallint PRIMARY KEY,
    skip_reason text COLLATE "C" NOT NULL UNIQUE
);
INSERT INTO fixture_reasons (skip_reason_id, skip_reason)
SELECT
    reason_ordinal + 1,
    'Ж' || lpad((reason_ordinal + 1)::text, 2, '0')
        || repeat('r', (39 + (reason_ordinal % 3) * 4) - 4)
FROM generate_series(0, 36) AS reason(reason_ordinal);

CREATE TABLE fixture_rows (
    row_ordinal integer PRIMARY KEY,
    strategy_ordinal integer NOT NULL,
    market_ordinal integer NOT NULL,
    strategy_id uuid NOT NULL,
    market_identity_id integer NOT NULL,
    metadata_version_id integer NOT NULL,
    archived_run_id uuid NOT NULL,
    market_id text COLLATE "C" NOT NULL,
    condition_id text COLLATE "C" NOT NULL,
    market_slug text COLLATE "C" NOT NULL,
    market_title text COLLATE "C" NOT NULL,
    category text COLLATE "C" NULL,
    market_start_utc timestamptz NULL,
    market_end_utc timestamptz NULL,
    detected_at_utc timestamptz NOT NULL,
    entry_due_at_utc timestamptz NOT NULL,
    selected_asset_id text NULL,
    selected_outcome text NULL,
    stake_usd numeric(28,8) NOT NULL,
    skip_reason_id smallint NOT NULL,
    skip_reason text COLLATE "C" NOT NULL,
    run_updated_at_utc timestamptz NOT NULL
);
WITH row_keys AS (
    SELECT
        strategy_ordinal * 1024 + market_ordinal AS row_ordinal,
        strategy_ordinal,
        market_ordinal,
        CASE
            WHEN market_ordinal < 8 AND strategy_ordinal % 2 = 1
                THEN 1025 + market_ordinal
            ELSE market_ordinal + 1
        END AS metadata_version_id,
        ((strategy_ordinal * 1024 + market_ordinal) % 37 + 1)::smallint AS skip_reason_id,
        timestamptz '2026-07-01 00:00:00+00'
            + (market_ordinal % 30) * interval '1 day'
            + ((strategy_ordinal * 307 + market_ordinal) % 86400) * interval '1 second'
            AS run_updated_at_utc
    FROM generate_series(0, 255) AS strategy(strategy_ordinal)
    CROSS JOIN generate_series(0, 1023) AS market(market_ordinal)
)
INSERT INTO fixture_rows (
    row_ordinal, strategy_ordinal, market_ordinal, strategy_id, market_identity_id,
    metadata_version_id, archived_run_id, market_id, condition_id, market_slug,
    market_title, category, market_start_utc, market_end_utc, detected_at_utc,
    entry_due_at_utc, selected_asset_id, selected_outcome, stake_usd,
    skip_reason_id, skip_reason, run_updated_at_utc)
SELECT
    row_keys.row_ordinal,
    row_keys.strategy_ordinal,
    row_keys.market_ordinal,
    ('10000000-0000-4000-8000-'
        || lpad(to_hex(row_keys.strategy_ordinal + 1), 12, '0'))::uuid,
    row_keys.market_ordinal + 1,
    row_keys.metadata_version_id,
    ('20000000-0000-4000-8000-'
        || lpad(to_hex(row_keys.row_ordinal + 1), 12, '0'))::uuid,
    identity.market_id,
    metadata.condition_id,
    metadata.market_slug,
    metadata.market_title,
    metadata.category,
    metadata.market_start_utc,
    metadata.market_end_utc,
    row_keys.run_updated_at_utc - interval '10 minutes',
    row_keys.run_updated_at_utc - interval '5 minutes',
    CASE WHEN row_keys.row_ordinal % 3 = 0 THEN NULL
         ELSE 'asset-' || lpad((row_keys.market_ordinal % 113)::text, 3, '0')
    END,
    CASE WHEN row_keys.row_ordinal % 4 = 0 THEN NULL
         WHEN row_keys.row_ordinal % 2 = 0 THEN 'Yes'
         ELSE 'Нет'
    END,
    ((100 + row_keys.row_ordinal % 900)::numeric / 100)::numeric(28,8),
    row_keys.skip_reason_id,
    reason.skip_reason,
    row_keys.run_updated_at_utc
FROM row_keys
INNER JOIN fixture_market_identities identity
    ON identity.market_identity_id = row_keys.market_ordinal + 1
INNER JOIN fixture_metadata_versions metadata
    ON metadata.metadata_version_id = row_keys.metadata_version_id
   AND metadata.market_identity_id = identity.market_identity_id
INNER JOIN fixture_reasons reason
    ON reason.skip_reason_id = row_keys.skip_reason_id;
""";

    private const string PrototypePopulationSql =
        """
INSERT INTO v1_tombstones (
    strategy_id, market_id, archived_run_id, archived_at_utc, archive_format_version,
    condition_id, market_slug, market_title, category, market_start_utc, market_end_utc,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome, stake_usd,
    skip_reason, run_created_at_utc, run_updated_at_utc, rollup_bucket_start_utc)
SELECT
    strategy_id, market_id, archived_run_id, run_updated_at_utc + interval '1 hour', 1,
    condition_id, market_slug, market_title, category, market_start_utc, market_end_utc,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome, stake_usd,
    skip_reason, detected_at_utc, run_updated_at_utc,
    date_trunc('day', run_updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
FROM fixture_rows;

INSERT INTO same_market_identities OVERRIDING SYSTEM VALUE
SELECT * FROM fixture_market_identities;
INSERT INTO same_metadata_versions OVERRIDING SYSTEM VALUE
SELECT * FROM fixture_metadata_versions;
INSERT INTO same_reasons OVERRIDING SYSTEM VALUE
SELECT * FROM fixture_reasons;
SELECT setval(
    pg_get_serial_sequence('same_market_identities', 'market_identity_id'),
    (SELECT max(market_identity_id) FROM same_market_identities),
    true);
SELECT setval(
    pg_get_serial_sequence('same_metadata_versions', 'metadata_version_id'),
    (SELECT max(metadata_version_id) FROM same_metadata_versions),
    true);
SELECT setval(
    pg_get_serial_sequence('same_reasons', 'skip_reason_id'),
    (SELECT max(skip_reason_id) FROM same_reasons),
    true);
INSERT INTO same_tombstones (
    strategy_id, market_identity_id, metadata_version_id, archived_run_id,
    archive_format_version, detected_at_utc, entry_due_at_utc, selected_asset_id,
    selected_outcome, stake_usd, skip_reason_id, run_updated_at_utc)
SELECT
    strategy_id, market_identity_id, metadata_version_id, archived_run_id,
    2, detected_at_utc, entry_due_at_utc, selected_asset_id,
    selected_outcome, stake_usd, skip_reason_id, run_updated_at_utc
FROM fixture_rows;

INSERT INTO per_row_tombstones (
    strategy_id, market_id, archived_run_id, condition_id, market_slug, market_title,
    category, market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc,
    selected_asset_id, selected_outcome, stake_usd, skip_reason, run_updated_at_utc)
SELECT
    strategy_id, market_id, archived_run_id, condition_id, market_slug, market_title,
    category, market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc,
    selected_asset_id, selected_outcome, stake_usd, skip_reason, run_updated_at_utc
FROM fixture_rows;

INSERT INTO normalized_market_identities OVERRIDING SYSTEM VALUE
SELECT * FROM fixture_market_identities;
INSERT INTO normalized_metadata_versions OVERRIDING SYSTEM VALUE
SELECT * FROM fixture_metadata_versions;
SELECT setval(
    pg_get_serial_sequence('normalized_market_identities', 'market_identity_id'),
    (SELECT max(market_identity_id) FROM normalized_market_identities),
    true);
SELECT setval(
    pg_get_serial_sequence('normalized_metadata_versions', 'metadata_version_id'),
    (SELECT max(metadata_version_id) FROM normalized_metadata_versions),
    true);
INSERT INTO normalized_market_tombstones (
    strategy_id, market_identity_id, metadata_version_id, archived_run_id,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome,
    stake_usd, skip_reason, run_updated_at_utc)
SELECT
    strategy_id, market_identity_id, metadata_version_id, archived_run_id,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome,
    stake_usd, skip_reason, run_updated_at_utc
FROM fixture_rows;

INSERT INTO proposed_market_identities OVERRIDING SYSTEM VALUE
SELECT * FROM fixture_market_identities;
INSERT INTO proposed_metadata_versions OVERRIDING SYSTEM VALUE
SELECT * FROM fixture_metadata_versions;
INSERT INTO proposed_reasons OVERRIDING SYSTEM VALUE
SELECT * FROM fixture_reasons;
SELECT setval(
    pg_get_serial_sequence('proposed_market_identities', 'market_identity_id'),
    (SELECT max(market_identity_id) FROM proposed_market_identities),
    true);
SELECT setval(
    pg_get_serial_sequence('proposed_metadata_versions', 'metadata_version_id'),
    (SELECT max(metadata_version_id) FROM proposed_metadata_versions),
    true);
SELECT setval(
    pg_get_serial_sequence('proposed_reasons', 'skip_reason_id'),
    (SELECT max(skip_reason_id) FROM proposed_reasons),
    true);
INSERT INTO proposed_tombstones (
    strategy_id, market_identity_id, metadata_version_id, archived_run_id,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome,
    stake_usd, skip_reason_id, run_updated_at_utc)
SELECT
    strategy_id, market_identity_id, metadata_version_id, archived_run_id,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome,
    stake_usd, skip_reason_id, run_updated_at_utc
FROM fixture_rows;
""";

    private static IReadOnlyList<PrototypeLayout> CreatePrototypeLayouts()
    {
        return
        [
            new(
                "current_v1",
                ["v1_tombstones"],
                []),
            new(
                "same_table_versioned",
                ["same_market_identities", "same_metadata_versions", "same_reasons", "same_tombstones"],
                [
                    "same_market_identities_market_identity_id_seq",
                    "same_metadata_versions_metadata_version_id_seq",
                    "same_reasons_skip_reason_id_seq"
                ]),
            new(
                "dedicated_per_row_v2",
                ["per_row_tombstones"],
                []),
            new(
                "normalized_market_v2",
                ["normalized_market_identities", "normalized_metadata_versions", "normalized_market_tombstones"],
                [
                    "normalized_market_identities_market_identity_id_seq",
                    "normalized_metadata_versions_metadata_version_id_seq"
                ]),
            new(
                "proposed_normalized_v2",
                ["proposed_market_identities", "proposed_metadata_versions", "proposed_reasons", "proposed_tombstones"],
                [
                    "proposed_market_identities_market_identity_id_seq",
                    "proposed_metadata_versions_metadata_version_id_seq",
                    "proposed_reasons_skip_reason_id_seq"
                ])
        ];
    }

    private static async Task<PrototypeDatabaseEvidence> ReadPrototypeDatabaseEvidenceAsync(
        NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT version(), current_database(), current_setting('server_version_num')::integer,
       current_setting('server_encoding');
""",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new PrototypeDatabaseEvidence(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task ExecutePrototypeSqlAsync(
        NpgsqlConnection connection,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 0
        };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task VacuumPrototypeTablesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<PrototypeLayout> layouts)
    {
        var tableNames = layouts
            .SelectMany(layout => layout.Tables)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var tableName in tableNames)
        {
            await ExecutePrototypeSqlAsync(connection, $"VACUUM (ANALYZE) {tableName};");
        }
    }

    private static async Task<IReadOnlyList<PrototypeLayoutMeasurement>> MeasurePrototypeLayoutsAsync(
        NpgsqlConnection connection,
        string schemaName,
        IReadOnlyList<PrototypeLayout> layouts)
    {
        var result = new List<PrototypeLayoutMeasurement>(layouts.Count);
        foreach (var layout in layouts)
        {
            var relationMeasurements = new List<PrototypeRelationMeasurement>(
                layout.Tables.Count + layout.Sequences.Count);
            foreach (var tableName in layout.Tables)
            {
                await using var command = new NpgsqlCommand(
                    """
WITH target AS (
    SELECT c.oid, c.reltoastrelid
    FROM pg_class c
    INNER JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = @SchemaName
      AND c.relname = @TableName
      AND c.relkind = 'r'
)
SELECT
    @TableName,
    COALESCE(pg_relation_size(target.oid), 0)::bigint,
    COALESCE(pg_indexes_size(target.oid), 0)::bigint,
    CASE WHEN target.reltoastrelid = 0 THEN 0::bigint
         ELSE pg_total_relation_size(target.reltoastrelid)::bigint
    END,
    COALESCE(pg_total_relation_size(target.oid), 0)::bigint,
    CASE WHEN target.reltoastrelid = 0 THEN NULL
         ELSE target.reltoastrelid::regclass::text
    END
FROM target;
""",
                    connection);
                command.Parameters.AddWithValue("SchemaName", schemaName);
                command.Parameters.AddWithValue("TableName", tableName);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var relationName = reader.GetString(0);
                var heapBytes = reader.GetInt64(1);
                var indexBytes = reader.GetInt64(2);
                var toastBytes = reader.GetInt64(3);
                var totalBytes = reader.GetInt64(4);
                var toastRelation = reader.IsDBNull(5) ? null : reader.GetString(5);
                Assert.False(await reader.ReadAsync());
                await reader.DisposeAsync();

                var rowCount = await ReadPrototypeScalarAsync<long>(
                    connection,
                    $"SELECT count(*)::bigint FROM {tableName};");
                var indexes = await ReadPrototypeIndexMeasurementsAsync(
                    connection,
                    schemaName,
                    tableName);
                Assert.Equal(indexBytes, indexes.Sum(index => index.Bytes));
                relationMeasurements.Add(new PrototypeRelationMeasurement(
                    relationName,
                    rowCount,
                    heapBytes,
                    indexBytes,
                    toastBytes,
                    totalBytes,
                    toastRelation,
                    indexes));
            }

            foreach (var sequenceName in layout.Sequences)
            {
                await using var command = new NpgsqlCommand(
                    """
WITH target AS (
    SELECT c.oid
    FROM pg_class c
    INNER JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = @SchemaName
      AND c.relname = @SequenceName
      AND c.relkind = 'S'
)
SELECT
    @SequenceName,
    COALESCE(pg_relation_size(target.oid), 0)::bigint,
    COALESCE(pg_total_relation_size(target.oid), 0)::bigint
FROM target;
""",
                    connection);
                command.Parameters.AddWithValue("SchemaName", schemaName);
                command.Parameters.AddWithValue("SequenceName", sequenceName);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var relationName = reader.GetString(0);
                var heapBytes = reader.GetInt64(1);
                var totalBytes = reader.GetInt64(2);
                Assert.False(await reader.ReadAsync());
                relationMeasurements.Add(new PrototypeRelationMeasurement(
                    relationName,
                    1,
                    heapBytes,
                    0,
                    0,
                    totalBytes,
                    null,
                    []));
            }

            result.Add(new PrototypeLayoutMeasurement(
                layout.Name,
                relationMeasurements,
                relationMeasurements.Sum(relation => relation.HeapBytes),
                relationMeasurements.Sum(relation => relation.IndexBytes),
                relationMeasurements.Sum(relation => relation.ToastBytes),
                relationMeasurements.Sum(relation => relation.TotalBytes)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<PrototypeIndexMeasurement>>
        ReadPrototypeIndexMeasurementsAsync(
            NpgsqlConnection connection,
            string schemaName,
            string tableName)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT index_class.relname, pg_relation_size(index_class.oid)::bigint
FROM pg_index index_definition
INNER JOIN pg_class table_class ON table_class.oid = index_definition.indrelid
INNER JOIN pg_namespace table_namespace ON table_namespace.oid = table_class.relnamespace
INNER JOIN pg_class index_class ON index_class.oid = index_definition.indexrelid
WHERE table_namespace.nspname = @SchemaName
  AND table_class.relname = @TableName
ORDER BY index_class.relname;
""",
            connection);
        command.Parameters.AddWithValue("SchemaName", schemaName);
        command.Parameters.AddWithValue("TableName", tableName);
        var indexes = new List<PrototypeIndexMeasurement>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(new PrototypeIndexMeasurement(
                reader.GetString(0),
                reader.GetInt64(1)));
        }

        return indexes;
    }

    private static void AssertPrototypeLayoutRowCounts(
        IReadOnlyList<PrototypeLayoutMeasurement> measurements)
    {
        var byLayout = measurements.ToDictionary(
            measurement => measurement.Layout,
            StringComparer.Ordinal);
        Assert.Equal(262_144, Assert.Single(byLayout["current_v1"].Relations).Rows);
        Assert.Equal(262_144, byLayout["same_table_versioned"].Relations
            .Single(relation => relation.Relation == "same_tombstones").Rows);
        Assert.Equal(262_144, Assert.Single(byLayout["dedicated_per_row_v2"].Relations).Rows);
        Assert.Equal(262_144, byLayout["normalized_market_v2"].Relations
            .Single(relation => relation.Relation == "normalized_market_tombstones").Rows);
        Assert.Equal(262_144, byLayout["proposed_normalized_v2"].Relations
            .Single(relation => relation.Relation == "proposed_tombstones").Rows);
        Assert.Equal(1_024, byLayout["proposed_normalized_v2"].Relations
            .Single(relation => relation.Relation == "proposed_market_identities").Rows);
        Assert.Equal(1_032, byLayout["proposed_normalized_v2"].Relations
            .Single(relation => relation.Relation == "proposed_metadata_versions").Rows);
        Assert.Equal(37, byLayout["proposed_normalized_v2"].Relations
            .Single(relation => relation.Relation == "proposed_reasons").Rows);
    }

    private static IReadOnlyList<PrototypeLayoutComparison> ComparePrototypeLayoutSizes(
        IReadOnlyList<PrototypeLayoutMeasurement> emptyMeasurements,
        IReadOnlyList<PrototypeLayoutMeasurement> populatedMeasurements)
    {
        var emptyByLayout = emptyMeasurements.ToDictionary(
            measurement => measurement.Layout,
            StringComparer.Ordinal);
        return populatedMeasurements
            .Select(populated =>
            {
                var empty = emptyByLayout[populated.Layout];
                return new PrototypeLayoutComparison(
                    populated.Layout,
                    empty.TotalBytes,
                    populated.TotalBytes,
                    populated.TotalBytes - empty.TotalBytes,
                    populated.HeapBytes,
                    populated.IndexBytes,
                    populated.ToastBytes,
                    empty.Relations,
                    populated.Relations);
            })
            .ToArray();
    }

    private static async Task<PrototypeFixtureCounts> ReadPrototypeFixtureCountsAsync(
        NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT
    count(*)::bigint,
    count(DISTINCT strategy_id)::bigint,
    count(DISTINCT market_identity_id)::bigint,
    count(DISTINCT metadata_version_id)::bigint,
    (SELECT count(*)::bigint FROM fixture_metadata_versions),
    (SELECT count(*)::bigint
     FROM (
         SELECT market_identity_id
         FROM fixture_rows
         GROUP BY market_identity_id
         HAVING count(DISTINCT metadata_version_id) = 2
     ) dual_version_market),
    (SELECT count(*)::bigint
     FROM (
         SELECT market_identity_id
         FROM fixture_rows
         GROUP BY market_identity_id
         HAVING count(DISTINCT metadata_version_id) = 1
     ) single_version_market),
    count(DISTINCT skip_reason_id)::bigint,
    count(DISTINCT (run_updated_at_utc AT TIME ZONE 'UTC')::date)::bigint,
    (SELECT count(*) > 0 FROM fixture_rows WHERE category IS NULL)
        AND (SELECT count(*) > 0 FROM fixture_rows WHERE category IS NOT NULL)
        AND (SELECT count(*) > 0 FROM fixture_rows WHERE market_start_utc IS NULL)
        AND (SELECT count(*) > 0 FROM fixture_rows WHERE market_start_utc IS NOT NULL)
        AND (SELECT count(*) > 0 FROM fixture_rows WHERE market_end_utc IS NULL)
        AND (SELECT count(*) > 0 FROM fixture_rows WHERE market_end_utc IS NOT NULL)
        AND (SELECT count(*) > 0 FROM fixture_rows WHERE selected_asset_id IS NULL)
        AND (SELECT count(*) > 0 FROM fixture_rows WHERE selected_asset_id IS NOT NULL)
        AND (SELECT count(*) > 0 FROM fixture_rows WHERE selected_outcome IS NULL)
        AND (SELECT count(*) > 0 FROM fixture_rows WHERE selected_outcome IS NOT NULL),
    (SELECT avg(octet_length(condition_id))::double precision FROM fixture_rows),
    (SELECT avg(octet_length(market_slug))::double precision FROM fixture_rows),
    (SELECT avg(octet_length(market_title))::double precision FROM fixture_rows),
    (SELECT avg(octet_length(category))::double precision FROM fixture_rows
     WHERE category IS NOT NULL),
    (SELECT avg(octet_length(skip_reason))::double precision FROM fixture_rows)
FROM fixture_rows;
""",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new PrototypeFixtureCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetBoolean(9),
            reader.GetDouble(10),
            reader.GetDouble(11),
            reader.GetDouble(12),
            reader.GetDouble(13),
            reader.GetDouble(14));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task<IReadOnlyList<PrototypeCanonicalRestorationEvidence>>
        ReadPrototypeCanonicalRestorationEvidenceAsync(
            NpgsqlConnection connection,
            IReadOnlyList<PrototypeLayout> layouts)
    {
        IReadOnlyList<(string Layout, string Sql)> canonicalQueries =
        [
            (
                "fixture",
                """
SELECT
    strategy_id, market_id, archived_run_id,
    condition_id, market_slug, market_title, category, market_start_utc, market_end_utc,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome, stake_usd,
    skip_reason, detected_at_utc AS run_created_at_utc, run_updated_at_utc,
    date_trunc('day', run_updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
        AS rollup_bucket_start_utc
FROM fixture_rows
ORDER BY archived_run_id
"""),
            (
                "current_v1",
                """
SELECT
    strategy_id, market_id, archived_run_id,
    condition_id, market_slug, market_title, category, market_start_utc, market_end_utc,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome, stake_usd,
    skip_reason, run_created_at_utc, run_updated_at_utc, rollup_bucket_start_utc
FROM v1_tombstones
WHERE archive_format_version = 1
ORDER BY archived_run_id
"""),
            (
                "same_table_versioned",
                """
SELECT
    tombstone.strategy_id, market_identity.market_id, tombstone.archived_run_id,
    metadata.condition_id, metadata.market_slug, metadata.market_title, metadata.category,
    metadata.market_start_utc, metadata.market_end_utc,
    tombstone.detected_at_utc, tombstone.entry_due_at_utc,
    tombstone.selected_asset_id, tombstone.selected_outcome, tombstone.stake_usd,
    reason.skip_reason, tombstone.detected_at_utc AS run_created_at_utc,
    tombstone.run_updated_at_utc,
    date_trunc('day', tombstone.run_updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
        AS rollup_bucket_start_utc
FROM same_tombstones tombstone
INNER JOIN same_market_identities market_identity
    ON market_identity.market_identity_id = tombstone.market_identity_id
INNER JOIN same_metadata_versions metadata
    ON metadata.metadata_version_id = tombstone.metadata_version_id
   AND metadata.market_identity_id = tombstone.market_identity_id
INNER JOIN same_reasons reason ON reason.skip_reason_id = tombstone.skip_reason_id
WHERE tombstone.archive_format_version = 2
ORDER BY tombstone.archived_run_id
"""),
            (
                "dedicated_per_row_v2",
                """
SELECT
    strategy_id, market_id, archived_run_id,
    condition_id, market_slug, market_title, category, market_start_utc, market_end_utc,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome, stake_usd,
    skip_reason, detected_at_utc AS run_created_at_utc, run_updated_at_utc,
    date_trunc('day', run_updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
        AS rollup_bucket_start_utc
FROM per_row_tombstones
ORDER BY archived_run_id
"""),
            (
                "normalized_market_v2",
                """
SELECT
    tombstone.strategy_id, market_identity.market_id, tombstone.archived_run_id,
    metadata.condition_id, metadata.market_slug, metadata.market_title, metadata.category,
    metadata.market_start_utc, metadata.market_end_utc,
    tombstone.detected_at_utc, tombstone.entry_due_at_utc,
    tombstone.selected_asset_id, tombstone.selected_outcome, tombstone.stake_usd,
    tombstone.skip_reason, tombstone.detected_at_utc AS run_created_at_utc,
    tombstone.run_updated_at_utc,
    date_trunc('day', tombstone.run_updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
        AS rollup_bucket_start_utc
FROM normalized_market_tombstones tombstone
INNER JOIN normalized_market_identities market_identity
    ON market_identity.market_identity_id = tombstone.market_identity_id
INNER JOIN normalized_metadata_versions metadata
    ON metadata.metadata_version_id = tombstone.metadata_version_id
   AND metadata.market_identity_id = tombstone.market_identity_id
ORDER BY tombstone.archived_run_id
"""),
            (
                "proposed_normalized_v2",
                """
SELECT
    tombstone.strategy_id, market_identity.market_id, tombstone.archived_run_id,
    metadata.condition_id, metadata.market_slug, metadata.market_title, metadata.category,
    metadata.market_start_utc, metadata.market_end_utc,
    tombstone.detected_at_utc, tombstone.entry_due_at_utc,
    tombstone.selected_asset_id, tombstone.selected_outcome, tombstone.stake_usd,
    reason.skip_reason, tombstone.detected_at_utc AS run_created_at_utc,
    tombstone.run_updated_at_utc,
    date_trunc('day', tombstone.run_updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
        AS rollup_bucket_start_utc
FROM proposed_tombstones tombstone
INNER JOIN proposed_market_identities market_identity
    ON market_identity.market_identity_id = tombstone.market_identity_id
INNER JOIN proposed_metadata_versions metadata
    ON metadata.metadata_version_id = tombstone.metadata_version_id
   AND metadata.market_identity_id = tombstone.market_identity_id
INNER JOIN proposed_reasons reason ON reason.skip_reason_id = tombstone.skip_reason_id
ORDER BY tombstone.archived_run_id
""")
        ];

        Assert.Equal(layouts.Count + 1, canonicalQueries.Count);
        Assert.All(
            layouts,
            layout => Assert.Single(
                canonicalQueries,
                query => query.Layout == layout.Name));

        var evidence = new List<PrototypeCanonicalRestorationEvidence>(canonicalQueries.Count);
        foreach (var query in canonicalQueries)
        {
            var result = await ReadPrototypeQueryResultAsync(connection, query.Sql, []);
            evidence.Add(new PrototypeCanonicalRestorationEvidence(
                query.Layout,
                result.Cardinality,
                result.Sha256));
        }

        return evidence;
    }

    private static async Task<IReadOnlyList<PrototypeQuerySpec>> CreatePrototypeQuerySpecsAsync(
        NpgsqlConnection connection)
    {
        var nowUtc = await ReadPrototypeScalarAsync<DateTime>(
            connection,
            "SELECT max(run_updated_at_utc) FROM fixture_rows;");
        var cutoffUtc = nowUtc.AddHours(-24);
        var strategyId = await ReadPrototypeScalarAsync<Guid>(
            connection,
            "SELECT strategy_id FROM fixture_rows WHERE row_ordinal = 262143;");
        var marketId = await ReadPrototypeScalarAsync<string>(
            connection,
            "SELECT market_id FROM fixture_rows WHERE row_ordinal = 262143;");
        var archivedRunId = await ReadPrototypeScalarAsync<Guid>(
            connection,
            "SELECT archived_run_id FROM fixture_rows WHERE row_ordinal = 262143;");
        var conditionId = await ReadPrototypeScalarAsync<string>(
            connection,
            "SELECT condition_id FROM fixture_rows WHERE row_ordinal = 1023;");
        var rollupStrategyId = await ReadPrototypeScalarAsync<Guid>(
            connection,
            "SELECT strategy_id FROM fixture_rows WHERE row_ordinal = 261120;");
        var rollupDay = await ReadPrototypeScalarAsync<DateOnly>(
            connection,
            "SELECT (run_updated_at_utc AT TIME ZONE 'UTC')::date FROM fixture_rows WHERE row_ordinal = 261120;");
        var rollupReasonId = await ReadPrototypeScalarAsync<short>(
            connection,
            "SELECT skip_reason_id FROM fixture_rows WHERE row_ordinal = 261120;");

        return
        [
            new(
                "condition-dependency",
                """
SELECT t.archived_run_id
FROM proposed_metadata_versions m
INNER JOIN proposed_tombstones t
    ON t.metadata_version_id = m.metadata_version_id
   AND t.market_identity_id = m.market_identity_id
WHERE m.condition_id = @ConditionId
ORDER BY t.archived_run_id
""",
                "SELECT archived_run_id FROM fixture_rows WHERE condition_id = @ConditionId ORDER BY archived_run_id",
                [new("ConditionId", conditionId)],
                "proposed_tombstones_metadata_strategy_idx",
                int.MaxValue,
                matched => matched + 2),
            new(
                "strategy-market",
                """
SELECT t.archived_run_id
FROM proposed_market_identities m
INNER JOIN proposed_tombstones t ON t.market_identity_id = m.market_identity_id
WHERE m.market_id = @MarketId AND t.strategy_id = @StrategyId
ORDER BY t.archived_run_id
""",
                "SELECT archived_run_id FROM fixture_rows WHERE market_id = @MarketId AND strategy_id = @StrategyId ORDER BY archived_run_id",
                [new("MarketId", marketId), new("StrategyId", strategyId)],
                "proposed_tombstones_pkey",
                1,
                matched => matched + 2),
            new(
                "archived-run",
                "SELECT archived_run_id FROM proposed_tombstones WHERE archived_run_id = @ArchivedRunId ORDER BY archived_run_id",
                "SELECT archived_run_id FROM fixture_rows WHERE archived_run_id = @ArchivedRunId ORDER BY archived_run_id",
                [new("ArchivedRunId", archivedRunId)],
                "proposed_tombstones_archived_run_id_key",
                1,
                matched => matched + 2),
            new(
                "rollup-reversal",
                """
SELECT archived_run_id
FROM proposed_tombstones
WHERE strategy_id = @StrategyId
  AND (run_updated_at_utc AT TIME ZONE 'UTC')::date = @UtcDay
  AND skip_reason_id = @SkipReasonId
ORDER BY run_updated_at_utc, archived_run_id
""",
                """
SELECT archived_run_id
FROM fixture_rows
WHERE strategy_id = @StrategyId
  AND (run_updated_at_utc AT TIME ZONE 'UTC')::date = @UtcDay
  AND skip_reason_id = @SkipReasonId
ORDER BY run_updated_at_utc, archived_run_id
""",
                [
                    new("StrategyId", rollupStrategyId),
                    new("UtcDay", rollupDay),
                    new("SkipReasonId", rollupReasonId)
                ],
                "proposed_tombstones_rollup_idx",
                1,
                matched => matched + 4),
            new(
                "strategy-recent-24h",
                """
SELECT tombstone.archived_run_id, tombstone.strategy_id, tombstone.stake_usd,
       tombstone.entry_due_at_utc, reason.skip_reason, tombstone.run_updated_at_utc
FROM proposed_tombstones tombstone
INNER JOIN proposed_reasons reason ON reason.skip_reason_id = tombstone.skip_reason_id
WHERE tombstone.strategy_id = @StrategyId
  AND tombstone.run_updated_at_utc >= @CutoffUtc
  AND tombstone.run_updated_at_utc <= @NowUtc
ORDER BY tombstone.strategy_id, tombstone.run_updated_at_utc, tombstone.archived_run_id
""",
                """
SELECT archived_run_id, strategy_id, stake_usd, entry_due_at_utc,
       skip_reason, run_updated_at_utc
FROM fixture_rows
WHERE strategy_id = @StrategyId
  AND run_updated_at_utc >= @CutoffUtc
  AND run_updated_at_utc <= @NowUtc
ORDER BY strategy_id, run_updated_at_utc, archived_run_id
""",
                [
                    new("StrategyId", strategyId),
                    new("CutoffUtc", cutoffUtc),
                    new("NowUtc", nowUtc)
                ],
                "proposed_tombstones_strategy_recent_idx",
                40,
                _ => 40),
            new(
                "global-recent-24h",
                """
SELECT tombstone.archived_run_id, tombstone.strategy_id, tombstone.stake_usd,
       tombstone.entry_due_at_utc, reason.skip_reason, tombstone.run_updated_at_utc
FROM proposed_tombstones tombstone
INNER JOIN proposed_reasons reason ON reason.skip_reason_id = tombstone.skip_reason_id
WHERE tombstone.run_updated_at_utc >= @CutoffUtc
  AND tombstone.run_updated_at_utc <= @NowUtc
ORDER BY tombstone.strategy_id, tombstone.run_updated_at_utc, tombstone.archived_run_id
""",
                """
SELECT archived_run_id, strategy_id, stake_usd, entry_due_at_utc,
       skip_reason, run_updated_at_utc
FROM fixture_rows
WHERE run_updated_at_utc >= @CutoffUtc
  AND run_updated_at_utc <= @NowUtc
ORDER BY strategy_id, run_updated_at_utc, archived_run_id
""",
                [new("CutoffUtc", cutoffUtc), new("NowUtc", nowUtc)],
                "proposed_tombstones_global_recent_idx",
                9_000,
                _ => 9_000)
        ];
    }

    private static async Task<T> ReadPrototypeScalarAsync<T>(
        NpgsqlConnection connection,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Assert.IsType<T>(await command.ExecuteScalarAsync());
    }

    private static async Task<PrototypeQueryResult> ReadPrototypeQueryResultAsync(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<PrototypeQueryParameter> parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 0
        };
        AddPrototypeParameters(command, parameters);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var cardinality = 0;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            hash.AppendData([(byte)0xF0]);
            AppendPrototypeInt32(hash, reader.FieldCount);
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                if (reader.IsDBNull(ordinal))
                {
                    hash.AppendData([(byte)0]);
                    continue;
                }

                hash.AppendData([(byte)1]);
                AppendPrototypeValue(hash, reader.GetValue(ordinal));
            }

            hash.AppendData([(byte)0xF1]);
            cardinality++;
        }

        return new PrototypeQueryResult(
            cardinality,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void AppendPrototypeValue(IncrementalHash hash, object value)
    {
        switch (value)
        {
            case Guid guid:
            {
                hash.AppendData([(byte)'g']);
                Span<byte> buffer = stackalloc byte[16];
                Assert.True(guid.TryWriteBytes(buffer));
                hash.AppendData(buffer);
                break;
            }
            case string text:
            {
                hash.AppendData([(byte)'s']);
                var bytes = Encoding.UTF8.GetBytes(text);
                AppendPrototypeInt32(hash, bytes.Length);
                hash.AppendData(bytes);
                break;
            }
            case DateTime dateTime:
                hash.AppendData([(byte)'t']);
                AppendPrototypeInt64(hash, dateTime.ToUniversalTime().Ticks);
                break;
            case DateTimeOffset dateTimeOffset:
                hash.AppendData([(byte)'o']);
                AppendPrototypeInt64(hash, dateTimeOffset.UtcTicks);
                break;
            case decimal decimalValue:
                hash.AppendData([(byte)'d']);
                foreach (var part in decimal.GetBits(decimalValue))
                {
                    AppendPrototypeInt32(hash, part);
                }

                break;
            case short shortValue:
                hash.AppendData([(byte)'h']);
                AppendPrototypeInt32(hash, shortValue);
                break;
            case int intValue:
                hash.AppendData([(byte)'i']);
                AppendPrototypeInt32(hash, intValue);
                break;
            case long longValue:
                hash.AppendData([(byte)'l']);
                AppendPrototypeInt64(hash, longValue);
                break;
            case DateOnly dateOnly:
                hash.AppendData([(byte)'D']);
                AppendPrototypeInt32(hash, dateOnly.DayNumber);
                break;
            case bool boolValue:
                hash.AppendData([(byte)'b', boolValue ? (byte)1 : (byte)0]);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported prototype result type {value.GetType().FullName}.");
        }
    }

    private static void AppendPrototypeInt32(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static void AppendPrototypeInt64(IncrementalHash hash, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static async Task<JsonDocument> ReadPrototypePlanAsync(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<PrototypeQueryParameter> parameters)
    {
        await using var command = new NpgsqlCommand(
            "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql,
            connection)
        {
            CommandTimeout = 0
        };
        AddPrototypeParameters(command, parameters);
        var planJson = Assert.IsType<string>(await command.ExecuteScalarAsync());
        return JsonDocument.Parse(planJson);
    }

    private static void AddPrototypeParameters(
        NpgsqlCommand command,
        IReadOnlyList<PrototypeQueryParameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
    }

    private static PrototypePlanEvidence InspectPrototypePlan(
        JsonDocument plan,
        PrototypeQuerySpec spec)
    {
        var nodes = new List<PrototypePlanNode>();
        CollectPrototypePlanNodes(plan.RootElement[0].GetProperty("Plan"), nodes);
        var drivingNode = Assert.Single(nodes, node =>
            node.IndexName == spec.RequiredIndexName
            && (node.RelationName == "proposed_tombstones"
                || (node.NodeType == "Bitmap Index Scan" && node.RelationName is null)));
        Assert.Contains(
            drivingNode.NodeType,
            (IReadOnlyCollection<string>)["Index Scan", "Index Only Scan", "Bitmap Index Scan"]);
        var hasSequentialScan = nodes.Any(node =>
            node.RelationName == "proposed_tombstones"
            && node.NodeType == "Seq Scan");
        var sequentialDimensions = nodes
            .Where(node => node.NodeType == "Seq Scan"
                && node.RelationName is "proposed_market_identities"
                    or "proposed_metadata_versions"
                    or "proposed_reasons")
            .Select(node => new PrototypeSequentialDimension(
                node.RelationName!,
                checked((int)Math.Ceiling(node.ActualRows * node.ActualLoops))))
            .ToArray();
        return new PrototypePlanEvidence(
            drivingNode.IndexName!,
            drivingNode.NodeType,
            checked((int)Math.Ceiling(
                (drivingNode.ActualRows + drivingNode.RowsRemovedByFilter
                    + drivingNode.RowsRemovedByIndexRecheck) * drivingNode.ActualLoops)),
            hasSequentialScan,
            sequentialDimensions);
    }

    private static void CollectPrototypePlanNodes(
        JsonElement node,
        ICollection<PrototypePlanNode> nodes)
    {
        nodes.Add(new PrototypePlanNode(
            node.GetProperty("Node Type").GetString()!,
            node.TryGetProperty("Relation Name", out var relationName)
                ? relationName.GetString()
                : null,
            node.TryGetProperty("Index Name", out var indexName)
                ? indexName.GetString()
                : null,
            node.TryGetProperty("Actual Rows", out var actualRows)
                ? actualRows.GetDouble()
                : 0,
            node.TryGetProperty("Actual Loops", out var actualLoops)
                ? actualLoops.GetDouble()
                : 0,
            node.TryGetProperty("Rows Removed by Filter", out var removedByFilter)
                ? removedByFilter.GetDouble()
                : 0,
            node.TryGetProperty("Rows Removed by Index Recheck", out var removedByRecheck)
                ? removedByRecheck.GetDouble()
                : 0));
        if (node.TryGetProperty("Plans", out var childPlans))
        {
            foreach (var child in childPlans.EnumerateArray())
            {
                CollectPrototypePlanNodes(child, nodes);
            }
        }
    }

    private static string FormatPrototypeQueryEvidenceSql(PrototypeQuerySpec spec)
    {
        var parameterLines = string.Join(
            Environment.NewLine,
            spec.Parameters.Select(parameter =>
                $"-- @{parameter.Name} = {Convert.ToString(parameter.Value, CultureInfo.InvariantCulture)}"));
        return $"{parameterLines}{Environment.NewLine}{Environment.NewLine}" +
               $"-- Actual proposed-v2 query{Environment.NewLine}{spec.ActualSql};" +
               $"{Environment.NewLine}{Environment.NewLine}" +
               $"-- Independent fixture query{Environment.NewLine}{spec.ExpectedSql};" +
               Environment.NewLine;
    }

    private static void WritePrototypeEvidenceFile(
        string evidenceDirectory,
        string fileName,
        string content)
    {
        var path = Path.Combine(evidenceDirectory, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WritePrototypeJsonEvidenceFile<T>(
        string evidenceDirectory,
        string fileName,
        T value)
    {
        WritePrototypeEvidenceFile(
            evidenceDirectory,
            fileName,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record PrototypeLayout(
        string Name,
        IReadOnlyList<string> Tables,
        IReadOnlyList<string> Sequences);

    private sealed record PrototypeDatabaseEvidence(
        string Version,
        string DatabaseName,
        int ServerVersionNumber,
        string ServerEncoding);

    private sealed record PrototypeRelationMeasurement(
        string Relation,
        long Rows,
        long HeapBytes,
        long IndexBytes,
        long ToastBytes,
        long TotalBytes,
        string? ToastRelation,
        IReadOnlyList<PrototypeIndexMeasurement> Indexes);

    private sealed record PrototypeIndexMeasurement(string Index, long Bytes);

    private sealed record PrototypeLayoutMeasurement(
        string Layout,
        IReadOnlyList<PrototypeRelationMeasurement> Relations,
        long HeapBytes,
        long IndexBytes,
        long ToastBytes,
        long TotalBytes);

    private sealed record PrototypeLayoutComparison(
        string Layout,
        long EmptyTotalBytes,
        long PopulatedTotalBytes,
        long DeltaTotalBytes,
        long PopulatedHeapBytes,
        long PopulatedIndexBytes,
        long PopulatedToastBytes,
        IReadOnlyList<PrototypeRelationMeasurement> EmptyRelations,
        IReadOnlyList<PrototypeRelationMeasurement> Relations);

    private sealed record PrototypeFixtureCounts(
        long Rows,
        long Strategies,
        long MarketIdentities,
        long MetadataVersions,
        long MetadataDimensionRows,
        long DualVersionMarkets,
        long SingleVersionMarkets,
        long Reasons,
        long UtcDays,
        bool NullableStatesAllCovered,
        double AverageConditionBytes,
        double AverageSlugBytes,
        double AverageTitleBytes,
        double AverageCategoryBytes,
        double AverageReasonBytes);

    private sealed record PrototypeCanonicalRestorationEvidence(
        string Layout,
        int Rows,
        string Sha256);

    private sealed record PrototypeQueryParameter(string Name, object Value);

    private sealed record PrototypeQuerySpec(
        string Name,
        string ActualSql,
        string ExpectedSql,
        IReadOnlyList<PrototypeQueryParameter> Parameters,
        string RequiredIndexName,
        int ResultCardinalityLimit,
        Func<int, int> ExaminedRowsLimit);

    private sealed record PrototypeQueryResult(int Cardinality, string Sha256);

    private sealed record PrototypePlanNode(
        string NodeType,
        string? RelationName,
        string? IndexName,
        double ActualRows,
        double ActualLoops,
        double RowsRemovedByFilter,
        double RowsRemovedByIndexRecheck);

    private sealed record PrototypeSequentialDimension(string Relation, int RelationRows);

    private sealed record PrototypePlanEvidence(
        string DrivingIndexName,
        string DrivingNodeType,
        int DrivingExaminedRows,
        bool HasProposedTombstoneSequentialScan,
        IReadOnlyList<PrototypeSequentialDimension> SequentialDimensionRelations);

    private sealed record PrototypeQueryEvidence(
        string Query,
        int Cardinality,
        string ExpectedSha256,
        string ActualSha256,
        string DrivingIndex,
        string DrivingNodeType,
        int DrivingExaminedRows,
        string PlanFile);

    private sealed record PrototypeCompletionReport(
        string FixtureGeneratorVersion,
        string PostgreSqlVersion,
        string DatabaseName,
        PrototypeFixtureCounts FixtureCounts,
        IReadOnlyList<PrototypeCanonicalRestorationEvidence> CanonicalRestoration,
        IReadOnlyList<PrototypeLayoutComparison> Layouts,
        IReadOnlyList<PrototypeQueryEvidence> Queries,
        string FixtureSql,
        string CandidateLayoutsSql,
        string PopulationSql,
        string SavingsBoundary);

    private sealed record RetentionCounts(
        long RawRuns,
        long RollupRuns,
        long Tombstones,
        long ProjectionEvents,
        long ReconciliationQueueRows);

    private sealed record ProjectionEventPayload(
        string Operation,
        string? OldPayloadJson,
        string? NewPayloadJson);

    private sealed record ReconciliationQueueVersion(
        string TransactionId,
        string TupleId,
        int Priority,
        string Reason,
        int AttemptCount,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset NextAttemptAtUtc,
        string? LastError);

    private sealed record PaperHistoryCounts(
        long Orders,
        long Fills,
        long Positions,
        long Settlements);

    private sealed record RollupGroup(
        int RunCount,
        DateTimeOffset FirstUpdatedAtUtc,
        DateTimeOffset LastUpdatedAtUtc);

    private sealed record V2DimensionIds(
        int MarketIdentityId,
        int MetadataVersionId,
        short SkipReasonId);

    private sealed record V2DimensionCounts(
        long MarketIdentities,
        long MetadataVersions);

    private sealed record V2DimensionTableSnapshot(
        long MarketIdentityCount,
        string MarketIdentitiesJson,
        long MetadataVersionCount,
        string MetadataVersionsJson,
        long ReasonCount,
        string ReasonsJson);
}
