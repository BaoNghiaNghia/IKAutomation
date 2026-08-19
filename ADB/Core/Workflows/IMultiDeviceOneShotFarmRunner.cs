using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.Workflows
{
    public interface IMultiDeviceOneShotFarmRunner
    {
        Task<MultiDeviceOneShotFarmResult> RunAsync(
            IReadOnlyList<string> deviceNames,
            OneShotFarmRequest request,
            IProgress<MultiDeviceOneShotFarmProgress> progress,
            CancellationToken cancellationToken);
    }

    public enum MultiDeviceOneShotFarmStage
    {
        Queued,
        PreflightQueued,
        Preflight,
        PreflightFailed,
        ReadyForGameplay,
        DispatchingTeam,
        Requeued,
        WaitingForReadyTeam,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public sealed class MultiDeviceOneShotFarmProgress
    {
        public string DeviceName { get; set; }
        public MultiDeviceOneShotFarmStage Stage { get; set; }
        public OneShotFarmProgress DeviceProgress { get; set; }
        public string Message { get; set; }
        public int ConcurrencyLimit { get; set; }
        public int ConcurrencyMaximum { get; set; }
        public int QueuedExecutions { get; set; }
        public int ActiveExecutions { get; set; }
        public int PreflightActive { get; set; }
        public int PreflightQueued { get; set; }
        public int PreflightLimit { get; set; }
        public long PreflightQueueWaitMs { get; set; }
        public long PreflightExecutionMs { get; set; }
        public long PreflightDurationMs { get; set; }
        public long GameplayLeaseWaitMs { get; set; }
        public long GameplayLeaseHeldMs { get; set; }
        public int TeamsDispatchedPerLease { get; set; }
        public int TeamsDispatchedPerDeviceCycle { get; set; }
        public int DeviceRequeueCount { get; set; }
        public int PreflightTimeoutCount { get; set; }
        public int PreflightFailureCount { get; set; }
    }

    public sealed class MultiDeviceOneShotFarmItemResult
    {
        public string DeviceName { get; set; }
        public MultiDeviceOneShotFarmStage Stage { get; set; }
        public OneShotFarmResult Result { get; set; }
        public string ErrorMessage { get; set; }
        public long PreflightDurationMs { get; set; }
        public long GameplayLeaseWaitMs { get; set; }
        public long GameplayLeaseHeldMs { get; set; }
        public int TeamsDispatchedPerLease { get; set; }
        public int TeamsDispatchedPerDeviceCycle { get; set; }
        public int DeviceRequeueCount { get; set; }
        public int PreflightTimeoutCount { get; set; }
        public int PreflightFailureCount { get; set; }
    }

    public sealed class MultiDeviceOneShotFarmResult
    {
        public IReadOnlyList<MultiDeviceOneShotFarmItemResult> Devices { get; set; }
        public int MaximumConcurrency { get; set; }
        public bool WasCancelled { get; set; }
        public bool AdaptiveConcurrencyEnabled { get; set; }
        public int FinalConcurrencyLimit { get; set; }
        public int TotalSelected { get; set; }
        public int Active { get; set; }
        public int Queued { get; set; }
        public int WaitingForTeam { get; set; }
        public int ScheduledForNextCheck { get; set; }
        public int Failed { get; set; }
    }
}
