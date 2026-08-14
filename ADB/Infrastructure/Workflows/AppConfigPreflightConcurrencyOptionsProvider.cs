using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System.Configuration;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public static class AppConfigPreflightConcurrencyOptionsProvider
    {
        public static PreflightConcurrencyOptions Load()
        {
            int maximum = Read("Operations.MaxConcurrentPreflightVerifications", 8);
            int minimumStagger = Read("Operations.PreflightStaggerMinMs", 50);
            int maximumStagger = Read("Operations.PreflightStaggerMaxMs", 200);
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
