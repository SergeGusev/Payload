using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.PaperTrading;
namespace PolyCopyTrader.Tests;

public sealed class PaperConfirmationLoadTests
{
    [Fact]
    public void GrowthErrorsTimeoutAndRecoveryHaveIndependentBounds()
    {
        var options=new PaperConfirmationOptions();var control=new PaperConfirmationLoadControl(options);var now=DateTimeOffset.UtcNow;
        control.Observe(now,1,0);control.Observe(now.AddSeconds(1),2,0);Assert.False(control.IsPaused(now.AddSeconds(1)));
        control.Observe(now.AddSeconds(2),3,0);Assert.True(control.IsPaused(now.AddSeconds(31)));Assert.False(control.IsPaused(now.AddSeconds(32)));
        control.Observe(now.AddSeconds(32),0,1);Assert.True(control.IsPaused(now.AddSeconds(33)));
        control.Timeout(now.AddSeconds(40));Assert.Equal(16,control.BatchSize);
        for(var i=0;i<10;i++)control.Timeout(now);Assert.Equal(1,control.BatchSize);
        for(var i=0;i<100;i++)control.Succeeded();Assert.Equal(32,control.BatchSize);
    }
    [Fact]
    public void DurableRatesUseSameWindowAndNeverPromiseCatchUpWhenArrivalsWin()
    {
        var before=new PaperConfirmationProgress(DateTimeOffset.UtcNow,true,1000,100,5,8);
        var after=before with {CapturedAtUtc=before.CapturedAtUtc.AddMinutes(2),Orders=1100,Confirmed=180};
        var rate=PaperConfirmationRates.Between(before,after)!;Assert.Equal(40,rate.ConfirmedPerMinute);Assert.Equal(50,rate.ArrivalsPerMinute);
        Assert.Equal(23,rate.FixedBacklogMinutes);Assert.Null(rate.CatchUpMinutes);
        Assert.Null(PaperConfirmationRates.Between(before,after with {Initialized=false}));
        // Retention deletes current rows; cumulative unique counters preserve real rates.
        var retainedBefore=before with { Arrivals=1000,UniqueConfirmations=100 };
        var retainedAfter=after with { Orders=800,Confirmed=90,Arrivals=1100,UniqueConfirmations=180 };
        var retainedRate=PaperConfirmationRates.Between(retainedBefore,retainedAfter)!;
        Assert.Equal(rate.ConfirmedPerMinute,retainedRate.ConfirmedPerMinute);
        Assert.Equal(rate.ArrivalsPerMinute,retainedRate.ArrivalsPerMinute);
        Assert.Equal(710d/40,retainedRate.FixedBacklogMinutes);Assert.Null(retainedRate.CatchUpMinutes);
    }

    [Fact]
    public void ConfigurationRejectsUnboundedBatchAndTimeouts()
    {
        var errors=AppOptionsValidator.Validate(new AppConfiguration {PaperConfirmation=new() {MaxBatchSize=33,ApplyTimeoutSeconds=6}});
        Assert.Contains(errors,x=>x.StartsWith("PaperConfirmation"));
        Assert.DoesNotContain(AppOptionsValidator.Validate(new AppConfiguration()),x=>x.StartsWith("PaperConfirmation"));
    }
}
