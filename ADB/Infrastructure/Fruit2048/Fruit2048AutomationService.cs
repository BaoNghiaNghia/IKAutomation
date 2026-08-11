using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
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

        public Fruit2048AutomationService(ILdPlayerClient playerClient,
            IFruit2048BoardReader reader, IFruit2048Solver solver,
            IFruit2048SwipeExecutor swipeExecutor,
            IDeviceAutomationOwnershipService ownership,
            IFruit2048TransitionLearner transitionLearner,
            IFruitTileLearningCatalog learningCatalog, IDiagnosticLogger logger,
            IFruit2048NavigationService navigation = null, IFruit2048LearningCoordinator learningCoordinator = null,
            IFruit2048TransitionValidator transitionValidator = null,
            Fruit2048LearningDiagnosticStore diagnosticStore = null)
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
            this.transitionValidator = transitionValidator;
            this.diagnosticStore = diagnosticStore;
        }

        public async Task<Fruit2048RunResult> RunAsync(string deviceName,
            Fruit2048RunRequest request, IProgress<Fruit2048Progress> progress,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.TargetTile < 1) throw new ArgumentOutOfRangeException(nameof(request.TargetTile));
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
            int teacherMoves = 0;
            Report(progress, deviceName, Fruit2048RuntimeStatus.Starting, null,
                0, null, "Đang khởi động", null);
            LogDevice(deviceName, "Fruit2048", "Starting");
            try
            {
                if (learningCoordinator != null)
                {
                    string reason;
                    if (learningCoordinator.TryAcquireTeacher(deviceName, out teacherSession, out reason))
                        Report(progress, deviceName, Fruit2048RuntimeStatus.Starting, null, 0, null, "Thiết bị học", null);
                }
                Report(progress, deviceName, Fruit2048RuntimeStatus.Starting, null, 0, null,
                    "Đang kiểm tra màn hình", null);
                if (navigation != null)
                {
                    Fruit2048NavigationResult navigationResult = await navigation.EnsureFruit2048ScreenAsync(
                        deviceName, new Progress<string>(message => Report(progress, deviceName,
                            Fruit2048RuntimeStatus.Starting, null, moveCount, null, message, null)), cancellationToken);
                    if (!navigationResult.Success)
                        return Result(deviceName, Fruit2048Outcome.ScreenNotOpen, moveCount, highestTile,
                            request.TargetTile, false, navigationResult.Error);
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
                    return Result(deviceName, Fruit2048Outcome.MissingSeedAssets,
                        moveCount, highestTile, request.TargetTile, false, missingSeedMessage);
                }
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!await playerClient.IsRunningAsync(deviceName, cancellationToken))
                        return Result(deviceName, Fruit2048Outcome.Disconnected, moveCount,
                            highestTile, request.TargetTile, false, "Mất kết nối");

                    Report(progress, deviceName, Fruit2048RuntimeStatus.Scanning, null,
                        moveCount, null, "Đang đọc bàn", null);
                    bool retainPostMoveFrame = boardBeforeLastMove != null && previousMove.HasValue;
                    Fruit2048BoardReadResult read = await reader.ReadAsync(deviceName,
                        retainPostMoveFrame, cancellationToken);
                    if (boardBeforeLastMove != null && previousMove.HasValue && transitionValidator != null
                        && learningCoordinator != null && learningCoordinator.CanLearn(deviceName))
                    {
                        var observations = new List<Fruit2048BoardReadResult> { read };
                        Fruit2048Board expected = boardBeforeLastMove.Simulate(previousMove.Value);
                        IReadOnlyList<Fruit2048MergeOperation> merges = Fruit2048TransitionLearner.GetMergeOperations(
                            boardBeforeLastMove, previousMove.Value);
                        Fruit2048TransitionValidationResult validation = null;
                        for (int retry = 0; retry < 3; retry++)
                        {
                            Fruit2048BoardReadResult candidate = observations.Last();
                            Report(progress, deviceName, Fruit2048RuntimeStatus.ValidatingTransition, candidate,
                                moveCount, previousMove, "Đang xác minh nước đi", null);
                            validation = transitionValidator.Validate(new Fruit2048TransitionValidationRequest
                            {
                                DeviceName = deviceName,
                                TransitionId = previousEvidenceId,
                                BoardBefore = boardBeforeLastMove,
                                Move = previousMove.Value,
                                ExpectedBoardAfterMove = expected,
                                MergeOperations = merges,
                                Observed = candidate
                            });
                            logger?.Info($"[Fruit2048 Transition] DeviceName='{deviceName}', TransitionId='{previousEvidenceId}', Retry={retry}, Status='{validation.Status}', SpawnCandidates={validation.SpawnCandidates}, UnknownCells={validation.UnknownCells}, Reason='{validation.Reason}'");
                            if (validation.IsValidForLearning || validation.Status == Fruit2048TransitionValidationStatus.Invalid)
                                break;
                            if (retry == 2) break;
                            await Task.Delay(200, cancellationToken);
                            observations.Add(await reader.ReadAsync(deviceName, true, cancellationToken));
                        }
                        read = observations.Last();
                        if (!validation.IsValidForLearning)
                        {
                            string diagnosticPath = diagnosticStore?.Save(deviceName, teacherSession, previousEvidenceId,
                                validation.Reason, boardBeforeLastMove, expected, observations);
                            logger?.Info($"[Fruit2048 Learning Diagnostics] DeviceName='{deviceName}', TransitionId='{previousEvidenceId}', Path='{diagnosticPath}'");
                            Report(progress, deviceName, Fruit2048RuntimeStatus.Failed, read, moveCount, previousMove,
                                "Transition không khớp simulator — đã tạm dừng.", validation.Reason);
                            return Result(deviceName, Fruit2048Outcome.BoardReadFailed, moveCount, highestTile,
                                request.TargetTile, false, validation.Reason);
                        }

                        // This is the sole point at which a Teacher can submit merge evidence.
                        IReadOnlyList<Fruit2048LearningResult> learning = transitionLearner.Learn(deviceName,
                            boardBeforeLastMove, previousMove.Value, observations, previousEvidenceId);
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
                            read = observations.Last();
                            read.Error = "LearningConflict: " + conflict.Error;
                        }
                        else
                        {
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
                        const string unknownMessage = "Board hiện tại có trái cây chưa học. "
                            + "Hãy bắt đầu từ board mới hoặc dùng Làm mới để tool học từ Tier 1.";
                        bool hasUnknownCells = read.UnknownCells != null
                            && read.UnknownCells.Count > 0;
                        bool learningConflict = !string.IsNullOrWhiteSpace(read.Error)
                            && read.Error.StartsWith("LearningConflict", StringComparison.Ordinal);
                        string failureMessage = hasUnknownCells ? unknownMessage : read.Error;
                        if (hasUnknownCells)
                        {
                            string diagnosticPath = diagnosticStore?.Save(deviceName, teacherSession, previousEvidenceId,
                                failureMessage, boardBeforeLastMove, null, new[] { read });
                            logger?.Info($"[Fruit2048 Learning Diagnostics] DeviceName='{deviceName}', TransitionId='{previousEvidenceId}', Path='{diagnosticPath}'");
                        }
                        Report(progress, deviceName,
                            hasUnknownCells ? Fruit2048RuntimeStatus.NeedsFreshBoard
                                : Fruit2048RuntimeStatus.BoardUnknown,
                            read, moveCount, null, failureMessage, read.Error,
                            hasUnknownCells ? (Fruit2048BootstrapState?)Fruit2048BootstrapState.NeedsFreshBoard
                                : null);
                        return Result(deviceName,
                            read.ScreenStatus == Fruit2048ScreenStatus.NotOpen
                                ? Fruit2048Outcome.ScreenNotOpen
                                : learningConflict
                                    ? Fruit2048Outcome.BoardReadFailed
                                    : hasUnknownCells ? Fruit2048Outcome.NeedsFreshBoard
                                        : Fruit2048Outcome.BoardReadFailed,
                            moveCount, highestTile, request.TargetTile, false,
                            learningConflict ? read.Error : failureMessage);
                    }

                    Report(progress, deviceName, Fruit2048RuntimeStatus.Playing, read,
                        moveCount, null, "Đang chơi Fruit 2048", null);
                    if (read.Board.HasReached(request.TargetTile))
                    {
                        Report(progress, deviceName, Fruit2048RuntimeStatus.TargetReached,
                            read, moveCount, null, "Đã đạt mục tiêu", null);
                        return Result(deviceName, Fruit2048Outcome.TargetReached, moveCount,
                            read.HighestTile, request.TargetTile, false, null);
                    }

                    Fruit2048Move move;
                    if (!solver.TryChooseMove(read.Board, out move))
                    {
                        if (request.AutoRefresh && !refreshUsedForCurrentDeadBoard
                            && await reader.TryTapRefreshAsync(deviceName, cancellationToken))
                        {
                            refreshUsedForCurrentDeadBoard = true;
                            await Task.Delay(500, cancellationToken);
                            continue;
                        }
                        Report(progress, deviceName, Fruit2048RuntimeStatus.NoMoves, read,
                            moveCount, null, "Không còn nước đi", null);
                        return Result(deviceName, Fruit2048Outcome.NoMoves, moveCount,
                            read.HighestTile, request.TargetTile, false, null);
                    }

                    refreshUsedForCurrentDeadBoard = false;
                    if (teacherSession != null && request.TeacherMoveLimit > 0
                        && teacherMoves >= request.TeacherMoveLimit)
                    {
                        const string moveLimitMessage = "Đã đạt giới hạn lượt học.";
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
                    if (teacherSession != null)
                        pendingTransition = new Fruit2048PendingTransition
                        {
                            DeviceName = deviceName,
                            TransitionId = previousEvidenceId,
                            BoardBefore = boardBeforeLastMove,
                            Move = move,
                            ExpectedBoardAfterMove = boardBeforeLastMove.Simulate(move),
                            MergeOperations = Fruit2048TransitionLearner.GetMergeOperations(boardBeforeLastMove, move),
                            StartedAtUtc = DateTimeOffset.UtcNow
                        };
                    var stopwatch = Stopwatch.StartNew();
                    await swipeExecutor.ExecuteAsync(deviceName, move, read.BoardRegion.Value,
                        read.ScreenWidth, read.ScreenHeight, cancellationToken);
                    stopwatch.Stop();
                    moveCount++;
                    if (teacherSession != null) teacherMoves++;
                    logger?.Info($"[Fruit2048 Move] DeviceName='{deviceName}', "
                        + $"MoveNumber={moveCount}, Move='{move}', HighestTileBefore={read.HighestTile}, "
                        + $"EmptyCellsBefore={read.Board.EmptyCellCount}, SwipeSent=true, "
                        + $"DurationMs={stopwatch.ElapsedMilliseconds}");
                    Report(progress, deviceName, Fruit2048RuntimeStatus.Playing, read,
                        moveCount, move, "Đang chơi Fruit 2048", null);
                    Report(progress, deviceName, Fruit2048RuntimeStatus.WaitingPostMove, read,
                        moveCount, move, "Đang chờ xác minh nước đi", null);
                    await Task.Delay(200, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                Report(progress, deviceName, Fruit2048RuntimeStatus.Cancelled, null,
                    moveCount, null, "Đã dừng", null);
                return Result(deviceName, Fruit2048Outcome.Cancelled, moveCount,
                    highestTile, request.TargetTile, true, null);
            }
            catch (Exception exception)
            {
                logger?.Error("[Fruit2048 Result] Automation failed.", exception);
                Report(progress, deviceName, Fruit2048RuntimeStatus.Failed, null,
                    moveCount, null, "Lỗi Fruit 2048", exception.Message);
                return Result(deviceName, Fruit2048Outcome.Failed, moveCount,
                    highestTile, request.TargetTile, false, exception.Message);
            }
            finally
            {
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
                if (navigation != null)
                {
                    Fruit2048NavigationResult navigationResult = await navigation.EnsureFruit2048ScreenAsync(
                        deviceName, null, cancellationToken);
                    if (!navigationResult.Success) return Unavailable(navigationResult.Error);
                }
                return await reader.ReadAsync(deviceName, cancellationToken);
            }
            finally { lease.Dispose(); }
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
                await swipeExecutor.ExecuteAsync(deviceName, move, before.BoardRegion.Value,
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
                RecognitionSource = cell.RecognitionSource, Fingerprint = cell.Fingerprint
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
            resolved.FallbackCells = cells.Count(cell =>
                cell.RecognitionSource == Fruit2048RecognitionSource.PrototypeMatch
                || cell.RecognitionSource == Fruit2048RecognitionSource.StaticSeed);
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
                RecognitionSource = cell.RecognitionSource, Fingerprint = cell.Fingerprint
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

        private void Report(IProgress<Fruit2048Progress> progress, string deviceName,
            Fruit2048RuntimeStatus status, Fruit2048BoardReadResult read, int moveCount,
            Fruit2048Move? lastMove, string message, string error,
            Fruit2048BootstrapState? bootstrapOverride = null)
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
                UnknownCellCount = read?.UnknownCells?.Count ?? 0
            });
        }

        private void LogDevice(string deviceName, string owner, string status) =>
            logger?.Info($"[Fruit2048 Device] DeviceName='{deviceName}', Owner='{owner}', Status='{status}'");
    }
}
