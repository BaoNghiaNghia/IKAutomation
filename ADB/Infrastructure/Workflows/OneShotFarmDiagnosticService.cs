using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class OneShotFarmDiagnosticOptions
    {
        public OneShotFarmDiagnosticOptions(bool enableSuccessScreenshots = false,
            bool enableFailureScreenshots = true, int cooldownSeconds = 30,
            int maxScreenshotsPerDevice = 100, int retentionDays = 7,
            int queueCapacity = 16)
        {
            if (cooldownSeconds < 0 || maxScreenshotsPerDevice < 1
                || retentionDays < 1 || queueCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(cooldownSeconds));
            EnableSuccessScreenshots = enableSuccessScreenshots;
            EnableFailureScreenshots = enableFailureScreenshots;
            Cooldown = TimeSpan.FromSeconds(cooldownSeconds);
            MaxScreenshotsPerDevice = maxScreenshotsPerDevice;
            Retention = TimeSpan.FromDays(retentionDays);
            QueueCapacity = queueCapacity;
        }
        public bool EnableSuccessScreenshots { get; }
        public bool EnableFailureScreenshots { get; }
        public TimeSpan Cooldown { get; }
        public int MaxScreenshotsPerDevice { get; }
        public TimeSpan Retention { get; }
        public int QueueCapacity { get; }
    }

    /// <summary>Captures promptly and delegates disk I/O to one bounded background writer.</summary>
    public sealed class OneShotFarmDiagnosticService : IOneShotFarmDiagnosticService, IDisposable
    {
        private readonly ILdPlayerClient client;
        private readonly string root;
        private readonly OneShotFarmDiagnosticOptions options;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<string, byte[], Task> writer;
        private readonly BlockingCollection<DiagnosticJob> queue;
        private readonly Task worker;
        private readonly object sync = new object();
        private readonly Dictionary<string, DateTimeOffset> lastCaptures =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        private TaskCompletionSource<bool> idle = CompletedIdle();
        private int pending;
        private int disposed;

        public OneShotFarmDiagnosticService(ILdPlayerClient client, string rootDirectory)
            : this(client, rootDirectory, new OneShotFarmDiagnosticOptions(), null, null) { }

        public OneShotFarmDiagnosticService(ILdPlayerClient client, string rootDirectory,
            OneShotFarmDiagnosticOptions options, Func<DateTimeOffset> utcNow,
            Func<string, byte[], Task> writer)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Diagnostic root is required.", nameof(rootDirectory));
            root = Path.GetFullPath(rootDirectory);
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            this.writer = writer ?? WriteFileAsync;
            queue = new BlockingCollection<DiagnosticJob>(options.QueueCapacity);
            worker = Task.Run(ProcessQueueAsync);
        }

        public async Task<string> CaptureAsync(string deviceName, OneShotFarmStep step,
            OneShotFarmOutcome outcome, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref disposed) != 0 || !DiagnosticStorageGate.IsWriteEnabled)
                return null;
            bool success = outcome == OneShotFarmOutcome.MarchStarted;
            if ((success && !options.EnableSuccessScreenshots)
                || (!success && !options.EnableFailureScreenshots)) return null;

            DateTimeOffset now = utcNow();
            string safeDevice = ScreenshotPathPolicy.SanitizeDeviceName(deviceName);
            string category = ScreenshotPathPolicy.SanitizeStateName(
                step.ToString().ToLowerInvariant() + "_" + outcome.ToString().ToLowerInvariant());
            string cooldownKey = safeDevice + "|" + category;
            lock (sync)
            {
                if (lastCaptures.TryGetValue(cooldownKey, out DateTimeOffset last)
                    && now - last < options.Cooldown) return null;
                lastCaptures[cooldownKey] = now;
            }

            byte[] png = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            string directory = Path.Combine(root, safeDevice, now.ToString("yyyy-MM-dd"));
            string path = Path.Combine(directory,
                $"ikdiag_{safeDevice}_{category}_{now:yyyyMMddTHHmmssfffZ}_{Guid.NewGuid():N}.png");
            var job = new DiagnosticJob(path, safeDevice, png, now);
            lock (sync)
            {
                if (pending == 0) idle = NewIdle();
                if (!queue.TryAdd(job)) return null; // Drop newest when capacity is full.
                pending++;
            }
            return path;
        }

        public Task FlushAsync()
        {
            lock (sync) return idle.Task;
        }

        private async Task ProcessQueueAsync()
        {
            foreach (DiagnosticJob job in queue.GetConsumingEnumerable())
            {
                try
                {
                    await writer(job.Path, job.Png);
                    Cleanup(job.DeviceName, job.CreatedAt);
                }
                catch { }
                finally
                {
                    job.Png = null;
                    lock (sync)
                    {
                        pending--;
                        if (pending == 0) idle.TrySetResult(true);
                    }
                }
            }
        }

        private static async Task WriteFileAsync(string path, byte[] png)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 81920, true))
                    await stream.WriteAsync(png, 0, png.Length);
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) TryDelete(temporary);
            }
        }

        private void Cleanup(string deviceName, DateTimeOffset now)
        {
            string deviceRoot = Path.Combine(root, deviceName);
            if (!Directory.Exists(deviceRoot)) return;
            string[] files = Directory.GetFiles(deviceRoot, "ikdiag_*.png",
                SearchOption.AllDirectories);
            foreach (string file in files.Where(file =>
                File.GetLastWriteTimeUtc(file) < now.UtcDateTime - options.Retention))
                TryDelete(file);
            files = Directory.GetFiles(deviceRoot, "ikdiag_*.png", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
            foreach (string file in files.Skip(options.MaxScreenshotsPerDevice)) TryDelete(file);
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            queue.CompleteAdding();
            // Shutdown never blocks gameplay. The active write may finish, while
            // queued jobs are explicitly discarded and their large buffers released.
            while (queue.TryTake(out DiagnosticJob job))
            {
                job.Png = null;
                lock (sync)
                {
                    pending--;
                    if (pending == 0) idle.TrySetResult(true);
                }
            }
        }

        private static TaskCompletionSource<bool> NewIdle() =>
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource<bool> CompletedIdle()
        {
            var value = NewIdle(); value.SetResult(true); return value;
        }

        private sealed class DiagnosticJob
        {
            public DiagnosticJob(string path, string deviceName, byte[] png,
                DateTimeOffset createdAt)
            { Path = path; DeviceName = deviceName; Png = png; CreatedAt = createdAt; }
            public string Path { get; }
            public string DeviceName { get; }
            public byte[] Png { get; set; }
            public DateTimeOffset CreatedAt { get; }
        }
    }
}
