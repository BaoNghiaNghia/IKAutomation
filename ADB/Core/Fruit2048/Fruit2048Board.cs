using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.Fruit2048
{
    public enum Fruit2048Move { Up, Down, Left, Right }

    public sealed class Fruit2048Board
    {
        public const int Size = 4;
        private readonly int[] values;

        public Fruit2048Board(IEnumerable<int> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            this.values = values.ToArray();
            if (this.values.Length != Size * Size)
                throw new ArgumentException("A Fruit 2048 board must contain exactly 16 cells.", nameof(values));
            if (this.values.Any(value => value < 0 || (value != 0 && (value & (value - 1)) != 0)))
                throw new ArgumentException("Board contains an unsupported tile value.", nameof(values));
        }

        public int this[int row, int column]
        {
            get
            {
                if (row < 0 || row >= Size) throw new ArgumentOutOfRangeException(nameof(row));
                if (column < 0 || column >= Size) throw new ArgumentOutOfRangeException(nameof(column));
                return values[(row * Size) + column];
            }
        }

        public IReadOnlyList<int> Values => Array.AsReadOnly((int[])values.Clone());
        public int HighestTile => values.Max();
        public int EmptyCellCount => values.Count(value => value == 0);
        public bool HasReached(int target) => HighestTile >= target;

        public bool CanMove(Fruit2048Move move) => !Equals(Simulate(move));

        public Fruit2048Board Simulate(Fruit2048Move move)
        {
            int[] result = new int[values.Length];
            for (int line = 0; line < Size; line++)
            {
                int[] source = ReadLine(line, move);
                int[] merged = MergeLine(source);
                WriteLine(result, line, move, merged);
            }
            return new Fruit2048Board(result);
        }

        public override bool Equals(object obj)
        {
            Fruit2048Board other = obj as Fruit2048Board;
            return other != null && values.SequenceEqual(other.values);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (int value in values) hash = (hash * 31) + value;
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Join("|", Enumerable.Range(0, Size)
                .Select(row => string.Join(",", Enumerable.Range(0, Size)
                    .Select(column => this[row, column].ToString()))));
        }

        private int[] ReadLine(int line, Fruit2048Move move)
        {
            var result = new int[Size];
            for (int offset = 0; offset < Size; offset++)
            {
                int row, column;
                ResolveCoordinate(line, offset, move, out row, out column);
                result[offset] = this[row, column];
            }
            return result;
        }

        private static void WriteLine(int[] target, int line, Fruit2048Move move, int[] source)
        {
            for (int offset = 0; offset < Size; offset++)
            {
                int row, column;
                ResolveCoordinate(line, offset, move, out row, out column);
                target[(row * Size) + column] = source[offset];
            }
        }

        private static void ResolveCoordinate(int line, int offset, Fruit2048Move move,
            out int row, out int column)
        {
            switch (move)
            {
                case Fruit2048Move.Left: row = line; column = offset; break;
                case Fruit2048Move.Right: row = line; column = Size - 1 - offset; break;
                case Fruit2048Move.Up: row = offset; column = line; break;
                case Fruit2048Move.Down: row = Size - 1 - offset; column = line; break;
                default: throw new ArgumentOutOfRangeException(nameof(move));
            }
        }

        private static int[] MergeLine(int[] line)
        {
            List<int> compact = line.Where(value => value != 0).ToList();
            var merged = new List<int>(Size);
            for (int index = 0; index < compact.Count; index++)
            {
                if (index + 1 < compact.Count && compact[index] == compact[index + 1])
                {
                    merged.Add(compact[index] * 2);
                    index++;
                }
                else merged.Add(compact[index]);
            }
            while (merged.Count < Size) merged.Add(0);
            return merged.ToArray();
        }
    }
}
