using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Startup;

public static class StrategyStakeAdminCommand
{
    private const string PaperLiveShadowTestSource = "paper_live_shadow_test";
    private const int StrategyAdminFetchLimit = int.MaxValue;

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        decimal paperStakeAmount,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var repository = new PostgresAppRepository(new PostgresConnectionFactory(configuration.Storage));
        return await ExecuteAsync(repository, paperStakeAmount, liveStakeAmount: null, output, cancellationToken);
    }

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        decimal paperStakeAmount,
        decimal liveStakeAmount,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var repository = new PostgresAppRepository(new PostgresConnectionFactory(configuration.Storage));
        return await ExecuteAsync(repository, paperStakeAmount, liveStakeAmount, output, cancellationToken);
    }

    public static async Task<int> ExecuteAsync(
        IAppRepository repository,
        decimal paperStakeAmount,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(repository, paperStakeAmount, liveStakeAmount: null, output, cancellationToken);
    }

    public static async Task<int> ExecuteAsync(
        IAppRepository repository,
        decimal paperStakeAmount,
        decimal? liveStakeAmount,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(output);

        if (paperStakeAmount <= 0m)
        {
            await output.WriteLineAsync("Paper stake amount must be greater than zero.");
            return 1;
        }

        if (liveStakeAmount is <= 0m)
        {
            await output.WriteLineAsync("Live stake amount must be greater than zero.");
            return 1;
        }

        var strategies = await repository.GetStrategyPerformanceAsync(StrategyAdminFetchLimit, cancellationToken);
        if (strategies.Count == 0)
        {
            await output.WriteLineAsync("No strategies found.");
            return 1;
        }

        var updatedAtUtc = DateTimeOffset.UtcNow;
        var updated = 0;
        var failed = 0;
        foreach (var strategy in strategies)
        {
            var applied = await repository.SetStrategyStakeAmountsAsync(
                strategy.StrategyId,
                paperStakeAmount,
                liveStakeAmount ?? strategy.LiveStakeAmount,
                updatedAtUtc,
                cancellationToken);

            if (applied)
            {
                updated++;
            }
            else
            {
                failed++;
            }
        }

        await output.WriteLineAsync($"Paper stake target: {paperStakeAmount:0.########}");
        if (liveStakeAmount is { } liveStakeTarget)
        {
            await output.WriteLineAsync($"Live stake target: {liveStakeTarget:0.########}");
        }

        await output.WriteLineAsync($"Strategies found: {strategies.Count}");
        await output.WriteLineAsync($"Strategies updated: {updated}");
        await output.WriteLineAsync($"Strategies failed: {failed}");

        return failed == 0 && updated > 0 ? 0 : 1;
    }

    public static async Task<int> ExecuteLiveStakesOnlyAsync(
        AppConfiguration configuration,
        string strategyCode,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var repository = new PostgresAppRepository(new PostgresConnectionFactory(configuration.Storage));
        return await ExecuteLiveStakesOnlyAsync(repository, [strategyCode], output, cancellationToken);
    }

    public static async Task<int> ExecuteLiveStakesOnlyAsync(
        AppConfiguration configuration,
        IReadOnlyCollection<string> strategyCodes,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var repository = new PostgresAppRepository(new PostgresConnectionFactory(configuration.Storage));
        return await ExecuteLiveStakesOnlyAsync(repository, strategyCodes, output, cancellationToken);
    }

    public static async Task<int> ExecuteLiveStakesOnlyAsync(
        IAppRepository repository,
        string strategyCode,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        return await ExecuteLiveStakesOnlyAsync(repository, [strategyCode], output, cancellationToken);
    }

    public static async Task<int> ExecuteLiveStakesOnlyAsync(
        IAppRepository repository,
        IReadOnlyCollection<string> strategyCodes,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(strategyCodes);

        var normalizedCodes = strategyCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedCodes.Length == 0)
        {
            await output.WriteLineAsync("At least one strategy code must be provided.");
            return 1;
        }

        var strategies = await repository.GetStrategyPerformanceAsync(StrategyAdminFetchLimit, cancellationToken);
        var strategyByCode = strategies.ToDictionary(
            strategy => strategy.Code,
            StringComparer.OrdinalIgnoreCase);
        var missingCodes = normalizedCodes
            .Where(code => !strategyByCode.ContainsKey(code))
            .ToArray();
        if (missingCodes.Length > 0)
        {
            await output.WriteLineAsync($"Strategy code not found: {string.Join(", ", missingCodes)}");
            return 1;
        }

        var targets = normalizedCodes
            .Select(code => strategyByCode[code])
            .ToArray();
        var targetIds = targets
            .Select(target => target.StrategyId)
            .ToHashSet();
        var updatedAtUtc = DateTimeOffset.UtcNow;
        var enabled = 0;
        var disabled = 0;
        var failed = 0;
        foreach (var strategy in strategies)
        {
            var liveStakes = targetIds.Contains(strategy.StrategyId);
            var applied = await repository.SetStrategyLiveStakesAsync(
                strategy.StrategyId,
                liveStakes,
                updatedAtUtc,
                cancellationToken);

            if (!applied)
            {
                failed++;
                continue;
            }

            if (liveStakes)
            {
                enabled++;
            }
            else
            {
                disabled++;
            }
        }

        await output.WriteLineAsync($"Live stakes enabled only for: {string.Join(", ", targets.Select(target => target.Code))}");
        await output.WriteLineAsync($"Target strategy ids: {string.Join(", ", targets.Select(target => target.StrategyId))}");
        await output.WriteLineAsync($"Strategies found: {strategies.Count}");
        await output.WriteLineAsync($"Strategies live-enabled: {enabled}");
        await output.WriteLineAsync($"Strategies live-disabled: {disabled}");
        await output.WriteLineAsync($"Strategies failed: {failed}");

        return failed == 0 && enabled == targets.Length ? 0 : 1;
    }

    public static async Task<int> DisableAllLiveStakesAsync(
        AppConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var repository = new PostgresAppRepository(new PostgresConnectionFactory(configuration.Storage));
        return await DisableAllLiveStakesAsync(repository, output, cancellationToken);
    }

    public static async Task<int> DisableAllLiveStakesAsync(
        IAppRepository repository,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(output);

        var strategies = await repository.GetStrategyPerformanceAsync(StrategyAdminFetchLimit, cancellationToken);
        var updatedAtUtc = DateTimeOffset.UtcNow;
        var disabled = 0;
        var failed = 0;
        foreach (var strategy in strategies)
        {
            var applied = await repository.SetStrategyLiveStakesAsync(
                strategy.StrategyId,
                false,
                updatedAtUtc,
                cancellationToken);

            if (applied)
            {
                disabled++;
            }
            else
            {
                failed++;
            }
        }

        await output.WriteLineAsync("Live stakes disabled for all strategies.");
        await output.WriteLineAsync($"Strategies found: {strategies.Count}");
        await output.WriteLineAsync($"Strategies live-disabled: {disabled}");
        await output.WriteLineAsync($"Strategies failed: {failed}");

        return failed == 0 ? 0 : 1;
    }

    public static async Task<int> PrintLiveShadowStateAsync(
        AppConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var repository = new PostgresAppRepository(new PostgresConnectionFactory(configuration.Storage));
        return await PrintLiveShadowStateAsync(repository, output, cancellationToken);
    }

    public static async Task<int> PrintLiveShadowStateAsync(
        IAppRepository repository,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(output);

        var strategies = await repository.GetStrategyPerformanceAsync(StrategyAdminFetchLimit, cancellationToken);
        var liveStrategies = strategies.Where(strategy => strategy.LiveStakes).ToArray();
        await output.WriteLineAsync($"LiveStakes strategies: {liveStrategies.Length}");
        foreach (var strategy in liveStrategies)
        {
            await output.WriteLineAsync(
                $"Strategy {strategy.Code}; id={strategy.StrategyId}; liveStake={strategy.LiveStakeAmount:0.########}; liveBalance={strategy.LiveAvailableBalance:0.########}; liveOpen={strategy.LiveOpenOrdersCount}; liveOrders={strategy.LiveOrdersCount}");
        }

        var recentPaper = await repository.GetRecentPaperOrdersAsync(50, cancellationToken);
        var shadowPaper = recentPaper
            .Where(order => string.Equals(order.ExecutionSource, PaperLiveShadowTestSource, StringComparison.Ordinal))
            .Take(10)
            .ToArray();
        await output.WriteLineAsync($"Recent shadow paper orders: {shadowPaper.Length}");
        foreach (var order in shadowPaper)
        {
            await output.WriteLineAsync(
                $"Paper {order.CreatedAtUtc:O}; status={order.Status}; strategy={order.StrategyId}; corr={order.CorrelationId}; outcome={order.Outcome}; price={order.Price:0.########}; size={order.SizeShares:0.########}; notional={order.NotionalUsd:0.########}");
        }

        var recentLive = await repository.GetRecentLiveOrdersAsync(50, cancellationToken);
        var shadowLive = recentLive
            .Where(order => string.Equals(order.ExecutionSource, PaperLiveShadowTestSource, StringComparison.Ordinal))
            .Take(10)
            .ToArray();
        await output.WriteLineAsync($"Recent shadow live orders: {shadowLive.Length}");
        foreach (var order in shadowLive)
        {
            await output.WriteLineAsync(
                $"Live {order.CreatedAtUtc:O}; status={order.Status}; response={order.ResponseStatus}; strategy={order.StrategyId}; corr={order.CorrelationId}; orderId={Shorten(order.OrderId)}; outcome={order.Outcome}; price={order.Price:0.########}; size={order.SizeShares:0.########}; filled={order.FilledSize:0.########}; remaining={order.RemainingSize:0.########}; avg={order.AverageFillPrice:0.########}; cost={order.CostBasisUsd:0.########}; balanceApplied={order.BalanceEffectApplied}; settlement={order.SettlementValueUsd:0.########}; pnl={order.RealizedPnlUsd:0.########}; winning={order.WinningOutcome}; settlementSource={order.SettlementSource}; validation={Trim(order.ValidationSummary, 300)}; raw={Trim(order.RawResponseJson, 500)}");
        }

        var events = await repository.GetRecentLiveTradingEventsAsync(50, cancellationToken);
        var shadowEvents = events
            .Where(item => item.Action.Contains("PaperLiveShadow", StringComparison.Ordinal))
            .Take(10)
            .ToArray();
        await output.WriteLineAsync($"Recent shadow live events: {shadowEvents.Length}");
        foreach (var liveEvent in shadowEvents)
        {
            await output.WriteLineAsync(
                $"Event {liveEvent.CreatedAtUtc:O}; action={liveEvent.Action}; status={liveEvent.Status}; details={liveEvent.Details}");
        }

        return 0;
    }

    public static async Task<int> PrintLiveShadowExchangeStatusAsync(
        AppConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(output);

        var repository = new PostgresAppRepository(new PostgresConnectionFactory(configuration.Storage));
        using var httpClient = new HttpClient();
        var tradingClient = new PolymarketTradingClient(
            httpClient,
            configuration.Polymarket,
            configuration.PolymarketAuth,
            PolymarketSecretProviderFactory.Create(configuration.PolymarketAuth),
            new ClobV2OrderBuilder(new OrderAmountCalculator()),
            new ClobV2OrderSigner(),
            new ClobV2OrderPayloadSerializer(),
            new PolymarketAuthHeaderFactory(new PolymarketL2HmacSigner()),
            new NullPolymarketApiErrorSink(),
            new NullPolymarketHttpLogSink());

        var recentLive = await repository.GetRecentLiveOrdersAsync(50, cancellationToken);
        var shadowLive = recentLive
            .Where(order =>
                string.Equals(order.ExecutionSource, PaperLiveShadowTestSource, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(order.OrderId))
            .Take(10)
            .ToArray();

        await output.WriteLineAsync($"Recent shadow live exchange-status checks: {shadowLive.Length}");
        foreach (var order in shadowLive)
        {
            try
            {
                var status = await tradingClient.GetLiveOrderStatusAsync(order.OrderId!, cancellationToken);
                if (status is null)
                {
                    await output.WriteLineAsync(
                        $"Live {order.CreatedAtUtc:O}; orderId={Shorten(order.OrderId)}; dbStatus={order.Status}; dbFilled={order.FilledSize:0.########}; exchangeStatus=NotFound");
                    continue;
                }

                await output.WriteLineAsync(
                    $"Live {order.CreatedAtUtc:O}; orderId={Shorten(order.OrderId)}; dbStatus={order.Status}; dbFilled={order.FilledSize:0.########}; exchangeStatus={status.Status}; exchangeMatched={FromTokenUnits(status.SizeMatched):0.########}; exchangeOriginal={FromTokenUnits(status.OriginalSize):0.########}; exchangePrice={status.Price}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await output.WriteLineAsync(
                    $"Live {order.CreatedAtUtc:O}; orderId={Shorten(order.OrderId)}; dbStatus={order.Status}; dbFilled={order.FilledSize:0.########}; exchangeStatus=Error; error={ex.GetType().Name}");
            }
        }

        return 0;
    }

    private static decimal FromTokenUnits(string value)
    {
        return decimal.TryParse(value, out var units) ? units / 1_000_000m : 0m;
    }

    private static string Shorten(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Length <= 12
            ? value
            : string.Concat(value.AsSpan(0, 6), "...", value.AsSpan(value.Length - 4, 4));
    }

    private static string Trim(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Length <= length ? value : value[..length];
    }
}
