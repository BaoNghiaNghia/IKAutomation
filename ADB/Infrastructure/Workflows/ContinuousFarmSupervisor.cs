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
        private readonly IDeviceRecoveryService recoveryService;
        private readonly ContinuousFarmSupervisorOptions options;

        public ContinuousFarmSupervisor(IMultiDeviceOneShotFarmRunner runner,
            IDeviceRecoveryService recoveryService, ContinuousFarmSupervisorOptions options)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.recoveryService = recoveryService
                ?? throw new ArgumentNullException(nameof(recoveryService));
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
                .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (devices.Length == 0)
                throw new ArgumentException("At least one LDPlayer device must be selected.",
                    nameof(deviceNames));

            var snapshots = new ConcurrentDictionary<string, ContinuousFarmDeviceSnapshot>(
                StringComparer.OrdinalIgnoreCase);
            await Task.WhenAll(devices.Select(device => RunDeviceLoopAsync(device, request,
                snapshots, progress, cancellationToken)));
            return new ContinuousFarmSupervisorResult
            {
                Devices = devices.Select(device => Copy(snapshots[device])).ToArray(),
                WasCancelled = cancellationToken.IsCancellationRequested
            };
        }

        private async Task RunDeviceLoopAsync(string deviceName, OneShotFarmRequest request,
            ConcurrentDictionary<string, ContinuousFarmDeviceSnapshot> snapshots,
            IProgress<ContinuousFarmSupervisorProgress> progress,
            CancellationToken cancellationToken)
        {
            ContinuousFarmDeviceSnapshot snapshot = NewSnapshot(deviceName);
            var technicalFailures = new Queue<DateTimeOffset>();
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
                    MarkProgress(snapshot, "Starting cycle");
                    Publish(snapshot, progress, null);

                    AttemptResult attempt = await RunAttemptWithWatchdogAsync(deviceName,
                        request, snapshot, progress, cancellationToken);
                    if (attempt.RunnerDidNotStop)
                    {
                        Quarantine(snapshot, progress,
                            "Watchdog cancelled the attempt, but it did not stop within the grace period.",
                            "The previous workflow may still own native LDPlayer work; recovery input was suppressed.");
                        return;
                    }

                    if (attempt.Success)
                    {
                        snapshot.ConsecutiveFailures = 0;
                        snapshot.LastSuccessAt = DateTimeOffset.UtcNow;
                        DateTimeOffset next = DateTimeOffset.UtcNow.AddMilliseconds(options.CycleIntervalMs);
                        Transition(snapshot, ContinuousFarmDeviceState.Waiting,
                            "Cycle completed; waiting for the next supervised cycle.", null, next);
                        Publish(snapshot, progress, null);
                        await Task.Delay(options.CycleIntervalMs, cancellationToken);
                        continue;
                    }

                    snapshot.ConsecutiveFailures++;
                    snapshot.LastFailureAt = DateTimeOffset.UtcNow;
                    if (!attempt.NeedsRecovery)
                    {
                        int ordinaryDelay = GetRetryDelayMs(deviceName,
                            snapshot.ConsecutiveFailures, false);
                        snapshot.LastBackoffDelayMs = ordinaryDelay;
                        DateTimeOffset ordinaryRetryAt = DateTimeOffset.UtcNow
                            .AddMilliseconds(ordinaryDelay);
                        Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                            "Cycle failed; this device will retry independently.",
                            attempt.Error, ordinaryRetryAt);
                        Publish(snapshot, progress, null);
                        await Task.Delay(ordinaryDelay, cancellationToken);
                        continue;
                    }
                    RecordTechnicalFailure(technicalFailures, snapshot);
                    snapshot.RecoveryAttemptCount++;
                    Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                        attempt.WatchdogTimedOut
                            ? "Watchdog detected no progress; starting recovery ladder."
                            : "Cycle failed; starting recovery ladder.", attempt.Error, null);
                    Publish(snapshot, progress, null);

                    DeviceRecoveryResult recovery;
                    try
                    {
                        recovery = await recoveryService.RecoverAsync(deviceName, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        Quarantine(snapshot, progress, "Recovery ladder threw an exception.",
                            exception.Message);
                        return;
                    }

                    snapshot.LastRecoveryStep = recovery.LastStep;
                    if (!recovery.Success)
                    {
                        await WaitInQuarantineAsync(deviceName, snapshot,
                            technicalFailures, progress, recovery.Message,
                            recovery.ErrorMessage ?? attempt.Error, cancellationToken);
                        continue;
                    }

                    if (snapshot.TechnicalFailuresInWindow
                        >= options.CircuitFailureThreshold)
                    {
                        await WaitInQuarantineAsync(deviceName, snapshot,
                            technicalFailures, progress,
                            "Circuit breaker opened after repeated technical failures.",
                            attempt.Error, cancellationToken);
                        continue;
                    }

                    int retryDelay = GetRetryDelayMs(deviceName,
                        snapshot.ConsecutiveFailures, true);
                    snapshot.LastBackoffDelayMs = retryDelay;
                    DateTimeOffset retryAt = DateTimeOffset.UtcNow
                        .AddMilliseconds(retryDelay);
                    Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                        recovery.Message + " A fresh preflight will run before retry.", null, retryAt);
                    Publish(snapshot, progress, null);
                    await Task.Delay(retryDelay, cancellationToken);
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

        private async Task<AttemptResult> RunAttemptWithWatchdogAsync(string deviceName,
            OneShotFarmRequest request, ContinuousFarmDeviceSnapshot snapshot,
            IProgress<ContinuousFarmSupervisorProgress> progress,
            CancellationToken cancellationToken)
        {
            using (var attemptCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken))
            {
                CancellationToken attemptToken = attemptCancellation.Token;
                var farmProgress = new InlineProgress<MultiDeviceOneShotFarmProgress>(value =>
                {
                    if (attemptToken.IsCancellationRequested) return;
                    ApplyFarmProgress(snapshot, value);
                    MarkProgress(snapshot, value?.Message ?? "Farm progress");
                    Publish(snapshot, progress, value);
                });
                Task<MultiDeviceOneShotFarmResult> runTask;
                try
                {
                    runTask = runner.RunAsync(new[] { deviceName }, request, farmProgress,
                        attemptCancellation.Token);
                }
                catch (Exception exception)
                {
                    return AttemptResult.TechnicalFailure(exception.Message);
                }

                while (!runTask.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TimeSpan idle = DateTimeOffset.UtcNow - snapshot.LastProgressAt;
                    int timeoutMs = snapshot.State == ContinuousFarmDeviceState.Waiting
                        ? options.WaitingNoProgressTimeoutMs : options.NoProgressTimeoutMs;
                    if (idle.TotalMilliseconds >= timeoutMs)
                    {
                        snapshot.WatchdogTimeoutCount++;
                        attemptCancellation.Cancel();
                        Task grace = Task.Delay(options.CancellationGraceMs, cancellationToken);
                        Task stopped = await Task.WhenAny(runTask, grace);
                        if (stopped != runTask)
                        {
                            _ = runTask.ContinueWith(task => { var ignored = task.Exception; },
                                TaskContinuationOptions.OnlyOnFaulted);
                            return AttemptResult.UnstoppedTimeout();
                        }
                        try { await runTask; }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                        catch { }
                        return AttemptResult.Timeout();
                    }
                    await Task.WhenAny(runTask, Task.Delay(options.WatchdogPollIntervalMs,
                        cancellationToken));
                }

                MultiDeviceOneShotFarmResult result;
                try { result = await runTask; }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) { return AttemptResult.TechnicalFailure(exception.Message); }
                MultiDeviceOneShotFarmItemResult item = result?.Devices?.FirstOrDefault(value =>
                    string.Equals(value.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
                bool succeeded = item != null && item.Stage == MultiDeviceOneShotFarmStage.Completed
                    && item.Result != null && item.Result.Success;
                string error = item?.ErrorMessage ?? item?.Result?.ErrorMessage
                    ?? item?.Result?.Message ?? "Supervised cycle returned no result.";
                return succeeded ? AttemptResult.Completed() : AttemptResult.Failed(error);
            }
        }

        private static void Quarantine(ContinuousFarmDeviceSnapshot snapshot,
            IProgress<ContinuousFarmSupervisorProgress> progress, string message, string error)
        {
            snapshot.LastFailureAt = DateTimeOffset.UtcNow;
            Transition(snapshot, ContinuousFarmDeviceState.Quarantined, message, error, null);
            Publish(snapshot, progress, null);
        }

        private async Task WaitInQuarantineAsync(string deviceName,
            ContinuousFarmDeviceSnapshot snapshot, Queue<DateTimeOffset> technicalFailures,
            IProgress<ContinuousFarmSupervisorProgress> progress, string message,
            string error, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                snapshot.QuarantineCount++;
                DateTimeOffset retryAt = DateTimeOffset.UtcNow
                    .AddMilliseconds(options.QuarantineCooldownMs);
                snapshot.CircuitOpenUntil = retryAt;
                Transition(snapshot, ContinuousFarmDeviceState.Quarantined,
                    message + " Automatic recovery will retry after cooldown.", error, retryAt);
                Publish(snapshot, progress, null);
                await Task.Delay(options.QuarantineCooldownMs, cancellationToken);

                snapshot.RecoveryAttemptCount++;
                Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                    "Quarantine cooldown elapsed; probing device recovery.", null, null);
                Publish(snapshot, progress, null);
                DeviceRecoveryResult recovery;
                try
                {
                    recovery = await recoveryService.RecoverAsync(deviceName, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    message = "Quarantine recovery probe threw an exception.";
                    error = exception.Message;
                    continue;
                }

                snapshot.LastRecoveryStep = recovery.LastStep;
                if (!recovery.Success)
                {
                    message = recovery.Message ?? "Quarantine recovery probe failed.";
                    error = recovery.ErrorMessage ?? error;
                    continue;
                }

                technicalFailures.Clear();
                snapshot.TechnicalFailuresInWindow = 0;
                snapshot.ConsecutiveFailures = 0;
                snapshot.CircuitOpenUntil = null;
                snapshot.LastBackoffDelayMs = 0;
                Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                    "Circuit breaker closed after a successful recovery probe; fresh preflight required.",
                    null, DateTimeOffset.UtcNow);
                Publish(snapshot, progress, null);
                return;
            }
        }

        private void RecordTechnicalFailure(Queue<DateTimeOffset> failures,
            ContinuousFarmDeviceSnapshot snapshot)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset cutoff = now.AddMilliseconds(-options.CircuitWindowMs);
            while (failures.Count > 0 && failures.Peek() < cutoff)
                failures.Dequeue();
            failures.Enqueue(now);
            snapshot.TechnicalFailuresInWindow = failures.Count;
        }

        private int GetRetryDelayMs(string deviceName, int consecutiveFailures,
            bool technicalFailure)
        {
            int baseDelay;
            if (technicalFailure)
            {
                int index = Math.Min(Math.Max(consecutiveFailures - 1, 0),
                    options.TechnicalRetryDelaysMs.Count - 1);
                baseDelay = options.TechnicalRetryDelaysMs[index];
            }
            else
            {
                baseDelay = options.FailureRetryDelayMs;
            }
            if (!technicalFailure) return baseDelay;
            if (options.RetryJitterMaxMs == 0) return baseDelay;
            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(deviceName ?? string.Empty);
            long positive = hash & 0x7fffffffL;
            int jitter = (int)(positive % ((long)options.RetryJitterMaxMs + 1));
            return (int)Math.Min(int.MaxValue, (long)baseDelay + jitter);
        }

        private static void ApplyFarmProgress(ContinuousFarmDeviceSnapshot snapshot,
            MultiDeviceOneShotFarmProgress progress)
        {
            if (progress == null) return;
            ContinuousFarmDeviceState state;
            switch (progress.Stage)
            {
                case MultiDeviceOneShotFarmStage.Preflight: state = ContinuousFarmDeviceState.Preflight; break;
                case MultiDeviceOneShotFarmStage.Queued: state = ContinuousFarmDeviceState.Ready; break;
                case MultiDeviceOneShotFarmStage.WaitingForReadyTeam: state = ContinuousFarmDeviceState.Waiting; break;
                case MultiDeviceOneShotFarmStage.Completed: state = ContinuousFarmDeviceState.Waiting; break;
                case MultiDeviceOneShotFarmStage.Failed: state = ContinuousFarmDeviceState.Recovering; break;
                case MultiDeviceOneShotFarmStage.Cancelled: state = ContinuousFarmDeviceState.Stopped; break;
                default: state = MapDeviceProgress(progress.DeviceProgress); break;
            }
            Transition(snapshot, state, progress.Message, null, null);
        }

        private static ContinuousFarmDeviceState MapDeviceProgress(OneShotFarmProgress progress)
        {
            if (progress == null) return ContinuousFarmDeviceState.Running;
            switch (progress.Stage)
            {
                case OneShotFarmProgressStage.CheckingTeamAvailability:
                case OneShotFarmProgressStage.WaitingForReadyTeam: return ContinuousFarmDeviceState.Waiting;
                case OneShotFarmProgressStage.ReadyTeamFound:
                case OneShotFarmProgressStage.PreparingFarm: return ContinuousFarmDeviceState.Ready;
                case OneShotFarmProgressStage.Failed: return ContinuousFarmDeviceState.Recovering;
                case OneShotFarmProgressStage.Cancelled:
                case OneShotFarmProgressStage.Stopping: return ContinuousFarmDeviceState.Stopped;
                default: return ContinuousFarmDeviceState.Running;
            }
        }

        private static ContinuousFarmDeviceSnapshot NewSnapshot(string deviceName)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return new ContinuousFarmDeviceSnapshot { DeviceName = deviceName,
                State = ContinuousFarmDeviceState.Preflight, LastTransitionAt = now,
                LastProgressAt = now, CurrentOperation = "Starting",
                Message = "Continuous supervisor started." };
        }

        private static void MarkProgress(ContinuousFarmDeviceSnapshot snapshot, string operation)
        {
            snapshot.LastProgressAt = DateTimeOffset.UtcNow;
            snapshot.CurrentOperation = string.IsNullOrWhiteSpace(operation)
                ? "Farm progress" : operation;
        }

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
            try { progress.Report(new ContinuousFarmSupervisorProgress
                { Device = Copy(snapshot), FarmProgress = farmProgress }); }
            catch { }
        }

        private static ContinuousFarmDeviceSnapshot Copy(ContinuousFarmDeviceSnapshot source) =>
            new ContinuousFarmDeviceSnapshot { DeviceName = source.DeviceName, State = source.State,
                CycleCount = source.CycleCount, ConsecutiveFailures = source.ConsecutiveFailures,
                LastTransitionAt = source.LastTransitionAt, LastSuccessAt = source.LastSuccessAt,
                LastFailureAt = source.LastFailureAt, NextAttemptAt = source.NextAttemptAt,
                LastProgressAt = source.LastProgressAt, CurrentOperation = source.CurrentOperation,
                WatchdogTimeoutCount = source.WatchdogTimeoutCount,
                RecoveryAttemptCount = source.RecoveryAttemptCount,
                LastRecoveryStep = source.LastRecoveryStep, Message = source.Message,
                TechnicalFailuresInWindow = source.TechnicalFailuresInWindow,
                QuarantineCount = source.QuarantineCount,
                CircuitOpenUntil = source.CircuitOpenUntil,
                LastBackoffDelayMs = source.LastBackoffDelayMs,
                LastError = source.LastError };

        private sealed class AttemptResult
        {
            public bool Success { get; private set; }
            public bool WatchdogTimedOut { get; private set; }
            public bool RunnerDidNotStop { get; private set; }
            public bool NeedsRecovery { get; private set; }
            public string Error { get; private set; }
            public static AttemptResult Completed() => new AttemptResult { Success = true };
            public static AttemptResult Failed(string error) => new AttemptResult { Error = error };
            public static AttemptResult TechnicalFailure(string error) => new AttemptResult
                { Error = error, NeedsRecovery = true };
            public static AttemptResult Timeout() => new AttemptResult { WatchdogTimedOut = true,
                NeedsRecovery = true,
                Error = "No verified workflow progress was observed before the watchdog timeout." };
            public static AttemptResult UnstoppedTimeout() => new AttemptResult
                { WatchdogTimedOut = true, RunnerDidNotStop = true, NeedsRecovery = true,
                    Error = "The timed-out workflow did not stop after cancellation." };
        }

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> report;
            public InlineProgress(Action<T> report) { this.report = report
                ?? throw new ArgumentNullException(nameof(report)); }
            public void Report(T value) => report(value);
        }
    }
}
