using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class RetiredPairedMakerGtdFirstAcceptingStrategyTests
{
    private const string CleanupMigrationKey =
        "20260813_remove_paired_maker_gtd_first_accepting_strategies";

    private static readonly (Guid Id, string Code)[] RetiredStrategies =
    [
        (
            Guid.Parse("b7c50005-0000-4000-8224-000000000101"),
            "btc_up_down_5m_up_paired_maker_gtd_first_accepting"),
        (
            Guid.Parse("b7c50005-0000-4000-8224-000000000102"),
            "btc_up_down_5m_down_paired_maker_gtd_first_accepting"),
        (
            Guid.Parse("b7c50005-0000-4000-8224-000000000201"),
            "eth_up_down_5m_up_paired_maker_gtd_first_accepting"),
        (
            Guid.Parse("b7c50005-0000-4000-8224-000000000202"),
            "eth_up_down_5m_down_paired_maker_gtd_first_accepting"),
        (
            Guid.Parse("b7c50005-0000-4000-8224-000000000301"),
            "sol_up_down_5m_up_paired_maker_gtd_first_accepting"),
        (
            Guid.Parse("b7c50005-0000-4000-8224-000000000302"),
            "sol_up_down_5m_down_paired_maker_gtd_first_accepting")
    ];

    [Fact]
    public void StrategyIds_ExcludeExactPairedMakerGtdFirstAcceptingAllowlist()
    {
        Assert.Equal(6, RetiredStrategies.Length);

        foreach (var retired in RetiredStrategies)
        {
            Assert.DoesNotContain(
                StrategyIds.UpDown5mStrategyVariants,
                variant => variant.Id == retired.Id ||
                           string.Equals(variant.Code, retired.Code, StringComparison.Ordinal));
            Assert.DoesNotContain(retired.Id, StrategyIds.AllStrategyIds);
            Assert.Null(StrategyIds.TryGetStrategyIdByCode(retired.Code));
        }
    }

    [Fact]
    public void CurrentSource_ExcludesRetiredBehaviorTimingConfigurationAndRuntimeWiring()
    {
        var models = ReadRepositorySource("src", "PolyCopyTrader.Domain", "Models.cs");
        Assert.DoesNotContain("PairedFixedOutcomeMakerGtdFirstAccepting", models, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstAcceptingOrders", models, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "crypto_paired_maker_gtd_first_accepting_paper",
            models,
            StringComparison.Ordinal);

        var executionContract = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "PaperTrading",
            "MakerGtdPaperExecutionContract.cs");
        Assert.DoesNotContain(
            "PairedMakerGtdPaperExecutionContract",
            executionContract,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "crypto_paired_maker_gtd_first_accepting_paper",
            executionContract,
            StringComparison.Ordinal);

        var appConfiguration = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Domain",
            "Configuration",
            "AppConfiguration.cs");
        var optionsValidator = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Domain",
            "Configuration",
            "AppOptionsValidator.cs");
        var configurationLoader = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "Configuration",
            "AppConfigurationLoader.cs");
        var appSettings = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "appsettings.json");

        Assert.DoesNotContain("PairedMakerGtdDayAheadDiscovery", appConfiguration, StringComparison.Ordinal);
        Assert.DoesNotContain("PairedMakerGtdDayAheadDiscovery", optionsValidator, StringComparison.Ordinal);
        Assert.DoesNotContain("PairedMakerGtdDayAheadDiscovery", configurationLoader, StringComparison.Ordinal);
        Assert.DoesNotContain("PairedMakerGtdDayAheadDiscovery", appSettings, StringComparison.Ordinal);

        Assert.False(RepositoryFileExists(
            "src",
            "PolyCopyTrader.Service",
            "GammaMarkets",
            "PairedMakerGtdDayAheadDiscoveryWorker.cs"));
        Assert.False(RepositoryFileExists(
            "src",
            "PolyCopyTrader.Service",
            "Strategies",
            "PairedMakerGtdFirstAcceptingProcessor.cs"));

        var program = ReadRepositorySource("src", "PolyCopyTrader.Service", "Program.cs");
        Assert.DoesNotContain("PairedMakerGtdDayAheadDiscoveryWorker", program, StringComparison.Ordinal);
        Assert.DoesNotContain("PairedMakerGtdFirstAcceptingProcessor", program, StringComparison.Ordinal);
        Assert.DoesNotContain("PairedMakerGtdDayAheadDiscoveryOptions", program, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgresSchema_ExcludesCurrentSeedsAndContainsExactCleanupAllowlist()
    {
        var schemaSource = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Storage",
            "PostgresSchema.cs");
        var cleanupMigrationIndex = schemaSource.IndexOf(CleanupMigrationKey, StringComparison.Ordinal);
        Assert.True(cleanupMigrationIndex >= 0);

        var schemaBeforeCleanupMigration = schemaSource[..cleanupMigrationIndex];
        Assert.DoesNotContain(
            "crypto_paired_maker_gtd_first_accepting_paper",
            schemaBeforeCleanupMigration,
            StringComparison.Ordinal);
        Assert.All(RetiredStrategies, retired =>
            Assert.DoesNotContain(retired.Code, schemaBeforeCleanupMigration, StringComparison.Ordinal));

        Assert.Contains(CleanupMigrationKey, PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("allowlist_count <> 6", PostgresSchema.SchemaSql, StringComparison.Ordinal);

        foreach (var retired in RetiredStrategies)
        {
            var allowlistPair = $"('{retired.Id}'::uuid, '{retired.Code}',";
            Assert.Equal(1, CountOccurrences(PostgresSchema.SchemaSql, allowlistPair));
        }
    }

    [Fact]
    public void ReferenceAverageMakerGtdFamily_RemainsCataloguedAndSupported()
    {
        var referenceAverageVariants = StrategyIds.UpDown5mStrategyVariants
            .Where(variant =>
                variant.Behavior ==
                BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdMakerGtdPremarket)
            .ToArray();

        Assert.Equal(28, referenceAverageVariants.Length);
        Assert.All(referenceAverageVariants, variant =>
        {
            Assert.Equal("ETH", variant.ReferenceAssetSymbol);
            Assert.StartsWith("b7c50005-0000-4000-8223-", variant.Id.ToString(), StringComparison.Ordinal);
        });

        var firstThresholdId = Guid.Parse("b7c50005-0000-4000-8223-000000000101");
        Assert.Contains(firstThresholdId, StrategyIds.AllStrategyIds);
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode(
            "eth_up_down_5m_reference_average_bps_1_maker_gtd_premarket"));
        var referenceAverageContract = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "PaperTrading",
            "MakerGtdPaperExecutionContract.cs");
        Assert.Contains(
            "eth_reference_average_maker_gtd_paper",
            referenceAverageContract,
            StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = value.IndexOf(search, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += search.Length;
        }

        return count;
    }

    private static bool RepositoryFileExists(params string[] segments)
    {
        return File.Exists(Path.Combine(FindRepositoryRoot(), Path.Combine(segments)));
    }

    private static string ReadRepositorySource(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), Path.Combine(segments)));
    }

    private static string FindRepositoryRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("POLYCOPYTRADER_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) &&
            File.Exists(Path.Combine(configuredRoot, "PolyCopyTrader.sln")))
        {
            return Path.GetFullPath(configuredRoot);
        }

        foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PolyCopyTrader.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the PolyCopyTrader repository root.");
    }
}
