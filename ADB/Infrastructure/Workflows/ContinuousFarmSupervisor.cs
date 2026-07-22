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
        private readonly IOperationalMaintenanceService maintenanceService;
        private readonly IContinuousFarmCheckpointStore checkpointStore;
        private readonly IAdaptiveConcurrencyGate adaptiveConcurrencyGate;
        private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> failureHistory =
            new ConcurrentDictionary<string, Queue<DateTimeOffset>>(
                StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CheckpointWriteState> checkpointWriteStates =
            new ConcurrentDictionary<string, CheckpointWriteState>(
                StringComparer.OrdinalIgnoreCase);

        public ContinuousFarmSupervisor(IMultiDeviceOneShotFarmRunner runner,
            IDeviceRecoveryService recoveryService, ContinuousFarmSupervisorOptions options,
            IOperationalMaintenanceService maintenanceService = null,
            IContinuousFarmCheckpointStore checkpointStore = null,
            IAdaptiveConcurrencyGate adaptiveConcurrencyGate = null)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            this.recoveryService = recoveryService
                ?? throw new ArgumentNullException(nameof(recoveryService));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.maintenanceService = maintenanceService;
            this.checkpointStore = checkpointStore;
            this.adaptiveConcurrencyGate = adaptiveConcurrencyGate;
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
            ContinuousFarmCheckpoint checkpoint = LoadCheckpointSafely(deviceName,
                cancellationToken);
            ContinuousFarmDeviceSnapshot snapshot = RestoreSnapshot(deviceName, checkpoint);
            var technicalFailures = RestoreTechnicalFailures(checkpoint);
            snapshot.TechnicalFailuresInWindow = technicalFailures.Count;
            failureHistory[deviceName] = technicalFailures;
            snapshots[deviceName] = snapshot;
            Publish(snapshot, progress, null, cancellationToken);
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await RunMaintenanceSafelyAsync(snapshot, progress, cancellationToken);
                    snapshot.CycleCount++;
                    Transition(snapshot, ContinuousFarmDeviceState.Preflight,
                        $"Starting supervised cycle {snapshot.CycleCount}.", null, null);
                    MarkProgress(snapshot, "Starting cycle");
                    Publish(snapshot, progress, null, cancellationToken);

                    AttemptResult attempt = await RunAttemptWithWatchdogAsync(deviceName,
                        request, snapshot, progress, cancellationToken);
                    if (attempt.RunnerDidNotStop)
                    {
                        Quarantine(snapshot, progress,
                            "Watchdog cancelled the attempt, but it did not stop within the grace period.",
                            "The previous workflow may still own native LDPlayer work; recovery input was suppressed.",
                            cancellationToken);
                        return;
                    }

                    if (attempt.Success)
                    {
                        snapshot.ConsecutiveFailures = 0;
                        snapshot.LastSuccessAt = DateTimeOffset.UtcNow;
                        DateTimeOffset next = DateTimeOffset.UtcNow.AddMilliseconds(options.CycleIntervalMs);
                        Transition(snapshot, ContinuousFarmDeviceState.Waiting,
                            "Cycle completed; waiting for the next supervised cycle.", null, next);
                        Publish(snapshot, progress, null, cancellationToken);
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
                        Publish(snapshot, progress, null, cancellationToken);
                        await Task.Delay(ordinaryDelay, cancellationToken);
                        continue;
                    }
                    RecordTechnicalFailure(technicalFailures, snapshot);
                    snapshot.RecoveryAttemptCount++;
                    Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                        attempt.WatchdogTimedOut
                            ? "Watchdog detected no progress; starting recovery ladder."
                            : "Cycle failed; starting recovery ladder.", attempt.Error, null);
                    Publish(snapshot, progress, null, cancellationToken);

                    DeviceRecoveryResult recovery;
                    try
                    {
                        recovery = await RecoverAsync(deviceName, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        Quarantine(snapshot, progress, "Recovery ladder threw an exception.",
                            exception.Message, cancellationToken);
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
                    Publish(snapshot, progress, null, cancellationToken);
                    await Task.Delay(retryDelay, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Transition(snapshot, ContinuousFarmDeviceState.Stopped,
                    "Continuous supervisor stopped.", snapshot.LastError, null);
                Publish(snapshot, progress, null, cancellationToken);
            }
            catch (Exception exception)
            {
                snapshot.ConsecutiveFailures++;
                snapshot.LastFailureAt = DateTimeOffset.UtcNow;
                Transition(snapshot, ContinuousFarmDeviceState.Stopped,
                    "Continuous supervisor stopped after an unexpected error.",
                    exception.Message, null);
                Publish(snapshot, progress, null, cancellationToken);
            }
            finally
            {
                failureHistory.TryRemove(deviceName, out Queue<DateTimeOffset> ignoredHistory);
                checkpointWriteStates.TryRemove(deviceName,
                    out CheckpointWriteState ignoredCheckpointState);
            }
        }

        private async Task RunMaintenanceSafelyAsync(ContinuousFarmDeviceSnapshot snapshot,
            IProgress<ContinuousFarmSupervisorProgress> progress,
            CancellationToken cancellationToken)
        {
            if (maintenanceService == null) return;
            try
            {
                OperationalMaintenanceResult result = await maintenanceService
                    .RunIfDueAsync(cancellationToken);
                snapshot.FreeDiskBytes = result.FreeDiskBytes;
                snapshot.DiagnosticWritesSuspended = result.DiagnosticWritesSuspended;
                if (result.WasRun)
                {
                    snapshot.LastMaintenanceAt = DateTimeOffset.UtcNow;
                    Publish(snapshot, progress, null, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                snapshot.LastError = "Operational maintenance failed: " + exception.Message;
                Publish(snapshot, progress, null, cancellationToken);
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
                    Publish(snapshot, progress, value, attemptToken);
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
                ApplyResultObservation(snapshot, item?.Result);
                bool succeeded = item != null && item.Stage == MultiDeviceOneShotFarmStage.Completed
                    && item.Result != null && item.Result.Success;
                string error = item?.ErrorMessage ?? item?.Result?.ErrorMessage
                    ?? item?.Result?.Message ?? "Supervised cycle returned no result.";
                return succeeded ? AttemptResult.Completed() : AttemptResult.Failed(error);
            }
        }

        private void Quarantine(ContinuousFarmDeviceSnapshot snapshot,
            IProgress<ContinuousFarmSupervisorProgress> progress, string message, string error,
            CancellationToken cancellationToken)
        {
            snapshot.LastFailureAt = DateTimeOffset.UtcNow;
            Transition(snapshot, ContinuousFarmDeviceState.Quarantined, message, error, null);
            Publish(snapshot, progress, null, cancellationToken);
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
                Publish(snapshot, progress, null, cancellationToken);
                await Task.Delay(options.QuarantineCooldownMs, cancellationToken);

                snapshot.RecoveryAttemptCount++;
                Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                    "Quarantine cooldown elapsed; probing device recovery.", null, null);
                Publish(snapshot, progress, null, cancellationToken);
                DeviceRecoveryResult recovery;
                try
                {
                    recovery = await RecoverAsync(deviceName, cancellationToken);
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

                lock (technicalFailures) technicalFailures.Clear();
                snapshot.TechnicalFailuresInWindow = 0;
                snapshot.ConsecutiveFailures = 0;
                snapshot.CircuitOpenUntil = null;
                snapshot.LastBackoffDelayMs = 0;
                Transition(snapshot, ContinuousFarmDeviceState.Recovering,
                    "Circuit breaker closed after a successful recovery probe; fresh preflight required.",
                    null, DateTimeOffset.UtcNow);
                Publish(snapshot, progress, null, cancellationToken);
                return;
            }
        }

        private void RecordTechnicalFailure(Queue<DateTimeOffset> failures,
            ContinuousFarmDeviceSnapshot snapshot)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset cutoff = now.AddMilliseconds(-options.CircuitWindowMs);
            lock (failures)
            {
                while (failures.Count > 0 && failures.Peek() < cutoff)
                    failures.Dequeue();
                failures.Enqueue(now);
                snapshot.TechnicalFailuresInWindow = failures.Count;
            }
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
            snapshot.ConcurrencyLimit = progress.ConcurrencyLimit;
            snapshot.ActiveExecutions = progress.ActiveExecutions;
            if (progress.DeviceProgress?.CurrentResource != null)
                snapshot.CurrentResource = progress.DeviceProgress.CurrentResource.Value.ToString();
            if (progress.DeviceProgress?.CurrentLevel != null)
                snapshot.CurrentLevel = progress.DeviceProgress.CurrentLevel;
            if (progress.DeviceProgress?.CurrentTeam != null)
                snapshot.CurrentTeam = progress.DeviceProgress.CurrentTeam.Value.ToString();
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

        private ContinuousFarmCheckpoint LoadCheckpointSafely(string deviceName,
            CancellationToken cancellationToken)
        {
            if (checkpointStore == null) return null;
            try { return checkpointStore.Load(deviceName, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { return null; }
        }

        private ContinuousFarmDeviceSnapshot RestoreSnapshot(string deviceName,
            ContinuousFarmCheckpoint checkpoint)
        {
            if (checkpoint?.Device == null) return NewSnapshot(deviceName);
            ContinuousFarmDeviceSnapshot snapshot = Copy(checkpoint.Device);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            snapshot.DeviceName = deviceName;
            snapshot.State = ContinuousFarmDeviceState.Preflight;
            snapshot.RestoredFromCheckpoint = true;
            snapshot.CheckpointSavedAt = checkpoint.SavedAt;
            snapshot.LastTransitionAt = now;
            snapshot.LastProgressAt = now;
            snapshot.CurrentOperation = "Restart preflight";
            snapshot.Message = "Checkpoint restored; a fresh preflight is required before any input.";
            return snapshot;
        }

        private Queue<DateTimeOffset> RestoreTechnicalFailures(
            ContinuousFarmCheckpoint checkpoint)
        {
            DateTimeOffset cutoff = DateTimeOffset.UtcNow
                .AddMilliseconds(-options.CircuitWindowMs);
            DateTimeOffset[] values = (checkpoint?.TechnicalFailureTimestamps
                ?? new DateTimeOffset[0]).Where(value => value >= cutoff)
                .OrderBy(value => value).ToArray();
            return new Queue<DateTimeOffset>(values);
        }

        private static void ApplyResultObservation(ContinuousFarmDeviceSnapshot snapshot,
            OneShotFarmResult result)
        {
            if (result == null) return;
            if (result.CurrentResource.HasValue)
                snapshot.CurrentResource = result.CurrentResource.Value.ToString();
            else if (result.DispatchedResource.HasValue)
                snapshot.CurrentResource = result.DispatchedResource.Value.ToString();
            if (result.LocatedLevel.HasValue) snapshot.CurrentLevel = result.LocatedLevel;
            if (result.DispatchedTeam.HasValue)
                snapshot.CurrentTeam = result.DispatchedTeam.Value.ToString();
            else if (result.SelectedTeam.HasValue)
                snapshot.CurrentTeam = result.SelectedTeam.Value.ToString();
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

        private void Publish(ContinuousFarmDeviceSnapshot snapshot,
            IProgress<ContinuousFarmSupervisorProgress> progress,
            MultiDeviceOneShotFarmProgress farmProgress,
            CancellationToken cancellationToken)
        {
            SaveCheckpointSafely(snapshot, cancellationToken);
            if (progress == null) return;
            try { progress.Report(new ContinuousFarmSupervisorProgress
                { Device = Copy(snapshot), FarmProgress = farmProgress }); }
            catch { }
        }

        private void SaveCheckpointSafely(ContinuousFarmDeviceSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (checkpointStore == null || cancellationToken.IsCancellationRequested) return;
            CheckpointWriteState writeState = checkpointWriteStates.GetOrAdd(
                snapshot.DeviceName, _ => new CheckpointWriteState());
            lock (writeState)
            {
                if (!writeState.IsDue(snapshot, DateTimeOffset.UtcNow,
                    options.CheckpointIntervalMs)) return;
                try
                {
                    DateTimeOffset savedAt = DateTimeOffset.UtcNow;
                    ContinuousFarmDeviceSnapshot copy = Copy(snapshot);
                    copy.CheckpointSavedAt = savedAt;
                    DateTimeOffset[] failures = new DateTimeOffset[0];
                    if (failureHistory.TryGetValue(snapshot.DeviceName,
                        out Queue<DateTimeOffset> history))
                        lock (history) failures = history.ToArray();
                    checkpointStore.Save(new ContinuousFarmCheckpoint
                    {
                        Version = 1,
                        SavedAt = savedAt,
                        Device = copy,
                        TechnicalFailureTimestamps = failures
                    }, cancellationToken);
                    snapshot.CheckpointSavedAt = savedAt;
                    writeState.Record(snapshot, savedAt);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested) { }
                catch { }
            }
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
                CurrentResource = source.CurrentResource,
                CurrentLevel = source.CurrentLevel,
                CurrentTeam = source.CurrentTeam,
                RestoredFromCheckpoint = source.RestoredFromCheckpoint,
                CheckpointSavedAt = source.CheckpointSavedAt,
                FreeDiskBytes = source.FreeDiskBytes,
                DiagnosticWritesSuspended = source.DiagnosticWritesSuspended,
                LastMaintenanceAt = source.LastMaintenanceAt,
                ConcurrencyLimit = source.ConcurrencyLimit,
                ActiveExecutions = source.ActiveExecutions,
                LastError = source.LastError };

        private async Task<DeviceRecoveryResult> RecoverAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            if (adaptiveConcurrencyGate == null)
                return await recoveryService.RecoverAsync(deviceName, cancellationToken);
            using (IAdaptiveConcurrencyLease lease = await adaptiveConcurrencyGate.AcquireAsync(
                deviceName, AdaptiveOperationKind.Recovery, cancellationToken))
            {
                DateTimeOffset started = DateTimeOffset.UtcNow;
                try
                {
                    DeviceRecoveryResult result = await recoveryService.RecoverAsync(
                        deviceName, cancellationToken);
                    adaptiveConcurrencyGate.Report(new AdaptiveConcurrencyObservation
                    {
                        DeviceName = deviceName,
                        Success = result != null && result.Success,
                        TechnicalFailure = result == null || !result.Success,
                        DurationMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds
                    });
                    return result;
                }
                catch
                {
                    adaptiveConcurrencyGate.Report(new AdaptiveConcurrencyObservation
                    {
                        DeviceName = deviceName,
                        Success = false,
                        TechnicalFailure = true,
                        DurationMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds
                    });
                    throw;
                }
            }
        }

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

        private sealed class CheckpointWriteState
        {
            private DateTimeOffset lastSavedAt;
            private string signature;

            public bool IsDue(ContinuousFarmDeviceSnapshot snapshot, DateTimeOffset now,
                int intervalMs)
            {
                string current = Signature(snapshot);
                return lastSavedAt == default(DateTimeOffset)
                    || !string.Equals(signature, current, StringComparison.Ordinal)
                    || (now - lastSavedAt).TotalMilliseconds >= intervalMs;
            }

            public void Record(ContinuousFarmDeviceSnapshot snapshot, DateTimeOffset savedAt)
            {
                lastSavedAt = savedAt;
                signature = Signature(snapshot);
            }

            private static string Signature(ContinuousFarmDeviceSnapshot value) => string.Join("|",
                value.State, value.CycleCount, value.ConsecutiveFailures,
                value.WatchdogTimeoutCount, value.RecoveryAttemptCount,
                value.TechnicalFailuresInWindow, value.QuarantineCount,
                value.CurrentResource ?? string.Empty,
                value.CurrentLevel?.ToString() ?? string.Empty,
                value.CurrentTeam ?? string.Empty,
                value.NextAttemptAt?.UtcDateTime.Ticks.ToString() ?? string.Empty,
                value.CircuitOpenUntil?.UtcDateTime.Ticks.ToString() ?? string.Empty,
                value.LastSuccessAt?.UtcDateTime.Ticks.ToString() ?? string.Empty,
                value.LastFailureAt?.UtcDateTime.Ticks.ToString() ?? string.Empty,
                value.DiagnosticWritesSuspended);
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
