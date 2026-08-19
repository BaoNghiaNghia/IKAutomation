using System;
using System.Collections.Generic;
using System.Linq;

namespace IK_Auto_ADB.Core.Fruit2048
{
    public sealed class Fruit2048TransitionLearner : IFruit2048TransitionLearner
    {
        private readonly IFruitTileLearningCatalog catalog;
        private readonly int stableHashDistance;
        private readonly IFruit2048LearningCoordinator coordinator;

        public Fruit2048TransitionLearner(IFruitTileLearningCatalog catalog,
            int stableHashDistance = 5, IFruit2048LearningCoordinator coordinator = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (stableHashDistance < 0 || stableHashDistance > 64)
                throw new ArgumentOutOfRangeException(nameof(stableHashDistance));
            this.stableHashDistance = stableHashDistance;
            this.coordinator = coordinator;
        }

        public IReadOnlyList<Fruit2048LearningResult> Learn(string deviceName,
            Fruit2048Board before, Fruit2048Move move,
            IReadOnlyList<Fruit2048BoardReadResult> observations, string evidenceId,
            int? resultTierFilter = null)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (observations == null) throw new ArgumentNullException(nameof(observations));
            var results = new List<Fruit2048LearningResult>();
            foreach (Fruit2048MergeOperation merge in GetMergeOperations(before, move))
            {
                if (resultTierFilter.HasValue && merge.ResultTier != resultTierFilter.Value)
                    continue;
                Fruit2048Cell[] cells = observations
                    .Select(observation => observation?.Cells?.FirstOrDefault(cell =>
                        cell.Row == merge.DestinationRow && cell.Column == merge.DestinationColumn))
                    .Where(cell => cell != null).ToArray();
                Fruit2048Cell known = cells.FirstOrDefault(cell => cell.Tier.HasValue);
                if (known != null)
                {
                    if (known.Tier.Value == merge.ResultTier)
                    {
                        // A known deterministic result needs no catalog mutation.  The
                        // coordinator is deliberately the only teacher-side writer.
                        if (coordinator == null)
                        catalog.ObserveTier(merge.ResultTier);
                    }
                    else
                        results.Add(catalog.RejectConflict(merge.ResultTier,
                            known.Tier.Value, known.Confidence, move,
                            merge.SourceRow, merge.SourceColumn));
                    continue;
                }

                FruitTileVisualFingerprint[] fingerprints = cells
                    .Select(cell => cell.Fingerprint).Where(value => value != null).ToArray();
                // The first post-swipe observation may still contain merge animation.
                // Learning uses the newest stable pair from this one transition rather than
                // rejecting an otherwise clean later pair because of that transient frame.
                FruitTileVisualFingerprint[] stablePair = fingerprints
                    .Skip(Math.Max(0, fingerprints.Length - 2)).ToArray();
                if (stablePair.Length < 2 || !AreStable(stablePair)) continue;
                Fruit2048LearningResult learningResult = coordinator == null
                    ? catalog.ObserveMerge(merge.ResultTier, stablePair[stablePair.Length - 1], evidenceId + ":tier:" + merge.ResultTier,
                        merge.SourceTier, move, merge.SourceRow, merge.SourceColumn)
                    : coordinator.ObserveMerge(deviceName, merge.ResultTier, stablePair[stablePair.Length - 1], evidenceId + ":tier:" + merge.ResultTier,
                        merge.SourceTier, move, merge.SourceRow, merge.SourceColumn);
                learningResult.DestinationRow = merge.DestinationRow;
                learningResult.DestinationColumn = merge.DestinationColumn;
                results.Add(learningResult);
            }
            return results;
        }

        private bool AreStable(IReadOnlyList<FruitTileVisualFingerprint> fingerprints)
        {
            for (int index = 1; index < fingerprints.Count; index++)
                if (FruitFingerprintDistance.Hamming(fingerprints[0].AverageHash,
                    fingerprints[index].AverageHash) > stableHashDistance) return false;
            return true;
        }

        public static IReadOnlyList<Fruit2048MergeOperation> GetMergeOperations(
            Fruit2048Board board, Fruit2048Move move)
        {
            var merges = new List<Fruit2048MergeOperation>();
            for (int line = 0; line < Fruit2048Board.Size; line++)
            {
                var source = new List<SourceTile>();
                for (int offset = 0; offset < Fruit2048Board.Size; offset++)
                {
                    int row, column;
                    ResolveCoordinate(line, offset, move, out row, out column);
                    int value = board[row, column];
                    if (value != 0) source.Add(new SourceTile(value, row, column));
                }
                int outputOffset = 0;
                for (int index = 0; index < source.Count; index++, outputOffset++)
                {
                    if (index + 1 >= source.Count || source[index].Value != source[index + 1].Value)
                        continue;
                    int destinationRow, destinationColumn;
                    ResolveCoordinate(line, outputOffset, move,
                        out destinationRow, out destinationColumn);
                    merges.Add(new Fruit2048MergeOperation
                    {
                        DestinationRow = destinationRow,
                        DestinationColumn = destinationColumn,
                        SourceTier = FruitTierCatalog.ToTier(source[index].Value),
                        ResultTier = FruitTierCatalog.ToTier(source[index].Value) + 1,
                        SourceRow = source[index].Row,
                        SourceColumn = source[index].Column
                    });
                    index++;
                }
            }
            return merges;
        }

        private static void ResolveCoordinate(int line, int offset, Fruit2048Move move,
            out int row, out int column)
        {
            switch (move)
            {
                case Fruit2048Move.Left: row = line; column = offset; break;
                case Fruit2048Move.Right: row = line; column = 3 - offset; break;
                case Fruit2048Move.Up: row = offset; column = line; break;
                case Fruit2048Move.Down: row = 3 - offset; column = line; break;
                default: throw new ArgumentOutOfRangeException(nameof(move));
            }
        }

        private sealed class SourceTile
        {
            public SourceTile(int value, int row, int column)
            { Value = value; Row = row; Column = column; }
            public int Value { get; }
            public int Row { get; }
            public int Column { get; }
        }

    }

    public static class FruitFingerprintDistance
    {
        public static int Hamming(string first, string second)
        {
            ulong left, right;
            if (!ulong.TryParse(first, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out left)
                || !ulong.TryParse(second, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out right)) return 64;
            ulong value = left ^ right;
            int count = 0;
            while (value != 0) { value &= value - 1; count++; }
            return count;
        }
    }
}
