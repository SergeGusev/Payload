using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

public sealed class CryptoUpDown5mMarketResolvedEventRecorder(
    ILogger<CryptoUpDown5mMarketResolvedEventRecorder> logger,
    IAppRepository repository) : ICryptoUpDown5mMarketResolvedEventRecorder
{
    private const string ComponentName = "CryptoUpDown5mMarketResolvedEventRecorder";
    private const string Source = "MarketWebSocket";
    private static readonly IReadOnlySet<string> AssetSymbols = new HashSet<string>(
        ["BTC", "ETH", "SOL"],
        StringComparer.OrdinalIgnoreCase);

    public async Task RecordAsync(
        string component,
        MarketDataUpdate update,
        ActiveMarketAssetSnapshot? activeMarketSnapshot,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!update.MarketResolved)
        {
            return;
        }

        var diagnosticComponent = string.IsNullOrWhiteSpace(component) ? ComponentName : component.Trim();
        var recorderAction = "IgnoredNoSnapshot";
        string? snapshotAssetSymbol = null;
        DateTimeOffset? snapshotMarketStartUtc = null;
        var snapshotIsCryptoUpDown5m = false;

        try
        {
            if (activeMarketSnapshot is null)
            {
                await TryRecordDiagnosticAsync(
                    diagnosticComponent,
                    update,
                    null,
                    null,
                    null,
                    false,
                    recorderAction,
                    receivedAtUtc,
                    cancellationToken);
                return;
            }

            var market = ToGammaMarket(activeMarketSnapshot);
            var hasAssetSymbol = CryptoUpDown5mMarketAnalyzer.TryGetAssetSymbol(market, AssetSymbols, out var assetSymbol);
            if (hasAssetSymbol)
            {
                snapshotAssetSymbol = assetSymbol;
            }

            var marketInterval = CryptoUpDown5mMarketAnalyzer.GetMarketInterval(market);
            snapshotMarketStartUtc = CryptoUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
            snapshotIsCryptoUpDown5m = hasAssetSymbol && marketInterval == BtcUpDownMarketInterval.FiveMinutes;
            if (!snapshotIsCryptoUpDown5m)
            {
                recorderAction = "IgnoredUnsupportedMarket";
                await TryRecordDiagnosticAsync(
                    diagnosticComponent,
                    update,
                    activeMarketSnapshot,
                    snapshotAssetSymbol,
                    snapshotMarketStartUtc,
                    snapshotIsCryptoUpDown5m,
                    recorderAction,
                    receivedAtUtc,
                    cancellationToken);
                return;
            }

            if (snapshotMarketStartUtc is null)
            {
                recorderAction = "IgnoredMissingMarketStart";
                await TryRecordDiagnosticAsync(
                    diagnosticComponent,
                    update,
                    activeMarketSnapshot,
                    snapshotAssetSymbol,
                    snapshotMarketStartUtc,
                    snapshotIsCryptoUpDown5m,
                    recorderAction,
                    receivedAtUtc,
                    cancellationToken);
                await TryRecordApiErrorAsync(
                    "NormalizeMarketResolved",
                    $"Unable to determine market start for {activeMarketSnapshot.Slug}.",
                    cancellationToken);
                return;
            }

            if (!TryGetWinningOutcome(update, activeMarketSnapshot, out var winningOutcome))
            {
                recorderAction = "IgnoredMissingWinningOutcome";
                await TryRecordDiagnosticAsync(
                    diagnosticComponent,
                    update,
                    activeMarketSnapshot,
                    snapshotAssetSymbol,
                    snapshotMarketStartUtc,
                    snapshotIsCryptoUpDown5m,
                    recorderAction,
                    receivedAtUtc,
                    cancellationToken);
                await TryRecordApiErrorAsync(
                    "NormalizeMarketResolved",
                    $"Unable to normalize winning outcome for {snapshotAssetSymbol} market {activeMarketSnapshot.Slug}.",
                    cancellationToken);
                return;
            }

            var marketEndUtc = snapshotMarketStartUtc.Value.Add(CryptoUpDown5mMarketAnalyzer.GetIntervalDuration(BtcUpDownMarketInterval.FiveMinutes));
            var resultDelaySeconds = Math.Max(0m, (decimal)(receivedAtUtc - marketEndUtc).TotalSeconds);
            var resolvedMarket = new CryptoUpDown5mWebSocketResolvedMarket(
                Guid.NewGuid(),
                snapshotAssetSymbol!,
                activeMarketSnapshot.MarketId,
                activeMarketSnapshot.ConditionId,
                activeMarketSnapshot.Slug,
                snapshotMarketStartUtc.Value,
                marketEndUtc,
                winningOutcome,
                update.WinningAssetId,
                update.TimestampUtc,
                receivedAtUtc,
                receivedAtUtc,
                1,
                Math.Round(resultDelaySeconds, 3, MidpointRounding.AwayFromZero),
                Source,
                update.RawEventType,
                NormalizeRawJson(update.RawJson),
                receivedAtUtc,
                receivedAtUtc);

            await repository.UpsertCryptoUpDown5mWebSocketResolvedMarketAsync(resolvedMarket, cancellationToken);
            recorderAction = "RecordedCryptoUpDown5mResult";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            recorderAction = "RecordFailed";
            logger.LogError(
                ex,
                "Failed to record crypto Up/Down 5m market_resolved event. Asset={AssetSymbol} Market={MarketSlug}",
                snapshotAssetSymbol,
                activeMarketSnapshot?.Slug);
            await TryRecordApiErrorAsync("RecordMarketResolved", ex.Message, cancellationToken);
        }

        await TryRecordDiagnosticAsync(
            diagnosticComponent,
            update,
            activeMarketSnapshot,
            snapshotAssetSymbol,
            snapshotMarketStartUtc,
            snapshotIsCryptoUpDown5m,
            recorderAction,
            receivedAtUtc,
            cancellationToken);
    }

    private static PolymarketGammaMarket ToGammaMarket(ActiveMarketAssetSnapshot snapshot)
    {
        return new PolymarketGammaMarket(
            snapshot.MarketId,
            snapshot.ConditionId,
            snapshot.QuestionId,
            snapshot.Slug,
            snapshot.Question,
            snapshot.EventId,
            snapshot.EventSlug,
            snapshot.EventTitle,
            snapshot.SeriesSlug,
            snapshot.Category,
            snapshot.Active,
            snapshot.Closed,
            snapshot.Archived,
            snapshot.Restricted,
            snapshot.AcceptingOrders,
            snapshot.EnableOrderBook,
            snapshot.NegativeRisk,
            snapshot.Liquidity,
            snapshot.LiquidityClob,
            snapshot.Volume,
            snapshot.Volume24Hr,
            snapshot.BestBid,
            snapshot.BestAsk,
            snapshot.Spread,
            snapshot.CreatedAtUtc,
            snapshot.UpdatedAtUtc,
            snapshot.StartDateUtc,
            snapshot.EndDateUtc,
            snapshot.EventStartTimeUtc,
            snapshot.Outcomes,
            snapshot.ClobTokenIds,
            "{}",
            snapshot.MarketFetchedAtUtc,
            snapshot.LastTradePrice,
            snapshot.OrderMinSize,
            snapshot.OrderPriceMinTickSize);
    }

    private static bool TryGetWinningOutcome(
        MarketDataUpdate update,
        ActiveMarketAssetSnapshot snapshot,
        out string winningOutcome)
    {
        if (TryNormalizeOutcome(update.WinningOutcome, out winningOutcome))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(update.WinningAssetId))
        {
            return false;
        }

        for (var index = 0; index < snapshot.ClobTokenIds.Count && index < snapshot.Outcomes.Count; index++)
        {
            if (string.Equals(snapshot.ClobTokenIds[index], update.WinningAssetId, StringComparison.OrdinalIgnoreCase) &&
                TryNormalizeOutcome(snapshot.Outcomes[index], out winningOutcome))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryNormalizeOutcome(string? value, out string winningOutcome)
    {
        if (string.Equals(value, "Up", StringComparison.OrdinalIgnoreCase))
        {
            winningOutcome = "Up";
            return true;
        }

        if (string.Equals(value, "Down", StringComparison.OrdinalIgnoreCase))
        {
            winningOutcome = "Down";
            return true;
        }

        winningOutcome = string.Empty;
        return false;
    }

    private static string NormalizeRawJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return "{}";
        }

        try
        {
            using var _ = JsonDocument.Parse(rawJson);
            return rawJson;
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    private async Task TryRecordDiagnosticAsync(
        string component,
        MarketDataUpdate update,
        ActiveMarketAssetSnapshot? activeMarketSnapshot,
        string? snapshotAssetSymbol,
        DateTimeOffset? snapshotMarketStartUtc,
        bool snapshotIsCryptoUpDown5m,
        string recorderAction,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken)
    {
        var diagnostic = new MarketResolvedEventDiagnostic(
            Guid.NewGuid(),
            component,
            update.RawEventType,
            update.AssetId,
            update.ConditionId,
            update.WinningAssetId,
            update.WinningOutcome,
            update.TimestampUtc,
            receivedAtUtc,
            activeMarketSnapshot is not null,
            activeMarketSnapshot?.MarketId,
            activeMarketSnapshot?.ConditionId,
            activeMarketSnapshot?.Slug,
            snapshotAssetSymbol,
            snapshotMarketStartUtc,
            snapshotIsCryptoUpDown5m,
            recorderAction,
            NormalizeRawJson(update.RawJson),
            DateTimeOffset.UtcNow);

        try
        {
            await repository.AddMarketResolvedEventDiagnosticAsync(diagnostic, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to record market_resolved diagnostic event. Action={RecorderAction} AssetId={AssetId}",
                recorderAction,
                update.AssetId);
            await TryRecordApiErrorAsync("RecordMarketResolvedDiagnostic", ex.Message, cancellationToken);
        }
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), ComponentName, operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist crypto Up/Down 5m market_resolved recorder API error.");
        }
    }
}
