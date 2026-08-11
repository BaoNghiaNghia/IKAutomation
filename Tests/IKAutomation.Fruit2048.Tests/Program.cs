using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;

internal static class Program
{
    private static int passed, failed;

    private static int Main()
    {
        Run("Lease_NoneToFarm_Succeeds", LeaseNoneToFarm);
        Run("Lease_NoneToFruit_Succeeds", LeaseNoneToFruit);
        Run("Lease_FarmBlocksFruit", LeaseFarmBlocksFruit);
        Run("Lease_FruitBlocksFarm", LeaseFruitBlocksFarm);
        Run("Lease_ReleaseFarmAllowsFruit", LeaseReleaseFarmAllowsFruit);
        Run("Lease_ReleaseFruitAllowsFarm", LeaseReleaseFruitAllowsFarm);
        Run("Lease_ConcurrentAcquireHasSingleWinner", LeaseConcurrentSingleWinner);
        Run("Lease_DifferentDevicesAllowDifferentOwners", LeaseDifferentDevices);
        Run("UI_FarmOwnedDeviceIsVisible", UiFarmVisible);
        Run("UI_FarmOwnedDeviceIsNotSelectable", UiFarmNotSelectable);
        Run("UI_SelectAllSkipsFarm", UiSelectAllSkipsFarm);
        Run("UI_OpeningModelAcquiresNothing", UiOpeningAcquiresNothing);
        Run("UI_FruitOwnerStatus", UiFruitStatus);
        Run("UI_OwnershipChangeEvent", UiOwnershipChangeEvent);
        Run("Board_GeometryCreates16Cells", BoardGeometry);
        Run("Board_UnknownCellFailsRead", BoardUnknownFails);
        Run("Board_HighestTile", BoardHighestTile);
        Run("Rules_LeftMerge", RulesLeft);
        Run("Rules_RightMerge", RulesRight);
        Run("Rules_UpMerge", RulesUp);
        Run("Rules_DownMerge", RulesDown);
        Run("Rules_MultipleIndependentMerges", RulesMultiple);
        Run("Rules_NoDoubleMerge", RulesNoDoubleMerge);
        Run("Rules_IllegalMove", RulesIllegalMove);
        Run("Solver_ReturnsOnlyLegalMove", SolverLegal);
        Run("Solver_HandlesOneLegalMove", SolverOneMove);
        Run("Solver_TargetDetection", SolverTarget);
        Run("Runtime_ExactlyOneSwipePerIteration", RuntimeOneSwipe);
        Run("Runtime_StopPreventsSwipes", RuntimeStop);
        Run("Runtime_ExceptionReleasesLease", RuntimeExceptionReleases);
        Run("Runtime_TargetReleasesLease", RuntimeTargetReleases);
        Run("Runtime_DisconnectReleasesLease", RuntimeDisconnectReleases);
        Run("Runtime_AutoRefreshDefaultsFalse", RuntimeDefaultRefresh);
        Run("Learning_SeedProfileCanLoad", LearningSeedProfileCanLoad);
        Run("Learning_CatalogPersistsAndReloads", LearningCatalogPersistsAndReloads);
        Run("Recognition_HighConfidenceFingerprintUsesFastPath", RecognitionFastPath);
        Run("Recognition_AmbiguousFastFingerprintFallsBack", RecognitionFallback);
        Run("Recognition_UnknownNeverBecomesEmpty", RecognitionUnknownNotEmpty);
        Run("Learning_EqualMergeInfersNextTier", LearningMergeInfersNextTier);
        Run("Learning_SpawnedUnknownIsNotLabeled", LearningSpawnNotLabeled);
        Run("Learning_OneObservationRemainsCandidate", LearningOneCandidate);
        Run("Learning_ThreeIndependentTransitionsBecomeLearned", LearningBecomesLearned);
        Run("Learning_DuplicateEvidenceIsIgnored", LearningDuplicateIgnored);
        Run("Learning_ConflictIsRejected", LearningConflictRejected);
        Run("Recognition_LearnedTierUsesFastPath", RecognitionLearnedFast);
        Run("Mode_FastUsesHighestObservedTier", ModeFastByHighest);
        Run("Mode_NewHigherTierChangesFastToHybrid", ModeHigherTierHybrid);
        Run("Mode_LearnedHigherTierChangesHybridToFast", ModeHigherTierFast);
        Run("Learning_RuntimeDataSurvivesReload", LearningSurvivesReload);
        Run("Safety_UnknownBoardBlocksSolver", SafetyUnknownBlocksSolver);
        Run("Safety_FailedLearningSendsNoSwipe", SafetyFailedLearningNoSwipe);
        Run("Storage_DefaultPathIsOutsideBinObj", StorageOutsideBuildOutput);
        Run("Bootstrap_MissingEmptySeedBlocksAuto", BootstrapMissingEmpty);
        Run("Bootstrap_MissingTier1SeedBlocksAuto", BootstrapMissingTier1);
        Run("Bootstrap_InitialUnknownHighTierDoesNotSwipe", SafetyFailedLearningNoSwipe);
        Run("Bootstrap_FreshTier1BoardMayStart", RuntimeOneSwipe);
        Run("Progression_Tier1MergeProducesCandidateTier2", LearningMergeInfersNextTier);
        Run("Progression_ThreeTier1MergesLearnTier2", LearningBecomesLearned);
        Run("Progression_LearnedTier2UsesFastPath", RecognitionFastPath);
        Run("Progression_Tier2MergeStartsTier3", ProgressionTier3Candidate);
        Run("Candidate_LowConfidenceCannotClassifyArbitraryCell", CandidateStrict);
        Run("Transition_DeterministicCandidateTemporarilyResolvesDestination", TransitionTemporaryResolution);
        Run("Transition_UnknownSpawnRemainsUnknown", LearningSpawnNotLabeled);
        Run("Learning_ResetPreservesStaticSeed", ResetPreservesSeed);
        Run("Performance_FastPathIncreasesAfterLearning", FastPathIncreases);
        Run("Safety_UnresolvedBoardNeverSwipes", SafetyFailedLearningNoSwipe);
        Run("Calibration_SelectedCellUsesBoardReaderGeometry", CalibrationBoardGeometry);
        Run("Calibration_SeedsPersistOutsideBuildOutput", CalibrationSeedsOutsideBuildOutput);
        Run("Calibration_UserSeedOverridesStaticSeed", CalibrationUserSeedWins);
        Run("Calibration_StaticSeedUsedWhenUserSeedAbsent", CalibrationStaticSeedFallback);
        Console.WriteLine($"Fruit2048 focused tests: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine(name + ": PASSED"); }
        catch (Exception ex) { failed++; Console.WriteLine(name + ": FAILED - " + ex.Message); }
    }

    private static void Assert(bool condition, string message = "Assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }

    private static DeviceAutomationOwnershipService NewOwnership() => new DeviceAutomationOwnershipService();

    private static void CalibrationBoardGeometry()
    {
        var board = new ImageRegion(120, 80, 400, 400);
        ImageRegion selected = Fruit2048ScreenProfile.GetCellRegion(board, 2, 3);
        Assert(selected.X == 420 && selected.Y == 280 && selected.Width == 100 && selected.Height == 100);
    }

    private static void CalibrationSeedsOutsideBuildOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), "Fruit2048-Calibrated", Guid.NewGuid().ToString("N"));
        var store = new Fruit2048CalibratedSeedStore(root);
        byte[] png = TinyPng(Color.LimeGreen);
        store.SavePair(1280, 720, png, TinyPng(Color.Goldenrod), new Fruit2048SeedMetadata());
        Assert(File.Exists(store.GetSeedPath(1280, 720, 0)) && !store.GetSeedPath(1280, 720, 0).Contains("\\bin\\"));
    }

    private static void CalibrationUserSeedWins()
    {
        string root = Path.Combine(Path.GetTempPath(), "Fruit2048-Calibrated", Guid.NewGuid().ToString("N"));
        string app = Path.Combine(Path.GetTempPath(), "Fruit2048-Packaged", Guid.NewGuid().ToString("N"));
        string packaged = Path.Combine(app, Fruit2048TemplateCatalog.RelativeDirectory); Directory.CreateDirectory(packaged);
        File.WriteAllBytes(Path.Combine(packaged, Fruit2048TemplateCatalog.EmptyTile), TinyPng(Color.Red));
        var store = new Fruit2048CalibratedSeedStore(root); store.SavePair(1280, 720, TinyPng(Color.Blue), TinyPng(Color.Green), new Fruit2048SeedMetadata());
        var catalog = new Fruit2048TemplateCatalog(app, store);
        Assert(catalog.SeedAvailability.EmptySource == Fruit2048SeedSource.UserCalibrated);
    }

    private static void CalibrationStaticSeedFallback()
    {
        string root = Path.Combine(Path.GetTempPath(), "Fruit2048-Calibrated", Guid.NewGuid().ToString("N"));
        string app = Path.Combine(Path.GetTempPath(), "Fruit2048-Packaged", Guid.NewGuid().ToString("N"));
        string packaged = Path.Combine(app, Fruit2048TemplateCatalog.RelativeDirectory); Directory.CreateDirectory(packaged);
        File.WriteAllBytes(Path.Combine(packaged, Fruit2048TemplateCatalog.EmptyTile), TinyPng(Color.Red));
        File.WriteAllBytes(Path.Combine(packaged, Fruit2048TemplateCatalog.Tier1Tile), TinyPng(Color.Green));
        var catalog = new Fruit2048TemplateCatalog(app, new Fruit2048CalibratedSeedStore(root));
        Assert(catalog.SeedAvailability.IsReady && catalog.SeedAvailability.EmptySource == Fruit2048SeedSource.StaticPackaged);
    }

    private static byte[] TinyPng(Color color)
    {
        using (var bitmap = new Bitmap(8, 8)) using (var graphics = Graphics.FromImage(bitmap)) using (var stream = new MemoryStream())
        { graphics.Clear(color); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray(); }
    }
    private static void LeaseNoneToFarm() { IDeviceAutomationLease l; Assert(NewOwnership().TryAcquire("A", DeviceAutomationOwner.Farm, out l)); l.Dispose(); }
    private static void LeaseNoneToFruit() { IDeviceAutomationLease l; Assert(NewOwnership().TryAcquire("A", DeviceAutomationOwner.Fruit2048, out l)); l.Dispose(); }
    private static void LeaseFarmBlocksFruit() { var s = NewOwnership(); IDeviceAutomationLease a, b; Assert(s.TryAcquire("A", DeviceAutomationOwner.Farm, out a)); Assert(!s.TryAcquire("A", DeviceAutomationOwner.Fruit2048, out b)); a.Dispose(); }
    private static void LeaseFruitBlocksFarm() { var s = NewOwnership(); IDeviceAutomationLease a, b; Assert(s.TryAcquire("A", DeviceAutomationOwner.Fruit2048, out a)); Assert(!s.TryAcquire("A", DeviceAutomationOwner.Farm, out b)); a.Dispose(); }
    private static void LeaseReleaseFarmAllowsFruit() { var s = NewOwnership(); IDeviceAutomationLease a, b; s.TryAcquire("A", DeviceAutomationOwner.Farm, out a); a.Dispose(); Assert(s.TryAcquire("A", DeviceAutomationOwner.Fruit2048, out b)); b.Dispose(); }
    private static void LeaseReleaseFruitAllowsFarm() { var s = NewOwnership(); IDeviceAutomationLease a, b; s.TryAcquire("A", DeviceAutomationOwner.Fruit2048, out a); a.Dispose(); Assert(s.TryAcquire("A", DeviceAutomationOwner.Farm, out b)); b.Dispose(); }
    private static void LeaseConcurrentSingleWinner()
    {
        var service = NewOwnership(); var leases = new ConcurrentBag<IDeviceAutomationLease>();
        Task[] tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => { IDeviceAutomationLease lease; if (service.TryAcquire("A", DeviceAutomationOwner.Fruit2048, out lease)) leases.Add(lease); })).ToArray();
        Task.WaitAll(tasks); Assert(leases.Count == 1); foreach (var lease in leases) lease.Dispose();
    }
    private static void LeaseDifferentDevices() { var s = NewOwnership(); IDeviceAutomationLease a, b; Assert(s.TryAcquire("A", DeviceAutomationOwner.Farm, out a)); Assert(s.TryAcquire("B", DeviceAutomationOwner.Fruit2048, out b)); a.Dispose(); b.Dispose(); }

    private static Fruit2048DeviceAvailability Availability(DeviceAutomationOwner owner) => new Fruit2048DeviceAvailability { DeviceName = "A", Connected = true, Owner = owner };
    private static void UiFarmVisible() { var item = Availability(DeviceAutomationOwner.Farm); Assert(item.DeviceName == "A"); }
    private static void UiFarmNotSelectable() { Assert(!Availability(DeviceAutomationOwner.Farm).IsSelectable); }
    private static void UiSelectAllSkipsFarm() { var a = Availability(DeviceAutomationOwner.Farm); var b = Availability(DeviceAutomationOwner.None); b.DeviceName = "B"; Assert(Fruit2048DeviceAvailability.SelectAllAvailable(new[] { a, b }).SequenceEqual(new[] { "B" })); }
    private static void UiOpeningAcquiresNothing() { var service = NewOwnership(); var model = Availability(service.GetOwner("A")); Assert(model.Owner == DeviceAutomationOwner.None && service.GetOwner("A") == DeviceAutomationOwner.None); }
    private static void UiFruitStatus() { Assert(Availability(DeviceAutomationOwner.Fruit2048).DisplayStatus.Contains("Fruit 2048")); }
    private static void UiOwnershipChangeEvent() { var s = NewOwnership(); int changes = 0; s.OwnershipChanged += (o, e) => changes++; IDeviceAutomationLease l; s.TryAcquire("A", DeviceAutomationOwner.Farm, out l); l.Dispose(); Assert(changes == 2); }

    private static void BoardGeometry() { var board = new ImageRegion(0, 0, 401, 399); var cells = Enumerable.Range(0, 4).SelectMany(r => Enumerable.Range(0, 4).Select(c => Fruit2048ScreenProfile.GetCellRegion(board, r, c))).ToArray(); Assert(cells.Length == 16 && cells.Sum(c => c.Width * c.Height) == board.Width * board.Height); }
    private static List<Fruit2048Cell> Cells(params int?[] values) { return Enumerable.Range(0, 16).Select(i => new Fruit2048Cell { Row = i / 4, Column = i % 4, Bounds = new ImageRegion(i % 4, i / 4, 1, 1), Value = i < values.Length ? values[i] : 0 }).ToList(); }
    private static void BoardUnknownFails() { var cells = Cells(); cells[7].Value = null; var result = Fruit2048BoardAssembler.Assemble(cells, 1); Assert(!result.Success && result.UnknownCells.Count == 1 && result.Board == null); }
    private static void BoardHighestTile() { var values = Enumerable.Repeat<int?>(0, 16).ToArray(); values[9] = 512; var result = Fruit2048BoardAssembler.Assemble(Cells(values), 1); Assert(result.HighestTile == 512); }
    private static Fruit2048Board Board(params int[] values) { int[] all = new int[16]; Array.Copy(values, all, values.Length); return new Fruit2048Board(all); }
    private static void RulesLeft() { Assert(Board(1, 1).Simulate(Fruit2048Move.Left)[0, 0] == 2); }
    private static void RulesRight() { Assert(Board(1, 1).Simulate(Fruit2048Move.Right)[0, 3] == 2); }
    private static void RulesUp() { Assert(Board(1, 0, 0, 0, 1).Simulate(Fruit2048Move.Up)[0, 0] == 2); }
    private static void RulesDown() { Assert(Board(1, 0, 0, 0, 1).Simulate(Fruit2048Move.Down)[3, 0] == 2); }
    private static void RulesMultiple() { var b = Board(1, 1, 1, 1).Simulate(Fruit2048Move.Left); Assert(b[0, 0] == 2 && b[0, 1] == 2); }
    private static void RulesNoDoubleMerge() { var b = Board(1, 1, 2).Simulate(Fruit2048Move.Left); Assert(b[0, 0] == 2 && b[0, 1] == 2); }
    private static void RulesIllegalMove() { Assert(!Board(1).CanMove(Fruit2048Move.Left)); }
    private static void SolverLegal() { var board = Board(1, 1); Fruit2048Move move; Assert(new Fruit2048Solver().TryChooseMove(board, out move) && board.CanMove(move)); }
    private static void SolverOneMove() { var board = Board(1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 1, 2, 4, 8, 0); Fruit2048Move move; Assert(new Fruit2048Solver().TryChooseMove(board, out move) && board.CanMove(move)); }
    private static void SolverTarget() { Assert(Board(2048).HasReached(2048)); }

    private static void RuntimeOneSwipe() { var fixture = RuntimeFixture.Create(Board(1, 1), Board(2), true); fixture.Run(); Assert(fixture.Swipe.Count == 1); }
    private static void RuntimeStop() { var fixture = RuntimeFixture.Create(Board(1, 1), Board(2), true); fixture.Cancellation.Cancel(); fixture.Run(); Assert(fixture.Swipe.Count == 0); }
    private static void RuntimeExceptionReleases() { var fixture = RuntimeFixture.Create(Board(1, 1), Board(2), true); fixture.Reader.Throw = true; fixture.Run(); Assert(fixture.Ownership.GetOwner("A") == DeviceAutomationOwner.None); }
    private static void RuntimeTargetReleases() { var fixture = RuntimeFixture.Create(Board(2048), Board(2048), true); fixture.Run(); Assert(fixture.Ownership.GetOwner("A") == DeviceAutomationOwner.None); }
    private static void RuntimeDisconnectReleases() { var fixture = RuntimeFixture.Create(Board(1, 1), Board(2), false); fixture.Run(); Assert(fixture.Ownership.GetOwner("A") == DeviceAutomationOwner.None); }
    private static void RuntimeDefaultRefresh() { Assert(!new Fruit2048RunRequest().AutoRefresh); }

    private static FruitTileVisualFingerprint Fingerprint(ulong hash = 0,
        ulong edge = 0, int color = 100) => new FruitTileVisualFingerprint
    {
        AverageHash = hash.ToString("X16"), EdgeHash = edge.ToString("X16"),
        MeanRed = color, MeanGreen = color, MeanBlue = color
    };

    private static string TempCatalogPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "IKAutomation-Fruit2048-Tests",
            Guid.NewGuid().ToString("N"));
        return Path.Combine(directory, "catalog.json");
    }

    private static FruitTileLearningCatalog LearnedCatalog(int highestTier = 2)
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.AddSeed(1, Fingerprint(ulong.MaxValue));
        for (int tier = 2; tier <= highestTier; tier++)
        for (int sample = 1; sample <= FruitTileLearningCatalog.RequiredSamples; sample++)
            catalog.ObserveMerge(tier, Fingerprint((ulong)(tier * 0x100)),
                "tier-" + tier + "-sample-" + sample, tier - 1,
                Fruit2048Move.Left, 0, 0);
        return catalog;
    }

    private static void LearningSeedProfileCanLoad()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.AddSeed(1, Fingerprint(0x55));
        FruitTileRecognitionResult result = catalog.Recognize(Fingerprint(0x55));
        Assert(result.Tier == 1 && result.Source == Fruit2048RecognitionSource.StaticSeed);
    }

    private static void LearningCatalogPersistsAndReloads()
    {
        string path = TempCatalogPath();
        var catalog = new FruitTileLearningCatalog(path);
        catalog.AddSeed(1, Fingerprint(1));
        catalog.ObserveMerge(2, Fingerprint(2), "a", 1, Fruit2048Move.Left, 0, 0);
        var reloaded = new FruitTileLearningCatalog(path);
        Assert(reloaded.Snapshot.Profiles.Any(profile => profile.Tier == 2
            && profile.SampleCount == 1));
    }

    private static void RecognitionFastPath()
    {
        FruitTileLearningCatalog catalog = LearnedCatalog();
        FruitTileRecognitionResult result = catalog.Recognize(Fingerprint(0x200));
        Assert(result.Tier == 2 && result.Source == Fruit2048RecognitionSource.FastFingerprint);
    }

    private static void RecognitionFallback()
    {
        FruitTileLearningCatalog catalog = LearnedCatalog();
        FruitTileRecognitionResult result = catalog.Recognize(Fingerprint(0x23F));
        Assert(result.Tier == 2 && result.Source == Fruit2048RecognitionSource.PrototypeMatch);
    }

    private static void RecognitionUnknownNotEmpty()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        FruitTileRecognitionResult result = catalog.Recognize(Fingerprint(ulong.MaxValue));
        Assert(!result.Tier.HasValue);
        var cells = Cells(); cells[0].Value = null; cells[0].Tier = null;
        Assert(!Fruit2048BoardAssembler.Assemble(cells, 0).Success);
    }

    private static Fruit2048BoardReadResult UnknownObservation(
        FruitTileVisualFingerprint fingerprint, int row = 0, int column = 0)
    {
        List<Fruit2048Cell> cells = Cells();
        Fruit2048Cell cell = cells.First(item => item.Row == row && item.Column == column);
        cell.Value = null; cell.Tier = null; cell.Fingerprint = fingerprint;
        Fruit2048BoardReadResult result = Fruit2048BoardAssembler.Assemble(cells, 1);
        result.Cells = cells; result.UnknownCells = new[] { cell };
        result.ScreenStatus = Fruit2048ScreenStatus.Ready;
        result.BoardRegion = new ImageRegion(0, 0, 400, 400);
        result.ScreenWidth = 1280; result.ScreenHeight = 720;
        return result;
    }

    private static void LearningMergeInfersNextTier()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        var learner = new Fruit2048TransitionLearner(catalog);
        Fruit2048BoardReadResult a = UnknownObservation(Fingerprint(10));
        Fruit2048BoardReadResult b = UnknownObservation(Fingerprint(10));
        var results = learner.Learn("A", Board(1, 1), Fruit2048Move.Left,
            new[] { a, b }, "merge-1");
        Assert(results.Count == 1 && results[0].Tier == 2);
    }

    private static void LearningSpawnNotLabeled()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        var learner = new Fruit2048TransitionLearner(catalog);
        Fruit2048BoardReadResult a = UnknownObservation(Fingerprint(10), 3, 3);
        Fruit2048BoardReadResult b = UnknownObservation(Fingerprint(10), 3, 3);
        Assert(learner.Learn("A", Board(1), Fruit2048Move.Right,
            new[] { a, b }, "spawn").Count == 0);
    }

    private static void LearningOneCandidate()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        Fruit2048LearningResult result = catalog.ObserveMerge(2, Fingerprint(2),
            "one", 1, Fruit2048Move.Left, 0, 0);
        Assert(result.State == FruitTileLearningState.Candidate && result.SampleCount == 1);
    }

    private static void LearningBecomesLearned()
    {
        FruitTileLearningCatalog catalog = LearnedCatalog();
        Assert(catalog.Snapshot.Profiles.Single(profile => profile.Tier == 2).State
            == FruitTileLearningState.Learned);
    }

    private static void LearningDuplicateIgnored()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.ObserveMerge(2, Fingerprint(2), "same", 1, Fruit2048Move.Left, 0, 0);
        Fruit2048LearningResult duplicate = catalog.ObserveMerge(2, Fingerprint(2),
            "same", 1, Fruit2048Move.Left, 0, 0);
        Assert(duplicate.Action == Fruit2048LearningAction.DuplicateIgnored
            && duplicate.SampleCount == 1);
    }

    private static void LearningConflictRejected()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        Fruit2048LearningResult conflict = catalog.RejectConflict(3, 2, 0.9,
            Fruit2048Move.Down, 1, 1);
        Assert(conflict.Action == Fruit2048LearningAction.ConflictRejected
            && conflict.Evidence == "LearningConflict");
    }

    private static void RecognitionLearnedFast() => RecognitionFastPath();

    private static void ModeFastByHighest()
    {
        Assert(LearnedCatalog(2).Snapshot.Mode == Fruit2048RecognitionMode.Fast);
    }

    private static void ModeHigherTierHybrid()
    {
        FruitTileLearningCatalog catalog = LearnedCatalog(2);
        catalog.ObserveTier(3);
        Assert(catalog.Snapshot.Mode == Fruit2048RecognitionMode.Hybrid);
    }

    private static void ModeHigherTierFast()
    {
        FruitTileLearningCatalog catalog = LearnedCatalog(2);
        catalog.ObserveTier(3);
        for (int sample = 0; sample < 3; sample++) catalog.ObserveMerge(3,
            Fingerprint(0x300), "three-" + sample, 2, Fruit2048Move.Up, 0, 0);
        Assert(catalog.Snapshot.Mode == Fruit2048RecognitionMode.Fast);
    }

    private static void LearningSurvivesReload() => LearningCatalogPersistsAndReloads();
    private static void SafetyUnknownBlocksSolver() => BoardUnknownFails();

    private static void SafetyFailedLearningNoSwipe()
    {
        var reader = new FakeReader(Board(1)) { ForcedResult = UnknownObservation(Fingerprint(99), 3, 3) };
        var swipe = new FakeSwipe(); var ownership = NewOwnership();
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        var service = new Fruit2048AutomationService(new FakeClient(), reader,
            new Fruit2048Solver(), swipe, ownership,
            new Fruit2048TransitionLearner(catalog), catalog, null);
        using (var cancellation = new CancellationTokenSource())
            service.RunAsync("A", new Fruit2048RunRequest(), null,
                cancellation.Token).GetAwaiter().GetResult();
        Assert(swipe.Count == 0);
    }

    private static void StorageOutsideBuildOutput()
    {
        string path = FruitTileLearningCatalog.GetDefaultStoragePath().Replace('/', '\\');
        Assert(path.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) < 0
            && path.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0);
    }

    private static Fruit2048RunResult RunWithSeeds(Fruit2048SeedAvailability seeds)
    {
        var reader = new FakeReader(Board(1, 1)) { SeedAvailability = seeds };
        var swipe = new FakeSwipe(); var ownership = NewOwnership();
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        var service = new Fruit2048AutomationService(new FakeClient(), reader,
            new Fruit2048Solver(), swipe, ownership,
            new Fruit2048TransitionLearner(catalog), catalog, null);
        using (var cancellation = new CancellationTokenSource())
            return service.RunAsync("A", new Fruit2048RunRequest { TargetTile = 2 }, null,
                cancellation.Token).GetAwaiter().GetResult();
    }

    private static void BootstrapMissingEmpty()
    {
        Fruit2048RunResult result = RunWithSeeds(new Fruit2048SeedAvailability
        { EmptyAvailable = false, Tier1Available = true });
        Assert(result.Outcome == Fruit2048Outcome.MissingSeedAssets);
    }

    private static void BootstrapMissingTier1()
    {
        Fruit2048RunResult result = RunWithSeeds(new Fruit2048SeedAvailability
        { EmptyAvailable = true, Tier1Available = false });
        Assert(result.Outcome == Fruit2048Outcome.MissingSeedAssets);
    }

    private static void ProgressionTier3Candidate()
    {
        var catalog = LearnedCatalog(2);
        var learner = new Fruit2048TransitionLearner(catalog);
        Fruit2048BoardReadResult first = UnknownObservation(Fingerprint(0x300));
        Fruit2048BoardReadResult second = UnknownObservation(Fingerprint(0x300));
        IReadOnlyList<Fruit2048LearningResult> result = learner.Learn("A", Board(2, 2),
            Fruit2048Move.Left, new[] { first, second }, "tier3-transition");
        Assert(result.Count == 1 && result[0].Tier == 3
            && result[0].State == FruitTileLearningState.Candidate);
    }

    private static void CandidateStrict()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.ObserveMerge(2, Fingerprint(0), "candidate", 1,
            Fruit2048Move.Left, 0, 0);
        Assert(!catalog.Recognize(Fingerprint(0x3F)).Tier.HasValue);
    }

    private static void TransitionTemporaryResolution()
    {
        Fruit2048BoardReadResult unknown1 = UnknownObservation(Fingerprint(0x22));
        Fruit2048BoardReadResult unknown2 = UnknownObservation(Fingerprint(0x22));
        Fruit2048BoardReadResult unknown3 = UnknownObservation(Fingerprint(0x22));
        var reader = new FakeReader(Board(1, 1));
        reader.ForcedResults.Enqueue(KnownRead(Board(1, 1)));
        reader.ForcedResults.Enqueue(unknown1);
        reader.ForcedResults.Enqueue(unknown2);
        reader.ForcedResults.Enqueue(unknown3);
        var swipe = new FakeSwipe(); var ownership = NewOwnership();
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        var service = new Fruit2048AutomationService(new FakeClient(), reader,
            new Fruit2048Solver(), swipe, ownership,
            new Fruit2048TransitionLearner(catalog), catalog, null);
        Fruit2048RunResult result;
        using (var cancellation = new CancellationTokenSource())
            result = service.RunAsync("A", new Fruit2048RunRequest { TargetTile = 2 },
                null, cancellation.Token).GetAwaiter().GetResult();
        Assert(result.Outcome == Fruit2048Outcome.TargetReached && swipe.Count == 1);
    }

    private static Fruit2048BoardReadResult KnownRead(Fruit2048Board board) =>
        new Fruit2048BoardReadResult
        {
            Success = true, ScreenStatus = Fruit2048ScreenStatus.Ready, Board = board,
            HighestTile = board.HighestTile, BoardRegion = new ImageRegion(0, 0, 400, 400),
            ScreenWidth = 1280, ScreenHeight = 720, Cells = Cells(board.Values
                .Select(value => (int?)value).ToArray()), UnknownCells = new Fruit2048Cell[0]
        };

    private static void ResetPreservesSeed()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.AddSeed(1, Fingerprint(1));
        for (int sample = 0; sample < 3; sample++) catalog.ObserveMerge(2,
            Fingerprint(2), "learn-" + sample, 1, Fruit2048Move.Left, 0, 0);
        catalog.ResetLearningData();
        Assert(catalog.Snapshot.Profiles.Any(profile => profile.Tier == 1
            && profile.Prototypes.Any(prototype => prototype.IsStaticSeed)));
        Assert(!catalog.Snapshot.Profiles.Any(profile => profile.Tier == 2));
    }

    private static void FastPathIncreases()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.ObserveMerge(2, Fingerprint(2), "one", 1, Fruit2048Move.Left, 0, 0);
        Assert(catalog.Recognize(Fingerprint(2)).Source == Fruit2048RecognitionSource.PrototypeMatch);
        catalog.ObserveMerge(2, Fingerprint(2), "two", 1, Fruit2048Move.Left, 0, 0);
        catalog.ObserveMerge(2, Fingerprint(2), "three", 1, Fruit2048Move.Left, 0, 0);
        Assert(catalog.Recognize(Fingerprint(2)).Source == Fruit2048RecognitionSource.FastFingerprint);
    }

    private sealed class RuntimeFixture
    {
        public FakeReader Reader; public FakeSwipe Swipe; public FakeClient Client; public DeviceAutomationOwnershipService Ownership; public CancellationTokenSource Cancellation = new CancellationTokenSource(); public Fruit2048AutomationService Service;
        public static RuntimeFixture Create(Fruit2048Board first, Fruit2048Board second, bool running)
        {
            var f = new RuntimeFixture { Reader = new FakeReader(first, second), Swipe = new FakeSwipe(), Client = new FakeClient { Running = running }, Ownership = NewOwnership() };
            var catalog = new FruitTileLearningCatalog(TempCatalogPath());
            f.Service = new Fruit2048AutomationService(f.Client, f.Reader, new Fruit2048Solver(),
                f.Swipe, f.Ownership, new Fruit2048TransitionLearner(catalog), catalog, null); return f;
        }
        public void Run() { Service.RunAsync("A", new Fruit2048RunRequest { TargetTile = 2 }, null, Cancellation.Token).GetAwaiter().GetResult(); }
    }

    private sealed class FakeReader : IFruit2048BoardReader
    {
        private readonly Queue<Fruit2048Board> boards; public bool Throw;
        public Fruit2048BoardReadResult ForcedResult;
        public Queue<Fruit2048BoardReadResult> ForcedResults = new Queue<Fruit2048BoardReadResult>();
        public Fruit2048SeedAvailability SeedAvailability { get; set; } =
            new Fruit2048SeedAvailability { EmptyAvailable = true, Tier1Available = true };
        public FakeReader(params Fruit2048Board[] boards) { this.boards = new Queue<Fruit2048Board>(boards); }
        public Task<Fruit2048BoardReadResult> ReadAsync(string d, CancellationToken c) { if (Throw) throw new InvalidOperationException("boom"); if (ForcedResults.Count > 0) return Task.FromResult(ForcedResults.Dequeue()); if (ForcedResult != null) return Task.FromResult(ForcedResult); var b = boards.Count > 1 ? boards.Dequeue() : boards.Peek(); return Task.FromResult(KnownRead(b)); }
        public Task<bool> TryTapRefreshAsync(string d, CancellationToken c) => Task.FromResult(false);
    }
    private sealed class FakeSwipe : IFruit2048SwipeExecutor { public int Count; public Task ExecuteAsync(string d, Fruit2048Move m, ImageRegion r, int w, int h, CancellationToken c) { c.ThrowIfCancellationRequested(); Count++; return Task.CompletedTask; } }
    private sealed class FakeClient : ILdPlayerClient
    {
        public bool Running = true;
        public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken c) => Task.FromResult((IReadOnlyList<string>)new[] { "A" });
        public Task<bool> IsRunningAsync(string d, CancellationToken c) => Task.FromResult(Running);
        public Task OpenAsync(string d, CancellationToken c) => Task.CompletedTask; public Task CloseAsync(string d, CancellationToken c) => Task.CompletedTask; public Task RunAppAsync(string d, string p, CancellationToken c) => Task.CompletedTask;
        public Task<byte[]> CaptureScreenshotPngAsync(string d, CancellationToken c) => Task.FromResult(new byte[0]); public Task TapAsync(string d, int x, int y, CancellationToken c) => Task.CompletedTask; public Task TapByPercentAsync(string d, double x, double y, CancellationToken c) => Task.CompletedTask; public Task LongPressAsync(string d, int x, int y, int ms, CancellationToken c) => Task.CompletedTask;
        public Task SwipeByPercentAsync(string d, double sx, double sy, double ex, double ey, int ms, CancellationToken c) => Task.CompletedTask; public Task BackAsync(string d, CancellationToken c) => Task.CompletedTask; public Task InputTextAsync(string d, string t, CancellationToken c) => Task.CompletedTask; public Task PressKeyAsync(string d, AndroidKeyCode k, CancellationToken c) => Task.CompletedTask;
    }
}
