using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class FileSystemOperationalMaintenanceService : IOperationalMaintenanceService
    {
        private readonly OperationalMaintenanceOptions options;
        private readonly IDiagnosticLogger logger;
        private readonly Func<DateTimeOffset> clock;
        private readonly Func<string, long> freeSpaceProvider;
        private readonly SemaphoreSlim maintenanceLock = new SemaphoreSlim(1, 1);
        private DateTimeOffset? lastRunAt;
        private OperationalMaintenanceResult lastResult;

        public FileSystemOperationalMaintenanceService(OperationalMaintenanceOptions options,
            IDiagnosticLogger logger)
            : this(options, logger, () => DateTimeOffset.Now, GetAvailableFreeSpace)
        {
        }

        public FileSystemOperationalMaintenanceService(OperationalMaintenanceOptions options,
            IDiagnosticLogger logger, Func<DateTimeOffset> clock,
            Func<string, long> freeSpaceProvider)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.freeSpaceProvider = freeSpaceProvider
                ?? throw new ArgumentNullException(nameof(freeSpaceProvider));
        }

        public async Task<OperationalMaintenanceResult> RunIfDueAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await maintenanceLock.WaitAsync(cancellationToken);
            try
            {
                DateTimeOffset now = clock();
                if (lastRunAt.HasValue
                    && (now - lastRunAt.Value).TotalMilliseconds < options.MaintenanceIntervalMs)
                    return Copy(lastResult, false);

                OperationalMaintenanceResult result = RunMaintenance(now, cancellationToken);
                lastRunAt = now;
                lastResult = result;
                return Copy(result, true);
            }
            finally
            {
                maintenanceLock.Release();
            }
        }

        private OperationalMaintenanceResult RunMaintenance(DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            string root = ResolveRoot(options.DiagnosticRootDirectory);
            var files = LoadDiagnosticFiles(root, cancellationToken);
            DateTime cutoff = now.UtcDateTime.AddDays(-options.ScreenshotRetentionDays);
            int deleted = 0;
            long reclaimed = 0;

            foreach (FileInfo file in files.Where(item => item.LastWriteTimeUtc < cutoff).ToArray())
                Delete(file, ref deleted, ref reclaimed, cancellationToken);

            files = LoadDiagnosticFiles(root, cancellationToken);
            long total = files.Sum(file => file.Length);
            foreach (FileInfo file in files.OrderBy(item => item.LastWriteTimeUtc))
            {
                if (total <= options.MaximumDiagnosticBytes) break;
                long length = file.Length;
                if (Delete(file, ref deleted, ref reclaimed, cancellationToken))
                    total -= length;
            }

            RemoveEmptyDirectories(root, cancellationToken);
            long free = freeSpaceProvider(root);
            bool wasSuspended = !DiagnosticStorageGate.IsWriteEnabled;
            if ((!wasSuspended && free < options.MinimumFreeDiskBytes)
                || (wasSuspended && free < options.ResumeFreeDiskBytes))
            {
                DiagnosticStorageGate.Suspend(
                    $"Free disk space is {FormatBytes(free)}, below the safe threshold.");
            }
            else
            {
                DiagnosticStorageGate.Resume();
            }

            bool suspended = !DiagnosticStorageGate.IsWriteEnabled;
            if (suspended != wasSuspended)
                logger.Info(suspended
                    ? "[Storage] Diagnostic screenshot writes suspended: "
                        + DiagnosticStorageGate.SuspensionReason
                    : "[Storage] Diagnostic screenshot writes resumed after disk space recovered.");

            return new OperationalMaintenanceResult
            {
                WasRun = true,
                DeletedFileCount = deleted,
                ReclaimedBytes = reclaimed,
                DiagnosticBytes = total,
                FreeDiskBytes = free,
                DiagnosticWritesSuspended = suspended,
                Message = $"Maintenance completed; deleted={deleted}, reclaimed={FormatBytes(reclaimed)}, "
                    + $"diagnostics={FormatBytes(total)}, free={FormatBytes(free)}, suspended={suspended}."
            };
        }

        private static List<FileInfo> LoadDiagnosticFiles(string root,
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(root)) return new List<FileInfo>();
            var result = new List<FileInfo>();
            foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string extension = Path.GetExtension(path);
                if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    result.Add(new FileInfo(path));
            }
            return result;
        }

        private static bool Delete(FileInfo file, ref int deleted, ref long reclaimed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                long length = file.Length;
                file.Delete();
                deleted++;
                reclaimed += length;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static void RemoveEmptyDirectories(string root,
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(root)) return;
            foreach (string directory in Directory.EnumerateDirectories(root, "*",
                SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string ResolveRoot(string configured)
        {
            return Path.IsPathRooted(configured) ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        }

        private static long GetAvailableFreeSpace(string path)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path));
            return new DriveInfo(root).AvailableFreeSpace;
        }

        private static string FormatBytes(long value) =>
            (value / 1073741824d).ToString("0.00") + " GB";

        private static OperationalMaintenanceResult Copy(OperationalMaintenanceResult source,
            bool wasRun)
        {
            if (source == null) return new OperationalMaintenanceResult { WasRun = false };
            return new OperationalMaintenanceResult
            {
                WasRun = wasRun,
                DeletedFileCount = source.DeletedFileCount,
                ReclaimedBytes = source.ReclaimedBytes,
                DiagnosticBytes = source.DiagnosticBytes,
                FreeDiskBytes = source.FreeDiskBytes,
                DiagnosticWritesSuspended = source.DiagnosticWritesSuspended,
                Message = source.Message
            };
        }
    }
}
