using System.Diagnostics;
using System.Net;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket;

internal sealed class PolymarketHttpClient(
    HttpClient httpClient,
    PolymarketOptions options,
    IPolymarketApiErrorSink errorSink,
    string component,
    IPolymarketHttpLogSink? httpLogSink = null)
{
    private const int ResponseBodyLogLimit = 4_096;
    private readonly IPolymarketHttpLogSink httpLogSink = httpLogSink ?? new NullPolymarketHttpLogSink();

    public async Task<JsonDocument> GetJsonDocumentAsync(
        Uri requestUri,
        string operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var requestedAtUtc = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var responseLogged = false;
            HttpResponseMessage? response = null;
            try
            {
                response = await httpClient.GetAsync(requestUri, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                await RecordHttpLogAsync(
                    HttpMethod.Get.Method,
                    requestUri,
                    operation,
                    attempt,
                    requestedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    response.StatusCode,
                    response.IsSuccessStatusCode,
                    body,
                    null,
                    cancellationToken);
                responseLogged = true;

                if (response.IsSuccessStatusCode)
                {
                    return JsonDocument.Parse(body);
                }

                if (ShouldRetry(response.StatusCode, attempt))
                {
                    await DelayAsync(attempt, cancellationToken);
                    continue;
                }

                throw new PolymarketApiException(
                    component,
                    operation,
                    $"{operation} failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body: {Trim(body)}");
            }
            catch (Exception ex) when (ex is not PolymarketApiException && ex is not OperationCanceledException)
            {
                if (!responseLogged)
                {
                    await RecordHttpLogAsync(
                        HttpMethod.Get.Method,
                        requestUri,
                        operation,
                        attempt,
                        requestedAtUtc,
                        stopwatch.ElapsedMilliseconds,
                        null,
                        false,
                        string.Empty,
                        ex.Message,
                        cancellationToken);
                }

                if (attempt < options.MaxRetries)
                {
                    await DelayAsync(attempt, cancellationToken);
                    continue;
                }

                await RecordErrorAsync(operation, ex.Message, cancellationToken);
                throw new PolymarketApiException(component, operation, $"{operation} failed: {ex.Message}", ex);
            }
            catch (PolymarketApiException ex)
            {
                await RecordErrorAsync(operation, ex.Message, cancellationToken);
                throw;
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    public async Task<string> GetStringAsync(
        Uri requestUri,
        string operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var requestedAtUtc = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var responseLogged = false;
            HttpResponseMessage? response = null;
            try
            {
                response = await httpClient.GetAsync(requestUri, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                await RecordHttpLogAsync(
                    HttpMethod.Get.Method,
                    requestUri,
                    operation,
                    attempt,
                    requestedAtUtc,
                    stopwatch.ElapsedMilliseconds,
                    response.StatusCode,
                    response.IsSuccessStatusCode,
                    body,
                    null,
                    cancellationToken);
                responseLogged = true;

                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                if (ShouldRetry(response.StatusCode, attempt))
                {
                    await DelayAsync(attempt, cancellationToken);
                    continue;
                }

                throw new PolymarketApiException(
                    component,
                    operation,
                    $"{operation} failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body: {Trim(body)}");
            }
            catch (Exception ex) when (ex is not PolymarketApiException && ex is not OperationCanceledException)
            {
                if (!responseLogged)
                {
                    await RecordHttpLogAsync(
                        HttpMethod.Get.Method,
                        requestUri,
                        operation,
                        attempt,
                        requestedAtUtc,
                        stopwatch.ElapsedMilliseconds,
                        null,
                        false,
                        string.Empty,
                        ex.Message,
                        cancellationToken);
                }

                if (attempt < options.MaxRetries)
                {
                    await DelayAsync(attempt, cancellationToken);
                    continue;
                }

                await RecordErrorAsync(operation, ex.Message, cancellationToken);
                throw new PolymarketApiException(component, operation, $"{operation} failed: {ex.Message}", ex);
            }
            catch (PolymarketApiException ex)
            {
                await RecordErrorAsync(operation, ex.Message, cancellationToken);
                throw;
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private bool ShouldRetry(HttpStatusCode statusCode, int attempt)
    {
        if (attempt >= options.MaxRetries)
        {
            return false;
        }

        var code = (int)statusCode;
        return statusCode == HttpStatusCode.TooManyRequests || code >= 500;
    }

    private Task DelayAsync(int attempt, CancellationToken cancellationToken)
    {
        if (options.RetryBaseDelayMilliseconds <= 0)
        {
            return Task.CompletedTask;
        }

        var delay = TimeSpan.FromMilliseconds(options.RetryBaseDelayMilliseconds * Math.Pow(2, attempt));
        return Task.Delay(delay, cancellationToken);
    }

    private Task RecordErrorAsync(string operation, string message, CancellationToken cancellationToken)
    {
        return errorSink.RecordAsync(
            new ApiError(Guid.NewGuid(), component, operation, message, DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private Task RecordHttpLogAsync(
        string httpMethod,
        Uri requestUri,
        string operation,
        int attempt,
        DateTimeOffset requestedAtUtc,
        long durationMilliseconds,
        HttpStatusCode? statusCode,
        bool succeeded,
        string responseBody,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        return httpLogSink.RecordAsync(
            new PolymarketHttpLogEntry(
                Guid.NewGuid(),
                component,
                operation,
                httpMethod,
                requestUri.AbsoluteUri,
                requestedAtUtc,
                statusCode is null ? null : DateTimeOffset.UtcNow,
                Math.Max(0, durationMilliseconds),
                attempt + 1,
                statusCode is { } value ? (int)value : null,
                succeeded,
                Trim(responseBody, ResponseBodyLogLimit),
                errorMessage is null ? null : Trim(errorMessage, ResponseBodyLogLimit)),
            cancellationToken);
    }

    private static string Trim(string value)
    {
        return value.Length <= 512 ? value : value[..512];
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
