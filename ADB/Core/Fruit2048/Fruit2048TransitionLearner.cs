using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.Fruit2048
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
            IReadOnlyList<Fruit2048BoardReadResult> observations, string evidenceId)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (observations == null) throw new ArgumentNullException(nameof(observations));
            var results = new List<Fruit2048LearningResult>();
            foreach (MergeDestination merge in FindMergeDestinations(before, move))
            {
                Fruit2048Cell[] cells = observations
                    .Select(observation => observation?.Cells?.FirstOrDefault(cell =>
                        cell.Row == merge.Row && cell.Column == merge.Column))
                    .Where(cell => cell != null).ToArray();
                Fruit2048Cell known = cells.FirstOrDefault(cell => cell.Tier.HasValue);
                if (known != null)
                {
                    if (known.Tier.Value == merge.ExpectedTier)
                        catalog.ObserveTier(merge.ExpectedTier);
                    else
                        results.Add(catalog.RejectConflict(merge.ExpectedTier,
                            known.Tier.Value, known.Confidence, move,
                            merge.SourceRow, merge.SourceColumn));
                    continue;
                }

                FruitTileVisualFingerprint[] fingerprints = cells
                    .Select(cell => cell.Fingerprint).Where(value => value != null).ToArray();
                if (fingerprints.Length < 2 || !AreStable(fingerprints)) continue;
                Fruit2048LearningResult learningResult = coordinator == null
                    ? catalog.ObserveMerge(merge.ExpectedTier, fingerprints[0], evidenceId + ":tier:" + merge.ExpectedTier,
                        merge.ExpectedTier - 1, move, merge.SourceRow, merge.SourceColumn)
                    : coordinator.ObserveMerge(deviceName, merge.ExpectedTier, fingerprints[0], evidenceId + ":tier:" + merge.ExpectedTier,
                        merge.ExpectedTier - 1, move, merge.SourceRow, merge.SourceColumn);
                learningResult.DestinationRow = merge.Row;
                learningResult.DestinationColumn = merge.Column;
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

        private static IReadOnlyList<MergeDestination> FindMergeDestinations(
            Fruit2048Board board, Fruit2048Move move)
        {
            var merges = new List<MergeDestination>();
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
                    merges.Add(new MergeDestination
                    {
                        Row = destinationRow,
                        Column = destinationColumn,
                        ExpectedTier = FruitTierCatalog.ToTier(source[index].Value) + 1,
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

        private sealed class MergeDestination
        {
            public int Row { get; set; }
            public int Column { get; set; }
            public int ExpectedTier { get; set; }
            public int SourceRow { get; set; }
            public int SourceColumn { get; set; }
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
