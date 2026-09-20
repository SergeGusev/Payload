using System.Reflection;
using System.Runtime.ExceptionServices;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class FinalMarketOutcomeEvidenceTests
{
    [Theory]
    [InlineData("resolved", "1", "0", true)]
    [InlineData("settled", "0", "1", false)]
    [InlineData("proposed", "1", "0", false)]
    [InlineData("resolved", "0.999", "0.001", false)]
    [InlineData("resolved", "1", "1", false)]
    public void GammaRequiresFinalOracleAndExactUniquePayout(string status, string up, string down, bool accepted)
    {
        var raw = System.Text.Json.JsonSerializer.Serialize(new { umaResolutionStatus = status,
            outcomePrices = System.Text.Json.JsonSerializer.Serialize(new[] { up, down }) });
        Assert.Equal(accepted, FinalMarketOutcomeEvidence.FromGamma("market", "condition", ["up", "down"],
            ["Up", "Down"], "Up", raw, DateTimeOffset.UtcNow) is not null);
    }

    [Theory]
    [InlineData("condition", "up", "Up", true)]
    [InlineData("other", "up", "Up", false)]
    [InlineData("condition", "up", "Down", false)]
    [InlineData("condition", "missing", "Up", false)]
    public void WebSocketMustMatchRawIdentity(string condition, string winner, string outcome, bool accepted)
    {
        var raw = System.Text.Json.JsonSerializer.Serialize(new { event_type = "market_resolved", market = condition,
            winning_asset_id = winner, winning_outcome = outcome });
        var final = FinalMarketOutcomeEvidence.FromWebSocket("market", "condition", ["up", "down"],
            ["Up", "Down"], raw, DateTimeOffset.UtcNow);
        Assert.Equal(accepted, final is not null);
        if (final is not null) Assert.False(final.Matches("next-condition", "up", "Up"));
    }
}

// Adapts the existing sealed in-memory fake only. Atomicity is tested against PostgreSQL.
public class FinalSettlementTestRepository : DispatchProxy
{
    internal TestAppRepository Inner = null!;
    internal static IAppRepository Wrap(TestAppRepository inner)
    {
        var proxy = Create<IAppRepository, FinalSettlementTestRepository>();
        ((FinalSettlementTestRepository)(object)proxy).Inner = inner;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(IAppRepository.PersistFinalPaperPositionsAsync))
            return Inner.PersistPaperPositionSettlementBatchAsync((IReadOnlyList<PaperPositionSettlementWrite>)args![0]!,
                (Action<PaperSettlementPersistenceStageEvent>)args[2]!, (CancellationToken)args[3]!);
        if (method.Name == nameof(IAppRepository.PersistFinalPaperRunAsync))
            return PersistRun((FinalPaperRunSettlement)args![0]!, (CancellationToken)args[1]!);
        if (method.Name == nameof(IAppRepository.ConfirmFinalPaperOrderAsync)) return Task.CompletedTask;
        if (method.Name == nameof(IAppRepository.GetEffectivePaperLostCounterAsync)) return Counter((Guid)args![0]!);
        if (method.Name == nameof(IAppRepository.RecordPaperAlgorithmOutcomeAsync)) return Task.CompletedTask;
        try { return method.Invoke(Inner, args); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
    private async Task<int> Counter(Guid id)
    {
        var settings = await Inner.GetStrategyRuntimeSettingsAsync();
        return settings.TryGetValue(id, out var setting) ? setting.PaperLostCounter : 0;
    }
    private async Task<StrategyLostCounterUpdateResult> PersistRun(FinalPaperRunSettlement write, CancellationToken token)
    {
        if (write.Settlement is not null) await Inner.TryAddPaperPositionSettlementAsync(write.Settlement, token);
        if (write.Position is not null) await Inner.UpsertPaperPositionAsync(write.Position, token);
        await Inner.UpdateStrategyMarketPaperRunAsync(write.Run, token);
        var settings = await ((IAppRepository)Inner).GetStrategyRuntimeSettingsAsync(token);
        var enabled = settings.TryGetValue(write.Run.StrategyId, out var setting) && setting.PaperLostCoeff > 1;
        return await Inner.UpdateStrategyLostCounterAfterSettlementAsync(write.Run.StrategyId, false,
            write.Run.SelectedAssetId == write.Evidence.WinningAssetId, enabled, write.Run.UpdatedAtUtc, token);
    }
}
