using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class AdaptiveConcurrencyOptions
    {
        public AdaptiveConcurrencyOptions(int minimumConcurrency = 4,
            int initialConcurrency = 6, int maximumConcurrency = 20,
            int sampleIntervalMs = 5000, int healthySamplesToIncrease = 3,
            double highCpuPercent = 88d, long lowAvailableMemoryBytes = 2147483648L,
            double highTechnicalFailureRate = 0.25d, int observationWindowSize = 20,
            int highProbeLatencyMs = 30000,
            int automationStaggerMinMs = 2000, int automationStaggerMaxMs = 10000,
            int recoveryStaggerMinMs = 30000, int recoveryStaggerMaxMs = 60000)
        {
            if (minimumConcurrency < 1 || maximumConcurrency > 20
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

    public sealed class AdaptiveConcurrencyGate : IAdaptiveConcurrencyGate
    {
        private readonly object sync = new object();
        private readonly AdaptiveConcurrencyOptions options;
        private readonly IHostResourceProbe resourceProbe;
        private readonly Queue<PendingAdmission> pending = new Queue<PendingAdmission>();
        private readonly Queue<bool> technicalOutcomes = new Queue<bool>();
        private readonly Queue<bool> latencyOutcomes = new Queue<bool>();
        private int currentLimit;
        private int activeExecutions;
        private int healthySamples;
        private DateTimeOffset nextSampleAt = DateTimeOffset.MinValue;
        private DateTimeOffset nextAutomationAdmissionAt = DateTimeOffset.MinValue;
        private DateTimeOffset nextRecoveryAdmissionAt = DateTimeOffset.MinValue;
        private HostResourceSnapshot lastResources = new HostResourceSnapshot();

        public AdaptiveConcurrencyGate(AdaptiveConcurrencyOptions options,
            IHostResourceProbe resourceProbe = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.resourceProbe = resourceProbe ?? new WindowsHostResourceProbe();
            currentLimit = options.InitialConcurrency;
        }

        public async Task<IAdaptiveConcurrencyLease> AcquireAsync(string deviceName,
            AdaptiveOperationKind operationKind, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("Device name is required.", nameof(deviceName));
            int staggerDelay = ReserveStaggerDelay(deviceName.Trim(), operationKind);
            if (staggerDelay > 0)
                await Task.Delay(staggerDelay, cancellationToken);
            RefreshLimitIfDue();

            var admission = new PendingAdmission();
            lock (sync)
            {
                pending.Enqueue(admission);
                PumpAdmissionsLocked();
            }
            using (cancellationToken.Register(() => admission.Cancel(cancellationToken)))
            {
                await admission.Task;
            }
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
                    CurrentLimit = currentLimit,
                    ActiveExecutions = activeExecutions,
                    QueuedExecutions = CountPendingLocked(),
                    CpuUsagePercent = lastResources.CpuUsagePercent,
                    AvailableMemoryBytes = lastResources.AvailableMemoryBytes,
                    TechnicalFailureRate = GetFailureRateLocked()
                };
            }
        }

        private int ReserveStaggerDelay(string deviceName, AdaptiveOperationKind kind)
        {
            lock (sync)
            {
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
                bool pressured = resources.CpuUsagePercent >= options.HighCpuPercent
                    || (resources.AvailableMemoryBytes > 0
                        && resources.AvailableMemoryBytes <= options.LowAvailableMemoryBytes)
                    || hasFailurePressure || hasLatencyPressure;
                if (pressured)
                {
                    currentLimit = Math.Max(options.MinimumConcurrency, currentLimit - 1);
                    healthySamples = 0;
                }
                else if (++healthySamples >= options.HealthySamplesToIncrease)
                {
                    currentLimit = Math.Min(options.MaximumConcurrency, currentLimit + 1);
                    healthySamples = 0;
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
