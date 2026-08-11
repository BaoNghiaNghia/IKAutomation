using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Fruit2048
{
    public enum Fruit2048ScreenStatus { Ready, NotOpen, CaptureUnavailable }
    public enum Fruit2048RuntimeStatus { Idle, Starting, Scanning, Playing, Stopping, TargetReached, NoMoves, BoardUnknown, NeedsFreshBoard, MissingSeeds, Disconnected, Failed, Cancelled }
    public enum Fruit2048Outcome { TargetReached, NoMoves, BoardReadFailed, NeedsFreshBoard, MissingSeedAssets, ScreenNotOpen, Disconnected, Cancelled, Failed }
    public enum Fruit2048BootstrapState { MissingSeedAssets, ReadyToLearn, NeedsFreshBoard, Learning, Ready }
    public enum FruitTileLearningState { Unknown, Candidate, Learned }
    public enum Fruit2048RecognitionSource { Unknown, FastFingerprint, PrototypeMatch, StaticSeed }
    public enum Fruit2048RecognitionMode { Learning, Hybrid, Fast }
    public enum Fruit2048LearningAction { CandidateAdded, CandidateConfirmed, Learned, DuplicateIgnored, ConflictRejected }
    public enum Fruit2048SeedSource { Missing, StaticPackaged, UserCalibrated }
    public enum Fruit2048NavigationState { BoardReady, FruitFestivalTabVisible, CityFestivalEntryVisible, Unknown, CaptureUnavailable }
    public enum Fruit2048TeacherStatus { None, Acquiring, Learning, Paused, Completed, Error }

    public sealed class FruitTileVisualFingerprint
    {
        public string AverageHash { get; set; }
        public string EdgeHash { get; set; }
        public int MeanRed { get; set; }
        public int MeanGreen { get; set; }
        public int MeanBlue { get; set; }
    }

    public sealed class FruitTilePrototype
    {
        public FruitTileVisualFingerprint Fingerprint { get; set; }
        public string SamplePath { get; set; }
        public bool IsStaticSeed { get; set; }
    }

    public sealed class FruitTileProfile
    {
        public int Tier { get; set; }
        public FruitTileLearningState State { get; set; }
        public int SampleCount { get; set; }
        public double Confidence { get; set; }
        public bool VerifiedByMerge { get; set; }
        public List<FruitTilePrototype> Prototypes { get; set; } = new List<FruitTilePrototype>();
        public List<string> EvidenceIds { get; set; } = new List<string>();
    }

    public sealed class FruitTileRecognitionResult
    {
        public int? Tier { get; set; }
        public double Confidence { get; set; }
        public Fruit2048RecognitionSource Source { get; set; }
    }

    public sealed class Fruit2048LearningSnapshot
    {
        public Fruit2048RecognitionMode Mode { get; set; }
        public int KnownTierCount { get; set; }
        public int HighestObservedTier { get; set; }
        public IReadOnlyList<FruitTileProfile> Profiles { get; set; }
    }

    public sealed class Fruit2048SeedAvailability
    {
        public bool EmptyAvailable { get; set; }
        public bool Tier1Available { get; set; }
        public string EmptyPath { get; set; }
        public string Tier1Path { get; set; }
        public Fruit2048SeedSource EmptySource { get; set; }
        public Fruit2048SeedSource Tier1Source { get; set; }
        public bool IsReady => EmptyAvailable && Tier1Available;
    }

    public sealed class Fruit2048CalibrationCapture
    {
        public string DeviceName { get; set; }
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public ImageRegion BoardBounds { get; set; }
        public byte[] ScreenshotPng { get; set; }
        public string Error { get; set; }
        public bool Success => ScreenshotPng != null && BoardBounds.Width > 0 && BoardBounds.Height > 0;
    }

    public sealed class Fruit2048CalibrationResult
    {
        public bool Success { get; set; }
        public Fruit2048BootstrapState BootstrapState { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
    }

    public sealed class Fruit2048NavigationResult
    {
        public bool Success { get; set; }
        public Fruit2048NavigationState State { get; set; }
        public string Error { get; set; }
    }
    public sealed class Fruit2048TeacherSnapshot
    {
        public string DeviceName { get; set; }
        public string SessionId { get; set; }
        public Fruit2048TeacherStatus Status { get; set; }
        public long CatalogVersion { get; set; }
    }

    public sealed class Fruit2048LearningResult
    {
        public int Tier { get; set; }
        public FruitTileLearningState State { get; set; }
        public int SampleCount { get; set; }
        public int RequiredSamples { get; set; }
        public double Confidence { get; set; }
        public Fruit2048LearningAction Action { get; set; }
        public string Evidence { get; set; }
        public int SourceTier { get; set; }
        public Fruit2048Move Move { get; set; }
        public int SourceRow { get; set; }
        public int SourceColumn { get; set; }
        public int DestinationRow { get; set; }
        public int DestinationColumn { get; set; }
        public string Error { get; set; }
    }

    public sealed class FruitTierMetadata
    {
        public int Tier { get; set; }
        public string DisplayName { get; set; }
        public int Equivalent2048Value => FruitTierCatalog.ToValue(Tier);
    }

    public static class FruitTierCatalog
    {
        public static int ToValue(int tier)
        {
            if (tier <= 0) return 0;
            if (tier > 31) throw new ArgumentOutOfRangeException(nameof(tier));
            return 1 << (tier - 1);
        }

        public static int ToTier(int value)
        {
            if (value == 0) return 0;
            if (value < 0 || (value & (value - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            int tier = 1;
            while (value > 1) { value >>= 1; tier++; }
            return tier;
        }
    }

    public sealed class Fruit2048Cell
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public ImageRegion Bounds { get; set; }
        public int? Value { get; set; }
        public int? Tier { get; set; }
        public double Confidence { get; set; }
        public Fruit2048RecognitionSource RecognitionSource { get; set; }
        public FruitTileVisualFingerprint Fingerprint { get; set; }
    }

    public sealed class Fruit2048BoardReadResult
    {
        public Fruit2048ScreenStatus ScreenStatus { get; set; }
        public Fruit2048Board Board { get; set; }
        public IReadOnlyList<Fruit2048Cell> Cells { get; set; }
        public IReadOnlyList<Fruit2048Cell> UnknownCells { get; set; }
        public bool Success { get; set; }
        public int HighestTile { get; set; }
        public long DurationMs { get; set; }
        public string Error { get; set; }
        public IReadOnlyList<string> MissingAssets { get; set; }
        public ImageRegion? BoardRegion { get; set; }
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public Fruit2048LearningSnapshot Learning { get; set; }
        public Fruit2048BootstrapState BootstrapState { get; set; }
        public int FastPathCells { get; set; }
        public int FallbackCells { get; set; }
    }

    public sealed class Fruit2048RunRequest
    {
        public int TargetTile { get; set; } = 2048;
        public bool AutoRefresh { get; set; }
    }

    public sealed class Fruit2048Progress
    {
        public string DeviceName { get; set; }
        public Fruit2048RuntimeStatus Status { get; set; }
        public Fruit2048Board Board { get; set; }
        public IReadOnlyList<Fruit2048Cell> Cells { get; set; }
        public int HighestTile { get; set; }
        public int MoveCount { get; set; }
        public Fruit2048Move? LastMove { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
        public Fruit2048RecognitionMode RecognitionMode { get; set; }
        public int KnownTierCount { get; set; }
        public int HighestObservedTier { get; set; }
        public IReadOnlyList<FruitTileProfile> LearningProfiles { get; set; }
        public string LearningMessage { get; set; }
        public Fruit2048BootstrapState BootstrapState { get; set; }
        public int UnknownCellCount { get; set; }
    }

    public sealed class Fruit2048RunResult
    {
        public string DeviceName { get; set; }
        public Fruit2048Outcome Outcome { get; set; }
        public int MoveCount { get; set; }
        public int HighestTile { get; set; }
        public int TargetTile { get; set; }
        public bool WasCancelled { get; set; }
        public string Error { get; set; }
    }

    public interface IFruit2048BoardReader
    {
        Fruit2048SeedAvailability SeedAvailability { get; }
        Task<Fruit2048BoardReadResult> ReadAsync(string deviceName, CancellationToken cancellationToken);
        Task<bool> TryTapRefreshAsync(string deviceName, CancellationToken cancellationToken);
    }

    public interface IFruit2048SeedCalibrationService
    {
        Task<Fruit2048CalibrationCapture> CaptureAsync(string deviceName, CancellationToken cancellationToken);
        Task<Fruit2048CalibrationResult> SaveAsync(Fruit2048CalibrationCapture capture,
            int emptyRow, int emptyColumn, int tier1Row, int tier1Column,
            CancellationToken cancellationToken);
        void DeleteCalibratedSeeds();
    }

    public interface IFruit2048NavigationService
    {
        Task<Fruit2048NavigationResult> EnsureFruit2048ScreenAsync(string deviceName,
            IProgress<string> status, CancellationToken cancellationToken);
    }
    public interface IFruit2048LearningCoordinator
    {
        Fruit2048TeacherSnapshot Teacher { get; }
        bool SelectTeacher(string deviceName, out string reason);
        bool TryAcquireTeacher(string deviceName, out string sessionId, out string reason);
        void ReleaseTeacher(string deviceName, string sessionId, string reason);
        bool CanLearn(string deviceName);
        Fruit2048LearningResult ObserveMerge(string deviceName, int tier, FruitTileVisualFingerprint fingerprint,
            string transitionId, int sourceTier, Fruit2048Move move, int sourceRow, int sourceColumn);
    }

    public interface IFruitTileLearningCatalog
    {
        string StoragePath { get; }
        Fruit2048LearningSnapshot Snapshot { get; }
        FruitTileRecognitionResult Recognize(FruitTileVisualFingerprint fingerprint);
        Fruit2048LearningResult ObserveMerge(int tier, FruitTileVisualFingerprint fingerprint,
            string evidenceId, int sourceTier, Fruit2048Move move, int sourceRow, int sourceColumn);
        Fruit2048LearningResult RejectConflict(int expectedTier, int matchedTier,
            double confidence, Fruit2048Move move, int sourceRow, int sourceColumn);
        void ObserveTier(int tier);
        void ResetLearningData();
        string ExportLearningDiagnostics(string destinationDirectory);
    }

    public interface IFruit2048TransitionLearner
    {
        IReadOnlyList<Fruit2048LearningResult> Learn(string deviceName, Fruit2048Board before,
            Fruit2048Move move, IReadOnlyList<Fruit2048BoardReadResult> observations,
            string evidenceId);
    }

    public interface IFruit2048Solver
    {
        bool TryChooseMove(Fruit2048Board board, out Fruit2048Move move);
    }

    public interface IFruit2048SwipeExecutor
    {
        Task ExecuteAsync(string deviceName, Fruit2048Move move, ImageRegion boardRegion,
            int screenWidth, int screenHeight, CancellationToken cancellationToken);
    }

    public interface IFruit2048AutomationService
    {
        Task<Fruit2048RunResult> RunAsync(string deviceName, Fruit2048RunRequest request,
            IProgress<Fruit2048Progress> progress, CancellationToken cancellationToken);
        Task<Fruit2048BoardReadResult> ScanAsync(string deviceName, CancellationToken cancellationToken);
        Task<Fruit2048BoardReadResult> SendManualMoveAsync(string deviceName, Fruit2048Move move,
            CancellationToken cancellationToken);
    }
}
