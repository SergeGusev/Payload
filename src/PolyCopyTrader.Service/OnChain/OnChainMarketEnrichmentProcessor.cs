using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainMarketEnrichmentProcessor(
    ILogger<OnChainMarketEnrichmentProcessor> logger,
    OnChainIngestionOptions options,
    IPolymarketGammaClient gammaClient,
    IPolymarketClobPublicClient clobClient,
    IAppRepository repository) : IOnChainMarketEnrichmentProcessor
{
    private readonly object sync = new();
    private bool isRunning;

    public async Task<OnChainMarketEnrichmentResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (isRunning)
            {
                throw new InvalidOperationException("On-chain market enrichment is already running.");
            }

            isRunning = true;
        }

        try
        {
            return await RefreshCoreAsync(cancellationToken);
        }
        finally
        {
            lock (sync)
            {
                isRunning = false;
            }
        }
    }

    private async Task<OnChainMarketEnrichmentResult> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("On-chain market enrichment is disabled because on-chain ingestion is disabled.");
            return new OnChainMarketEnrichmentResult(0, 0, 0, 0, 0, false);
        }

        var requested = 0;
        var resolved = 0;
        var notFound = 0;
        var rowsStored = 0;
        var batchesRun = 0;
        var attemptedTokenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eventCategoryById = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        logger.LogInformation(
            "Starting on-chain market enrichment. BatchSize={BatchSize} MaxBatches={MaxBatches}",
            options.MarketEnrichmentBatchSize,
            options.MarketEnrichmentMaxBatchesPerRun);

        while (batchesRun < options.MarketEnrichmentMaxBatchesPerRun)
        {
            var tokenIds = (await repository.GetOnChainTokenIdsMissingMetadataAsync(
                options.MarketEnrichmentBatchSize + attemptedTokenIds.Count,
                cancellationToken))
                .Where(tokenId => !attemptedTokenIds.Contains(tokenId))
                .Take(options.MarketEnrichmentBatchSize)
                .ToArray();
            if (tokenIds.Length == 0)
            {
                break;
            }

            batchesRun++;
            requested += tokenIds.Length;
            logger.LogInformation(
                "On-chain market enrichment batch starting. Batch={Batch} Tokens={Tokens}",
                batchesRun,
                tokenIds.Length);

            foreach (var tokenId in tokenIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attemptedTokenIds.Add(tokenId);

                var metadata = await GetTokenMetadataWithFallbackAsync(tokenId, eventCategoryById, cancellationToken);

                if (metadata.Count == 0)
                {
                    await repository.UpsertPolymarketOnChainTokenMetadataAsync(
                        [PolymarketJsonParser.BuildMissingTokenMetadata(tokenId, "Gamma market not found for token_id.")],
                        cancellationToken);
                    notFound++;
                    rowsStored++;
                    logger.LogInformation("On-chain token metadata not found. TokenId={TokenId}", tokenId);
                    continue;
                }

                await repository.UpsertPolymarketOnChainTokenMetadataAsync(metadata, cancellationToken);
                resolved++;
                rowsStored += metadata.Count;
                logger.LogInformation(
                    "On-chain token metadata enriched. TokenId={TokenId} Rows={Rows} Market={Market}",
                    tokenId,
                    metadata.Count,
                    metadata[0].MarketSlug);
            }
        }

        var reachedBatchLimit = batchesRun >= options.MarketEnrichmentMaxBatchesPerRun &&
            (await repository.GetOnChainTokenIdsMissingMetadataAsync(attemptedTokenIds.Count + 1, cancellationToken))
            .Any(tokenId => !attemptedTokenIds.Contains(tokenId));

        logger.LogInformation(
            "On-chain market enrichment finished. Tokens={Tokens} Resolved={Resolved} NotFound={NotFound} Rows={Rows} Batches={Batches} ReachedBatchLimit={ReachedBatchLimit}",
            requested,
            resolved,
            notFound,
            rowsStored,
            batchesRun,
            reachedBatchLimit);

        return new OnChainMarketEnrichmentResult(requested, resolved, notFound, rowsStored, batchesRun, reachedBatchLimit);
    }

    private async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataWithFallbackAsync(
        string tokenId,
        Dictionary<string, string?> eventCategoryById,
        CancellationToken cancellationToken)
    {
        var bestMetadata = await gammaClient.GetTokenMetadataAsync(tokenId, closed: false, cancellationToken);
        await DelayIfConfiguredAsync(cancellationToken);
        if (HasCategory(bestMetadata))
        {
            return bestMetadata;
        }

        var closedMetadata = await gammaClient.GetTokenMetadataAsync(tokenId, closed: true, cancellationToken);
        await DelayIfConfiguredAsync(cancellationToken);
        if (HasCategory(closedMetadata))
        {
            return closedMetadata;
        }

        if (bestMetadata.Count == 0)
        {
            bestMetadata = closedMetadata;
        }

        var eventMetadata = await TryApplyEventCategoryFallbackAsync(bestMetadata, eventCategoryById, cancellationToken);
        if (HasCategory(eventMetadata))
        {
            return eventMetadata;
        }

        var marketByToken = await TryGetMarketByTokenAsync(tokenId, cancellationToken);
        await DelayIfConfiguredAsync(cancellationToken);
        if (marketByToken is null || string.IsNullOrWhiteSpace(marketByToken.ConditionId))
        {
            return eventMetadata;
        }

        var conditionMetadata = await gammaClient.GetTokenMetadataByConditionIdAsync(
            marketByToken.ConditionId,
            tokenId,
            closed: false,
            cancellationToken);
        await DelayIfConfiguredAsync(cancellationToken);
        if (HasCategory(conditionMetadata))
        {
            return conditionMetadata;
        }

        var conditionEventMetadata = await TryApplyEventCategoryFallbackAsync(conditionMetadata, eventCategoryById, cancellationToken);
        if (HasCategory(conditionEventMetadata))
        {
            return conditionEventMetadata;
        }

        var closedConditionMetadata = await gammaClient.GetTokenMetadataByConditionIdAsync(
            marketByToken.ConditionId,
            tokenId,
            closed: true,
            cancellationToken);
        await DelayIfConfiguredAsync(cancellationToken);
        if (HasCategory(closedConditionMetadata))
        {
            return closedConditionMetadata;
        }

        var closedConditionEventMetadata = await TryApplyEventCategoryFallbackAsync(closedConditionMetadata, eventCategoryById, cancellationToken);
        if (HasCategory(closedConditionEventMetadata))
        {
            return closedConditionEventMetadata;
        }

        if (conditionMetadata.Count > 0)
        {
            return conditionEventMetadata.Count > 0 ? conditionEventMetadata : conditionMetadata;
        }

        return closedConditionEventMetadata.Count > 0
            ? closedConditionEventMetadata
            : closedConditionMetadata.Count > 0
                ? closedConditionMetadata
                : eventMetadata;
    }

    private async Task<PolymarketClobMarketByToken?> TryGetMarketByTokenAsync(
        string tokenId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await clobClient.GetMarketByTokenAsync(tokenId, cancellationToken);
        }
        catch (PolymarketApiException ex)
        {
            logger.LogInformation(
                ex,
                "CLOB market-by-token fallback failed during on-chain market enrichment. TokenId={TokenId}",
                tokenId);
            return null;
        }
    }

    private async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> TryApplyEventCategoryFallbackAsync(
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        Dictionary<string, string?> eventCategoryById,
        CancellationToken cancellationToken)
    {
        if (metadata.Count == 0 || HasCategory(metadata))
        {
            return metadata;
        }

        var eventId = metadata
            .Select(item => TryParseEventId(item.RawJson))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return metadata;
        }

        try
        {
            if (!eventCategoryById.TryGetValue(eventId, out var category))
            {
                category = await gammaClient.GetEventCategoryAsync(eventId, cancellationToken);
                eventCategoryById[eventId] = category;
                await DelayIfConfiguredAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                return metadata;
            }

            return metadata.Select(item => item with { Category = category }).ToArray();
        }
        catch (JsonException ex)
        {
            logger.LogInformation(
                ex,
                "Gamma event category fallback returned invalid JSON during on-chain market enrichment. EventId={EventId}",
                eventId);
            return metadata;
        }
        catch (PolymarketApiException ex)
        {
            logger.LogInformation(
                ex,
                "Gamma event category fallback failed during on-chain market enrichment. EventId={EventId}",
                eventId);
            return metadata;
        }
    }

    private static string? TryParseEventId(string rawJson)
    {
        try
        {
            return PolymarketJsonParser.ParseGammaMarketEventId(rawJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasCategory(IReadOnlyList<PolymarketOnChainTokenMetadata> metadata)
    {
        return metadata.Any(item => !string.IsNullOrWhiteSpace(item.Category));
    }

    private Task DelayIfConfiguredAsync(CancellationToken cancellationToken)
    {
        return options.RequestDelayMilliseconds <= 0
            ? Task.CompletedTask
            : Task.Delay(TimeSpan.FromMilliseconds(options.RequestDelayMilliseconds), cancellationToken);
    }
}
