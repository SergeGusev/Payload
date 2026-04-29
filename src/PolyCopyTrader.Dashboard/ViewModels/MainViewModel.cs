using CommunityToolkit.Mvvm.ComponentModel;

namespace PolyCopyTrader.Dashboard.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string appTitle = "PolyCopyTrader";

    [ObservableProperty]
    private string mode = "ReadOnly";

    [ObservableProperty]
    private string serviceStatus = "Scaffold";

    [ObservableProperty]
    private DateTimeOffset lastUpdatedUtc = DateTimeOffset.UtcNow;

    public string Summary => "Read-only scaffold is ready. API clients, storage, strategy, and paper trading will be added in later tasks.";
}
