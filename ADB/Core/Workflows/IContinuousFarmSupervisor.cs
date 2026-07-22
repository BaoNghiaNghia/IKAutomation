using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IContinuousFarmSupervisor
    {
        Task<ContinuousFarmSupervisorResult> RunAsync(
            IReadOnlyList<string> deviceNames,
            OneShotFarmRequest request,
            IProgress<ContinuousFarmSupervisorProgress> progress,
            CancellationToken cancellationToken);
    }

    public enum ContinuousFarmDeviceState
    {
        Preflight,
        Ready,
        Running,
        Waiting,
        Recovering,
        Quarantined,
        Stopped
    }

    public sealed class ContinuousFarmDeviceSnapshot
    {
        public string DeviceName { get; set; }
        public ContinuousFarmDeviceState State { get; set; }
        public int CycleCount { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset LastTransitionAt { get; set; }
        public DateTimeOffset? LastSuccessAt { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
        public DateTimeOffset? NextAttemptAt { get; set; }
        public string Message { get; set; }
        public string LastError { get; set; }
    }

    public sealed class ContinuousFarmSupervisorProgress
    {
        public ContinuousFarmDeviceSnapshot Device { get; set; }
        public MultiDeviceOneShotFarmProgress FarmProgress { get; set; }
    }

    public sealed class ContinuousFarmSupervisorResult
    {
        public IReadOnlyList<ContinuousFarmDeviceSnapshot> Devices { get; set; }
        public bool WasCancelled { get; set; }
    }

    public sealed class ContinuousFarmSupervisorOptions
    {
        public ContinuousFarmSupervisorOptions(int cycleIntervalMs = 900000,
            int failureRetryDelayMs = 120000)
        {
            if (cycleIntervalMs < 1)
                throw new ArgumentOutOfRangeException(nameof(cycleIntervalMs));
            if (failureRetryDelayMs < 1)
                throw new ArgumentOutOfRangeException(nameof(failureRetryDelayMs));
            CycleIntervalMs = cycleIntervalMs;
            FailureRetryDelayMs = failureRetryDelayMs;
        }

        public int CycleIntervalMs { get; }
        public int FailureRetryDelayMs { get; }
    }
}
