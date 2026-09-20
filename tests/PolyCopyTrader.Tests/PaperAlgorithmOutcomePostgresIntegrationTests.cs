using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;
using static PolyCopyTrader.Tests.PaperOutcomeConfirmationPostgresIntegrationTests;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class PaperAlgorithmOutcomePostgresIntegrationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ProvisionalIsDurableAndNonfinancial_FinalReplacesOnce(bool preliminaryWin, bool finalWin)
    {
        var repository = await RepositoryAsync();
        var seed = await ActiveAsync(repository);
        var outcome = Preliminary(seed, preliminaryWin);
        await repository.RecordPaperAlgorithmOutcomeAsync(outcome);
        await repository.RecordPaperAlgorithmOutcomeAsync(outcome);
        repository = await RepositoryAsync();
        Assert.Equal(preliminaryWin ? -1 : 1, await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT paper_lost_counter FROM strategies WHERE id=(SELECT strategy_id FROM paper_orders WHERE id=@Id)", seed.Order.Id));
        Assert.Equal("Entered", await ScalarAsync<string>("SELECT status FROM strategy_market_paper_runs WHERE paper_order_id=@Id", seed.Order.Id));
        Assert.Equal(12m, (await repository.GetPaperPositionAsync(seed.Order.CopiedTraderWallet, seed.Order.AssetId))!.SizeShares);
        Assert.False((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        var write = await FinalWriteAsync(repository, seed, finalWin);
        Assert.True((await repository.PersistFinalPaperRunAsync(write)).Applied);
        Assert.Equal(finalWin ? -1 : 1, await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
        Assert.True((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        Assert.Equal(finalWin ? 5.8m : -6.2m, await ScalarAsync<decimal>("SELECT net_realized_pnl_usd FROM strategy_market_paper_runs WHERE paper_order_id=@Id", seed.Order.Id));
        Assert.False((await repository.PersistFinalPaperRunAsync(write)).Applied);
        await repository.RecordPaperAlgorithmOutcomeAsync(outcome with { ObservedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1) });
        Assert.Equal(finalWin ? -1 : 1, await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
    }

    [Fact]
    public async Task ReplacementResetAndDisableDoNotResurrectAnOldContribution()
    {
        var repository = await RepositoryAsync(); var seed = await ActiveAsync(repository);
        var first = Preliminary(seed, false);
        await repository.RecordPaperAlgorithmOutcomeAsync(first);
        await repository.RecordPaperAlgorithmOutcomeAsync(Preliminary(seed, true) with { ObservedAtUtc = first.ObservedAtUtc.AddSeconds(1) });
        Assert.Equal(-1, await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
        await repository.SetStrategyStakeAmountsAsync(seed.Order.StrategyId, 1, 1, 1, 1, 0, 0, DateTimeOffset.UtcNow);
        await repository.SetStrategyStakeAmountsAsync(seed.Order.StrategyId, 1, 1, 2, 1, 0, 0, DateTimeOffset.UtcNow);
        await repository.RecordPaperAlgorithmOutcomeAsync(first with { ObservedAtUtc = first.ObservedAtUtc.AddMinutes(1) });
        Assert.Equal(0, await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
        await repository.PersistFinalPaperRunAsync(await FinalWriteAsync(repository, seed, false));
        Assert.Equal(1, await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    public async Task OnlyActuallyFilledOrdersContribute(decimal filled, int expected)
    {
        var repository = await RepositoryAsync(); var seed = await ActiveAsync(repository);
        await SqlAsync("DELETE FROM paper_fills WHERE paper_order_id=@Id", seed.Order.Id);
        if (filled > 0) await repository.AddPaperFillAsync(new PaperFill(Guid.NewGuid(), seed.Order.Id, .5m, filled, DateTimeOffset.UtcNow, "partial"));
        await repository.RecordPaperAlgorithmOutcomeAsync(Preliminary(seed, false));
        Assert.Equal(expected, await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
    }

    internal static async Task<Seed> ActiveAsync(PostgresAppRepository repository)
    {
        var seed = await SeedAsync(repository, false, 0);
        await SqlAsync("""
            DELETE FROM paper_position_settlements WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id);
            UPDATE strategy_market_paper_runs SET status='Entered',settlement_price=NULL,settlement_value_usd=NULL,
                realized_pnl_usd=NULL,net_realized_pnl_usd=NULL,settled_at_utc=NULL WHERE paper_order_id=@Id;
            UPDATE strategies SET paper_lost_counter=0 WHERE id=(SELECT strategy_id FROM paper_orders WHERE id=@Id);
            """, seed.Order.Id);
        await repository.UpsertPaperPositionAsync(new PaperPosition(seed.Order.AssetId, seed.Order.ConditionId, "Up",12,.5m,6,0,
            DateTimeOffset.UtcNow,seed.Order.CopiedTraderWallet,FeeUsd:.2m,FeeAccountingStatus:"Calculated"));
        return seed;
    }

    internal static PaperAlgorithmOutcome Preliminary(Seed seed, bool won) => new(seed.RunId,seed.Order.StrategyId,
        seed.Order.ConditionId,seed.Order.ConditionId,seed.Order.AssetId,seed.Order.Outcome,
        won ? seed.Order.AssetId : seed.Order.AssetId+"-other",won?"Up":"Down","BinanceTimedClose","{}",DateTimeOffset.UtcNow);

    internal static async Task<FinalPaperRunSettlement> FinalWriteAsync(PostgresAppRepository repository, Seed seed, bool won)
    {
        var run = Assert.Single(await repository.GetStrategyMarketPaperRunsByPaperOrderIdsAsync([seed.Order.Id]));
        var now=DateTimeOffset.UtcNow;
        var evidence=FinalMarketOutcomeEvidence.FromGamma(seed.Order.ConditionId,seed.Order.ConditionId,
            [seed.Order.AssetId,seed.Order.AssetId+"-other"],["Up","Down"],won?"Up":"Down",
            System.Text.Json.JsonSerializer.Serialize(new { umaResolutionStatus="resolved",outcomePrices=won?"[\"1\",\"0\"]":"[\"0\",\"1\"]" }),now)!;
        var settlement=new PaperPositionSettlement(Guid.NewGuid(),seed.Order.CopiedTraderWallet,seed.Order.AssetId,
            seed.Order.ConditionId,"Up",evidence.WinningAssetId,evidence.WinningOutcome,"crypto",12,.5m,6,won?12:0,won?6:-6,
            won,"GammaClosedMarket",now,now,FeeUsd:.2m,FeeAccountingStatus:"Calculated",NetRealizedPnlUsd:won?5.8m:-6.2m);
        var position=(await repository.GetPaperPositionAsync(seed.Order.CopiedTraderWallet,seed.Order.AssetId))! with
            { SizeShares=0,AveragePrice=0,EstimatedValueUsd=0,UnrealizedPnlUsd=0,FeeUsd=0,NetUnrealizedPnlUsd=0,UpdatedAtUtc=now };
        return new(run with { Status="Settled",SettlementPrice=won?1:0,SettlementValueUsd=won?12:0,
            RealizedPnlUsd=won?6:-6,NetRealizedPnlUsd=won?5.8m:-6.2m,SettledAtUtc=now,UpdatedAtUtc=now },position,settlement,evidence);
    }
}
