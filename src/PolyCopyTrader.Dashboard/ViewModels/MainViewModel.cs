using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PolyCopyTrader.Dashboard.Models;
using PolyCopyTrader.Dashboard.Services;

namespace PolyCopyTrader.Dashboard.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
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
        dataService = new DashboardDataService(runtime.Repository, runtime.Configuration, runtime.StorageConfigured);
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

    public ObservableCollection<OverviewMetric> Overview { get; } = [];

    public ObservableCollection<WatchlistRow> Watchlist { get; } = [];

    public ObservableCollection<LeaderTradeRow> LeaderTrades { get; } = [];

    public ObservableCollection<SignalRow> Signals { get; } = [];

    public ObservableCollection<PaperOrderRow> PaperOrders { get; } = [];

    public ObservableCollection<PaperPositionRow> PaperPositions { get; } = [];

    public ObservableCollection<MarketDataRow> MarketData { get; } = [];

    public ObservableCollection<DailyReportRow> DailyReports { get; } = [];

    public ObservableCollection<TraderPerformanceRow> TraderPerformance { get; } = [];

    public ObservableCollection<CategoryPerformanceRow> CategoryPerformance { get; } = [];

    public ObservableCollection<ExecutionQualityRow> ExecutionQuality { get; } = [];

    public ObservableCollection<RejectionAnalysisRow> RejectionAnalysis { get; } = [];

    public ObservableCollection<RiskUsageRow> RiskUsage { get; } = [];

    public ObservableCollection<LogRow> Logs { get; } = [];

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
            var snapshot = await dataService.LoadAsync();
            Apply(snapshot);
            LastUpdatedUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Summary = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task PauseScanningAsync()
    {
        await SendCommandAsync(() => controlClient.PauseScanningAsync());
    }

    [RelayCommand]
    private async Task KillSwitchAsync()
    {
        await SendCommandAsync(() => controlClient.PauseAllAsync());
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
        }
    }

    private async Task SendCommandAsync(Func<Task<ControlCommandResponse>> send)
    {
        try
        {
            var response = await send();
            CommandStatus = response.Message;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CommandStatus = $"IPC command failed: {ex.Message}";
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
        Replace(LeaderTrades, snapshot.LeaderTrades);
        Replace(Signals, snapshot.Signals);
        Replace(PaperOrders, snapshot.PaperOrders);
        Replace(PaperPositions, snapshot.PaperPositions);
        Replace(MarketData, snapshot.MarketData);
        Replace(DailyReports, snapshot.DailyReports);
        Replace(TraderPerformance, snapshot.TraderPerformance);
        Replace(CategoryPerformance, snapshot.CategoryPerformance);
        Replace(ExecutionQuality, snapshot.ExecutionQuality);
        Replace(RejectionAnalysis, snapshot.RejectionAnalysis);
        Replace(RiskUsage, snapshot.RiskUsage);
        Replace(Logs, snapshot.Logs);

        Mode = Overview.FirstOrDefault(item => item.Name == "Mode")?.Value ?? "Unknown";
        ServiceStatus = Overview.FirstOrDefault(item => item.Name == "Service status")?.Value ?? "No heartbeat";
        var webSocketStatus = Overview.FirstOrDefault(item => item.Name == "WebSocket status")?.Value ?? "No market data status";
        Summary = $"{ServiceStatus}; WS={webSocketStatus}; {StorageStatus}; {Signals.Count} signals; {PaperOrders.Count} paper orders; {PaperPositions.Count} positions.";
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
