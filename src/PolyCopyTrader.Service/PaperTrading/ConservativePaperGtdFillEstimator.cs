using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class ConservativePaperGtdFillEstimator(BtcUpDown5mStrategyOptions options)
{
    private const string FillModelName = "conservative_gtd_queue_v1";
    private const string PaperLiveShadowTestSource = "paper_live_shadow_test";
    private static readonly HashSet<Guid> UpDownStrategyIds = new(StrategyIds.UpDown5mStrategyVariants.Select(variant => variant.Id));

    public ConservativePaperGtdFillEvaluation Evaluate(
        PaperOrder order,
        OrderBookSnapshot? orderBook,
        DateTimeOffset nowUtc,
        decimal previouslyFilledShares)
    {
        if (!IsEligible(order))
        {
            return ConservativePaperGtdFillEvaluation.CreateNotHandled(order);
        }

        var root = ParseRawDecisionJson(order.RawDecisionJson);
        var baseline = TryReadInitialBaseline(root) ?? TryReadCapturedBaseline(root);
        if (baseline is null)
        {
            if (orderBook is null)
            {
                return ConservativePaperGtdFillEvaluation.CreateHandled(order);
            }

            baseline = CreateCapturedBaseline(order, orderBook, nowUtc);
            ApplyBaseline(root, baseline, "baseline_captured");
            return ConservativePaperGtdFillEvaluation.CreateHandled(order with { RawDecisionJson = root.ToJsonString() }, orderChanged: true);
        }

        var remainingShares = Math.Max(0m, order.SizeShares - Math.Max(0m, previouslyFilledShares));
        if (remainingShares <= 0m)
        {
            return ConservativePaperGtdFillEvaluation.CreateHandled(order);
        }

        var immediateFill = TryCreateImmediateFill(order, baseline, nowUtc, previouslyFilledShares, remainingShares, root);
        if (immediateFill is not null)
        {
            return ConservativePaperGtdFillEvaluation.CreateHandled(
                order with { RawDecisionJson = root.ToJsonString() },
                immediateFill,
                orderChanged: true);
        }

        var lateFill = TryCreateLateFill(order, baseline, orderBook, nowUtc, remainingShares, root);
        if (lateFill is not null)
        {
            return ConservativePaperGtdFillEvaluation.CreateHandled(
                order with { RawDecisionJson = root.ToJsonString() },
                lateFill,
                orderChanged: true);
        }

        if (SetStatusIfChanged(root, orderBook is null ? "waiting_for_orderbook" : "waiting_for_high_confidence_evidence"))
        {
            root["paper_gtd_fill_model"] = FillModelName;
            return ConservativePaperGtdFillEvaluation.CreateHandled(
                order with { RawDecisionJson = root.ToJsonString() },
                orderChanged: true);
        }

        return ConservativePaperGtdFillEvaluation.CreateHandled(order);
    }

    private bool IsEligible(PaperOrder order)
    {
        if (!options.PaperGtdConservativeFillEnabled ||
            order.Side != TradeSide.Buy ||
            string.Equals(order.ExecutionSource, PaperLiveShadowTestSource, StringComparison.OrdinalIgnoreCase) ||
            !UpDownStrategyIds.Contains(order.StrategyId) ||
            string.IsNullOrWhiteSpace(order.RawDecisionJson))
        {
            return false;
        }

        var root = ParseRawDecisionJson(order.RawDecisionJson);
        return IsGtd(root) && IsOpeningLimit(root);
    }

    private static bool IsGtd(JsonObject root)
    {
        return string.Equals(GetString(root, "order_type"), "GTD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(GetString(root, "order_execution_mode"), "GTD", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpeningLimit(JsonObject root)
    {
        return string.Equals(GetString(root, "pricing_mode"), "opening_limit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(GetString(root, "pricing_mode"), "paper_gtd_limit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(GetString(root, "converted_to_gtd_limit_order"), "True", StringComparison.OrdinalIgnoreCase);
    }

    private PaperFill? TryCreateImmediateFill(
        PaperOrder order,
        ConservativePaperGtdBaseline baseline,
        DateTimeOffset nowUtc,
        decimal previouslyFilledShares,
        decimal remainingShares,
        JsonObject root)
    {
        if (!baseline.AllowImmediateFill)
        {
            return null;
        }

        var executableShares = baseline.ImmediateExecutableAskShares ?? 0m;
        if (executableShares <= previouslyFilledShares)
        {
            return null;
        }

        var depthMultiplier = Math.Max(0.000001m, options.PaperGtdImmediateFillDepthMultiplier);
        var conservativeExecutableShares = executableShares / depthMultiplier;
        var fillShares = Math.Min(remainingShares, Math.Max(0m, conservativeExecutableShares - previouslyFilledShares));
        if (fillShares <= 0m)
        {
            return null;
        }

        var observedAskVwap = baseline.ImmediateExecutableAskVwap ?? baseline.BestAsk;
        var fillPrice = order.Price;
        var evidence = string.Concat(
            "ConservativeGtdImmediateFill: initial submit snapshot had marketable asks within BUY limit. Limit=",
            Format(order.Price),
            " FillPrice=",
            Format(fillPrice),
            " ObservedAskVwap=",
            FormatNullable(observedAskVwap),
            " FillShares=",
            Format(fillShares),
            " InitialExecutableAskShares=",
            Format(executableShares),
            " PreviouslyFilledShares=",
            Format(previouslyFilledShares),
            " Confidence=High.");

        ApplyFillDiagnostics(root, "filled_immediate_marketable", "high", fillPrice, fillShares, nowUtc, null);
        return new PaperFill(Guid.NewGuid(), order.Id, fillPrice, fillShares, nowUtc, evidence);
    }

    private PaperFill? TryCreateLateFill(
        PaperOrder order,
        ConservativePaperGtdBaseline baseline,
        OrderBookSnapshot? orderBook,
        DateTimeOffset nowUtc,
        decimal remainingShares,
        JsonObject root)
    {
        if (orderBook is null ||
            nowUtc < order.CreatedAtUtc.AddSeconds(Math.Max(0, options.PaperGtdMinLateFillEvidenceSeconds)))
        {
            return null;
        }

        var currentBidQueueAtOrAboveLimit = GetBuyQueueAheadShares(orderBook, order.Price);
        var currentBestBid = orderBook.BestBid;
        var bidSideClearedThroughLimit = currentBidQueueAtOrAboveLimit <= 0m ||
            currentBestBid is null ||
            currentBestBid < order.Price;
        var lastTradeCrossedLimit = orderBook.LastTradePrice is { } lastTradePrice && lastTradePrice <= order.Price;

        if (!bidSideClearedThroughLimit || !lastTradeCrossedLimit)
        {
            root["paper_gtd_last_checked_at_utc"] = nowUtc.ToString("O", CultureInfo.InvariantCulture);
            root["paper_gtd_current_snapshot_at_utc"] = orderBook.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture);
            root["paper_gtd_current_best_bid"] = currentBestBid;
            root["paper_gtd_current_best_ask"] = orderBook.BestAsk;
            root["paper_gtd_current_last_trade_price"] = orderBook.LastTradePrice;
            root["paper_gtd_current_bid_queue_at_or_above_limit"] = currentBidQueueAtOrAboveLimit;
            return null;
        }

        var evidence = string.Concat(
            "ConservativeGtdLateFill: post-submit book/trade evidence shows market traded through BUY limit. Limit=",
            Format(order.Price),
            " FillShares=",
            Format(remainingShares),
            " InitialQueueAheadShares=",
            Format(baseline.QueueAheadShares ?? 0m),
            " CurrentBidQueueAtOrAboveLimit=",
            Format(currentBidQueueAtOrAboveLimit),
            " CurrentBestBid=",
            FormatNullable(currentBestBid),
            " LastTradePrice=",
            FormatNullable(orderBook.LastTradePrice),
            " Confidence=High.");

        ApplyFillDiagnostics(root, "filled_late_trade_through_limit", "high", order.Price, remainingShares, nowUtc, orderBook);
        return new PaperFill(Guid.NewGuid(), order.Id, order.Price, remainingShares, nowUtc, evidence);
    }

    private static ConservativePaperGtdBaseline? TryReadInitialBaseline(JsonObject root)
    {
        if (GetDateTime(root, "paper_gtd_initial_snapshot_at_utc") is not { } snapshotAtUtc)
        {
            return null;
        }

        return new ConservativePaperGtdBaseline(
            snapshotAtUtc,
            GetDecimal(root, "paper_gtd_initial_best_bid"),
            GetDecimal(root, "paper_gtd_initial_best_ask"),
            GetDecimal(root, "paper_gtd_initial_last_trade_price"),
            GetDecimal(root, "paper_gtd_initial_queue_ahead_shares"),
            GetDecimal(root, "paper_gtd_initial_executable_ask_shares"),
            GetDecimal(root, "paper_gtd_initial_executable_ask_vwap"),
            AllowImmediateFill: true);
    }

    private static ConservativePaperGtdBaseline? TryReadCapturedBaseline(JsonObject root)
    {
        if (GetDateTime(root, "paper_gtd_fill_baseline_at_utc") is not { } baselineAtUtc)
        {
            return null;
        }

        return new ConservativePaperGtdBaseline(
            baselineAtUtc,
            GetDecimal(root, "paper_gtd_fill_baseline_best_bid"),
            GetDecimal(root, "paper_gtd_fill_baseline_best_ask"),
            GetDecimal(root, "paper_gtd_fill_baseline_last_trade_price"),
            GetDecimal(root, "paper_gtd_fill_baseline_queue_ahead_shares"),
            null,
            null,
            AllowImmediateFill: false);
    }

    private static ConservativePaperGtdBaseline CreateCapturedBaseline(
        PaperOrder order,
        OrderBookSnapshot orderBook,
        DateTimeOffset nowUtc)
    {
        return new ConservativePaperGtdBaseline(
            nowUtc,
            orderBook.BestBid,
            orderBook.BestAsk,
            orderBook.LastTradePrice,
            GetBuyQueueAheadShares(orderBook, order.Price),
            null,
            null,
            AllowImmediateFill: false);
    }

    private static void ApplyBaseline(JsonObject root, ConservativePaperGtdBaseline baseline, string status)
    {
        root["paper_gtd_fill_model"] = FillModelName;
        root["paper_gtd_fill_model_status"] = status;
        root["paper_gtd_fill_baseline_at_utc"] = baseline.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture);
        root["paper_gtd_fill_baseline_best_bid"] = baseline.BestBid;
        root["paper_gtd_fill_baseline_best_ask"] = baseline.BestAsk;
        root["paper_gtd_fill_baseline_last_trade_price"] = baseline.LastTradePrice;
        root["paper_gtd_fill_baseline_queue_ahead_shares"] = baseline.QueueAheadShares;
        root["paper_gtd_fill_baseline_allows_immediate_fill"] = baseline.AllowImmediateFill;
    }

    private static void ApplyFillDiagnostics(
        JsonObject root,
        string status,
        string confidence,
        decimal fillPrice,
        decimal fillShares,
        DateTimeOffset nowUtc,
        OrderBookSnapshot? orderBook)
    {
        root["paper_gtd_fill_model"] = FillModelName;
        root["paper_gtd_fill_model_status"] = status;
        root["paper_gtd_fill_confidence"] = confidence;
        root["paper_gtd_fill_price"] = fillPrice;
        root["paper_gtd_fill_size_shares"] = fillShares;
        root["paper_gtd_fill_at_utc"] = nowUtc.ToString("O", CultureInfo.InvariantCulture);
        root["paper_gtd_current_snapshot_at_utc"] = orderBook?.SnapshotAtUtc.ToString("O", CultureInfo.InvariantCulture);
        root["paper_gtd_current_best_bid"] = orderBook?.BestBid;
        root["paper_gtd_current_best_ask"] = orderBook?.BestAsk;
        root["paper_gtd_current_last_trade_price"] = orderBook?.LastTradePrice;
    }

    private static bool SetStatusIfChanged(JsonObject root, string status)
    {
        root["paper_gtd_fill_model"] = FillModelName;
        if (string.Equals(GetString(root, "paper_gtd_fill_model_status"), status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        root["paper_gtd_fill_model_status"] = status;
        return true;
    }

    private static decimal GetBuyQueueAheadShares(OrderBookSnapshot orderBook, decimal limitPrice)
    {
        return orderBook.Bids
            .Where(level => level is { Price: > 0m, Size: > 0m } && level.Price >= limitPrice)
            .Sum(level => level.Size);
    }

    private static JsonObject ParseRawDecisionJson(string? rawDecisionJson)
    {
        if (string.IsNullOrWhiteSpace(rawDecisionJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(rawDecisionJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
        catch (InvalidOperationException)
        {
            return new JsonObject();
        }
    }

    private static string? GetString(JsonObject root, string name)
    {
        return root.TryGetPropertyValue(name, out var node) ? node?.ToString() : null;
    }

    private static decimal? GetDecimal(JsonObject root, string name)
    {
        if (!root.TryGetPropertyValue(name, out var node) || node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<decimal>();
        }
        catch (InvalidOperationException)
        {
            return decimal.TryParse(node.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
        catch (FormatException)
        {
            return decimal.TryParse(node.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
    }

    private static DateTimeOffset? GetDateTime(JsonObject root, string name)
    {
        var value = GetString(root, name);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    private static string FormatNullable(decimal? value)
    {
        return value is { } actual ? Format(actual) : "null";
    }

    private sealed record ConservativePaperGtdBaseline(
        DateTimeOffset SnapshotAtUtc,
        decimal? BestBid,
        decimal? BestAsk,
        decimal? LastTradePrice,
        decimal? QueueAheadShares,
        decimal? ImmediateExecutableAskShares,
        decimal? ImmediateExecutableAskVwap,
        bool AllowImmediateFill);
}

public sealed record ConservativePaperGtdFillEvaluation(
    bool Handled,
    PaperOrder Order,
    PaperFill? Fill = null,
    bool OrderChanged = false)
{
    public static ConservativePaperGtdFillEvaluation CreateNotHandled(PaperOrder order)
    {
        return new ConservativePaperGtdFillEvaluation(false, order);
    }

    public static ConservativePaperGtdFillEvaluation CreateHandled(
        PaperOrder order,
        PaperFill? fill = null,
        bool orderChanged = false)
    {
        return new ConservativePaperGtdFillEvaluation(true, order, fill, orderChanged);
    }
}
