using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
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
                return Result(Fruit2048TransitionValidationStatus.Invalid,
                    "SimulatorMismatch@" + row + "," + column + ": expected=" + expected + ", observed=" + actual,
                    spawns, unknowns, merges);
            }

            // The only tolerated unknown is a deterministic merge destination: the
            // simulator proves its tier, while TransitionLearner separately applies
            // its visual-fingerprint stability gate before it can persist a prototype.
            return Result(spawns == 0 ? Fruit2048TransitionValidationStatus.Valid : Fruit2048TransitionValidationStatus.ValidWithSpawn,
                unknowns > 0 ? "DeterministicBoardMatchesWithUnknownMergeDestination"
                    : spawns == 0 ? "DeterministicBoardMatches" : "DeterministicBoardMatchesWithSpawn",
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
