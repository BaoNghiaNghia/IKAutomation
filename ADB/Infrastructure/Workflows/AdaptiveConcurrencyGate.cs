using IK_Auto_ADB.Core.Workflows;
using IK_Auto_ADB.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Infrastructure.Workflows
{
    public sealed class AdaptiveConcurrencyOptions
    {
        public const int DefaultMinimumConcurrency = 8;
        public const int DefaultInitialConcurrency = 16;
        public const int DefaultMaximumConcurrency = 20;

        public AdaptiveConcurrencyOptions(int minimumConcurrency = DefaultMinimumConcurrency,
            int initialConcurrency = DefaultInitialConcurrency,
            int maximumConcurrency = DefaultMaximumConcurrency,
            int sampleIntervalMs = 5000, int healthySamplesToIncrease = 3,
            double highCpuPercent = 88d, long lowAvailableMemoryBytes = 2147483648L,
            double highTechnicalFailureRate = 0.25d, int observationWindowSize = 20,
            int highProbeLatencyMs = 30000,
            int automationStaggerMinMs = 0, int automationStaggerMaxMs = 0,
            int recoveryStaggerMinMs = 0, int recoveryStaggerMaxMs = 0,
            int highScreenshotGateWaitMs = 1500, int highVisionGateWaitMs = 1000,
            double highIoFailureRate = 0.15d, int queuePressureWindows = 3,
            int adjustmentCooldownMs = 10000, int highGameplayLeaseWaitMs = 3000)
        {
            if (minimumConcurrency < 1 || maximumConcurrency > 25
                || initialConcurrency < minimumConcurrency
                || initialConcurrency > maximumConcurrency)
                throw new ArgumentOutOfRangeException(nameof(initialConcurrency));
            if (sampleIntervalMs < 1 || healthySamplesToIncrease < 1)
                throw new ArgumentOutOfRangeException(nameof(sampleIntervalMs));
            if (highCpuPercent <= 0 || highCpuPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(highCpuPercent));
            if (lowAvailableMemoryBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(lowAvailableMemoryBytes));
            if (highTechnicalFailureRate <= 0 || highTechnicalFailureRate > 1)
                throw new ArgumentOutOfRangeException(nameof(highTechnicalFailureRate));
            if (observationWindowSize < 4)
                throw new ArgumentOutOfRangeException(nameof(observationWindowSize));
            if (highProbeLatencyMs < 1)
                throw new ArgumentOutOfRangeException(nameof(highProbeLatencyMs));
            if (highScreenshotGateWaitMs < 1 || highVisionGateWaitMs < 1
                || highGameplayLeaseWaitMs < 1 || queuePressureWindows < 2
                || adjustmentCooldownMs < 0 || highIoFailureRate <= 0
                || highIoFailureRate > 1)
                throw new ArgumentOutOfRangeException(nameof(highScreenshotGateWaitMs));
            ValidateStagger(automationStaggerMinMs, automationStaggerMaxMs,
                nameof(automationStaggerMinMs));
            ValidateStagger(recoveryStaggerMinMs, recoveryStaggerMaxMs,
                nameof(recoveryStaggerMinMs));
            MinimumConcurrency = minimumConcurrency;
            InitialConcurrency = initialConcurrency;
            MaximumConcurrency = maximumConcurrency;
            SampleIntervalMs = sampleIntervalMs;
            HealthySamplesToIncrease = healthySamplesToIncrease;
            HighCpuPercent = highCpuPercent;
            LowAvailableMemoryBytes = lowAvailableMemoryBytes;
            HighTechnicalFailureRate = highTechnicalFailureRate;
            ObservationWindowSize = observationWindowSize;
            HighProbeLatencyMs = highProbeLatencyMs;
            AutomationStaggerMinMs = automationStaggerMinMs;
            AutomationStaggerMaxMs = automationStaggerMaxMs;
            RecoveryStaggerMinMs = recoveryStaggerMinMs;
            RecoveryStaggerMaxMs = recoveryStaggerMaxMs;
            HighScreenshotGateWaitMs = highScreenshotGateWaitMs;
            HighVisionGateWaitMs = highVisionGateWaitMs;
            HighIoFailureRate = highIoFailureRate;
            QueuePressureWindows = queuePressureWindows;
            AdjustmentCooldownMs = adjustmentCooldownMs;
            HighGameplayLeaseWaitMs = highGameplayLeaseWaitMs;
        }

        public int MinimumConcurrency { get; }
        public int InitialConcurrency { get; }
        public int MaximumConcurrency { get; }
        public int SampleIntervalMs { get; }
        public int HealthySamplesToIncrease { get; }
        public double HighCpuPercent { get; }
        public long LowAvailableMemoryBytes { get; }
        public double HighTechnicalFailureRate { get; }
        public int ObservationWindowSize { get; }
        public int HighProbeLatencyMs { get; }
        public int AutomationStaggerMinMs { get; }
        public int AutomationStaggerMaxMs { get; }
        public int RecoveryStaggerMinMs { get; }
        public int RecoveryStaggerMaxMs { get; }
        public int HighScreenshotGateWaitMs { get; }
        public int HighVisionGateWaitMs { get; }
        public double HighIoFailureRate { get; }
        public int QueuePressureWindows { get; }
        public int AdjustmentCooldownMs { get; }
        public int HighGameplayLeaseWaitMs { get; }

        private static void ValidateStagger(int minimum, int maximum, string name)
        {
            if (minimum < 0 || maximum < minimum)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public interface IHostResourceProbe
    {
        HostResourceSnapshot Sample();
    }

    public sealed class HostResourceSnapshot
    {
        public double CpuUsagePercent { get; set; }
        public long AvailableMemoryBytes { get; set; }
    }

    public sealed class AdaptiveConcurrencyGate : IAdaptiveConcurrencyAdmissionGate
    {
        private readonly object sync = new object();
        private readonly AdaptiveConcurrencyOptions options;
        private readonly IHostResourceProbe resourceProbe;
        private readonly Action<string> infoLogger;
        private readonly Func<int, CancellationToken, Task> delayAsync;
        private readonly Queue<PendingAdmission> pending = new Queue<PendingAdmission>();
        private readonly Queue<bool> technicalOutcomes = new Queue<bool>();
        private readonly Queue<bool> latencyOutcomes = new Queue<bool>();
        private readonly HashSet<string> appliedStaggerKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> appliedStaggerKeyOrder = new Queue<string>();
        private int currentLimit;
        private int activeExecutions;
        private int healthySamples;
        private int queuePressureSamples;
        private long lastRuntimeSampleVersion;
        private DateTimeOffset nextAdjustmentAt = DateTimeOffset.MinValue;
        private DateTimeOffset nextSampleAt = DateTimeOffset.MinValue;
        private DateTimeOffset nextAutomationAdmissionAt = DateTimeOffset.MinValue;
        private DateTimeOffset nextRecoveryAdmissionAt = DateTimeOffset.MinValue;
        private HostResourceSnapshot lastResources = new HostResourceSnapshot();

        public AdaptiveConcurrencyGate(AdaptiveConcurrencyOptions options,
            IHostResourceProbe resourceProbe = null, Action<string> infoLogger = null,
            Func<int, CancellationToken, Task> delayAsync = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.resourceProbe = resourceProbe ?? new WindowsHostResourceProbe();
            this.infoLogger = infoLogger ?? (message => Trace.TraceInformation(message));
            this.delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
            currentLimit = options.InitialConcurrency;
        }

        public async Task<IAdaptiveConcurrencyLease> AcquireAsync(string deviceName,
            AdaptiveOperationKind operationKind, CancellationToken cancellationToken)
        {
            return await AcquireAsync(deviceName, operationKind, null, cancellationToken);
        }

        public async Task<IAdaptiveConcurrencyLease> AcquireAsync(string deviceName,
            AdaptiveOperationKind operationKind, AdaptiveAdmissionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("Device name is required.", nameof(deviceName));
            string normalizedDevice = deviceName.Trim();
            int staggerDelay = request != null && request.ApplyStartupStagger
                ? ReserveStaggerDelay(normalizedDevice, operationKind, request.StaggerKey)
                : 0;
            if (staggerDelay > 0)
                await delayAsync(staggerDelay, cancellationToken);
            RefreshLimitIfDue();

            var admission = new PendingAdmission();
            var wait = Stopwatch.StartNew();
            lock (sync)
            {
                pending.Enqueue(admission);
                PumpAdmissionsLocked();
            }
            using (cancellationToken.Register(() => admission.Cancel(cancellationToken)))
            {
                await admission.Task;
            }
            wait.Stop();
            AdaptiveConcurrencySnapshot snapshot = GetSnapshot();
            infoLogger($"[Adaptive Admission] DeviceName='{normalizedDevice}', "
                + $"DeviceIndex={request?.DeviceIndex ?? -1}, "
                + $"ExecutionPhase='{request?.ExecutionPhase.ToString() ?? "Unspecified"}', "
                + $"AdaptiveGateWaitMs={wait.ElapsedMilliseconds}, "
                + $"StartupStaggerDelayMs={staggerDelay}, Active={snapshot.ActiveExecutions}, "
                + $"Queued={snapshot.QueuedExecutions}");
            return new Lease(this);
        }

        public void Report(AdaptiveConcurrencyObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            lock (sync)
            {
                technicalOutcomes.Enqueue(observation.TechnicalFailure);
                while (technicalOutcomes.Count > options.ObservationWindowSize)
                    technicalOutcomes.Dequeue();
                if (observation.UseDurationAsPressure)
                {
                    latencyOutcomes.Enqueue(observation.DurationMs >= options.HighProbeLatencyMs);
                    while (latencyOutcomes.Count > options.ObservationWindowSize)
                        latencyOutcomes.Dequeue();
                }
            }
            RefreshLimitIfDue();
        }

        public AdaptiveConcurrencySnapshot GetSnapshot()
        {
            lock (sync)
            {
                return new AdaptiveConcurrencySnapshot
                {
                    Enabled = true,
                    MinimumLimit = options.MinimumConcurrency,
                    InitialLimit = options.InitialConcurrency,
                    MaximumLimit = options.MaximumConcurrency,
                    CurrentLimit = currentLimit,
                    ActiveExecutions = activeExecutions,
                    QueuedExecutions = CountPendingLocked(),
                    CpuUsagePercent = lastResources.CpuUsagePercent,
                    AvailableMemoryBytes = lastResources.AvailableMemoryBytes,
                    TechnicalFailureRate = GetFailureRateLocked()
                };
            }
        }

        /// <summary>Forces one evaluation using the latest aggregate samples.</summary>
        public void EvaluatePressureNow()
        {
            lock (sync) nextSampleAt = DateTimeOffset.MinValue;
            RefreshLimitIfDue();
        }

        private int ReserveStaggerDelay(string deviceName, AdaptiveOperationKind kind,
            string staggerKey)
        {
            lock (sync)
            {
                if (kind == AdaptiveOperationKind.Automation)
                {
                    string key = deviceName + "|" + (staggerKey ?? string.Empty);
                    if (!appliedStaggerKeys.Add(key)) return 0;
                    appliedStaggerKeyOrder.Enqueue(key);
                    while (appliedStaggerKeyOrder.Count > 1024)
                        appliedStaggerKeys.Remove(appliedStaggerKeyOrder.Dequeue());
                }
                DateTimeOffset now = DateTimeOffset.UtcNow;
                DateTimeOffset scheduled;
                int minimum;
                int maximum;
                if (kind == AdaptiveOperationKind.Recovery)
                {
                    scheduled = nextRecoveryAdmissionAt > now ? nextRecoveryAdmissionAt : now;
                    minimum = options.RecoveryStaggerMinMs;
                    maximum = options.RecoveryStaggerMaxMs;
                    nextRecoveryAdmissionAt = scheduled.AddMilliseconds(
                        StableSpacing(deviceName, minimum, maximum));
                }
                else
                {
                    scheduled = nextAutomationAdmissionAt > now
                        ? nextAutomationAdmissionAt : now;
                    minimum = options.AutomationStaggerMinMs;
                    maximum = options.AutomationStaggerMaxMs;
                    nextAutomationAdmissionAt = scheduled.AddMilliseconds(
                        StableSpacing(deviceName, minimum, maximum));
                }
                double delay = (scheduled - now).TotalMilliseconds;
                return delay <= 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Ceiling(delay));
            }
        }

        private static int StableSpacing(string deviceName, int minimum, int maximum)
        {
            if (maximum <= minimum) return minimum;
            unchecked
            {
                int hash = 17;
                foreach (char value in deviceName.ToUpperInvariant()) hash = hash * 31 + value;
                int range = maximum - minimum + 1;
                return minimum + (int)((uint)hash % (uint)range);
            }
        }

        private void RefreshLimitIfDue()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (sync)
            {
                if (now < nextSampleAt) return;
                nextSampleAt = now.AddMilliseconds(options.SampleIntervalMs);
            }

            HostResourceSnapshot resources;
            try { resources = resourceProbe.Sample() ?? new HostResourceSnapshot(); }
            catch { resources = new HostResourceSnapshot(); }
            lock (sync)
            {
                lastResources = resources;
                double failureRate = GetFailureRateLocked();
                bool hasFailurePressure = technicalOutcomes.Count >= 4
                    && failureRate >= options.HighTechnicalFailureRate;
                bool hasLatencyPressure = latencyOutcomes.Count >= 4
                    && GetPressureRateLocked(latencyOutcomes)
                        >= options.HighTechnicalFailureRate;
                RuntimePressureSnapshot runtime = RuntimePressureMetrics.GetSnapshot();
                bool hasFreshRuntimeSample = runtime.SampleVersion != lastRuntimeSampleVersion;
                if (hasFreshRuntimeSample) lastRuntimeSampleVersion = runtime.SampleVersion;
                bool queuePressure = runtime.ScreenshotP95WaitMs
                        >= options.HighScreenshotGateWaitMs
                    || runtime.VisionP95WaitMs >= options.HighVisionGateWaitMs
                    || runtime.ScreenshotFailureRate >= options.HighIoFailureRate
                    || runtime.VisionFailureRate >= options.HighIoFailureRate
                    || runtime.AverageGameplayLeaseWaitMs
                        >= options.HighGameplayLeaseWaitMs;
                if (hasFreshRuntimeSample)
                    queuePressureSamples = queuePressure ? queuePressureSamples + 1 : 0;
                bool sustainedQueuePressure = queuePressureSamples
                    >= options.QueuePressureWindows;
                bool pressured = resources.CpuUsagePercent >= options.HighCpuPercent
                    || (resources.AvailableMemoryBytes > 0
                        && resources.AvailableMemoryBytes <= options.LowAvailableMemoryBytes)
                    || hasFailurePressure || hasLatencyPressure || sustainedQueuePressure;
                if (pressured && now >= nextAdjustmentAt)
                {
                    int previous = currentLimit;
                    currentLimit = Math.Max(options.MinimumConcurrency, currentLimit - 1);
                    healthySamples = 0;
                    nextAdjustmentAt = now.AddMilliseconds(options.AdjustmentCooldownMs);
                    if (currentLimit != previous)
                        infoLogger($"[Adaptive Concurrency] Concurrency {previous} -> {currentLimit}. "
                            + (sustainedQueuePressure
                                ? $"Reason: screenshot p95={runtime.ScreenshotP95WaitMs}ms, "
                                    + $"vision p95={runtime.VisionP95WaitMs}ms for "
                                    + $"{queuePressureSamples} windows."
                                : "Reason: sustained host/failure pressure."));
                }
                else if (!queuePressure && now >= nextAdjustmentAt
                    && ++healthySamples >= options.HealthySamplesToIncrease)
                {
                    int previous = currentLimit;
                    currentLimit = Math.Min(options.MaximumConcurrency, currentLimit + 1);
                    healthySamples = 0;
                    nextAdjustmentAt = now.AddMilliseconds(options.AdjustmentCooldownMs);
                    if (currentLimit != previous)
                        infoLogger($"[Adaptive Concurrency] Concurrency {previous} -> {currentLimit}. "
                            + "Reason: CPU, memory, queues and failure rate remained healthy.");
                }
                PumpAdmissionsLocked();
            }
        }

        private void Release()
        {
            lock (sync)
            {
                if (activeExecutions > 0) activeExecutions--;
                PumpAdmissionsLocked();
            }
        }

        private void PumpAdmissionsLocked()
        {
            while (activeExecutions < currentLimit && pending.Count > 0)
            {
                PendingAdmission admission = pending.Dequeue();
                if (!admission.TryAdmit()) continue;
                activeExecutions++;
            }
        }

        private int CountPendingLocked()
        {
            int count = 0;
            foreach (PendingAdmission admission in pending)
                if (!admission.Task.IsCompleted) count++;
            return count;
        }

        private double GetFailureRateLocked()
        {
            return GetPressureRateLocked(technicalOutcomes);
        }

        private static double GetPressureRateLocked(IEnumerable<bool> outcomes)
        {
            int failures = 0;
            int count = 0;
            foreach (bool failed in outcomes)
            {
                count++;
                if (failed) failures++;
            }
            return count == 0 ? 0d : (double)failures / count;
        }

        private sealed class Lease : IAdaptiveConcurrencyLease
        {
            private AdaptiveConcurrencyGate owner;
            public Lease(AdaptiveConcurrencyGate owner) { this.owner = owner; }
            public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
        }

        private sealed class PendingAdmission
        {
            private readonly TaskCompletionSource<bool> completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Task => completion.Task;
            public bool TryAdmit() => completion.TrySetResult(true);
            public void Cancel(CancellationToken token) => completion.TrySetCanceled(token);
        }
    }

    public sealed class WindowsHostResourceProbe : IHostResourceProbe
    {
        private readonly object sync = new object();
        private ulong previousIdle;
        private ulong previousKernel;
        private ulong previousUser;
        private bool hasCpuSample;

        public HostResourceSnapshot Sample()
        {
            lock (sync)
            {
                double cpu = SampleCpu();
                var memory = new MemoryStatusEx();
                memory.Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
                long available = GlobalMemoryStatusEx(ref memory)
                    ? (long)Math.Min((ulong)long.MaxValue, memory.AvailablePhysical)
                    : 0L;
                return new HostResourceSnapshot
                {
                    CpuUsagePercent = cpu,
                    AvailableMemoryBytes = available
                };
            }
        }

        private double SampleCpu()
        {
            FileTime idleTime;
            FileTime kernelTime;
            FileTime userTime;
            if (!GetSystemTimes(out idleTime, out kernelTime, out userTime)) return 0d;
            ulong idle = idleTime.Value;
            ulong kernel = kernelTime.Value;
            ulong user = userTime.Value;
            if (!hasCpuSample)
            {
                previousIdle = idle;
                previousKernel = kernel;
                previousUser = user;
                hasCpuSample = true;
                return 0d;
            }
            ulong idleDelta = idle - previousIdle;
            ulong totalDelta = (kernel - previousKernel) + (user - previousUser);
            previousIdle = idle;
            previousKernel = kernel;
            previousUser = user;
            if (totalDelta == 0) return 0d;
            return Math.Max(0d, Math.Min(100d,
                100d * (totalDelta - Math.Min(idleDelta, totalDelta)) / totalDelta));
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FileTime idleTime,
            out FileTime kernelTime, out FileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            public uint Low;
            public uint High;
            public ulong Value => ((ulong)High << 32) | Low;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }
    }
}
