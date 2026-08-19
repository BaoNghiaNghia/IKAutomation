using System;
using System.Collections.Generic;
using System.Linq;

namespace IK_Auto_ADB.Core.Fruit2048
{
    public sealed class Fruit2048Solver : IFruit2048Solver
    {
        private static readonly Fruit2048Move[] TieBreakOrder =
        { Fruit2048Move.Down, Fruit2048Move.Left, Fruit2048Move.Right, Fruit2048Move.Up };

        public bool TryChooseMove(Fruit2048Board board, out Fruit2048Move move)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            double bestScore = double.NegativeInfinity;
            move = Fruit2048Move.Left;
            bool found = false;
            foreach (Fruit2048Move candidate in TieBreakOrder)
            {
                if (!board.CanMove(candidate)) continue;
                Fruit2048Board next = board.Simulate(candidate);
                // The teacher needs verified merge transitions to learn the next
                // visual tier.  Score the merge that happens now, rather than
                // only rewarding pairs which might merge on a later move.
                IReadOnlyList<Fruit2048MergeOperation> merges =
                    Fruit2048TransitionLearner.GetMergeOperations(board, candidate);
                double score = Score(next, merges);
                if (!found || score > bestScore)
                {
                    found = true;
                    bestScore = score;
                    move = candidate;
                }
            }
            return found;
        }

        private static double Score(Fruit2048Board board,
            IReadOnlyList<Fruit2048MergeOperation> immediateMerges)
        {
            int mergeCount = immediateMerges?.Count ?? 0;
            int highestMergedTier = immediateMerges == null || immediateMerges.Count == 0
                ? 0
                : immediateMerges.Max(merge => merge.ResultTier);

            // A direct merge is the only safe source of new tier evidence.  Give
            // it precedence over shape heuristics, and prefer a higher-result
            // merge when there are several legal directions.
            double score = (highestMergedTier * 100000.0) + (mergeCount * 10000.0);
            score += board.EmptyCellCount * 1000.0;
            int max = board.HighestTile;
            bool maxInCorner = board[0, 0] == max || board[0, 3] == max
                || board[3, 0] == max || board[3, 3] == max;
            if (maxInCorner) score += max * 12.0;

            int futureMerges = 0;
            double roughness = 0;
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
            {
                int value = board[row, column];
                if (column < 3)
                {
                    int right = board[row, column + 1];
                    if (value != 0 && value == right) futureMerges++;
                    roughness += Difference(value, right);
                }
                if (row < 3)
                {
                    int down = board[row + 1, column];
                    if (value != 0 && value == down) futureMerges++;
                    roughness += Difference(value, down);
                }
            }
            score += futureMerges * 150.0;
            score -= roughness * 4.0;
            score += Monotonicity(board) * 20.0;
            return score;
        }

        private static double Difference(int first, int second)
        {
            if (first == 0 || second == 0) return 0;
            return Math.Abs(Math.Log(first, 2) - Math.Log(second, 2));
        }

        private static double Monotonicity(Fruit2048Board board)
        {
            double total = 0;
            for (int row = 0; row < 4; row++)
            {
                double ascending = 0, descending = 0;
                for (int column = 0; column < 3; column++)
                {
                    double current = board[row, column] == 0 ? 0 : Math.Log(board[row, column], 2);
                    double next = board[row, column + 1] == 0 ? 0 : Math.Log(board[row, column + 1], 2);
                    if (current > next) descending += current - next; else ascending += next - current;
                }
                total += Math.Max(ascending, descending);
            }
            return total;
        }
    }
}
