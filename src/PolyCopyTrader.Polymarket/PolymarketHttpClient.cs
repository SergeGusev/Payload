using System.Net;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket;

internal sealed class PolymarketHttpClient(
    HttpClient httpClient,
    PolymarketOptions options,
    IPolymarketApiErrorSink errorSink,
    string component)
{
    public async Task<JsonDocument> GetJsonDocumentAsync(
        Uri requestUri,
        string operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await httpClient.GetAsync(requestUri, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
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
            HttpResponseMessage? response = null;
            try
            {
                response = await httpClient.GetAsync(requestUri, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
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

    private static string Trim(string value)
    {
        return value.Length <= 512 ? value : value[..512];
    }
}
