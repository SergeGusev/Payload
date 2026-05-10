using System.ServiceProcess;

namespace PolyCopyTrader.Service
{
    internal sealed class PolyCopyTraderNet48Service : ServiceBase
    {
        public PolyCopyTraderNet48Service()
        {
            ServiceName = "PolyCopyTrader.Net48";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            // The legacy worker engine will be attached here after the ported dependencies compile.
        }

        protected override void OnStop()
        {
        }
    }
}
