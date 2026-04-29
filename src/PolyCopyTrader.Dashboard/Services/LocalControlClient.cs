using System.Net.Http;
using System.Text.Json;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Dashboard.Services;

public sealed class LocalControlClient(IpcOptions options)
{
    private readonly HttpClient httpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ControlCommandResponse> PauseScanningAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("pause-scanning", cancellationToken);
    }

    public Task<ControlCommandResponse> ResumeScanningAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("resume-scanning", cancellationToken);
    }

    public Task<ControlCommandResponse> PausePaperTradingAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("pause-paper", cancellationToken);
    }

    public Task<ControlCommandResponse> ResumePaperTradingAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("resume-paper", cancellationToken);
    }

    public Task<ControlCommandResponse> PauseLiveTradingAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("pause-live", cancellationToken);
    }

    public Task<ControlCommandResponse> ResumeLiveTradingAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("resume-live", cancellationToken);
    }

    public Task<ControlCommandResponse> KillSwitchAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("kill-switch", cancellationToken);
    }

    public Task<ControlCommandResponse> ClearKillSwitchAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("clear-kill-switch", cancellationToken);
    }

    public Task<ControlCommandResponse> CancelAllLiveOrdersAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("cancel-all-live", cancellationToken);
    }

    public Task<ControlCommandResponse> RefreshTraderDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("refresh-trader-discovery", cancellationToken, TimeSpan.FromMinutes(5));
    }

    public Task<ControlCommandResponse> PauseAllAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("pause", cancellationToken);
    }

    public Task<ControlCommandResponse> ResumeAllAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("resume", cancellationToken);
    }

    public Task<ControlCommandResponse> PinAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        return PostAsync($"pin-asset?assetId={Uri.EscapeDataString(assetId)}", cancellationToken);
    }

    public Task<ControlCommandResponse> UnpinAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        return PostAsync($"unpin-asset?assetId={Uri.EscapeDataString(assetId)}", cancellationToken);
    }

    public async Task<ControlStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var response = await httpClient.GetAsync(BuildUri("status"), timeout.Token);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        return await JsonSerializer.DeserializeAsync<ControlStatusResponse>(stream, jsonOptions, timeout.Token)
            ?? new ControlStatusResponse("Unknown", false, false, false, false, string.Empty, null);
    }

    private async Task<ControlCommandResponse> PostAsync(
        string path,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        if (!options.Enabled)
        {
            return new ControlCommandResponse(path, "dashboard", false, "IPC is disabled in dashboard configuration.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(3));
        using var response = await httpClient.PostAsync(BuildUri(path), null, timeoutCts.Token);
        var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
        var payload = await JsonSerializer.DeserializeAsync<ControlCommandResponse>(stream, jsonOptions, timeoutCts.Token);
        if (payload is null)
        {
            return new ControlCommandResponse(path, "dashboard", false, $"Empty IPC response: {(int)response.StatusCode}.");
        }

        return response.IsSuccessStatusCode
            ? payload
            : payload with { Accepted = false };
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = options.DashboardBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? options.DashboardBaseUrl
            : options.DashboardBaseUrl + "/";
        return new Uri(new Uri(baseUrl), path);
    }
}

public sealed record ControlCommandResponse(
    string Command,
    string Source,
    bool Accepted,
    string Message);

public sealed record ControlStatusResponse(
    string State,
    bool ScanningPaused,
    bool PaperTradingPaused,
    bool LiveTradingPaused,
    bool KillSwitchActive,
    string CurrentLoop,
    string? LastError);
