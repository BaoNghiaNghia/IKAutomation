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
        public DateTimeOffset LastProgressAt { get; set; }
        public string CurrentOperation { get; set; }
        public int WatchdogTimeoutCount { get; set; }
        public int RecoveryAttemptCount { get; set; }
        public DeviceRecoveryStep? LastRecoveryStep { get; set; }
        public int TechnicalFailuresInWindow { get; set; }
        public int QuarantineCount { get; set; }
        public DateTimeOffset? CircuitOpenUntil { get; set; }
        public int LastBackoffDelayMs { get; set; }
        public string CurrentResource { get; set; }
        public int? CurrentLevel { get; set; }
        public string CurrentTeam { get; set; }
        public bool RestoredFromCheckpoint { get; set; }
        public DateTimeOffset? CheckpointSavedAt { get; set; }
        public long FreeDiskBytes { get; set; }
        public bool DiagnosticWritesSuspended { get; set; }
        public DateTimeOffset? LastMaintenanceAt { get; set; }
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
            int failureRetryDelayMs = 120000, int noProgressTimeoutMs = 300000,
            int watchdogPollIntervalMs = 1000, int cancellationGraceMs = 10000,
            int waitingNoProgressTimeoutMs = 1200000,
            IReadOnlyList<int> technicalRetryDelaysMs = null,
            int retryJitterMaxMs = 15000, int circuitFailureThreshold = 5,
            int circuitWindowMs = 1800000, int quarantineCooldownMs = 1800000,
            int checkpointIntervalMs = 30000)
        {
            if (cycleIntervalMs < 1)
                throw new ArgumentOutOfRangeException(nameof(cycleIntervalMs));
            if (failureRetryDelayMs < 1)
                throw new ArgumentOutOfRangeException(nameof(failureRetryDelayMs));
            if (noProgressTimeoutMs < 1)
                throw new ArgumentOutOfRangeException(nameof(noProgressTimeoutMs));
            if (watchdogPollIntervalMs < 1 || watchdogPollIntervalMs > noProgressTimeoutMs)
                throw new ArgumentOutOfRangeException(nameof(watchdogPollIntervalMs));
            if (cancellationGraceMs < 1)
                throw new ArgumentOutOfRangeException(nameof(cancellationGraceMs));
            if (waitingNoProgressTimeoutMs < noProgressTimeoutMs)
                throw new ArgumentOutOfRangeException(nameof(waitingNoProgressTimeoutMs));
            int[] retryDelays = technicalRetryDelaysMs == null
                ? new[] { 30000, 120000, 600000 }
                : new List<int>(technicalRetryDelaysMs).ToArray();
            if (retryDelays.Length == 0 || Array.Exists(retryDelays, value => value < 1))
                throw new ArgumentOutOfRangeException(nameof(technicalRetryDelaysMs));
            if (retryJitterMaxMs < 0)
                throw new ArgumentOutOfRangeException(nameof(retryJitterMaxMs));
            if (circuitFailureThreshold < 1)
                throw new ArgumentOutOfRangeException(nameof(circuitFailureThreshold));
            if (circuitWindowMs < 1)
                throw new ArgumentOutOfRangeException(nameof(circuitWindowMs));
            if (quarantineCooldownMs < 1)
                throw new ArgumentOutOfRangeException(nameof(quarantineCooldownMs));
            if (checkpointIntervalMs < 1)
                throw new ArgumentOutOfRangeException(nameof(checkpointIntervalMs));
            CycleIntervalMs = cycleIntervalMs;
            FailureRetryDelayMs = failureRetryDelayMs;
            NoProgressTimeoutMs = noProgressTimeoutMs;
            WatchdogPollIntervalMs = watchdogPollIntervalMs;
            CancellationGraceMs = cancellationGraceMs;
            WaitingNoProgressTimeoutMs = waitingNoProgressTimeoutMs;
            TechnicalRetryDelaysMs = retryDelays;
            RetryJitterMaxMs = retryJitterMaxMs;
            CircuitFailureThreshold = circuitFailureThreshold;
            CircuitWindowMs = circuitWindowMs;
            QuarantineCooldownMs = quarantineCooldownMs;
            CheckpointIntervalMs = checkpointIntervalMs;
        }

        public int CycleIntervalMs { get; }
        public int FailureRetryDelayMs { get; }
        public int NoProgressTimeoutMs { get; }
        public int WatchdogPollIntervalMs { get; }
        public int CancellationGraceMs { get; }
        public int WaitingNoProgressTimeoutMs { get; }
        public IReadOnlyList<int> TechnicalRetryDelaysMs { get; }
        public int RetryJitterMaxMs { get; }
        public int CircuitFailureThreshold { get; }
        public int CircuitWindowMs { get; }
        public int QuarantineCooldownMs { get; }
        public int CheckpointIntervalMs { get; }
    }
}
