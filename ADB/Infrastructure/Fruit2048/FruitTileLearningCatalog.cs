using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    public sealed class FruitTileLearningCatalog : IFruitTileLearningCatalog
    {
        public const int RequiredSamples = 3;
        public const int MaximumPrototypesPerTier = 5;
        private const int FastHashDistance = 4;
        private const double PrototypeThreshold = 0.82;
        private readonly object sync = new object();
        private readonly IDiagnosticLogger logger;
        private FruitTileCatalogDocument document;

        public FruitTileLearningCatalog(string storagePath = null, IDiagnosticLogger logger = null)
        {
            StoragePath = storagePath ?? GetDefaultStoragePath();
            this.logger = logger;
            document = Load(StoragePath) ?? new FruitTileCatalogDocument();
            Normalize();
            LogCatalog();
        }

        public string StoragePath { get; }
        public Fruit2048LearningSnapshot Snapshot
        {
            get { lock (sync) return CreateSnapshot(); }
        }

        public static string GetDefaultStoragePath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "IKAutomation", "Fruit2048", "Learning", "catalog.json");
        }

        public void AddSeed(int tier, FruitTileVisualFingerprint fingerprint)
        {
            if (tier < 0) throw new ArgumentOutOfRangeException(nameof(tier));
            if (fingerprint == null) throw new ArgumentNullException(nameof(fingerprint));
            lock (sync)
            {
                FruitTileProfile profile = GetOrCreate(tier);
                if (profile.Prototypes.Any(item => FruitFingerprintDistance.Hamming(
                    item.Fingerprint.AverageHash, fingerprint.AverageHash) <= 2)) return;
                profile.State = FruitTileLearningState.Learned;
                profile.SampleCount = Math.Max(1, profile.SampleCount);
                profile.Confidence = Math.Max(0.95, profile.Confidence);
                profile.Prototypes.Add(new FruitTilePrototype
                { Fingerprint = fingerprint, IsStaticSeed = true });
                Trim(profile.Prototypes);
                if (tier > 0) document.HighestObservedTier = Math.Max(document.HighestObservedTier, tier);
                Save();
            }
        }

        public void ReplaceBootstrapSeed(int tier, FruitTileVisualFingerprint fingerprint, string samplePath)
        {
            if (tier < 0) throw new ArgumentOutOfRangeException(nameof(tier));
            if (fingerprint == null) throw new ArgumentNullException(nameof(fingerprint));
            lock (sync)
            {
                FruitTileProfile profile = GetOrCreate(tier);
                profile.Prototypes.RemoveAll(item => item.IsStaticSeed);
                profile.Prototypes.Add(new FruitTilePrototype
                {
                    Fingerprint = fingerprint,
                    SamplePath = samplePath,
                    IsStaticSeed = true
                });
                profile.State = FruitTileLearningState.Learned;
                profile.SampleCount = Math.Max(1, profile.SampleCount);
                profile.Confidence = Math.Max(0.95, profile.Confidence);
                Trim(profile.Prototypes);
                if (tier > 0) document.HighestObservedTier = Math.Max(document.HighestObservedTier, tier);
                Save();
            }
        }

        public FruitTileRecognitionResult Recognize(FruitTileVisualFingerprint fingerprint)
        {
            if (fingerprint == null) return Unknown();
            lock (sync)
            {
                MatchCandidate fast = BestMatch(fingerprint, profile => profile.State == FruitTileLearningState.Learned
                    && profile.VerifiedByMerge, true);
                if (fast != null && fast.HashDistance <= FastHashDistance && fast.Margin >= 2)
                    return Known(fast.Profile.Tier, Math.Max(0.90, 1.0 - fast.HashDistance / 64.0),
                        Fruit2048RecognitionSource.FastFingerprint);

                MatchCandidate fallback = BestMatch(fingerprint,
                    profile => profile.State == FruitTileLearningState.Learned, false);
                if (fallback == null || fallback.Score < PrototypeThreshold || fallback.ScoreMargin < 0.04)
                {
                    MatchCandidate candidate = BestMatch(fingerprint,
                        profile => profile.State == FruitTileLearningState.Candidate, false);
                    if (candidate == null || candidate.HashDistance > 2
                        || candidate.Score < 0.97 || candidate.ScoreMargin < 0.08) return Unknown();
                    return Known(candidate.Profile.Tier, candidate.Score,
                        Fruit2048RecognitionSource.PrototypeMatch);
                }
                return Known(fallback.Profile.Tier, fallback.Score,
                    fallback.Prototype.IsStaticSeed
                        ? Fruit2048RecognitionSource.StaticSeed
                        : Fruit2048RecognitionSource.PrototypeMatch);
            }
        }

        public Fruit2048LearningResult ObserveMerge(int tier,
            FruitTileVisualFingerprint fingerprint, string evidenceId, int sourceTier,
            Fruit2048Move move, int sourceRow, int sourceColumn)
        {
            if (tier <= 1) throw new ArgumentOutOfRangeException(nameof(tier));
            if (fingerprint == null) throw new ArgumentNullException(nameof(fingerprint));
            if (string.IsNullOrWhiteSpace(evidenceId)) throw new ArgumentException("Evidence is required.", nameof(evidenceId));
            lock (sync)
            {
                document.HighestObservedTier = Math.Max(document.HighestObservedTier, tier);
                FruitTileProfile profile = GetOrCreate(tier);
                if (profile.State == FruitTileLearningState.Learned)
                    return LearningResult(profile, Fruit2048LearningAction.DuplicateIgnored,
                        sourceTier, move, sourceRow, sourceColumn, "VerifiedMergeTransition");
                if (profile.EvidenceIds.Contains(evidenceId, StringComparer.Ordinal))
                    return LearningResult(profile, Fruit2048LearningAction.DuplicateIgnored,
                        sourceTier, move, sourceRow, sourceColumn, "VerifiedMergeTransition");

                if (profile.Prototypes.Count > 0)
                {
                    int distance = profile.Prototypes.Min(item => FruitFingerprintDistance.Hamming(
                        item.Fingerprint.AverageHash, fingerprint.AverageHash));
                    if (distance > 10)
                        return RejectConflict(tier, tier, 1.0 - distance / 64.0,
                            move, sourceRow, sourceColumn);
                }

                profile.EvidenceIds.Add(evidenceId);
                profile.SampleCount++;
                profile.State = profile.SampleCount >= RequiredSamples
                    ? FruitTileLearningState.Learned : FruitTileLearningState.Candidate;
                profile.VerifiedByMerge = profile.State == FruitTileLearningState.Learned;
                profile.Confidence = Math.Min(1.0, profile.SampleCount / (double)RequiredSamples);
                if (!profile.Prototypes.Any(item => FruitFingerprintDistance.Hamming(
                    item.Fingerprint.AverageHash, fingerprint.AverageHash) <= 2))
                {
                    profile.Prototypes.Add(new FruitTilePrototype { Fingerprint = fingerprint });
                    Trim(profile.Prototypes);
                }
                Fruit2048LearningAction action = profile.SampleCount == 1
                    ? Fruit2048LearningAction.CandidateAdded
                    : (profile.State == FruitTileLearningState.Learned
                        ? Fruit2048LearningAction.Learned
                        : Fruit2048LearningAction.CandidateConfirmed);
                Save();
                Fruit2048LearningResult result = LearningResult(profile, action,
                    sourceTier, move, sourceRow, sourceColumn, "VerifiedMergeTransition");
                LogLearning(result);
                return result;
            }
        }

        public Fruit2048LearningResult RejectConflict(int expectedTier, int matchedTier,
            double confidence, Fruit2048Move move, int sourceRow, int sourceColumn)
        {
            FruitTileProfile profile;
            lock (sync) profile = document.Profiles.FirstOrDefault(item => item.Tier == expectedTier)
                ?? new FruitTileProfile { Tier = expectedTier };
            var result = LearningResult(profile, Fruit2048LearningAction.ConflictRejected,
                Math.Max(0, expectedTier - 1), move, sourceRow, sourceColumn,
                "LearningConflict");
            result.Confidence = confidence;
            result.Error = "Expected Tier " + expectedTier + " but matched Tier " + matchedTier + ".";
            LogLearning(result);
            return result;
        }

        public void ObserveTier(int tier)
        {
            if (tier <= 0) return;
            lock (sync)
            {
                if (tier <= document.HighestObservedTier) return;
                document.HighestObservedTier = tier;
                Save();
            }
        }

        public void ResetLearningData()
        {
            lock (sync)
            {
                FruitTileProfile[] seeds = document.Profiles
                    .Where(profile => profile.Prototypes.Any(prototype => prototype.IsStaticSeed))
                    .Select(profile => new FruitTileProfile
                    {
                        Tier = profile.Tier,
                        State = FruitTileLearningState.Learned,
                        SampleCount = 1,
                        Confidence = 0.95,
                        VerifiedByMerge = false,
                        Prototypes = profile.Prototypes.Where(prototype => prototype.IsStaticSeed)
                            .Select(prototype => new FruitTilePrototype
                            {
                                Fingerprint = prototype.Fingerprint,
                                SamplePath = prototype.SamplePath,
                                IsStaticSeed = true
                            }).ToList()
                    }).ToArray();
                document = new FruitTileCatalogDocument
                {
                    HighestObservedTier = seeds.Where(profile => profile.Tier > 0)
                        .Select(profile => profile.Tier).DefaultIfEmpty(0).Max(),
                    Profiles = seeds.ToList()
                };
                if (File.Exists(StoragePath)) File.Delete(StoragePath);
                Save();
                LogCatalog();
            }
        }

        public string ExportLearningDiagnostics(string destinationDirectory)
        {
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                throw new ArgumentException("Destination is required.", nameof(destinationDirectory));
            lock (sync)
            {
                Directory.CreateDirectory(destinationDirectory);
                string catalogPath = Path.Combine(destinationDirectory, "catalog.json");
                var serializer = new DataContractJsonSerializer(typeof(FruitTileCatalogDocument));
                using (var stream = File.Create(catalogPath)) serializer.WriteObject(stream, document);
                Fruit2048LearningSnapshot snapshot = CreateSnapshot();
                string summaryPath = Path.Combine(destinationDirectory, "learning-summary.txt");
                File.WriteAllLines(summaryPath, new[]
                {
                    "Mode=" + snapshot.Mode,
                    "HighestObservedTier=" + snapshot.HighestObservedTier,
                    "LearnedTiers=" + snapshot.KnownTierCount,
                    "Profiles=" + string.Join("; ", snapshot.Profiles.Select(profile =>
                        "Tier " + profile.Tier + ": " + profile.State + ", samples="
                        + profile.SampleCount + ", prototypes=" + profile.Prototypes.Count))
                });
                return destinationDirectory;
            }
        }

        private MatchCandidate BestMatch(FruitTileVisualFingerprint fingerprint,
            Func<FruitTileProfile, bool> predicate, bool hashOnly)
        {
            var candidates = new List<MatchCandidate>();
            foreach (FruitTileProfile profile in document.Profiles.Where(predicate))
            foreach (FruitTilePrototype prototype in profile.Prototypes.Where(item => item.Fingerprint != null))
            {
                int hash = FruitFingerprintDistance.Hamming(fingerprint.AverageHash,
                    prototype.Fingerprint.AverageHash);
                int edge = FruitFingerprintDistance.Hamming(fingerprint.EdgeHash,
                    prototype.Fingerprint.EdgeHash);
                int color = Math.Abs(fingerprint.MeanRed - prototype.Fingerprint.MeanRed)
                    + Math.Abs(fingerprint.MeanGreen - prototype.Fingerprint.MeanGreen)
                    + Math.Abs(fingerprint.MeanBlue - prototype.Fingerprint.MeanBlue);
                double score = hashOnly ? 1.0 - hash / 64.0
                    : (0.60 * (1.0 - hash / 64.0))
                    + (0.20 * (1.0 - edge / 64.0))
                    + (0.20 * Math.Max(0, 1.0 - color / 765.0));
                candidates.Add(new MatchCandidate
                { Profile = profile, Prototype = prototype, HashDistance = hash, Score = score });
            }
            MatchCandidate[] ordered = candidates.OrderByDescending(item => item.Score).ToArray();
            if (ordered.Length == 0) return null;
            ordered[0].ScoreMargin = ordered.Length == 1 ? 1 : ordered[0].Score - ordered[1].Score;
            ordered[0].Margin = ordered.Length == 1 ? 64 : ordered[1].HashDistance - ordered[0].HashDistance;
            return ordered[0];
        }

        private FruitTileProfile GetOrCreate(int tier)
        {
            FruitTileProfile profile = document.Profiles.FirstOrDefault(item => item.Tier == tier);
            if (profile != null) return profile;
            profile = new FruitTileProfile { Tier = tier };
            document.Profiles.Add(profile);
            return profile;
        }

        private Fruit2048LearningSnapshot CreateSnapshot()
        {
            FruitTileProfile[] profiles = document.Profiles.OrderBy(item => item.Tier)
                .Select(Clone).ToArray();
            int highest = document.HighestObservedTier;
            int learned = profiles.Count(item => item.Tier > 0 && item.State == FruitTileLearningState.Learned);
            bool allLearned = highest > 1 && Enumerable.Range(1, highest).All(tier =>
                profiles.Any(item => item.Tier == tier && item.State == FruitTileLearningState.Learned));
            Fruit2048RecognitionMode mode = allLearned ? Fruit2048RecognitionMode.Fast
                : (profiles.Any(item => item.Tier > 1 && (item.State == FruitTileLearningState.Candidate
                    || item.State == FruitTileLearningState.Learned))
                    ? Fruit2048RecognitionMode.Hybrid : Fruit2048RecognitionMode.Learning);
            return new Fruit2048LearningSnapshot
            { Mode = mode, KnownTierCount = learned, HighestObservedTier = highest, Profiles = profiles };
        }

        private void Normalize()
        {
            if (document.Profiles == null) document.Profiles = new List<FruitTileProfile>();
            foreach (FruitTileProfile profile in document.Profiles)
            {
                if (profile.Prototypes == null) profile.Prototypes = new List<FruitTilePrototype>();
                if (profile.EvidenceIds == null) profile.EvidenceIds = new List<string>();
            }
        }

        private void Save()
        {
            string directory = Path.GetDirectoryName(StoragePath);
            Directory.CreateDirectory(directory);
            string temporary = StoragePath + ".tmp";
            var serializer = new DataContractJsonSerializer(typeof(FruitTileCatalogDocument));
            using (var stream = File.Create(temporary)) serializer.WriteObject(stream, document);
            if (File.Exists(StoragePath)) File.Replace(temporary, StoragePath, null);
            else File.Move(temporary, StoragePath);
        }

        private static FruitTileCatalogDocument Load(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(FruitTileCatalogDocument));
                using (var stream = File.OpenRead(path))
                    return serializer.ReadObject(stream) as FruitTileCatalogDocument;
            }
            catch { return null; }
        }

        private void LogCatalog()
        {
            Fruit2048LearningSnapshot snapshot = CreateSnapshot();
            logger?.Info($"[Fruit2048 Catalog] LoadedProfiles={snapshot.Profiles.Count}, "
                + $"LearnedTiers={snapshot.KnownTierCount}, HighestObservedTier={snapshot.HighestObservedTier}, "
                + $"StoragePath='{StoragePath}'");
        }

        private void LogLearning(Fruit2048LearningResult result) => logger?.Info(
            $"[Fruit2048 Learning] Tier={result.Tier}, State='{result.State}', "
            + $"SampleCount={result.SampleCount}, RequiredSamples={result.RequiredSamples}, "
            + $"Confidence={result.Confidence:F3}, Evidence='{result.Evidence}', "
            + $"SourceTier={result.SourceTier}, Move='{result.Move}', Action='{result.Action}'");

        private static Fruit2048LearningResult LearningResult(FruitTileProfile profile,
            Fruit2048LearningAction action, int sourceTier, Fruit2048Move move,
            int sourceRow, int sourceColumn, string evidence) => new Fruit2048LearningResult
        {
            Tier = profile.Tier, State = profile.State, SampleCount = profile.SampleCount,
            RequiredSamples = RequiredSamples, Confidence = profile.Confidence, Action = action,
            Evidence = evidence, SourceTier = sourceTier, Move = move,
            SourceRow = sourceRow, SourceColumn = sourceColumn
        };

        private static FruitTileRecognitionResult Known(int tier, double confidence,
            Fruit2048RecognitionSource source) => new FruitTileRecognitionResult
            { Tier = tier, Confidence = confidence, Source = source };
        private static FruitTileRecognitionResult Unknown() => new FruitTileRecognitionResult
            { Tier = null, Confidence = 0, Source = Fruit2048RecognitionSource.Unknown };
        private static void Trim(List<FruitTilePrototype> prototypes)
        { while (prototypes.Count > MaximumPrototypesPerTier) prototypes.RemoveAt(0); }
        private static FruitTileProfile Clone(FruitTileProfile profile) => new FruitTileProfile
        {
            Tier = profile.Tier, State = profile.State, SampleCount = profile.SampleCount,
            Confidence = profile.Confidence, VerifiedByMerge = profile.VerifiedByMerge,
            Prototypes = profile.Prototypes.Select(item => new FruitTilePrototype
            { Fingerprint = item.Fingerprint, SamplePath = item.SamplePath, IsStaticSeed = item.IsStaticSeed }).ToList(),
            EvidenceIds = profile.EvidenceIds.ToList()
        };

        [DataContract]
        private sealed class FruitTileCatalogDocument
        {
            [DataMember] public int HighestObservedTier { get; set; }
            [DataMember] public List<FruitTileProfile> Profiles { get; set; } = new List<FruitTileProfile>();
        }

        private sealed class MatchCandidate
        {
            public FruitTileProfile Profile { get; set; }
            public FruitTilePrototype Prototype { get; set; }
            public int HashDistance { get; set; }
            public int Margin { get; set; }
            public double Score { get; set; }
            public double ScoreMargin { get; set; }
        }
    }
}
