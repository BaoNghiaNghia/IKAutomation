using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IOperationalMaintenanceService
    {
        Task<OperationalMaintenanceResult> RunIfDueAsync(CancellationToken cancellationToken);
    }

    public sealed class OperationalMaintenanceResult
    {
        public bool WasRun { get; set; }
        public int DeletedFileCount { get; set; }
        public long ReclaimedBytes { get; set; }
        public long DiagnosticBytes { get; set; }
        public long FreeDiskBytes { get; set; }
        public bool DiagnosticWritesSuspended { get; set; }
        public string Message { get; set; }
    }

    public sealed class OperationalMaintenanceOptions
    {
        public OperationalMaintenanceOptions(string diagnosticRootDirectory,
            int maintenanceIntervalMs = 300000, int screenshotRetentionDays = 14,
            long maximumDiagnosticBytes = 5368709120L,
            long minimumFreeDiskBytes = 10737418240L,
            long resumeFreeDiskBytes = 12884901888L)
        {
            if (string.IsNullOrWhiteSpace(diagnosticRootDirectory))
                throw new ArgumentException("Diagnostic root directory is required.", nameof(diagnosticRootDirectory));
            if (maintenanceIntervalMs < 1)
                throw new ArgumentOutOfRangeException(nameof(maintenanceIntervalMs));
            if (screenshotRetentionDays < 1)
                throw new ArgumentOutOfRangeException(nameof(screenshotRetentionDays));
            if (maximumDiagnosticBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumDiagnosticBytes));
            if (minimumFreeDiskBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumFreeDiskBytes));
            if (resumeFreeDiskBytes < minimumFreeDiskBytes)
                throw new ArgumentOutOfRangeException(nameof(resumeFreeDiskBytes));
            DiagnosticRootDirectory = diagnosticRootDirectory.Trim();
            MaintenanceIntervalMs = maintenanceIntervalMs;
            ScreenshotRetentionDays = screenshotRetentionDays;
            MaximumDiagnosticBytes = maximumDiagnosticBytes;
            MinimumFreeDiskBytes = minimumFreeDiskBytes;
            ResumeFreeDiskBytes = resumeFreeDiskBytes;
        }

        public string DiagnosticRootDirectory { get; }
        public int MaintenanceIntervalMs { get; }
        public int ScreenshotRetentionDays { get; }
        public long MaximumDiagnosticBytes { get; }
        public long MinimumFreeDiskBytes { get; }
        public long ResumeFreeDiskBytes { get; }
    }
}
