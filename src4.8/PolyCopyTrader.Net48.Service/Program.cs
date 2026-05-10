using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using PolyCopyTrader.Domain;

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
            return 0;
        }

        private static string GetExecutablePath()
        {
            return typeof(Program).Assembly.Location;
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
