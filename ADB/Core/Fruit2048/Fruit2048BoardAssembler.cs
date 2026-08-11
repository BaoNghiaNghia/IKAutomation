using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.Fruit2048
{
    public static class Fruit2048BoardAssembler
    {
        public static Fruit2048BoardReadResult Assemble(IReadOnlyList<Fruit2048Cell> cells,
            long durationMs)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (cells.Count != 16) throw new ArgumentException("Exactly 16 cells are required.", nameof(cells));
            Fruit2048Cell[] unknown = cells.Where(cell => !cell.Value.HasValue).ToArray();
            Fruit2048Board board = unknown.Length == 0
                ? new Fruit2048Board(cells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column)
                    .Select(cell => cell.Value.Value)) : null;
            return new Fruit2048BoardReadResult
            {
                ScreenStatus = Fruit2048ScreenStatus.Ready,
                Board = board,
                Cells = cells,
                UnknownCells = unknown,
                Success = unknown.Length == 0,
                HighestTile = board?.HighestTile ?? 0,
                DurationMs = durationMs,
                Error = unknown.Length == 0 ? null : "Không đọc được một hoặc nhiều ô."
            };
        }
    }
}
