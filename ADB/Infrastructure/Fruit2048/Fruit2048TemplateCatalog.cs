using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    public sealed class Fruit2048TemplateCatalog
    {
        public const string RelativeDirectory = @"Data\InfinityKingdom\1280x720\vi\Fruit2048";
        public const string EventTitle = "event_title_anchor.png";
        public const string BoardAnchor = "board_anchor.png";
        public const string RefreshButton = "refresh_button.png";
        public const string EmptyTile = "tile_empty.png";
        public const string Tier1Tile = "tile_1.png";
        public const string CityFestivalEntry = @"Navigation\city_festival_entry.png";
        public const string Fruit2048Tab = @"Navigation\fruit_2048_tab.png";
        public const string NavigationBoardAnchor = @"Navigation\board_anchor.png";
        private static readonly int[] TileValues =
        { 0, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048 };
        private readonly string root;
        private readonly Fruit2048CalibratedSeedStore calibratedSeeds;
        private readonly Dictionary<string, byte[]> cache =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public Fruit2048TemplateCatalog(string applicationBaseDirectory = null,
            Fruit2048CalibratedSeedStore calibratedSeeds = null)
        {
            string baseDirectory = applicationBaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            root = Path.Combine(baseDirectory, RelativeDirectory);
            this.calibratedSeeds = calibratedSeeds ?? new Fruit2048CalibratedSeedStore();
        }

        public string RootPath => root;
        public Fruit2048CalibratedSeedStore CalibratedSeeds => calibratedSeeds;
        public string EmptySeedPath => ResolveTilePath(0).Path;
        public string Tier1SeedPath => ResolveTilePath(1).Path;
        public Fruit2048SeedAvailability SeedAvailability => new Fruit2048SeedAvailability
        {
            EmptyAvailable = IsDecodableFile(EmptySeedPath),
            Tier1Available = IsDecodableFile(Tier1SeedPath),
            EmptyPath = EmptySeedPath,
            Tier1Path = Tier1SeedPath,
            EmptySource = ResolveTilePath(0).Source,
            Tier1Source = ResolveTilePath(1).Source
        };
        public IReadOnlyList<string> RequiredFileNames => new[] { EventTitle, BoardAnchor };
        public IReadOnlyList<string> MissingAssets => RequiredFileNames
            .Where(name => !File.Exists(Path.Combine(root, name))).ToArray();
        public IReadOnlyList<int> AvailableSeedValues => TileValues
            .Where(value => value == 0 || value == 1
                ? ResolveTilePath(value).Source != Fruit2048SeedSource.Missing
                : File.Exists(Path.Combine(root, TileFileName(value)))).ToArray();

        public bool TryGet(string fileName, out byte[] png)
        {
            lock (cache)
            {
                if (cache.TryGetValue(fileName, out png)) return true;
                string path = Path.Combine(root, fileName);
                if (!File.Exists(path)) { png = null; return false; }
                byte[] bytes = File.ReadAllBytes(path);
                if (!IsDecodablePng(bytes)) { png = null; return false; }
                cache[fileName] = bytes;
                png = bytes;
                return true;
            }
        }

        public bool TryGetTile(int value, out byte[] png)
        {
            if (value != 0 && value != 1) return TryGet(TileFileName(value), out png);
            SeedPath resolved = ResolveTilePath(value);
            if (resolved.Source == Fruit2048SeedSource.Missing) { png = null; return false; }
            return TryGetPath(resolved.Path, out png);
        }
        public void ReloadSeeds()
        {
            lock (cache) cache.Clear();
        }
        public static string TileFileName(int value) => value == 0
            ? "tile_empty.png" : "tile_" + value + ".png";

        private static bool IsDecodablePng(byte[] bytes)
        {
            if (bytes == null || bytes.Length <= 8 || bytes[0] != 0x89
                || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var bitmap = new Bitmap(stream))
                    return bitmap.Width > 0 && bitmap.Height > 0;
            }
            catch { return false; }
        }

        private static bool IsDecodableFile(string path)
        {
            if (!File.Exists(path)) return false;
            try { return IsDecodablePng(File.ReadAllBytes(path)); }
            catch { return false; }
        }

        private SeedPath ResolveTilePath(int value)
        {
            if (value == 0 || value == 1)
            {
                string calibrated;
                if (calibratedSeeds.TryGetSeedPath(1280, 720, value, out calibrated))
                    return new SeedPath(calibrated, Fruit2048SeedSource.UserCalibrated);
            }
            string packaged = Path.Combine(root, TileFileName(value));
            return IsDecodableFile(packaged)
                ? new SeedPath(packaged, Fruit2048SeedSource.StaticPackaged)
                : new SeedPath(packaged, Fruit2048SeedSource.Missing);
        }

        private bool TryGetPath(string path, out byte[] png)
        {
            lock (cache)
            {
                if (cache.TryGetValue(path, out png)) return true;
                if (!File.Exists(path)) { png = null; return false; }
                byte[] bytes = File.ReadAllBytes(path);
                if (!IsDecodablePng(bytes)) { png = null; return false; }
                cache[path] = bytes;
                png = bytes;
                return true;
            }
        }

        private sealed class SeedPath
        {
            public SeedPath(string path, Fruit2048SeedSource source) { Path = path; Source = source; }
            public string Path { get; }
            public Fruit2048SeedSource Source { get; }
        }
    }
}
