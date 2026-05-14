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
    private const string AllStrategyCategories = "All categories";
    private const string BtcUpDownPrefix = "BTC Up or Down ";
    private static readonly string[] BtcUpDownIntervals = ["5m", "15m", "1h", "4h"];

    private DashboardRuntime runtime = null!;
    private DashboardDataService dataService = null!;
    private LocalControlClient controlClient = null!;
    private DashboardCsvExporter csvExporter = null!;
    private readonly DispatcherTimer refreshTimer;
    private readonly EventHandler refreshTickHandler;
    private IReadOnlyList<StrategyPerformanceRow> allStrategies = [];
    private IReadOnlyList<StrategyRecentPerformanceRow> allStrategyRecentPerformance = [];
    private DashboardDatabaseSource currentDatabaseSource;
    private bool isChangingDatabaseSource;
    private bool disposed;

    public MainViewModel()
    {
        RebuildRuntime(DashboardDatabaseSource.Local);
        refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, runtime.Configuration.Dashboard.RefreshIntervalSeconds))
        };
        refreshTickHandler = async (_, _) => await RefreshAsync();
        refreshTimer.Tick += refreshTickHandler;
        Summary = "Waiting for first dashboard refresh.";
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
    private string storageStatus = string.Empty;

    [ObservableProperty]
    private string selectedDatabaseSource = DashboardDatabaseSources.LocalDisplayName;

    [ObservableProperty]
    private string commandStatus = "Ready.";

    [ObservableProperty]
    private string pinnedAssetId = string.Empty;

    [ObservableProperty]
    private string summary = string.Empty;

    [ObservableProperty]
    private DateTimeOffset lastUpdatedUtc = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private string lastError = string.Empty;

    [ObservableProperty]
    private DashboardErrorRow? selectedDashboardError;

    [ObservableProperty]
    private string selectedStrategyCategory = AllStrategyCategories;

    [ObservableProperty]
    private string selectedStrategy24HoursCategory = AllStrategyCategories;

    [ObservableProperty]
    private string selectedStrategy6HoursCategory = AllStrategyCategories;

    [ObservableProperty]
    private string selectedStrategy1HourCategory = AllStrategyCategories;

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

    public ObservableCollection<string> StrategyCategoryOptions { get; } = [AllStrategyCategories];

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

    public IReadOnlyList<string> DatabaseSourceOptions { get; } = DashboardDatabaseSources.DisplayNames;

    partial void OnSelectedStrategyCategoryChanged(string value)
    {
        ApplyStrategyFilters();
    }

    partial void OnSelectedStrategy24HoursCategoryChanged(string value)
    {
        ApplyStrategyFilters();
    }

    partial void OnSelectedStrategy6HoursCategoryChanged(string value)
    {
        ApplyStrategyFilters();
    }

    partial void OnSelectedStrategy1HourCategoryChanged(string value)
    {
        ApplyStrategyFilters();
    }

    partial void OnSelectedDatabaseSourceChanged(string value)
    {
        if (isChangingDatabaseSource)
        {
            return;
        }

        var requestedSource = DashboardDatabaseSources.FromDisplayName(value);
        if (requestedSource == currentDatabaseSource)
        {
            return;
        }

        _ = SwitchDatabaseSourceAsync(requestedSource);
    }

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

    private async Task SwitchDatabaseSourceAsync(DashboardDatabaseSource requestedSource)
    {
        if (IsRefreshing)
        {
            CommandStatus = "Wait for the current refresh before switching database source.";
            ResetSelectedDatabaseSource();
            return;
        }

        var previousSource = currentDatabaseSource;
        refreshTimer.Stop();
        try
        {
            CommandStatus = $"Switching to {DashboardDatabaseSources.ToDisplayName(requestedSource)}...";
            RebuildRuntime(requestedSource);
            refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, runtime.Configuration.Dashboard.RefreshIntervalSeconds));
            ClearLoadedData();
            await RefreshAsync();
            CommandStatus = $"Using {StorageStatus}.";
        }
        catch (Exception ex)
        {
            RecordDashboardError("Database source", ex);
            try
            {
                RebuildRuntime(previousSource);
            }
            catch (Exception restoreException)
            {
                RecordDashboardError("Database source restore", restoreException);
            }

            ResetSelectedDatabaseSource();
            CommandStatus = $"Database source switch failed: {ex.Message}";
        }
        finally
        {
            if (!disposed)
            {
                refreshTimer.Start();
            }
        }
    }

    private void RebuildRuntime(DashboardDatabaseSource databaseSource)
    {
        var nextRuntime = DashboardRepositoryFactory.Create(databaseSource);
        var nextDataService = new DashboardDataService(
            nextRuntime.Repository,
            nextRuntime.Configuration,
            nextRuntime.StorageConfigured,
            nextRuntime.AuthService);
        var nextControlClient = new LocalControlClient(nextRuntime.Configuration.Ipc);
        var nextCsvExporter = new DashboardCsvExporter(nextRuntime.Repository, nextRuntime.Configuration);

        runtime = nextRuntime;
        dataService = nextDataService;
        controlClient = nextControlClient;
        csvExporter = nextCsvExporter;
        currentDatabaseSource = databaseSource;
        StorageStatus = BuildStorageStatus(nextRuntime);
    }

    private void ResetSelectedDatabaseSource()
    {
        isChangingDatabaseSource = true;
        try
        {
            SelectedDatabaseSource = DashboardDatabaseSources.ToDisplayName(currentDatabaseSource);
        }
        finally
        {
            isChangingDatabaseSource = false;
        }
    }

    private static string BuildStorageStatus(DashboardRuntime runtime)
    {
        var configured = runtime.StorageConfigured ? "PostgreSQL configured" : "PostgreSQL not configured";
        return runtime.DatabaseSource == DashboardDatabaseSource.Remote
            ? $"Remote database ({runtime.DatabaseHost}); {configured}"
            : $"Local database; {configured}";
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
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
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

    [RelayCommand(CanExecute = nameof(CanCopySelectedDashboardError))]
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
        allStrategies = snapshot.Strategies;
        allStrategyRecentPerformance = snapshot.StrategyRecentPerformance;
        RefreshStrategyCategoryOptions();
        ApplyStrategyFilters();
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
        Summary = $"{ServiceStatus}; WS={webSocketStatus}; {StorageStatus}; live blockers={liveBlocked}; {TraderDiscovery.Count} discovery candidates; {OnChainParticipantDetails.Count} on-chain participants; {OnChainTradeDetails.Count} on-chain trades; {OnChainLeaders.Count} on-chain leaders; {OnChainPositions.Count} on-chain positions; {Signals.Count} signals; {allStrategies.Count} strategies; {PaperOrders.Count} paper orders; {PaperCopiedTraderPerformance.Count} copied ratings; {DryRunOrders.Count} dry-run orders; {LiveOrders.Count} live orders; {PaperPositions.Count} positions.";
    }

    private void ClearLoadedData()
    {
        Replace(Overview, Array.Empty<OverviewMetric>());
        Replace(Watchlist, Array.Empty<WatchlistRow>());
        Replace(TraderDiscovery, Array.Empty<TraderDiscoveryRow>());
        Replace(OnChainLeaders, Array.Empty<OnChainLeaderRow>());
        Replace(OnChainTraders, Array.Empty<OnChainTraderRow>());
        Replace(OnChainPositions, Array.Empty<OnChainPositionRow>());
        Replace(OnChainFills, Array.Empty<OnChainFillRow>());
        Replace(OnChainTradeDetails, Array.Empty<OnChainTradeDetailRow>());
        Replace(OnChainParticipantDetails, Array.Empty<OnChainParticipantDetailRow>());
        Replace(LeaderTrades, Array.Empty<LeaderTradeRow>());
        Replace(Signals, Array.Empty<SignalRow>());
        Replace(PaperOrders, Array.Empty<PaperOrderRow>());
        Replace(PaperPositions, Array.Empty<PaperPositionRow>());
        allStrategies = [];
        allStrategyRecentPerformance = [];
        RefreshStrategyCategoryOptions();
        ApplyStrategyFilters();
        Replace(PaperCopiedTraderPerformance, Array.Empty<PaperCopiedTraderPerformanceRow>());
        Replace(DryRunOrders, Array.Empty<DryRunOrderRow>());
        Replace(LiveOrders, Array.Empty<LiveOrderRow>());
        Replace(LiveTradingEvents, Array.Empty<LiveTradingEventRow>());
        Replace(LiveReadiness, Array.Empty<LiveReadinessRow>());
        Replace(MarketData, Array.Empty<MarketDataRow>());
        Replace(DailyReports, Array.Empty<DailyReportRow>());
        Replace(TraderPerformance, Array.Empty<TraderPerformanceRow>());
        Replace(CategoryPerformance, Array.Empty<CategoryPerformanceRow>());
        Replace(ExecutionQuality, Array.Empty<ExecutionQualityRow>());
        Replace(RejectionAnalysis, Array.Empty<RejectionAnalysisRow>());
        Replace(RiskUsage, Array.Empty<RiskUsageRow>());
        Replace(Diagnostics, Array.Empty<DiagnosticRow>());
        Replace(RunbookLinks, Array.Empty<RunbookLinkRow>());
        Replace(Logs, Array.Empty<LogRow>());
    }

    private void RefreshStrategyCategoryOptions()
    {
        var selected = new[]
        {
            SelectedStrategyCategory,
            SelectedStrategy24HoursCategory,
            SelectedStrategy6HoursCategory,
            SelectedStrategy1HourCategory
        };
        var categories = allStrategies
            .Select(item => item.Name)
            .Concat(allStrategyRecentPerformance.Select(item => item.Name))
            .Select(GetStrategyCategory)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Replace(StrategyCategoryOptions, new[] { AllStrategyCategories }.Concat(categories).ToArray());
        SelectedStrategyCategory = NormalizeSelectedStrategyCategory(selected[0]);
        SelectedStrategy24HoursCategory = NormalizeSelectedStrategyCategory(selected[1]);
        SelectedStrategy6HoursCategory = NormalizeSelectedStrategyCategory(selected[2]);
        SelectedStrategy1HourCategory = NormalizeSelectedStrategyCategory(selected[3]);
    }

    private void ApplyStrategyFilters()
    {
        Replace(
            Strategies,
            allStrategies
                .Where(item => IsStrategyCategoryVisible(item.Name, SelectedStrategyCategory))
                .ToArray());
        Replace(
            StrategyRecentPerformance,
            allStrategyRecentPerformance
                .Where(item => IsStrategyCategoryVisible(item.Name, SelectedStrategyCategory))
                .ToArray());
        Replace(
            StrategyRecent24Hours,
            allStrategyRecentPerformance
                .Where(item => string.Equals(item.Window, "24h", StringComparison.OrdinalIgnoreCase))
                .Where(item => IsStrategyCategoryVisible(item.Name, SelectedStrategy24HoursCategory))
                .ToArray());
        Replace(
            StrategyRecent6Hours,
            allStrategyRecentPerformance
                .Where(item => string.Equals(item.Window, "6h", StringComparison.OrdinalIgnoreCase))
                .Where(item => IsStrategyCategoryVisible(item.Name, SelectedStrategy6HoursCategory))
                .ToArray());
        Replace(
            StrategyRecent1Hour,
            allStrategyRecentPerformance
                .Where(item => string.Equals(item.Window, "1h", StringComparison.OrdinalIgnoreCase))
                .Where(item => IsStrategyCategoryVisible(item.Name, SelectedStrategy1HourCategory))
                .ToArray());
    }

    private string NormalizeSelectedStrategyCategory(string selected)
    {
        return StrategyCategoryOptions.Contains(selected, StringComparer.OrdinalIgnoreCase)
            ? selected
            : AllStrategyCategories;
    }

    private static bool IsStrategyCategoryVisible(string strategyName, string selectedCategory)
    {
        return string.IsNullOrWhiteSpace(selectedCategory) ||
            string.Equals(selectedCategory, AllStrategyCategories, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(GetStrategyCategory(strategyName), selectedCategory, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStrategyCategory(string strategyName)
    {
        var name = strategyName.Trim();
        if (!name.StartsWith(BtcUpDownPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "Other";
        }

        var btcSuffix = name.Substring(BtcUpDownPrefix.Length).Trim();
        var interval = BtcUpDownIntervals.FirstOrDefault(candidate =>
            btcSuffix.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
            btcSuffix.StartsWith(candidate + " ", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(interval))
        {
            return "Other";
        }

        var categoryPrefix = BtcUpDownPrefix + interval + " ";
        var suffix = btcSuffix.Substring(interval.Length).Trim();
        if (StartsWithStrategyWord(suffix, "PreOpen"))
        {
            var parts = suffix.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                (string.Equals(parts[1], "Half", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parts[1], "Full", StringComparison.OrdinalIgnoreCase)))
            {
                return categoryPrefix + "PreOpen " + parts[1];
            }

            return categoryPrefix + "PreOpen";
        }

        if (!string.Equals(interval, "5m", StringComparison.OrdinalIgnoreCase))
        {
            return categoryPrefix + "Other";
        }

        if (StartsWithStrategyWord(suffix, "More"))
        {
            return ContainsStrategyWord(suffix, "Gamma")
                ? categoryPrefix + "More Gamma"
                : categoryPrefix + "More";
        }

        if (StartsWithStrategyWord(suffix, "Less"))
        {
            return ContainsStrategyWord(suffix, "Gamma")
                ? categoryPrefix + "Less Gamma"
                : categoryPrefix + "Less";
        }

        if (StartsWithStrategyWord(suffix, "Binance"))
        {
            return categoryPrefix + "Binance";
        }

        if (StartsWithStrategyWord(suffix, "Middle"))
        {
            return ContainsStrategyWord(suffix, "Revert")
                ? categoryPrefix + "Middle Revert"
                : categoryPrefix + "Middle";
        }

        if (StartsWithStrategyWord(suffix, "Skip"))
        {
            return categoryPrefix + "Skip";
        }

        if (ContainsStrategyWord(suffix, "Countertrend"))
        {
            return categoryPrefix + "Countertrend";
        }

        if (StartsWithStrategyWord(suffix, "Up"))
        {
            return categoryPrefix + "Other";
        }

        if (StartsWithStrategyWord(suffix, "Down"))
        {
            return categoryPrefix + "Other";
        }

        return categoryPrefix + "Other";
    }

    private static bool StartsWithStrategyWord(string value, string word)
    {
        return value.Equals(word, StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(word + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsStrategyWord(string value, string word)
    {
        return value
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(item => string.Equals(item, word, StringComparison.OrdinalIgnoreCase));
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

    partial void OnSelectedDashboardErrorChanged(DashboardErrorRow? value)
    {
        CopySelectedDashboardErrorCommand.NotifyCanExecuteChanged();
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

    private bool CanCopySelectedDashboardError()
    {
        return SelectedDashboardError is not null;
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
