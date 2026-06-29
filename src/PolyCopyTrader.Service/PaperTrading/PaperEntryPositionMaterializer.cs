using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.PaperTrading;

public static class PaperEntryPositionMaterializer
{
    public static async Task<PaperEntryPersistenceBatch> MaterializeAsync(
        PaperEntryPersistenceBatch batch,
        IPaperTradingEngine paperTradingEngine,
        IExposureSnapshotCache exposureCache,
        CancellationToken cancellationToken = default)
    {
        if (batch.PaperPositionMaterializations.Count == 0)
        {
            return batch;
        }

        await exposureCache.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        var positionsByKey = new Dictionary<PaperPositionKey, PaperPosition>();
        foreach (var position in batch.PaperPositions)
        {
            positionsByKey[PaperPositionKey.From(position.CopiedTraderWallet, position.AssetId)] = position;
        }
        foreach (var materialization in batch.PaperPositionMaterializations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = PaperPositionKey.From(
                materialization.Order.CopiedTraderWallet,
                materialization.Order.AssetId);
            var currentPosition = positionsByKey.TryGetValue(key, out var batchPosition)
                ? batchPosition
                : exposureCache.GetPaperPosition(
                    materialization.Order.CopiedTraderWallet,
                    materialization.Order.AssetId);
            var updatedPosition = paperTradingEngine.ApplyBuyFill(
                currentPosition,
                materialization.Order,
                materialization.Fill,
                materialization.CurrentBid,
                materialization.UpdatedAtUtc);
            positionsByKey[key] = updatedPosition;
        }

        return batch with
        {
            PaperPositions = positionsByKey.Values.ToArray(),
            PaperPositionMaterializations = []
        };
    }

    private readonly record struct PaperPositionKey(string CopiedTraderWallet, string AssetId)
    {
        public static PaperPositionKey From(string copiedTraderWallet, string assetId)
        {
            return new PaperPositionKey(
                copiedTraderWallet.Trim().ToUpperInvariant(),
                assetId.Trim().ToUpperInvariant());
        }
    }
}
