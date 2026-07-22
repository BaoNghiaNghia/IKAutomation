using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Helpers;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using System;
using System.Configuration;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public static class AppConfigOperationalMaintenanceOptionsProvider
    {
        public static OperationalMaintenanceOptions Load()
        {
            string root = ReadString("Operations.DiagnosticRootDirectory", "Diagnostics");
            return new OperationalMaintenanceOptions(root,
                ReadInt("Operations.MaintenanceIntervalMinutes", 5) * 60000,
                ReadInt("Operations.ScreenshotRetentionDays", 14),
                ReadLong("Operations.MaximumDiagnosticBytes", 5368709120L),
                ReadLong("Operations.MinimumFreeDiskBytes", 10737418240L),
                ReadLong("Operations.ResumeFreeDiskBytes", 12884901888L));
        }

        public static IOperationalMaintenanceService Create()
        {
            Logger.Configure(ReadLong("Operations.LogRotationBytes", 20971520L),
                ReadInt("Operations.LogRetentionDays", 30));
            return new FileSystemOperationalMaintenanceService(Load(),
                new ApplicationDiagnosticLogger());
        }

        private static string ReadString(string key, string fallback) =>
            string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings[key])
                ? fallback : ConfigurationManager.AppSettings[key].Trim();

        private static int ReadInt(string key, int fallback) =>
            int.TryParse(ConfigurationManager.AppSettings[key], out int value) ? value : fallback;

        private static long ReadLong(string key, long fallback) =>
            long.TryParse(ConfigurationManager.AppSettings[key], out long value) ? value : fallback;
    }
}
