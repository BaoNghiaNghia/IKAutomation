using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IDeviceRecoveryService
    {
        Task<DeviceRecoveryResult> RecoverAsync(string deviceName,
            CancellationToken cancellationToken);
    }

    public enum DeviceRecoveryStep
    {
        ScreenshotRetry,
        ValidateDevice,
        RelaunchGame,
        RestartInstance,
        Preflight
    }

    public sealed class DeviceRecoveryStepResult
    {
        public DeviceRecoveryStep Step { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class DeviceRecoveryResult
    {
        public bool Success { get; set; }
        public DeviceRecoveryStep LastStep { get; set; }
        public IReadOnlyList<DeviceRecoveryStepResult> Steps { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }
}
