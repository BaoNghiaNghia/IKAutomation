using System;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.Fruit2048
{
    /// <summary>Small, deterministic rules for the teacher-only Tier 2 learning proof.</summary>
    public static class Fruit2048FirstLearningProofPolicy
    {
        public const int MaxProofMoves = 50;
        public const int RequiredTier2Samples = 3;

        /// <summary>
        /// The 14 Empty / 2 Tier 1 completion rule exists only to bootstrap a
        /// brand-new catalog (and to make the dedicated proof deterministic).
        /// A normal session which already knows higher tiers must be allowed to
        /// resume the board that is currently on screen.
        /// </summary>
        public static bool RequiresInitialFreshBoardBootstrap(Fruit2048RunMode mode,
            Fruit2048LearningSnapshot snapshot)
        {
            return mode == Fruit2048RunMode.FirstLearningProof
                || (snapshot?.HighestObservedTier ?? 0) <= 1;
        }

        public static bool IsFreshTier1Board(Fruit2048BoardReadResult read)
        {
            if (read == null || !read.Success || read.Board == null || read.Cells == null
                || read.Cells.Count != Fruit2048Board.Size * Fruit2048Board.Size) return false;
            return read.Cells.Count(cell => cell.Tier == 0) == 14
                && read.Cells.Count(cell => cell.Tier == 1) == 2
                && read.Cells.All(cell => cell.Tier.HasValue);
        }

        public static FruitTileProfile Tier2Profile(Fruit2048LearningSnapshot snapshot)
        {
            return snapshot?.Profiles?.FirstOrDefault(profile => profile.Tier == 2);
        }

        public static bool IsTier2Learned(Fruit2048LearningSnapshot snapshot)
        {
            return Tier2Profile(snapshot)?.State == FruitTileLearningState.Learned;
        }

        public static bool HasCatalogTier2Recognition(Fruit2048BoardReadResult read)
        {
            return read?.Cells != null && read.Cells.Any(cell => cell.Tier == 2
                && (cell.RecognitionSource == Fruit2048RecognitionSource.FastFingerprint
                    || cell.RecognitionSource == Fruit2048RecognitionSource.PrototypeMatch));
        }

        public static Fruit2048FirstLearningProofProgress CreateProgress(int movesUsed,
            Fruit2048LearningSnapshot snapshot, bool recognitionConfirmed, string stage)
        {
            FruitTileProfile tier2 = Tier2Profile(snapshot);
            return new Fruit2048FirstLearningProofProgress
            {
                MovesUsed = movesUsed,
                MaxMoves = MaxProofMoves,
                Tier2SampleCount = tier2?.SampleCount ?? 0,
                RequiredSamples = RequiredTier2Samples,
                Tier2Learned = tier2?.State == FruitTileLearningState.Learned,
                Tier2RecognitionConfirmed = recognitionConfirmed,
                Stage = stage ?? string.Empty
            };
        }
    }
}
