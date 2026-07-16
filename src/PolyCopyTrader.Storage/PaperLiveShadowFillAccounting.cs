using System.Text.Json;
using System.Text.Json.Nodes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Storage;

internal static class PaperLiveShadowFillAccounting
{
    internal const string TestExecutionSource = "paper_live_shadow_test";
    internal const string ActualFillExecutionSource = "paper_live_shadow_actual_fill";
    internal const string ActualFillModel = "live_order_actual_fill_v1";
    private const string LegacyActualFillEvidencePrefix = "Paper live-shadow copied actual Live fill.";
    private const decimal AccountingTolerance = 0.000001m;

    internal static CanonicalPaperLiveShadowState CreateCanonicalState(
        PaperOrder currentOrder,
        LiveOrder liveOrder,
        IReadOnlyList<PaperFill> existingFills,
        PaperPosition? currentPosition,
        DateTimeOffset reconciledAtUtc)
    {
        ValidateShape(currentOrder, liveOrder);

        var liveFilledSize = liveOrder.FilledSize;
        if (liveFilledSize <= 0m)
        {
            throw new InvalidOperationException("Paper/Live shadow reconciliation requires a positive persisted Live filled size.");
        }

        var liveFilledNotional = liveOrder.FilledNotionalUsd > 0m
            ? liveOrder.FilledNotionalUsd
            : (liveOrder.AverageFillPrice ?? liveOrder.Price) * liveFilledSize;
        var fillPrice = liveFilledNotional > 0m
            ? liveFilledNotional / liveFilledSize
            : liveOrder.AverageFillPrice ?? liveOrder.Price;
        if (fillPrice <= 0m || fillPrice > 1m || liveFilledNotional <= 0m)
        {
            throw new InvalidOperationException("Persisted Live fill accounting is invalid for Paper shadow reconciliation.");
        }

        var isMatched = liveOrder.Status == LiveOrderStatus.Matched;
        var isTerminal = liveOrder.Status is LiveOrderStatus.Cancelled or
            LiveOrderStatus.CancelFailed or
            LiveOrderStatus.Rejected or
            LiveOrderStatus.Error;
        var isFinalAccounting = isMatched || isTerminal;
        var canonicalSize = isMatched
            ? liveFilledSize
            : Math.Min(liveFilledSize, currentOrder.SizeShares);
        if (canonicalSize <= 0m)
        {
            throw new InvalidOperationException("Paper/Live shadow reconciliation produced a non-positive canonical fill size.");
        }

        var canonicalNotional = isMatched ? liveFilledNotional : fillPrice * canonicalSize;
        var statusUpdatedAtUtc = liveOrder.UpdatedAtUtc;
        var filledAtUtc = ResolveCanonicalFillTimestamp(existingFills, statusUpdatedAtUtc);
        var rawDecisionJson = AttachLiveFillAccounting(
            currentOrder.RawDecisionJson,
            liveOrder,
            fillPrice,
            canonicalSize,
            canonicalNotional,
            reconciledAtUtc,
            isFinalAccounting);
        var canonicalOrder = isMatched
            ? currentOrder with
            {
                Status = PaperOrderStatus.Filled,
                Price = fillPrice,
                SizeShares = canonicalSize,
                NotionalUsd = canonicalNotional,
                FilledAtUtc = statusUpdatedAtUtc,
                CancelledAtUtc = null,
                RawDecisionJson = rawDecisionJson,
                ExecutionSource = ActualFillExecutionSource
            }
            : currentOrder with
            {
                Status = canonicalSize >= currentOrder.SizeShares - AccountingTolerance
                    ? PaperOrderStatus.Filled
                    : isTerminal
                        ? PaperOrderStatus.PartiallyFilledExpired
                        : PaperOrderStatus.PartiallyFilled,
                FilledAtUtc = canonicalSize >= currentOrder.SizeShares - AccountingTolerance
                    ? statusUpdatedAtUtc
                    : null,
                CancelledAtUtc = isTerminal && canonicalSize < currentOrder.SizeShares - AccountingTolerance
                    ? statusUpdatedAtUtc
                    : null,
                RawDecisionJson = rawDecisionJson,
                ExecutionSource = isTerminal
                    ? ActualFillExecutionSource
                    : currentOrder.ExecutionSource
            };

        var canonicalFillId = existingFills
            .OrderBy(fill => fill.FilledAtUtc)
            .ThenBy(fill => fill.Id)
            .Select(fill => (Guid?)fill.Id)
            .FirstOrDefault() ?? Guid.NewGuid();
        var canonicalFill = new PaperFill(
            canonicalFillId,
            currentOrder.Id,
            fillPrice,
            canonicalSize,
            filledAtUtc,
            BuildEvidence(liveOrder, fillPrice, canonicalSize, canonicalNotional, reconciledAtUtc, isFinalAccounting));
        var canonicalPosition = CreateCanonicalPosition(
            currentOrder,
            canonicalFill,
            existingFills,
            currentPosition,
            reconciledAtUtc);

        return new CanonicalPaperLiveShadowState(canonicalOrder, canonicalFill, canonicalPosition, isFinalAccounting);
    }

    private static DateTimeOffset ResolveCanonicalFillTimestamp(
        IReadOnlyList<PaperFill> existingFills,
        DateTimeOffset observedAtUtc)
    {
        return existingFills
            .Where(IsAuthoritativeLiveShadowFill)
            .OrderBy(fill => fill.FilledAtUtc)
            .ThenBy(fill => fill.Id)
            .Select(fill => (DateTimeOffset?)fill.FilledAtUtc)
            .FirstOrDefault() ?? observedAtUtc;
    }

    private static bool IsAuthoritativeLiveShadowFill(PaperFill fill)
    {
        if (fill.Evidence.StartsWith(LegacyActualFillEvidencePrefix, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(fill.Evidence);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("model", out var model) &&
                model.ValueKind == JsonValueKind.String &&
                string.Equals(model.GetString(), ActualFillModel, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ValidateShape(PaperOrder paperOrder, LiveOrder liveOrder)
    {
        if (paperOrder.Side != TradeSide.Buy || liveOrder.Side != TradeSide.Buy)
        {
            throw new InvalidOperationException("Paper/Live shadow reconciliation supports BUY orders only.");
        }

        if (!string.Equals(liveOrder.ExecutionSource, TestExecutionSource, StringComparison.OrdinalIgnoreCase) ||
            (!string.Equals(paperOrder.ExecutionSource, TestExecutionSource, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(paperOrder.ExecutionSource, ActualFillExecutionSource, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Paper/Live shadow reconciliation received a non-shadow order.");
        }

        if (liveOrder.PaperOrderId != paperOrder.Id ||
            liveOrder.SignalId != paperOrder.SignalId ||
            StrategyIds.Normalize(liveOrder.StrategyId) != StrategyIds.Normalize(paperOrder.StrategyId) ||
            !string.Equals(liveOrder.AssetId, paperOrder.AssetId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(liveOrder.ConditionId, paperOrder.ConditionId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(liveOrder.Outcome, paperOrder.Outcome, StringComparison.OrdinalIgnoreCase) ||
            (liveOrder.CorrelationId is { } liveCorrelationId && paperOrder.CorrelationId != liveCorrelationId))
        {
            throw new InvalidOperationException("Persisted Paper and Live shadow order shapes do not match.");
        }
    }

    private static PaperPosition CreateCanonicalPosition(
        PaperOrder order,
        PaperFill canonicalFill,
        IReadOnlyList<PaperFill> existingFills,
        PaperPosition? currentPosition,
        DateTimeOffset reconciledAtUtc)
    {
        var oldSize = existingFills.Sum(fill => Math.Max(0m, fill.SizeShares));
        var oldCost = existingFills.Sum(fill => Math.Max(0m, fill.SizeShares) * fill.Price);
        if (oldSize > AccountingTolerance && currentPosition is null)
        {
            throw new InvalidOperationException("Existing Paper shadow fills have no matching aggregate Paper position.");
        }

        if (currentPosition is not null &&
            (!string.Equals(currentPosition.AssetId, order.AssetId, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(currentPosition.CopiedTraderWallet, order.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(currentPosition.ConditionId, order.ConditionId, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(currentPosition.Outcome, order.Outcome, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Aggregate Paper position shape does not match the Paper shadow order.");
        }

        var currentSize = currentPosition?.SizeShares ?? 0m;
        var currentCost = currentSize * (currentPosition?.AveragePrice ?? 0m);
        var baseSize = currentSize - oldSize;
        var baseCost = currentCost - oldCost;
        if (baseSize < -AccountingTolerance || baseCost < -AccountingTolerance)
        {
            throw new InvalidOperationException("Aggregate Paper position no longer contains the existing shadow fill contribution.");
        }

        baseSize = Math.Max(0m, baseSize);
        baseCost = Math.Max(0m, baseCost);
        var newSize = baseSize + canonicalFill.SizeShares;
        var newCost = baseCost + canonicalFill.Price * canonicalFill.SizeShares;
        var averagePrice = newCost / newSize;
        var estimatedValue = newSize * canonicalFill.Price;
        return new PaperPosition(
            order.AssetId,
            order.ConditionId,
            order.Outcome,
            newSize,
            averagePrice,
            estimatedValue,
            estimatedValue - newCost,
            reconciledAtUtc,
            order.CopiedTraderWallet);
    }

    private static string AttachLiveFillAccounting(
        string? rawDecisionJson,
        LiveOrder liveOrder,
        decimal fillPrice,
        decimal fillSize,
        decimal fillNotional,
        DateTimeOffset reconciledAtUtc,
        bool isFinal)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(rawDecisionJson)
                ? new JsonObject()
                : JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["paper_live_shadow_test"] = true;
        root["paper_live_shadow_reconciled_at_utc"] = reconciledAtUtc.ToString("O");
        root["live_order_id"] = liveOrder.Id.ToString();
        root["live_order_status"] = liveOrder.Status.ToString();
        root["live_order_response_status"] = liveOrder.ResponseStatus;
        root["live_clob_order_id"] = liveOrder.OrderId;
        root["live_order_price"] = liveOrder.Price;
        root["live_order_notional_usd"] = liveOrder.NotionalUsd;
        root["live_order_size_shares"] = liveOrder.SizeShares;
        root["live_filled_size"] = liveOrder.FilledSize;
        root["live_filled_notional_usd"] = liveOrder.FilledNotionalUsd;
        root["live_cost_basis_usd"] = liveOrder.CostBasisUsd;
        root["live_average_fill_price"] = liveOrder.AverageFillPrice;
        root["actual_fill_price"] = fillPrice;
        root["actual_fill_size_shares"] = fillSize;
        root["actual_fill_notional_usd"] = fillNotional;
        if (isFinal)
        {
            root["source"] = ActualFillExecutionSource;
            root["paper_live_shadow_actual_fill"] = true;
            root["paper_fill_model"] = ActualFillModel;
            root["paper_fill_source"] = ActualFillModel;
            root["actual_fill_copied_at_utc"] = reconciledAtUtc.ToString("O");
        }

        return root.ToJsonString();
    }

    private static string BuildEvidence(
        LiveOrder liveOrder,
        decimal fillPrice,
        decimal fillSize,
        decimal fillNotional,
        DateTimeOffset reconciledAtUtc,
        bool isFinal)
    {
        return JsonSerializer.Serialize(new
        {
            source = ActualFillExecutionSource,
            model = ActualFillModel,
            authority = isFinal ? "final" : "interim",
            live_order_id = liveOrder.Id,
            live_exchange_order_id = liveOrder.OrderId,
            correlation_id = liveOrder.CorrelationId,
            live_status = liveOrder.Status.ToString(),
            live_response_status = liveOrder.ResponseStatus,
            fill_price = fillPrice,
            fill_size_shares = fillSize,
            fill_notional_usd = fillNotional,
            reconciled_at_utc = reconciledAtUtc
        });
    }
}

internal sealed record CanonicalPaperLiveShadowState(
    PaperOrder PaperOrder,
    PaperFill PaperFill,
    PaperPosition PaperPosition,
    bool IsFinal);
