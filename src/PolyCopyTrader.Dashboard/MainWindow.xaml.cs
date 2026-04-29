using System.Windows;
using PolyCopyTrader.Dashboard.ViewModels;

namespace PolyCopyTrader.Dashboard;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
