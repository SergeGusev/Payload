using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public enum HistoricalFeeLookupDisposition
{
    Calculated,
    ProvedMarketAbsent,
    SemanticUnavailable,
    OperationalFailure,
    ProtocolInvariantConflict
}

public sealed record HistoricalFeeLookupRequest(
    string ConditionId,
    decimal Shares,
    decimal Price,
    FeeLiquidityRole LiquidityRole,
    bool LiquidityRoleIsValid = true);

public sealed record HistoricalFeeLookupMarketEvidence(
    bool FeeSchedulePresent,
    long? MakerBaseFeeBps,
    long? TakerBaseFeeBps);

public sealed record HistoricalFeeLookupResult(
    HistoricalFeeLookupDisposition Disposition,
    decimal? FeeUsd,
    string FeeAccountingStatus,
    string FeeLiquidityRole,
    string CalculationSource,
    decimal? FeeRate,
    int? FeeExponent,
    bool? FeeTakerOnly,
    DateTimeOffset CalculatedAtUtc,
    int? HttpStatusCode,
    string Evidence,
    HistoricalFeeLookupMarketEvidence? MarketEvidence = null)
{
    public bool IsCalculated => Disposition == HistoricalFeeLookupDisposition.Calculated;

    public bool IsOperationalFailure => Disposition == HistoricalFeeLookupDisposition.OperationalFailure;
}

public interface IPolymarketFeeAccountingService
{
    Task<PaperFill> ApplyToPaperFillAsync(
        PaperOrder order,
        PaperFill fill,
        CancellationToken cancellationToken = default);

    Task<LiveOrder> ApplyToLiveOrderAsync(
        LiveOrder order,
        CancellationToken cancellationToken = default);

    Task<PaperEntryPersistenceBatch> ApplyToEntryBatchAsync(
        PaperEntryPersistenceBatch batch,
        CancellationToken cancellationToken = default);

    Task<HistoricalFeeLookupResult> CalculateHistoricalFeeAsync(
        HistoricalFeeLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Historical fee lookup is not implemented by this service.");
    }
}

public sealed class PolymarketFeeAccountingService(
    ILogger<PolymarketFeeAccountingService> logger,
    IPolymarketClobPublicClient clobClient) : IPolymarketFeeAccountingService
{
    private const int MarketInfoCachePruneThreshold = 2048;
    private static readonly TimeSpan MarketInfoCacheDuration = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, MarketInfoCacheEntry> marketInfoCache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<PaperFill> ApplyToPaperFillAsync(
        PaperOrder order,
        PaperFill fill,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(fill);

        if (FeeAccountingRules.IsAccounted(fill.FeeAccountingStatus))
        {
            return fill;
        }

        var role = FeeAccountingRules.ParseLiquidityRole(fill.FeeLiquidityRole);
        var application = await CalculateAsync(
            order.ConditionId,
            fill.SizeShares,
            fill.Price,
            role,
            cancellationToken).ConfigureAwait(false);

        return fill with
        {
            FeeUsd = application.Result.FeeUsd ?? 0m,
            FeeAccountingStatus = application.Result.Status.ToString(),
            FeeLiquidityRole = role.ToString(),
            FeeCalculationSource = application.Result.CalculationSource,
            FeeRate = application.MarketInfo?.FeeSchedule?.Rate,
            FeeExponent = application.MarketInfo?.FeeSchedule?.Exponent,
            FeeTakerOnly = application.MarketInfo?.FeeSchedule?.TakerOnly,
            FeeCalculatedAtUtc = application.CalculatedAtUtc
        };
    }

    public async Task<LiveOrder> ApplyToLiveOrderAsync(
        LiveOrder order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.FilledSize <= 0m)
        {
            return order;
        }

        var currentStatus = FeeAccountingRules.ParseStatus(order.FeeAccountingStatus);
        if (currentStatus == FeeAccountingStatus.VenueReported)
        {
            return ApplyLiveFinancialTotals(order, order.FeeUsd, feeIsFullyAccounted: true);
        }

        var role = ResolveLiveLiquidityRole(order);
        var fillPrice = order.AverageFillPrice ??
            (order.FilledNotionalUsd > 0m ? order.FilledNotionalUsd / order.FilledSize : order.Price);
        var application = await CalculateAsync(
            order.ConditionId,
            order.FilledSize,
            fillPrice,
            role,
            cancellationToken).ConfigureAwait(false);
        var feeUsd = application.Result.FeeUsd ?? 0m;
        var updatedOrder = order with
        {
            FeeUsd = feeUsd,
            FeeAccountingStatus = application.Result.Status.ToString(),
            FeeLiquidityRole = role.ToString(),
            FeeCalculationSource = application.Result.CalculationSource,
            FeeRate = application.MarketInfo?.FeeSchedule?.Rate,
            FeeExponent = application.MarketInfo?.FeeSchedule?.Exponent,
            FeeTakerOnly = application.MarketInfo?.FeeSchedule?.TakerOnly,
            FeeCalculatedAtUtc = application.CalculatedAtUtc
        };
        return ApplyLiveFinancialTotals(
            updatedOrder,
            feeUsd,
            FeeAccountingRules.IsAccounted(application.Result.Status));
    }

    public async Task<PaperEntryPersistenceBatch> ApplyToEntryBatchAsync(
        PaperEntryPersistenceBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.PaperFills.Count == 0)
        {
            return batch;
        }

        var ordersById = batch.PaperOrders.ToDictionary(order => order.Id);
        var feeTasks = batch.PaperFills.Select(async fill =>
        {
            if (FeeAccountingRules.IsAccounted(fill.FeeAccountingStatus))
            {
                return fill;
            }

            if (!ordersById.TryGetValue(fill.PaperOrderId, out var order))
            {
                return fill with
                {
                    FeeAccountingStatus = FeeAccountingStatus.CalculationUnavailable.ToString(),
                    FeeCalculationSource = PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource,
                    FeeCalculatedAtUtc = DateTimeOffset.UtcNow
                };
            }

            return await ApplyToPaperFillAsync(order, fill, cancellationToken).ConfigureAwait(false);
        });
        var fills = await Task.WhenAll(feeTasks).ConfigureAwait(false);
        var fillsById = fills.ToDictionary(fill => fill.Id);
        var fillsByOrderId = fills
            .GroupBy(fill => fill.PaperOrderId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var materializations = batch.PaperPositionMaterializations
            .Select(materialization => fillsById.TryGetValue(materialization.Fill.Id, out var fill)
                ? materialization with { Fill = fill }
                : materialization)
            .ToArray();
        var runs = batch.StrategyRuns
            .Select(run => run.PaperOrderId is { } paperOrderId && fillsByOrderId.TryGetValue(paperOrderId, out var runFills)
                ? ApplyAggregateFee(run, runFills)
                : run)
            .ToArray();

        return batch with
        {
            PaperFills = fills,
            StrategyRuns = runs,
            PaperPositionMaterializations = materializations
        };
    }

    public async Task<HistoricalFeeLookupResult> CalculateHistoricalFeeAsync(
        HistoricalFeeLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var calculatedAtUtc = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(request.ConditionId))
        {
            return HistoricalUnavailable(
                HistoricalFeeLookupDisposition.SemanticUnavailable,
                request,
                calculatedAtUtc,
                null,
                "Condition ID is missing.");
        }

        if (request.Shares <= 0m)
        {
            return HistoricalUnavailable(
                HistoricalFeeLookupDisposition.SemanticUnavailable,
                request,
                calculatedAtUtc,
                null,
                "Filled shares must be greater than zero.");
        }

        if (request.Price <= 0m || request.Price >= 1m)
        {
            return HistoricalUnavailable(
                HistoricalFeeLookupDisposition.SemanticUnavailable,
                request,
                calculatedAtUtc,
                null,
                "Fill price must be greater than zero and less than one.");
        }

        if (!request.LiquidityRoleIsValid || !Enum.IsDefined(request.LiquidityRole))
        {
            return HistoricalUnavailable(
                HistoricalFeeLookupDisposition.SemanticUnavailable,
                request,
                calculatedAtUtc,
                null,
                "Liquidity role is invalid.");
        }

        PolymarketClobMarketInfo marketInfo;
        try
        {
            marketInfo = await clobClient
                .GetClobMarketInfoAsync(request.ConditionId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var statusCode = GetStatusCode(ex);
            if (statusCode == HttpStatusCode.NotFound)
            {
                return HistoricalUnavailable(
                    HistoricalFeeLookupDisposition.ProvedMarketAbsent,
                    request,
                    calculatedAtUtc,
                    (int)statusCode.Value,
                    "CLOB market info returned HTTP 404.");
            }

            if (IsOperationalLookupFailure(ex, statusCode))
            {
                return HistoricalUnavailable(
                    HistoricalFeeLookupDisposition.OperationalFailure,
                    request,
                    calculatedAtUtc,
                    statusCode is null ? null : (int)statusCode.Value,
                    ex.GetType().Name);
            }

            return HistoricalUnavailable(
                HistoricalFeeLookupDisposition.ProtocolInvariantConflict,
                request,
                calculatedAtUtc,
                statusCode is null ? null : (int)statusCode.Value,
                ex.GetType().Name);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var calculation = PolymarketFeeCalculator.CalculatePlatformFee(
            request.Shares,
            request.Price,
            request.LiquidityRole,
            marketInfo);
        if (!FeeAccountingRules.IsAccounted(calculation.Status) || calculation.FeeUsd is null)
        {
            return new HistoricalFeeLookupResult(
                HistoricalFeeLookupDisposition.SemanticUnavailable,
                null,
                calculation.Status.ToString(),
                request.LiquidityRole.ToString(),
                calculation.CalculationSource,
                marketInfo.FeeSchedule?.Rate,
                marketInfo.FeeSchedule?.Exponent,
                marketInfo.FeeSchedule?.TakerOnly,
                calculatedAtUtc,
                null,
                calculation.UnavailableReason ?? "CLOB fee schedule is incomplete.",
                CreateHistoricalMarketEvidence(marketInfo));
        }

        return new HistoricalFeeLookupResult(
            HistoricalFeeLookupDisposition.Calculated,
            calculation.FeeUsd,
            calculation.Status.ToString(),
            request.LiquidityRole.ToString(),
            calculation.CalculationSource,
            marketInfo.FeeSchedule?.Rate,
            marketInfo.FeeSchedule?.Exponent,
            marketInfo.FeeSchedule?.TakerOnly,
            calculatedAtUtc,
            null,
            "CLOB market info and local fee formula completed.",
            CreateHistoricalMarketEvidence(marketInfo));
    }

    private async Task<FeeApplication> CalculateAsync(
        string conditionId,
        decimal shares,
        decimal price,
        FeeLiquidityRole liquidityRole,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var calculatedAtUtc = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(conditionId))
        {
            return FeeApplication.Unavailable("Condition ID is missing.", liquidityRole, calculatedAtUtc);
        }

        var lookup = await GetMarketInfoAsync(conditionId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (lookup.MarketInfo is null)
        {
            return FeeApplication.Unavailable(lookup.Error ?? "CLOB market info is unavailable.", liquidityRole, calculatedAtUtc);
        }

        return new FeeApplication(
            PolymarketFeeCalculator.CalculatePlatformFee(shares, price, liquidityRole, lookup.MarketInfo),
            lookup.MarketInfo,
            calculatedAtUtc);
    }

    private async Task<MarketInfoLookup> GetMarketInfoAsync(
        string conditionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            if (marketInfoCache.TryGetValue(conditionId, out var current) && current.ExpiresAtUtc > nowUtc)
            {
                return await current.Value.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            var replacement = new MarketInfoCacheEntry(
                nowUtc + MarketInfoCacheDuration,
                new Lazy<Task<MarketInfoLookup>>(
                    () => LoadMarketInfoAsync(conditionId),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            var installed = current is null
                ? marketInfoCache.TryAdd(conditionId, replacement)
                : marketInfoCache.TryUpdate(conditionId, replacement, current);
            if (installed)
            {
                PruneExpiredMarketInfoEntries(nowUtc);
                return await replacement.Value.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void PruneExpiredMarketInfoEntries(DateTimeOffset nowUtc)
    {
        if (marketInfoCache.Count <= MarketInfoCachePruneThreshold)
        {
            return;
        }

        foreach (var entry in marketInfoCache)
        {
            if (entry.Value.ExpiresAtUtc <= nowUtc)
            {
                marketInfoCache.TryRemove(entry);
            }
        }
    }

    private async Task<MarketInfoLookup> LoadMarketInfoAsync(string conditionId)
    {
        try
        {
            return new MarketInfoLookup(
                await clobClient.GetClobMarketInfoAsync(conditionId, CancellationToken.None).ConfigureAwait(false),
                null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Polymarket fee calculation could not load CLOB market info. ConditionId={ConditionId}",
                conditionId);
            return new MarketInfoLookup(null, ex.Message);
        }
    }

    private static FeeLiquidityRole ResolveLiveLiquidityRole(LiveOrder order)
    {
        var persistedRole = FeeAccountingRules.ParseLiquidityRole(order.FeeLiquidityRole);
        var isImmediateOrder = string.Equals(order.OrderType, "FAK", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(order.OrderType, "FOK", StringComparison.OrdinalIgnoreCase);
        if (order.PostOnly == true)
        {
            if (isImmediateOrder || persistedRole == FeeLiquidityRole.Taker)
            {
                return FeeLiquidityRole.Unknown;
            }

            return FeeLiquidityRole.Maker;
        }

        if (isImmediateOrder)
        {
            return persistedRole == FeeLiquidityRole.Maker
                ? FeeLiquidityRole.Unknown
                : FeeLiquidityRole.Taker;
        }

        return persistedRole;
    }

    private static LiveOrder ApplyLiveFinancialTotals(
        LiveOrder order,
        decimal feeUsd,
        bool feeIsFullyAccounted)
    {
        var fillPrice = order.AverageFillPrice ??
            (order.FilledNotionalUsd > 0m ? order.FilledNotionalUsd / order.FilledSize : order.Price);
        var filledNotionalUsd = order.FilledNotionalUsd > 0m
            ? order.FilledNotionalUsd
            : fillPrice * order.FilledSize;
        var costBasisUsd = filledNotionalUsd + feeUsd;
        var netRealizedPnlUsd = feeIsFullyAccounted
            ? order.SettlementValueUsd is { } settlementValueUsd
                ? settlementValueUsd - costBasisUsd
                : order.RealizedPnlUsd is { } grossRealizedPnlUsd
                    ? grossRealizedPnlUsd - feeUsd
                    : order.NetRealizedPnlUsd
            : null;

        return order with
        {
            FilledNotionalUsd = filledNotionalUsd,
            CostBasisUsd = costBasisUsd,
            NetRealizedPnlUsd = netRealizedPnlUsd
        };
    }

    private static HistoricalFeeLookupResult HistoricalUnavailable(
        HistoricalFeeLookupDisposition disposition,
        HistoricalFeeLookupRequest request,
        DateTimeOffset calculatedAtUtc,
        int? httpStatusCode,
        string evidence)
    {
        return new HistoricalFeeLookupResult(
            disposition,
            null,
            FeeAccountingStatus.CalculationUnavailable.ToString(),
            request.LiquidityRole.ToString(),
            PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource,
            null,
            null,
            null,
            calculatedAtUtc,
            httpStatusCode,
            evidence);
    }

    private static HttpStatusCode? GetStatusCode(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PolymarketApiException apiException &&
                TryParsePolymarketHttpStatusCode(apiException.Message, out var parsedStatusCode))
            {
                return parsedStatusCode;
            }

            if (current is HttpRequestException httpException && httpException.StatusCode is not null)
            {
                return httpException.StatusCode;
            }
        }

        return null;
    }

    private static bool TryParsePolymarketHttpStatusCode(
        string message,
        out HttpStatusCode statusCode)
    {
        const string marker = "failed with HTTP ";
        var markerIndex = message.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            statusCode = default;
            return false;
        }

        var digitsStart = markerIndex + marker.Length;
        var digitsEnd = digitsStart;
        while (digitsEnd < message.Length && char.IsAsciiDigit(message[digitsEnd]))
        {
            digitsEnd++;
        }

        if (digitsEnd == digitsStart ||
            !int.TryParse(
                message.AsSpan(digitsStart, digitsEnd - digitsStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var numericStatusCode) ||
            numericStatusCode is < 100 or > 599)
        {
            statusCode = default;
            return false;
        }

        statusCode = (HttpStatusCode)numericStatusCode;
        return true;
    }

    private static bool IsOperationalLookupFailure(Exception exception, HttpStatusCode? statusCode)
    {
        if (statusCode is not null)
        {
            var numericStatusCode = (int)statusCode.Value;
            return statusCode is HttpStatusCode.RequestTimeout or
                       (HttpStatusCode)425 or
                       HttpStatusCode.TooManyRequests ||
                   numericStatusCode is >= 500 and <= 599;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or SocketException or IOException or TimeoutException or TaskCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private static HistoricalFeeLookupMarketEvidence CreateHistoricalMarketEvidence(
        PolymarketClobMarketInfo marketInfo) =>
        new(
            marketInfo.FeeSchedule is not null,
            marketInfo.MakerBaseFeeBps,
            marketInfo.TakerBaseFeeBps);

    private static StrategyMarketPaperRun ApplyAggregateFee(
        StrategyMarketPaperRun run,
        IReadOnlyList<PaperFill> fills)
    {
        var status = FeeAccountingRules.Aggregate(fills.Select(fill => fill.FeeAccountingStatus));
        var feeUsd = fills.Sum(fill => fill.FeeUsd);
        return run with
        {
            FeeUsd = feeUsd,
            FeeAccountingStatus = status.ToString(),
            FeeLiquidityRole = SingleValueOrDefault(fills.Select(fill => fill.FeeLiquidityRole), "Unknown"),
            FeeCalculationSource = SingleValueOrDefault(fills.Select(fill => fill.FeeCalculationSource), "mixed"),
            FeeRate = SingleNullableValueOrDefault(fills.Select(fill => fill.FeeRate)),
            FeeExponent = SingleNullableValueOrDefault(fills.Select(fill => fill.FeeExponent)),
            FeeTakerOnly = SingleNullableValueOrDefault(fills.Select(fill => fill.FeeTakerOnly)),
            FeeCalculatedAtUtc = fills.Max(fill => fill.FeeCalculatedAtUtc),
            NetRealizedPnlUsd = FeeAccountingRules.IsAccounted(status)
                ? run.RealizedPnlUsd is { } grossRealizedPnlUsd
                    ? grossRealizedPnlUsd - feeUsd
                    : run.NetRealizedPnlUsd
                : null
        };
    }

    private static string SingleValueOrDefault(IEnumerable<string> values, string fallback)
    {
        var distinct = values
            .Select(value => string.IsNullOrWhiteSpace(value) ? fallback : value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return distinct.Length == 1 ? distinct[0] : fallback;
    }

    private static T? SingleNullableValueOrDefault<T>(IEnumerable<T?> values) where T : struct
    {
        var distinct = values.Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private sealed record MarketInfoCacheEntry(
        DateTimeOffset ExpiresAtUtc,
        Lazy<Task<MarketInfoLookup>> Value);

    private sealed record MarketInfoLookup(PolymarketClobMarketInfo? MarketInfo, string? Error);

    private sealed record FeeApplication(
        PolymarketFeeCalculationResult Result,
        PolymarketClobMarketInfo? MarketInfo,
        DateTimeOffset CalculatedAtUtc)
    {
        public static FeeApplication Unavailable(
            string reason,
            FeeLiquidityRole liquidityRole,
            DateTimeOffset calculatedAtUtc)
        {
            _ = liquidityRole;
            return new FeeApplication(
                new PolymarketFeeCalculationResult(
                    FeeAccountingStatus.CalculationUnavailable,
                    null,
                    null,
                    PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource,
                    reason),
                null,
                calculatedAtUtc);
        }
    }
}
