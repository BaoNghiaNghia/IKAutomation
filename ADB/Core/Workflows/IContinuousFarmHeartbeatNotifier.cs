using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IContinuousFarmHeartbeatNotifier
    {
        bool IsConfigured { get; }
        Task<ContinuousFarmHeartbeatDeliveryResult> NotifyHeartbeatAsync(
            ContinuousFarmHealthSnapshot snapshot,
            CancellationToken cancellationToken);
    }

    public sealed class ContinuousFarmHeartbeatDeliveryResult
    {
        public bool Attempted { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public sealed class ContinuousFarmHealthSnapshot
    {
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }
        public int TotalDevices { get; set; }
        public int HealthyDevices { get; set; }
        public int PreflightDevices { get; set; }
        public int ReadyDevices { get; set; }
        public int RunningDevices { get; set; }
        public int WaitingDevices { get; set; }
        public int RecoveringDevices { get; set; }
        public int QuarantinedDevices { get; set; }
        public int StoppedDevices { get; set; }
        public int DevicesWithFailures { get; set; }
        public int LowDiskDevices { get; set; }
        public int ActiveExecutions { get; set; }
        public int ConcurrencyLimit { get; set; }
        public DateTimeOffset? LastHeartbeatAttemptAt { get; set; }
        public bool? LastHeartbeatSucceeded { get; set; }
        public string HeartbeatMessage { get; set; }
        public IReadOnlyList<ContinuousFarmDeviceHealth> Devices { get; set; }
    }

    public sealed class ContinuousFarmDeviceHealth
    {
        public string DeviceName { get; set; }
        public string State { get; set; }
        public int ConsecutiveFailures { get; set; }
        public bool DiagnosticWritesSuspended { get; set; }
        public string LastError { get; set; }
    }
}
