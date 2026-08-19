using IK_Auto_ADB.Core.Workflows;
using IK_Auto_ADB.Core.Diagnostics;
using IK_Auto_ADB.Helpers;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using System;
using System.Configuration;

namespace IK_Auto_ADB.Infrastructure.Workflows
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
            Logger.Configure(ReadLong("Operations.LogRotationBytes", 5242880L),
                ReadInt("Operations.LogRetentionDays", 7),
                ReadInt("Operations.LogFlushIntervalMs", 1000),
                ReadInt("Operations.LogBufferBytes", 65536),
                ReadInt("Operations.VerboseLogThrottleMs", 5000),
                ReadLong("Operations.MaximumLogArchiveBytes", 104857600L));
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
