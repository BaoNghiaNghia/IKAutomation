using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class LdPlayerDeviceRecoveryOptions
    {
        public LdPlayerDeviceRecoveryOptions(string packageName,
            int relaunchWaitMs = 15000, int restartWaitMs = 30000)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("Android package name is required.", nameof(packageName));
            if (relaunchWaitMs < 1) throw new ArgumentOutOfRangeException(nameof(relaunchWaitMs));
            if (restartWaitMs < 1) throw new ArgumentOutOfRangeException(nameof(restartWaitMs));
            PackageName = packageName.Trim();
            RelaunchWaitMs = relaunchWaitMs;
            RestartWaitMs = restartWaitMs;
        }

        public string PackageName { get; }
        public int RelaunchWaitMs { get; }
        public int RestartWaitMs { get; }
    }

    public sealed class LdPlayerDeviceRecoveryService : IDeviceRecoveryService
    {
        private readonly ILdPlayerClient client;
        private readonly LdPlayerDeviceRecoveryOptions options;

        public LdPlayerDeviceRecoveryService(ILdPlayerClient client,
            LdPlayerDeviceRecoveryOptions options)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<DeviceRecoveryResult> RecoverAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steps = new List<DeviceRecoveryStepResult>();

            if (await TryScreenshotAsync(deviceName, DeviceRecoveryStep.ScreenshotRetry,
                steps, cancellationToken))
                return Success(DeviceRecoveryStep.ScreenshotRetry, steps,
                    "Screenshot capture recovered.");

            bool running = await TryIsRunningAsync(deviceName, steps, cancellationToken);
            if (running && await TryRelaunchAsync(deviceName, steps, cancellationToken))
            {
                if (await TryScreenshotAsync(deviceName, DeviceRecoveryStep.Preflight,
                    steps, cancellationToken))
                    return Success(DeviceRecoveryStep.Preflight, steps,
                        "Game relaunch and screenshot preflight succeeded.");
            }

            if (await TryRestartAsync(deviceName, steps, cancellationToken)
                && await TryScreenshotAsync(deviceName, DeviceRecoveryStep.Preflight,
                    steps, cancellationToken))
                return Success(DeviceRecoveryStep.Preflight, steps,
                    "LDPlayer restart and screenshot preflight succeeded.");

            DeviceRecoveryStep last = steps.Count == 0
                ? DeviceRecoveryStep.ScreenshotRetry : steps[steps.Count - 1].Step;
            string error = steps.Count == 0 ? "Recovery produced no result."
                : steps[steps.Count - 1].ErrorMessage ?? steps[steps.Count - 1].Message;
            return new DeviceRecoveryResult
            {
                Success = false,
                LastStep = last,
                Steps = steps,
                Message = "Device recovery ladder was exhausted.",
                ErrorMessage = error
            };
        }

        private async Task<bool> TryScreenshotAsync(string deviceName,
            DeviceRecoveryStep step, List<DeviceRecoveryStepResult> steps,
            CancellationToken cancellationToken)
        {
            try
            {
                byte[] png = await client.CaptureScreenshotPngAsync(deviceName,
                    cancellationToken);
                bool success = png != null && png.Length > 0;
                steps.Add(new DeviceRecoveryStepResult { Step = step, Success = success,
                    Message = success ? "Screenshot captured." : "Screenshot was empty." });
                return success;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                steps.Add(Failure(step, exception));
                return false;
            }
        }

        private async Task<bool> TryIsRunningAsync(string deviceName,
            List<DeviceRecoveryStepResult> steps, CancellationToken cancellationToken)
        {
            try
            {
                bool running = await client.IsRunningAsync(deviceName, cancellationToken);
                steps.Add(new DeviceRecoveryStepResult { Step = DeviceRecoveryStep.ValidateDevice,
                    Success = running, Message = running ? "LDPlayer is running."
                        : "LDPlayer is not running." });
                return running;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                steps.Add(Failure(DeviceRecoveryStep.ValidateDevice, exception));
                return false;
            }
        }

        private async Task<bool> TryRelaunchAsync(string deviceName,
            List<DeviceRecoveryStepResult> steps, CancellationToken cancellationToken)
        {
            try
            {
                await client.RunAppAsync(deviceName, options.PackageName, cancellationToken);
                await Task.Delay(options.RelaunchWaitMs, cancellationToken);
                steps.Add(new DeviceRecoveryStepResult { Step = DeviceRecoveryStep.RelaunchGame,
                    Success = true, Message = "Game relaunch command completed." });
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                steps.Add(Failure(DeviceRecoveryStep.RelaunchGame, exception));
                return false;
            }
        }

        private async Task<bool> TryRestartAsync(string deviceName,
            List<DeviceRecoveryStepResult> steps, CancellationToken cancellationToken)
        {
            try
            {
                await client.CloseAsync(deviceName, cancellationToken);
                await Task.Delay(options.RestartWaitMs, cancellationToken);
                await client.OpenAsync(deviceName, cancellationToken);
                await Task.Delay(options.RestartWaitMs, cancellationToken);
                await client.RunAppAsync(deviceName, options.PackageName, cancellationToken);
                await Task.Delay(options.RelaunchWaitMs, cancellationToken);
                steps.Add(new DeviceRecoveryStepResult { Step = DeviceRecoveryStep.RestartInstance,
                    Success = true, Message = "LDPlayer instance restart completed." });
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                steps.Add(Failure(DeviceRecoveryStep.RestartInstance, exception));
                return false;
            }
        }

        private static DeviceRecoveryStepResult Failure(DeviceRecoveryStep step,
            Exception exception) => new DeviceRecoveryStepResult
            {
                Step = step, Success = false, Message = "Recovery step failed.",
                ErrorMessage = exception.Message
            };

        private static DeviceRecoveryResult Success(DeviceRecoveryStep step,
            IReadOnlyList<DeviceRecoveryStepResult> steps, string message) =>
            new DeviceRecoveryResult { Success = true, LastStep = step,
                Steps = steps, Message = message };
    }
}
