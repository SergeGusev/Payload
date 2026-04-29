using System.Windows;
using PolyCopyTrader.Dashboard.ViewModels;

namespace PolyCopyTrader.Dashboard;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await viewModel.StartAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        viewModel.Stop();
        viewModel.Dispose();
    }
}
