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

    public interface IAdaptiveConcurrencyLease : IDisposable
    {
    }

    public enum AdaptiveOperationKind
    {
        Automation,
        Recovery
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
