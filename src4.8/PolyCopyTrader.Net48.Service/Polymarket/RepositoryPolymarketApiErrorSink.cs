using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Polymarket;

public sealed class RepositoryPolymarketApiErrorSink(IAppRepository repository) : IPolymarketApiErrorSink
{
    public Task RecordAsync(ApiError error, CancellationToken cancellationToken = default)
    {
        return repository.AddApiErrorAsync(error, cancellationToken);
    }
}
