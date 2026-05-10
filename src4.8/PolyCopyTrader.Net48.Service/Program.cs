using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Microsoft.Extensions.Configuration;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Configuration;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service
{
    internal static class Program
    {
        private const string ServiceName = "PolyCopyTrader.Net48";
        private const string ServiceDisplayName = "PolyCopyTrader .NET Framework 4.8";

        private static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--console", StringComparison.OrdinalIgnoreCase))
            {
                return RunConsole();
            }

            if (args.Length > 0 && string.Equals(args[0], "--strategy-smoke", StringComparison.OrdinalIgnoreCase))
            {
                return RunStrategySmoke();
            }

            if (args.Length > 0 && string.Equals(args[0], "--print-config", StringComparison.OrdinalIgnoreCase))
            {
                return RunPrintConfig();
            }

            if (args.Length > 0 && string.Equals(args[0], "--storage-smoke", StringComparison.OrdinalIgnoreCase))
            {
                return RunStorageSmoke();
            }

            if (args.Length > 0 && string.Equals(args[0], "--install", StringComparison.OrdinalIgnoreCase))
            {
                return RunSc("create " + ServiceName + " binPath= \"" + GetExecutablePath() + "\" start= auto DisplayName= \"" + ServiceDisplayName + "\"");
            }

            if (args.Length > 0 && string.Equals(args[0], "--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                RunSc("stop " + ServiceName);
                return RunSc("delete " + ServiceName);
            }

            if (args.Length > 0 && string.Equals(args[0], "--start", StringComparison.OrdinalIgnoreCase))
            {
                return RunSc("start " + ServiceName);
            }

            if (args.Length > 0 && string.Equals(args[0], "--stop", StringComparison.OrdinalIgnoreCase))
            {
                return RunSc("stop " + ServiceName);
            }

            if (Environment.UserInteractive)
            {
                return RunConsole();
            }

            ServiceBase.Run(new PolyCopyTraderNet48Service());
            return 0;
        }

        private static int RunConsole()
        {
            Console.WriteLine("PolyCopyTrader .NET Framework 4.8 service scaffold");
            Console.WriteLine(Net48PortInfo.RuntimePosture);
            Console.WriteLine();
            Console.WriteLine("Windows Service commands:");
            Console.WriteLine("  PolyCopyTrader.Net48.Service.exe --install");
            Console.WriteLine("  PolyCopyTrader.Net48.Service.exe --start");
            Console.WriteLine("  PolyCopyTrader.Net48.Service.exe --stop");
            Console.WriteLine("  PolyCopyTrader.Net48.Service.exe --uninstall");
            Console.WriteLine("  PolyCopyTrader.Net48.Service.exe --print-config");
            Console.WriteLine("  PolyCopyTrader.Net48.Service.exe --storage-smoke");
            Console.WriteLine("  PolyCopyTrader.Net48.Service.exe --strategy-smoke");
            return 0;
        }

        private static int RunPrintConfig()
        {
            var configuration = LoadAppConfiguration();
            AppOptionsValidator.ValidateAndThrow(configuration);
            Console.WriteLine(AppOptionsValidator.ToSanitizedSummary(configuration));
            return 0;
        }

        private static int RunStorageSmoke()
        {
            var configuration = LoadAppConfiguration();
            AppOptionsValidator.ValidateAndThrow(configuration);

            if (!StorageConnectionResolver.IsConfigured(configuration.Storage))
            {
                Console.WriteLine("Storage smoke skipped: PostgreSQL connection string is not configured.");
                Console.WriteLine("Set " + configuration.Storage.ConnectionStringEnvironmentVariable + " or Storage:ConnectionString.");
                return 0;
            }

            var connectionFactory = new PostgresConnectionFactory(configuration.Storage);
            using (var connection = connectionFactory.CreateConnection())
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT 1;";
                    var result = command.ExecuteScalar();
                    if (!object.Equals(result, 1))
                    {
                        Console.Error.WriteLine("Storage smoke failed: unexpected SELECT 1 result.");
                        return 1;
                    }
                }
            }

            Console.WriteLine("Storage smoke passed.");
            return 0;
        }

        private static int RunStrategySmoke()
        {
            var now = DateTimeOffset.UtcNow;
            var riskOptions = new RiskOptions();
            var paperOptions = new PaperTradingOptions { InitialBankrollUsd = 1000m, UseMinimumMarketOrderSize = true };
            var riskEngine = new DefaultRiskEngine(riskOptions, paperOptions);
            var signalEngine = new DefaultSignalEngine(
                new SignalOptions(),
                new ExecutionOptions(),
                riskOptions,
                paperOptions,
                riskEngine);
            var paperEngine = new DefaultPaperTradingEngine();

            var orderBook = new OrderBookSnapshot(
                "asset-up",
                new[] { new OrderBookLevel(0.48m, 50m) },
                new[] { new OrderBookLevel(0.49m, 50m) },
                now,
                "condition-1",
                MinOrderSize: 5m,
                TickSize: 0.01m);

            var leaderTrade = new LeaderTrade(
                "0xleader",
                "leader",
                "condition-1",
                "asset-up",
                "market-slug",
                "Market title",
                "Up",
                TradeSide.Buy,
                0.50m,
                10m,
                5m,
                now);

            var decision = signalEngine.Evaluate(new SignalEvaluationContext(
                leaderTrade,
                new TraderRule("0xleader", Array.Empty<string>(), 60, 1m, 2m, 5m, 0.10m),
                new MarketInfo("condition-1", "market-slug", "Market title", "Crypto", now.AddDays(2)),
                orderBook,
                new ExposureSnapshot(0m, 0m, 0m, 0m, 0m, 0)));

            if (!decision.Accepted || decision.ProposedPrice == null || decision.ProposedSizeShares == null)
            {
                Console.Error.WriteLine("Strategy smoke failed: decision was not accepted. Reason: " + decision.DecisionCode);
                return 1;
            }

            var signal = new Signal(
                Guid.NewGuid(),
                leaderTrade,
                decision.Score,
                decision.Accepted,
                decision.DecisionCode,
                decision.Reasons,
                decision.ProposedPrice,
                decision.ProposedSizeShares,
                decision.ProposedNotionalUsd,
                decision.CreatedAtUtc);
            var order = paperEngine.CreateOrder(
                signal,
                decision.ProposedPrice.Value,
                decision.ProposedSizeShares.Value,
                now.AddMinutes(2));
            var fill = paperEngine.TrySimulateFill(order, orderBook, null, now);

            if (fill == null)
            {
                Console.Error.WriteLine("Strategy smoke failed: expected a simulated fill.");
                return 1;
            }

            Console.WriteLine("Strategy smoke passed.");
            Console.WriteLine("Decision: " + decision.DecisionCode + ", score " + decision.Score);
            Console.WriteLine("Order: " + order.Id + ", price " + order.Price + ", shares " + order.SizeShares);
            Console.WriteLine("Fill: " + fill.Price + ", shares " + fill.SizeShares);
            return 0;
        }

        private static string GetExecutablePath()
        {
            return typeof(Program).Assembly.Location;
        }

        private static AppConfiguration LoadAppConfiguration()
        {
            var baseDirectory = AppContext.BaseDirectory;
            var builder = new ConfigurationBuilder()
                .SetBasePath(baseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

            builder.AddEnvironmentVariables("POLYCOPYTRADER_");
            return AppConfigurationLoader.Load(builder.Build());
        }

        private static int RunSc(string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    Console.Error.WriteLine("Failed to start sc.exe.");
                    return 1;
                }

                Console.Write(process.StandardOutput.ReadToEnd());
                Console.Error.Write(process.StandardError.ReadToEnd());
                process.WaitForExit();
                return process.ExitCode;
            }
        }
    }
}
