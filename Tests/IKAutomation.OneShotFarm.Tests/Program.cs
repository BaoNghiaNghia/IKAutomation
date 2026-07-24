using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection;
using ADB_Tool_Automation_Post_FB.Infrastructure.Workflows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    static int pass, fail;
    static int Main()
    {
        Run("Invalid request calls no services", Invalid); Run("Unknown initial state is precondition failure", Unknown);
        Run("ResourcePopup initial state is rejected", InitialPopup); Run("TeamSelection initial state is rejected", InitialTeam);
        Run("ContinentMap is handled by EnsureWorldMap", Continent); Run("Ensure failure stops before panel", EnsureFail);
        Run("Panel failure stops before fallback", PanelFail); Run("Configure failure stops before search", ConfigureFail);
        Run("Panel composite evidence is accepted without panel anchor", CompositePanelEvidence);
        Run("Execute disables configure-before-search", ConfigureBeforeFalse); Run("Levels exhausted stops before popup", NotFound);
        Run("Levels exhausted is business outcome", NotFoundBusiness); Run("Search timeout stops before popup", SearchTimeout);
        Run("Levels exhausted records fallback as last completed", NotFoundLastCompleted);
        Run("Levels exhausted skips popup team and dispatch", NotFoundSkipsDownstream);
        Run("ResourceLocated verifies popup", LocatedCallsPopup); Run("Popup not ready stops before team", PopupFail);
        Run("TeamSelection not ready stops before select", OpenTeamNotReady); Run("No eligible team stops before dispatch", NoTeam);
        Run("Selected team is passed to dispatch", SelectedPassed); Run("Team3 is not hard-coded to Team4", Team3Passed);
        Run("Verified MarchStarted succeeds", MarchSuccess); Run("Unverified dispatch is not success", UnverifiedDispatch);
        Run("Verified AlreadyMarching succeeds", AlreadyMarching); Run("Steps preserve order", StepOrder);
        Run("Last completed step is preserved on failure", LastCompleted); Run("No step runs after failure", NoStepAfterFailure);
        Run("Workflow is not retried", NoWorkflowRetry); Run("Each service is called once", EachOnce);
        Run("Cancellation before first step calls no service", CancelBefore); Run("Cancellation between steps stops next", CancelBetween);
        Run("Lease releases on success", LeaseSuccess); Run("Lease releases on failure", LeaseFailure);
        Run("Lease releases on cancellation", LeaseCancel); Run("Same-device workflows serialize", SameDevice);
        Run("Different devices have independent leases", DifferentDevices); Run("Workflow has no LDPlayer client dependency", NoClient);
        Run("Workflow has no Auto_LDPlayer call", NoAuto); Run("Workflow has no direct input call", NoInput);
        Run("Workflow has no default token bypass", NoNone); Run("Screenshot failure preserves outcome", ScreenshotFailure);
        Run("ResourceNotFound falls back through requested levels", NoLevelFallback); Run("Success requires MarchStartedVerified", VerifiedRequired);
        Run("Level 6 located continues workflow", Level6Located);
        Run("Level 6 popup failure remains a popup outcome", Level6PopupFailureMapping);
        Run("Structural fallback result is accepted", StructuralAccepted); Run("Success does not start second cycle", NoSecondCycle);
        Run("Iron storage full switches to Stone", IronFullSwitchesStone);
        Run("Resource expiry switches resource without marking storage full", ResourceExpirySwitchesResource);
        Run("Iron and Stone storage full exhausts plan", BothStoragesFull);
        Run("Default resource priority has four resources", DefaultResourcePriority);
        Run("Four-resource plan advances after exhausted levels", ExhaustedAdvances);
        Run("Exhausted four-resource pass repositions and retries", ExhaustedPassRepositionsAndRetries);
        Run("All four resources exhausted returns plan exhausted", AllResourcesExhausted);
        Run("All four resources storage full returns all full", AllResourcesStorageFull);
        Run("Mixed fallback reaches Wood", MixedFallbackReachesWood);
        Run("Technical search failure does not switch resource", TechnicalSearchDoesNotSwitch);
        Run("Missing resource templates stop before input", MissingProfileStopsInput);
        Run("Popup mismatch stops before team selection", PopupMismatchStopsTeam);
        Run("March started sends only one team", MarchSendsOneTeam);
        Run("Resource plan runs inside one-shot device lease", ResourcePlanUsesWorkflowLease);
        Run("Initial SearchPanel safely recovers into resource plan", InitialSearchPanelUsesPlan);
        Run("Resource selection defaults to all four resources", SelectionDefaults);
        Run("Resource selection rejects zero resources", SelectionRejectsZero);
        Run("Resource selection rejects one resource", SelectionRejectsOne);
        Run("Resource selection accepts two resources", SelectionAcceptsTwo);
        Run("Resource selection maps and shuffles without additions", SelectionMapsAndShuffles);
        Run("Shuffled priority remains fixed for workflow run", ShuffledPriorityIsFixed);
        Run("Storage full advances in shuffled order", ShuffledStorageFallback);
        Run("Levels exhausted advances in shuffled order", ShuffledLevelFallback);
        Run("Selected subset never tries excluded resources", SelectedSubsetOnly);
        Run("Diagnostic source displays selected and shuffled resources", DiagnosticDisplaysSelection);
        Run("Resource selection code has no default cancellation token", SelectionHasNoNone);
        Run("All selected resource templates are validated before shuffle", PreflightBeforeShuffle);
        Run("Unchecked resources are not validated", UncheckedResourcesIgnored);
        Run("Multiple missing templates are reported without input", MissingTemplatesStopBeforeInput);
        Run("Four popup title source files exist", PopupTitleFilesExist);
        Run("Ready team gate starts workflow immediately", ReadyGateStartsImmediately);
        Run("Ready team gate waits and rechecks", ReadyGateWaitsAndRechecks);
        Run("Batch farm dispatches until no allowed team remains", BatchFarmUsesAllReadyTeams);
        Run("Batch farm rotates selected resource priority", BatchFarmRotatesResources);
        Run("Batch farm never reuses a stale ready team", BatchFarmSkipsDispatchedTeam);
        Run("Batch farm survives a transient empty readiness frame", BatchFarmRechecksTransientEmpty);
        Run("Batch farm stops after a failed one-shot", BatchFarmStopsOnFailure);
        Run("Ready gate excludes teams absent from the device roster", ReadyGateExcludesUnavailableTeams);
        Run("Ready team gate ignores ready teams outside AllowedTeams", ReadyGateUsesAllowedTeams);
        Run("Ready team gate stops at configured wait timeout", ReadyGateTimeout);
        Run("Ready team gate cancellation stops before workflow", ReadyGateCancellation);
        Run("Ready team gate technical failure stops workflow", ReadyGateTechnicalFailure);
        Run("Ready team gate has no default token bypass", ReadyGateHasNoNone);
        Run("Ready gate reports checking before availability", ProgressCheckingFirst);
        Run("Ready gate reports waiting with schedule", ProgressWaitingSchedule);
        Run("Ready gate progress filters allowed teams", ProgressAllowedTeams);
        Run("Ready gate reports eligible ready team", ProgressReadyFound);
        Run("Ready gate forwards the same progress", ProgressForwarded);
        Run("One-shot workflow reports running steps", ProgressInnerSteps);
        Run("Ready wait cancellation is prompt and bounded", ProgressCancellationPrompt);
        Run("Null progress remains supported", ProgressNullSupported);
        Run("Countdown never becomes negative", ProgressCountdownNonNegative);
        Run("Stale run progress is rejected", ProgressStaleRunRejected);
        Run("Progress callback failure does not fail workflow", ProgressCallbackSafe);
        Run("Progress code has no default token bypass", ProgressHasNoNone);
        Run("Missing preference file loads defaults", PreferenceMissingUsesDefaults);
        Run("Safe preference defaults select resources", PreferenceSafeDefaults);
        Run("Preference resource flags round-trip", PreferenceResourcesRoundTrip);
        Run("Preference level and team order round-trip", PreferencePrioritiesRoundTrip);
        Run("Preference switches and waits round-trip", PreferenceOptionsRoundTrip);
        Run("Invalid preference file recovers defaults", PreferenceInvalidRecovery);
        Run("Preference save is atomic on commit failure", PreferenceAtomicSave);
        Run("Preference resource validation", PreferenceResourceValidation);
        Run("Preference level validation", PreferenceLevelValidation);
        Run("Preference team validation", PreferenceTeamValidation);
        Run("Preference wait validation", PreferenceWaitValidation);
        Run("User preference overrides application defaults", PreferenceOverridesDefaults);
        Run("Save failure does not prevent in-memory request", PreferenceSaveFailureInMemory);
        Run("Preferences map all requested farm settings", PreferenceRequestMapping);
        Run("Runtime shuffle is not persisted", PreferenceShuffleNotPersisted);
        Run("Per-run ready options do not mutate shared defaults", PreferenceRunOptionsIsolated);
        Run("Preference reset restores defaults", PreferenceResetDefaults);
        Run("Preference code has no default token bypass", PreferenceHasNoNone);
        Run("WorldMap readiness check uses roster ROI", AvailabilityUsesRosterRoi);
        Run("WorldMap readiness check maps ready rows to team numbers", AvailabilityMapsReadyTeams);
        Run("WorldMap readiness accepts ready rows when three-team badges vary", AvailabilityAcceptsThreeTeamBadgeVariation);
        Run("WorldMap readiness check detects two, three, or four teams", AvailabilityDetectsVariableTeamCounts);
        Run("WorldMap readiness infers roster when number badges do not match", AvailabilityInfersRosterFromReadyRows);
        Run("WorldMap readiness check accepts no-ready result", AvailabilityNoReady);
        Run("Missing readiness template sends no device action", AvailabilityMissingTemplate);
        Run("WorldMap readiness service has no default token bypass", AvailabilityHasNoNone);
        Run("One-Shot UI has per-run Stop cancellation", OneShotUiHasStop);
        Run("One-Shot UI hides manual diagnostic controls", OneShotUiIsFocused);
        Run("Multi-device runner caps concurrency at twenty", MultiDeviceConcurrencyIsCapped);
        Run("Adaptive gate enforces its live concurrency limit", AdaptiveGateEnforcesLiveLimit);
        Run("Adaptive gate reduces concurrency under host pressure", AdaptiveGateReducesOnPressure);
        Run("Adaptive gate increases slowly without exceeding maximum", AdaptiveGateIncreasesWithinMaximum);
        Run("Adaptive stagger honors cancellation", AdaptiveStaggerHonorsCancellation);
        Run("Concurrent retry shares the twenty-device limit", ConcurrentBatchesShareConcurrencyLimit);
        Run("Multi-device runner isolates requests per device", MultiDeviceRequestsAreIsolated);
        Run("One device failure does not stop other devices", MultiDeviceFailureIsIsolated);
        Run("All device preflights finish before farming starts", MultiDevicePreflightBarrier);
        Run("Preflight failure does not stop healthy devices", MultiDevicePreflightFailureIsIsolated);
        Run("Ready gate consumes preflight result without duplicate check", MultiDevicePreflightIsReused);
        Run("One-Shot UI retries only failed devices", MultiDeviceUiRetriesFailures);
        Run("Continuous supervisor keeps device states independent", ContinuousSupervisorIsolatesDevices);
        Run("Continuous supervisor cancellation stops waiting devices", ContinuousSupervisorCancellationStopsWaiting);
        Run("Continuous supervisor publishes aggregated health", ContinuousSupervisorPublishesHealth);
        Run("Heartbeat failure does not stop device workflows", HeartbeatFailureIsIsolated);
        Run("Watchdog recovers a cancellable stalled device", ContinuousWatchdogRecoversStalledDevice);
        Run("Watchdog quarantines a runner that ignores cancellation", ContinuousWatchdogQuarantinesUnstoppedRunner);
        Run("Technical retry uses configured backoff", ContinuousSupervisorUsesTechnicalBackoff);
        Run("Circuit breaker quarantines and probes after cooldown", ContinuousCircuitBreakerRecoversAfterCooldown);
        Run("Continuous checkpoint round-trips device state", CheckpointRoundTripsDeviceState);
        Run("Invalid checkpoint is isolated per device", CheckpointInvalidFileIsIsolated);
        Run("Restart restores checkpoint through safe preflight", CheckpointRestoresAsPreflight);
        Run("Supervisor checkpoints observed resource and team", CheckpointCapturesFarmObservation);
        Run("Checkpoint persistence honors cancellation", CheckpointHonorsCancellation);
        Run("Checkpoint code has no default token bypass", CheckpointHasNoNone);
        Run("Recovery ladder restarts only after softer steps fail", RecoveryLadderEscalatesInOrder);
        Run("Operational maintenance removes expired screenshots", MaintenanceRemovesExpiredScreenshots);
        Run("Operational maintenance enforces diagnostic quota", MaintenanceEnforcesDiagnosticQuota);
        Run("Disk pressure gate uses resume hysteresis", MaintenanceDiskGateUsesHysteresis);
        Run("Operational maintenance is interval gated", MaintenanceRunsOnlyWhenDue);
        Run("Logger rotates without clearing startup log", LoggerUsesRotationAndRetention);
        Run("Diagnostic stores honor disk-pressure gate", DiagnosticStoresHonorStorageGate);
        Run("Continuous supervisor has no default token bypass", ContinuousSupervisorHasNoNone);
        Run("Continuous supervisor UI is wired without replacing bounded run", ContinuousSupervisorUiIsWired);
        Console.WriteLine($"One-shot farm tests: {pass} passed, {fail} failed."); return fail == 0 ? 0 : 1;
    }
    static void Run(string n, Action a) { try { a(); pass++; Console.WriteLine("PASS: " + n); } catch (Exception e) { fail++; Console.WriteLine("FAIL: " + n + " - " + e); } }
    static void Is(bool v, string m) { if (!v) throw new Exception(m); } static void Eq<T>(T e,T a,string m){if(!Equals(e,a))throw new Exception($"{m} Expected={e}, Actual={a}");}
    static OneShotFarmResult Go(H h, CancellationToken t=default(CancellationToken))=>h.Workflow.RunAsync("LDPlayer",h.Request,t).GetAwaiter().GetResult();
    static ResourceFarmFallbackResult Plan(H h, FakeProfiles profiles=null, CancellationToken t=default(CancellationToken))
        => new ResourceFarmFallbackService(h.Nav,new FakeFallback(h.Config,h.Search),h.Popup,h.Open,h.Select,h.Dispatch,
            profiles??new FakeProfiles(),new ResourceFarmFallbackOptions(),new Log())
            .RunAsync("LDPlayer",h.Request,GameState.WorldMap,t).GetAwaiter().GetResult();

    static void MultiDeviceConcurrencyIsCapped()
    {
        var probe = new MultiDeviceWorkflowProbe(20);
        var runner = new MultiDeviceOneShotFarmRunner(
            () => new MultiDeviceProbeWorkflow(probe), 20);
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            Task<MultiDeviceOneShotFarmResult> run = runner.RunAsync(
                Enumerable.Range(1, 25).Select(index => "Device" + index).ToArray(),
                new H().Request, null, cancellation.Token);
            Is(probe.RequiredConcurrencyReached.Task.Wait(TimeSpan.FromSeconds(5)),
                "twenty devices did not enter concurrently");
            Eq(20, probe.MaximumActive, "maximum active workflows");
            probe.Release.TrySetResult(true);
            MultiDeviceOneShotFarmResult result = run.GetAwaiter().GetResult();
            Eq(25, result.Devices.Count, "device results");
            Is(result.Devices.All(item => item.Stage == MultiDeviceOneShotFarmStage.Completed),
                "all devices should complete");
        }
    }

    static void ContinuousSupervisorIsolatesDevices()
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var runner = new ContinuousSupervisorRunner(cancellation, true);
            var states = new List<string>();
            var progress = new InlineProgress<ContinuousFarmSupervisorProgress>(value =>
            {
                lock (states)
                    states.Add(value.Device.DeviceName + ":" + value.Device.State);
            });
            var supervisor = new ContinuousFarmSupervisor(runner,
                new FakeDeviceRecovery(true),
                new ContinuousFarmSupervisorOptions(1, 1));
            ContinuousFarmSupervisorResult result = supervisor.RunAsync(
                new[] { "May 1", "May 2" }, new H().Request, progress,
                cancellation.Token).GetAwaiter().GetResult();

            Is(result.WasCancelled, "supervisor cancellation was not reported");
            Is(result.Devices.All(item => item.State == ContinuousFarmDeviceState.Stopped),
                "all device loops should stop on cancellation");
            Is(runner.Calls("May 1") >= 2, "failed device did not retry independently");
            Is(runner.Calls("May 2") >= 2, "healthy device did not continue cycling");
            lock (states)
            {
                Is(states.Contains("May 1:Recovering"), "failed device never entered Recovering");
                Is(states.Contains("May 1:Waiting"), "recovered device never entered Waiting");
                Is(states.Contains("May 2:Waiting"), "healthy device never entered Waiting");
                Is(states.Any(item => item.EndsWith(":Preflight")), "Preflight was not reported");
                Is(states.Any(item => item.EndsWith(":Ready")), "Ready was not reported");
                Is(states.Any(item => item.EndsWith(":Running")), "Running was not reported");
            }
        }
    }

    static void ContinuousSupervisorCancellationStopsWaiting()
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var runner = new ContinuousSupervisorRunner(cancellation, false);
            var supervisor = new ContinuousFarmSupervisor(runner,
                new FakeDeviceRecovery(true),
                new ContinuousFarmSupervisorOptions(60000, 60000));
            var progress = new InlineProgress<ContinuousFarmSupervisorProgress>(value =>
            {
                if (value.Device.State == ContinuousFarmDeviceState.Waiting)
                    cancellation.Cancel();
            });
            DateTime started = DateTime.UtcNow;
            ContinuousFarmSupervisorResult result = supervisor.RunAsync(
                new[] { "May 1" }, new H().Request, progress,
                cancellation.Token).GetAwaiter().GetResult();
            Is(DateTime.UtcNow - started < TimeSpan.FromSeconds(2),
                "cancellation did not interrupt the supervisor delay promptly");
            Eq(ContinuousFarmDeviceState.Stopped, result.Devices[0].State,
                "final device state");
        }
    }

    static void ContinuousSupervisorPublishesHealth()
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var health = new List<ContinuousFarmHealthSnapshot>();
            var notifier = new FakeHeartbeatNotifier();
            var supervisor = new ContinuousFarmSupervisor(
                new CancelAfterSuccessRunner(cancellation),
                new FakeDeviceRecovery(true),
                new ContinuousFarmSupervisorOptions(60000, 60000,
                    heartbeatIntervalMs: 60000), null, null, null, notifier);
            var progress = new InlineProgress<ContinuousFarmSupervisorProgress>(value =>
            {
                if (value.Health != null) lock (health) health.Add(value.Health);
            });
            supervisor.RunAsync(new[] { "May 1", "May 2" }, new H().Request,
                progress, cancellation.Token).GetAwaiter().GetResult();
            Is(notifier.Calls >= 1, "startup heartbeat was not sent");
            lock (health)
            {
                Is(health.Any(value => value.TotalDevices == 2),
                    "dashboard did not aggregate both devices");
                Is(health.Any(value => value.LastHeartbeatSucceeded == true),
                    "heartbeat delivery status was not published");
            }
        }
    }

    static void HeartbeatFailureIsIsolated()
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var notifier = new FakeHeartbeatNotifier { ThrowOnNotify = true };
            var runner = new CancelAfterSuccessRunner(cancellation);
            var supervisor = new ContinuousFarmSupervisor(runner,
                new FakeDeviceRecovery(true),
                new ContinuousFarmSupervisorOptions(60000, 60000,
                    heartbeatIntervalMs: 60000), null, null, null, notifier);
            ContinuousFarmSupervisorResult result = supervisor.RunAsync(
                new[] { "May 1" }, new H().Request, null, cancellation.Token)
                .GetAwaiter().GetResult();
            Is(result.WasCancelled, "workflow did not continue after heartbeat failure");
            Is(notifier.Calls >= 1, "heartbeat notifier was not invoked");
        }
    }

    static void ContinuousSupervisorHasNoNone()
    {
        string code = File.ReadAllText(Path.Combine(Environment.CurrentDirectory,
            "ADB", "Infrastructure", "Workflows", "ContinuousFarmSupervisor.cs"));
        Is(!code.Contains("CancellationToken.None"), "CancellationToken.None found");
        Is(code.Contains("Task.Delay(options.CycleIntervalMs, cancellationToken)"),
            "cycle delay does not use cancellation token");
        Is(code.Contains("Task.Delay(ordinaryDelay, cancellationToken)"),
            "ordinary failure delay does not use cancellation token");
        Is(code.Contains("Task.Delay(retryDelay, cancellationToken)"),
            "technical backoff delay does not use cancellation token");
        Is(code.Contains("Task.Delay(options.QuarantineCooldownMs, cancellationToken)"),
            "quarantine cooldown does not use cancellation token");
    }

    static void ContinuousWatchdogRecoversStalledDevice()
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var runner = new WatchdogRunner(cancellation, false);
            var recovery = new FakeDeviceRecovery(true);
            var supervisor = new ContinuousFarmSupervisor(runner, recovery,
                new ContinuousFarmSupervisorOptions(cycleIntervalMs: 60000,
                    failureRetryDelayMs: 1, noProgressTimeoutMs: 30,
                    watchdogPollIntervalMs: 5, cancellationGraceMs: 100,
                    technicalRetryDelaysMs: new[] { 1 }, retryJitterMaxMs: 0));
            ContinuousFarmSupervisorResult result = supervisor.RunAsync(new[] { "May 1" },
                new H().Request, null, cancellation.Token).GetAwaiter().GetResult();
            Is(result.WasCancelled, "successful retry did not reach test cancellation");
            Eq(1, recovery.Calls, "recovery calls");
            Eq(1, result.Devices[0].WatchdogTimeoutCount, "watchdog count");
            Is(runner.Calls >= 2, "device was not retried after recovery");
        }
    }

    static void ContinuousWatchdogQuarantinesUnstoppedRunner()
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var runner = new WatchdogRunner(cancellation, true);
            var recovery = new FakeDeviceRecovery(true);
            var supervisor = new ContinuousFarmSupervisor(runner, recovery,
                new ContinuousFarmSupervisorOptions(60000, 1, 30, 5, 20));
            ContinuousFarmSupervisorResult result = supervisor.RunAsync(new[] { "May 1" },
                new H().Request, null, cancellation.Token).GetAwaiter().GetResult();
            Eq(ContinuousFarmDeviceState.Quarantined, result.Devices[0].State,
                "unstopped runner state");
            Eq(0, recovery.Calls, "recovery must not overlap an unstopped runner");
        }
    }

    static void RecoveryLadderEscalatesInOrder()
    {
        var client = new RecoveryClient { ScreenshotFailures = 1, Running = true };
        var service = new LdPlayerDeviceRecoveryService(client,
            new LdPlayerDeviceRecoveryOptions("com.gtarcade.ioe.global", 1, 1));
        DeviceRecoveryResult result = service.RecoverAsync("May 1",
            new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token)
            .GetAwaiter().GetResult();
        Is(result.Success, "recovery ladder did not recover");
        Is(client.RunAppCalls >= 1, "game was not relaunched");
        Is(client.CloseCalls == 0, "instance restart should not run after relaunch recovery");
        Eq(DeviceRecoveryStep.Preflight, result.LastStep, "last recovery step");
    }

    static void ContinuousSupervisorUsesTechnicalBackoff()
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var runner = new TechnicalFailureRunner(cancellation, 2);
            var snapshots = new List<ContinuousFarmDeviceSnapshot>();
            var supervisor = new ContinuousFarmSupervisor(runner,
                new FakeDeviceRecovery(true), SupervisorPolicyOptions(99));
            var progress = new InlineProgress<ContinuousFarmSupervisorProgress>(value =>
            {
                lock (snapshots) snapshots.Add(value.Device);
            });
            supervisor.RunAsync(new[] { "May 1" }, new H().Request, progress,
                cancellation.Token).GetAwaiter().GetResult();
            lock (snapshots)
                Is(snapshots.Any(item => item.LastBackoffDelayMs == 5),
                    "first technical backoff tier was not used");
        }
    }

    static void ContinuousCircuitBreakerRecoversAfterCooldown()
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var runner = new TechnicalFailureRunner(cancellation, 3);
            var recovery = new FakeDeviceRecovery(true);
            var states = new List<ContinuousFarmDeviceSnapshot>();
            var supervisor = new ContinuousFarmSupervisor(runner, recovery,
                SupervisorPolicyOptions(2));
            var progress = new InlineProgress<ContinuousFarmSupervisorProgress>(value =>
            {
                lock (states) states.Add(value.Device);
            });
            ContinuousFarmSupervisorResult result = supervisor.RunAsync(new[] { "May 1" },
                new H().Request, progress, cancellation.Token).GetAwaiter().GetResult();
            lock (states)
            {
                Is(states.Any(item => item.State == ContinuousFarmDeviceState.Quarantined
                    && item.CircuitOpenUntil.HasValue), "circuit never opened");
                Is(states.Any(item => item.Message != null
                    && item.Message.Contains("Circuit breaker closed")),
                    "circuit never closed after recovery probe");
            }
            Is(recovery.Calls >= 3, "quarantine recovery probe did not run");
            Eq(ContinuousFarmDeviceState.Stopped, result.Devices[0].State,
                "test supervisor final state");
        }
    }

    static ContinuousFarmSupervisorOptions SupervisorPolicyOptions(int threshold) =>
        new ContinuousFarmSupervisorOptions(cycleIntervalMs: 60000,
            failureRetryDelayMs: 5, noProgressTimeoutMs: 1000,
            watchdogPollIntervalMs: 10, cancellationGraceMs: 10,
            waitingNoProgressTimeoutMs: 2000,
            technicalRetryDelaysMs: new[] { 5, 10, 20 }, retryJitterMaxMs: 0,
            circuitFailureThreshold: threshold, circuitWindowMs: 10000,
            quarantineCooldownMs: 20);

    static void CheckpointRoundTripsDeviceState()
    {
        string root = TemporaryDirectory();
        try
        {
            var store = new LocalAppDataContinuousFarmCheckpointStore(
                new TestCheckpointPath(root), new Log());
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var checkpoint = new ContinuousFarmCheckpoint
            {
                Version = 1,
                SavedAt = now,
                TechnicalFailureTimestamps = new[] { now.AddMinutes(-2), now.AddMinutes(-1) },
                Device = new ContinuousFarmDeviceSnapshot
                {
                    DeviceName = "May 1", State = ContinuousFarmDeviceState.Waiting,
                    CycleCount = 9, ConsecutiveFailures = 2,
                    LastTransitionAt = now, LastProgressAt = now,
                    LastSuccessAt = now.AddMinutes(-3), LastError = "last error",
                    NextAttemptAt = now.AddMinutes(15), CurrentResource = "Stone",
                    CurrentLevel = 7, CurrentTeam = "Team3",
                    WatchdogTimeoutCount = 1, RecoveryAttemptCount = 2,
                    TechnicalFailuresInWindow = 2, QuarantineCount = 1
                }
            };
            CancellationToken token = new CancellationTokenSource(
                TimeSpan.FromSeconds(5)).Token;
            store.Save(checkpoint, token);
            ContinuousFarmCheckpoint loaded = store.Load("May 1", token);
            Eq(9, loaded.Device.CycleCount, "cycle count");
            Eq("Stone", loaded.Device.CurrentResource, "resource");
            Eq(7, loaded.Device.CurrentLevel, "level");
            Eq("Team3", loaded.Device.CurrentTeam, "team");
            Eq(2, loaded.TechnicalFailureTimestamps.Count, "failure history");
            Eq(1, Directory.GetFiles(root, "*.json").Length, "checkpoint files");
            Eq(0, Directory.GetFiles(root, "*.tmp").Length, "temporary files");
        }
        finally { TryDeleteDirectory(root); }
    }

    static void CheckpointInvalidFileIsIsolated()
    {
        string root = TemporaryDirectory();
        try
        {
            var store = new LocalAppDataContinuousFarmCheckpointStore(
                new TestCheckpointPath(root), new Log());
            CancellationToken token = new CancellationTokenSource(
                TimeSpan.FromSeconds(5)).Token;
            store.Save(TestCheckpoint("May 1", 1), token);
            store.Save(TestCheckpoint("May 2", 2), token);
            string first = Directory.GetFiles(root, "May 1-*.json").Single();
            File.WriteAllText(first, "{invalid");
            Is(store.Load("May 1", token) == null, "invalid checkpoint was accepted");
            Eq(2, store.Load("May 2", token).Device.CycleCount,
                "healthy checkpoint was affected");
            Is(Directory.GetFiles(root, "*.invalid-*.json").Length == 1,
                "invalid checkpoint was not quarantined");
        }
        finally { TryDeleteDirectory(root); }
    }

    static void CheckpointRestoresAsPreflight()
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var store = new MemoryCheckpointStore(TestCheckpoint("May 1", 6));
            store.Current.Device.State = ContinuousFarmDeviceState.Running;
            store.Current.Device.CurrentResource = "Wood";
            store.Current.Device.CurrentTeam = "Team2";
            var first = new TaskCompletionSource<ContinuousFarmDeviceSnapshot>();
            var progress = new InlineProgress<ContinuousFarmSupervisorProgress>(value =>
            {
                first.TrySetResult(value.Device);
            });
            var runner = new CancelAfterSuccessRunner(cancellation);
            var supervisor = new ContinuousFarmSupervisor(runner,
                new FakeDeviceRecovery(true), new ContinuousFarmSupervisorOptions(1, 1),
                null, store);
            supervisor.RunAsync(new[] { "May 1" }, new H().Request, progress,
                cancellation.Token).GetAwaiter().GetResult();
            ContinuousFarmDeviceSnapshot restored = first.Task.GetAwaiter().GetResult();
            Eq(ContinuousFarmDeviceState.Preflight, restored.State,
                "stored gameplay state was resumed");
            Is(restored.RestoredFromCheckpoint, "restore marker");
            Eq(6, restored.CycleCount, "restored cycle count");
            Eq("Wood", restored.CurrentResource, "restored observation");
            Is(restored.Message.Contains("fresh preflight"), "safe restore message");
        }
    }

    static void CheckpointCapturesFarmObservation()
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var store = new MemoryCheckpointStore();
            var supervisor = new ContinuousFarmSupervisor(
                new ObservedFarmRunner(cancellation), new FakeDeviceRecovery(true),
                new ContinuousFarmSupervisorOptions(1, 1), null, store);
            supervisor.RunAsync(new[] { "May 1" }, new H().Request, null,
                cancellation.Token).GetAwaiter().GetResult();
            Eq("Food", store.Current.Device.CurrentResource, "observed resource");
            Eq(6, store.Current.Device.CurrentLevel, "observed level");
            Eq("Team3", store.Current.Device.CurrentTeam, "observed team");
            Is(store.Saves > 1, "checkpoint was not updated during progress");
        }
    }

    static ContinuousFarmCheckpoint TestCheckpoint(string deviceName, int cycles) =>
        new ContinuousFarmCheckpoint
        {
            Version = 1,
            SavedAt = DateTimeOffset.UtcNow,
            Device = new ContinuousFarmDeviceSnapshot
            {
                DeviceName = deviceName, State = ContinuousFarmDeviceState.Waiting,
                CycleCount = cycles, LastTransitionAt = DateTimeOffset.UtcNow,
                LastProgressAt = DateTimeOffset.UtcNow
            },
            TechnicalFailureTimestamps = new DateTimeOffset[0]
        };

    static void CheckpointHasNoNone()
    {
        string[] files = { "Core/Workflows/IContinuousFarmCheckpointStore.cs",
            "Infrastructure/Workflows/LocalAppDataContinuousFarmCheckpointStore.cs",
            "Infrastructure/Workflows/ContinuousFarmSupervisor.cs" };
        foreach (string file in files)
            Is(!File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "ADB", file))
                .Contains("CancellationToken.None"), "token bypass: " + file);
    }

    static void CheckpointHonorsCancellation()
    {
        string root = TemporaryDirectory();
        try
        {
            var store = new LocalAppDataContinuousFarmCheckpointStore(
                new TestCheckpointPath(root), new Log());
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool saveCancelled = false;
                try { store.Save(TestCheckpoint("May 1", 1), cancellation.Token); }
                catch (OperationCanceledException) { saveCancelled = true; }
                Is(saveCancelled, "cancelled save continued");
                bool loadCancelled = false;
                try { store.Load("May 1", cancellation.Token); }
                catch (OperationCanceledException) { loadCancelled = true; }
                Is(loadCancelled, "cancelled load continued");
            }
            Eq(0, Directory.GetFiles(root).Length, "cancelled operation wrote a file");
        }
        finally { TryDeleteDirectory(root); }
    }

    static void MaintenanceRemovesExpiredScreenshots()
    {
        string root = TemporaryDirectory();
        try
        {
            string expired = Path.Combine(root, "old.png");
            string current = Path.Combine(root, "current.png");
            File.WriteAllBytes(expired, new byte[4]);
            File.WriteAllBytes(current, new byte[4]);
            DateTimeOffset now = new DateTimeOffset(2026, 7, 22, 12, 0, 0,
                TimeSpan.Zero);
            File.SetLastWriteTimeUtc(expired, now.UtcDateTime.AddDays(-15));
            File.SetLastWriteTimeUtc(current, now.UtcDateTime);
            var service = Maintenance(root, now, 14, 1000);
            OperationalMaintenanceResult result = service.RunIfDueAsync(
                new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token)
                .GetAwaiter().GetResult();
            Is(!File.Exists(expired), "expired screenshot was retained");
            Is(File.Exists(current), "current screenshot was deleted");
            Eq(1, result.DeletedFileCount, "deleted files");
        }
        finally { DiagnosticStorageGate.Resume(); TryDeleteDirectory(root); }
    }

    static void MaintenanceEnforcesDiagnosticQuota()
    {
        string root = TemporaryDirectory();
        try
        {
            string older = Path.Combine(root, "older.png");
            string newer = Path.Combine(root, "newer.png");
            File.WriteAllBytes(older, new byte[4]);
            File.WriteAllBytes(newer, new byte[4]);
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(-1));
            OperationalMaintenanceResult result = Maintenance(root,
                DateTimeOffset.Now, 100, 6).RunIfDueAsync(
                    new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token)
                .GetAwaiter().GetResult();
            Is(!File.Exists(older) && File.Exists(newer),
                "oldest screenshot was not removed first");
            Is(result.DiagnosticBytes <= 6, "diagnostic quota remains exceeded");
        }
        finally { DiagnosticStorageGate.Resume(); TryDeleteDirectory(root); }
    }

    static void MaintenanceDiskGateUsesHysteresis()
    {
        string root = TemporaryDirectory();
        long free = 5;
        DateTimeOffset now = DateTimeOffset.Now;
        try
        {
            var options = new OperationalMaintenanceOptions(root, 1, 14, 100, 10, 12);
            var service = new FileSystemOperationalMaintenanceService(options, new Log(),
                () => now, path => free);
            CancellationToken token = new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;
            Is(service.RunIfDueAsync(token).GetAwaiter().GetResult()
                .DiagnosticWritesSuspended, "low disk did not suspend diagnostics");
            free = 11; now = now.AddMilliseconds(2);
            Is(service.RunIfDueAsync(token).GetAwaiter().GetResult()
                .DiagnosticWritesSuspended, "gate resumed below hysteresis threshold");
            free = 12; now = now.AddMilliseconds(2);
            Is(!service.RunIfDueAsync(token).GetAwaiter().GetResult()
                .DiagnosticWritesSuspended, "gate did not resume at safe threshold");
        }
        finally { DiagnosticStorageGate.Resume(); TryDeleteDirectory(root); }
    }

    static void MaintenanceRunsOnlyWhenDue()
    {
        string root = TemporaryDirectory();
        int probes = 0;
        DateTimeOffset now = DateTimeOffset.Now;
        try
        {
            var options = new OperationalMaintenanceOptions(root, 60000, 14, 100, 0, 0);
            var service = new FileSystemOperationalMaintenanceService(options, new Log(),
                () => now, path => { probes++; return 100; });
            CancellationToken token = new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;
            Is(service.RunIfDueAsync(token).GetAwaiter().GetResult().WasRun,
                "first maintenance did not run");
            Is(!service.RunIfDueAsync(token).GetAwaiter().GetResult().WasRun,
                "maintenance ignored interval gate");
            Eq(1, probes, "disk probes");
        }
        finally { DiagnosticStorageGate.Resume(); TryDeleteDirectory(root); }
    }

    static FileSystemOperationalMaintenanceService Maintenance(string root,
        DateTimeOffset now, int retentionDays, long quota) =>
        new FileSystemOperationalMaintenanceService(
            new OperationalMaintenanceOptions(root, 1, retentionDays, quota, 0, 0),
            new Log(), () => now, path => 1000);

    static string TemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ikautomation-maintenance-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }

    static void LoggerUsesRotationAndRetention()
    {
        string code = File.ReadAllText(Path.Combine(Environment.CurrentDirectory,
            "ADB", "Helpers", "Logger.cs"));
        Is(code.Contains("RotateIfRequired") && code.Contains("CleanupArchives"),
            "log rotation or retention is missing");
        Is(!code.Contains("Xóa toàn bộ log khi khởi động")
            && !code.Contains("File.WriteAllText(LogFilePath, string.Empty);\n\n                //"),
            "startup still clears the log");
    }

    static void DiagnosticStoresHonorStorageGate()
    {
        string[] files = { "Infrastructure/Diagnostics/ScreenshotFileStore.cs",
            "Infrastructure/Workflows/OneShotFarmDiagnosticService.cs",
            "Infrastructure/GameDetection/UnknownScreenshotStore.cs",
            "Infrastructure/MarchDispatch/DispatchMarchDiagnosticStore.cs",
            "Infrastructure/ResourcePopup/ResourcePopupDiagnosticStore.cs",
            "Infrastructure/ResourceSearch/ResourceLevelFallbackDiagnosticStore.cs",
            "Infrastructure/ResourceSearch/ResourceSearchDiagnosticStore.cs",
            "Infrastructure/TeamSelection/OpenTeamSelectionDiagnosticStore.cs",
            "Infrastructure/TeamSelection/SelectFarmTeamDiagnosticStore.cs" };
        foreach (string file in files)
            Is(File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "ADB", file))
                .Contains("DiagnosticStorageGate.IsWriteEnabled"),
                "storage gate missing: " + file);
    }

    static void ContinuousSupervisorUiIsWired()
    {
        string root = Path.Combine(Environment.CurrentDirectory, "ADB");
        string xaml = File.ReadAllText(Path.Combine(root, "UI",
            "DeviceDiagnosticWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "UI",
            "DeviceDiagnosticWindow.xaml.cs"));
        string main = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"));
        Is(xaml.Contains("RunOneShotFarmButton") && xaml.Contains("RunContinuousFarmButton"),
            "bounded or continuous run control is missing");
        Is(code.Contains("continuousFarmSupervisor.RunAsync")
            && code.Contains("RunContinuousSupervisorAsync"),
            "continuous supervisor is not called by the UI");
        Is(main.Contains("new ContinuousFarmSupervisor("),
            "continuous supervisor is not composed in MainWindow");
    }

    static void MultiDeviceRequestsAreIsolated()
    {
        var probe = new MultiDeviceWorkflowProbe(2);
        var runner = new MultiDeviceOneShotFarmRunner(
            () => new MultiDeviceProbeWorkflow(probe), 2);
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            Task<MultiDeviceOneShotFarmResult> run = runner.RunAsync(
                new[] { "May 1", "May 2" }, new H().Request, null, cancellation.Token);
            Is(probe.RequiredConcurrencyReached.Task.Wait(TimeSpan.FromSeconds(5)),
                "two devices did not enter concurrently");
            probe.Release.TrySetResult(true);
            run.GetAwaiter().GetResult();
            Eq(2, probe.Requests.Count, "request count");
            Is(!ReferenceEquals(probe.Requests[0], probe.Requests[1]),
                "devices shared a mutable request");
            Is(!string.Equals(probe.Requests[0].RunId, probe.Requests[1].RunId,
                    StringComparison.Ordinal), "devices shared a run id");
        }
    }

    static void AdaptiveGateEnforcesLiveLimit()
    {
        var gate = new AdaptiveConcurrencyGate(new AdaptiveConcurrencyOptions(
            minimumConcurrency: 1, initialConcurrency: 2, maximumConcurrency: 4,
            sampleIntervalMs: 60000, healthySamplesToIncrease: 100,
            automationStaggerMinMs: 0, automationStaggerMaxMs: 0,
            recoveryStaggerMinMs: 0, recoveryStaggerMaxMs: 0),
            new FakeHostResourceProbe(10, 8L * 1024 * 1024 * 1024));
        IAdaptiveConcurrencyLease first = gate.AcquireAsync("May 1",
            AdaptiveOperationKind.Automation, default(CancellationToken)).GetAwaiter().GetResult();
        IAdaptiveConcurrencyLease second = gate.AcquireAsync("May 2",
            AdaptiveOperationKind.Automation, default(CancellationToken)).GetAwaiter().GetResult();
        Task<IAdaptiveConcurrencyLease> third = gate.AcquireAsync("May 3",
            AdaptiveOperationKind.Automation, default(CancellationToken));
        Is(!third.Wait(30), "third device bypassed live limit");
        Eq(2, gate.GetSnapshot().ActiveExecutions, "active count");
        first.Dispose();
        IAdaptiveConcurrencyLease admitted = third.GetAwaiter().GetResult();
        Eq(2, gate.GetSnapshot().ActiveExecutions, "queued device was not admitted");
        admitted.Dispose();
        second.Dispose();
    }

    static void AdaptiveGateReducesOnPressure()
    {
        var gate = new AdaptiveConcurrencyGate(new AdaptiveConcurrencyOptions(
            minimumConcurrency: 1, initialConcurrency: 3, maximumConcurrency: 4,
            sampleIntervalMs: 1, healthySamplesToIncrease: 100, highCpuPercent: 50,
            automationStaggerMinMs: 0, automationStaggerMaxMs: 0,
            recoveryStaggerMinMs: 0, recoveryStaggerMaxMs: 0),
            new FakeHostResourceProbe(99, 8L * 1024 * 1024 * 1024));
        using (gate.AcquireAsync("May 1", AdaptiveOperationKind.Automation,
            default(CancellationToken)).GetAwaiter().GetResult()) { }
        Thread.Sleep(5);
        using (gate.AcquireAsync("May 2", AdaptiveOperationKind.Automation,
            default(CancellationToken)).GetAwaiter().GetResult()) { }
        Eq(1, gate.GetSnapshot().CurrentLimit, "pressure did not reduce limit");
    }

    static void AdaptiveStaggerHonorsCancellation()
    {
        var gate = new AdaptiveConcurrencyGate(new AdaptiveConcurrencyOptions(
            minimumConcurrency: 1, initialConcurrency: 1, maximumConcurrency: 1,
            sampleIntervalMs: 60000, healthySamplesToIncrease: 100,
            automationStaggerMinMs: 200, automationStaggerMaxMs: 200,
            recoveryStaggerMinMs: 0, recoveryStaggerMaxMs: 0),
            new FakeHostResourceProbe(10, 8L * 1024 * 1024 * 1024));
        using (gate.AcquireAsync("May 1", AdaptiveOperationKind.Automation,
            default(CancellationToken)).GetAwaiter().GetResult()) { }
        using (var source = new CancellationTokenSource(10))
        {
            bool cancelled = false;
            try
            {
                gate.AcquireAsync("May 2", AdaptiveOperationKind.Automation,
                    source.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancelled = true; }
            Is(cancelled, "stagger delay ignored cancellation");
        }
    }

    static void AdaptiveGateIncreasesWithinMaximum()
    {
        var gate = new AdaptiveConcurrencyGate(new AdaptiveConcurrencyOptions(
            minimumConcurrency: 1, initialConcurrency: 1, maximumConcurrency: 2,
            sampleIntervalMs: 1, healthySamplesToIncrease: 1,
            automationStaggerMinMs: 0, automationStaggerMaxMs: 0,
            recoveryStaggerMinMs: 0, recoveryStaggerMaxMs: 0),
            new FakeHostResourceProbe(10, 8L * 1024 * 1024 * 1024));
        for (int index = 0; index < 4; index++)
        {
            using (gate.AcquireAsync("Healthy " + index,
                AdaptiveOperationKind.Automation, default(CancellationToken))
                .GetAwaiter().GetResult()) { }
            Thread.Sleep(3);
        }
        Eq(2, gate.GetSnapshot().CurrentLimit, "healthy samples exceeded maximum");
    }

    static void ConcurrentBatchesShareConcurrencyLimit()
    {
        var probe = new MultiDeviceWorkflowProbe(20);
        var runner = new MultiDeviceOneShotFarmRunner(
            () => new MultiDeviceProbeWorkflow(probe), 20);
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            Task<MultiDeviceOneShotFarmResult> first = runner.RunAsync(
                Enumerable.Range(1, 15).Select(index => "First" + index).ToArray(),
                new H().Request, null, cancellation.Token);
            Task<MultiDeviceOneShotFarmResult> retry = runner.RunAsync(
                Enumerable.Range(1, 15).Select(index => "Retry" + index).ToArray(),
                new H().Request, null, cancellation.Token);
            Is(probe.RequiredConcurrencyReached.Task.Wait(TimeSpan.FromSeconds(5)),
                "concurrent batches did not fill the shared limit");
            Eq(20, probe.MaximumActive, "concurrent batches exceeded shared limit");
            probe.Release.TrySetResult(true);
            Task.WhenAll(first, retry).GetAwaiter().GetResult();
            Eq(20, probe.MaximumActive, "shared limit changed after queued work");
        }
    }

    static void MultiDeviceFailureIsIsolated()
    {
        var runner = new MultiDeviceOneShotFarmRunner(
            () => new SelectiveFailureWorkflow(), 2);
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            MultiDeviceOneShotFarmResult result = runner.RunAsync(
                new[] { "Failing Device", "Healthy Device" }, new H().Request,
                null, cancellation.Token).GetAwaiter().GetResult();
            Eq(MultiDeviceOneShotFarmStage.Failed,
                result.Devices.Single(item => item.DeviceName == "Failing Device").Stage,
                "failed device stage");
            Eq(MultiDeviceOneShotFarmStage.Completed,
                result.Devices.Single(item => item.DeviceName == "Healthy Device").Stage,
                "healthy device was stopped");
        }
    }

    static void MultiDevicePreflightBarrier()
    {
        var availability = new MultiDevicePreflightAvailability(2);
        var workflow = new PreflightAwareWorkflow(availability);
        var runner = new MultiDeviceOneShotFarmRunner(() => workflow,
            () => availability, 2);
        MultiDeviceOneShotFarmResult result = runner.RunAsync(
            new[] { "May 1", "May 2" }, new H().Request, null,
            default(CancellationToken)).GetAwaiter().GetResult();
        Eq(2, availability.Calls, "preflight calls");
        Is(workflow.AllPreflightsCompleted, "farm started before the preflight barrier");
        Is(result.Devices.All(item => item.Stage == MultiDeviceOneShotFarmStage.Completed),
            "healthy devices did not run");
    }

    static void MultiDevicePreflightFailureIsIsolated()
    {
        var availability = new MultiDevicePreflightAvailability(2, "Broken");
        var workflow = new PreflightAwareWorkflow(availability);
        var runner = new MultiDeviceOneShotFarmRunner(() => workflow,
            () => availability, 2);
        MultiDeviceOneShotFarmResult result = runner.RunAsync(
            new[] { "Broken", "Healthy" }, new H().Request, null,
            default(CancellationToken)).GetAwaiter().GetResult();
        Eq(MultiDeviceOneShotFarmStage.Failed,
            result.Devices.Single(item => item.DeviceName == "Broken").Stage,
            "broken device stage");
        Eq(MultiDeviceOneShotFarmStage.Completed,
            result.Devices.Single(item => item.DeviceName == "Healthy").Stage,
            "healthy device was blocked");
        Is(!workflow.StartedDevices.Contains("Broken"), "failed preflight entered gameplay");
    }

    static void MultiDevicePreflightIsReused()
    {
        var availability = new MultiDevicePreflightAvailability(1);
        var inner = new FakeInnerWorkflow();
        var runner = new MultiDeviceOneShotFarmRunner(
            () => new ReadyTeamOneShotFarmWorkflow(inner, availability,
                new ReadyTeamGateOptions(1, 1000), new Log()),
            () => availability, 1);
        MultiDeviceOneShotFarmResult result = runner.RunAsync(new[] { "May 1" },
            new H().Request, null, default(CancellationToken)).GetAwaiter().GetResult();
        Eq(1, availability.Calls, "readiness was checked twice");
        Eq(MultiDeviceOneShotFarmStage.Completed, result.Devices[0].Stage,
            "seeded workflow result");
    }

    static void MultiDeviceUiRetriesFailures()
    {
        string root = Path.Combine(Environment.CurrentDirectory, "ADB", "UI");
        string xaml = File.ReadAllText(Path.Combine(root, "DeviceDiagnosticWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "DeviceDiagnosticWindow.xaml.cs"));
        Is(xaml.Contains("RetryFailedDevicesButton")
            && xaml.Contains("RetryFailedDevices_Click"), "retry button missing");
        Is(code.Contains("item.Stage == MultiDeviceOneShotFarmStage.Failed")
            && code.Contains("RunDeviceBatchAsync(devices, retryRequest"),
            "retry does not isolate failed devices");
        Is(code.Contains("deviceAttemptVersions")
            && code.Contains("IsCurrentDeviceAttempt"),
            "stale device attempts can overwrite retry results");
    }

    static void Invalid(){var h=new H();h.Request.ResourceType=(ResourceType)99;var r=Go(h);Eq(OneShotFarmOutcome.PreconditionFailed,r.Outcome,"outcome");Eq(0,h.Total,"calls");}
    static void Unknown(){var h=new H();h.Detector.Initial=GameState.Unknown;var r=Go(h);Eq(OneShotFarmOutcome.PreconditionFailed,r.Outcome,"outcome");Eq(0,h.Nav.EnsureCalls,"input");}
    static void InitialPopup(){var h=new H();h.Detector.Initial=GameState.ResourcePopup;Eq(OneShotFarmOutcome.PreconditionFailed,Go(h).Outcome,"outcome");}
    static void InitialTeam(){var h=new H();h.Detector.Initial=GameState.TeamSelection;Eq(OneShotFarmOutcome.PreconditionFailed,Go(h).Outcome,"outcome");}
    static void Continent(){var h=new H();h.Detector.Initial=GameState.ContinentMap;Is(Go(h).Success,"not handled");Eq(1,h.Nav.EnsureCalls,"ensure");}
    static void EnsureFail(){var h=new H();h.Nav.EnsureSuccess=false;Eq(OneShotFarmOutcome.WorldMapUnavailable,Go(h).Outcome,"outcome");Eq(0,h.Nav.PanelCalls,"panel");}
    static void PanelFail(){var h=new H();h.Nav.PanelSuccess=false;Eq(OneShotFarmOutcome.SearchPanelUnavailable,Go(h).Outcome,"outcome");Eq(0,h.Config.Calls,"config");}
    static void CompositePanelEvidence(){var h=new H();h.Nav.UseFallbackEvidence=true;var r=Go(h);Is(r.Success,"composite panel evidence was rejected");Eq(1,h.Config.Calls,"config");}
    static void ConfigureFail(){var h=new H();h.Config.Success=false;Eq(OneShotFarmOutcome.SearchConfigurationFailed,Go(h).Outcome,"outcome");Eq(0,h.Search.Calls,"search");}
    static void ConfigureBeforeFalse(){var h=new H();Go(h);Is(!h.Search.Last.ConfigureBeforeSearch,"double configure");}
    static void NotFound(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Eq(OneShotFarmOutcome.ResourceLevelsExhausted,r.Outcome,"outcome");Eq(0,h.Popup.Calls,"popup");}
    static void NotFoundBusiness(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Is(!r.Success&&r.ErrorMessage==null,"treated as exception");}
    static void NotFoundLastCompleted(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;Eq(OneShotFarmStep.SearchWithLevelFallback,Go(h).LastCompletedStep,"last");}
    static void NotFoundSkipsDownstream(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;Go(h);Eq(0,h.Popup.Calls+h.Open.Calls+h.Select.Calls+h.Dispatch.Calls,"downstream calls");}
    static void SearchTimeout(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.Timeout;h.Search.Success=false;Eq(OneShotFarmOutcome.SearchExecutionFailed,Go(h).Outcome,"outcome");Eq(0,h.Popup.Calls,"popup");}
    static void LocatedCallsPopup(){var h=new H();Go(h);Eq(1,h.Popup.Calls,"popup");}
    static void PopupFail(){var h=new H();h.Popup.Ready=false;Eq(OneShotFarmOutcome.ResourcePopupNotReady,Go(h).Outcome,"outcome");Eq(0,h.Open.Calls,"team");}
    static void OpenTeamNotReady(){var h=new H();h.Open.Ready=false;Eq(OneShotFarmOutcome.TeamSelectionNotReady,Go(h).Outcome,"outcome");Eq(0,h.Select.Calls,"select");}
    static void NoTeam(){var h=new H();h.Select.Outcome=SelectFarmTeamOutcome.NoEligibleTeam;h.Select.Success=false;var r=Go(h);Eq(OneShotFarmOutcome.NoEligibleTeam,r.Outcome,"outcome");Eq(0,h.Dispatch.Calls,"dispatch");}
    static void SelectedPassed(){var h=new H();Go(h);Eq(TeamNumber.Team4,h.Dispatch.Last.ExpectedTeam,"team");}
    static void Team3Passed(){var h=new H();h.Select.Team=TeamNumber.Team3;var r=Go(h);Eq(TeamNumber.Team3,h.Dispatch.Last.ExpectedTeam,"team");Eq(TeamNumber.Team3,r.SelectedTeam.Value,"selected");}
    static void MarchSuccess(){var h=new H();var r=Go(h);Is(r.Success,"success");Eq(OneShotFarmOutcome.MarchStarted,r.Outcome,"outcome");}
    static void UnverifiedDispatch(){var h=new H();h.Dispatch.Verified=false;Is(!Go(h).Success,"false success");}
    static void AlreadyMarching(){var h=new H();h.Dispatch.Outcome=DispatchMarchOutcome.AlreadyMarching;Is(Go(h).Success,"already marching");}
    static void StepOrder(){var h=new H();var s=Go(h).Steps.Select(x=>x.Step).ToArray();var expected=new[]{OneShotFarmStep.Preflight,OneShotFarmStep.EnsureWorldMap,OneShotFarmStep.OpenSearchPanel,OneShotFarmStep.SearchWithLevelFallback,OneShotFarmStep.VerifyResourcePopup,OneShotFarmStep.OpenTeamSelection,OneShotFarmStep.SelectTeam,OneShotFarmStep.DispatchTeam,OneShotFarmStep.FinalVerification,OneShotFarmStep.Completed};Is(expected.SequenceEqual(s),"order");}
    static void LastCompleted(){var h=new H();h.Config.Success=false;Eq(OneShotFarmStep.OpenSearchPanel,Go(h).LastCompletedStep,"last");}
    static void NoStepAfterFailure(){ConfigureFail();} static void NoWorkflowRetry(){var h=new H();h.Nav.EnsureSuccess=false;Go(h);Eq(1,h.Nav.EnsureCalls,"retry");}
    static void EachOnce(){var h=new H();Go(h);Eq(7,h.Nav.EnsureCalls+h.Nav.PanelCalls+h.Config.Calls+h.Search.Calls+h.Popup.Calls+h.Open.Calls+h.Select.Calls,"pre-dispatch calls");Eq(1,h.Dispatch.Calls,"dispatch");}
    static void CancelBefore(){var h=new H();using(var c=new CancellationTokenSource()){c.Cancel();var r=Go(h,c.Token);Eq(OneShotFarmOutcome.Cancelled,r.Outcome,"outcome");Eq(0,h.Total,"calls");}}
    static void CancelBetween(){var h=new H();using(var c=new CancellationTokenSource()){h.Nav.AfterEnsure=()=>c.Cancel();var r=Go(h,c.Token);Eq(OneShotFarmOutcome.Cancelled,r.Outcome,"outcome");Eq(0,h.Nav.PanelCalls,"panel");}}
    static void LeaseSuccess(){var h=new H();Go(h);Eq(1,h.Lock.Releases,"release");} static void LeaseFailure(){var h=new H();h.Config.Success=false;Go(h);Eq(1,h.Lock.Releases,"release");}
    static void LeaseCancel(){var h=new H();using(var c=new CancellationTokenSource()){h.Nav.AfterEnsure=()=>c.Cancel();Go(h,c.Token);Eq(1,h.Lock.Releases,"release");}}
    static void SameDevice(){var gate=DeviceOperationLock.Shared;int active=0,max=0;Func<Task<int>> f=()=>gate.RunAsync("same",async t=>{max=Math.Max(max,Interlocked.Increment(ref active));await Task.Delay(20,t);Interlocked.Decrement(ref active);return 1;},default(CancellationToken));Task.WaitAll(f(),f());Eq(1,max,"concurrency");}
    static void DifferentDevices(){var gate=DeviceOperationLock.Shared;var entered=new CountdownEvent(2);Func<string,Task<int>> f=d=>gate.RunAsync(d,async t=>{entered.Signal();Is(entered.Wait(500),"global lock");await Task.Delay(1,t);return 1;},default(CancellationToken));Task.WaitAll(Task.Run(()=>f("a")),Task.Run(()=>f("b")));}
    static string Source()=>File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","Infrastructure","Workflows","OneShotFarmWorkflow.cs"));
    static void NoClient(){Is(!Source().Contains("ILdPlayerClient"),"client dependency");} static void NoAuto(){Is(!Source().Contains("Auto_"+"LDPlayer"),"direct adapter");}
    static void NoInput(){string s=Source();Is(!s.Contains("TapAsync")&&!s.Contains("SwipeByPercent")&&!s.Contains("BackAsync"),"direct input");}
    static void NoNone(){Is(!Source().Contains("CancellationToken"+".None"),"token bypass");}
    static void ScreenshotFailure(){var h=new H();h.Diag.Throw=true;Is(Go(h).Success,"diagnostic replaced outcome");}
    static void NoLevelFallback(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Eq(3,h.Config.Calls,"fallback config");Is(new[]{7,6,5}.SequenceEqual(r.AttemptedLevels),"levels");}
    static void Level6Located(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Go(h);Is(r.Success,"workflow");Eq(6,r.LocatedLevel,"located");Is(new[]{7,6}.SequenceEqual(r.AttemptedLevels),"attempted");}
    static void Level6PopupFailureMapping(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);h.Popup.Ready=false;var r=Go(h);Eq(OneShotFarmOutcome.ResourcePopupNotReady,r.Outcome,"outcome");Is(r.Outcome!=OneShotFarmOutcome.SearchExecutionFailed,"wrong mapping");Eq(6,r.LocatedLevel,"located");Is(new[]{7,6}.SequenceEqual(r.AttemptedLevels),"attempted");Eq(OneShotFarmStep.VerifyResourcePopup,r.LastCompletedStep,"last");Eq(0,h.Open.Calls,"team opened");}
    static void VerifiedRequired(){UnverifiedDispatch();} static void StructuralAccepted(){var h=new H();h.Dispatch.Verified=true;Is(Go(h).Success,"structural");}
    static void NoSecondCycle(){var h=new H();Go(h);Eq(1,h.Search.Calls,"cycle count");}
    static void IronFullSwitchesStone()
    {
        var h=new H(); h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);
        h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.MarchStarted);
        var r=Go(h);
        Is(r.Success,"Stone should dispatch"); Eq(OneShotFarmOutcome.MarchStarted,r.Outcome,"outcome");
        Is(new[]{ResourceType.Iron,ResourceType.Stone}.SequenceEqual(r.AttemptedResources),"resource order");
        Is(new[]{ResourceType.Iron}.SequenceEqual(r.StorageFullResources),"storage list");
        Eq((ResourceType?)ResourceType.Stone,r.LocatedResource,"located resource");
        Eq((ResourceType?)ResourceType.Stone,r.DispatchedResource,"dispatched resource");
        Eq(2,h.Dispatch.Calls,"dispatch attempts"); Eq(ResourceType.Stone,h.Dispatch.Last.CurrentResource,"last resource");
    }
    static void ResourceExpirySwitchesResource()
    {
        var h=new H();
        h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.ResourceExpiryResourceSwitchRequired);
        h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.MarchStarted);
        ResourceFarmFallbackResult r=Plan(h);
        Eq(ResourceFarmFallbackOutcome.MarchStarted,r.Outcome,"outcome");
        Is(new[]{ResourceType.Iron,ResourceType.Stone}.SequenceEqual(r.AttemptedResources),"resource order");
        Eq(0,r.StorageFullResources.Count,"storage list");
        Is(r.Attempts[0].ResourceExpiryDetected&&r.Attempts[0].RecoverySucceeded,"expiry recovery");
        Eq((ResourceType?)ResourceType.Stone,r.DispatchedResource,"dispatched resource"); Eq(2,h.Dispatch.Calls,"dispatch attempts");
    }
    static void BothStoragesFull()
    {
        var h=new H(); h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);
        h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);
        var r=Go(h);
        Eq(OneShotFarmOutcome.AllCandidateStoragesFull,r.Outcome,"outcome");
        Is(new[]{ResourceType.Iron,ResourceType.Stone}.SequenceEqual(r.StorageFullResources),"storage list");
    }
    static void DefaultResourcePriority(){Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}.SequenceEqual(new OneShotFarmRequest().ResourcePriority),"priority");}
    static void ExhaustedAdvances(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Plan(h);Eq(ResourceFarmFallbackOutcome.MarchStarted,r.Outcome,"outcome");Is(new[]{ResourceType.Iron,ResourceType.Stone}.SequenceEqual(r.AttemptedResources),"attempted");Is(new[]{ResourceType.Iron}.SequenceEqual(r.LevelsExhaustedResources),"exhausted");}
    static void ExhaustedPassRepositionsAndRetries(){var h=new H();h.Request.ResourceLevelPriority=new[]{7};for(int i=0;i<4;i++)h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Plan(h);Eq(ResourceFarmFallbackOutcome.MarchStarted,r.Outcome,"outcome");Eq(1,h.Nav.RepositionCalls,"reposition");Eq(5,h.Search.Calls,"search calls");Eq((ResourceType?)ResourceType.Iron,r.DispatchedResource,"retried resource");Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}.SequenceEqual(r.LevelsExhaustedResources),"exhausted pass");}
    static void AllResourcesExhausted(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;var r=Plan(h);Eq(ResourceFarmFallbackOutcome.ResourcePlanExhausted,r.Outcome,"outcome");Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}.SequenceEqual(r.LevelsExhaustedResources),"exhausted");Eq(0,h.Popup.Calls+h.Open.Calls+h.Select.Calls+h.Dispatch.Calls,"downstream");}
    static void AllResourcesStorageFull(){var h=new H();for(int i=0;i<4;i++)h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);var r=Plan(h);Eq(ResourceFarmFallbackOutcome.AllCandidateStoragesFull,r.Outcome,"outcome");Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}.SequenceEqual(r.StorageFullResources),"storage order");Eq(4,h.Dispatch.Calls,"dispatches");}
    static void MixedFallbackReachesWood(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.MarchStarted);var r=Plan(h);Eq(ResourceFarmFallbackOutcome.MarchStarted,r.Outcome,"outcome");Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood}.SequenceEqual(r.AttemptedResources),"attempted");Is(new[]{ResourceType.Iron}.SequenceEqual(r.StorageFullResources),"storage");Is(new[]{ResourceType.Stone}.SequenceEqual(r.LevelsExhaustedResources),"exhausted");Eq((ResourceType?)ResourceType.Wood,r.DispatchedResource,"resource");Eq((int?)7,r.LocatedLevel,"level");}
    static void TechnicalSearchDoesNotSwitch(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.Timeout;h.Search.Success=false;var r=Plan(h);Eq(ResourceFarmFallbackOutcome.SearchFailed,r.Outcome,"outcome");Is(new[]{ResourceType.Iron}.SequenceEqual(r.AttemptedResources),"switched");}
    static void MissingProfileStopsInput(){var h=new H();var p=new FakeProfiles{Unsupported=ResourceType.Iron};var r=Plan(h,p);Eq(ResourceFarmFallbackOutcome.SearchFailed,r.Outcome,"outcome");Eq(0,h.Nav.PanelCalls,"input");Is(r.ErrorMessage.Contains("ResourcePopupIronTitle"),"template id");}
    static void PopupMismatchStopsTeam(){var h=new H();h.Popup.Ready=false;var r=Plan(h);Eq(ResourceFarmFallbackOutcome.PopupFailed,r.Outcome,"outcome");Eq(0,h.Open.Calls,"team");Is(new[]{ResourceType.Iron}.SequenceEqual(r.AttemptedResources),"switched");}
    static void MarchSendsOneTeam(){var h=new H();var r=Plan(h);Is(r.Success,"success");Eq(1,h.Dispatch.Calls,"multiple teams");Is(new[]{ResourceType.Iron}.SequenceEqual(r.AttemptedResources),"continued plan");}
    static void ResourcePlanUsesWorkflowLease(){var h=new H();var plan=new FakePlan{DuringRun=()=>Is(h.Lock.Active,"lease was released")};var workflow=PlanWorkflow(h,plan);var r=workflow.RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"workflow");Eq(1,plan.Calls,"plan calls");Eq(1,h.Lock.Releases,"lease releases");}
    static void InitialSearchPanelUsesPlan(){var h=new H();h.Detector.Initial=GameState.ResourceSearchPanel;var plan=new FakePlan();var r=PlanWorkflow(h,plan).RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"workflow");Eq(1,plan.Calls,"plan calls");Eq(1,h.Nav.EnsureCalls,"bounded recovery");}
    static void SelectionDefaults(){var s=new OneShotFarmResourceSelection();Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}.SequenceEqual(s.GetSelectedResources()),"defaults");}
    static void SelectionRejectsZero(){var s=new OneShotFarmResourceSelection{Iron=false,Stone=false,Wood=false,Food=false};ThrowsSelection(s);}
    static void SelectionRejectsOne(){var s=new OneShotFarmResourceSelection{Iron=true,Stone=false,Wood=false,Food=false};ThrowsSelection(s);}
    static void ThrowsSelection(OneShotFarmResourceSelection s){try{s.CreateRequest(new OneShotFarmRequest());throw new Exception("selection was accepted");}catch(ArgumentException e){Is(e.Message.Contains("ít nhất 2"),"validation message");}}
    static void SelectionAcceptsTwo(){var s=new OneShotFarmResourceSelection{Iron=true,Stone=false,Wood=true,Food=false};var r=s.CreateRequest(new OneShotFarmRequest());Eq(2,r.ResourcePriority.Count,"count");Is(r.ShuffleResourcePriority,"shuffle flag");}
    static void SelectionMapsAndShuffles(){var s=new OneShotFarmResourceSelection{Iron=true,Stone=false,Wood=true,Food=true};var r=s.CreateRequest(new OneShotFarmRequest());Is(new[]{ResourceType.Iron,ResourceType.Wood,ResourceType.Food}.SequenceEqual(r.SelectedResources),"selected");Is(!r.ResourcePriority.Contains(ResourceType.Stone),"added Stone");}
    static void ShuffledPriorityIsFixed(){var h=new H();var s=new OneShotFarmResourceSelection{Iron=true,Stone=false,Wood=true,Food=true};h.Request=s.CreateRequest(h.Request);var plan=new FakePlan();var random=new FakeRandom(0,0);var r=PlanWorkflow(h,plan,new FakeRegistry(),random).RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Is(h.Request.ResourcePriority.SequenceEqual(plan.LastPriority),"priority changed");Is(h.Request.ResourcePriority.SequenceEqual(r.ShuffledResourcePriority),"result priority");Is(h.Request.SelectedResources.SequenceEqual(r.SelectedResources),"result selected");Eq(2,random.Calls,"shuffle count");}
    static void ShuffledStorageFallback(){var h=new H();h.Request.ResourcePriority=new[]{ResourceType.Food,ResourceType.Iron,ResourceType.Wood};h.Request.SelectedResources=h.Request.ResourcePriority;h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.MarchStarted);var r=Plan(h);Is(new[]{ResourceType.Food,ResourceType.Iron}.SequenceEqual(r.AttemptedResources),"storage order");}
    static void ShuffledLevelFallback(){var h=new H();h.Request.ResourcePriority=new[]{ResourceType.Wood,ResourceType.Iron};h.Request.SelectedResources=h.Request.ResourcePriority;for(int i=0;i<3;i++)h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Plan(h);Is(new[]{ResourceType.Wood,ResourceType.Iron}.SequenceEqual(r.AttemptedResources),"level order");}
    static void SelectedSubsetOnly(){var h=new H();h.Request.ResourcePriority=new[]{ResourceType.Iron,ResourceType.Wood};h.Request.SelectedResources=h.Request.ResourcePriority;h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;var r=Plan(h);Is(new[]{ResourceType.Iron,ResourceType.Wood}.SequenceEqual(r.AttemptedResources),"subset");Is(!r.AttemptedResources.Contains(ResourceType.Stone)&&!r.AttemptedResources.Contains(ResourceType.Food),"excluded resource tried");}
    static void DiagnosticDisplaysSelection(){string s=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","UI","DeviceDiagnosticWindow.xaml.cs"));Is(s.Contains("Selected resources:")&&s.Contains("Shuffled order:"),"status fields");}
    static void SelectionHasNoNone(){string root=Path.Combine(Environment.CurrentDirectory,"ADB");foreach(string f in new[]{Path.Combine(root,"Core","Workflows","OneShotFarmResourceSelection.cs"),Path.Combine(root,"Infrastructure","Workflows","SystemRandomProvider.cs")})Is(!File.ReadAllText(f).Contains("CancellationToken"+".None"),"token bypass");}
    static void PreflightBeforeShuffle(){var h=new H();h.Request=new OneShotFarmResourceSelection().CreateRequest(h.Request);var registry=new FakeRegistry();registry.Missing.Add(TemplateId.ResourcePopupFoodTitle);var random=new FakeRandom(0,0,0);var plan=new FakePlan();var r=PlanWorkflow(h,plan,registry,random).RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Eq(0,random.Calls,"random called");Eq(0,plan.Calls,"fallback called");Eq(0,h.Nav.EnsureCalls,"ensure called");Eq(OneShotFarmOutcome.PreconditionFailed,r.Outcome,"outcome");Eq(OneShotFarmStep.Preflight,r.LastCompletedStep,"last");Eq(0,r.AttemptedResources.Count,"attempted");Eq(0,r.ShuffledResourcePriority.Count,"shuffle result");Is(r.ErrorMessage.Contains("No game input was sent"),"message");}
    static void UncheckedResourcesIgnored(){var h=new H();h.Request=new OneShotFarmResourceSelection{Iron=true,Stone=false,Wood=true,Food=false}.CreateRequest(h.Request);var registry=new FakeRegistry();registry.Missing.UnionWith(new[]{TemplateId.ResourceStoneSelected,TemplateId.ResourcePopupFoodTitle});var random=new FakeRandom(0);var plan=new FakePlan();var r=PlanWorkflow(h,plan,registry,random).RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"unchecked template blocked run");Eq(1,random.Calls,"random");}
    static void MissingTemplatesStopBeforeInput(){var h=new H();h.Request=new OneShotFarmResourceSelection().CreateRequest(h.Request);var registry=new FakeRegistry();registry.Missing.UnionWith(new[]{TemplateId.ResourceStoneSelected,TemplateId.ResourcePopupWoodTitle,TemplateId.ResourcePopupFoodTitle});var plan=new FakePlan();var r=PlanWorkflow(h,plan,registry,new FakeRandom(0,0,0)).RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Eq(3,r.MissingRuntimeTemplates.Count,"missing count");Is(r.MissingRuntimeTemplates.All(x=>!string.IsNullOrWhiteSpace(x.ExpectedPath)),"paths");Eq(0,h.Total,"input services");Eq(0,plan.Calls,"plan");Is(r.Outcome!=OneShotFarmOutcome.SearchExecutionFailed,"wrong mapping");}
    static void PopupTitleFilesExist(){string root=Path.Combine(Environment.CurrentDirectory,"ADB","Data","InfinityKingdom","1280x720","vi","Resources");foreach(string name in new[]{"resource_popup_iron_title.png","resource_popup_stone_title.png","resource_popup_wood_title.png","resource_popup_food_title.png"})Is(File.Exists(Path.Combine(root,name)),"missing "+name);}
    static void ReadyGateStartsImmediately(){var availability=new FakeAvailability(true);var inner=new FakeInnerWorkflow();var gate=ReadyGate(inner,availability);var r=gate.RunAsync("LDPlayer",new OneShotFarmRequest(),default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success&&r.ReadyTeamObserved,"ready result");Eq(1,r.TeamAvailabilityChecks,"checks");Eq(1,inner.Calls,"inner calls");Is(r.ReadyTeams.SequenceEqual(new[]{TeamNumber.Team4}),"ready teams");}
    static void ReadyGateWaitsAndRechecks(){var availability=new FakeAvailability(false,true);var inner=new FakeInnerWorkflow();var gate=ReadyGate(inner,availability);var r=gate.RunAsync("LDPlayer",new OneShotFarmRequest(),default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success&&r.ReadyTeamObserved,"ready result");Eq(2,r.TeamAvailabilityChecks,"checks");Eq(1,inner.Calls,"inner calls");}
    static void BatchFarmUsesAllReadyTeams(){var availability=new FakeAvailability();availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team4,TeamNumber.Team3});availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team3});availability.ReadyTeamSequences.Enqueue(new TeamNumber[0]);var inner=new FakeInnerWorkflow();var request=new OneShotFarmRequest{RunUntilNoReadyTeams=true,ShuffleResourcePriority=false};var r=ReadyGate(inner,availability,new ReadyTeamGateOptions(1,1000,3,1)).RunAsync("LDPlayer",request,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"batch failed");Eq(2,inner.Calls,"dispatch count");Eq(5,r.TeamAvailabilityChecks,"availability checks");Eq(2,r.CompletedDispatches,"summary count");Is(inner.Requests[0].AllowedTeams.SequenceEqual(new[]{TeamNumber.Team4,TeamNumber.Team3}),"first eligible teams");Is(inner.Requests[1].AllowedTeams.SequenceEqual(new[]{TeamNumber.Team3}),"second eligible team");}
    static void BatchFarmRotatesResources(){var availability=new FakeAvailability();availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team4});availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team3});availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team2});availability.ReadyTeamSequences.Enqueue(new TeamNumber[0]);var inner=new FakeInnerWorkflow();var request=new OneShotFarmRequest{RunUntilNoReadyTeams=true,ShuffleResourcePriority=false,SelectedResources=new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food},ResourcePriority=new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}};var r=ReadyGate(inner,availability).RunAsync("LDPlayer",request,default(CancellationToken)).GetAwaiter().GetResult();Is(inner.Requests[0].ResourcePriority.SequenceEqual(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}),"first order");Is(inner.Requests[1].ResourcePriority.SequenceEqual(new[]{ResourceType.Stone,ResourceType.Wood,ResourceType.Food,ResourceType.Iron}),"second order");Is(inner.Requests[2].ResourcePriority.SequenceEqual(new[]{ResourceType.Wood,ResourceType.Food,ResourceType.Iron,ResourceType.Stone}),"third order");Is(r.DispatchedResources.SequenceEqual(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood}),"resource summary");}
    static void BatchFarmSkipsDispatchedTeam(){var availability=new FakeAvailability();availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team4});availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team4});var inner=new FakeInnerWorkflow();var request=new OneShotFarmRequest{RunUntilNoReadyTeams=true,ShuffleResourcePriority=false};var r=ReadyGate(inner,availability).RunAsync("LDPlayer",request,default(CancellationToken)).GetAwaiter().GetResult();Eq(1,inner.Calls,"stale team reused");Is(r.BatchDispatchedTeams.SequenceEqual(new[]{TeamNumber.Team4}),"team summary");}
    static void BatchFarmRechecksTransientEmpty(){var availability=new FakeAvailability();availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team4});availability.ReadyTeamSequences.Enqueue(new TeamNumber[0]);availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team3});availability.ReadyTeamSequences.Enqueue(new TeamNumber[0]);var inner=new FakeInnerWorkflow();var request=new OneShotFarmRequest{RunUntilNoReadyTeams=true,ShuffleResourcePriority=false};var r=ReadyGate(inner,availability,new ReadyTeamGateOptions(1,1000,3,1)).RunAsync("LDPlayer",request,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"batch failed");Eq(2,inner.Calls,"transient empty stopped batch");Is(r.BatchDispatchedTeams.SequenceEqual(new[]{TeamNumber.Team4,TeamNumber.Team3}),"teams");}
    static void BatchFarmStopsOnFailure(){var availability=new FakeAvailability();availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team4});availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team3});var inner=new FakeInnerWorkflow();inner.Successes.Enqueue(true);inner.Successes.Enqueue(false);var request=new OneShotFarmRequest{RunUntilNoReadyTeams=true,ShuffleResourcePriority=false};var r=ReadyGate(inner,availability).RunAsync("LDPlayer",request,default(CancellationToken)).GetAwaiter().GetResult();Is(!r.Success,"failure hidden");Eq(2,inner.Calls,"continued after failure");Eq(1,r.CompletedDispatches,"completed count");}
    static void ReadyGateExcludesUnavailableTeams(){var availability=new FakeAvailability{AvailableTeams=new[]{TeamNumber.Team1,TeamNumber.Team2,TeamNumber.Team3}};availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team4});availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team3});var inner=new FakeInnerWorkflow();var request=new OneShotFarmRequest{AllowedTeams=new[]{TeamNumber.Team4,TeamNumber.Team3},TeamPriority=new[]{TeamNumber.Team4,TeamNumber.Team3}};var r=ReadyGate(inner,availability,new ReadyTeamGateOptions(1,1000)).RunAsync("LDPlayer",request,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"gate failed");Eq(2,r.TeamAvailabilityChecks,"checks");Is(r.DetectedTeams.SequenceEqual(new[]{TeamNumber.Team1,TeamNumber.Team2,TeamNumber.Team3}),"detected teams");Is(inner.Requests[0].AllowedTeams.SequenceEqual(new[]{TeamNumber.Team3}),"absent Team4 was allowed");}
    static void ReadyGateUsesAllowedTeams(){var availability=new FakeAvailability();availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team1});availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team3});var inner=new FakeInnerWorkflow();var request=new OneShotFarmRequest{AllowedTeams=new[]{TeamNumber.Team3,TeamNumber.Team4}};var r=ReadyGate(inner,availability,new ReadyTeamGateOptions(1,1000)).RunAsync("LDPlayer",request,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"gate failed");Eq(2,r.TeamAvailabilityChecks,"checks");Is(r.ReadyTeams.SequenceEqual(new[]{TeamNumber.Team3}),"eligible ready teams");Eq(1,inner.Calls,"inner calls");}
    static void ReadyGateTimeout(){var availability=new FakeAvailability(false);var inner=new FakeInnerWorkflow();var r=ReadyGate(inner,availability,new ReadyTeamGateOptions(1,5)).RunAsync("LDPlayer",new OneShotFarmRequest(),default(CancellationToken)).GetAwaiter().GetResult();Eq(OneShotFarmOutcome.TeamAvailabilityWaitTimeout,r.Outcome,"outcome");Is(r.TeamAvailabilityChecks>0,"checks");Eq(0,inner.Calls,"inner calls");}
    static void ReadyGateCancellation(){using(var source=new CancellationTokenSource()){var availability=new FakeAvailability(false){AfterCheck=()=>source.Cancel()};var inner=new FakeInnerWorkflow();var gate=ReadyGate(inner,availability);var r=gate.RunAsync("LDPlayer",new OneShotFarmRequest(),source.Token).GetAwaiter().GetResult();Eq(OneShotFarmOutcome.Cancelled,r.Outcome,"outcome");Eq(1,r.TeamAvailabilityChecks,"checks");Eq(0,inner.Calls,"inner calls");}}
    static void ReadyGateTechnicalFailure(){var availability=new FakeAvailability(false){TechnicalFailure=true};var inner=new FakeInnerWorkflow();var r=ReadyGate(inner,availability).RunAsync("LDPlayer",new OneShotFarmRequest(),default(CancellationToken)).GetAwaiter().GetResult();Eq(OneShotFarmOutcome.TeamAvailabilityCheckFailed,r.Outcome,"outcome");Eq(0,inner.Calls,"inner calls");}
    static void ReadyGateHasNoNone(){string source=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","Infrastructure","Workflows","ReadyTeamOneShotFarmWorkflow.cs"));Is(!source.Contains("CancellationToken"+".None"),"token bypass");}
    static void ProgressCheckingFirst(){var p=new RecordingProgress();var availability=new FakeAvailability(true){BeforeCheck=()=>Eq(OneShotFarmProgressStage.CheckingTeamAvailability,p.Items.Last().Stage,"stage before call")};var r=ReadyGate(new FakeInnerWorkflow(),availability).RunAsync("LDPlayer",new OneShotFarmRequest(),p,default(CancellationToken)).GetAwaiter().GetResult();Eq(OneShotFarmProgressStage.CheckingTeamAvailability,p.Items[0].Stage,"first stage");Eq(1,p.Items[0].TeamAvailabilityChecks,"upcoming check");Eq(1,availability.Calls,"availability calls");}
    static void ProgressWaitingSchedule(){var p=new RecordingProgress();var availability=new FakeAvailability(false,true);var request=new OneShotFarmRequest();ReadyGate(new FakeInnerWorkflow(),availability,new ReadyTeamGateOptions(1,1000)).RunAsync("LDPlayer",request,p,default(CancellationToken)).GetAwaiter().GetResult();var waiting=p.Items.First(x=>x.Stage==OneShotFarmProgressStage.WaitingForReadyTeam);Eq(1,waiting.TeamAvailabilityChecks,"checks");Is(waiting.NextCheckAt.HasValue&&waiting.WaitDeadline.HasValue,"schedule");Is(waiting.NextCheckAt.Value<=waiting.WaitDeadline.Value,"deadline order");}
    static void ProgressAllowedTeams(){var p=new RecordingProgress();var availability=new FakeAvailability();availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team1});availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team4});var request=new OneShotFarmRequest{AllowedTeams=new[]{TeamNumber.Team4,TeamNumber.Team3}};ReadyGate(new FakeInnerWorkflow(),availability,new ReadyTeamGateOptions(1,1000)).RunAsync("LDPlayer",request,p,default(CancellationToken)).GetAwaiter().GetResult();var waiting=p.Items.First(x=>x.Stage==OneShotFarmProgressStage.WaitingForReadyTeam);Is(waiting.ReadyTeams.SequenceEqual(new[]{TeamNumber.Team1}),"ready list");Eq(0,waiting.EligibleReadyTeams.Count,"ineligible list");Is(waiting.AllowedTeams.SequenceEqual(request.AllowedTeams),"allowed list");}
    static void ProgressReadyFound(){var p=new RecordingProgress();var availability=new FakeAvailability();availability.ReadyTeamSequences.Enqueue(new[]{TeamNumber.Team4,TeamNumber.Team1});var request=new OneShotFarmRequest{AllowedTeams=new[]{TeamNumber.Team4,TeamNumber.Team3}};ReadyGate(new FakeInnerWorkflow(),availability).RunAsync("LDPlayer",request,p,default(CancellationToken)).GetAwaiter().GetResult();var ready=p.Items.Single(x=>x.Stage==OneShotFarmProgressStage.ReadyTeamFound);Is(ready.ReadyTeams.SequenceEqual(new[]{TeamNumber.Team4,TeamNumber.Team1}),"ready teams");Is(ready.EligibleReadyTeams.SequenceEqual(new[]{TeamNumber.Team4}),"eligible teams");}
    static void ProgressForwarded(){var p=new RecordingProgress();var inner=new FakeInnerWorkflow();ReadyGate(inner,new FakeAvailability(true)).RunAsync("LDPlayer",new OneShotFarmRequest(),p,default(CancellationToken)).GetAwaiter().GetResult();Is(ReferenceEquals(p,inner.LastProgress),"progress reference");}
    static void ProgressInnerSteps(){var h=new H();var p=new RecordingProgress();var r=h.Workflow.RunAsync("LDPlayer",h.Request,p,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"workflow");Is(p.Items.Any(x=>x.Stage==OneShotFarmProgressStage.PreparingFarm),"preparing");Is(p.Items.Any(x=>x.Stage==OneShotFarmProgressStage.RunningFarmStep&&x.CurrentStep==OneShotFarmStep.EnsureWorldMap),"running step");Eq(OneShotFarmProgressStage.Completed,p.Items.Last().Stage,"terminal");}
    static void ProgressCancellationPrompt(){using(var source=new CancellationTokenSource()){var availability=new FakeAvailability(false);var p=new DelegateProgress(x=>{if(x.Stage==OneShotFarmProgressStage.WaitingForReadyTeam)source.Cancel();});var watch=System.Diagnostics.Stopwatch.StartNew();var r=ReadyGate(new FakeInnerWorkflow(),availability,new ReadyTeamGateOptions(30000,60000)).RunAsync("LDPlayer",new OneShotFarmRequest(),p,source.Token).GetAwaiter().GetResult();watch.Stop();Eq(OneShotFarmOutcome.Cancelled,r.Outcome,"outcome");Is(watch.ElapsedMilliseconds<2000,"cancel was not prompt");Eq(1,availability.Calls,"extra availability check");}}
    static void ProgressNullSupported(){var r=ReadyGate(new FakeInnerWorkflow(),new FakeAvailability(true)).RunAsync("LDPlayer",new OneShotFarmRequest(),null,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"null progress");}
    static void ProgressCountdownNonNegative(){Eq(TimeSpan.Zero,OneShotFarmProgressUtilities.Remaining(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddSeconds(-1)),"past");Is(OneShotFarmProgressUtilities.Remaining(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddSeconds(1))>TimeSpan.Zero,"future");}
    static void ProgressStaleRunRejected(){var oldRun=new object();var currentRun=new object();Is(!OneShotFarmProgressUtilities.IsCurrentRun(1,2,oldRun,currentRun),"generation");Is(!OneShotFarmProgressUtilities.IsCurrentRun(2,2,oldRun,currentRun),"reference");Is(OneShotFarmProgressUtilities.IsCurrentRun(2,2,currentRun,currentRun),"current");}
    static void ProgressCallbackSafe(){var h=new H();var r=h.Workflow.RunAsync("LDPlayer",h.Request,new ThrowingProgress(),default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"progress callback changed workflow outcome");}
    static void ProgressHasNoNone(){foreach(string file in new[]{"ADB/Core/Workflows/OneShotFarmProgress.cs","ADB/Infrastructure/Workflows/ReadyTeamOneShotFarmWorkflow.cs","ADB/Infrastructure/Workflows/OneShotFarmWorkflow.cs","ADB/UI/DeviceDiagnosticWindow.xaml.cs"})Is(!File.ReadAllText(Path.Combine(Environment.CurrentDirectory,file)).Contains("CancellationToken"+".None"),file);}
    static void PreferenceMissingUsesDefaults(){using(var f=new PreferenceFixture()){var d=ValidPreferences();d.Iron=false;var r=f.Store.LoadAsync(d,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success&&r.UsedDefaults,"defaults");Is(!r.Preferences.Iron,"default value");Is(!File.Exists(f.FilePath),"file created on load");}}
    static void PreferenceSafeDefaults(){var p=new FarmUiPreferences();int count=(p.Iron?1:0)+(p.Stone?1:0)+(p.Wood?1:0)+(p.Food?1:0);Is(count>=2,"resource count");Is(FarmUiPreferencesMapper.Validate(p).IsValid,"safe defaults invalid");}
    static void PreferenceResourcesRoundTrip(){using(var f=new PreferenceFixture()){var p=ValidPreferences();p.Iron=false;p.Stone=true;p.Wood=false;p.Food=true;f.Save(p);var a=f.Load().Preferences;Is(!a.Iron&&a.Stone&&!a.Wood&&a.Food,"flags");}}
    static void PreferencePrioritiesRoundTrip(){using(var f=new PreferenceFixture()){var p=ValidPreferences();p.LevelPriority=new[]{7,5,3};p.TeamPriority=new[]{TeamNumber.Team3,TeamNumber.Team4,TeamNumber.Team2};f.Save(p);var a=f.Load().Preferences;Is(a.LevelPriority.SequenceEqual(p.LevelPriority),"levels");Is(a.TeamPriority.SequenceEqual(p.TeamPriority),"teams");}}
    static void PreferenceOptionsRoundTrip(){using(var f=new PreferenceFixture()){var p=ValidPreferences();p.AllowTeam1=true;p.TeamPriority=new[]{TeamNumber.Team4,TeamNumber.Team1};p.ReadyCheckIntervalMinutes=31;p.ReadyMaxWaitHours=7;p.UnoccupiedOnly=false;f.Save(p);var a=f.Load().Preferences;Is(a.AllowTeam1&&!a.UnoccupiedOnly,"switches");Eq(31,a.ReadyCheckIntervalMinutes,"interval");Eq(7,a.ReadyMaxWaitHours,"wait");}}
    static void PreferenceInvalidRecovery(){using(var f=new PreferenceFixture()){Directory.CreateDirectory(Path.GetDirectoryName(f.FilePath));File.WriteAllText(f.FilePath,"{invalid");var r=f.Load();Is(r.Success&&r.UsedDefaults&&r.RecoveredInvalidFile,"recovery");Is(Directory.GetFiles(Path.GetDirectoryName(f.FilePath),"farm-ui-preferences.invalid-*.json").Length==1,"invalid rename");}}
    static void PreferenceAtomicSave(){using(var f=new PreferenceFixture()){var original=ValidPreferences();original.ReadyCheckIntervalMinutes=11;f.Save(original);var failing=new LocalAppDataFarmUiPreferencesStore(new TestPreferencePath(f.FilePath),new Log(),new FailingCommitter());var changed=ValidPreferences();changed.ReadyCheckIntervalMinutes=22;var save=failing.SaveAsync(changed,default(CancellationToken)).GetAwaiter().GetResult();Is(!save.Success,"failure result");Eq(11,f.Load().Preferences.ReadyCheckIntervalMinutes,"main file changed");Is(Directory.GetFiles(Path.GetDirectoryName(f.FilePath),"*.tmp").Length==0,"temp remained");}}
    static void PreferenceResourceValidation(){var p=ValidPreferences();p.Iron=true;p.Stone=p.Wood=p.Food=false;Is(!FarmUiPreferencesMapper.Validate(p).IsValid,"single resource");}
    static void PreferenceLevelValidation(){var p=ValidPreferences();p.LevelPriority=new[]{7,0};Is(!FarmUiPreferencesMapper.Validate(p).IsValid,"outside range");p.LevelPriority=new[]{7,7};Is(!FarmUiPreferencesMapper.Validate(p).IsValid,"duplicate");}
    static void PreferenceTeamValidation(){var p=ValidPreferences();p.TeamPriority=new[]{TeamNumber.Team4,TeamNumber.Team4};Is(!FarmUiPreferencesMapper.Validate(p).IsValid,"duplicate");p.TeamPriority=new[]{TeamNumber.Team4,TeamNumber.Team1};p.AllowTeam1=false;Is(!FarmUiPreferencesMapper.Validate(p).IsValid,"team1 mismatch");}
    static void PreferenceWaitValidation(){var p=ValidPreferences();p.ReadyCheckIntervalMinutes=1441;Is(!FarmUiPreferencesMapper.Validate(p).IsValid,"interval range");p.ReadyCheckIntervalMinutes=121;p.ReadyMaxWaitHours=2;Is(!FarmUiPreferencesMapper.Validate(p).IsValid,"wait shorter");}
    static void PreferenceOverridesDefaults(){using(var f=new PreferenceFixture()){var user=ValidPreferences();user.LevelPriority=new[]{6,4};f.Save(user);var defaults=ValidPreferences();defaults.LevelPriority=new[]{7,6,5};var r=f.Store.LoadAsync(defaults,default(CancellationToken)).GetAwaiter().GetResult();Is(!r.UsedDefaults&&r.Preferences.LevelPriority.SequenceEqual(new[]{6,4}),"override");}}
    static void PreferenceSaveFailureInMemory(){var p=ValidPreferences();var request=FarmUiPreferencesMapper.CreateRequest(p,new OneShotFarmRequest());using(var f=new PreferenceFixture()){var store=new LocalAppDataFarmUiPreferencesStore(new TestPreferencePath(f.FilePath),new Log(),new FailingCommitter());Is(!store.SaveAsync(p,default(CancellationToken)).GetAwaiter().GetResult().Success,"save");Is(request.ResourceLevelPriority.SequenceEqual(p.LevelPriority),"request unavailable");}}
    static void PreferenceRequestMapping(){var p=ValidPreferences();p.Iron=false;p.LevelPriority=new[]{6,4};p.TeamPriority=new[]{TeamNumber.Team3,TeamNumber.Team2};p.AllowTeam1=false;p.UnoccupiedOnly=false;p.ReadyCheckIntervalMinutes=10;p.ReadyMaxWaitHours=3;var r=FarmUiPreferencesMapper.CreateRequest(p,new OneShotFarmRequest());Is(r.SelectedResources.SequenceEqual(new[]{ResourceType.Stone,ResourceType.Wood,ResourceType.Food}),"resources");Is(r.ResourceLevelPriority.SequenceEqual(new[]{6,4})&&r.TargetLevel==6,"levels");Is(r.TeamPriority.SequenceEqual(p.TeamPriority)&&r.AllowedTeams.SequenceEqual(p.TeamPriority),"teams");Is(!r.AllowTeam1&&!r.UnoccupiedOnly,"switches");Eq(600000,r.ReadyTeamOptions.CheckIntervalMs,"interval");Eq(10800000,r.ReadyTeamOptions.MaxWaitMs,"wait");}
    static void PreferenceShuffleNotPersisted(){using(var f=new PreferenceFixture()){var p=ValidPreferences();f.Save(p);var request=FarmUiPreferencesMapper.CreateRequest(p,new OneShotFarmRequest());request.ResourcePriority=new[]{ResourceType.Food,ResourceType.Wood,ResourceType.Stone,ResourceType.Iron};var loaded=f.Load().Preferences;Is(loaded.Iron&&loaded.Stone&&loaded.Wood&&loaded.Food,"selection changed");Is(loaded.LevelPriority.SequenceEqual(p.LevelPriority),"preference changed");}}
    static void PreferenceRunOptionsIsolated(){var shared=new ReadyTeamGateOptions(900000,43200000);var a=ValidPreferences();a.ReadyCheckIntervalMinutes=5;a.ReadyMaxWaitHours=2;var b=ValidPreferences();b.ReadyCheckIntervalMinutes=20;b.ReadyMaxWaitHours=4;var ra=FarmUiPreferencesMapper.CreateRequest(a,new OneShotFarmRequest());var rb=FarmUiPreferencesMapper.CreateRequest(b,new OneShotFarmRequest());Eq(300000,ra.ReadyTeamOptions.CheckIntervalMs,"run a");Eq(1200000,rb.ReadyTeamOptions.CheckIntervalMs,"run b");Eq(900000,shared.CheckIntervalMs,"shared interval");Eq(43200000,shared.MaxWaitMs,"shared wait");var request=new OneShotFarmRequest{ReadyTeamOptions=new ReadyTeamGateRunOptions(1,100)};var watch=System.Diagnostics.Stopwatch.StartNew();var result=ReadyGate(new FakeInnerWorkflow(),new FakeAvailability(false,true),shared).RunAsync("LDPlayer",request,default(CancellationToken)).GetAwaiter().GetResult();watch.Stop();Is(result.Success&&watch.ElapsedMilliseconds<2000,"per-run override ignored");Eq(900000,shared.CheckIntervalMs,"shared mutated");}
    static void PreferenceResetDefaults(){using(var f=new PreferenceFixture()){var user=ValidPreferences();user.Wood=false;f.Save(user);f.Store.ResetAsync(default(CancellationToken)).GetAwaiter().GetResult();var defaults=ValidPreferences();defaults.Wood=true;var r=f.Store.LoadAsync(defaults,default(CancellationToken)).GetAwaiter().GetResult();Is(r.UsedDefaults&&r.Preferences.Wood,"reset");}}
    static void PreferenceHasNoNone(){foreach(string file in new[]{"ADB/Core/Workflows/FarmUiPreferences.cs","ADB/Core/Workflows/ReadyTeamGateRunOptions.cs","ADB/Infrastructure/Workflows/LocalAppDataFarmUiPreferencesStore.cs","ADB/UI/DeviceDiagnosticWindow.xaml.cs"})Is(!File.ReadAllText(Path.Combine(Environment.CurrentDirectory,file)).Contains("CancellationToken"+".None"),file);}
    static FarmUiPreferences ValidPreferences()=>new FarmUiPreferences{Iron=true,Stone=true,Wood=true,Food=true,LevelPriority=new[]{7,6,5},TeamPriority=new[]{TeamNumber.Team4,TeamNumber.Team3,TeamNumber.Team2},AllowTeam1=false,ReadyCheckIntervalMinutes=15,ReadyMaxWaitHours=12,UnoccupiedOnly=true};
    static ReadyTeamOneShotFarmWorkflow ReadyGate(FakeInnerWorkflow inner,FakeAvailability availability,ReadyTeamGateOptions options=null)=>new ReadyTeamOneShotFarmWorkflow(inner,availability,options??new ReadyTeamGateOptions(1),new Log());
    static void AvailabilityUsesRosterRoi(){var f=new AvailabilityFixture();f.Matcher.ReadyRows.Add(4);var r=f.Service.CheckAsync("LDPlayer",default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success&&r.AnyReadyTeam,"ready not found");Eq(2,f.Client.Captures,"captures");Eq(16,f.Matcher.Regions.Count,"badge and ready scans");for(int frame=0;frame<2;frame++)for(int scan=0;scan<2;scan++)for(int i=0;i<4;i++){var region=f.Matcher.Regions[(frame*8)+(scan*4)+i].Value;Eq(298+(i*52),region.Y,"wrong row top "+i);Eq(52,region.Height,"wrong row height "+i);Eq(150,region.Width,"wrong row width "+i);}}
    static void AvailabilityMapsReadyTeams(){var f=new AvailabilityFixture();f.Matcher.ReadyRows.UnionWith(new[]{2,4});var r=f.Service.CheckAsync("LDPlayer",default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success&&r.AnyReadyTeam,"ready not found");Is(r.ReadyTeams.SequenceEqual(new[]{TeamNumber.Team2,TeamNumber.Team4}),"team mapping");Eq(2,r.ReadyMatches.Count,"match count");}
    static void AvailabilityAcceptsThreeTeamBadgeVariation(){var f=new AvailabilityFixture();f.Matcher.PresentRows.IntersectWith(new[]{2,3});f.Matcher.ReadyRows.Add(3);f.Matcher.SecondFrameReadyRows.UnionWith(new[]{2,3});var r=f.Service.CheckAsync("LDPlayer",default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success&&r.AnyReadyTeam,"detected three-team roster should be checked");Is(r.AvailableTeams.SequenceEqual(new[]{TeamNumber.Team1,TeamNumber.Team2,TeamNumber.Team3}),"three-team rows");Is(r.ReadyTeams.SequenceEqual(new[]{TeamNumber.Team2,TeamNumber.Team3}),"three-team ready mapping");}
    static void AvailabilityDetectsVariableTeamCounts(){foreach(int count in new[]{2,3,4}){var f=new AvailabilityFixture();f.Matcher.PresentRows.RemoveWhere(row=>row>count);f.Matcher.ReadyRows.UnionWith(Enumerable.Range(1,count));var r=f.Service.CheckAsync("LDPlayer",default(CancellationToken)).GetAwaiter().GetResult();var expected=Enumerable.Range(1,count).Select(number=>(TeamNumber)number).ToArray();Is(r.Success,"availability failed for "+count);Is(r.AvailableTeams.SequenceEqual(expected),"detected count "+count);Is(r.ReadyTeams.SequenceEqual(expected),"absent team reported ready for "+count);Eq(16,f.Matcher.Regions.Count,"unexpected scans for "+count);}}
    static void AvailabilityInfersRosterFromReadyRows(){var f=new AvailabilityFixture();f.Matcher.PresentRows.Clear();f.Matcher.ReadyRows.UnionWith(new[]{2,3});var r=f.Service.CheckAsync("LDPlayer",default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success&&r.AnyReadyTeam,"ready row fallback failed");Is(r.AvailableTeams.SequenceEqual(new[]{TeamNumber.Team1,TeamNumber.Team2,TeamNumber.Team3}),"contiguous roster was not inferred");Is(r.ReadyTeams.SequenceEqual(new[]{TeamNumber.Team2,TeamNumber.Team3}),"ready rows were mapped incorrectly");}
    static void AvailabilityNoReady(){var f=new AvailabilityFixture();var r=f.Service.CheckAsync("LDPlayer",default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success&&!r.AnyReadyTeam,"busy roster treated as failure or ready");Eq(0,f.Client.Inputs,"input");}
    static void AvailabilityMissingTemplate(){var f=new AvailabilityFixture();f.Registry.Missing.Add(TemplateId.WorldMapTeamReadyAnchor);var r=f.Service.CheckAsync("LDPlayer",default(CancellationToken)).GetAwaiter().GetResult();Is(!r.Success&&!r.AnyReadyTeam,"missing template accepted");Eq(0,f.Nav.EnsureCalls,"navigation");Eq(0,f.Client.Captures,"capture");}
    static void AvailabilityHasNoNone(){string source=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","Infrastructure","TeamSelection","WorldMapTeamAvailabilityService.cs"));Is(!source.Contains("CancellationToken"+".None"),"token bypass");}
    static void OneShotUiHasStop(){string root=Path.Combine(Environment.CurrentDirectory,"ADB","UI");string xaml=File.ReadAllText(Path.Combine(root,"DeviceDiagnosticWindow.xaml"));string code=File.ReadAllText(Path.Combine(root,"DeviceDiagnosticWindow.xaml.cs"));Is(xaml.Contains("StopOneShotFarmButton")&&xaml.Contains("StopOneShotFarm_Click"),"Stop button missing");Is(code.Contains("CreateLinkedTokenSource")&&code.Contains("oneShotFarmCancellation")&&code.Contains("currentRun.Cancel()"),"per-run cancellation missing");Is(!code.Substring(code.IndexOf("private async void RunOneShotFarm_Click"),code.IndexOf("private void ApplyFarmPreferences")-code.IndexOf("private async void RunOneShotFarm_Click")).Contains("RunOperationAsync"),"one-shot still disables the whole window");}
    static void OneShotUiIsFocused(){string xaml=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","UI","DeviceDiagnosticWindow.xaml"));foreach(string hidden in new[]{"Tap Test","Swipe Test","Check Device","Launch Game","Capture Screenshot","Detect Current State","Ensure World Map","Open Search Panel","Configure Search","Search Iron","Verify Resource Popup","Open Team Selection","Select Farm Team","Dispatch Selected Team","Back Test","Text=\"Package name\"","Text=\"State name\"","Text=\"Note\"","Chỉ mục tiêu chưa có người khai thác"})Is(!xaml.Contains(hidden),"manual control remains: "+hidden);Is(xaml.Contains("RunOneShotFarmButton")&&xaml.Contains("StopOneShotFarmButton"),"farm controls missing");Is(xaml.Contains("Header=\"Thiết lập nâng cao\"")&&xaml.Contains("FarmProgressItemsControl")&&xaml.Contains("Chi tiết lần chạy gần nhất"),"compact operations layout missing");}
    static OneShotFarmWorkflow PlanWorkflow(H h,FakePlan plan,FakeRegistry registry=null,FakeRandom random=null)=>new OneShotFarmWorkflow(h.Nav,new FakeFallback(h.Config,h.Search),h.Popup,h.Open,h.Select,h.Dispatch,h.Detector,h.Lock,new OneShotFarmWorkflowOptions(true,true,"Diagnostics/OneShotFarm"),h.Diag,new Log(),new ResourceFarmFallbackOptions(),plan,registry==null?null:new FakeProfiles(),registry,random);

    sealed class H
    {
        public FakeNav Nav=new FakeNav();public FakeConfig Config=new FakeConfig();public FakeSearch Search=new FakeSearch();public FakePopup Popup=new FakePopup();public FakeOpen Open=new FakeOpen();public FakeSelect Select=new FakeSelect();public FakeDispatch Dispatch=new FakeDispatch();public FakeDetector Detector=new FakeDetector();public RecordingLock Lock=new RecordingLock();public FakeDiag Diag=new FakeDiag();public OneShotFarmRequest Request=new OneShotFarmRequest();public OneShotFarmWorkflow Workflow;
        public H(){Workflow=new OneShotFarmWorkflow(Nav,new FakeFallback(Config,Search),Popup,Open,Select,Dispatch,Detector,Lock,new OneShotFarmWorkflowOptions(true,true,"Diagnostics/OneShotFarm"),Diag,new Log());}
        public int Total=>Nav.EnsureCalls+Nav.PanelCalls+Config.Calls+Search.Calls+Popup.Calls+Open.Calls+Select.Calls+Dispatch.Calls;
    }
    sealed class FakeNav:IWorldMapNavigationService{public int EnsureCalls,PanelCalls,RepositionCalls;public bool EnsureSuccess=true,PanelSuccess=true,RepositionSuccess=true,UseFallbackEvidence;public Action AfterEnsure;public Task<NavigationResult> EnsureWorldMapAsync(string d,CancellationToken t){EnsureCalls++;AfterEnsure?.Invoke();return Task.FromResult(new NavigationResult{Success=EnsureSuccess,FinalState=GameState.WorldMap,Message="world"});}public Task<NavigationResult> OpenResourceSearchPanelAsync(string d,CancellationToken t){PanelCalls++;GameDetectionEvidence[] evidence=UseFallbackEvidence?new[]{new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,Found=true},new GameDetectionEvidence{TemplateId=TemplateId.LevelMinusButton,Found=true},new GameDetectionEvidence{TemplateId=TemplateId.ResourceSearchPanelAnchor,Found=false}}:new[]{new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,Found=true},new GameDetectionEvidence{TemplateId=TemplateId.ResourceSearchPanelAnchor,Found=true}};return Task.FromResult(new NavigationResult{Success=PanelSuccess,FinalState=PanelSuccess?GameState.ResourceSearchPanel:GameState.WorldMap,Message="panel",FinalEvidence=PanelSuccess?evidence:new GameDetectionEvidence[0]});}public Task<NavigationResult> RepositionToAllianceTerritoryAsync(string d,CancellationToken t){RepositionCalls++;return Task.FromResult(new NavigationResult{Success=RepositionSuccess,FinalState=RepositionSuccess?GameState.WorldMap:GameState.ContinentMap,Message=RepositionSuccess?"repositioned":"failed",FinalEvidence=new GameDetectionEvidence[0],Transitions=new NavigationTransition[0]});}}
    sealed class FakeConfig:IResourceSearchConfigurationService{public int Calls;public bool Success=true;public ResourceSearchConfigurationRequest Last;public Task<ResourceSearchConfigurationResult> ConfigureAsync(string d,ResourceSearchConfigurationRequest r,CancellationToken t){Calls++;Last=r;return Task.FromResult(new ResourceSearchConfigurationResult{Success=Success,ResourceVerified=Success,LevelVerified=Success,FilterVerified=Success,FinalState=GameState.ResourceSearchPanel,Message="config"});}}
    sealed class FakeSearch:IResourceSearchExecutionService{public int Calls;public bool Success=true;public ResourceSearchOutcome Outcome=ResourceSearchOutcome.ResourceLocated;public Queue<ResourceSearchOutcome> Outcomes=new Queue<ResourceSearchOutcome>();public ResourceSearchExecutionRequest Last;public Task<ResourceSearchExecutionResult> ExecuteAsync(string d,ResourceSearchExecutionRequest r,CancellationToken t){Calls++;Last=r;ResourceSearchOutcome o=Outcomes.Count>0?Outcomes.Dequeue():Outcome;return Task.FromResult(new ResourceSearchExecutionResult{Success=Success&&o==ResourceSearchOutcome.ResourceLocated,Outcome=o,FinalState=o==ResourceSearchOutcome.ResourceLocated?GameState.ResourcePopup:GameState.ResourceSearchPanel,Message="search"});}}
    sealed class FakeFallback:IResourceLevelFallbackService{readonly FakeConfig c;readonly FakeSearch s;public FakeFallback(FakeConfig c,FakeSearch s){this.c=c;this.s=s;}public async Task<ResourceLevelFallbackResult> SearchAsync(string d,ResourceType type,ResourceLevelFallbackPolicy p,bool u,CancellationToken t){var a=new List<ResourceLevelAttemptResult>();foreach(int level in p.Levels){for(int n=1;n<=p.AttemptsPerLevel;n++){t.ThrowIfCancellationRequested();var q=new ResourceSearchConfigurationRequest{ResourceType=type,TargetLevel=level,UnoccupiedOnly=u};var cr=await c.ConfigureAsync(d,q,t);var ar=new ResourceLevelAttemptResult{Level=level,AttemptNumber=n,ConfigurationResult=cr,ConfigurationSucceeded=cr.Success&&cr.ResourceVerified&&cr.LevelVerified&&cr.FilterVerified};a.Add(ar);if(!ar.ConfigurationSucceeded)return R(type,ResourceLevelFallbackOutcome.ConfigurationFailed,a,null,cr.Message,cr.ErrorMessage);var sr=await s.ExecuteAsync(d,new ResourceSearchExecutionRequest{Configuration=q,ConfigureBeforeSearch=false},t);ar.SearchResult=sr;ar.SearchOutcome=sr.Outcome;if(sr.Outcome==ResourceSearchOutcome.ResourceLocated)return R(type,ResourceLevelFallbackOutcome.ResourceLocated,a,level,"located",null);if(sr.Outcome!=ResourceSearchOutcome.ResourceNotFound)return R(type,ResourceLevelFallbackOutcome.SearchFailed,a,null,sr.Message,sr.ErrorMessage);}}return R(type,ResourceLevelFallbackOutcome.ResourceLevelsExhausted,a,null,"exhausted",null);}static ResourceLevelFallbackResult R(ResourceType type,ResourceLevelFallbackOutcome o,List<ResourceLevelAttemptResult>a,int?l,string m,string e)=>new ResourceLevelFallbackResult{Outcome=o,Success=o==ResourceLevelFallbackOutcome.ResourceLocated,ResourceType=type,LocatedLevel=l,LastAttemptedLevel=a.LastOrDefault()?.Level,RequestedLevels=new[]{7,6,5},Attempts=a,InitialState=GameState.ResourceSearchPanel,FinalState=o==ResourceLevelFallbackOutcome.ResourceLocated?GameState.ResourcePopup:GameState.ResourceSearchPanel,Message=m,ErrorMessage=e};}
    sealed class FakePopup:IResourceAwarePopupVerificationService{public int Calls;public bool Ready=true;public Task<ResourcePopupVerificationResult> VerifyAsync(string d,CancellationToken t)=>VerifyAsync(d,ResourceType.Iron,t);public Task<ResourcePopupVerificationResult> VerifyAsync(string d,ResourceType resource,CancellationToken t){Calls++;return Task.FromResult(new ResourcePopupVerificationResult{Success=Ready,Outcome=Ready?ResourcePopupOutcome.ResourcePopupReady:ResourcePopupOutcome.ResourcePopupMismatch,PopupAnchorVerified=Ready,IronResourceVerified=Ready,ResourceVerified=Ready,ResourceType=resource,ExpectedResource=resource,ExpectedResourceVerified=Ready,ExpectedPopupTitleTemplate=FakeProfiles.Profile(resource).PopupTitleTemplate,GatherButtonVerified=Ready,FinalState=GameState.ResourcePopup,Message="popup"});}}
    sealed class FakeProfiles:IResourceTemplateProfileProvider{public ResourceType? Unsupported;public ResourceTemplateProfile Get(ResourceType r)=>Profile(r);public bool IsSupported(ResourceType r)=>Unsupported!=r;public string GetUnsupportedReason(ResourceType r)=>$"TemplateId '{Profile(r).PopupTitleTemplate}' is missing at 'Data/missing.png'.";public static ResourceTemplateProfile Profile(ResourceType r){switch(r){case ResourceType.Iron:return new ResourceTemplateProfile{ResourceType=r,SelectedTemplate=TemplateId.ResourceIronSelected,UnselectedTemplate=TemplateId.ResourceIronUnselected,PopupTitleTemplate=TemplateId.ResourcePopupIronTitle,DisplayName="Sắt"};case ResourceType.Stone:return new ResourceTemplateProfile{ResourceType=r,SelectedTemplate=TemplateId.ResourceStoneSelected,UnselectedTemplate=TemplateId.ResourceStoneUnselected,PopupTitleTemplate=TemplateId.ResourcePopupStoneTitle,DisplayName="Mỏ Đá"};case ResourceType.Wood:return new ResourceTemplateProfile{ResourceType=r,SelectedTemplate=TemplateId.ResourceWoodSelected,UnselectedTemplate=TemplateId.ResourceWoodUnselected,PopupTitleTemplate=TemplateId.ResourcePopupWoodTitle,DisplayName="Rừng"};default:return new ResourceTemplateProfile{ResourceType=r,SelectedTemplate=TemplateId.ResourceFoodSelected,UnselectedTemplate=TemplateId.ResourceFoodUnselected,PopupTitleTemplate=TemplateId.ResourcePopupFoodTitle,DisplayName="Đất nông nghiệp"};}}}
    sealed class FakeOpen:IOpenTeamSelectionService{public int Calls;public bool Ready=true;public Task<OpenTeamSelectionResult> OpenAsync(string d,CancellationToken t){Calls++;return Task.FromResult(new OpenTeamSelectionResult{Success=Ready,Outcome=Ready?OpenTeamSelectionOutcome.TeamSelectionOpened:OpenTeamSelectionOutcome.TeamSelectionOpenedButNotReady,FinalState=GameState.TeamSelection,TeamSelectionVerified=true,TeamSelectionReady=Ready,Message="open"});}}
    sealed class FakeSelect:ISelectFarmTeamService{public int Calls;public bool Success=true;public TeamNumber Team=TeamNumber.Team4;public SelectFarmTeamOutcome Outcome=SelectFarmTeamOutcome.TeamSelected;public Task<SelectFarmTeamResult> SelectAsync(string d,TeamSelectionRequest r,CancellationToken t){Calls++;return Task.FromResult(new SelectFarmTeamResult{Success=Success,Outcome=Outcome,SelectedTeam=Outcome==SelectFarmTeamOutcome.NoEligibleTeam?(TeamNumber?)null:Team,SelectedStateVerified=Success,FinalState=GameState.TeamSelection,Message="select"});}}
    sealed class FakeDispatch:IDispatchSelectedTeamService{public int Calls;public bool Verified=true;public DispatchMarchOutcome Outcome=DispatchMarchOutcome.MarchStarted;public Queue<DispatchMarchOutcome> Outcomes=new Queue<DispatchMarchOutcome>();public DispatchMarchRequest Last;public Task<DispatchMarchResult> DispatchAsync(string d,DispatchMarchRequest r,CancellationToken t){Calls++;Last=r;var outcome=Outcomes.Count>0?Outcomes.Dequeue():Outcome;bool storage=outcome==DispatchMarchOutcome.StorageLimitResourceSwitchRequired;bool expiry=outcome==DispatchMarchOutcome.ResourceExpiryResourceSwitchRequired;bool resourceSwitch=storage||expiry;return Task.FromResult(new DispatchMarchResult{Success=!resourceSwitch&&Verified,Outcome=outcome,MarchStartedVerified=!resourceSwitch&&Verified,DispatchedTeam=resourceSwitch?(TeamNumber?)null:r.ExpectedTeam,StorageLimitDialogDetected=storage,StorageLimitCancelled=storage,ResourceExpiryDialogDetected=expiry,ResourceExpiryCancelled=expiry,ResourceSwitchRequired=resourceSwitch,StorageFullResource=storage?(ResourceType?)r.CurrentResource:null,StorageLimitResult=resourceSwitch?new StorageLimitDialogResult{ReturnedToWorldMap=true,FinalState=GameState.WorldMap,RecoveryTransitions=1}:null,FinalState=GameState.WorldMap,Message="dispatch"});}}
    sealed class FakeDetector:IGameStateDetector{public GameState Initial=GameState.WorldMap;int calls;public Task<GameDetectionResult> DetectAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();calls++;GameState state=calls==1?Initial:GameState.WorldMap;GameDetectionEvidence[] evidence=state==GameState.ResourceSearchPanel?new[]{new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,Found=true},new GameDetectionEvidence{TemplateId=TemplateId.LevelMinusButton,Found=true}}:new GameDetectionEvidence[0];return Task.FromResult(new GameDetectionResult{State=state,IsSuccessful=true,Evidence=evidence});}public GameDetectionResult Detect(byte[] p)=>null;}
    sealed class RecordingLock:IDeviceOperationLock{public int Releases;public bool Active;public async Task<T> RunAsync<T>(string d,Func<CancellationToken,Task<T>> o,CancellationToken t){t.ThrowIfCancellationRequested();Active=true;try{return await o(t);}finally{Active=false;Releases++;}}}
    sealed class FakePlan:IResourceFarmFallbackService{public int Calls;public Action DuringRun;public IReadOnlyList<ResourceType> LastPriority;public Task<ResourceFarmFallbackResult> RunAsync(string d,OneShotFarmRequest r,GameState s,CancellationToken t){t.ThrowIfCancellationRequested();Calls++;LastPriority=r.ResourcePriority.ToArray();DuringRun?.Invoke();return Task.FromResult(new ResourceFarmFallbackResult{Outcome=ResourceFarmFallbackOutcome.MarchStarted,Success=true,RequestedResources=r.ResourcePriority,AttemptedResources=new[]{r.ResourcePriority[0]},StorageFullResources=new ResourceType[0],LevelsExhaustedResources=new ResourceType[0],LocatedResource=r.ResourcePriority[0],LocatedLevel=7,DispatchedResource=r.ResourcePriority[0],DispatchedTeam=TeamNumber.Team4,Attempts=new ResourceFarmAttemptResult[0],InitialState=s,FinalState=GameState.WorldMap,Message="march"});}}
    sealed class FakeRandom:IRandomProvider{readonly Queue<int> values;public int Calls;public FakeRandom(params int[] values){this.values=new Queue<int>(values);}public int Next(int maxExclusive){Calls++;return values.Count==0?0:values.Dequeue();}}
    sealed class FakeRegistry:ITemplateRegistry{public readonly HashSet<TemplateId> Missing=new HashSet<TemplateId>();public TemplateDefinition GetDefinition(TemplateId id)=>new TemplateDefinition(id,id+".png",.8);public string GetPath(TemplateId id)=>Path.Combine("Data","InfinityKingdom",id+".png");public byte[] LoadBytes(TemplateId id)=>new[]{(byte)id};public bool Exists(TemplateId id)=>!Missing.Contains(id);}
    sealed class FakeDiag:IOneShotFarmDiagnosticService{public bool Throw;public Task<string> CaptureAsync(string d,OneShotFarmStep s,OneShotFarmOutcome o,CancellationToken t){if(Throw)throw new Exception("disk");return Task.FromResult("diag.png");}}
    sealed class RecordingProgress:IProgress<OneShotFarmProgress>{public readonly List<OneShotFarmProgress> Items=new List<OneShotFarmProgress>();public void Report(OneShotFarmProgress value){Items.Add(value);}}
    sealed class DelegateProgress:IProgress<OneShotFarmProgress>{readonly Action<OneShotFarmProgress> action;public DelegateProgress(Action<OneShotFarmProgress> action){this.action=action;}public void Report(OneShotFarmProgress value){action(value);}}
    sealed class ThrowingProgress:IProgress<OneShotFarmProgress>{public void Report(OneShotFarmProgress value){throw new InvalidOperationException("closed UI");}}
    sealed class TestPreferencePath:IFarmUiPreferencesPathProvider{readonly string path;public TestPreferencePath(string path){this.path=path;}public string GetPath()=>path;}
    sealed class TestCheckpointPath:IContinuousFarmCheckpointPathProvider{readonly string path;public TestCheckpointPath(string path){this.path=path;}public string GetDirectory()=>path;}
    sealed class MemoryCheckpointStore:IContinuousFarmCheckpointStore
    {
        public ContinuousFarmCheckpoint Current;public int Saves;
        public MemoryCheckpointStore(ContinuousFarmCheckpoint current=null){Current=current;}
        public ContinuousFarmCheckpoint Load(string deviceName,CancellationToken token){token.ThrowIfCancellationRequested();return Current;}
        public void Save(ContinuousFarmCheckpoint checkpoint,CancellationToken token){token.ThrowIfCancellationRequested();Saves++;Current=checkpoint;}
    }
    sealed class FailingCommitter:IFarmUiPreferencesFileCommitter{public void Commit(string temporaryPath,string destinationPath){throw new IOException("commit failed");}}
    sealed class PreferenceFixture:IDisposable{public readonly string Root;public readonly string FilePath;public readonly LocalAppDataFarmUiPreferencesStore Store;public PreferenceFixture(){Root=Path.Combine(Path.GetTempPath(),"IKAutomation.Tests",Guid.NewGuid().ToString("N"));FilePath=Path.Combine(Root,"farm-ui-preferences.json");Store=new LocalAppDataFarmUiPreferencesStore(new TestPreferencePath(FilePath),new Log());}public void Save(FarmUiPreferences p){Is(Store.SaveAsync(p,default(CancellationToken)).GetAwaiter().GetResult().Success,"save");}public FarmUiPreferencesLoadResult Load()=>Store.LoadAsync(ValidPreferences(),default(CancellationToken)).GetAwaiter().GetResult();public void Dispose(){try{if(Directory.Exists(Root))Directory.Delete(Root,true);}catch{}}}
    sealed class FakeAvailability:IWorldMapTeamAvailabilityService{readonly Queue<bool> values;public int Calls;public readonly Queue<IReadOnlyList<TeamNumber>> ReadyTeamSequences=new Queue<IReadOnlyList<TeamNumber>>();public IReadOnlyList<TeamNumber> AvailableTeams=new[]{TeamNumber.Team1,TeamNumber.Team2,TeamNumber.Team3,TeamNumber.Team4};public TeamNumber ReadyTeam=TeamNumber.Team4;public bool TechnicalFailure;public Action BeforeCheck;public Action AfterCheck;public FakeAvailability(params bool[] values){this.values=new Queue<bool>(values);}public Task<WorldMapTeamAvailabilityResult> CheckAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();BeforeCheck?.Invoke();Calls++;IReadOnlyList<TeamNumber> teams=ReadyTeamSequences.Count>0?ReadyTeamSequences.Dequeue():(values.Count>0&&values.Dequeue()?new[]{ReadyTeam}:new TeamNumber[0]);AfterCheck?.Invoke();return Task.FromResult(new WorldMapTeamAvailabilityResult{Success=!TechnicalFailure,AnyReadyTeam=teams.Count>0,AvailableTeams=AvailableTeams,ReadyTeams=teams,ReadyMatches=new ImageMatchResult[0],FinalState=GameState.WorldMap,Message=TechnicalFailure?"failed":teams.Count>0?"ready":"busy",ErrorMessage=TechnicalFailure?"detector error":null});}}
    sealed class FakeInnerWorkflow:IOneShotFarmWorkflow{public int Calls;public IProgress<OneShotFarmProgress> LastProgress;public readonly List<OneShotFarmRequest> Requests=new List<OneShotFarmRequest>();public readonly Queue<bool> Successes=new Queue<bool>();public Task<OneShotFarmResult> RunAsync(string d,OneShotFarmRequest r,CancellationToken t)=>RunAsync(d,r,null,t);public Task<OneShotFarmResult> RunAsync(string d,OneShotFarmRequest r,IProgress<OneShotFarmProgress> p,CancellationToken t){t.ThrowIfCancellationRequested();Calls++;LastProgress=p;Requests.Add(r);bool success=Successes.Count==0||Successes.Dequeue();TeamNumber team=r.TeamPriority[0];return Task.FromResult(new OneShotFarmResult{Outcome=success?OneShotFarmOutcome.MarchStarted:OneShotFarmOutcome.TeamDispatchFailed,Success=success,DeviceName=d,RequestedResource=r.ResourceType,RequestedLevel=r.TargetLevel,AttemptedLevels=new int[0],AttemptedResources=new ResourceType[0],SelectedResources=r.SelectedResources,ShuffledResourcePriority=r.ResourcePriority,StorageFullResources=new ResourceType[0],LevelsExhaustedResources=new ResourceType[0],LocatedResource=success?(ResourceType?)r.ResourcePriority[0]:null,DispatchedResource=success?(ResourceType?)r.ResourcePriority[0]:null,SelectedTeam=success?(TeamNumber?)team:null,DispatchedTeam=success?(TeamNumber?)team:null,Steps=new OneShotFarmStepResult[0],LastCompletedStep=success?OneShotFarmStep.Completed:OneShotFarmStep.DispatchTeam});}}
    sealed class AvailabilityFixture{public FakeNav Nav=new FakeNav();public AvailabilityDetector Detector=new AvailabilityDetector();public AvailabilityClient Client=new AvailabilityClient();public FakeRegistry Registry=new FakeRegistry();public AvailabilityMatcher Matcher=new AvailabilityMatcher();public WorldMapTeamAvailabilityService Service;public AvailabilityFixture(){Service=new WorldMapTeamAvailabilityService(Nav,Detector,Client,Registry,Matcher,new RecordingLock(),new WorldMapTeamAvailabilityOptions(new ImageRegion(0,290,150,280)),new Log());}}
    sealed class AvailabilityDetector:IGameStateDetector{public GameState State=GameState.WorldMap;public Task<GameDetectionResult> DetectAsync(string d,CancellationToken t)=>Task.FromResult(Result());public GameDetectionResult Detect(byte[] p)=>Result();GameDetectionResult Result()=>new GameDetectionResult{State=State,IsSuccessful=State!=GameState.Unknown,Evidence=new GameDetectionEvidence[0]};}
    sealed class AvailabilityMatcher:IImageMatcher{public readonly HashSet<int> PresentRows=new HashSet<int>(new[]{1,2,3,4});public readonly HashSet<int> ReadyRows=new HashSet<int>();public readonly HashSet<int> SecondFrameReadyRows=new HashSet<int>();public readonly List<ImageRegion?> Regions=new List<ImageRegion?>();public ImageMatchResult Find(byte[] s,byte[] t,ImageRegion? r=null){Regions.Add(r);if(!r.HasValue||t==null||t.Length==0)return ImageMatchResult.NotFound();TemplateId id=(TemplateId)t[0];int badgeRow=id==TemplateId.Team1Badge?1:id==TemplateId.Team2Badge?2:id==TemplateId.Team3Badge?3:id==TemplateId.Team4Badge?4:0;if(badgeRow>0)return PresentRows.Contains(badgeRow)?ImageMatchResult.FoundAt(8,306+((badgeRow-1)*52),20,20):ImageMatchResult.NotFound();int readyRow=((r.Value.Y-298)/52)+1;bool secondFrame=Regions.Count>8;bool found=id==TemplateId.WorldMapTeamReadyAnchor&&(ReadyRows.Contains(readyRow)||(secondFrame&&SecondFrameReadyRows.Contains(readyRow)));return found?ImageMatchResult.FoundAt(70,r.Value.Y+10,20,20):ImageMatchResult.NotFound();}}
    sealed class AvailabilityClient:ILdPlayerClient{public int Captures,Inputs;public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();Captures++;return Task.FromResult(new byte[]{1});}public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult<IReadOnlyList<string>>(new[]{"LDPlayer"});public Task<bool> IsRunningAsync(string d,CancellationToken t)=>Task.FromResult(true);public Task OpenAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task CloseAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task RunAppAsync(string d,string p,CancellationToken t)=>Task.CompletedTask;public Task TapAsync(string d,int x,int y,CancellationToken t){Inputs++;return Task.CompletedTask;}public Task TapByPercentAsync(string d,double x,double y,CancellationToken t){Inputs++;return Task.CompletedTask;}public Task LongPressAsync(string d,int x,int y,int ms,CancellationToken t){Inputs++;return Task.CompletedTask;}public Task SwipeByPercentAsync(string d,double sx,double sy,double ex,double ey,int ms,CancellationToken t){Inputs++;return Task.CompletedTask;}public Task BackAsync(string d,CancellationToken t){Inputs++;return Task.CompletedTask;}public Task InputTextAsync(string d,string v,CancellationToken t){Inputs++;return Task.CompletedTask;}public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t){Inputs++;return Task.CompletedTask;}}
    sealed class InlineProgress<T>:IProgress<T>{readonly Action<T> action;public InlineProgress(Action<T> action){this.action=action;}public void Report(T value){action(value);}}
    sealed class FakeDeviceRecovery:IDeviceRecoveryService
    {
        readonly bool success;public int Calls;public FakeDeviceRecovery(bool success){this.success=success;}
        public Task<DeviceRecoveryResult> RecoverAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();Calls++;return Task.FromResult(new DeviceRecoveryResult{Success=success,LastStep=DeviceRecoveryStep.Preflight,Steps=new DeviceRecoveryStepResult[0],Message=success?"recovered":"failed",ErrorMessage=success?null:"recovery failed"});}
    }
    sealed class WatchdogRunner:IMultiDeviceOneShotFarmRunner
    {
        readonly CancellationTokenSource supervisorCancellation;readonly bool ignoreCancellation;public int Calls;
        public WatchdogRunner(CancellationTokenSource supervisorCancellation,bool ignoreCancellation){this.supervisorCancellation=supervisorCancellation;this.ignoreCancellation=ignoreCancellation;}
        public async Task<MultiDeviceOneShotFarmResult> RunAsync(IReadOnlyList<string> devices,OneShotFarmRequest request,IProgress<MultiDeviceOneShotFarmProgress> progress,CancellationToken token)
        {
            Calls++;string device=devices.Single();
            if(Calls==1)
            {
                if(ignoreCancellation)await new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
                else await Task.Delay(Timeout.Infinite,token);
            }
            supervisorCancellation.Cancel();
            return SuccessfulMultiDeviceResult(device);
        }
        static MultiDeviceOneShotFarmResult SuccessfulMultiDeviceResult(string device)
        {
            var farm=new OneShotFarmResult{DeviceName=device,Success=true,Outcome=OneShotFarmOutcome.MarchStarted,Message="completed"};
            return new MultiDeviceOneShotFarmResult{MaximumConcurrency=20,Devices=new[]{new MultiDeviceOneShotFarmItemResult{DeviceName=device,Stage=MultiDeviceOneShotFarmStage.Completed,Result=farm}}};
        }
    }
    sealed class RecoveryClient:ILdPlayerClient
    {
        public int ScreenshotFailures;public bool Running;public int RunAppCalls,CloseCalls,OpenCalls;
        public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();if(ScreenshotFailures-->0)throw new InvalidOperationException("capture failed");return Task.FromResult(new byte[]{1});}
        public Task<bool> IsRunningAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();return Task.FromResult(Running);}
        public Task RunAppAsync(string d,string p,CancellationToken t){t.ThrowIfCancellationRequested();RunAppCalls++;return Task.CompletedTask;}
        public Task CloseAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();CloseCalls++;return Task.CompletedTask;}
        public Task OpenAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();OpenCalls++;return Task.CompletedTask;}
        public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult<IReadOnlyList<string>>(new[]{"May 1"});
        public Task TapAsync(string d,int x,int y,CancellationToken t)=>Task.CompletedTask;public Task TapByPercentAsync(string d,double x,double y,CancellationToken t)=>Task.CompletedTask;
        public Task LongPressAsync(string d,int x,int y,int ms,CancellationToken t)=>Task.CompletedTask;public Task SwipeByPercentAsync(string d,double sx,double sy,double ex,double ey,int ms,CancellationToken t)=>Task.CompletedTask;
        public Task BackAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task InputTextAsync(string d,string v,CancellationToken t)=>Task.CompletedTask;public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t)=>Task.CompletedTask;
    }
    sealed class TechnicalFailureRunner:IMultiDeviceOneShotFarmRunner
    {
        readonly CancellationTokenSource cancellation;readonly int cancelOnCall;public int Calls;
        public TechnicalFailureRunner(CancellationTokenSource cancellation,int cancelOnCall){this.cancellation=cancellation;this.cancelOnCall=cancelOnCall;}
        public Task<MultiDeviceOneShotFarmResult> RunAsync(IReadOnlyList<string> devices,OneShotFarmRequest request,IProgress<MultiDeviceOneShotFarmProgress> progress,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Calls++;if(Calls>=cancelOnCall)cancellation.Cancel();
            throw new InvalidOperationException("technical failure " + Calls);
        }
    }
    sealed class ContinuousSupervisorRunner:IMultiDeviceOneShotFarmRunner
    {
        readonly object sync=new object();readonly Dictionary<string,int> calls=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        readonly CancellationTokenSource cancellation;readonly bool failFirstDeviceOnce;
        public ContinuousSupervisorRunner(CancellationTokenSource cancellation,bool failFirstDeviceOnce){this.cancellation=cancellation;this.failFirstDeviceOnce=failFirstDeviceOnce;}
        public int Calls(string device){lock(sync){return calls.TryGetValue(device,out int count)?count:0;}}
        public Task<MultiDeviceOneShotFarmResult> RunAsync(IReadOnlyList<string> devices,OneShotFarmRequest request,IProgress<MultiDeviceOneShotFarmProgress> progress,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();string device=devices.Single();int call;
            lock(sync){calls.TryGetValue(device,out call);call++;calls[device]=call;}
            progress?.Report(new MultiDeviceOneShotFarmProgress{DeviceName=device,Stage=MultiDeviceOneShotFarmStage.Preflight,Message="preflight"});
            progress?.Report(new MultiDeviceOneShotFarmProgress{DeviceName=device,Stage=MultiDeviceOneShotFarmStage.Queued,Message="ready"});
            progress?.Report(new MultiDeviceOneShotFarmProgress{DeviceName=device,Stage=MultiDeviceOneShotFarmStage.Running,Message="running",DeviceProgress=new OneShotFarmProgress{Stage=OneShotFarmProgressStage.RunningFarmStep}});
            bool success=!(failFirstDeviceOnce&&device=="May 1"&&call==1);
            lock(sync){if(CallsUnsafe("May 1")>=2&&CallsUnsafe("May 2")>=2)cancellation.Cancel();}
            var farmResult=new OneShotFarmResult{DeviceName=device,Success=success,Outcome=success?OneShotFarmOutcome.MarchStarted:OneShotFarmOutcome.SearchExecutionFailed,Message=success?"completed":"failed",ErrorMessage=success?null:"search failed"};
            return Task.FromResult(new MultiDeviceOneShotFarmResult{MaximumConcurrency=20,WasCancelled=false,Devices=new[]{new MultiDeviceOneShotFarmItemResult{DeviceName=device,Stage=success?MultiDeviceOneShotFarmStage.Completed:MultiDeviceOneShotFarmStage.Failed,Result=farmResult,ErrorMessage=farmResult.ErrorMessage}}});
        }
        int CallsUnsafe(string device){return calls.TryGetValue(device,out int count)?count:0;}
    }
    sealed class FakeHeartbeatNotifier:IContinuousFarmHeartbeatNotifier
    {
        public int Calls;public bool ThrowOnNotify;public bool IsConfigured=>true;
        public Task<ContinuousFarmHeartbeatDeliveryResult> NotifyHeartbeatAsync(
            ContinuousFarmHealthSnapshot snapshot,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Calls++;
            if(ThrowOnNotify)throw new InvalidOperationException("telegram offline");
            return Task.FromResult(new ContinuousFarmHeartbeatDeliveryResult
                {Attempted=true,Success=true,Message="sent"});
        }
    }
    sealed class CancelAfterSuccessRunner:IMultiDeviceOneShotFarmRunner
    {
        readonly CancellationTokenSource cancellation;public CancelAfterSuccessRunner(CancellationTokenSource cancellation){this.cancellation=cancellation;}
        public Task<MultiDeviceOneShotFarmResult> RunAsync(IReadOnlyList<string> devices,OneShotFarmRequest request,IProgress<MultiDeviceOneShotFarmProgress> progress,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();string device=devices.Single();cancellation.Cancel();
            return Task.FromResult(new MultiDeviceOneShotFarmResult{Devices=new[]{new MultiDeviceOneShotFarmItemResult{DeviceName=device,Stage=MultiDeviceOneShotFarmStage.Completed,Result=new OneShotFarmResult{DeviceName=device,Success=true,Outcome=OneShotFarmOutcome.MarchStarted}}}});
        }
    }
    sealed class ObservedFarmRunner:IMultiDeviceOneShotFarmRunner
    {
        readonly CancellationTokenSource cancellation;public ObservedFarmRunner(CancellationTokenSource cancellation){this.cancellation=cancellation;}
        public Task<MultiDeviceOneShotFarmResult> RunAsync(IReadOnlyList<string> devices,OneShotFarmRequest request,IProgress<MultiDeviceOneShotFarmProgress> progress,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();string device=devices.Single();
            progress?.Report(new MultiDeviceOneShotFarmProgress{DeviceName=device,Stage=MultiDeviceOneShotFarmStage.Running,Message="observed",DeviceProgress=new OneShotFarmProgress{Stage=OneShotFarmProgressStage.RunningFarmStep,CurrentResource=ResourceType.Food,CurrentLevel=6,CurrentTeam=TeamNumber.Team3}});
            cancellation.Cancel();
            return Task.FromResult(new MultiDeviceOneShotFarmResult{Devices=new[]{new MultiDeviceOneShotFarmItemResult{DeviceName=device,Stage=MultiDeviceOneShotFarmStage.Completed,Result=new OneShotFarmResult{DeviceName=device,Success=true,Outcome=OneShotFarmOutcome.MarchStarted,CurrentResource=ResourceType.Food,LocatedLevel=6,SelectedTeam=TeamNumber.Team3}}}});
        }
    }
    sealed class MultiDeviceWorkflowProbe
    {
        readonly object sync = new object();
        readonly int requiredConcurrency;
        int active;
        public MultiDeviceWorkflowProbe(int requiredConcurrency){this.requiredConcurrency=requiredConcurrency;}
        public int MaximumActive { get; private set; }
        public List<OneShotFarmRequest> Requests { get; } = new List<OneShotFarmRequest>();
        public TaskCompletionSource<bool> RequiredConcurrencyReached { get; } =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Enter(OneShotFarmRequest request)
        {
            lock(sync)
            {
                Requests.Add(request);
                active++;
                MaximumActive=Math.Max(MaximumActive,active);
                if(active>=requiredConcurrency)RequiredConcurrencyReached.TrySetResult(true);
            }
        }
        public void Exit(){lock(sync){active--;}}
    }
    sealed class MultiDeviceProbeWorkflow:IOneShotFarmWorkflow
    {
        readonly MultiDeviceWorkflowProbe probe;
        public MultiDeviceProbeWorkflow(MultiDeviceWorkflowProbe probe){this.probe=probe;}
        public Task<OneShotFarmResult> RunAsync(string d,OneShotFarmRequest r,CancellationToken t)=>RunAsync(d,r,null,t);
        public async Task<OneShotFarmResult> RunAsync(string d,OneShotFarmRequest r,IProgress<OneShotFarmProgress> p,CancellationToken t)
        {
            t.ThrowIfCancellationRequested();probe.Enter(r);
            try
            {
                using(t.Register(()=>probe.Release.TrySetCanceled())){await probe.Release.Task;}
                t.ThrowIfCancellationRequested();
                return new OneShotFarmResult{DeviceName=d,Success=true,Outcome=OneShotFarmOutcome.MarchStarted,
                    RequestedResource=r.ResourceType,RequestedLevel=r.TargetLevel,AttemptedLevels=new int[0],
                    AttemptedResources=new ResourceType[0],SelectedResources=r.SelectedResources,
                    ShuffledResourcePriority=r.ResourcePriority,StorageFullResources=new ResourceType[0],
                    LevelsExhaustedResources=new ResourceType[0],Steps=new OneShotFarmStepResult[0],
                    LastCompletedStep=OneShotFarmStep.Completed,Message="completed"};
            }
            finally{probe.Exit();}
        }
    }
    sealed class SelectiveFailureWorkflow:IOneShotFarmWorkflow
    {
        public Task<OneShotFarmResult> RunAsync(string d,OneShotFarmRequest r,CancellationToken t)=>RunAsync(d,r,null,t);
        public Task<OneShotFarmResult> RunAsync(string d,OneShotFarmRequest r,IProgress<OneShotFarmProgress> p,CancellationToken t)
        {
            t.ThrowIfCancellationRequested();bool success=d=="Healthy Device";
            return Task.FromResult(new OneShotFarmResult{DeviceName=d,Success=success,
                Outcome=success?OneShotFarmOutcome.MarchStarted:OneShotFarmOutcome.SearchExecutionFailed,
                RequestedResource=r.ResourceType,RequestedLevel=r.TargetLevel,AttemptedLevels=new int[0],
                AttemptedResources=new ResourceType[0],SelectedResources=r.SelectedResources,
                ShuffledResourcePriority=r.ResourcePriority,StorageFullResources=new ResourceType[0],
                LevelsExhaustedResources=new ResourceType[0],Steps=new OneShotFarmStepResult[0],
                LastCompletedStep=success?OneShotFarmStep.Completed:OneShotFarmStep.ExecuteSearch,
                Message=success?"completed":"failed"});
        }
    }
    sealed class MultiDevicePreflightAvailability:IWorldMapTeamAvailabilityService
    {
        readonly int expected;readonly string failedDevice;int calls;
        readonly TaskCompletionSource<bool> allChecked=new TaskCompletionSource<bool>();
        public int Calls=>Volatile.Read(ref calls);
        public MultiDevicePreflightAvailability(int expected,string failedDevice=null)
        {this.expected=expected;this.failedDevice=failedDevice;}
        public async Task<WorldMapTeamAvailabilityResult> CheckAsync(string d,CancellationToken t)
        {
            if(Interlocked.Increment(ref calls)>=expected)allChecked.TrySetResult(true);
            using(t.Register(()=>allChecked.TrySetCanceled()))await allChecked.Task;
            t.ThrowIfCancellationRequested();bool success=d!=failedDevice;
            return new WorldMapTeamAvailabilityResult{Success=success,AnyReadyTeam=success,
                AvailableTeams=new[]{TeamNumber.Team4,TeamNumber.Team3},
                ReadyTeams=success?new[]{TeamNumber.Team4}:new TeamNumber[0],
                ReadyMatches=new ImageMatchResult[0],FinalState=GameState.WorldMap,
                Message=success?"ready":"preflight failed",ErrorMessage=success?null:"capture failed"};
        }
    }
    sealed class PreflightAwareWorkflow:IOneShotFarmWorkflow
    {
        readonly MultiDevicePreflightAvailability availability;
        public bool AllPreflightsCompleted=true;
        public readonly List<string> StartedDevices=new List<string>();
        public PreflightAwareWorkflow(MultiDevicePreflightAvailability availability){this.availability=availability;}
        public Task<OneShotFarmResult> RunAsync(string d,OneShotFarmRequest r,CancellationToken t)=>RunAsync(d,r,null,t);
        public Task<OneShotFarmResult> RunAsync(string d,OneShotFarmRequest r,IProgress<OneShotFarmProgress> p,CancellationToken t)
        {
            t.ThrowIfCancellationRequested();StartedDevices.Add(d);
            if(availability.Calls<2)AllPreflightsCompleted=false;
            return Task.FromResult(new OneShotFarmResult{DeviceName=d,Success=true,
                Outcome=OneShotFarmOutcome.MarchStarted,LastCompletedStep=OneShotFarmStep.Completed,
                AttemptedLevels=new int[0],AttemptedResources=new ResourceType[0],
                StorageFullResources=new ResourceType[0],LevelsExhaustedResources=new ResourceType[0],
                Steps=new OneShotFarmStepResult[0],Message="completed"});
        }
    }
    sealed class FakeHostResourceProbe:IHostResourceProbe
    {
        readonly double cpu;readonly long memory;
        public FakeHostResourceProbe(double cpu,long memory){this.cpu=cpu;this.memory=memory;}
        public HostResourceSnapshot Sample()=>new HostResourceSnapshot
            {CpuUsagePercent=cpu,AvailableMemoryBytes=memory};
    }
    sealed class Log:IDiagnosticLogger{public void Info(string m){}public void Error(string m,Exception e){}}
}
