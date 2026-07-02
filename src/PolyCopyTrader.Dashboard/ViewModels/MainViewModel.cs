using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PolyCopyTrader.Dashboard.Models;
using PolyCopyTrader.Dashboard.Services;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;

namespace PolyCopyTrader.Dashboard.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    [Flags]
    private enum OrderRefreshScope
    {
        None = 0,
        Paper = 1,
        Live = 2,
        Both = Paper | Live
    }

    private const int MaxDashboardErrors = 500;
    private const string AllStrategyCategories = "All categories";
    private const string AllOrderStrategies = "All strategies";
    private const int OverviewTabIndex = 0;
    private const int StrategiesTabIndex = 1;
    private const int PaperOrdersTabIndex = 12;
    private const int LiveOrdersTabIndex = 16;
    private const int LiveOrdersPageSize = 100;
    private const decimal BigRoiThresholdPct = 10m;
    private const int BigSettlesThreshold = 100;
    private static readonly StrategyOrderFilterOption AllOrderStrategiesOption = new(null, AllOrderStrategies);

    private DashboardRuntime runtime = null!;
    private DashboardDataService dataService = null!;
    private LocalControlClient controlClient = null!;
    private PolymarketCertificateCheckService certificateCheckService = null!;
    private DashboardCsvExporter csvExporter = null!;
    private readonly DispatcherTimer refreshTimer;
    private readonly EventHandler refreshTickHandler;
    private IReadOnlyList<PaperOrderRow> allPaperOrders = [];
    private IReadOnlyList<LiveOrderRow> allLiveOrders = [];
    private IReadOnlyList<StrategyPerformanceRow> allStrategies = [];
    private IReadOnlyList<StrategyRecentPerformanceRow> allStrategyRecentPerformance = [];
    private DashboardDatabaseSource currentDatabaseSource;
    private bool isChangingDatabaseSource;
    private bool suppressOrderRefresh;
    private OrderRefreshScope pendingOrderRefreshScope;
    private int orderRefreshVersion;
    private int liveOrdersPageIndex;
    private bool liveOrdersHasNextPage;
    private int? paperOrdersWindowHours;
    private int? liveOrdersWindowHours;
    private bool disposed;

    public MainViewModel()
    {
        var initialDatabaseSource = DashboardRepositoryFactory.GetDefaultDatabaseSource();
        RebuildRuntime(initialDatabaseSource);
        SelectedDatabaseSource = DashboardDatabaseSources.ToDisplayName(initialDatabaseSource);
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
    private string serviceBannerDetail = "Waiting for first database heartbeat refresh.";

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

    [ObservableProperty]
    private StrategyOrderFilterOption? selectedPaperOrdersStrategy = AllOrderStrategiesOption;

    [ObservableProperty]
    private StrategyOrderFilterOption? selectedLiveOrdersStrategy = AllOrderStrategiesOption;

    [ObservableProperty]
    private string paperOrdersWindowStatus = "Window: all history";

    [ObservableProperty]
    private string liveOrdersWindowStatus = "Window: all history";

    [ObservableProperty]
    private string liveOrdersPageStatus = "Page 1: 0 rows";

    [ObservableProperty]
    private bool canLoadPreviousLiveOrdersPage;

    [ObservableProperty]
    private bool canLoadNextLiveOrdersPage;

    [ObservableProperty]
    private int dashboardTabSelectedIndex;

    [ObservableProperty]
    private bool showOnlyPositiveStrategies;

    [ObservableProperty]
    private bool showOnlyPositiveStrategy24Hours;

    [ObservableProperty]
    private bool showOnlyPositiveStrategy6Hours;

    [ObservableProperty]
    private bool showOnlyPositiveStrategy1Hour;

    [ObservableProperty]
    private bool showOnlyEnabledStrategies;

    [ObservableProperty]
    private bool showOnlyEnabledStrategy24Hours;

    [ObservableProperty]
    private bool showOnlyEnabledStrategy6Hours;

    [ObservableProperty]
    private bool showOnlyEnabledStrategy1Hour;

    [ObservableProperty]
    private bool showOnlyLiveStrategies;

    [ObservableProperty]
    private bool showOnlyLiveStrategy24Hours;

    [ObservableProperty]
    private bool showOnlyLiveStrategy6Hours;

    [ObservableProperty]
    private bool showOnlyLiveStrategy1Hour;

    [ObservableProperty]
    private bool showOnlyBigRoiStrategies;

    [ObservableProperty]
    private bool showOnlyBigRoiStrategy24Hours;

    [ObservableProperty]
    private bool showOnlyBigRoiStrategy6Hours;

    [ObservableProperty]
    private bool showOnlyBigRoiStrategy1Hour;

    [ObservableProperty]
    private bool showOnlyBigSettlesStrategies;

    [ObservableProperty]
    private bool showOnlyBigSettlesStrategy24Hours;

    [ObservableProperty]
    private bool showOnlyBigSettlesStrategy6Hours;

    [ObservableProperty]
    private bool showOnlyBigSettlesStrategy1Hour;

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

    public ObservableCollection<StrategyOrderFilterOption> PaperOrderStrategyOptions { get; } = [AllOrderStrategiesOption];

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

    public ObservableCollection<StrategyOrderFilterOption> LiveOrderStrategyOptions { get; } = [AllOrderStrategiesOption];

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

    public ObservableCollection<CertificateCheckRow> CertificateChecks { get; } = [];

    public ObservableCollection<RunbookLinkRow> RunbookLinks { get; } = [];

    public ObservableCollection<LogRow> Logs { get; } = [];

    public ObservableCollection<DashboardErrorRow> DashboardErrors { get; } = [];

    public IReadOnlyList<string> DatabaseSourceOptions { get; } = DashboardDatabaseSources.DisplayNames;

    private int LiveOrdersOffset => liveOrdersPageIndex * LiveOrdersPageSize;

    private DateTimeOffset? PaperOrdersCreatedAfterUtc => CreatedAfterUtcForWindowHours(paperOrdersWindowHours);

    private DateTimeOffset? LiveOrdersCreatedAfterUtc => CreatedAfterUtcForWindowHours(liveOrdersWindowHours);

    public Visibility NonStrategyVisibility =>
        runtime.Configuration.Dashboard.StrategiesOnlyMode ? Visibility.Collapsed : Visibility.Visible;

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

    partial void OnSelectedPaperOrdersStrategyChanged(StrategyOrderFilterOption? value)
    {
        ApplyOrderFilters();
        RequestOrderRefresh(OrderRefreshScope.Paper);
    }

    partial void OnSelectedLiveOrdersStrategyChanged(StrategyOrderFilterOption? value)
    {
        ResetLiveOrdersPage(clearRows: true);
        RequestOrderRefresh(OrderRefreshScope.Live);
    }

    partial void OnIsRefreshingChanged(bool value)
    {
        UpdateLiveOrdersPageState();
    }

    partial void OnShowOnlyPositiveStrategiesChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyPositiveStrategy24HoursChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyPositiveStrategy6HoursChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyPositiveStrategy1HourChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyEnabledStrategiesChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyEnabledStrategy24HoursChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyEnabledStrategy6HoursChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyEnabledStrategy1HourChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyLiveStrategiesChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyLiveStrategy24HoursChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyLiveStrategy6HoursChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyLiveStrategy1HourChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyBigRoiStrategiesChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyBigRoiStrategy24HoursChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyBigRoiStrategy6HoursChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyBigRoiStrategy1HourChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyBigSettlesStrategiesChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyBigSettlesStrategy24HoursChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyBigSettlesStrategy6HoursChanged(bool value)
    {
        ApplyStrategyFilters();
    }

    partial void OnShowOnlyBigSettlesStrategy1HourChanged(bool value)
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
            var snapshot = await dataService.LoadAsync(
                SelectedPaperOrdersStrategy?.StrategyId,
                SelectedLiveOrdersStrategy?.StrategyId,
                LiveOrdersOffset,
                PaperOrdersCreatedAfterUtc,
                LiveOrdersCreatedAfterUtc);
            Apply(snapshot);
            ApplyServiceBanner(snapshot.ServiceAvailability);
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
            if (pendingOrderRefreshScope != OrderRefreshScope.None && !disposed)
            {
                var pendingScope = pendingOrderRefreshScope;
                pendingOrderRefreshScope = OrderRefreshScope.None;
                _ = RefreshOrdersAsync(pendingScope);
            }
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
            nextRuntime.DashboardSnapshots,
            nextRuntime.Configuration,
            nextRuntime.StorageConfigured,
            nextRuntime.AuthService);
        var nextControlClient = new LocalControlClient(nextRuntime.Configuration.Ipc);
        var nextCertificateCheckService = new PolymarketCertificateCheckService(
            nextRuntime.Configuration.Polymarket,
            nextRuntime.Configuration.MarketDataWebSocket);
        var nextCsvExporter = new DashboardCsvExporter(
            nextRuntime.Repository,
            nextRuntime.DashboardSnapshots,
            nextRuntime.Configuration);

        runtime = nextRuntime;
        dataService = nextDataService;
        controlClient = nextControlClient;
        certificateCheckService = nextCertificateCheckService;
        csvExporter = nextCsvExporter;
        currentDatabaseSource = databaseSource;
        StorageStatus = BuildStorageStatus(nextRuntime);
        DashboardTabSelectedIndex = nextRuntime.Configuration.Dashboard.StrategiesOnlyMode
            ? StrategiesTabIndex
            : OverviewTabIndex;
        OnPropertyChanged(nameof(NonStrategyVisibility));
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

    private void ApplyServiceBanner(ServiceAvailability availability)
    {
        if (!availability.HasHeartbeat || !availability.IsFresh)
        {
            ServiceBannerTitle = "SERVICE UNAVAILABLE";
            ServiceBannerDetail = BuildServiceBannerDetail(availability);
            ServiceBannerBackground = "#FEE2E2";
            ServiceBannerForeground = "#7F1D1D";
            ServiceBannerBorderBrush = "#EF4444";
            return;
        }

        if (string.Equals(availability.Status, "Error", StringComparison.OrdinalIgnoreCase))
        {
            ServiceBannerTitle = "SERVICE ERROR";
            ServiceBannerBackground = "#FEE2E2";
            ServiceBannerForeground = "#7F1D1D";
            ServiceBannerBorderBrush = "#EF4444";
        }
        else if (string.Equals(availability.Status, "Paused", StringComparison.OrdinalIgnoreCase))
        {
            ServiceBannerTitle = "SERVICE PAUSED";
            ServiceBannerBackground = "#FEF3C7";
            ServiceBannerForeground = "#78350F";
            ServiceBannerBorderBrush = "#F59E0B";
        }
        else if (string.Equals(availability.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            ServiceBannerTitle = "SERVICE RUNNING";
            ServiceBannerBackground = "#DCFCE7";
            ServiceBannerForeground = "#14532D";
            ServiceBannerBorderBrush = "#22C55E";
        }
        else if (string.Equals(availability.Status, "Stopping", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(availability.Status, "Stopped", StringComparison.OrdinalIgnoreCase))
        {
            ServiceBannerTitle = "SERVICE " + availability.Status.ToUpperInvariant();
            ServiceBannerBackground = "#FEE2E2";
            ServiceBannerForeground = "#7F1D1D";
            ServiceBannerBorderBrush = "#EF4444";
        }
        else
        {
            ServiceBannerTitle = "SERVICE " + availability.Status.ToUpperInvariant();
            ServiceBannerBackground = "#E0F2FE";
            ServiceBannerForeground = "#0C4A6E";
            ServiceBannerBorderBrush = "#38BDF8";
        }

        ServiceBannerDetail = BuildServiceBannerDetail(availability);
    }

    private static string BuildServiceBannerDetail(ServiceAvailability availability)
    {
        if (!availability.HasHeartbeat)
        {
            return "No service heartbeat was found in the selected database.";
        }

        var details = new List<string>
        {
            "DB status=" + availability.Status,
            "heartbeat age=" + DashboardServiceAvailabilityEvaluator.FormatHeartbeatAge(availability.HeartbeatAge)
        };

        if (!string.IsNullOrWhiteSpace(availability.Mode))
        {
            details.Add("mode=" + availability.Mode);
        }

        if (availability.LastHeartbeatUtc is not null)
        {
            details.Add("last heartbeat=" + availability.LastHeartbeatUtc.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        if (!string.IsNullOrWhiteSpace(availability.LastError))
        {
            details.Add("last error=" + TrimForBanner(availability.LastError, 160));
        }

        if (!string.IsNullOrWhiteSpace(availability.CurrentLoop))
        {
            details.Add("loop=" + TrimForBanner(availability.CurrentLoop, 220));
        }

        return string.Join("; ", details);
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
    private async Task CheckCertificatesAsync()
    {
        try
        {
            CommandStatus = "Checking Polymarket certificates...";
            var (source, results, warning) = await GetCertificateChecksAsync();
            var rows = results.Select(item => ToCertificateCheckRow(source, item)).ToArray();
            if (!string.IsNullOrWhiteSpace(warning))
            {
                rows = new[]
                {
                    new CertificateCheckRow(
                        DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        "service IPC",
                        "Service IPC",
                        string.Empty,
                        "Not checked",
                        "Not checked",
                        "Warning",
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        warning)
                }.Concat(rows).ToArray();
            }

            Replace(CertificateChecks, rows);
            CommandStatus = BuildCertificateCheckSummary(source, results, warning);
        }
        catch (Exception ex)
        {
            CommandStatus = $"Certificate check failed: {ex.Message}";
            RecordDashboardError("Certificate check", ex);
        }
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
    private void CopyStrategyName(string? strategyName)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
        {
            return;
        }

        try
        {
            Clipboard.SetText(strategyName);
            CommandStatus = $"Strategy name copied: {strategyName}";
        }
        catch (Exception ex)
        {
            CommandStatus = $"Strategy name copy failed: {ex.Message}";
            RecordDashboardError("Clipboard", ex);
        }
    }

    [RelayCommand]
    private async Task SaveDashboardErrorsAsync()
    {
        try
        {
            var path = await csvExporter.ExportDashboardErrorsAsync(DashboardErrors.ToArray());
            CommandStatus = $"Dashboard errors saved to {path}.";
        }
        catch (Exception ex)
        {
            CommandStatus = $"Dashboard error save failed: {ex.Message}";
            RecordDashboardError("Dashboard error export", ex);
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
    private async Task SetStrategyPausedAsync(StrategyPerformanceRow? strategy)
    {
        if (strategy is null)
        {
            return;
        }

        if (!runtime.StorageConfigured)
        {
            CommandStatus = "Strategy pause toggle requires PostgreSQL storage.";
            await RefreshAsync();
            return;
        }

        var paused = strategy.Paused;
        try
        {
            var updated = await runtime.Repository.SetStrategyPausedAsync(
                strategy.StrategyId,
                paused,
                pausedUntilUtc: null,
                DateTimeOffset.UtcNow);
            CommandStatus = updated
                ? $"Strategy {strategy.Name} {(paused ? "paused" : "unpaused")}."
                : $"Strategy {strategy.Name} was not found.";
            if (!updated)
            {
                RecordDashboardError("Strategy pause toggle", CommandStatus, CommandStatus);
            }

            dataService.InvalidateStrategyPerformanceCache();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CommandStatus = $"Strategy pause toggle failed: {ex.Message}";
            RecordDashboardError("Strategy pause toggle", ex);
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

        if (strategy.PaperLostCoeff < 1m || strategy.LiveLostCoeff < 1m)
        {
            CommandStatus = "Strategy lost coefficients must be at least one.";
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
                strategy.PaperLostCoeff,
                strategy.LiveLostCoeff,
                strategy.PaperLostCounter,
                strategy.LiveLostCounter,
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

    [RelayCommand]
    private void ShowPaperOrdersForStrategy(StrategyPerformanceRow? strategy)
    {
        NavigateToOrderTab(strategy?.StrategyId, strategy?.Name, paperOrders: true, windowHours: null);
    }

    [RelayCommand]
    private void ShowLiveOrdersForStrategy(StrategyPerformanceRow? strategy)
    {
        NavigateToOrderTab(strategy?.StrategyId, strategy?.Name, paperOrders: false, windowHours: null);
    }

    [RelayCommand]
    private void ShowPaperOrdersForRecentStrategy(StrategyRecentPerformanceRow? strategy)
    {
        NavigateToOrderTab(strategy?.StrategyId, strategy?.Name, paperOrders: true, windowHours: strategy?.WindowHours);
    }

    [RelayCommand]
    private void ShowLiveOrdersForRecentStrategy(StrategyRecentPerformanceRow? strategy)
    {
        NavigateToOrderTab(strategy?.StrategyId, strategy?.Name, paperOrders: false, windowHours: strategy?.WindowHours);
    }

    private void NavigateToOrderTab(Guid? strategyId, string? strategyName, bool paperOrders, int? windowHours)
    {
        if (paperOrders)
        {
            var previousSelection = SelectedPaperOrdersStrategy;
            var previousWindowHours = paperOrdersWindowHours;
            SetPaperOrdersWindow(windowHours);
            if (previousWindowHours != paperOrdersWindowHours)
            {
                allPaperOrders = [];
                Replace(PaperOrders, Array.Empty<PaperOrderRow>());
            }

            SelectedPaperOrdersStrategy = ResolveStrategyOrderFilterOption(
                strategyId,
                strategyName,
                PaperOrderStrategyOptions);
            DashboardTabSelectedIndex = PaperOrdersTabIndex;
            CommandStatus = $"Showing paper orders for {SelectedPaperOrdersStrategy?.Name ?? AllOrderStrategies} ({PaperOrdersWindowStatus}).";
            if (Equals(previousSelection, SelectedPaperOrdersStrategy))
            {
                RequestOrderRefresh(OrderRefreshScope.Paper);
            }

            return;
        }

        var previousLiveSelection = SelectedLiveOrdersStrategy;
        var previousLiveWindowHours = liveOrdersWindowHours;
        SetLiveOrdersWindow(windowHours);
        if (previousLiveWindowHours != liveOrdersWindowHours)
        {
            ResetLiveOrdersPage(clearRows: true);
        }

        SelectedLiveOrdersStrategy = ResolveStrategyOrderFilterOption(
            strategyId,
            strategyName,
            LiveOrderStrategyOptions);
        DashboardTabSelectedIndex = LiveOrdersTabIndex;
        CommandStatus = $"Showing live orders for {SelectedLiveOrdersStrategy?.Name ?? AllOrderStrategies} ({LiveOrdersWindowStatus}).";
        if (Equals(previousLiveSelection, SelectedLiveOrdersStrategy))
        {
            RequestOrderRefresh(OrderRefreshScope.Live);
        }
    }

    [RelayCommand]
    private void PreviousLiveOrdersPage()
    {
        if (liveOrdersPageIndex <= 0 || IsRefreshing)
        {
            return;
        }

        liveOrdersPageIndex--;
        ClearLoadedLiveOrdersPage();
        RequestOrderRefresh(OrderRefreshScope.Live);
    }

    [RelayCommand]
    private void NextLiveOrdersPage()
    {
        if (!liveOrdersHasNextPage || IsRefreshing)
        {
            return;
        }

        liveOrdersPageIndex++;
        ClearLoadedLiveOrdersPage();
        RequestOrderRefresh(OrderRefreshScope.Live);
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
        allPaperOrders = snapshot.PaperOrders;
        allLiveOrders = snapshot.LiveOrders;
        Replace(PaperPositions, snapshot.PaperPositions);
        allStrategies = snapshot.Strategies;
        allStrategyRecentPerformance = snapshot.StrategyRecentPerformance;
        RefreshStrategyCategoryOptions();
        suppressOrderRefresh = true;
        try
        {
            RefreshStrategyOrderOptions();
        }
        finally
        {
            suppressOrderRefresh = false;
        }
        ApplyStrategyFilters();
        ApplyOrderFilters();
        SetLiveOrdersPageState(snapshot.HasNextLiveOrdersPage);
        Replace(PaperCopiedTraderPerformance, snapshot.PaperCopiedTraderPerformance);
        Replace(DryRunOrders, snapshot.DryRunOrders);
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
        if (runtime.Configuration.Dashboard.StrategiesOnlyMode)
        {
            Summary = $"{ServiceStatus}; {StorageStatus}; {allStrategies.Count} strategies; {StrategyRecent24Hours.Count} 24h rows; {StrategyRecent6Hours.Count} 6h rows; {StrategyRecent1Hour.Count} 1h rows.";
            return;
        }

        var webSocketStatus = Overview.FirstOrDefault(item => item.Name == "WebSocket status")?.Value ?? "No market data status";
        var liveBlocked = LiveReadiness.Count(item => item.Status is "Blocked" or "Error");
        Summary = $"{ServiceStatus}; WS={webSocketStatus}; {StorageStatus}; live blockers={liveBlocked}; {TraderDiscovery.Count} discovery candidates; {OnChainParticipantDetails.Count} on-chain participants; {OnChainTradeDetails.Count} on-chain trades; {OnChainLeaders.Count} on-chain leaders; {OnChainPositions.Count} on-chain positions; {Signals.Count} signals; {allStrategies.Count} strategies; {allPaperOrders.Count} paper orders; {PaperCopiedTraderPerformance.Count} copied ratings; {DryRunOrders.Count} dry-run orders; {allLiveOrders.Count} live orders; {PaperPositions.Count} positions.";
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
        allPaperOrders = [];
        Replace(PaperOrders, Array.Empty<PaperOrderRow>());
        Replace(PaperPositions, Array.Empty<PaperPositionRow>());
        allStrategies = [];
        allStrategyRecentPerformance = [];
        RefreshStrategyCategoryOptions();
        suppressOrderRefresh = true;
        try
        {
            RefreshStrategyOrderOptions();
        }
        finally
        {
            suppressOrderRefresh = false;
        }
        ApplyStrategyFilters();
        Replace(PaperCopiedTraderPerformance, Array.Empty<PaperCopiedTraderPerformanceRow>());
        Replace(DryRunOrders, Array.Empty<DryRunOrderRow>());
        allLiveOrders = [];
        ResetLiveOrdersPage(clearRows: true);
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
        Replace(CertificateChecks, Array.Empty<CertificateCheckRow>());
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
            .Select(StrategyDisplayCategories.GetCategory)
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

    private void RefreshStrategyOrderOptions()
    {
        var selectedPaperStrategyId = SelectedPaperOrdersStrategy?.StrategyId;
        var selectedPaperStrategyName = SelectedPaperOrdersStrategy?.Name;
        var selectedLiveStrategyId = SelectedLiveOrdersStrategy?.StrategyId;
        var selectedLiveStrategyName = SelectedLiveOrdersStrategy?.Name;

        var strategyOptions = new[] { AllOrderStrategiesOption }
            .Concat(
                allStrategies
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new StrategyOrderFilterOption(StrategyIds.Normalize(item.StrategyId), item.Name)))
            .ToArray();

        Replace(PaperOrderStrategyOptions, strategyOptions);
        Replace(LiveOrderStrategyOptions, strategyOptions);
        SelectedPaperOrdersStrategy = ResolveStrategyOrderFilterOption(
            selectedPaperStrategyId,
            selectedPaperStrategyName,
            PaperOrderStrategyOptions);
        SelectedLiveOrdersStrategy = ResolveStrategyOrderFilterOption(
            selectedLiveStrategyId,
            selectedLiveStrategyName,
            LiveOrderStrategyOptions);
    }

    private void ApplyStrategyFilters()
    {
        var enabledStrategyNames = allStrategies
            .Where(item => item.Enabled)
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Replace(
            Strategies,
            allStrategies
                .Where(item => IsStrategyCategoryVisible(item.Name, SelectedStrategyCategory))
                .Where(item => IsStrategyPositiveVisible(item, ShowOnlyPositiveStrategies))
                .Where(item => IsStrategyEnabledVisible(item, ShowOnlyEnabledStrategies))
                .Where(item => IsStrategyLiveVisible(item, ShowOnlyLiveStrategies))
                .Where(item => IsStrategyBigRoiVisible(item, ShowOnlyBigRoiStrategies))
                .Where(item => IsStrategyBigSettlesVisible(item, ShowOnlyBigSettlesStrategies))
                .ToArray());
        Replace(
            StrategyRecentPerformance,
            allStrategyRecentPerformance
                .Where(item => IsStrategyCategoryVisible(item.Name, SelectedStrategyCategory))
                .Where(item => IsStrategyRecentEnabledVisible(item, ShowOnlyEnabledStrategies, enabledStrategyNames))
                .Where(item => IsStrategyRecentLiveVisible(item, ShowOnlyLiveStrategies))
                .Where(item => IsStrategyRecentBigRoiVisible(item, ShowOnlyBigRoiStrategies))
                .Where(item => IsStrategyRecentBigSettlesVisible(item, ShowOnlyBigSettlesStrategies))
                .ToArray());
        Replace(
            StrategyRecent24Hours,
            allStrategyRecentPerformance
                .Where(item => string.Equals(item.Window, "24h", StringComparison.OrdinalIgnoreCase))
                .Where(item => IsStrategyCategoryVisible(item.Name, SelectedStrategy24HoursCategory))
                .Where(item => IsStrategyRecentPositiveVisible(item, ShowOnlyPositiveStrategy24Hours))
                .Where(item => IsStrategyRecentEnabledVisible(item, ShowOnlyEnabledStrategy24Hours, enabledStrategyNames))
                .Where(item => IsStrategyRecentLiveVisible(item, ShowOnlyLiveStrategy24Hours))
                .Where(item => IsStrategyRecentBigRoiVisible(item, ShowOnlyBigRoiStrategy24Hours))
                .Where(item => IsStrategyRecentBigSettlesVisible(item, ShowOnlyBigSettlesStrategy24Hours))
                .ToArray());
        Replace(
            StrategyRecent6Hours,
            allStrategyRecentPerformance
                .Where(item => string.Equals(item.Window, "6h", StringComparison.OrdinalIgnoreCase))
                .Where(item => IsStrategyCategoryVisible(item.Name, SelectedStrategy6HoursCategory))
                .Where(item => IsStrategyRecentPositiveVisible(item, ShowOnlyPositiveStrategy6Hours))
                .Where(item => IsStrategyRecentEnabledVisible(item, ShowOnlyEnabledStrategy6Hours, enabledStrategyNames))
                .Where(item => IsStrategyRecentLiveVisible(item, ShowOnlyLiveStrategy6Hours))
                .Where(item => IsStrategyRecentBigRoiVisible(item, ShowOnlyBigRoiStrategy6Hours))
                .Where(item => IsStrategyRecentBigSettlesVisible(item, ShowOnlyBigSettlesStrategy6Hours))
                .ToArray());
        Replace(
            StrategyRecent1Hour,
            allStrategyRecentPerformance
                .Where(item => string.Equals(item.Window, "1h", StringComparison.OrdinalIgnoreCase))
                .Where(item => IsStrategyCategoryVisible(item.Name, SelectedStrategy1HourCategory))
                .Where(item => IsStrategyRecentPositiveVisible(item, ShowOnlyPositiveStrategy1Hour))
                .Where(item => IsStrategyRecentEnabledVisible(item, ShowOnlyEnabledStrategy1Hour, enabledStrategyNames))
                .Where(item => IsStrategyRecentLiveVisible(item, ShowOnlyLiveStrategy1Hour))
                .Where(item => IsStrategyRecentBigRoiVisible(item, ShowOnlyBigRoiStrategy1Hour))
                .Where(item => IsStrategyRecentBigSettlesVisible(item, ShowOnlyBigSettlesStrategy1Hour))
                .ToArray());
    }

    private void ApplyOrderFilters()
    {
        Replace(
            PaperOrders,
            allPaperOrders
                .Where(item => IsOrderStrategyVisible(item.StrategyId, SelectedPaperOrdersStrategy))
                .ToArray());
        Replace(
            LiveOrders,
            allLiveOrders
                .Where(item => IsOrderStrategyVisible(item.StrategyId, SelectedLiveOrdersStrategy))
                .ToArray());
        UpdateLiveOrdersPageState();
    }

    private async Task RefreshOrdersAsync(OrderRefreshScope scope)
    {
        if (scope == OrderRefreshScope.None)
        {
            return;
        }

        var refreshVersion = Interlocked.Increment(ref orderRefreshVersion);
        var paperStrategyId = SelectedPaperOrdersStrategy?.StrategyId;
        var liveStrategyId = SelectedLiveOrdersStrategy?.StrategyId;

        try
        {
            CommandStatus = FormatOrderLoadingStatus(scope);
            if ((scope & OrderRefreshScope.Paper) != 0)
            {
                var paperSnapshot = await dataService.LoadPaperOrderRowsAsync(
                    paperStrategyId,
                    PaperOrdersCreatedAfterUtc);
                if (refreshVersion != Volatile.Read(ref orderRefreshVersion))
                {
                    return;
                }

                allPaperOrders = paperSnapshot.PaperOrders;
            }

            if ((scope & OrderRefreshScope.Live) != 0)
            {
                var liveSnapshot = await dataService.LoadLiveOrderRowsAsync(
                    liveStrategyId,
                    LiveOrdersOffset,
                    LiveOrdersCreatedAfterUtc);
                if (refreshVersion != Volatile.Read(ref orderRefreshVersion))
                {
                    return;
                }

                allLiveOrders = liveSnapshot.LiveOrders;
                SetLiveOrdersPageState(liveSnapshot.HasNextLiveOrdersPage);
            }

            ApplyOrderFilters();
            LastUpdatedUtc = DateTimeOffset.UtcNow;
            CommandStatus = FormatOrderLoadedStatus(scope);
        }
        catch (Exception ex)
        {
            if (refreshVersion != Volatile.Read(ref orderRefreshVersion))
            {
                return;
            }

            LastError = ex.Message;
            CommandStatus = $"Orders refresh failed: {ex.Message}";
            RecordDashboardError("Orders refresh", ex);
            UpdateLiveOrdersPageState();
        }
    }

    private void RequestOrderRefresh(OrderRefreshScope scope)
    {
        if (suppressOrderRefresh)
        {
            return;
        }

        if (IsRefreshing)
        {
            pendingOrderRefreshScope |= scope;
            return;
        }

        _ = RefreshOrdersAsync(scope);
    }

    private string FormatOrderLoadingStatus(OrderRefreshScope scope)
    {
        return scope switch
        {
            OrderRefreshScope.Paper => $"Loading paper orders for {SelectedPaperOrdersStrategy?.Name ?? AllOrderStrategies} ({PaperOrdersWindowStatus}) from {StorageStatus}.",
            OrderRefreshScope.Live => $"Loading live orders for {SelectedLiveOrdersStrategy?.Name ?? AllOrderStrategies} ({LiveOrdersWindowStatus}; {LiveOrdersPageStatus}) from {StorageStatus}.",
            _ => $"Loading orders for {SelectedPaperOrdersStrategy?.Name ?? AllOrderStrategies} / {SelectedLiveOrdersStrategy?.Name ?? AllOrderStrategies} ({PaperOrdersWindowStatus}; {LiveOrdersWindowStatus}) from {StorageStatus}."
        };
    }

    private string FormatOrderLoadedStatus(OrderRefreshScope scope)
    {
        return scope switch
        {
            OrderRefreshScope.Paper => $"Loaded {PaperOrders.Count} paper orders ({PaperOrdersWindowStatus}) from {StorageStatus}.",
            OrderRefreshScope.Live => $"Loaded {LiveOrders.Count} live orders ({LiveOrdersWindowStatus}; {LiveOrdersPageStatus}) from {StorageStatus}.",
            _ => $"Loaded {PaperOrders.Count} paper orders and {LiveOrders.Count} live orders ({PaperOrdersWindowStatus}; {LiveOrdersWindowStatus}; {LiveOrdersPageStatus}) from {StorageStatus}."
        };
    }

    private void SetPaperOrdersWindow(int? windowHours)
    {
        paperOrdersWindowHours = NormalizeWindowHours(windowHours);
        PaperOrdersWindowStatus = FormatOrderWindowStatus(paperOrdersWindowHours);
    }

    private void SetLiveOrdersWindow(int? windowHours)
    {
        liveOrdersWindowHours = NormalizeWindowHours(windowHours);
        LiveOrdersWindowStatus = FormatOrderWindowStatus(liveOrdersWindowHours);
        UpdateLiveOrdersPageState();
    }

    private static int? NormalizeWindowHours(int? windowHours)
    {
        return windowHours is > 0 ? windowHours : null;
    }

    private static DateTimeOffset? CreatedAfterUtcForWindowHours(int? windowHours)
    {
        return windowHours is > 0 ? DateTimeOffset.UtcNow.AddHours(-windowHours.Value) : null;
    }

    private static string FormatOrderWindowStatus(int? windowHours)
    {
        return windowHours is > 0
            ? $"Window: last {windowHours.Value} {(windowHours.Value == 1 ? "hour" : "hours")}"
            : "Window: all history";
    }

    private void ResetLiveOrdersPage(bool clearRows)
    {
        liveOrdersPageIndex = 0;
        liveOrdersHasNextPage = false;
        if (clearRows)
        {
            allLiveOrders = [];
            Replace(LiveOrders, Array.Empty<LiveOrderRow>());
        }
        else
        {
            ApplyOrderFilters();
        }

        UpdateLiveOrdersPageState();
    }

    private void ClearLoadedLiveOrdersPage()
    {
        liveOrdersHasNextPage = false;
        allLiveOrders = [];
        Replace(LiveOrders, Array.Empty<LiveOrderRow>());
        UpdateLiveOrdersPageState();
    }

    private void SetLiveOrdersPageState(bool hasNextPage)
    {
        liveOrdersHasNextPage = hasNextPage;
        UpdateLiveOrdersPageState();
    }

    private void UpdateLiveOrdersPageState()
    {
        CanLoadPreviousLiveOrdersPage = liveOrdersPageIndex > 0 && !IsRefreshing;
        CanLoadNextLiveOrdersPage = liveOrdersHasNextPage && !IsRefreshing;
        var firstRow = LiveOrders.Count == 0 ? 0 : LiveOrdersOffset + 1;
        var lastRow = LiveOrdersOffset + LiveOrders.Count;
        LiveOrdersPageStatus = $"Page {liveOrdersPageIndex + 1}: rows {firstRow}-{lastRow}, {LiveOrdersPageSize} per page";
    }

    private string NormalizeSelectedStrategyCategory(string selected)
    {
        return StrategyCategoryOptions.Contains(selected, StringComparer.OrdinalIgnoreCase)
            ? selected
            : AllStrategyCategories;
    }

    private static StrategyOrderFilterOption ResolveStrategyOrderFilterOption(
        Guid? strategyId,
        string? strategyName,
        IEnumerable<StrategyOrderFilterOption> options)
    {
        if (strategyId is { } id)
        {
            var normalizedId = StrategyIds.Normalize(id);
            var byId = options.FirstOrDefault(item =>
                item.StrategyId is { } optionId &&
                StrategyIds.Normalize(optionId) == normalizedId);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(strategyName))
        {
            var byName = options.FirstOrDefault(item =>
                string.Equals(item.Name, strategyName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        return AllOrderStrategiesOption;
    }

    private static bool IsStrategyCategoryVisible(string strategyName, string selectedCategory)
    {
        return string.IsNullOrWhiteSpace(selectedCategory) ||
            string.Equals(selectedCategory, AllStrategyCategories, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(StrategyDisplayCategories.GetCategory(strategyName), selectedCategory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStrategyPositiveVisible(StrategyPerformanceRow strategy, bool onlyPositive)
    {
        return !onlyPositive || strategy.ClosedRoiPct >= 0m;
    }

    private static bool IsStrategyEnabledVisible(StrategyPerformanceRow strategy, bool onlyEnabled)
    {
        return !onlyEnabled || strategy.Enabled;
    }

    private static bool IsStrategyLiveVisible(StrategyPerformanceRow strategy, bool onlyLive)
    {
        return !onlyLive || strategy.LiveStakes;
    }

    private static bool IsStrategyBigRoiVisible(StrategyPerformanceRow strategy, bool onlyBigRoi)
    {
        return !onlyBigRoi || strategy.ClosedRoiPct > BigRoiThresholdPct;
    }

    private static bool IsStrategyBigSettlesVisible(StrategyPerformanceRow strategy, bool onlyBigSettles)
    {
        return !onlyBigSettles || strategy.SettledPositionsCount > BigSettlesThreshold;
    }

    private static bool IsOrderStrategyVisible(Guid strategyId, StrategyOrderFilterOption? selectedStrategy)
    {
        return selectedStrategy?.StrategyId is not { } selectedStrategyId ||
            StrategyIds.Normalize(strategyId) == StrategyIds.Normalize(selectedStrategyId);
    }

    private static bool IsStrategyRecentPositiveVisible(StrategyRecentPerformanceRow strategy, bool onlyPositive)
    {
        return !onlyPositive || strategy.RoiPct >= 0m;
    }

    private static bool IsStrategyRecentEnabledVisible(
        StrategyRecentPerformanceRow strategy,
        bool onlyEnabled,
        IReadOnlySet<string> enabledStrategyNames)
    {
        return !onlyEnabled || enabledStrategyNames.Contains(strategy.Name);
    }

    private static bool IsStrategyRecentLiveVisible(StrategyRecentPerformanceRow strategy, bool onlyLive)
    {
        return !onlyLive || strategy.LiveStakes;
    }

    private static bool IsStrategyRecentBigRoiVisible(StrategyRecentPerformanceRow strategy, bool onlyBigRoi)
    {
        return !onlyBigRoi || strategy.RoiPct > BigRoiThresholdPct;
    }

    private static bool IsStrategyRecentBigSettlesVisible(
        StrategyRecentPerformanceRow strategy,
        bool onlyBigSettles)
    {
        return !onlyBigSettles || strategy.SettledRunsCount > BigSettlesThreshold;
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

    private async Task<(string Source, IReadOnlyList<PolymarketCertificateCheckResult> Results, string? Warning)>
        GetCertificateChecksAsync()
    {
        try
        {
            var response = await controlClient.CheckCertificatesAsync();
            if (string.Equals(response.Status, "Error", StringComparison.OrdinalIgnoreCase) &&
                response.Checks.Count == 0)
            {
                throw new InvalidOperationException(response.Error ?? "Service IPC certificate check failed.");
            }

            return (
                string.IsNullOrWhiteSpace(response.Source) ? "service process" : response.Source,
                response.Checks,
                response.Error);
        }
        catch (Exception ex)
        {
            var localResults = await certificateCheckService.CheckAsync();
            return (
                "Dashboard process",
                localResults,
                $"Service IPC certificate check was unavailable; showing Dashboard-process check instead. IPC error: {ex.Message}");
        }
    }

    private static CertificateCheckRow ToCertificateCheckRow(
        string source,
        PolymarketCertificateCheckResult result)
    {
        return new CertificateCheckRow(
            result.CheckedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            source,
            result.EndpointName,
            result.Host,
            result.TlsStatus,
            result.PinStatus,
            result.Status,
            result.Subject,
            result.Issuer,
            result.ValidToUtc,
            result.PresentedPin,
            result.Details);
    }

    private static string BuildCertificateCheckSummary(
        string source,
        IReadOnlyList<PolymarketCertificateCheckResult> results,
        string? warning)
    {
        var ok = results.Count(item => string.Equals(item.Status, "OK", StringComparison.OrdinalIgnoreCase));
        var warnings = results.Count(item => string.Equals(item.Status, "Warning", StringComparison.OrdinalIgnoreCase));
        var errors = results.Count(item => string.Equals(item.Status, "Error", StringComparison.OrdinalIgnoreCase));
        var prefix = string.IsNullOrWhiteSpace(warning)
            ? "Certificate check"
            : "Certificate check with IPC fallback";

        return $"{prefix}: {ok} OK, {warnings} warning, {errors} error; source={source}.";
    }
}
