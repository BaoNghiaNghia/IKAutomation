using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    public sealed class Fruit2048AutomationService : IFruit2048AutomationService
    {
        // Post-swipe frames can differ slightly because the fruit settles inside
        // the cell. This is confirmation-only evidence for an already simulated
        // non-empty cell; merge learning keeps its stricter own threshold.
        private const int StableExpectedCellHashDistance = 18;
        // The fruit body can still glow or settle after a swipe.  Its number
        // badge is much more stable, so use it as confirmation when available.
        private const int StableExpectedBadgeHashDistance = 10;
        private readonly ILdPlayerClient playerClient;
        private readonly IFruit2048BoardReader reader;
        private readonly IFruit2048Solver solver;
        private readonly IFruit2048SwipeExecutor swipeExecutor;
        private readonly IDeviceAutomationOwnershipService ownership;
        private readonly IFruit2048TransitionLearner transitionLearner;
        private readonly IFruitTileLearningCatalog learningCatalog;
        private readonly IDiagnosticLogger logger;
        private readonly IFruit2048NavigationService navigation;
        private readonly IFruit2048LearningCoordinator learningCoordinator;
        private readonly IFruit2048TransitionValidator transitionValidator;
        private readonly Fruit2048LearningDiagnosticStore diagnosticStore;
        private readonly Fruit2048LearningProofStore proofStore;

        public Fruit2048AutomationService(ILdPlayerClient playerClient,
            IFruit2048BoardReader reader, IFruit2048Solver solver,
            IFruit2048SwipeExecutor swipeExecutor,
            IDeviceAutomationOwnershipService ownership,
            IFruit2048TransitionLearner transitionLearner,
            IFruitTileLearningCatalog learningCatalog, IDiagnosticLogger logger,
            IFruit2048NavigationService navigation = null, IFruit2048LearningCoordinator learningCoordinator = null,
            IFruit2048TransitionValidator transitionValidator = null,
            Fruit2048LearningDiagnosticStore diagnosticStore = null,
            Fruit2048LearningProofStore proofStore = null)
        {
            this.playerClient = playerClient ?? throw new ArgumentNullException(nameof(playerClient));
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
            this.solver = solver ?? throw new ArgumentNullException(nameof(solver));
            this.swipeExecutor = swipeExecutor ?? throw new ArgumentNullException(nameof(swipeExecutor));
            this.ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
            this.transitionLearner = transitionLearner ?? throw new ArgumentNullException(nameof(transitionLearner));
            this.learningCatalog = learningCatalog ?? throw new ArgumentNullException(nameof(learningCatalog));
            this.logger = logger;
            this.navigation = navigation;
            this.learningCoordinator = learningCoordinator;
            this.transitionValidator = transitionValidator ?? new Fruit2048TransitionValidator();
            this.diagnosticStore = diagnosticStore;
            this.proofStore = proofStore;
        }

        public async Task<Fruit2048RunResult> RunAsync(string deviceName,
            Fruit2048RunRequest request, IProgress<Fruit2048Progress> progress,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.TargetTile < 1) throw new ArgumentOutOfRangeException(nameof(request.TargetTile));
            bool firstLearningProof = request.Mode == Fruit2048RunMode.FirstLearningProof;
            bool initialFreshBoardBootstrap = Fruit2048FirstLearningProofPolicy
                .RequiresInitialFreshBoardBootstrap(request.Mode, learningCatalog.Snapshot);
            IDeviceAutomationLease lease;
            if (!ownership.TryAcquire(deviceName, DeviceAutomationOwner.Fruit2048, out lease))
                return Result(deviceName, Fruit2048Outcome.Failed, 0, 0,
                    request.TargetTile, false, "Thiết bị đang được sử dụng bởi " + ownership.GetOwner(deviceName) + ".");

            int moveCount = 0, highestTile = 0;
            bool refreshUsedForCurrentDeadBoard = false;
            string teacherSession = null;
            Fruit2048Board boardBeforeLastMove = null;
            Fruit2048Move? previousMove = null;
            string previousEvidenceId = null;
            Fruit2048PendingTransition pendingTransition = null;
            // This is deliberately a no-progress budget, not a lifetime move cap.
            // A verified merge that adds or strengthens a tier is progress and must
            // allow the teacher to continue through later tiers (4, 5, 6, ...).
            int movesWithoutLearningEvidence = 0;
            bool proofInitialBoardValidated = false;
            bool proofTier2Learned = false;
            bool proofTier2RecognitionConfirmed = false;
            var proofSession = new FirstLearningProofSession
            {
                FruitSessionId = request.FruitSessionId,
                StartedAtUtc = DateTimeOffset.UtcNow
            };
            Report(progress, deviceName, Fruit2048RuntimeStatus.Starting, null,
                0, null, "Đang khởi động", null);
            if (initialFreshBoardBootstrap)
                (reader as IFruit2048BootstrapSessionController)?.BeginInitialBootstrap(deviceName);
            LogDevice(deviceName, "Fruit2048", "Starting");
            try
            {
                if (learningCoordinator != null)
                {
                    string reason;
                    if (learningCoordinator.TryAcquireTeacher(deviceName, out teacherSession, out reason))
                        Report(progress, deviceName, Fruit2048RuntimeStatus.Starting, null, 0, null, "Thiết bị học", null);
                }
                if (firstLearningProof && string.IsNullOrWhiteSpace(teacherSession))
                {
                    const string teacherRequired = "FirstLearningProof chỉ chạy trên thiết bị học đã chọn.";
                    return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                        request.TargetTile, Fruit2048Outcome.LearningProofFailed, teacherRequired,
                        Fruit2048FirstLearningProofPolicy.CreateProgress(0, learningCatalog.Snapshot, false, "Failed"),
                        proofSession, Fruit2048LearningProofFailure.Unexpected);
                }
                proofSession.TeacherSessionId = teacherSession;
                proofSession.CatalogVersionStart = learningCoordinator?.Teacher.CatalogVersion ?? 0;
                proofSession.CatalogVersionEnd = proofSession.CatalogVersionStart;
                Report(progress, deviceName, Fruit2048RuntimeStatus.Starting, null, 0, null,
                    "Đang kiểm tra màn hình", null);
                if (navigation != null)
                {
                    Fruit2048NavigationResult navigationResult = await navigation.EnsureFruit2048ScreenAsync(
                        deviceName, new Progress<string>(message => Report(progress, deviceName,
                            Fruit2048RuntimeStatus.Starting, null, moveCount, null, message, null)), cancellationToken);
                    if (!navigationResult.Success)
                    {
                        if (firstLearningProof)
                            return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                                request.TargetTile, Fruit2048Outcome.LearningProofFailed, navigationResult.Error,
                                Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount,
                                    learningCatalog.Snapshot, false, "Failed"), proofSession,
                                Fruit2048LearningProofFailure.ScreenUnavailable);
                        return Result(deviceName, Fruit2048Outcome.ScreenNotOpen, moveCount, highestTile,
                            request.TargetTile, false, navigationResult.Error);
                    }
                    Report(progress, deviceName, Fruit2048RuntimeStatus.Starting, null, 0, null,
                        "Đã vào Lễ Hội Trái Cây", null);
                }
                Fruit2048SeedAvailability seeds = reader.SeedAvailability;
                if (seeds == null || !seeds.IsReady)
                {
                    const string missingSeedMessage = "Thiếu mẫu nhận diện Empty/Tier 1.";
                    Report(progress, deviceName, Fruit2048RuntimeStatus.MissingSeeds, null,
                        moveCount, null, missingSeedMessage, missingSeedMessage,
                        Fruit2048BootstrapState.MissingSeedAssets);
                    if (firstLearningProof)
                        return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                            request.TargetTile, Fruit2048Outcome.LearningProofFailed, missingSeedMessage,
                            Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount,
                                learningCatalog.Snapshot, false, "Failed"), proofSession,
                            Fruit2048LearningProofFailure.MissingSeeds);
                    return Result(deviceName, Fruit2048Outcome.MissingSeedAssets,
                        moveCount, highestTile, request.TargetTile, false, missingSeedMessage);
                }
                // Capture/read already reports a lost ADB endpoint with a typed
                // outcome.  Do this inexpensive availability check once at the
                // session boundary instead of adding an extra ADB round-trip to
                // every verified move.
                if (!await playerClient.IsRunningAsync(deviceName, cancellationToken))
                {
                    if (firstLearningProof)
                        return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                            request.TargetTile, Fruit2048Outcome.LearningProofFailed, "Mất kết nối",
                            Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount,
                                learningCatalog.Snapshot, false, "Failed"), proofSession,
                            Fruit2048LearningProofFailure.DeviceDisconnected);
                    return Result(deviceName, Fruit2048Outcome.Disconnected, moveCount,
                        highestTile, request.TargetTile, false, "Mất kết nối");
                }
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Report(progress, deviceName, Fruit2048RuntimeStatus.Scanning, null,
                        moveCount, null, "Đang đọc bàn", null);
                    // A full PNG encode is expensive and is not needed to validate a
                    // normal move.  The board reader keeps the in-memory Bitmap only
                    // for this read; a full screenshot is captured on the terminal
                    // diagnostic path below if an otherwise reliable transition fails.
                    Fruit2048BoardReadResult read = await reader.ReadAsync(deviceName,
                        false, cancellationToken);
                    if (read.ScreenStatus == Fruit2048ScreenStatus.DeviceUnavailable)
                    {
                        const string unavailable = "Mất kết nối ADB — đã dừng.";
                        Report(progress, deviceName, Fruit2048RuntimeStatus.Disconnected, read,
                            moveCount, previousMove, unavailable, read.Error);
                        return Result(deviceName, Fruit2048Outcome.DeviceUnavailable, moveCount,
                            highestTile, request.TargetTile, false, read.Error ?? unavailable);
                    }
                    if (initialFreshBoardBootstrap && moveCount == 0 && pendingTransition == null && read.Success)
                        (reader as IFruit2048BootstrapSessionController)?.CompleteInitialBootstrap(deviceName);
                    if (firstLearningProof && !proofInitialBoardValidated)
                    {
                        if (!Fruit2048FirstLearningProofPolicy.IsFreshTier1Board(read))
                        {
                            const string freshBoardRequired = "Board khởi đầu chưa nhận diện đủ 16/16.";
                            return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                                request.TargetTile, Fruit2048Outcome.LearningProofFailed, freshBoardRequired,
                                Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount,
                                    learningCatalog.Snapshot, false, "InitialBoard"), proofSession,
                                Fruit2048LearningProofFailure.InitialBoardUnresolved);
                        }
                        if (Fruit2048FirstLearningProofPolicy.IsTier2Learned(learningCatalog.Snapshot))
                        {
                            const string tier2AlreadyLearned = "Tier2 đã được học trước đó.";
                            return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                                request.TargetTile, Fruit2048Outcome.LearningProofFailed, tier2AlreadyLearned,
                                Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount,
                                    learningCatalog.Snapshot, false, "Failed"), proofSession,
                                Fruit2048LearningProofFailure.Tier2AlreadyLearned);
                        }
                        proofSession.InitialEmptyCells = read.Cells.Count(cell => cell.Tier == 0);
                        proofSession.InitialTier1Cells = read.Cells.Count(cell => cell.Tier == 1);
                        proofSession.InitialUnknownCells = read.UnknownCells?.Count ?? 0;
                        proofInitialBoardValidated = true;
                        Fruit2048FirstLearningProofProgress initialProof = EnrichProofProgress(
                            Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount, learningCatalog.Snapshot,
                                false, "InitialBoard"), proofSession, Fruit2048LearningProofFailure.None);
                        LogProof(deviceName, teacherSession, "InitialBoard", moveCount,
                            initialProof, null);
                        Report(progress, deviceName, Fruit2048RuntimeStatus.Bootstrapping, read, moveCount, null,
                            "Đã xác minh board 14 Empty / 2 Tier 1.", null, null,
                            EnrichProofProgress(Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount,
                                learningCatalog.Snapshot, false, "AwaitTier1Merge"), proofSession,
                                Fruit2048LearningProofFailure.None));
                    }
                    if (firstLearningProof && proofTier2Learned && boardBeforeLastMove == null
                        && Fruit2048FirstLearningProofPolicy.HasCatalogTier2Recognition(read))
                    {
                        proofTier2RecognitionConfirmed = true;
                        Fruit2048FirstLearningProofProgress proof = Fruit2048FirstLearningProofPolicy.CreateProgress(
                            moveCount, learningCatalog.Snapshot, true, "Completed");
                        LogProof(deviceName, teacherSession, "Tier2RecognitionProof", moveCount, proof, null);
                        return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                            request.TargetTile, Fruit2048Outcome.LearningProofCompleted,
                            "Đã xác nhận tự học Tier 2 thành công.", proof, proofSession,
                            Fruit2048LearningProofFailure.None);
                    }
                    if (pendingTransition != null && transitionValidator != null)
                    {
                        var observations = new List<Fruit2048BoardReadResult> { read };
                        Fruit2048Board expected = pendingTransition.ExpectedBoardAfterMove;
                        IReadOnlyList<Fruit2048MergeOperation> merges = pendingTransition.MergeOperations;

                        logger?.Info($"[Fruit2048 Post Move] DeviceName='{deviceName}', TransitionId='{pendingTransition.TransitionId}', "
                            + $"CaptureAttempt=1, CaptureFingerprint='{read.CaptureFingerprint}', BoardValues='{read.Board}', "
                            + $"Reliable={read.Success}, SameAsBefore={BoardsEqual(read.Board, pendingTransition.BoardBefore)}");
                        if (read.Success && BoardsEqual(read.Board, pendingTransition.BoardBefore))
                        {
                            await Task.Delay(Fruit2048PacingPolicy.GetFocusedRetryDelay(read.Learning), cancellationToken);
                            Fruit2048BoardReadResult confirmation = await reader.ReadAsync(deviceName, false, cancellationToken);
                            observations.Add(confirmation);
                            logger?.Info($"[Fruit2048 Post Move] DeviceName='{deviceName}', TransitionId='{pendingTransition.TransitionId}', "
                                + $"CaptureAttempt=2, CaptureFingerprint='{confirmation.CaptureFingerprint}', BoardValues='{confirmation.Board}', "
                                + $"Reliable={confirmation.Success}, SameAsBefore={BoardsEqual(confirmation.Board, pendingTransition.BoardBefore)}");
                            if (confirmation.ScreenStatus == Fruit2048ScreenStatus.DeviceUnavailable)
                            {
                                pendingTransition.ValidationStatus = Fruit2048TransitionValidationStatus.DeviceUnavailable;
                                return Result(deviceName, Fruit2048Outcome.DeviceUnavailable, moveCount, highestTile,
                                    request.TargetTile, false, "Mất kết nối ADB khi đang xác minh nước đi.");
                            }
                            if (confirmation.Success && BoardsEqual(confirmation.Board, pendingTransition.BoardBefore))
                            {
                                pendingTransition.ValidationStatus = Fruit2048TransitionValidationStatus.SwipeNoEffect;
                                string message = "Vuốt không làm thay đổi board — đã tạm dừng.";
                                Report(progress, deviceName, Fruit2048RuntimeStatus.Paused, confirmation, moveCount,
                                    pendingTransition.Move, message, message,
                                    null, null, Fruit2048TransitionValidationStatus.SwipeNoEffect, pendingTransition);
                                return Result(deviceName, Fruit2048Outcome.SwipeNoEffect, moveCount, highestTile,
                                    request.TargetTile, false, message);
                            }
                            read = confirmation;
                        }
                        Fruit2048TransitionValidationResult validation = null;
                        for (int retry = 0; retry < 3; retry++)
                        {
                            Fruit2048BoardReadResult candidate = observations.Last();
                            Report(progress, deviceName, Fruit2048RuntimeStatus.ValidatingTransition, candidate,
                                moveCount, previousMove, "Đang xác minh nước đi", null);
                            validation = transitionValidator.Validate(new Fruit2048TransitionValidationRequest
                            {
                                DeviceName = deviceName,
                                TransitionId = pendingTransition.TransitionId,
                                BoardBefore = pendingTransition.BoardBefore,
                                Move = pendingTransition.Move,
                                ExpectedBoardAfterMove = expected,
                                MergeOperations = merges,
                                Observed = candidate
                            });
                            pendingTransition.ValidationStatus = validation.Status;
                            logger?.Info($"[Fruit2048 Transition Validation] DeviceName='{deviceName}', TransitionId='{pendingTransition.TransitionId}', Status='{validation.Status}', BeforeBoard='{pendingTransition.BoardBefore}', ExpectedBoard='{expected}', ObservedBoard='{candidate.Board}', SpawnCandidates={validation.SpawnCandidates}, Reason='{validation.Reason}'");
                            Report(progress, deviceName, Fruit2048RuntimeStatus.ValidatingTransition, candidate,
                                moveCount, pendingTransition.Move, "Đang xác minh nước đi", null, null, null,
                                validation.Status, pendingTransition);
                            // A single fully recognised frame can still be the board while
                            // a merge is settling or an old capture. Re-read it within the
                            // existing bounded window before treating a contradiction as
                            // terminal. A true mismatch remains terminal after that window.
                            if (validation.IsValidForLearning
                                || (validation.Status == Fruit2048TransitionValidationStatus.Invalid && retry == 2))
                                break;
                            if (retry == 2) break;
                            await Task.Delay(Fruit2048PacingPolicy.GetFocusedRetryDelay(read.Learning), cancellationToken);
                            Fruit2048BoardReadResult retryRead = await reader.ReadAsync(deviceName, false, cancellationToken);
                            observations.Add(retryRead);
                            pendingTransition.RetryCount++;
                            if (retryRead.ScreenStatus == Fruit2048ScreenStatus.DeviceUnavailable)
                                break;
                        }

                        // Tier 2 has no learned visual prototype yet. During its merge
                        // animation the destination can therefore remain Unknown in each
                        // focused retry.  Accept it only when the same deterministic merge
                        // destination has a stable unknown fingerprint in at least two
                        // captures; this is the evidence TransitionLearner requires to add
                        // the first Candidate Tier 2 sample.
                        if (validation?.Status == Fruit2048TransitionValidationStatus.Ambiguous
                            && string.Equals(validation.Reason, "UnknownMergeDestinationRequiresFocusedRetry",
                                StringComparison.Ordinal)
                            && HasStableUnknownMergeEvidence(observations, validation))
                        {
                            validation = new Fruit2048TransitionValidationResult
                            {
                                Status = Fruit2048TransitionValidationStatus.ValidWithSpawn,
                                Reason = "StableUnknownMergeDestination",
                                SpawnCandidates = validation.SpawnCandidates,
                                UnknownCells = validation.UnknownCells,
                                MergeDestinations = validation.MergeDestinations
                            };
                            pendingTransition.ValidationStatus = validation.Status;
                            logger?.Info($"[Fruit2048 Transition Validation] DeviceName='{deviceName}', "
                                + $"TransitionId='{pendingTransition.TransitionId}', Status='{validation.Status}', "
                                + $"BeforeBoard='{pendingTransition.BoardBefore}', ExpectedBoard='{expected}', "
                                + $"ObservedBoard='{observations.Last().Board}', SpawnCandidates={validation.SpawnCandidates}, "
                                + "Reason='StableUnknownMergeDestination'");
                        }

                        // Candidate tiers are deliberately not promoted to Learned until
                        // they have three independent merge samples.  Their visual can
                        // therefore remain Unknown when it moves away from the merge
                        // destination.  After the bounded focused retries, use only a
                        // stable Unknown at a deterministic non-empty expected position
                        // for this transition.  Unknown spawn/empty positions are never
                        // filled by this path.
                        if (validation?.Status == Fruit2048TransitionValidationStatus.Ambiguous
                            && validation.Reason != null
                            && (validation.Reason.StartsWith("UnknownNonMergeCell@", StringComparison.Ordinal)
                                || string.Equals(validation.Reason, "ObservedBoardContainsUnknownCells",
                                    StringComparison.Ordinal)))
                        {
                            Fruit2048BoardReadResult resolvedExpectedCells = ResolveStableExpectedCells(
                                observations, expected);
                            if (resolvedExpectedCells != null)
                            {
                                observations[observations.Count - 1] = resolvedExpectedCells;
                                validation = transitionValidator.Validate(new Fruit2048TransitionValidationRequest
                                {
                                    DeviceName = deviceName,
                                    TransitionId = pendingTransition.TransitionId,
                                    BoardBefore = pendingTransition.BoardBefore,
                                    Move = pendingTransition.Move,
                                    ExpectedBoardAfterMove = expected,
                                    MergeOperations = merges,
                                    Observed = resolvedExpectedCells
                                });
                                pendingTransition.ValidationStatus = validation.Status;
                                logger?.Info($"[Fruit2048 Transition Validation] DeviceName='{deviceName}', "
                                    + $"TransitionId='{pendingTransition.TransitionId}', Status='{validation.Status}', "
                                    + $"BeforeBoard='{pendingTransition.BoardBefore}', ExpectedBoard='{expected}', "
                                    + $"ObservedBoard='{resolvedExpectedCells.Board}', SpawnCandidates={validation.SpawnCandidates}, "
                                    + "Reason='StableUnknownExpectedCells'");
                            }
                        }
                        read = observations.Last();
                        if (read.ScreenStatus == Fruit2048ScreenStatus.DeviceUnavailable)
                        {
                            pendingTransition.ValidationStatus = Fruit2048TransitionValidationStatus.DeviceUnavailable;
                            const string unavailable = "Mất kết nối ADB khi đang xác minh nước đi — đã dừng.";
                            Report(progress, deviceName, Fruit2048RuntimeStatus.Disconnected, read, moveCount,
                                pendingTransition.Move, unavailable, read.Error, null, null,
                                Fruit2048TransitionValidationStatus.DeviceUnavailable, pendingTransition);
                            return Result(deviceName, Fruit2048Outcome.DeviceUnavailable, moveCount, highestTile,
                                request.TargetTile, false, read.Error ?? unavailable);
                        }
                        // Do not learn from a contradictory transition, but do not end the
                        // whole session when repeated focused reads agree on a different,
                        // clean board. The next move is planned from this synchronized board.
                        // This deliberately excludes Unknown, stale-before, and one-frame
                        // observations, so it cannot bypass the pending-transition gate.
                        if (!firstLearningProof
                            && validation != null
                            && !validation.IsValidForLearning
                            && Fruit2048TransitionRecoveryPolicy.CanResynchronizeWithoutLearning(
                                observations, pendingTransition.BoardBefore))
                        {
                            logger?.Info($"[Fruit2048 Transition Recovery] DeviceName='{deviceName}', "
                                + $"TransitionId='{pendingTransition.TransitionId}', Action='ResynchronizeWithoutLearning', "
                                + $"Status='{validation.Status}', ObservedBoard='{read.Board}', Reason='{validation.Reason}'");
                            Report(progress, deviceName, Fruit2048RuntimeStatus.ValidatingTransition, read, moveCount,
                                pendingTransition.Move,
                                "Board đã ổn định nhưng khác mô phỏng — đồng bộ để tiếp tục, không học nước đi này.",
                                validation.Reason, null, null, validation.Status, pendingTransition);
                            boardBeforeLastMove = null;
                            previousMove = null;
                            previousEvidenceId = null;
                            pendingTransition = null;
                            continue;
                        }
                        if (!validation.IsValidForLearning)
                        {
                            if (firstLearningProof)
                            {
                                if (validation.Status == Fruit2048TransitionValidationStatus.Invalid)
                                    proofSession.TransitionInvalidCount++;
                                return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                                    request.TargetTile, Fruit2048Outcome.LearningProofFailed, validation.Reason,
                                    Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount, learningCatalog.Snapshot,
                                        proofTier2RecognitionConfirmed, "Failed"), proofSession,
                                    validation.Status == Fruit2048TransitionValidationStatus.Invalid
                                        ? Fruit2048LearningProofFailure.TransitionInvalid
                                        : Fruit2048LearningProofFailure.Tier2EvidenceRejected);
                            }
                            string diagnosticPath = null;
                            try
                            {
                                // Successful reads do not serialize PNGs during normal
                                // gameplay. Take one bounded, original-size diagnostic
                                // frame only now that this transition is terminal.
                                if (!observations.Any(value => value?.OriginalScreenshotPng != null
                                    && value.OriginalScreenshotPng.Length > 0))
                                {
                                    Fruit2048BoardReadResult diagnosticFrame = await reader.ReadAsync(
                                        deviceName, true, cancellationToken);
                                    if (diagnosticFrame != null)
                                        observations.Add(diagnosticFrame);
                                }
                                diagnosticPath = diagnosticStore?.Save(deviceName, teacherSession, pendingTransition.TransitionId,
                                    validation.Reason, pendingTransition.BoardBefore, expected, observations);
                            }
                            catch (Exception diagnosticException)
                            {
                                logger?.Error($"[Fruit2048 Learning Diagnostics] Could not persist transition '{pendingTransition.TransitionId}'.", diagnosticException);
                            }
                            logger?.Info($"[Fruit2048 Learning Diagnostics] DeviceName='{deviceName}', TransitionId='{pendingTransition.TransitionId}', Path='{diagnosticPath}'");
                            Report(progress, deviceName, Fruit2048RuntimeStatus.Failed, read, moveCount, previousMove,
                                "Transition không khớp simulator — đã tạm dừng.", validation.Reason);
                            return Result(deviceName, Fruit2048Outcome.BoardReadFailed, moveCount, highestTile,
                                request.TargetTile, false, validation.Reason);
                        }

                        // A valid transition always clears the gate. Only the active Teacher
                        // may submit visual learning evidence.
                        if (learningCoordinator != null && !learningCoordinator.CanLearn(deviceName))
                        {
                            logger?.Info($"[Fruit2048 Learning Gate] DeviceName='{deviceName}', "
                                + $"TransitionId='{pendingTransition.TransitionId}', CanLearn=False, "
                                + "Action='ClearValidatedTransitionWithoutCatalogWrite'");
                            boardBeforeLastMove = null;
                            previousMove = null;
                            previousEvidenceId = null;
                            pendingTransition = null;
                            continue;
                        }

                        // This is the sole point at which a Teacher can submit merge evidence.
                        logger?.Info($"[Fruit2048 Learning Gate] DeviceName='{deviceName}', "
                            + $"TransitionId='{pendingTransition.TransitionId}', CanLearn=True, "
                            + "Action='SubmitMergeEvidence'");
                        var learning = transitionLearner.Learn(deviceName,
                            pendingTransition.BoardBefore, pendingTransition.Move, observations,
                            pendingTransition.TransitionId,
                            firstLearningProof ? (int?)2 : null).ToList();
                        // A deterministic merge is the one safe moment to retain the
                        // value badge for a tier whose fruit artwork is not learned
                        // yet.  This is candidate-only evidence (never a promotion),
                        // but lets the next occurrence of Tier 4, Tier 5, Tier 6,
                        // etc. be recognised by its number instead of stopping the
                        // session just because the fruit visual is new.
                        learning.AddRange(ObserveValidatedDeterministicBadges(deviceName,
                            pendingTransition, expected, observations.Last()));
                        foreach (Fruit2048LearningResult item in learning)
                            logger?.Info($"[Fruit2048 Learning] DeviceName='{deviceName}', "
                                + $"Tier={item.Tier}, State='{item.State}', SampleCount={item.SampleCount}, "
                                + $"RequiredSamples={item.RequiredSamples}, Confidence={item.Confidence:F3}, "
                                + $"Evidence='{item.Evidence}', SourceTier={item.SourceTier}, "
                                + $"Move='{item.Move}', Action='{item.Action}'");
                        Fruit2048LearningResult conflict = learning.FirstOrDefault(item =>
                            item.Action == Fruit2048LearningAction.ConflictRejected);
                        if (conflict != null)
                        {
                            if (firstLearningProof)
                            {
                                proofSession.LearningConflictCount++;
                                return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                                    request.TargetTile, Fruit2048Outcome.LearningProofFailed,
                                    "LearningConflict: " + conflict.Error,
                                    Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount, learningCatalog.Snapshot,
                                        proofTier2RecognitionConfirmed, "Failed"), proofSession,
                                    Fruit2048LearningProofFailure.LearningConflict);
                            }
                            read = observations.Last();
                            read.Error = "LearningConflict: " + conflict.Error;
                        }
                        else
                        {
                            if (firstLearningProof)
                            {
                                foreach (Fruit2048LearningResult item in learning.Where(item => item.Tier == 2
                                    && item.SourceTier == 1 && item.Action != Fruit2048LearningAction.DuplicateIgnored))
                                {
                                    Fruit2048FirstLearningProofProgress proof = Fruit2048FirstLearningProofPolicy.CreateProgress(
                                        moveCount, learningCatalog.Snapshot, false,
                                        item.State == FruitTileLearningState.Learned ? "Tier2Learned" : "Tier2Candidate");
                                    proofTier2Learned = proof.Tier2Learned;
                                    proofSession.Tier1MergeTransitionsObserved = Math.Max(
                                        proofSession.Tier1MergeTransitionsObserved, item.SampleCount);
                                    proofSession.Tier2EvidenceAccepted = Math.Max(
                                        proofSession.Tier2EvidenceAccepted, item.SampleCount);
                                    proofSession.Tier2Learned = proof.Tier2Learned;
                                    if (proof.Tier2Learned && !proofSession.Tier2LearnedAtMove.HasValue)
                                        proofSession.Tier2LearnedAtMove = moveCount;
                                    proofSession.CatalogVersionEnd = learningCoordinator?.Teacher.CatalogVersion
                                        ?? proofSession.CatalogVersionStart;
                                    proof = EnrichProofProgress(proof, proofSession,
                                        Fruit2048LearningProofFailure.None);
                                    LogProof(deviceName, teacherSession,
                                        item.Action == Fruit2048LearningAction.CandidateConfirmed ? "Tier2CandidateConfirmed"
                                            : proof.Tier2Learned ? "Tier2Learned" : "Tier2Candidate",
                                        moveCount, proof, item.Evidence);
                                    Report(progress, deviceName, Fruit2048RuntimeStatus.Scanning, read, moveCount,
                                        previousMove, LearningMessage(item), null, null, proof);
                                }
                            }
                            // A validated merge result may be used for this in-memory
                            // board only.  This unblocks safe next-move simulation when
                            // the visual sample was too weak to enter the catalog.
                            read = ApplyValidatedMergeDestinations(observations.Last(), validation);
                            read = ApplyDeterministicLearning(read, learning);
                            if (learning.Count > 0)
                            {
                                Fruit2048LearningSnapshot snapshot = learningCatalog.Snapshot;
                                Fruit2048LearningResult latest = learning.Last();
                                logger?.Info($"[Fruit2048 Learning Progress] DeviceName='{deviceName}', "
                                    + $"Tier={latest.Tier}, State='{latest.State}', SampleCount={latest.SampleCount}, "
                                    + $"RequiredSamples={latest.RequiredSamples}, "
                                    + $"HighestObservedTier={snapshot.HighestObservedTier}, Mode='{snapshot.Mode}'");
                                Report(progress, deviceName, Fruit2048RuntimeStatus.Scanning,
                                    read, moveCount, previousMove,
                                    LearningMessage(learning.Last()), null);
                            }
                        }
                        if (teacherSession != null && HasAcceptedLearningEvidence(learning))
                        {
                            movesWithoutLearningEvidence = 0;
                            logger?.Info($"[Fruit2048 Learning Budget] DeviceName='{deviceName}', "
                                + $"TransitionId='{pendingTransition.TransitionId}', "
                                + "Action='ResetAfterAcceptedLearningEvidence'");
                        }
                        boardBeforeLastMove = null;
                        previousMove = null;
                        previousEvidenceId = null;
                        pendingTransition = null;
                    }
                    else if ((!read.Success || read.Board == null) && read.UnknownCells != null
                        && read.UnknownCells.Count > 0)
                    {
                        // Consumers are read-only.  They retain the existing bounded
                        // capture retry, but never submit catalog evidence.
                        for (int retry = 0; retry < 2 && read.UnknownCells.Count > 0; retry++)
                        {
                            await Task.Delay(200, cancellationToken);
                            read = await reader.ReadAsync(deviceName, cancellationToken);
                        }
                    }
                    highestTile = Math.Max(highestTile, read.HighestTile);
                    if (!read.Success || read.Board == null || !read.BoardRegion.HasValue)
                    {
                        int unknownCellCount = read.UnknownCells?.Count ?? 0;
                        bool hasUnknownCells = unknownCellCount > 0;
                        bool learningConflict = !string.IsNullOrWhiteSpace(read.Error)
                            && read.Error.StartsWith("LearningConflict", StringComparison.Ordinal);
                        string unknownMessage = initialFreshBoardBootstrap
                            ? "Board khởi đầu chưa nhận diện đủ. Hãy dùng Làm mới để tool học từ Tier 1."
                            : "Board hiện tại còn " + unknownCellCount
                              + " ô chưa nhận diện. Tool giữ nguyên board này; bấm Chạy để quét lại và tiếp tục "
                              + "từ vị trí hiện tại khi nhận diện đủ tin cậy.";
                        string failureMessage = hasUnknownCells ? unknownMessage : read.Error;
                        if (hasUnknownCells)
                        {
                            string diagnosticPath = diagnosticStore?.Save(deviceName, teacherSession, previousEvidenceId,
                                failureMessage, boardBeforeLastMove, null, new[] { read });
                            logger?.Info($"[Fruit2048 Learning Diagnostics] DeviceName='{deviceName}', TransitionId='{previousEvidenceId}', Path='{diagnosticPath}'");
                        }
                        Report(progress, deviceName,
                            hasUnknownCells && initialFreshBoardBootstrap
                                ? Fruit2048RuntimeStatus.NeedsFreshBoard
                                : hasUnknownCells ? Fruit2048RuntimeStatus.BoardUnknown
                                : Fruit2048RuntimeStatus.BoardUnknown,
                            read, moveCount, null, failureMessage, read.Error,
                            hasUnknownCells ? (Fruit2048BootstrapState?)(initialFreshBoardBootstrap
                                ? Fruit2048BootstrapState.NeedsFreshBoard
                                : Fruit2048BootstrapState.Learning)
                                : null);
                        if (firstLearningProof)
                            return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                                request.TargetTile, Fruit2048Outcome.LearningProofFailed, failureMessage,
                                Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount,
                                    learningCatalog.Snapshot, proofTier2RecognitionConfirmed, "Failed"), proofSession,
                                hasUnknownCells ? Fruit2048LearningProofFailure.CandidateRecognitionUnsafe
                                    : Fruit2048LearningProofFailure.Tier2EvidenceRejected);
                        return Result(deviceName,
                            read.ScreenStatus == Fruit2048ScreenStatus.NotOpen
                                ? Fruit2048Outcome.ScreenNotOpen
                                : learningConflict
                                    ? Fruit2048Outcome.BoardReadFailed
                                    : hasUnknownCells && initialFreshBoardBootstrap
                                        ? Fruit2048Outcome.NeedsFreshBoard
                                        : Fruit2048Outcome.BoardReadFailed,
                            moveCount, highestTile, request.TargetTile, false,
                            learningConflict ? read.Error : failureMessage);
                    }

                    Report(progress, deviceName, Fruit2048RuntimeStatus.Playing, read,
                        moveCount, null, "Đang chơi Fruit 2048", null);
                    if (!firstLearningProof && read.Board.HasReached(request.TargetTile))
                    {
                        Report(progress, deviceName, Fruit2048RuntimeStatus.TargetReached,
                            read, moveCount, null, "Đã đạt mục tiêu", null);
                        return Result(deviceName, Fruit2048Outcome.TargetReached, moveCount,
                            read.HighestTile, request.TargetTile, false, null);
                    }

                    Fruit2048Move move;
                    if (!solver.TryChooseMove(read.Board, out move))
                    {
                        if (!firstLearningProof && request.AutoRefresh && !refreshUsedForCurrentDeadBoard
                            && await reader.TryTapRefreshAsync(deviceName, cancellationToken))
                        {
                            refreshUsedForCurrentDeadBoard = true;
                            await Task.Delay(500, cancellationToken);
                            continue;
                        }
                        if (firstLearningProof)
                            return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                                request.TargetTile, Fruit2048Outcome.LearningProofFailed, "Proof dừng: không còn nước đi.",
                                Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount, learningCatalog.Snapshot,
                                    proofTier2RecognitionConfirmed, "Failed"), proofSession,
                                proofTier2Learned ? Fruit2048LearningProofFailure.Tier2LearnedButNotRecognized
                                    : Fruit2048LearningProofFailure.NoTier1MergeObserved);
                        Report(progress, deviceName, Fruit2048RuntimeStatus.NoMoves, read,
                            moveCount, null, "Không còn nước đi", null);
                        return Result(deviceName, Fruit2048Outcome.NoMoves, moveCount,
                            read.HighestTile, request.TargetTile, false, null);
                    }

                    refreshUsedForCurrentDeadBoard = false;
                    if (firstLearningProof && moveCount >= Fruit2048FirstLearningProofPolicy.MaxProofMoves)
                        return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                            request.TargetTile, Fruit2048Outcome.LearningProofFailed,
                            "Proof chưa hoàn tất trong 50 nước đi.",
                            Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount, learningCatalog.Snapshot,
                                proofTier2RecognitionConfirmed, "Failed"), proofSession,
                            proofTier2Learned ? Fruit2048LearningProofFailure.Tier2LearnedButNotRecognized
                                : Fruit2048LearningProofFailure.Timeout);
                    if (!firstLearningProof && teacherSession != null && request.TeacherMoveLimit > 0
                        && movesWithoutLearningEvidence >= request.TeacherMoveLimit)
                    {
                        string moveLimitMessage = "Đã đạt giới hạn " + request.TeacherMoveLimit
                            + " lượt liên tiếp chưa có bằng chứng học mới.";
                        Report(progress, deviceName, Fruit2048RuntimeStatus.Idle, read,
                            moveCount, null, moveLimitMessage, null);
                        return Result(deviceName, Fruit2048Outcome.MoveLimitReached, moveCount,
                            read.HighestTile, request.TargetTile, false, moveLimitMessage);
                    }
                    if (pendingTransition != null)
                    {
                        const string pendingMessage = "Transition trước chưa được xác minh.";
                        Report(progress, deviceName, Fruit2048RuntimeStatus.Failed, read,
                            moveCount, previousMove, pendingMessage, pendingMessage);
                        return Result(deviceName, Fruit2048Outcome.BoardReadFailed, moveCount,
                            read.HighestTile, request.TargetTile, false, pendingMessage);
                    }
                    boardBeforeLastMove = read.Board;
                    previousMove = move;
                    previousEvidenceId = deviceName + ":" + DateTimeOffset.UtcNow.Ticks
                        + ":" + (moveCount + 1);
                    Fruit2048Board expectedBoard = boardBeforeLastMove.Simulate(move);
                    if (BoardsEqual(boardBeforeLastMove, expectedBoard))
                    {
                        const string illegalMove = "Solver chọn nước đi không làm thay đổi board.";
                        return Result(deviceName, Fruit2048Outcome.BoardReadFailed, moveCount,
                            read.HighestTile, request.TargetTile, false, illegalMove);
                    }
                    pendingTransition = new Fruit2048PendingTransition
                    {
                        DeviceName = deviceName,
                        TransitionId = previousEvidenceId,
                        MoveSequence = moveCount + 1,
                        BoardBefore = boardBeforeLastMove,
                        Move = move,
                        ExpectedBoardAfterMove = expectedBoard,
                        MergeOperations = Fruit2048TransitionLearner.GetMergeOperations(boardBeforeLastMove, move),
                        BeforeCaptureFingerprint = read.CaptureFingerprint,
                        StartedAtUtc = DateTimeOffset.UtcNow,
                        SwipeStartedAtUtc = DateTimeOffset.UtcNow
                    };
                    logger?.Info($"[Fruit2048 Move Plan] DeviceName='{deviceName}', FruitSessionId='{request.FruitSessionId}', "
                        + $"MoveSequence={pendingTransition.MoveSequence}, TransitionId='{pendingTransition.TransitionId}', "
                        + $"BoardBefore='{pendingTransition.BoardBefore}', Move='{move}', "
                        + $"ExpectedBoardAfterMove='{expectedBoard}', BoardWouldChange=true");
                    var stopwatch = Stopwatch.StartNew();
                    Fruit2048SwipeExecution swipe = await swipeExecutor.ExecuteAsync(deviceName, read.Board, move, read.BoardRegion.Value,
                        read.ScreenWidth, read.ScreenHeight, cancellationToken);
                    stopwatch.Stop();
                    pendingTransition.SwipeCompletedAtUtc = DateTimeOffset.UtcNow;
                    pendingTransition.SwipeDurationMs = stopwatch.ElapsedMilliseconds;
                    moveCount++;
                    if (teacherSession != null) movesWithoutLearningEvidence++;
                    logger?.Info($"[Fruit2048 Move] DeviceName='{deviceName}', "
                        + $"MoveNumber={moveCount}, Move='{move}', HighestTileBefore={read.HighestTile}, "
                        + $"EmptyCellsBefore={read.Board.EmptyCellCount}, SwipeSent=true, "
                        + $"DurationMs={stopwatch.ElapsedMilliseconds}");
                    logger?.Info($"[Fruit2048 Swipe] DeviceName='{deviceName}', TransitionId='{pendingTransition.TransitionId}', "
                        + $"ADBSerial='{deviceName}', StartX={swipe.StartX}, StartY={swipe.StartY}, EndX={swipe.EndX}, EndY={swipe.EndY}, "
                        + $"DurationMs={swipe.DurationMs}, BoardBounds='{FormatRegion(read.BoardRegion)}', CommandAccepted={swipe.CommandAccepted}");
                    Report(progress, deviceName, Fruit2048RuntimeStatus.Playing, read,
                        moveCount, move, "Đang chơi Fruit 2048", null);
                    Report(progress, deviceName, Fruit2048RuntimeStatus.WaitingPostMove, read,
                        moveCount, move, "Đang chờ xác minh nước đi", null);
                    var postSwipeDelayMs = Fruit2048PacingPolicy.GetPostSwipeDelay(read.Learning);
                    logger?.Info($"[Fruit2048 Pace] DeviceName='{deviceName}', "
                        + $"FruitSessionId='{request.FruitSessionId}', TransitionId='{pendingTransition.TransitionId}', "
                        + $"RecognitionMode='{read.Learning?.Mode.ToString() ?? "Unknown"}', "
                        + $"PostSwipeDelayMs={postSwipeDelayMs}.");
                    await Task.Delay(postSwipeDelayMs, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                if (firstLearningProof)
                    return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                        request.TargetTile, Fruit2048Outcome.LearningProofFailed, "Đã dừng",
                        Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount,
                            learningCatalog.Snapshot, proofTier2RecognitionConfirmed, "Failed"), proofSession,
                        Fruit2048LearningProofFailure.Cancelled);
                Report(progress, deviceName, Fruit2048RuntimeStatus.Cancelled, null,
                    moveCount, null, "Đã dừng", null);
                return Result(deviceName, Fruit2048Outcome.Cancelled, moveCount,
                    highestTile, request.TargetTile, true, null);
            }
            catch (Exception exception)
            {
                logger?.Error("[Fruit2048 Result] Automation failed.", exception);
                if (firstLearningProof)
                    return FinishProof(progress, deviceName, teacherSession, moveCount, highestTile,
                        request.TargetTile, Fruit2048Outcome.LearningProofFailed, exception.Message,
                        Fruit2048FirstLearningProofPolicy.CreateProgress(moveCount,
                            learningCatalog.Snapshot, proofTier2RecognitionConfirmed, "Failed"), proofSession,
                        Fruit2048LearningProofFailure.Unexpected);
                Report(progress, deviceName, Fruit2048RuntimeStatus.Failed, null,
                    moveCount, null, "Lỗi Fruit 2048", exception.Message);
                return Result(deviceName, Fruit2048Outcome.Failed, moveCount,
                    highestTile, request.TargetTile, false, exception.Message);
            }
            finally
            {
                (reader as IFruit2048BootstrapSessionController)?.CompleteInitialBootstrap(deviceName);
                if (teacherSession != null) learningCoordinator?.ReleaseTeacher(deviceName, teacherSession, "Stopped");
                lease.Dispose();
            }
        }

        public async Task<Fruit2048BoardReadResult> ScanAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            IDeviceAutomationLease lease;
            if (!ownership.TryAcquire(deviceName, DeviceAutomationOwner.Fruit2048, out lease))
                return Unavailable("Thiết bị đang được sử dụng bởi " + ownership.GetOwner(deviceName) + ".");
            try
            {
                // A manual Scan Board is the only UI operation allowed to
                // establish the initial fresh-board consensus before Auto is
                // enabled.  The reader keeps this flag operation-local and
                // clears it in finally, so post-swipe reads cannot reuse it.
                IFruit2048BootstrapSessionController bootstrap = reader as IFruit2048BootstrapSessionController;
                bootstrap?.BeginInitialBootstrap(deviceName);
                if (navigation != null)
                {
                    Fruit2048NavigationResult navigationResult = await navigation.EnsureFruit2048ScreenAsync(
                        deviceName, null, cancellationToken);
                    if (!navigationResult.Success) return Unavailable(navigationResult.Error);
                }
                return await reader.ReadAsync(deviceName, cancellationToken);
            }
            finally
            {
                (reader as IFruit2048BootstrapSessionController)?.CompleteInitialBootstrap(deviceName);
                lease.Dispose();
            }
        }

        public async Task<Fruit2048BoardReadResult> SendManualMoveAsync(string deviceName,
            Fruit2048Move move, CancellationToken cancellationToken)
        {
            IDeviceAutomationLease lease;
            if (!ownership.TryAcquire(deviceName, DeviceAutomationOwner.Fruit2048, out lease))
                return Unavailable("Thiết bị đang được sử dụng bởi " + ownership.GetOwner(deviceName) + ".");
            try
            {
                Fruit2048BoardReadResult before = await reader.ReadAsync(deviceName, cancellationToken);
                if (!before.Success || !before.BoardRegion.HasValue) return before;
                if (!before.Board.CanMove(move)) return before;
                await swipeExecutor.ExecuteAsync(deviceName, before.Board, move, before.BoardRegion.Value,
                    before.ScreenWidth, before.ScreenHeight, cancellationToken);
                return await reader.ReadAsync(deviceName, cancellationToken);
            }
            finally { lease.Dispose(); }
        }

        private static Fruit2048BoardReadResult Unavailable(string error) =>
            new Fruit2048BoardReadResult
            {
                ScreenStatus = Fruit2048ScreenStatus.CaptureUnavailable,
                Success = false,
                Cells = new Fruit2048Cell[0],
                UnknownCells = new Fruit2048Cell[0],
                Error = error
            };

        private static bool HasAcceptedLearningEvidence(IReadOnlyList<Fruit2048LearningResult> learning)
        {
            return learning != null && learning.Any(item => item != null
                && (item.Action == Fruit2048LearningAction.CandidateAdded
                    || item.Action == Fruit2048LearningAction.CandidateConfirmed
                    || item.Action == Fruit2048LearningAction.Learned));
        }

        private Fruit2048BoardReadResult ApplyDeterministicLearning(
            Fruit2048BoardReadResult observation,
            IReadOnlyList<Fruit2048LearningResult> learning)
        {
            if (observation?.Cells == null || learning == null || learning.Count == 0)
                return observation;
            var cells = observation.Cells.Select(cell => new Fruit2048Cell
            {
                Row = cell.Row, Column = cell.Column, Bounds = cell.Bounds,
                Tier = cell.Tier, Value = cell.Value, Confidence = cell.Confidence,
                RecognitionSource = cell.RecognitionSource, Fingerprint = cell.Fingerprint,
                BadgeBounds = cell.BadgeBounds, BadgeFingerprint = cell.BadgeFingerprint,
                BadgeConfidence = cell.BadgeConfidence, BadgeBestTier = cell.BadgeBestTier,
                BadgeSecondBestTier = cell.BadgeSecondBestTier,
                BadgeSecondBestConfidence = cell.BadgeSecondBestConfidence
            }).ToList();
            foreach (Fruit2048LearningResult item in learning.Where(item =>
                item.Action != Fruit2048LearningAction.ConflictRejected))
            {
                Fruit2048Cell cell = cells.FirstOrDefault(value => value.Row == item.DestinationRow
                    && value.Column == item.DestinationColumn && !value.Value.HasValue);
                if (cell == null) continue;
                cell.Tier = item.Tier;
                cell.Value = FruitTierCatalog.ToValue(item.Tier);
                cell.Confidence = 1.0;
            }
            Fruit2048BoardReadResult resolved = Fruit2048BoardAssembler.Assemble(cells,
                observation.DurationMs);
            resolved.ScreenStatus = observation.ScreenStatus;
            resolved.MissingAssets = observation.MissingAssets;
            resolved.BoardRegion = observation.BoardRegion;
            resolved.ScreenWidth = observation.ScreenWidth;
            resolved.ScreenHeight = observation.ScreenHeight;
            resolved.Learning = learningCatalog.Snapshot;
            resolved.FastPathCells = cells.Count(cell =>
                cell.RecognitionSource == Fruit2048RecognitionSource.FastFingerprint);
            resolved.StaticCells = cells.Count(cell => cell.RecognitionSource == Fruit2048RecognitionSource.StaticSeed);
            resolved.PrototypeCells = cells.Count(cell => cell.RecognitionSource == Fruit2048RecognitionSource.PrototypeMatch);
            resolved.BadgeBootstrapCells = cells.Count(cell => cell.RecognitionSource == Fruit2048RecognitionSource.TierBadgeBootstrap);
            resolved.FallbackCells = resolved.StaticCells + resolved.PrototypeCells;
            resolved.BootstrapState = resolved.Success
                ? (resolved.Learning.Mode == Fruit2048RecognitionMode.Fast
                    ? Fruit2048BootstrapState.Ready : Fruit2048BootstrapState.Learning)
                : Fruit2048BootstrapState.NeedsFreshBoard;
            return resolved;
        }

        private Fruit2048BoardReadResult ApplyValidatedMergeDestinations(
            Fruit2048BoardReadResult observation, Fruit2048TransitionValidationResult validation)
        {
            if (observation?.Cells == null || validation?.MergeDestinations == null)
                return observation;
            var cells = observation.Cells.Select(cell => new Fruit2048Cell
            {
                Row = cell.Row, Column = cell.Column, Bounds = cell.Bounds,
                Tier = cell.Tier, Value = cell.Value, Confidence = cell.Confidence,
                RecognitionSource = cell.RecognitionSource, Fingerprint = cell.Fingerprint,
                BadgeBounds = cell.BadgeBounds, BadgeFingerprint = cell.BadgeFingerprint,
                BadgeConfidence = cell.BadgeConfidence, BadgeBestTier = cell.BadgeBestTier,
                BadgeSecondBestTier = cell.BadgeSecondBestTier,
                BadgeSecondBestConfidence = cell.BadgeSecondBestConfidence
            }).ToList();
            foreach (Fruit2048MergeOperation merge in validation.MergeDestinations)
            {
                Fruit2048Cell cell = cells.FirstOrDefault(value => value.Row == merge.DestinationRow
                    && value.Column == merge.DestinationColumn);
                if (cell == null || cell.Value.HasValue) continue;
                cell.Tier = merge.ResultTier;
                cell.Value = FruitTierCatalog.ToValue(merge.ResultTier);
                cell.Confidence = 1.0;
            }
            Fruit2048BoardReadResult resolved = Fruit2048BoardAssembler.Assemble(cells, observation.DurationMs);
            resolved.ScreenStatus = observation.ScreenStatus;
            resolved.MissingAssets = observation.MissingAssets;
            resolved.BoardRegion = observation.BoardRegion;
            resolved.ScreenWidth = observation.ScreenWidth;
            resolved.ScreenHeight = observation.ScreenHeight;
            resolved.Learning = learningCatalog.Snapshot;
            resolved.BootstrapState = Fruit2048BootstrapState.Learning;
            return resolved;
        }

        private IReadOnlyList<Fruit2048LearningResult> ObserveValidatedDeterministicBadges(
            string deviceName, Fruit2048PendingTransition pendingTransition,
            Fruit2048Board expected, Fruit2048BoardReadResult observation)
        {
            var results = new List<Fruit2048LearningResult>();
            if (pendingTransition == null || expected == null || observation?.Cells == null)
                return results;

            foreach (Fruit2048Cell destination in observation.Cells.Where(cell =>
                cell.RecognitionSource == Fruit2048RecognitionSource.DeterministicTransition
                && expected[cell.Row, cell.Column] > 0
                && cell.Value == expected[cell.Row, cell.Column]))
            {
                if (destination?.Fingerprint == null || destination.BadgeFingerprint == null)
                    continue;

                int tier = FruitTierCatalog.ToTier(destination.Value.Value);
                // Transition validation has already proved this non-empty expected
                // position. A badge sample is retained as candidate evidence only;
                // it cannot promote a tier by itself. Spawn positions are excluded
                // because their expected value is zero.
                string evidenceId = pendingTransition.TransitionId + ":badge:tier:"
                    + tier + ":" + destination.Row + ":" + destination.Column
                    + ":" + destination.BadgeFingerprint.AverageHash;
                Fruit2048LearningResult result = learningCoordinator == null
                    ? learningCatalog.ObserveBadgeBootstrap(tier, destination.Fingerprint,
                        destination.BadgeFingerprint, evidenceId, destination.Row, destination.Column)
                    : learningCoordinator.ObserveBadgeBootstrap(deviceName, tier,
                        destination.Fingerprint, destination.BadgeFingerprint, evidenceId,
                        destination.Row, destination.Column);
                results.Add(result);
            }
            return results;
        }

        private static string LearningMessage(Fruit2048LearningResult result)
        {
            if (result.Action == Fruit2048LearningAction.Learned)
                return "Đã học Tier " + result.Tier;
            return "Đang học Tier " + result.Tier + " — mẫu "
                + result.SampleCount + "/" + result.RequiredSamples;
        }

        private Fruit2048RunResult Result(string deviceName, Fruit2048Outcome outcome,
            int moveCount, int highestTile, int targetTile, bool cancelled, string error)
        {
            logger?.Info($"[Fruit2048 Result] DeviceName='{deviceName}', Outcome='{outcome}', "
                + $"MoveCount={moveCount}, HighestTile={highestTile}, TargetTile={targetTile}, "
                + $"Cancellation={cancelled}, Error='{error}'");
            return new Fruit2048RunResult
            {
                DeviceName = deviceName,
                Outcome = outcome,
                MoveCount = moveCount,
                HighestTile = highestTile,
                TargetTile = targetTile,
                WasCancelled = cancelled,
                Error = error
            };
        }

        private Fruit2048RunResult FinishProof(IProgress<Fruit2048Progress> progress,
            string deviceName, string teacherSessionId, int moveCount, int highestTile,
            int targetTile, Fruit2048Outcome outcome, string message,
            Fruit2048FirstLearningProofProgress proof, FirstLearningProofSession proofSession,
            Fruit2048LearningProofFailure failure)
        {
            string stage = outcome == Fruit2048Outcome.LearningProofCompleted ? "Completed" : "Failed";
            proofSession.Moves = moveCount;
            proofSession.CatalogVersionEnd = learningCoordinator?.Teacher.CatalogVersion ?? proofSession.CatalogVersionStart;
            proof = EnrichProofProgress(proof, proofSession, failure);
            proofSession.Tier2Learned = proof.Tier2Learned;
            proofSession.Tier2RecognitionConfirmed = proof.Tier2RecognitionConfirmed;
            string reportPath = null;
            try
            {
                reportPath = proofStore?.Save(new Fruit2048LearningProofReport
                {
                    DeviceName = deviceName,
                    FruitSessionId = proofSession.FruitSessionId,
                    TeacherSessionId = teacherSessionId,
                    StartedAtUtc = proofSession.StartedAtUtc,
                    EndedAtUtc = DateTimeOffset.UtcNow,
                    InitialEmptyCells = proofSession.InitialEmptyCells,
                    InitialTier1Cells = proofSession.InitialTier1Cells,
                    InitialUnknownCells = proofSession.InitialUnknownCells,
                    Moves = proofSession.Moves,
                    Tier1MergeTransitionsObserved = proofSession.Tier1MergeTransitionsObserved,
                    Tier2EvidenceAccepted = proofSession.Tier2EvidenceAccepted,
                    Tier2EvidenceRejected = proofSession.Tier2EvidenceRejected,
                    Tier2Learned = proofSession.Tier2Learned,
                    Tier2LearnedAtMove = proofSession.Tier2LearnedAtMove,
                    Tier2RecognitionConfirmed = proofSession.Tier2RecognitionConfirmed,
                    CatalogVersionStart = proofSession.CatalogVersionStart,
                    CatalogVersionEnd = proofSession.CatalogVersionEnd,
                    TransitionInvalidCount = proofSession.TransitionInvalidCount,
                    LearningConflictCount = proofSession.LearningConflictCount,
                    Outcome = outcome.ToString(),
                    FailureReason = failure
                });
            }
            catch (Exception exception)
            {
                logger?.Error("[Fruit2048 Learning Proof] Could not persist report.", exception);
                outcome = Fruit2048Outcome.LearningProofFailed;
                failure = Fruit2048LearningProofFailure.CatalogPersistFailed;
                stage = "Failed";
                message = "Không thể lưu catalog/report học Tier 2.";
                proof.Failure = failure;
            }
            LogProof(deviceName, teacherSessionId, stage, moveCount, proof,
                outcome == Fruit2048Outcome.LearningProofCompleted ? null : message);
            logger?.Info($"[Fruit2048 Learning Proof] DeviceName='{deviceName}', Stage='{stage}', ReportPath='{reportPath}'");
            Report(progress, deviceName,
                outcome == Fruit2048Outcome.LearningProofCompleted ? Fruit2048RuntimeStatus.Completed : Fruit2048RuntimeStatus.Failed,
                null, moveCount, null, message,
                outcome == Fruit2048Outcome.LearningProofCompleted ? null : message, null, proof);
            return Result(deviceName, outcome, moveCount, highestTile, targetTile, false,
                outcome == Fruit2048Outcome.LearningProofCompleted ? null : message);
        }

        private static Fruit2048FirstLearningProofProgress EnrichProofProgress(
            Fruit2048FirstLearningProofProgress proof, FirstLearningProofSession state,
            Fruit2048LearningProofFailure failure)
        {
            proof = proof ?? new Fruit2048FirstLearningProofProgress();
            proof.FruitSessionId = state?.FruitSessionId;
            proof.CatalogVersion = state?.CatalogVersionEnd ?? state?.CatalogVersionStart ?? 0;
            proof.InitialKnownCells = state == null ? 0 : state.InitialEmptyCells + state.InitialTier1Cells;
            proof.InitialEmptyCells = state?.InitialEmptyCells ?? 0;
            proof.InitialTier1Cells = state?.InitialTier1Cells ?? 0;
            proof.InitialUnknownCells = state?.InitialUnknownCells ?? 0;
            proof.Failure = failure;
            return proof;
        }

        private void LogProof(string deviceName, string teacherSessionId, string stage, int movesUsed,
            Fruit2048FirstLearningProofProgress proof, string transitionId)
        {
            logger?.Info($"[Fruit2048 Learning Proof] DeviceName='{deviceName}', FruitSessionId='{proof?.FruitSessionId}', "
                + $"TeacherSessionId='{teacherSessionId}', Stage='{stage}', MoveSequence={movesUsed}, "
                + $"TransitionId='{transitionId}', Tier=2, SampleCount={proof?.Tier2SampleCount ?? 0}, "
                + $"EmptyCells={proof?.InitialEmptyCells ?? 0}, Tier1Cells={proof?.InitialTier1Cells ?? 0}, "
                + $"UnknownCells={proof?.InitialUnknownCells ?? 0}, "
                + $"MaxMoves={proof?.MaxMoves ?? Fruit2048FirstLearningProofPolicy.MaxProofMoves}, "
                + $"RequiredSamples={proof?.RequiredSamples ?? FruitTileLearningCatalog.RequiredSamples}, "
                + $"CatalogVersion={proof?.CatalogVersion ?? 0}, "
                + $"Tier2Learned={proof?.Tier2Learned ?? false}, "
                + $"Tier2RecognitionConfirmed={proof?.Tier2RecognitionConfirmed ?? false}, "
                + $"Outcome='{stage}', Reason='{proof?.Failure}'");
        }

        private void Report(IProgress<Fruit2048Progress> progress, string deviceName,
            Fruit2048RuntimeStatus status, Fruit2048BoardReadResult read, int moveCount,
            Fruit2048Move? lastMove, string message, string error,
            Fruit2048BootstrapState? bootstrapOverride = null,
            Fruit2048FirstLearningProofProgress firstLearningProof = null,
            Fruit2048TransitionValidationStatus? transitionStatus = null,
            Fruit2048PendingTransition pendingTransition = null)
        {
            Fruit2048LearningSnapshot learning = learningCatalog.Snapshot;
            progress?.Report(new Fruit2048Progress
            {
                DeviceName = deviceName,
                Status = status,
                Board = read?.Board,
                Cells = read?.Cells,
                HighestTile = read?.HighestTile ?? 0,
                MoveCount = moveCount,
                LastMove = lastMove,
                Message = message,
                Error = error,
                RecognitionMode = learning.Mode,
                KnownTierCount = learning.KnownTierCount,
                HighestObservedTier = learning.HighestObservedTier,
                LearningProfiles = learning.Profiles,
                LearningMessage = message != null && (message.StartsWith("Đang học Tier ")
                    || message.StartsWith("Đã học Tier ")
                    || status == Fruit2048RuntimeStatus.NeedsFreshBoard
                    || status == Fruit2048RuntimeStatus.MissingSeeds) ? message : null,
                BootstrapState = bootstrapOverride ?? read?.BootstrapState
                    ?? (learning.Mode == Fruit2048RecognitionMode.Fast
                        ? Fruit2048BootstrapState.Ready : Fruit2048BootstrapState.Learning),
                UnknownCellCount = read?.UnknownCells?.Count ?? 0,
                TransitionStatus = transitionStatus,
                PendingTransition = pendingTransition != null,
                TransitionId = pendingTransition?.TransitionId,
                SpawnCandidateCount = transitionStatus.HasValue && pendingTransition?.MergeOperations != null
                    ? pendingTransition.MergeOperations.Count : 0,
                FirstLearningProof = firstLearningProof
            });
        }

        private static bool BoardsEqual(Fruit2048Board left, Fruit2048Board right)
        {
            if (left == null || right == null) return false;
            for (int row = 0; row < Fruit2048Board.Size; row++)
            for (int column = 0; column < Fruit2048Board.Size; column++)
                if (left[row, column] != right[row, column]) return false;
            return true;
        }

        private static bool HasStableUnknownMergeEvidence(
            IReadOnlyList<Fruit2048BoardReadResult> observations,
            Fruit2048TransitionValidationResult validation)
        {
            if (observations == null || validation?.MergeDestinations == null
                || validation.MergeDestinations.Count == 0) return false;

            foreach (Fruit2048MergeOperation merge in validation.MergeDestinations)
            {
                Fruit2048Cell[] unresolved = observations
                    .Select(observation => observation?.Cells?.FirstOrDefault(cell =>
                        cell.Row == merge.DestinationRow && cell.Column == merge.DestinationColumn))
                    .Where(cell => cell != null && !cell.Value.HasValue && cell.Fingerprint != null)
                    .ToArray();
                if (unresolved.Length < 2) return false;
                // Only two consecutive post-swipe captures are needed. Older
                // frames may belong to the animation phase and must not reject
                // a stable final frame.
                if (!AreStableExpectedCellFingerprints(unresolved[unresolved.Length - 1],
                    unresolved[unresolved.Length - 2])) return false;
            }
            return true;
        }

        private static Fruit2048BoardReadResult ResolveStableExpectedCells(
            IReadOnlyList<Fruit2048BoardReadResult> observations, Fruit2048Board expected)
        {
            if (observations == null || observations.Count < 2 || expected == null) return null;
            Fruit2048BoardReadResult latest = observations[observations.Count - 1];
            if (latest?.Cells == null || latest.UnknownCells == null || latest.UnknownCells.Count == 0) return null;

            var cells = latest.Cells.Select(CloneCell).ToList();
            int resolvedCount = 0;
            foreach (Fruit2048Cell unknown in latest.UnknownCells)
            {
                int expectedValue = expected[unknown.Row, unknown.Column];
                // A zero in the simulated board may be either empty or the
                // spawn location.  It is deliberately not inferred here.  Do
                // still process other deterministic non-empty cells instead of
                // abandoning the entire transition because such a cell exists.
                if (expectedValue == 0) continue;
                if (unknown.Fingerprint == null) return null;

                Fruit2048Cell[] repeatedCells = observations
                    .Select(observation => observation?.Cells?.FirstOrDefault(cell =>
                        cell.Row == unknown.Row && cell.Column == unknown.Column))
                    .Where(cell => cell != null)
                    .ToArray();
                // A recognised value which contradicts the deterministic result is
                // evidence against completion. A recognised value equal to the
                // expected value is positive evidence, however, and must not make
                // the retry sequence fail simply because one frame classified the
                // same cell before another frame did not.
                if (repeatedCells.Any(cell => cell.Value.HasValue && cell.Value.Value != expectedValue))
                    return null;

                FruitTileVisualFingerprint[] unknownFingerprints = repeatedCells
                    .Where(cell => !cell.Value.HasValue && cell.Fingerprint != null)
                    .Select(cell => cell.Fingerprint)
                    .ToArray();
                // Use the two latest stable unresolved observations. This is still
                // limited to an expected non-empty deterministic position and never
                // assigns an unknown spawn/empty cell. It prevents a single earlier
                // animation/partial read from discarding otherwise consistent later
                // evidence and stopping the teacher after a valid move.
                if (unknownFingerprints.Length < 2)
                    return null;
                Fruit2048Cell baselineCell = repeatedCells.LastOrDefault(cell =>
                    !cell.Value.HasValue && cell.Fingerprint != null);
                Fruit2048Cell confirmationCell = repeatedCells.Reverse().Skip(1).FirstOrDefault(cell =>
                    !cell.Value.HasValue && cell.Fingerprint != null);
                if (!AreStableExpectedCellFingerprints(baselineCell, confirmationCell))
                    return null;

                Fruit2048Cell resolved = cells.FirstOrDefault(cell => cell.Row == unknown.Row
                    && cell.Column == unknown.Column);
                if (resolved == null) return null;
                resolved.Value = expectedValue;
                resolved.Tier = FruitTierCatalog.ToTier(expectedValue);
                resolved.Confidence = 1.0;
                resolved.RecognitionSource = Fruit2048RecognitionSource.DeterministicTransition;
                resolvedCount++;
            }

            if (resolvedCount == 0) return null;
            Fruit2048BoardReadResult result = Fruit2048BoardAssembler.Assemble(cells, latest.DurationMs);
            if (!result.Success) return null;
            result.ScreenStatus = latest.ScreenStatus;
            result.MissingAssets = latest.MissingAssets;
            result.BoardRegion = latest.BoardRegion;
            result.ScreenWidth = latest.ScreenWidth;
            result.ScreenHeight = latest.ScreenHeight;
            result.Learning = latest.Learning;
            result.BootstrapState = latest.BootstrapState;
            result.FastPathCells = latest.FastPathCells;
            result.StaticCells = latest.StaticCells;
            result.PrototypeCells = latest.PrototypeCells;
            result.BadgeBootstrapCells = latest.BadgeBootstrapCells;
            result.FallbackCells = latest.FallbackCells;
            result.EmptyCells = latest.EmptyCells;
            result.Tier1Cells = latest.Tier1Cells;
            result.EmptyScoreMin = latest.EmptyScoreMin;
            result.EmptyScoreAverage = latest.EmptyScoreAverage;
            result.EmptyScoreMax = latest.EmptyScoreMax;
            result.OriginalScreenshotPng = latest.OriginalScreenshotPng;
            result.CaptureFingerprint = latest.CaptureFingerprint;
            return result;
        }

        private static bool AreStableExpectedCellFingerprints(Fruit2048Cell current,
            Fruit2048Cell previous)
        {
            if (current?.Fingerprint == null || previous?.Fingerprint == null) return false;
            if (FruitFingerprintDistance.Hamming(current.Fingerprint.AverageHash,
                previous.Fingerprint.AverageHash) <= StableExpectedCellHashDistance)
                return true;

            return current.BadgeFingerprint != null && previous.BadgeFingerprint != null
                && FruitFingerprintDistance.Hamming(current.BadgeFingerprint.AverageHash,
                    previous.BadgeFingerprint.AverageHash) <= StableExpectedBadgeHashDistance;
        }

        private static Fruit2048Cell CloneCell(Fruit2048Cell cell) => new Fruit2048Cell
        {
            Row = cell.Row,
            Column = cell.Column,
            Bounds = cell.Bounds,
            Tier = cell.Tier,
            Value = cell.Value,
            Confidence = cell.Confidence,
            RecognitionSource = cell.RecognitionSource,
            Fingerprint = cell.Fingerprint,
            BadgeBounds = cell.BadgeBounds,
            BadgeFingerprint = cell.BadgeFingerprint,
            BadgeConfidence = cell.BadgeConfidence,
            BadgeBestTier = cell.BadgeBestTier,
            BadgeSecondBestTier = cell.BadgeSecondBestTier,
            BadgeSecondBestConfidence = cell.BadgeSecondBestConfidence,
            SeedDiagnostics = cell.SeedDiagnostics
        };

        private static string FormatRegion(ImageRegion? region) => region.HasValue
            ? region.Value.X + "," + region.Value.Y + "," + region.Value.Width + "," + region.Value.Height
            : string.Empty;

        private void LogDevice(string deviceName, string owner, string status) =>
            logger?.Info($"[Fruit2048 Device] DeviceName='{deviceName}', Owner='{owner}', Status='{status}'");

        private sealed class FirstLearningProofSession
        {
            public string FruitSessionId { get; set; }
            public string TeacherSessionId { get; set; }
            public DateTimeOffset StartedAtUtc { get; set; }
            public int InitialEmptyCells { get; set; }
            public int InitialTier1Cells { get; set; }
            public int InitialUnknownCells { get; set; }
            public int Moves { get; set; }
            public int Tier1MergeTransitionsObserved { get; set; }
            public int Tier2EvidenceAccepted { get; set; }
            public int Tier2EvidenceRejected { get; set; }
            public bool Tier2Learned { get; set; }
            public int? Tier2LearnedAtMove { get; set; }
            public bool Tier2RecognitionConfirmed { get; set; }
            public long CatalogVersionStart { get; set; }
            public long CatalogVersionEnd { get; set; }
            public int TransitionInvalidCount { get; set; }
            public int LearningConflictCount { get; set; }
        }
    }
}
