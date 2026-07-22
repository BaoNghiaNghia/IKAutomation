using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
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
    }

    public sealed class MultiDeviceOneShotFarmItemResult
    {
        public string DeviceName { get; set; }
        public MultiDeviceOneShotFarmStage Stage { get; set; }
        public OneShotFarmResult Result { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class MultiDeviceOneShotFarmResult
    {
        public IReadOnlyList<MultiDeviceOneShotFarmItemResult> Devices { get; set; }
        public int MaximumConcurrency { get; set; }
        public bool WasCancelled { get; set; }
    }
}
