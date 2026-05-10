using System.ServiceProcess;
using Microsoft.Extensions.Hosting;
using PolyCopyTrader.Storage;
using Serilog;

namespace PolyCopyTrader.Service
{
    internal sealed class PolyCopyTraderNet48Service : ServiceBase
    {
        private readonly string[] args;
        private IHost? host;

        public PolyCopyTraderNet48Service(string[] args)
        {
            this.args = args;
            ServiceName = "PolyCopyTrader.Net48";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            host = Net48ServiceHostFactory.Build(this.args);
            host.Services.GetRequiredService<IStorageSchemaInitializer>().InitializeAsync().GetAwaiter().GetResult();
            host.StartAsync().GetAwaiter().GetResult();
        }

        protected override void OnStop()
        {
            if (host == null)
            {
                return;
            }

            try
            {
                host.StopAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            }
            finally
            {
                host.Dispose();
                host = null;
                Log.CloseAndFlush();
            }
        }
    }
}
