using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Polymarket;

namespace PolyCopyTrader.Tests;

public sealed class PolymarketHttpLogPolicyTests
{
    [Fact]
    public async Task RepositorySink_DefaultPolicy_SkipsSuccessAndExpectedNotFound()
    {
        var repository = new TestAppRepository();
        var sink = new RepositoryPolymarketHttpLogSink(
            repository,
            new PolymarketHttpLoggingOptions(),
            NullLogger<RepositoryPolymarketHttpLogSink>.Instance);

        await sink.RecordAsync(CreateEntry(statusCode: 200, succeeded: true));
        await sink.RecordAsync(CreateEntry(statusCode: 404, succeeded: false));
        await sink.RecordAsync(CreateEntry(statusCode: 429, succeeded: false));
        await sink.RecordAsync(CreateEntry(statusCode: null, succeeded: false, errorMessage: "timeout"));
        await sink.RecordAsync(CreateEntry(statusCode: 500, succeeded: false));

        Assert.Equal(3, repository.PolymarketHttpLogs.Count);
        Assert.DoesNotContain(repository.PolymarketHttpLogs, item => item.StatusCode == 200);
        Assert.DoesNotContain(repository.PolymarketHttpLogs, item => item.StatusCode == 404);
        Assert.Contains(repository.PolymarketHttpLogs, item => item.StatusCode == 429);
        Assert.Contains(repository.PolymarketHttpLogs, item => item.StatusCode is null);
        Assert.Contains(repository.PolymarketHttpLogs, item => item.StatusCode == 500);
    }

    [Fact]
    public async Task RepositorySink_SamplesSuccessfulRequests_WhenSampleRateIsConfigured()
    {
        var repository = new TestAppRepository();
        var sink = new RepositoryPolymarketHttpLogSink(
            repository,
            new PolymarketHttpLoggingOptions { SuccessfulRequestSampleRate = 2 },
            NullLogger<RepositoryPolymarketHttpLogSink>.Instance);

        await sink.RecordAsync(CreateEntry(statusCode: 200, succeeded: true, requestUrl: "https://example.test/1"));
        await sink.RecordAsync(CreateEntry(statusCode: 200, succeeded: true, requestUrl: "https://example.test/2"));
        await sink.RecordAsync(CreateEntry(statusCode: 200, succeeded: true, requestUrl: "https://example.test/3"));
        await sink.RecordAsync(CreateEntry(statusCode: 200, succeeded: true, requestUrl: "https://example.test/4"));

        Assert.Equal(2, repository.PolymarketHttpLogs.Count);
        Assert.Equal("https://example.test/2", repository.PolymarketHttpLogs[0].RequestUrl);
        Assert.Equal("https://example.test/4", repository.PolymarketHttpLogs[1].RequestUrl);
    }

    [Fact]
    public async Task RepositoryCleanup_RemovesRowsBySuccessAndFailureRetention()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        repository.PolymarketHttpLogs.Add(CreateEntry(statusCode: 200, succeeded: true, requestedAtUtc: now.AddHours(-7)));
        repository.PolymarketHttpLogs.Add(CreateEntry(statusCode: 200, succeeded: true, requestedAtUtc: now.AddHours(-1)));
        repository.PolymarketHttpLogs.Add(CreateEntry(statusCode: 500, succeeded: false, requestedAtUtc: now.AddDays(-15)));
        repository.PolymarketHttpLogs.Add(CreateEntry(statusCode: 500, succeeded: false, requestedAtUtc: now.AddDays(-1)));

        var result = await repository.CleanupPolymarketHttpLogsAsync(
            now.AddHours(-6),
            now.AddDays(-14),
            batchSize: 10);

        Assert.Equal(new PolymarketHttpLogCleanupResult(2, 1, 1), result);
        Assert.Equal(2, repository.PolymarketHttpLogs.Count);
        Assert.All(repository.PolymarketHttpLogs, item =>
        {
            if (item.Succeeded)
            {
                Assert.True(item.RequestedAtUtc >= now.AddHours(-6));
            }
            else
            {
                Assert.True(item.RequestedAtUtc >= now.AddDays(-14));
            }
        });
    }

    private static PolymarketHttpLogEntry CreateEntry(
        int? statusCode,
        bool succeeded,
        string requestUrl = "https://example.test",
        DateTimeOffset? requestedAtUtc = null,
        string? errorMessage = null)
    {
        var requestedAt = requestedAtUtc ?? DateTimeOffset.UtcNow;
        return new PolymarketHttpLogEntry(
            Guid.NewGuid(),
            "PolymarketClobPublicClient",
            "GetOrderBook",
            "GET",
            requestUrl,
            requestedAt,
            requestedAt.AddMilliseconds(10),
            10,
            1,
            statusCode,
            succeeded,
            "{}",
            errorMessage);
    }
}
