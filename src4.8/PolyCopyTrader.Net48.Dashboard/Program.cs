using System;
using System.Windows;
using System.Windows.Controls;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Dashboard
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            var app = new Application();
            var window = new Window
            {
                Title = "PolyCopyTrader .NET Framework 4.8",
                Width = 720,
                Height = 420,
                Content = new TextBlock
                {
                    Text = "PolyCopyTrader .NET Framework 4.8 dashboard scaffold\n" + Net48PortInfo.RuntimePosture,
                    Margin = new Thickness(24),
                    TextWrapping = TextWrapping.Wrap
                }
            };

            app.Run(window);
        }
    }
}
