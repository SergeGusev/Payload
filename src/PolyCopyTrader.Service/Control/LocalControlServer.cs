using System.Net;
using System.Text;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.LiveTrading;
using PolyCopyTrader.Service.OnChain;
using PolyCopyTrader.Service.TraderDiscovery;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Control;

public sealed class LocalControlServer(
    ILogger<LocalControlServer> logger,
    IpcOptions ipcOptions,
    TraderDiscoveryOptions traderDiscoveryOptions,
    ServiceControlState controlState,
    ILiveTradingProcessor liveTradingProcessor,
    ITraderDiscoveryProcessor traderDiscoveryProcessor,
    IOnChainIngestionProcessor onChainIngestionProcessor,
    IOnChainMarketEnrichmentProcessor onChainMarketEnrichmentProcessor,
    IBtcUsdReferencePriceCache btcUsdReferencePriceCache,
    PolymarketCertificateCheckService certificateCheckService,
    IAppRepository repository) : BackgroundService
{
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ipcOptions.Enabled)
        {
            logger.LogInformation("Local IPC control server is disabled.");
            return;
        }

        var listenUri = new Uri(ipcOptions.ListenUrl);
        if (!listenUri.IsLoopback)
        {
            logger.LogWarning("Refusing to start IPC listener on non-loopback URL {ListenUrl}.", ipcOptions.ListenUrl);
            return;
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add(EnsureTrailingSlash(ipcOptions.ListenUrl));
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            logger.LogError(ex, "Local IPC control server could not bind to {ListenUrl}. Service will continue without IPC.", ipcOptions.ListenUrl);
            try
            {
                await repository.AddApiErrorAsync(
                    new ApiError(Guid.NewGuid(), "LocalControlServer", "Start", ex.Message, DateTimeOffset.UtcNow),
                    stoppingToken);
            }
            catch (Exception auditEx)
            {
                logger.LogError(auditEx, "Failed to persist IPC startup error.");
            }

            return;
        }

        logger.LogInformation("Local IPC control server listening on {ListenUrl}.", ipcOptions.ListenUrl);

        stoppingToken.Register(() =>
        {
            try
            {
                listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context, stoppingToken), stoppingToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint?.Address ?? IPAddress.None))
            {
                await WriteAsync(context.Response, HttpStatusCode.Forbidden, new { error = "Loopback only." }, cancellationToken);
                return;
            }

            var path = context.Request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? string.Empty;
            var method = context.Request.HttpMethod.ToUpperInvariant();

            if (method == "GET" && (path == "/health" || path == string.Empty))
            {
                await WriteAsync(context.Response, HttpStatusCode.OK, HealthPayload(), cancellationToken);
                return;
            }

            if (method == "GET" && path == "/status")
            {
                await WriteAsync(context.Response, HttpStatusCode.OK, StatusPayload(), cancellationToken);
                return;
            }

            if (method == "GET" && path == "/btc-usd-reference")
            {
                await WriteAsync(context.Response, HttpStatusCode.OK, btcUsdReferencePriceCache.Snapshot, cancellationToken);
                return;
            }

            if (method == "GET" && path == "/certificates")
            {
                var checks = await certificateCheckService.CheckAsync(cancellationToken);
                await WriteAsync(
                    context.Response,
                    HttpStatusCode.OK,
                    new
                    {
                        source = "service process",
                        status = CertificateCheckStatus(checks),
                        checks
                    },
                    cancellationToken);
                return;
            }

            if (method == "POST")
            {
                var source = context.Request.RemoteEndPoint?.ToString() ?? "dashboard";
                ServiceCommandResult? result = path switch
                {
                    "/pause" => controlState.PauseAll(source),
                    "/resume" => controlState.ResumeAll(source),
                    "/pause-scanning" => controlState.PauseScanning(source),
                    "/resume-scanning" => controlState.ResumeScanning(source),
                    "/pause-paper" => controlState.PausePaperTrading(source),
                    "/resume-paper" => controlState.ResumePaperTrading(source),
                    "/pause-live" => controlState.PauseLiveTrading(source),
                    "/resume-live" => controlState.ResumeLiveTrading(source),
                    "/clear-kill-switch" => controlState.ClearKillSwitch(source),
                    _ => null
                };

                if (result is null && path == "/kill-switch")
                {
                    result = controlState.ActivateKillSwitch(source);
                    if (result.Accepted)
                    {
                        await TryCancelLiveOrdersAsync(source, cancellationToken);
                    }
                }

                if (result is null && path == "/cancel-all-live")
                {
                    await TryCancelLiveOrdersAsync(source, cancellationToken);
                    result = new ServiceCommandResult("CancelAllLive", source, true, "Cancel-all requested for live orders.");
                }

                if (result is null && path == "/refresh-trader-discovery")
                {
                    result = await RefreshTraderDiscoveryAsync(source, cancellationToken);
                }

                if (result is null && (path == "/refresh-onchain" || path == "/refresh-onchain-7d"))
                {
                    result = await RefreshOnChainAsync(source, cancellationToken);
                }

                if (result is null && path == "/refresh-onchain-markets")
                {
                    result = await RefreshOnChainMarketsAsync(source, cancellationToken);
                }

                if (result is null && path == "/cancel-onchain")
                {
                    result = CancelOnChain(source);
                }

                if (result is null && path == "/pin-asset")
                {
                    result = await PinAssetAsync(context.Request.QueryString["assetId"], source, cancellationToken);
                }

                if (result is null && path == "/unpin-asset")
                {
                    result = await UnpinAssetAsync(context.Request.QueryString["assetId"], source, cancellationToken);
                }

                if (result is not null)
                {
                    await TryAuditAsync(result, cancellationToken);
                    await WriteAsync(context.Response, HttpStatusCode.OK, result, cancellationToken);
                    return;
                }
            }

            await WriteAsync(context.Response, HttpStatusCode.NotFound, new { error = "Endpoint not found." }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local IPC request failed.");
            if (context.Response.OutputStream.CanWrite)
            {
                await WriteAsync(context.Response, HttpStatusCode.InternalServerError, new { error = ex.Message }, cancellationToken);
            }
        }
    }

    private object HealthPayload()
    {
        var snapshot = controlState.Snapshot;
        return new
        {
            ok = snapshot.RunState is ServiceRunState.Running or ServiceRunState.Paused,
            state = snapshot.RunState.ToString(),
            snapshotAtUtc = snapshot.SnapshotAtUtc
        };
    }

    private object StatusPayload()
    {
        var snapshot = controlState.Snapshot;
        return new
        {
            state = snapshot.RunState.ToString(),
            scanningPaused = snapshot.ScanningPaused,
            paperTradingPaused = snapshot.PaperTradingPaused,
            liveTradingPaused = snapshot.LiveTradingPaused,
            killSwitchActive = snapshot.KillSwitchActive,
            currentLoop = snapshot.CurrentLoop,
            lastError = snapshot.LastError,
            startedAtUtc = snapshot.StartedAtUtc,
            snapshotAtUtc = snapshot.SnapshotAtUtc
        };
    }

    private static string CertificateCheckStatus(IReadOnlyList<PolymarketCertificateCheckResult> checks)
    {
        if (checks.Any(item => string.Equals(item.Status, "Error", StringComparison.OrdinalIgnoreCase)))
        {
            return "Error";
        }

        return checks.Any(item => string.Equals(item.Status, "Warning", StringComparison.OrdinalIgnoreCase))
            ? "Warning"
            : "OK";
    }

    private async Task<ServiceCommandResult> PinAssetAsync(string? assetId, string source, CancellationToken cancellationToken)
    {
        if (!IsUsableAssetId(assetId))
        {
            return new ServiceCommandResult("PinAsset", source, false, "Asset id is required.");
        }

        await repository.AddPinnedMarketAssetAsync(
            new PinnedMarketAsset(assetId!.Trim(), "dashboard", DateTimeOffset.UtcNow),
            cancellationToken);
        return new ServiceCommandResult("PinAsset", source, true, "Asset pinned for WebSocket subscription.");
    }

    private async Task<ServiceCommandResult> RefreshTraderDiscoveryAsync(string source, CancellationToken cancellationToken)
    {
        if (!traderDiscoveryOptions.Enabled)
        {
            return new ServiceCommandResult("RefreshTraderDiscovery", source, false, "Trader discovery is disabled in service configuration.");
        }

        try
        {
            var candidates = await traderDiscoveryProcessor.RefreshAsync(cancellationToken);
            return new ServiceCommandResult("RefreshTraderDiscovery", source, true, $"Trader discovery refreshed: {candidates.Count} candidates.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Trader discovery refresh failed for {Source}.", source);
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "LocalControlServer", "RefreshTraderDiscovery", ex.Message, DateTimeOffset.UtcNow),
                cancellationToken);
            return new ServiceCommandResult("RefreshTraderDiscovery", source, false, "Trader discovery failed: " + ex.Message);
        }
    }

    private async Task<ServiceCommandResult> RefreshOnChainAsync(string source, CancellationToken cancellationToken)
    {
        try
        {
            var result = await onChainIngestionProcessor.RefreshLookbackAsync(cancellationToken);
            return new ServiceCommandResult(
                "RefreshOnChain",
                source,
                true,
                $"On-chain ingestion refreshed: blocks {result.FromBlock}-{result.ToBlock}, {result.LogsFetched} logs, {result.FillsStored} fills.");
        }
        catch (OperationCanceledException)
        {
            return new ServiceCommandResult("RefreshOnChain", source, true, "On-chain ingestion cancelled. Restart it to resume from the latest checkpoint.");
        }
        catch (InvalidOperationException ex) when (IsAlreadyRunning(ex))
        {
            return new ServiceCommandResult("RefreshOnChain", source, false, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "On-chain ingestion refresh failed for {Source}.", source);
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "LocalControlServer", "RefreshOnChain", ex.Message, DateTimeOffset.UtcNow),
                cancellationToken);
            return new ServiceCommandResult("RefreshOnChain", source, false, "On-chain ingestion failed: " + ex.Message);
        }
    }

    private async Task<ServiceCommandResult> RefreshOnChainMarketsAsync(string source, CancellationToken cancellationToken)
    {
        try
        {
            var result = await onChainMarketEnrichmentProcessor.RefreshAsync(cancellationToken);
            return new ServiceCommandResult(
                "RefreshOnChainMarkets",
                source,
                true,
                $"On-chain market enrichment refreshed: {result.TokensResolved}/{result.TokensRequested} tokens resolved, {result.TokensNotFound} not found, {result.MetadataRowsStored} rows stored across {result.BatchesRun} batches." +
                (result.ReachedBatchLimit ? " Batch limit reached; run it again to continue." : " No missing tokens remain."));
        }
        catch (OperationCanceledException)
        {
            return new ServiceCommandResult("RefreshOnChainMarkets", source, true, "On-chain market enrichment cancelled.");
        }
        catch (InvalidOperationException ex) when (IsAlreadyRunning(ex))
        {
            return new ServiceCommandResult("RefreshOnChainMarkets", source, false, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "On-chain market enrichment failed for {Source}.", source);
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "LocalControlServer", "RefreshOnChainMarkets", ex.Message, DateTimeOffset.UtcNow),
                cancellationToken);
            return new ServiceCommandResult("RefreshOnChainMarkets", source, false, "On-chain market enrichment failed: " + ex.Message);
        }
    }

    private ServiceCommandResult CancelOnChain(string source)
    {
        var accepted = onChainIngestionProcessor.RequestCancel();
        return accepted
            ? new ServiceCommandResult("CancelOnChain", source, true, "On-chain ingestion cancellation requested.")
            : new ServiceCommandResult("CancelOnChain", source, false, "No on-chain ingestion run is active.");
    }

    private static bool IsAlreadyRunning(Exception ex)
    {
        return ex.Message.Contains("already running", StringComparison.OrdinalIgnoreCase);
    }

    private async Task TryCancelLiveOrdersAsync(string source, CancellationToken cancellationToken)
    {
        try
        {
            await liveTradingProcessor.CancelAllOpenOrdersAsync(source, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Live cancel-all failed for {Source}.", source);
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(Guid.NewGuid(), "CancelAll", "Error", ex.Message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }

    private async Task<ServiceCommandResult> UnpinAssetAsync(string? assetId, string source, CancellationToken cancellationToken)
    {
        if (!IsUsableAssetId(assetId))
        {
            return new ServiceCommandResult("UnpinAsset", source, false, "Asset id is required.");
        }

        await repository.RemovePinnedMarketAssetAsync(assetId!.Trim(), cancellationToken);
        return new ServiceCommandResult("UnpinAsset", source, true, "Asset removed from WebSocket pinned assets.");
    }

    private async Task TryAuditAsync(ServiceCommandResult result, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddServiceCommandAuditAsync(
                new ServiceCommandAudit(
                    Guid.NewGuid(),
                    result.Command,
                    result.Source,
                    result.Accepted,
                    result.Message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist service command audit for {Command}.", result.Command);
        }
    }

    private async Task WriteAsync(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        object payload,
        CancellationToken cancellationToken)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(payload, jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    private static bool IsUsableAssetId(string? assetId)
    {
        return !string.IsNullOrWhiteSpace(assetId) &&
            !assetId.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
    }
}
