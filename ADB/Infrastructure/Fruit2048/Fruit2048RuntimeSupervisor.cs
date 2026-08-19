using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    /// <summary>
    /// Per-device session coordinator.  Gameplay, recognition, navigation and catalog
    /// mutation remain in their existing services; this class owns only bounded session
    /// state and the one safe screen-loss retry policy.
    /// </summary>
    public sealed class Fruit2048RuntimeSupervisor : IFruit2048RuntimeSupervisor
    {
        private const int MaxNavigationRecoveries = 2;
        private readonly IFruit2048AutomationService automation;
        private readonly IDiagnosticLogger logger;
        private readonly ConcurrentDictionary<string, Fruit2048RuntimeSnapshot> snapshots =
            new ConcurrentDictionary<string, Fruit2048RuntimeSnapshot>(StringComparer.OrdinalIgnoreCase);

        public Fruit2048RuntimeSupervisor(IFruit2048AutomationService automation, IDiagnosticLogger logger)
        {
            this.automation = automation ?? throw new ArgumentNullException(nameof(automation));
            this.logger = logger;
        }

        public async Task<Fruit2048RunResult> RunAsync(string deviceName, Fruit2048RunRequest request,
            IProgress<Fruit2048Progress> progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("Device name is required.", nameof(deviceName));
            if (request == null) throw new ArgumentNullException(nameof(request));
            var session = new Fruit2048RuntimeSnapshot
            {
                DeviceName = deviceName,
                FruitSessionId = Guid.NewGuid().ToString("N"),
                State = Fruit2048RuntimeState.Starting,
                LastStateChangeUtc = DateTimeOffset.UtcNow
            };
            snapshots[deviceName] = session;
            // Preserve one operation identity through navigation, learning proof
            // logs and the persisted proof report.
            request.FruitSessionId = session.FruitSessionId;
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            var counters = new int[4]; // boards, valid, invalid, ambiguous
            var burnIn = request.Mode == Fruit2048RunMode.BurnIn ? new Fruit2048BurnInMetrics() : null;
            Fruit2048RunResult result = null;
            var observedProgress = new Progress<Fruit2048Progress>(value =>
            {
                if (value == null) return;
                ApplyProgress(session, value, counters, burnIn);
                progress?.Report(value);
            });
            try
            {
                for (int recovery = 0; recovery <= MaxNavigationRecoveries; recovery++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result = await automation.RunAsync(deviceName, request, observedProgress, cancellationToken);
                    if (result.Outcome != Fruit2048Outcome.ScreenNotOpen || recovery == MaxNavigationRecoveries)
                        break;
                    session.NavigationRecoveryCount++;
                    SetState(session, Fruit2048RuntimeState.Recovering, Fruit2048RuntimeFailure.RecoverableScreenLoss,
                        "Mất màn hình Fruit2048 — đang khôi phục.");
                }
                ApplyResult(session, result);
                return result;
            }
            catch (OperationCanceledException)
            {
                SetState(session, Fruit2048RuntimeState.Completed, Fruit2048RuntimeFailure.Cancelled, "Đã dừng");
                throw;
            }
            finally
            {
                TimeSpan duration = DateTimeOffset.UtcNow - startedAt;
                logger?.Info("[Fruit2048 Session Summary] DeviceName='" + deviceName + "', FruitSessionId='"
                    + session.FruitSessionId + "', Moves=" + session.MoveSequence + ", BoardsRecognized=" + counters[0]
                    + ", TransitionsValid=" + counters[1] + ", TransitionsInvalid=" + counters[2]
                    + ", TransitionsAmbiguous=" + counters[3] + ", NavigationRecoveries=" + session.NavigationRecoveryCount
                    + ", Outcome='" + (result?.Outcome.ToString() ?? "Cancelled") + "', DurationMs=" + (long)duration.TotalMilliseconds);
                if (burnIn != null)
                    PersistBurnInReport(new Fruit2048BurnInReport
                    {
                        DeviceName = deviceName,
                        FruitSessionId = session.FruitSessionId,
                        StartedAtUtc = startedAt,
                        EndedAtUtc = DateTimeOffset.UtcNow,
                        TargetMoves = request.TeacherMoveLimit,
                        Outcome = MapBurnInOutcome(result),
                        Health = GetHealth(burnIn, result),
                        RecommendedNextAction = Recommend(request.TeacherMoveLimit, burnIn, result),
                        FailureReason = result?.Error,
                        Metrics = burnIn
                    });
            }
        }

        public Task<Fruit2048BoardReadResult> ScanAsync(string deviceName, CancellationToken cancellationToken) =>
            automation.ScanAsync(deviceName, cancellationToken);

        public Task<Fruit2048BoardReadResult> SendManualMoveAsync(string deviceName, Fruit2048Move move,
            CancellationToken cancellationToken) => automation.SendManualMoveAsync(deviceName, move, cancellationToken);

        public Fruit2048RuntimeSnapshot GetSnapshot(string deviceName)
        {
            Fruit2048RuntimeSnapshot result;
            return snapshots.TryGetValue(deviceName, out result) ? result : null;
        }

        private static void ApplyProgress(Fruit2048RuntimeSnapshot session, Fruit2048Progress progress, int[] counters,
            Fruit2048BurnInMetrics burnIn)
        {
            session.MoveSequence = Math.Max(session.MoveSequence, progress.MoveCount);
            if (progress.Board != null) { counters[0]++; session.LastReliableBoardUtc = DateTimeOffset.UtcNow; }
            if (burnIn != null)
            {
                burnIn.MovesIssued = Math.Max(burnIn.MovesIssued, progress.MoveCount);
                if (progress.Cells != null) burnIn.BoardsRead++;
                if (progress.Board != null && progress.UnknownCellCount == 0) burnIn.BoardsReliable++;
                if (progress.UnknownCellCount > 0) burnIn.UnknownCellEvents++;
                burnIn.FastPathCells += progress.Cells == null ? 0 : progress.Cells.Count(cell => cell.RecognitionSource == Fruit2048RecognitionSource.FastFingerprint);
                burnIn.FallbackCells += progress.Cells == null ? 0 : progress.Cells.Count(cell => cell.RecognitionSource == Fruit2048RecognitionSource.PrototypeMatch || cell.RecognitionSource == Fruit2048RecognitionSource.StaticSeed);
            }
            if (progress.Status == Fruit2048RuntimeStatus.ValidatingTransition) SetState(session, Fruit2048RuntimeState.ValidatingTransition, Fruit2048RuntimeFailure.None, null);
            else if (progress.Status == Fruit2048RuntimeStatus.WaitingPostMove) SetState(session, Fruit2048RuntimeState.WaitingPostMove, Fruit2048RuntimeFailure.None, null);
            else if (progress.Status == Fruit2048RuntimeStatus.Playing) SetState(session, Fruit2048RuntimeState.Playing, Fruit2048RuntimeFailure.None, null);
            else if (progress.Status == Fruit2048RuntimeStatus.Scanning) SetState(session, Fruit2048RuntimeState.Scanning, Fruit2048RuntimeFailure.None, null);
            if (progress.TransitionStatus == Fruit2048TransitionValidationStatus.Valid) { counters[1]++; if (burnIn != null) burnIn.TransitionsValid++; }
            else if (progress.TransitionStatus == Fruit2048TransitionValidationStatus.ValidWithSpawn) { counters[1]++; if (burnIn != null) burnIn.TransitionsValidWithSpawn++; }
            else if (progress.TransitionStatus == Fruit2048TransitionValidationStatus.Invalid) { counters[2]++; if (burnIn != null) burnIn.TransitionsInvalid++; }
            else if (progress.TransitionStatus == Fruit2048TransitionValidationStatus.Ambiguous) { counters[3]++; if (burnIn != null) burnIn.TransitionsAmbiguous++; }
        }

        private static Fruit2048BurnInHealth GetHealth(Fruit2048BurnInMetrics metrics, Fruit2048RunResult result)
        {
            if (metrics.TransitionsInvalid > 0 || metrics.LearningConflicts > 0 || result?.Outcome == Fruit2048Outcome.BoardReadFailed) return Fruit2048BurnInHealth.Failed;
            if (metrics.TransitionsAmbiguous > 0 || metrics.BoardReliabilityRate < .98 || metrics.NavigationRecoveries > 1) return Fruit2048BurnInHealth.Warning;
            return metrics.BoardReliabilityRate >= .99 ? Fruit2048BurnInHealth.Excellent : Fruit2048BurnInHealth.Good;
        }

        private static Fruit2048BurnInRecommendation Recommend(int target, Fruit2048BurnInMetrics metrics, Fruit2048RunResult result)
        {
            if (GetHealth(metrics, result) == Fruit2048BurnInHealth.Failed) return Fruit2048BurnInRecommendation.InspectDiagnostics;
            if (target <= 50) return Fruit2048BurnInRecommendation.Run250;
            if (target <= 250) return Fruit2048BurnInRecommendation.Run1000;
            return Fruit2048BurnInRecommendation.ReadyForProduction;
        }

        private static Fruit2048BurnInOutcome MapBurnInOutcome(Fruit2048RunResult result)
        {
            if (result == null || result.Outcome == Fruit2048Outcome.Cancelled) return Fruit2048BurnInOutcome.Cancelled;
            if (result.Outcome == Fruit2048Outcome.Disconnected) return Fruit2048BurnInOutcome.DeviceDisconnected;
            if (result.Outcome == Fruit2048Outcome.ScreenNotOpen) return Fruit2048BurnInOutcome.NavigationRecoveryFailed;
            if (result.Outcome == Fruit2048Outcome.BoardReadFailed || result.Outcome == Fruit2048Outcome.NeedsFreshBoard) return Fruit2048BurnInOutcome.BoardUnresolved;
            return Fruit2048BurnInOutcome.Completed;
        }

        private static void PersistBurnInReport(Fruit2048BurnInReport report)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IKAutomation", "Fruit2048", "BurnIn");
            Directory.CreateDirectory(root);
            string file = report.EndedAtUtc.ToString("yyyyMMdd-HHmmss") + "_" + report.DeviceName + "_" + report.FruitSessionId + ".json";
            string json = "{\"DeviceName\":\"" + Escape(report.DeviceName) + "\",\"FruitSessionId\":\"" + Escape(report.FruitSessionId)
                + "\",\"TargetMoves\":" + report.TargetMoves + ",\"CompletedMoves\":" + report.Metrics.MovesIssued
                + ",\"Outcome\":\"" + report.Outcome + "\",\"Health\":\"" + report.Health
                + "\",\"RecommendedNextAction\":\"" + report.RecommendedNextAction + "\",\"BoardReliabilityRate\":" + report.Metrics.BoardReliabilityRate.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"FastPathRate\":" + report.Metrics.FastPathRate.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
            string temporary = Path.Combine(root, file + ".tmp"); string destination = Path.Combine(root, file);
            File.WriteAllText(temporary, json); File.Move(temporary, destination);
        }

        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static void ApplyResult(Fruit2048RuntimeSnapshot session, Fruit2048RunResult result)
        {
            if (result == null) { SetState(session, Fruit2048RuntimeState.Error, Fruit2048RuntimeFailure.Fatal, "No result"); return; }
            if (result.Outcome == Fruit2048Outcome.Cancelled) SetState(session, Fruit2048RuntimeState.Completed, Fruit2048RuntimeFailure.Cancelled, "Đã dừng");
            else if (result.Outcome == Fruit2048Outcome.TargetReached || result.Outcome == Fruit2048Outcome.NoMoves || result.Outcome == Fruit2048Outcome.MoveLimitReached || result.Outcome == Fruit2048Outcome.LearningProofCompleted) SetState(session, Fruit2048RuntimeState.Completed, Fruit2048RuntimeFailure.None, result.Error);
            else if (result.Outcome == Fruit2048Outcome.DeviceUnavailable)
                SetState(session, Fruit2048RuntimeState.Paused, Fruit2048RuntimeFailure.DeviceUnavailable,
                    "Mất kết nối ADB — đã dừng.");
            else if (result.Outcome == Fruit2048Outcome.SwipeNoEffect)
                SetState(session, Fruit2048RuntimeState.Paused, Fruit2048RuntimeFailure.UnsafeBoard,
                    result.Error ?? "Vuốt không làm thay đổi board — đã tạm dừng.");
            else if (result.Outcome == Fruit2048Outcome.ScreenNotOpen || result.Outcome == Fruit2048Outcome.NeedsFreshBoard || result.Outcome == Fruit2048Outcome.BoardReadFailed || result.Outcome == Fruit2048Outcome.LearningProofFailed) SetState(session, Fruit2048RuntimeState.Paused, Fruit2048RuntimeFailure.UnsafeBoard, result.Error);
            else SetState(session, Fruit2048RuntimeState.Error, Fruit2048RuntimeFailure.Fatal, result.Error);
        }

        private static void SetState(Fruit2048RuntimeSnapshot state, Fruit2048RuntimeState next,
            Fruit2048RuntimeFailure failure, string reason)
        {
            state.State = next; state.Failure = failure; state.PauseReason = reason;
            state.LastStateChangeUtc = DateTimeOffset.UtcNow;
        }
    }
}
