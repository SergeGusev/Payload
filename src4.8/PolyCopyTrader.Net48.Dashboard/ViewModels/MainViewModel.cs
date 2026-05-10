using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PolyCopyTrader.Dashboard.Models;
using PolyCopyTrader.Dashboard.Services;

namespace PolyCopyTrader.Dashboard.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxDashboardErrors = 500;

    private readonly DashboardRuntime runtime;
    private readonly DashboardDataService dataService;
    private readonly LocalControlClient controlClient;
    private readonly DashboardCsvExporter csvExporter;
    private readonly DispatcherTimer refreshTimer;
    private readonly EventHandler refreshTickHandler;
    private bool disposed;

    public MainViewModel()
    {
        runtime = DashboardRepositoryFactory.Create();
        dataService = new DashboardDataService(
            runtime.Repository,
            runtime.Configuration,
            runtime.StorageConfigured,
            runtime.AuthService);
        controlClient = new LocalControlClient(runtime.Configuration.Ipc);
        csvExporter = new DashboardCsvExporter(runtime.Repository, runtime.Configuration);
        refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, runtime.Configuration.Dashboard.RefreshIntervalSeconds))
        };
        refreshTickHandler = async (_, _) => await RefreshAsync();
        refreshTimer.Tick += refreshTickHandler;
        Summary = "Waiting for first dashboard refresh.";
        StorageStatus = runtime.StorageConfigured ? "PostgreSQL configured" : "PostgreSQL not configured";
    }

    [ObservableProperty]
    private string appTitle = "PolyCopyTrader Dashboard";

    [ObservableProperty]
    private string mode = "Unknown";

    [ObservableProperty]
    private string serviceStatus = "No heartbeat";

    [ObservableProperty]
    private string serviceBannerTitle = "SERVICE UNKNOWN";

    [ObservableProperty]
    private string serviceBannerDetail = "Waiting for first IPC status refresh.";

    [ObservableProperty]
    private string serviceBannerBackground = "#FEF3C7";

    [ObservableProperty]
    private string serviceBannerForeground = "#78350F";

    [ObservableProperty]
    private string serviceBannerBorderBrush = "#F59E0B";

    [ObservableProperty]
    private string storageStatus;

    [ObservableProperty]
    private string commandStatus = "Ready.";

    [ObservableProperty]
    private string pinnedAssetId = string.Empty;

    [ObservableProperty]
    private string summary;

    [ObservableProperty]
    private DateTimeOffset lastUpdatedUtc = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private string lastError = string.Empty;

    [ObservableProperty]
    private DashboardErrorRow? selectedDashboardError;

    public ObservableCollection<OverviewMetric> Overview { get; } = [];

    public ObservableCollection<WatchlistRow> Watchlist { get; } = [];

    public ObservableCollection<TraderDiscoveryRow> TraderDiscovery { get; } = [];

    public ObservableCollection<OnChainLeaderRow> OnChainLeaders { get; } = [];

    public ObservableCollection<OnChainTraderRow> OnChainTraders { get; } = [];

    public ObservableCollection<OnChainPositionRow> OnChainPositions { get; } = [];

    public ObservableCollection<OnChainFillRow> OnChainFills { get; } = [];

    public ObservableCollection<OnChainTradeDetailRow> OnChainTradeDetails { get; } = [];

    public ObservableCollection<OnChainParticipantDetailRow> OnChainParticipantDetails { get; } = [];

    public ObservableCollection<LeaderTradeRow> LeaderTrades { get; } = [];

    public ObservableCollection<SignalRow> Signals { get; } = [];

    public ObservableCollection<PaperOrderRow> PaperOrders { get; } = [];

    public ObservableCollection<PaperPositionRow> PaperPositions { get; } = [];

    public ObservableCollection<StrategyPerformanceRow> Strategies { get; } = [];

    public ObservableCollection<StrategyRecentPerformanceRow> StrategyRecentPerformance { get; } = [];

    public ObservableCollection<StrategyRecentPerformanceRow> StrategyRecent24Hours { get; } = [];

    public ObservableCollection<StrategyRecentPerformanceRow> StrategyRecent6Hours { get; } = [];

    public ObservableCollection<StrategyRecentPerformanceRow> StrategyRecent1Hour { get; } = [];

    public ObservableCollection<PaperCopiedTraderPerformanceRow> PaperCopiedTraderPerformance { get; } = [];

    public ObservableCollection<DryRunOrderRow> DryRunOrders { get; } = [];

    public ObservableCollection<LiveOrderRow> LiveOrders { get; } = [];

    public ObservableCollection<LiveTradingEventRow> LiveTradingEvents { get; } = [];

    public ObservableCollection<LiveReadinessRow> LiveReadiness { get; } = [];

    public ObservableCollection<MarketDataRow> MarketData { get; } = [];

    public ObservableCollection<DailyReportRow> DailyReports { get; } = [];

    public ObservableCollection<TraderPerformanceRow> TraderPerformance { get; } = [];

    public ObservableCollection<CategoryPerformanceRow> CategoryPerformance { get; } = [];

    public ObservableCollection<ExecutionQualityRow> ExecutionQuality { get; } = [];

    public ObservableCollection<RejectionAnalysisRow> RejectionAnalysis { get; } = [];

    public ObservableCollection<RiskUsageRow> RiskUsage { get; } = [];

    public ObservableCollection<DiagnosticRow> Diagnostics { get; } = [];

    public ObservableCollection<RunbookLinkRow> RunbookLinks { get; } = [];

    public ObservableCollection<LogRow> Logs { get; } = [];

    public ObservableCollection<DashboardErrorRow> DashboardErrors { get; } = [];

    public async Task StartAsync()
    {
        refreshTimer.Start();
        await RefreshAsync();
    }

    public void Stop()
    {
        refreshTimer.Stop();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        try
        {
            IsRefreshing = true;
            LastError = string.Empty;
            var controlStatus = await TryGetControlStatusAsync();
            ApplyServiceBanner(controlStatus.Status, controlStatus.Error);
            var snapshot = await dataService.LoadAsync(
                controlStatus.Status,
                controlStatus.Error);
            Apply(snapshot);
            ApplyServiceBanner(controlStatus.Status, controlStatus.Error);
            LastUpdatedUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Summary = $"Refresh failed: {ex.Message}";
            RecordDashboardError("Refresh", ex);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ApplyServiceBanner(ControlStatusResponse? status, string? error)
    {
        if (status is null)
        {
            ServiceBannerTitle = "SERVICE UNAVAILABLE";
            ServiceBannerDetail = string.IsNullOrWhiteSpace(error)
                ? "Dashboard could not read IPC /status."
                : "Dashboard could not read IPC /status: " + TrimForBanner(error, 240);
            ServiceBannerBackground = "#FEE2E2";
            ServiceBannerForeground = "#7F1D1D";
            ServiceBannerBorderBrush = "#EF4444";
            return;
        }

        if (status.KillSwitchActive)
        {
            ServiceBannerTitle = "KILL SWITCH ACTIVE";
            ServiceBannerBackground = "#FEE2E2";
            ServiceBannerForeground = "#7F1D1D";
            ServiceBannerBorderBrush = "#EF4444";
        }
        else if (status.ScanningPaused || status.PaperTradingPaused ||
            string.Equals(status.State, "Paused", StringComparison.OrdinalIgnoreCase))
        {
            ServiceBannerTitle = "SERVICE PAUSED";
            ServiceBannerBackground = "#FEF3C7";
            ServiceBannerForeground = "#78350F";
            ServiceBannerBorderBrush = "#F59E0B";
        }
        else if (string.Equals(status.State, "Running", StringComparison.OrdinalIgnoreCase))
        {
            ServiceBannerTitle = "SERVICE RUNNING";
            ServiceBannerBackground = "#DCFCE7";
            ServiceBannerForeground = "#14532D";
            ServiceBannerBorderBrush = "#22C55E";
        }
        else
        {
            ServiceBannerTitle = "SERVICE " + status.State.ToUpperInvariant();
            ServiceBannerBackground = "#E0F2FE";
            ServiceBannerForeground = "#0C4A6E";
            ServiceBannerBorderBrush = "#38BDF8";
        }

        ServiceBannerDetail = BuildServiceBannerDetail(status);
    }

    private static string BuildServiceBannerDetail(ControlStatusResponse status)
    {
        var details = new List<string>
        {
            "IPC state=" + status.State,
            BuildPauseSummary(status)
        };

        if (status.KillSwitchActive)
        {
            details.Add("kill switch active");
        }

        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            details.Add("last error=" + TrimForBanner(status.LastError, 160));
        }

        if (!string.IsNullOrWhiteSpace(status.CurrentLoop))
        {
            details.Add("loop=" + TrimForBanner(status.CurrentLoop, 220));
        }

        return string.Join("; ", details);
    }

    private static string BuildPauseSummary(ControlStatusResponse status)
    {
        var paused = new List<string>();
        if (status.ScanningPaused)
        {
            paused.Add("scanning");
        }

        if (status.PaperTradingPaused)
        {
            paused.Add("paper");
        }

        if (status.LiveTradingPaused)
        {
            paused.Add("live");
        }

        return paused.Count == 0
            ? "pauses clear"
            : "paused=" + string.Join(", ", paused);
    }

    private static string TrimForBanner(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength) + "...";
    }

    [RelayCommand]
    private async Task PauseScanningAsync()
    {
        await SendCommandAsync(() => controlClient.PauseScanningAsync());
    }

    [RelayCommand]
    private async Task KillSwitchAsync()
    {
        await SendCommandAsync(() => controlClient.KillSwitchAsync());
    }

    [RelayCommand]
    private async Task ResumeScanningAsync()
    {
        await SendCommandAsync(() => controlClient.ResumeScanningAsync());
    }

    [RelayCommand]
    private async Task PausePaperTradingAsync()
    {
        await SendCommandAsync(() => controlClient.PausePaperTradingAsync());
    }

    [RelayCommand]
    private async Task ResumePaperTradingAsync()
    {
        await SendCommandAsync(() => controlClient.ResumePaperTradingAsync());
    }

    [RelayCommand]
    private async Task PauseLiveTradingAsync()
    {
        await SendCommandAsync(() => controlClient.PauseLiveTradingAsync());
    }

    [RelayCommand]
    private async Task ResumeLiveTradingAsync()
    {
        await SendCommandAsync(() => controlClient.ResumeLiveTradingAsync());
    }

    [RelayCommand]
    private async Task CancelAllLiveOrdersAsync()
    {
        await SendCommandAsync(() => controlClient.CancelAllLiveOrdersAsync());
    }

    [RelayCommand]
    private async Task RefreshTraderDiscoveryAsync()
    {
        CommandStatus = "Refreshing trader discovery...";
        await SendCommandAsync(() => controlClient.RefreshTraderDiscoveryAsync());
    }

    [RelayCommand]
    private async Task RefreshOnChainAsync()
    {
        CommandStatus = "Refreshing on-chain ingestion...";
        await SendCommandAsync(() => controlClient.RefreshOnChainAsync());
    }

    [RelayCommand]
    private async Task RefreshOnChainMarketsAsync()
    {
        CommandStatus = "Refreshing on-chain market metadata...";
        await SendCommandAsync(() => controlClient.RefreshOnChainMarketsAsync());
    }

    [RelayCommand]
    private async Task CancelOnChainAsync()
    {
        await SendCommandAsync(() => controlClient.CancelOnChainAsync());
    }

    [RelayCommand]
    private async Task ClearKillSwitchAsync()
    {
        await SendCommandAsync(() => controlClient.ClearKillSwitchAsync());
    }

    [RelayCommand]
    private async Task PinAssetAsync()
    {
        var assetId = PinnedAssetId.Trim();
        if (string.IsNullOrWhiteSpace(assetId))
        {
            CommandStatus = "Asset id is required.";
            return;
        }

        await SendCommandAsync(() => controlClient.PinAssetAsync(assetId));
    }

    [RelayCommand]
    private async Task UnpinAssetAsync()
    {
        var assetId = PinnedAssetId.Trim();
        if (string.IsNullOrWhiteSpace(assetId))
        {
            CommandStatus = "Asset id is required.";
            return;
        }

        await SendCommandAsync(() => controlClient.UnpinAssetAsync(assetId));
    }

    [RelayCommand]
    private void DisableTrader()
    {
        CommandStatus = "Disable trader requested. Placeholder only; trader configuration writes are not implemented yet.";
    }

    [RelayCommand]
    private void EnableTrader()
    {
        CommandStatus = "Enable trader requested. Placeholder only; trader configuration writes are not implemented yet.";
    }

    [RelayCommand]
    private void CancelPaperOrder()
    {
        CommandStatus = "Cancel paper order requested. Placeholder only; selected-order IPC is not implemented yet.";
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        CommandStatus = "Logs view cleared locally.";
    }

    [RelayCommand]
    private void ClearDashboardErrors()
    {
        DashboardErrors.Clear();
        SelectedDashboardError = null;
        CommandStatus = "Dashboard errors cleared locally.";
    }

    [RelayCommand]
    private void CopySelectedDashboardError()
    {
        if (SelectedDashboardError is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(FormatDashboardErrorForClipboard(SelectedDashboardError));
            CommandStatus = "Dashboard error copied to clipboard.";
        }
        catch (Exception ex)
        {
            CommandStatus = $"Clipboard copy failed: {ex.Message}";
            RecordDashboardError("Clipboard", ex);
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        try
        {
            var exportDirectory = await csvExporter.ExportAsync();
            CommandStatus = $"CSV export written to {exportDirectory}.";
        }
        catch (Exception ex)
        {
            CommandStatus = $"CSV export failed: {ex.Message}";
            RecordDashboardError("CSV export", ex);
        }
    }

    [RelayCommand]
    private async Task SetStrategyEnabledAsync(StrategyPerformanceRow? strategy)
    {
        if (strategy is null)
        {
            return;
        }

        if (!runtime.StorageConfigured)
        {
            CommandStatus = "Strategy toggle requires PostgreSQL storage.";
            await RefreshAsync();
            return;
        }

        var enabled = strategy.Enabled;
        try
        {
            var updated = await runtime.Repository.SetStrategyEnabledAsync(
                strategy.StrategyId,
                enabled,
                DateTimeOffset.UtcNow);
            CommandStatus = updated
                ? $"Strategy {strategy.Name} {(enabled ? "enabled" : "disabled")}."
                : $"Strategy {strategy.Name} was not found.";
            if (!updated)
            {
                RecordDashboardError("Strategy toggle", CommandStatus, CommandStatus);
            }

            dataService.InvalidateStrategyPerformanceCache();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CommandStatus = $"Strategy toggle failed: {ex.Message}";
            RecordDashboardError("Strategy toggle", ex);
            dataService.InvalidateStrategyPerformanceCache();
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task SetStrategyLiveStakesAsync(StrategyPerformanceRow? strategy)
    {
        if (strategy is null)
        {
            return;
        }

        if (!runtime.StorageConfigured)
        {
            CommandStatus = "Strategy live toggle requires PostgreSQL storage.";
            await RefreshAsync();
            return;
        }

        var liveStakes = strategy.LiveStakes;
        try
        {
            var updated = await runtime.Repository.SetStrategyLiveStakesAsync(
                strategy.StrategyId,
                liveStakes,
                DateTimeOffset.UtcNow);
            CommandStatus = updated
                ? $"Strategy {strategy.Name} live stakes {(liveStakes ? "enabled" : "disabled")}."
                : $"Strategy {strategy.Name} was not found.";
            if (!updated)
            {
                RecordDashboardError("Strategy live toggle", CommandStatus, CommandStatus);
            }

            dataService.InvalidateStrategyPerformanceCache();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CommandStatus = $"Strategy live toggle failed: {ex.Message}";
            RecordDashboardError("Strategy live toggle", ex);
            dataService.InvalidateStrategyPerformanceCache();
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task SaveStrategyStakeAmountsAsync(StrategyPerformanceRow? strategy)
    {
        if (strategy is null)
        {
            return;
        }

        if (!runtime.StorageConfigured)
        {
            CommandStatus = "Strategy stake amounts require PostgreSQL storage.";
            await RefreshAsync();
            return;
        }

        if (strategy.PaperStakeAmount <= 0m || strategy.LiveStakeAmount <= 0m)
        {
            CommandStatus = "Strategy stake amounts must be greater than zero.";
            RecordDashboardError("Strategy stakes", CommandStatus, CommandStatus);
            await RefreshAsync();
            return;
        }

        if (strategy.LiveAvailableBalance < 0m)
        {
            CommandStatus = "Strategy live available balance must be zero or greater.";
            RecordDashboardError("Strategy stakes", CommandStatus, CommandStatus);
            await RefreshAsync();
            return;
        }

        try
        {
            var updatedAtUtc = DateTimeOffset.UtcNow;
            var amountsUpdated = await runtime.Repository.SetStrategyStakeAmountsAsync(
                strategy.StrategyId,
                strategy.PaperStakeAmount,
                strategy.LiveStakeAmount,
                updatedAtUtc);
            var balanceUpdated = await runtime.Repository.SetStrategyLiveAvailableBalanceAsync(
                strategy.StrategyId,
                strategy.LiveAvailableBalance,
                updatedAtUtc);
            var updated = amountsUpdated && balanceUpdated;
            CommandStatus = updated
                ? $"Strategy {strategy.Name} stake amounts and live balance saved."
                : $"Strategy {strategy.Name} was not found.";
            if (!updated)
            {
                RecordDashboardError("Strategy stakes", CommandStatus, CommandStatus);
            }

            dataService.InvalidateStrategyPerformanceCache();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CommandStatus = $"Strategy stakes save failed: {ex.Message}";
            RecordDashboardError("Strategy stakes", ex);
            dataService.InvalidateStrategyPerformanceCache();
            await RefreshAsync();
        }
    }

    private async Task SendCommandAsync(Func<Task<ControlCommandResponse>> send)
    {
        try
        {
            var response = await send();
            CommandStatus = response.Message;
            if (!response.Accepted)
            {
                RecordDashboardError($"IPC {response.Command}", response.Message, response.Message);
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CommandStatus = $"IPC command failed: {ex.Message}";
            RecordDashboardError("IPC command", ex);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        refreshTimer.Stop();
        refreshTimer.Tick -= refreshTickHandler;
        disposed = true;
    }

    private void Apply(DashboardSnapshot snapshot)
    {
        Replace(Overview, snapshot.Overview);
        Replace(Watchlist, snapshot.Watchlist);
        Replace(TraderDiscovery, snapshot.TraderDiscovery);
        Replace(OnChainLeaders, snapshot.OnChainLeaders);
        Replace(OnChainTraders, snapshot.OnChainTraders);
        Replace(OnChainPositions, snapshot.OnChainPositions);
        Replace(OnChainFills, snapshot.OnChainFills);
        Replace(OnChainTradeDetails, snapshot.OnChainTradeDetails);
        Replace(OnChainParticipantDetails, snapshot.OnChainParticipantDetails);
        Replace(LeaderTrades, snapshot.LeaderTrades);
        Replace(Signals, snapshot.Signals);
        Replace(PaperOrders, snapshot.PaperOrders);
        Replace(PaperPositions, snapshot.PaperPositions);
        Replace(Strategies, snapshot.Strategies);
        Replace(StrategyRecentPerformance, snapshot.StrategyRecentPerformance);
        Replace(StrategyRecent24Hours, snapshot.StrategyRecentPerformance
            .Where(item => string.Equals(item.Window, "24h", StringComparison.OrdinalIgnoreCase))
            .ToArray());
        Replace(StrategyRecent6Hours, snapshot.StrategyRecentPerformance
            .Where(item => string.Equals(item.Window, "6h", StringComparison.OrdinalIgnoreCase))
            .ToArray());
        Replace(StrategyRecent1Hour, snapshot.StrategyRecentPerformance
            .Where(item => string.Equals(item.Window, "1h", StringComparison.OrdinalIgnoreCase))
            .ToArray());
        Replace(PaperCopiedTraderPerformance, snapshot.PaperCopiedTraderPerformance);
        Replace(DryRunOrders, snapshot.DryRunOrders);
        Replace(LiveOrders, snapshot.LiveOrders);
        Replace(LiveTradingEvents, snapshot.LiveTradingEvents);
        Replace(LiveReadiness, snapshot.LiveReadiness);
        Replace(MarketData, snapshot.MarketData);
        Replace(DailyReports, snapshot.DailyReports);
        Replace(TraderPerformance, snapshot.TraderPerformance);
        Replace(CategoryPerformance, snapshot.CategoryPerformance);
        Replace(ExecutionQuality, snapshot.ExecutionQuality);
        Replace(RejectionAnalysis, snapshot.RejectionAnalysis);
        Replace(RiskUsage, snapshot.RiskUsage);
        Replace(Diagnostics, snapshot.Diagnostics);
        Replace(RunbookLinks, snapshot.RunbookLinks);
        Replace(Logs, snapshot.Logs);

        Mode = Overview.FirstOrDefault(item => item.Name == "Mode")?.Value ?? "Unknown";
        ServiceStatus = Overview.FirstOrDefault(item => item.Name == "Service status")?.Value ?? "No heartbeat";
        var webSocketStatus = Overview.FirstOrDefault(item => item.Name == "WebSocket status")?.Value ?? "No market data status";
        var liveBlocked = LiveReadiness.Count(item => item.Status is "Blocked" or "Error");
        Summary = $"{ServiceStatus}; WS={webSocketStatus}; {StorageStatus}; live blockers={liveBlocked}; {TraderDiscovery.Count} discovery candidates; {OnChainParticipantDetails.Count} on-chain participants; {OnChainTradeDetails.Count} on-chain trades; {OnChainLeaders.Count} on-chain leaders; {OnChainPositions.Count} on-chain positions; {Signals.Count} signals; {Strategies.Count} strategies; {PaperOrders.Count} paper orders; {PaperCopiedTraderPerformance.Count} copied ratings; {DryRunOrders.Count} dry-run orders; {LiveOrders.Count} live orders; {PaperPositions.Count} positions.";
    }

    private async Task<(ControlStatusResponse? Status, string? Error)> TryGetControlStatusAsync()
    {
        try
        {
            return (await controlClient.GetStatusAsync(), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void RecordDashboardError(string source, Exception exception)
    {
        RecordDashboardError(source, exception.Message, exception.ToString());
    }

    private void RecordDashboardError(string source, string message, string details)
    {
        DashboardErrors.Insert(
            0,
            new DashboardErrorRow(
                DateTimeOffset.UtcNow.ToString("u"),
                source,
                message,
                details));

        while (DashboardErrors.Count > MaxDashboardErrors)
        {
            DashboardErrors.RemoveAt(DashboardErrors.Count - 1);
        }
    }

    private static string FormatDashboardErrorForClipboard(DashboardErrorRow error)
    {
        return
            $"Time UTC: {error.TimestampUtc}{Environment.NewLine}" +
            $"Source: {error.Source}{Environment.NewLine}" +
            $"Message: {error.Message}{Environment.NewLine}{Environment.NewLine}" +
            error.Details;
    }
}
