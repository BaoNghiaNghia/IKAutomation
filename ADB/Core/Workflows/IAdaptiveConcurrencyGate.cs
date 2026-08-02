using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IAdaptiveConcurrencyGate
    {
        Task<IAdaptiveConcurrencyLease> AcquireAsync(string deviceName,
            AdaptiveOperationKind operationKind, CancellationToken cancellationToken);

        void Report(AdaptiveConcurrencyObservation observation);

        AdaptiveConcurrencySnapshot GetSnapshot();
    }

    public interface IAdaptiveConcurrencyAdmissionGate : IAdaptiveConcurrencyGate
    {
        Task<IAdaptiveConcurrencyLease> AcquireAsync(string deviceName,
            AdaptiveOperationKind operationKind, AdaptiveAdmissionRequest request,
            CancellationToken cancellationToken);
    }

    public interface IAdaptiveConcurrencyLease : IDisposable
    {
    }

    public enum AdaptiveOperationKind
    {
        Automation,
        Recovery
    }

    public enum AdaptiveExecutionPhase
    {
        Preflight,
        Gameplay,
        Recovery
    }

    public sealed class AdaptiveAdmissionRequest
    {
        public bool ApplyStartupStagger { get; set; }
        public string StaggerKey { get; set; }
        public int DeviceIndex { get; set; }
        public AdaptiveExecutionPhase ExecutionPhase { get; set; }
    }

    public sealed class AdaptiveConcurrencyObservation
    {
        public string DeviceName { get; set; }
        public bool Success { get; set; }
        public bool TechnicalFailure { get; set; }
        public long DurationMs { get; set; }
        public bool UseDurationAsPressure { get; set; }
    }

    public sealed class AdaptiveConcurrencySnapshot
    {
        public bool Enabled { get; set; }
        public int CurrentLimit { get; set; }
        public int ActiveExecutions { get; set; }
        public int QueuedExecutions { get; set; }
        public double CpuUsagePercent { get; set; }
        public long AvailableMemoryBytes { get; set; }
        public double TechnicalFailureRate { get; set; }
    }
}
