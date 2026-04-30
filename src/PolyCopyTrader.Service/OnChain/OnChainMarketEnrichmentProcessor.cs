using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainMarketEnrichmentProcessor(
    ILogger<OnChainMarketEnrichmentProcessor> logger,
    OnChainIngestionOptions options,
    IPolymarketGammaClient gammaClient,
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

        logger.LogInformation(
            "Starting on-chain market enrichment. BatchSize={BatchSize} MaxBatches={MaxBatches}",
            options.MarketEnrichmentBatchSize,
            options.MarketEnrichmentMaxBatchesPerRun);

        while (batchesRun < options.MarketEnrichmentMaxBatchesPerRun)
        {
            var tokenIds = await repository.GetOnChainTokenIdsMissingMetadataAsync(
                options.MarketEnrichmentBatchSize,
                cancellationToken);
            if (tokenIds.Count == 0)
            {
                break;
            }

            batchesRun++;
            requested += tokenIds.Count;
            logger.LogInformation(
                "On-chain market enrichment batch starting. Batch={Batch} Tokens={Tokens}",
                batchesRun,
                tokenIds.Count);

            foreach (var tokenId in tokenIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var metadata = await gammaClient.GetTokenMetadataAsync(tokenId, closed: false, cancellationToken);
                await DelayIfConfiguredAsync(cancellationToken);
                if (metadata.Count == 0)
                {
                    metadata = await gammaClient.GetTokenMetadataAsync(tokenId, closed: true, cancellationToken);
                    await DelayIfConfiguredAsync(cancellationToken);
                }

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
            (await repository.GetOnChainTokenIdsMissingMetadataAsync(1, cancellationToken)).Count > 0;

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

    private Task DelayIfConfiguredAsync(CancellationToken cancellationToken)
    {
        return options.RequestDelayMilliseconds <= 0
            ? Task.CompletedTask
            : Task.Delay(TimeSpan.FromMilliseconds(options.RequestDelayMilliseconds), cancellationToken);
    }
}
