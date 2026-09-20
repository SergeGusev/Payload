using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;
using System.Reflection;
using static PolyCopyTrader.Tests.PaperAlgorithmOutcomePostgresIntegrationTests;
using static PolyCopyTrader.Tests.PaperOutcomeConfirmationPostgresIntegrationTests;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class FinalOutcomeSettlementPostgresIntegrationTests
{
    [Fact]
    public async Task OfficialWebSocketSettlesGenericPositionWithoutAnotherVenueLookup()
    {
        var repository=await RepositoryAsync();var seed=await ActiveAsync(repository);var order=seed.Order;var now=DateTimeOffset.UtcNow;
        await SqlAsync("UPDATE strategy_market_paper_runs SET status='Skipped' WHERE paper_order_id=@Id",order.Id);
        var market=new PolymarketGammaMarket(order.ConditionId,order.ConditionId,"question",order.ConditionId,"test",
            null,null,null,null,"crypto",false,true,false,false,false,true,false,null,null,null,null,null,null,null,
            null,null,now.AddMinutes(-5),now,now.AddMinutes(-5),["Up","Down"],[order.AssetId,order.AssetId+"-other"],"{}",now);
        await repository.UpsertPolymarketGammaMarketAsync(market);
        var raw=System.Text.Json.JsonSerializer.Serialize(new { event_type="market_resolved",market=order.ConditionId,
            winning_asset_id=order.AssetId+"-other",winning_outcome="Down" });
        var update=new MarketDataUpdate(MarketDataEventType.MarketResolved,"market_resolved",order.AssetId,
            order.ConditionId,null,null,null,null,null,TradeSide.Unknown,true,now,RawJson:raw,
            WinningAssetId:order.AssetId+"-other",WinningOutcome:"Down");
        var processor=new PaperSettlementProcessor(NullLogger<PaperSettlementProcessor>.Instance,
            DispatchProxy.Create<IPolymarketGammaClient, NoVenueLookup>(),new ExposureSnapshotCache(repository),repository);
        Assert.Equal(1,(await processor.SettleMarketResolutionAsync(update)).SettlementsInserted);
        Assert.True((await repository.GetPaperOrderAsync(order.Id))!.Confirmed);
        Assert.Equal(0,(await processor.SettleMarketResolutionAsync(update)).SettlementsInserted);
        Assert.Equal(0m,(await repository.GetPaperPositionAsync(order.CopiedTraderWallet,order.AssetId))!.SizeShares);
    }

    public class NoVenueLookup : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => throw new InvalidOperationException("Final WebSocket evidence must not trigger another venue lookup.");
    }
    [Fact]
    public async Task ClosedZeroFillCanConfirm_EnteredOrOpenInventoryCannot()
    {
        var repository=await RepositoryAsync();var seed=await ActiveAsync(repository);
        var evidence=(await FinalWriteAsync(repository,seed,false)).Evidence;
        await repository.ConfirmFinalPaperOrderAsync(seed.Order.Id,evidence);
        Assert.False((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        await SqlAsync("""
            DELETE FROM paper_fills WHERE paper_order_id=@Id;
            UPDATE strategy_market_paper_runs SET status='Skipped' WHERE paper_order_id=@Id;
            UPDATE paper_orders SET status='Cancelled' WHERE id=@Id;
            UPDATE paper_positions SET size_shares=0 WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id);
            """,seed.Order.Id);
        await repository.ConfirmFinalPaperOrderAsync(seed.Order.Id,evidence);
        Assert.True((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
    }

    [Fact]
    public async Task CounterReadersSeeOneAtomicTransitionDuringFinalCommit()
    {
        var repository=await RepositoryAsync();var seed=await ActiveAsync(repository);
        await repository.RecordPaperAlgorithmOutcomeAsync(Preliminary(seed,true));
        var write=await FinalWriteAsync(repository,seed,false);
        await using var connection=new NpgsqlConnection(ConnectionString);await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await using(var command=new NpgsqlCommand("SELECT id FROM paper_orders WHERE id=@Id FOR UPDATE",connection,transaction))
        { command.Parameters.AddWithValue("Id",seed.Order.Id);await command.ExecuteScalarAsync(); }
        var final=repository.PersistFinalPaperRunAsync(write);
        try
        {
            for(var i=0;i<20;i++) Assert.Equal(-1,await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
            Assert.False(final.IsCompleted);
        }
        finally { await transaction.RollbackAsync(); }
        Assert.True((await final.WaitAsync(TimeSpan.FromSeconds(5))).Applied);
        Assert.Equal(1,await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
    }

    [Fact]
    public async Task LedgerRetainsFinalEvidenceAndRejectsLateProvisionalOverwrite()
    {
        var repository=await RepositoryAsync();var suffix=Guid.NewGuid().ToString("N");var now=DateTimeOffset.UtcNow;
        var market=new PolymarketGammaMarket("market-"+suffix,"condition-"+suffix,"question","slug-"+suffix,"test",
            null,null,null,null,"crypto",false,true,false,false,false,true,false,null,null,null,null,null,null,null,
            null,null,now.AddMinutes(-5),now,now.AddMinutes(-5),["Up","Down"],["up-"+suffix,"down-"+suffix],"{}",now);
        await repository.UpsertPolymarketGammaMarketAsync(market);
        var start=new DateTimeOffset(now.Ticks-now.Ticks%10,TimeSpan.Zero).AddMinutes(-5);
        var row=new CryptoUpDown5mWebSocketResolvedMarket(Guid.NewGuid(),"ETH",market.MarketId,market.ConditionId,market.Slug,
            start,start.AddMinutes(5),"Up",market.ClobTokenIds[0],now,now,now,1,0,"BinanceTimedClose","binance_timed_close_provisional","{}",now,now);
        await repository.UpsertCryptoUpDown5mWebSocketResolvedMarketAsync(row);
        var raw=System.Text.Json.JsonSerializer.Serialize(new { event_type="market_resolved",market=market.ConditionId,
            winning_asset_id=market.ClobTokenIds[1],winning_outcome="Down" });
        await repository.UpsertCryptoUpDown5mWebSocketResolvedMarketAsync(row with { WinningOutcome="Down",WinningAssetId=market.ClobTokenIds[1],
            Source="MarketWebSocket",RawEventType="market_resolved",RawJson=raw });
        await repository.UpsertCryptoUpDown5mWebSocketResolvedMarketAsync(row with { EventTimestampUtc=now.AddSeconds(10) });
        var stored=Assert.Single(await repository.GetCryptoUpDown5mWebSocketResolvedMarketsAsync(["ETH"],start,start));
        Assert.Equal("Down",stored.WinningOutcome);Assert.Equal(market.ClobTokenIds[1],stored.WinningAssetId);
        Assert.NotNull(FinalMarketOutcomeEvidence.FromLedger(market,stored));
    }
    [Theory]
    [InlineData("Pending")]
    [InlineData("PartiallyFilled")]
    [InlineData("Filled")]
    public async Task FinalRecordsCommitWithEvidence_ConfirmationWaitsForReadiness(string status)
    {
        var repository=await RepositoryAsync();var seed=await ActiveAsync(repository);
        await repository.UpdatePaperOrderAsync(seed.Order with { Status=Enum.Parse<PaperOrderStatus>(status) });
        var write=await FinalWriteAsync(repository,seed,true);
        await repository.PersistFinalPaperRunAsync(write);
        Assert.Equal(status=="Filled",(await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        Assert.True(await ScalarAsync<bool>("SELECT skip_diagnostics_json ? 'final_outcome' FROM strategy_market_paper_runs WHERE paper_order_id=@Id",seed.Order.Id));
    }

    [Fact]
    public async Task PersistenceFailureRollsBackPositionRunCounterAndProvisionalRetirement()
    {
        var repository=await RepositoryAsync();var seed=await ActiveAsync(repository);
        await repository.RecordPaperAlgorithmOutcomeAsync(Preliminary(seed,true));
        var write=await FinalWriteAsync(repository,seed,false);
        // Valid JSON scalar fails when the final audit object is merged after position writes.
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.PersistFinalPaperRunAsync(
            write with { Run=write.Run with { SkipDiagnosticsJson="[]" } }));
        Assert.Equal("Entered",await ScalarAsync<string>("SELECT status FROM strategy_market_paper_runs WHERE paper_order_id=@Id",seed.Order.Id));
        Assert.Equal(12m,(await repository.GetPaperPositionAsync(seed.Order.CopiedTraderWallet,seed.Order.AssetId))!.SizeShares);
        Assert.Equal(-1,await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
        Assert.False((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        Assert.True((await repository.PersistFinalPaperRunAsync(write)).Applied);
    }

    [Fact]
    public async Task SyntheticMakerIdentityUsesExactConditionAndTokens()
    {
        var repository=await RepositoryAsync();var seed=await ActiveAsync(repository);
        await SqlAsync("UPDATE strategy_market_paper_runs SET market_id=market_id||':maker:Up' WHERE paper_order_id=@Id",seed.Order.Id);
        await repository.PersistFinalPaperRunAsync(await FinalWriteAsync(repository,seed,false));
        Assert.True((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
    }

    [Fact]
    public async Task LinkedLiveBlocksConfirmationButRetainsFinalProof()
    {
        var repository=await RepositoryAsync();var seed=await ActiveAsync(repository);var order=seed.Order;
        var live=new LiveOrder(Guid.NewGuid(),order.SignalId,LiveOrderStatus.Matched,"venue-id",TradeSide.Buy,
            order.AssetId,order.ConditionId,order.Outcome,.5m,12m,6m,"FAK",order.CreatedAtUtc,order.ExpiresAtUtc,
            order.CreatedAtUtc,"matched",12m,0m,"","{}","actual fill",seed.Settled,
            StrategyId:order.StrategyId,AverageFillPrice:.5m,FilledNotionalUsd:6m,CostBasisUsd:6m,FeeUsd:.2m,
            PaperOrderId:order.Id,FeeAccountingStatus:"VenueReported");
        await repository.AddLiveOrderAsync(live);
        var write=await FinalWriteAsync(repository,seed,false);
        await repository.PersistFinalPaperRunAsync(write);
        Assert.False((await repository.GetPaperOrderAsync(order.Id))!.Confirmed);
        Assert.True(await ScalarAsync<bool>("SELECT skip_diagnostics_json ? 'final_outcome' FROM strategy_market_paper_runs WHERE paper_order_id=@Id",order.Id));
        await repository.ApplyLiveOrderSettlementToStrategyBalanceWithConcurrencyAsync(live.Id,live.StrategyId,0,-6,-6.2m,write.Evidence.WinningAssetId,write.Evidence.WinningOutcome,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,live.RowVersion,write.Evidence);
        Assert.True((await repository.GetPaperOrderAsync(order.Id))!.Confirmed);
    }
}
