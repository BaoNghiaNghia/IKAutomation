using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class ContinuousFarmSupervisor : IContinuousFarmSupervisor
    {
        private readonly IMultiDeviceOneShotFarmRunner runner;
        private readonly ContinuousFarmSupervisorOptions options;

        public ContinuousFarmSupervisor(IMultiDeviceOneShotFarmRunner runner,
            ContinuousFarmSupervisorOptions options)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<ContinuousFarmSupervisorResult> RunAsync(
            IReadOnlyList<string> deviceNames, OneShotFarmRequest request,
            IProgress<ContinuousFarmSupervisorProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null) throw new ArgumentNullException(nameof(request));
            string[] devices = (deviceNames ?? new string[0])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (devices.Length == 0)
                throw new ArgumentException("At least one LDPlayer device must be selected.",
                    nameof(deviceNames));

            var snapshots = new ConcurrentDictionary<string, ContinuousFarmDeviceSnapshot>(
                StringComparer.OrdinalIgnoreCase);
            Task[] deviceLoops = devices.Select(device => RunDeviceLoopAsync(device,
                request, snapshots, progress, cancellationToken)).ToArray();
            await Task.WhenAll(deviceLoops);
            return new ContinuousFarmSupervisorResult
            {
                Devices = devices.Select(device => Copy(snapshots[device])).ToArray(),
                WasCancelled = cancellationToken.IsCancellationRequested
            };
        }

        private async Task RunDeviceLoopAsync(string deviceName,
            OneShotFarmRequest request,
            ConcurrentDictionary<string, ContinuousFarmDeviceSnapshot> snapshots,
            IProgress<ContinuousFarmSupervisorProgress> progress,
            CancellationToken cancellationToken)
        {
            ContinuousFarmDeviceSnapshot snapshot = NewSnapshot(deviceName);
            snapshots[deviceName] = snapshot;
            Publish(snapshot, progress, null);
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    snapshot.CycleCount++;
                    Transition(snapshot, ContinuousFarmDeviceState.Preflight,
                        $"Starting supervised cycle {snapshot.CycleCount}.", null, null);
                    Publish(snapshot, progress, null);

                    var farmProgress = new InlineProgress<MultiDeviceOneShotFarmProgress>(value =>
                    {
                        ApplyFarmProgress(snapshot, value);
                        Publish(snapshot, progress, value);
                    });
                    MultiDeviceOneShotFarmResult result;
                    try
                    {
                        result = await runner.RunAsync(new[] { deviceName }, request,
                            farmProgress, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        snapshot.ConsecutiveFailures++;
                        snapshot.LastFailureAt = DateTimeOffset.UtcNow;
                        DateTimeOffset retryAt = DateTimeOffset.UtcNow
                            .AddMilliseconds(options.FailureRetryDelayMs);
                        Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                            "Cycle threw an exception; this device will retry independently.",
                            exception.Message, retryAt);
                        Publish(snapshot, progress, null);
                        await Task.Delay(options.FailureRetryDelayMs, cancellationToken);
                        continue;
                    }
                    MultiDeviceOneShotFarmItemResult item = result?.Devices?
                        .FirstOrDefault(value => string.Equals(value.DeviceName, deviceName,
                            StringComparison.OrdinalIgnoreCase));
                    bool succeeded = item != null
                        && item.Stage == MultiDeviceOneShotFarmStage.Completed
                        && item.Result != null && item.Result.Success;
                    if (succeeded)
                    {
                        snapshot.ConsecutiveFailures = 0;
                        snapshot.LastSuccessAt = DateTimeOffset.UtcNow;
                        DateTimeOffset next = DateTimeOffset.UtcNow
                            .AddMilliseconds(options.CycleIntervalMs);
                        Transition(snapshot, ContinuousFarmDeviceState.Waiting,
                            "Cycle completed; waiting for the next supervised cycle.",
                            null, next);
                        Publish(snapshot, progress, null);
                        await Task.Delay(options.CycleIntervalMs, cancellationToken);
                    }
                    else if (item != null
                        && item.Stage == MultiDeviceOneShotFarmStage.Cancelled)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                            "Cycle was cancelled without stopping the supervisor.",
                            item.ErrorMessage, DateTimeOffset.UtcNow
                                .AddMilliseconds(options.FailureRetryDelayMs));
                        Publish(snapshot, progress, null);
                        await Task.Delay(options.FailureRetryDelayMs, cancellationToken);
                    }
                    else
                    {
                        snapshot.ConsecutiveFailures++;
                        snapshot.LastFailureAt = DateTimeOffset.UtcNow;
                        string error = item?.ErrorMessage ?? item?.Result?.ErrorMessage
                            ?? item?.Result?.Message ?? "Supervised cycle returned no result.";
                        DateTimeOffset next = DateTimeOffset.UtcNow
                            .AddMilliseconds(options.FailureRetryDelayMs);
                        Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                            "Cycle failed; this device will retry independently.", error, next);
                        Publish(snapshot, progress, null);
                        await Task.Delay(options.FailureRetryDelayMs, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Transition(snapshot, ContinuousFarmDeviceState.Stopped,
                    "Continuous supervisor stopped.", snapshot.LastError, null);
                Publish(snapshot, progress, null);
            }
            catch (Exception exception)
            {
                snapshot.ConsecutiveFailures++;
                snapshot.LastFailureAt = DateTimeOffset.UtcNow;
                Transition(snapshot, ContinuousFarmDeviceState.Stopped,
                    "Continuous supervisor stopped after an unexpected error.",
                    exception.Message, null);
                Publish(snapshot, progress, null);
            }
        }

        private static void ApplyFarmProgress(ContinuousFarmDeviceSnapshot snapshot,
            MultiDeviceOneShotFarmProgress progress)
        {
            if (progress == null) return;
            ContinuousFarmDeviceState state;
            switch (progress.Stage)
            {
                case MultiDeviceOneShotFarmStage.Preflight:
                    state = ContinuousFarmDeviceState.Preflight;
                    break;
                case MultiDeviceOneShotFarmStage.Queued:
                    state = ContinuousFarmDeviceState.Ready;
                    break;
                case MultiDeviceOneShotFarmStage.WaitingForReadyTeam:
                    state = ContinuousFarmDeviceState.Waiting;
                    break;
                case MultiDeviceOneShotFarmStage.Completed:
                    state = ContinuousFarmDeviceState.Waiting;
                    break;
                case MultiDeviceOneShotFarmStage.Failed:
                    state = ContinuousFarmDeviceState.Recovering;
                    break;
                case MultiDeviceOneShotFarmStage.Cancelled:
                    state = ContinuousFarmDeviceState.Stopped;
                    break;
                default:
                    state = MapDeviceProgress(progress.DeviceProgress);
                    break;
            }
            Transition(snapshot, state, progress.Message, null, null);
        }

        private static ContinuousFarmDeviceState MapDeviceProgress(
            OneShotFarmProgress progress)
        {
            if (progress == null) return ContinuousFarmDeviceState.Running;
            switch (progress.Stage)
            {
                case OneShotFarmProgressStage.CheckingTeamAvailability:
                case OneShotFarmProgressStage.WaitingForReadyTeam:
                    return ContinuousFarmDeviceState.Waiting;
                case OneShotFarmProgressStage.ReadyTeamFound:
                case OneShotFarmProgressStage.PreparingFarm:
                    return ContinuousFarmDeviceState.Ready;
                case OneShotFarmProgressStage.Failed:
                    return ContinuousFarmDeviceState.Recovering;
                case OneShotFarmProgressStage.Cancelled:
                case OneShotFarmProgressStage.Stopping:
                    return ContinuousFarmDeviceState.Stopped;
                default:
                    return ContinuousFarmDeviceState.Running;
            }
        }

        private static ContinuousFarmDeviceSnapshot NewSnapshot(string deviceName) =>
            new ContinuousFarmDeviceSnapshot
            {
                DeviceName = deviceName,
                State = ContinuousFarmDeviceState.Preflight,
                LastTransitionAt = DateTimeOffset.UtcNow,
                Message = "Continuous supervisor started."
            };

        private static void Transition(ContinuousFarmDeviceSnapshot snapshot,
            ContinuousFarmDeviceState state, string message, string error,
            DateTimeOffset? nextAttemptAt)
        {
            snapshot.State = state;
            snapshot.LastTransitionAt = DateTimeOffset.UtcNow;
            snapshot.Message = string.IsNullOrWhiteSpace(message) ? state.ToString() : message;
            if (!string.IsNullOrWhiteSpace(error)) snapshot.LastError = error;
            snapshot.NextAttemptAt = nextAttemptAt;
        }

        private static void Publish(ContinuousFarmDeviceSnapshot snapshot,
            IProgress<ContinuousFarmSupervisorProgress> progress,
            MultiDeviceOneShotFarmProgress farmProgress)
        {
            if (progress == null) return;
            try
            {
                progress.Report(new ContinuousFarmSupervisorProgress
                {
                    Device = Copy(snapshot),
                    FarmProgress = farmProgress
                });
            }
            catch
            {
                // Progress observers must not stop a long-running device supervisor.
            }
        }

        private static ContinuousFarmDeviceSnapshot Copy(
            ContinuousFarmDeviceSnapshot source) => new ContinuousFarmDeviceSnapshot
            {
                DeviceName = source.DeviceName,
                State = source.State,
                CycleCount = source.CycleCount,
                ConsecutiveFailures = source.ConsecutiveFailures,
                LastTransitionAt = source.LastTransitionAt,
                LastSuccessAt = source.LastSuccessAt,
                LastFailureAt = source.LastFailureAt,
                NextAttemptAt = source.NextAttemptAt,
                Message = source.Message,
                LastError = source.LastError
            };

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> report;

            public InlineProgress(Action<T> report)
            {
                this.report = report ?? throw new ArgumentNullException(nameof(report));
            }

            public void Report(T value) => report(value);
        }
    }
}
