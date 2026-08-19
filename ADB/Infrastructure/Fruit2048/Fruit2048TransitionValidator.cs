using IK_Auto_ADB.Core.Fruit2048;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IK_Auto_ADB.Infrastructure.Fruit2048
{
    /// <summary>
    /// Pure post-swipe structural check.  It consumes simulator output and a board-reader
    /// observation; it neither captures frames nor writes the learning catalog.
    /// </summary>
    public sealed class Fruit2048TransitionValidator : IFruit2048TransitionValidator
    {
        public Fruit2048TransitionValidationResult Validate(Fruit2048TransitionValidationRequest request)
        {
            if (request == null || request.ExpectedBoardAfterMove == null || request.Observed == null)
                return Result(Fruit2048TransitionValidationStatus.CaptureUnreliable, "MissingTransitionData", 0, 0, null);
            if (request.Observed.Cells == null || request.Observed.Cells.Count != Fruit2048Board.Size * Fruit2048Board.Size)
                return Result(Fruit2048TransitionValidationStatus.CaptureUnreliable, "BoardCaptureUnreliable", 0, 0, null);

            IReadOnlyList<Fruit2048MergeOperation> merges = request.MergeOperations
                ?? Fruit2048TransitionLearner.GetMergeOperations(request.BoardBefore, request.Move);
            var mergeCells = new HashSet<string>(merges.Select(merge => Key(merge.DestinationRow, merge.DestinationColumn)));

            // A partial recognition result is not reliable enough to prove a simulator
            // contradiction.  In particular, a merge animation can obscure one cell
            // while another cell is temporarily classified from the previous frame.
            // Let the caller take its bounded focused retry before comparing any known
            // cells.  Only a completely recognised board may become TransitionInvalid.
            Fruit2048Cell[] unresolvedCells = request.Observed.Cells
                .Where(cell => cell == null || !cell.Value.HasValue)
                .ToArray();
            if (unresolvedCells.Length > 0)
            {
                bool onlyMergeDestinations = unresolvedCells.All(cell => cell != null
                    && mergeCells.Contains(Key(cell.Row, cell.Column)));
                return Result(
                    Fruit2048TransitionValidationStatus.Ambiguous,
                    onlyMergeDestinations
                        ? "UnknownMergeDestinationRequiresFocusedRetry"
                        : "ObservedBoardContainsUnknownCells",
                    0,
                    unresolvedCells.Length,
                    merges);
            }

            int spawns = 0, unknowns = 0;
            for (int row = 0; row < Fruit2048Board.Size; row++)
            for (int column = 0; column < Fruit2048Board.Size; column++)
            {
                Fruit2048Cell observed = request.Observed.Cells.FirstOrDefault(cell => cell.Row == row && cell.Column == column);
                if (observed == null)
                    return Result(Fruit2048TransitionValidationStatus.CaptureUnreliable, "MissingObservedCell@" + row + "," + column, spawns, unknowns, merges);

                int expected = request.ExpectedBoardAfterMove[row, column];
                if (!observed.Value.HasValue)
                {
                    unknowns++;
                    // Only an unknown at a deterministic merge destination is eligible
                    // to become future learning evidence.  Other unknowns remain ambiguous.
                    if (!mergeCells.Contains(Key(row, column)))
                        return Result(Fruit2048TransitionValidationStatus.Ambiguous, "UnknownNonMergeCell@" + row + "," + column, spawns, unknowns, merges);
                    continue;
                }

                int actual = observed.Value.Value;
                if (actual == expected) continue;
                if (expected == 0 && actual != 0)
                {
                    spawns++;
                    continue;
                }

                // The game can keep the source value drawn at a merge destination
                // for one post-swipe frame while its merge animation is settling.
                // That frame is not clean evidence of a simulator contradiction.
                // The automation service owns a small bounded retry and will only
                // stop when the destination remains inconsistent after it finishes.
                if (mergeCells.Contains(Key(row, column)))
                    return Result(Fruit2048TransitionValidationStatus.Ambiguous,
                        "MergeDestinationMismatchRequiresFocusedRetry@" + row + "," + column,
                        spawns, unknowns, merges);

                return Result(Fruit2048TransitionValidationStatus.Invalid,
                    "SimulatorMismatch@" + row + "," + column + ": expected=" + expected + ", observed=" + actual,
                    spawns, unknowns, merges);
            }

            // A merge animation can hide its destination temporarily.  This is
            // not yet a valid transition: the caller must capture the bounded
            // follow-up frames and establish a stable deterministic sample first.
            if (unknowns > 0)
                return Result(Fruit2048TransitionValidationStatus.Ambiguous,
                    "UnknownMergeDestinationRequiresFocusedRetry", spawns, unknowns, merges);

            return Result(spawns == 0 ? Fruit2048TransitionValidationStatus.Valid : Fruit2048TransitionValidationStatus.ValidWithSpawn,
                spawns == 0 ? "DeterministicBoardMatches" : "DeterministicBoardMatchesWithSpawn",
                spawns, unknowns, merges);
        }

        private static string Key(int row, int column) => row + ":" + column;

        private static Fruit2048TransitionValidationResult Result(Fruit2048TransitionValidationStatus status,
            string reason, int spawns, int unknowns, IReadOnlyList<Fruit2048MergeOperation> merges) =>
            new Fruit2048TransitionValidationResult
            {
                Status = status,
                Reason = reason,
                SpawnCandidates = spawns,
                UnknownCells = unknowns,
                MergeDestinations = merges ?? new Fruit2048MergeOperation[0]
            };
    }
}
