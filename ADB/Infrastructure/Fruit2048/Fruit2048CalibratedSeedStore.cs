using System;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace IK_Auto_ADB.Infrastructure.Fruit2048
{
    /// <summary>Owns user-created bootstrap seeds; never writes into the installed application.</summary>
    public sealed class Fruit2048CalibratedSeedStore
    {
        public const int Version = 1;
        private readonly string root;

        public Fruit2048CalibratedSeedStore(string root = null)
        {
            this.root = root ?? Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "IKAutomation", "Fruit2048", "Seeds");
        }

        public string RootPath => root;
        public string GetResolutionDirectory(int width, int height) => Path.Combine(root, width + "x" + height);
        public string GetSeedPath(int width, int height, int tier) => Path.Combine(
            GetResolutionDirectory(width, height), tier == 0 ? "tile_empty.png" : "tile_1.png");
        public string GetMetadataPath(int width, int height) => Path.Combine(
            GetResolutionDirectory(width, height), "seed_metadata.json");

        public bool TryGetSeedPath(int width, int height, int tier, out string path)
        {
            path = GetSeedPath(width, height, tier);
            return IsDecodable(path);
        }

        public void SavePair(int width, int height, byte[] emptyPng, byte[] tier1Png,
            Fruit2048SeedMetadata metadata)
        {
            string directory = GetResolutionDirectory(width, height);
            Directory.CreateDirectory(directory);
            string empty = GetSeedPath(width, height, 0);
            string tier1 = GetSeedPath(width, height, 1);
            string emptyTemp = Path.Combine(directory, "tile_empty.tmp.png");
            string tier1Temp = Path.Combine(directory, "tile_1.tmp.png");
            File.WriteAllBytes(emptyTemp, emptyPng);
            File.WriteAllBytes(tier1Temp, tier1Png);
            if (!IsDecodable(emptyTemp) || !IsDecodable(tier1Temp))
                throw new InvalidDataException("Calibrated seed image cannot be decoded.");
            ReplaceAtomically(emptyTemp, empty);
            ReplaceAtomically(tier1Temp, tier1);
            metadata.Version = Version;
            using (var stream = File.Create(GetMetadataPath(width, height) + ".tmp"))
                new DataContractJsonSerializer(typeof(Fruit2048SeedMetadata)).WriteObject(stream, metadata);
            ReplaceAtomically(GetMetadataPath(width, height) + ".tmp", GetMetadataPath(width, height));
        }

        public void DeletePair(int width, int height)
        {
            DeleteIfExists(GetSeedPath(width, height, 0));
            DeleteIfExists(GetSeedPath(width, height, 1));
            DeleteIfExists(GetMetadataPath(width, height));
        }

        private static void ReplaceAtomically(string temporary, string destination)
        {
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public static bool IsDecodable(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
                using (var bitmap = new Bitmap(path)) return bitmap.Width > 0 && bitmap.Height > 0;
            }
            catch { return false; }
        }
    }

    [DataContract]
    public sealed class Fruit2048SeedMetadata
    {
        [DataMember] public string Resolution { get; set; }
        [DataMember] public DateTime CreatedAtUtc { get; set; }
        [DataMember] public string DeviceName { get; set; }
        [DataMember] public int BoardX { get; set; }
        [DataMember] public int BoardY { get; set; }
        [DataMember] public int BoardWidth { get; set; }
        [DataMember] public int BoardHeight { get; set; }
        [DataMember] public int EmptyCellRow { get; set; }
        [DataMember] public int EmptyCellColumn { get; set; }
        [DataMember] public int Tier1CellRow { get; set; }
        [DataMember] public int Tier1CellColumn { get; set; }
        [DataMember] public int Version { get; set; }
    }
}
