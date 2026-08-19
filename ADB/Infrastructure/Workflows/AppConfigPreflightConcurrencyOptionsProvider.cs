using IK_Auto_ADB.Core.Workflows;
using System.Configuration;

namespace IK_Auto_ADB.Infrastructure.Workflows
{
    public static class AppConfigPreflightConcurrencyOptionsProvider
    {
        public static PreflightConcurrencyOptions Load()
        {
            int maximum = Read("Operations.MaxConcurrentPreflightVerifications", 20);
            int minimumStagger = Read("Operations.PreflightStaggerMinMs", 0);
            int maximumStagger = Read("Operations.PreflightStaggerMaxMs", 0);
            try
            {
                return new PreflightConcurrencyOptions(maximum, minimumStagger,
                    maximumStagger);
            }
            catch
            {
                return new PreflightConcurrencyOptions();
            }
        }

        private static int Read(string key, int fallback)
        {
            int value;
            return int.TryParse(ConfigurationManager.AppSettings[key], out value)
                ? value : fallback;
        }
    }
}
