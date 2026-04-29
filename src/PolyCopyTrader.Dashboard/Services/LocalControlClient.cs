using System.Net.Http;
using System.Text.Json;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Dashboard.Services;

public sealed class LocalControlClient(IpcOptions options)
{
    private readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
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

    public Task<ControlCommandResponse> PauseAllAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("pause", cancellationToken);
    }

    public Task<ControlCommandResponse> ResumeAllAsync(CancellationToken cancellationToken = default)
    {
        return PostAsync("resume", cancellationToken);
    }

    public async Task<ControlStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildUri("status"), cancellationToken);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<ControlStatusResponse>(stream, jsonOptions, cancellationToken)
            ?? new ControlStatusResponse("Unknown", false, false, string.Empty, null);
    }

    private async Task<ControlCommandResponse> PostAsync(string path, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return new ControlCommandResponse(path, "dashboard", false, "IPC is disabled in dashboard configuration.");
        }

        using var response = await httpClient.PostAsync(BuildUri(path), null, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<ControlCommandResponse>(stream, jsonOptions, cancellationToken);
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
    string CurrentLoop,
    string? LastError);
