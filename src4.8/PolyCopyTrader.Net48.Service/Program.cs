using System;
using System.ServiceProcess;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--console", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("PolyCopyTrader .NET Framework 4.8 service scaffold");
                Console.WriteLine(Net48PortInfo.RuntimePosture);
                return 0;
            }

            ServiceBase.Run(new PolyCopyTraderNet48Service());
            return 0;
        }
    }
}
