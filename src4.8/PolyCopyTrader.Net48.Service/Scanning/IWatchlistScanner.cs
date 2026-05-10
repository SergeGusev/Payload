using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Scanning;

public interface IWatchlistScanner
{
    Task<ScannerStatusSnapshot> ScanOnceAsync(CancellationToken cancellationToken = default);
}
