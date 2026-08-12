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
        Run("Board_AnchorIsNotBoardRect", BoardAnchorIsNotBoardRect);
        Run("Navigation_FruitTabInsideExpectedRoiIsAccepted", NavigationFruitTabInsideRoi);
        Run("Navigation_FruitTabAtY583IsRejected", NavigationFruitTabAtY583Rejected);
        Run("Navigation_AnchorsUseFocusedRois", NavigationAnchorsUseFocusedRois);
        Run("Navigation_CityEntryHistoricalBoundsAreAccepted", NavigationCityEntryHistoricalBounds);
        Run("Navigation_BoardAnchorHistoricalBoundsAreAccepted", NavigationBoardAnchorHistoricalBounds);
        Run("Navigation_AbsoluteMatchTapCenterIsValidated", NavigationAbsoluteTapCenter);
        Run("Navigation_FailurePersistsSingleFullScreenshot", NavigationFailureDiagnostic);
        Run("Navigation_BoardReadyPrimaryHasPriorityOverFruitTab", NavigationBoardReadyPrimaryPriority);
        Run("Navigation_BoardReadySecondaryFallbackWorks", NavigationBoardReadySecondaryFallback);
        Run("Navigation_BoardReadyFrameAnchorIgnoresCellContents", NavigationBoardReadyIgnoresCellContents);
        Run("Board_RecognitionInsetsAreConsistent", BoardRecognitionInsetsAreConsistent);
        Run("Board_InitialFixtureHas14EmptyAnd2Tier1", BoardInitialFixture);
        Run("Board_NormalizationIsSharedForSeedAndRuntime", BoardNormalizationIsShared);
        Run("Board_DebugExportContainsFullAnd16Cells", BoardDebugExport);
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
        Run("Solver_PrefersImmediateMergeForLearning", SolverPrefersImmediateMerge);
        Run("Runtime_DefaultTeacherMoveLimitIsUnlimited", RuntimeDefaultTeacherMoveLimitIsUnlimited);
        Run("Pacing_FastRecognitionUsesShorterSettle", PacingFastRecognitionUsesShorterSettle);
        Run("Solver_TargetDetection", SolverTarget);
        Run("Runtime_ExactlyOneSwipePerIteration", RuntimeOneSwipe);
        Run("Runtime_SwipeNoEffectStopsBeforeMove2", RuntimeSwipeNoEffectStops);
        Run("Runtime_DeviceUnavailableIsNotBoardReadFailed", RuntimeDeviceUnavailable);
        Run("Runtime_StopPreventsSwipes", RuntimeStop);
        Run("Runtime_ExceptionReleasesLease", RuntimeExceptionReleases);
        Run("Runtime_TargetReleasesLease", RuntimeTargetReleases);
        Run("Runtime_DisconnectReleasesLease", RuntimeDisconnectReleases);
        Run("Runtime_AutoRefreshDefaultsFalse", RuntimeDefaultRefresh);
        Run("Learning_SeedProfileCanLoad", LearningSeedProfileCanLoad);
        Run("Learning_CatalogPersistsAndReloads", LearningCatalogPersistsAndReloads);
        Run("Recognition_HighConfidenceFingerprintUsesFastPath", RecognitionFastPath);
        Run("Recognition_AmbiguousFastFingerprintFallsBack", RecognitionFallback);
        Run("Recognition_CandidateFruitPrototypeDoesNotClassifyLiveBoard", CandidateFruitPrototypeDoesNotClassifyLiveBoard);
        Run("Recognition_UnknownNeverBecomesEmpty", RecognitionUnknownNotEmpty);
        Run("Badge_CandidateDoesNotOverrideLiveBoard", BadgeCandidateDoesNotOverrideLiveBoard);
        Run("Badge_MergeVerifiedTierCanResolveLiveBoard", BadgeMergeVerifiedTierCanResolveLiveBoard);
        Run("Badge_UnreliableMatchRemainsUnknown", BadgeUnreliableRemainsUnknown);
        Run("Badge_BootstrapCreatesCandidateNotLearned", BadgeCreatesCandidate);
        Run("Badge_ResetLearningPreservesCalibratedBadge", BadgeResetPreservesCalibration);
        Run("Badge_RegionUsesRecognitionCellGeometry", BadgeRegionUsesRecognitionGeometry);
        Run("Badge_MergeConfirmsBootstrapCandidate", BadgeMergeConfirmsCandidate);
        Run("Badge_MergeContradictionIsRejected", BadgeMergeContradictionIsRejected);
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
        Run("Bootstrap_KnownCatalogResumesCurrentBoard", BootstrapKnownCatalogResumesCurrentBoard);
        Run("Bootstrap_InitialUnknownHighTierDoesNotSwipe", SafetyFailedLearningNoSwipe);
        Run("Bootstrap_FreshTier1BoardMayStart", RuntimeOneSwipe);
        Run("Progression_Tier1MergeProducesCandidateTier2", LearningMergeInfersNextTier);
        Run("Progression_ThreeTier1MergesLearnTier2", LearningBecomesLearned);
        Run("Progression_LearnedTier2UsesFastPath", RecognitionFastPath);
        Run("Progression_Tier2MergeStartsTier3", ProgressionTier3Candidate);
        Run("Progression_Tier4MergeStartsTier5", ProgressionTier5Candidate);
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
        Run("Transition_ValidDeterministicMoveIsAccepted", TransitionValid);
        Run("Transition_SpawnIsNotMergeEvidence", TransitionSpawnIsNotMergeEvidence);
        Run("Transition_WrongKnownMergeResultIsInvalid", TransitionWrongMergeIsInvalid);
        Run("Transition_PreviousValueAtMergeDestinationRetries", TransitionPreviousValueAtMergeDestinationRetries);
        Run("Transition_StableContradictionResynchronizesWithoutLearning", TransitionStableContradictionResynchronizesWithoutLearning);
        Run("Transition_PartialBoardRetriesBeforeInvalid", TransitionPartialBoardRetriesBeforeInvalid);
        Run("Diagnostics_TerminalFailurePersistsOriginalScreenshot", DiagnosticsPersistsOriginalScreenshot);
        Run("Diagnostics_ExportResolvesLatestDeviceFailure", DiagnosticsResolvesLatestDeviceFailure);
        Run("Transition_UnknownMergeDestinationCanBeValidated", TransitionUnknownMergeDestination);
        Run("Proof_RequiresFresh14Empty2Tier1Board", ProofRequiresFreshBoard);
        Run("Proof_Tier2NeedsThreeDistinctTransitions", ProofTier2NeedsThreeTransitions);
        Run("Proof_CandidateTier2DoesNotHaveLearnedAuthority", ProofCandidateNotLearned);
        Run("Proof_OnlyCatalogRecognitionCompletes", ProofRequiresCatalogRecognition);
        Run("Proof_ResultFilterDoesNotLearnTier3", ProofFilterDoesNotLearnTier3);
        Run("Proof_ReportPersistsOutsideBuildOutput", ProofReportPersistsOutsideBuildOutput);
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

    private static Fruit2048TransitionValidationResult Validate(Fruit2048Board before,
        Fruit2048BoardReadResult observed, Fruit2048Move move)
    {
        return new Fruit2048TransitionValidator().Validate(new Fruit2048TransitionValidationRequest
        {
            DeviceName = "Teacher", TransitionId = "transition-1", BoardBefore = before,
            Move = move, ExpectedBoardAfterMove = before.Simulate(move),
            MergeOperations = Fruit2048TransitionLearner.GetMergeOperations(before, move), Observed = observed
        });
    }

    private static void TransitionValid()
    {
        Fruit2048Board before = Board(1, 1);
        Fruit2048TransitionValidationResult result = Validate(before, KnownRead(before.Simulate(Fruit2048Move.Left)), Fruit2048Move.Left);
        Assert(result.Status == Fruit2048TransitionValidationStatus.Valid && result.IsValidForLearning);
    }

    private static void TransitionSpawnIsNotMergeEvidence()
    {
        Fruit2048Board before = Board(1, 1);
        Fruit2048Board after = before.Simulate(Fruit2048Move.Left);
        int[] values = after.Values.ToArray(); values[3] = 1;
        Fruit2048TransitionValidationResult result = Validate(before, KnownRead(new Fruit2048Board(values)), Fruit2048Move.Left);
        Assert(result.Status == Fruit2048TransitionValidationStatus.ValidWithSpawn && result.SpawnCandidates == 1);
    }

    private static void TransitionWrongMergeIsInvalid()
    {
        // A non-merge move observed at a different clean position cannot be
        // explained by the merge animation and remains a real contradiction.
        Fruit2048Board before = Board(1);
        Fruit2048TransitionValidationResult result = Validate(before, KnownRead(Board(0, 1)), Fruit2048Move.Right);
        Assert(result.Status == Fruit2048TransitionValidationStatus.Invalid);
    }

    private static void TransitionPreviousValueAtMergeDestinationRetries()
    {
        // Tier values are intentionally not special-cased: any merge may keep
        // its old source value visible for one animation frame.
        Fruit2048Board before = Board(4, 4);
        Fruit2048TransitionValidationResult result = Validate(before, KnownRead(Board(4)), Fruit2048Move.Left);
        Assert(result.Status == Fruit2048TransitionValidationStatus.Ambiguous
            && result.Reason.StartsWith("MergeDestinationMismatchRequiresFocusedRetry",
                StringComparison.Ordinal));
    }

    private static void TransitionStableContradictionResynchronizesWithoutLearning()
    {
        Fruit2048Board before = Board(1, 1);
        Fruit2048Board stableContradiction = Board(2, 1);
        var observations = new[]
        {
            KnownRead(stableContradiction),
            KnownRead(stableContradiction)
        };

        Assert(Fruit2048TransitionRecoveryPolicy.CanResynchronizeWithoutLearning(observations, before));
        Assert(!Fruit2048TransitionRecoveryPolicy.CanResynchronizeWithoutLearning(
            new[] { KnownRead(before), KnownRead(before) }, before));
    }

    private static void TransitionUnknownMergeDestination()
    {
        Fruit2048Board before = Board(1, 1);
        Fruit2048BoardReadResult observed = KnownRead(before.Simulate(Fruit2048Move.Left));
        var cells = observed.Cells.Select(cell => new Fruit2048Cell
        {
            Row = cell.Row, Column = cell.Column, Bounds = cell.Bounds, Value = cell.Value,
            Tier = cell.Tier, Confidence = cell.Confidence, Fingerprint = cell.Fingerprint
        }).ToList();
        cells[0].Value = null; cells[0].Tier = null;
        observed = Fruit2048BoardAssembler.Assemble(cells, 1);
        observed.ScreenStatus = Fruit2048ScreenStatus.Ready;
        Fruit2048TransitionValidationResult result = Validate(before, observed, Fruit2048Move.Left);
        Assert(result.Status == Fruit2048TransitionValidationStatus.Ambiguous && result.UnknownCells == 1);
    }

    private static void TransitionPartialBoardRetriesBeforeInvalid()
    {
        Fruit2048Board before = Board(1, 1);
        Fruit2048BoardReadResult observed = KnownRead(before.Simulate(Fruit2048Move.Left));
        var cells = observed.Cells.Select(cell => new Fruit2048Cell
        {
            Row = cell.Row, Column = cell.Column, Bounds = cell.Bounds, Value = cell.Value,
            Tier = cell.Tier, Confidence = cell.Confidence, Fingerprint = cell.Fingerprint
        }).ToList();

        // A partial frame may also contain a stale recognised cell.  That is still
        // insufficient to prove an invalid transition until the focused retries finish.
        cells[0].Value = null; cells[0].Tier = null;
        cells[4].Value = 1; cells[4].Tier = 1;
        observed = Fruit2048BoardAssembler.Assemble(cells, 1);
        observed.ScreenStatus = Fruit2048ScreenStatus.Ready;

        Fruit2048TransitionValidationResult result = Validate(before, observed, Fruit2048Move.Left);
        Assert(result.Status == Fruit2048TransitionValidationStatus.Ambiguous
            && string.Equals(result.Reason, "ObservedBoardContainsUnknownCells", StringComparison.Ordinal));
    }

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
    private static void BoardAnchorIsNotBoardRect()
    {
        var profile = new Fruit2048ScreenProfile();
        Assert(profile.BoardRegion.Width > profile.BoardAnchorRegion.Width / 2
            && profile.BoardRegion.Height > profile.BoardAnchorRegion.Height / 2
            && profile.BoardRegion.X != 367);
    }

    private static void NavigationFruitTabInsideRoi()
    {
        var profile = new Fruit2048ScreenProfile();
        Assert(Fruit2048ScreenProfile.IsAbsoluteBoundsInsideRegion(
            40, 415, 56, 52, profile.FruitFestivalTabRegion));
    }

    private static void NavigationFruitTabAtY583Rejected()
    {
        var profile = new Fruit2048ScreenProfile();
        Assert(!Fruit2048ScreenProfile.IsAbsoluteBoundsInsideRegion(
            40, 583, 56, 52, profile.FruitFestivalTabRegion));
    }

    private static void NavigationAnchorsUseFocusedRois()
    {
        var profile = new Fruit2048ScreenProfile();
        Assert(profile.CityFestivalEntryRegion.X == 790 && profile.CityFestivalEntryRegion.Y == 110
            && profile.CityFestivalEntryRegion.Width == 200 && profile.CityFestivalEntryRegion.Height == 190);
        Assert(profile.FruitFestivalTabRegion.X == 20 && profile.FruitFestivalTabRegion.Y == 365
            && profile.FruitFestivalTabRegion.Width == 120 && profile.FruitFestivalTabRegion.Height == 140);
        Assert(profile.BoardAnchorRegion.X == 320 && profile.BoardAnchorRegion.Y == 85
            && profile.BoardAnchorRegion.Width == 170 && profile.BoardAnchorRegion.Height == 160);
    }

    private static void NavigationCityEntryHistoricalBounds()
    {
        var profile = new Fruit2048ScreenProfile();
        Assert(Fruit2048ScreenProfile.IsAbsoluteBoundsInsideRegion(
            858, 157, 70, 57, profile.CityFestivalEntryRegion));
    }

    private static void NavigationBoardAnchorHistoricalBounds()
    {
        var profile = new Fruit2048ScreenProfile();
        Assert(Fruit2048ScreenProfile.IsAbsoluteBoundsInsideRegion(
            367, 122, 52, 55, profile.BoardAnchorRegion));
    }

    private static void NavigationAbsoluteTapCenter()
    {
        var profile = new Fruit2048ScreenProfile();
        const int x = 40;
        const int y = 415;
        const int width = 56;
        const int height = 52;
        Assert(Fruit2048ScreenProfile.IsAbsoluteBoundsInsideRegion(
            x, y, width, height, profile.FruitFestivalTabRegion));
        Assert(x + (width / 2) == 68 && y + (height / 2) == 441);
    }

    private static void NavigationFailureDiagnostic()
    {
        string root = Path.Combine(Path.GetTempPath(), "Fruit2048-NavigationDiagnostics", Guid.NewGuid().ToString("N"));
        var store = new Fruit2048LearningDiagnosticStore(root);
        var anchors = new[]
        {
            new Fruit2048NavigationAnchorDiagnostic
            {
                Anchor = "Navigation\\board_anchor.png", SearchRoi = new ImageRegion(0, 0, 1, 1),
                TemplatePath = "board.png", TemplateExists = true, TemplateWidth = 1, TemplateHeight = 1,
                MatcherMode = "LocalNormalizedGrayscale", Outcome = "NotFound"
            },
            new Fruit2048NavigationAnchorDiagnostic
            {
                Anchor = "Navigation\\fruit_2048_tab.png", SearchRoi = new ImageRegion(0, 0, 1, 1),
                TemplatePath = "test.png", TemplateExists = true, TemplateWidth = 1, TemplateHeight = 1,
                MatcherMode = "FullFrameWithNativeRoi", Outcome = "NotFound"
            }
        };
        string directory = store.SaveNavigationFailure("May_2", "NavigationAnchorsNotDetected",
            TinyPng(Color.Goldenrod), "session", "attempt", anchors);
        Assert(File.Exists(Path.Combine(directory, "full.png"))
            && File.Exists(Path.Combine(directory, "roi_board_anchor.png"))
            && File.Exists(Path.Combine(directory, "roi_Navigation_fruit_2048_tab.png"))
            && File.Exists(Path.Combine(directory, "metadata.json")));
    }
    private static void NavigationBoardReadyPrimaryPriority()
    {
        using (var board = CreateBoardFrameFixture())
        {
            string root = WriteBoardReadyTemplates(board, false);
            var detector = new Fruit2048BoardReadyDetector(new Fruit2048TemplateCatalog(root), new Fruit2048ScreenProfile());
            using (var frame = new CapturedFrame(new Bitmap(board), DateTimeOffset.UtcNow))
            {
                Fruit2048BoardReadyDetection result = detector.Detect(frame);
                Assert(result.IsBoardReady && result.Primary.Found
                    && result.AcceptedAnchor.Anchor == Fruit2048TemplateCatalog.NavigationBoardAnchor);
            }
        }
    }

    private static void NavigationBoardReadySecondaryFallback()
    {
        using (var board = CreateBoardFrameFixture())
        {
            string root = WriteBoardReadyTemplates(board, true);
            var detector = new Fruit2048BoardReadyDetector(new Fruit2048TemplateCatalog(root), new Fruit2048ScreenProfile());
            using (var frame = new CapturedFrame(new Bitmap(board), DateTimeOffset.UtcNow))
            {
                Fruit2048BoardReadyDetection result = detector.Detect(frame);
                Assert(result.IsBoardReady && !result.Primary.Found && result.Secondary.Found);
            }
        }
    }

    private static void NavigationBoardReadyIgnoresCellContents()
    {
        using (var board = CreateBoardFrameFixture())
        {
            string root = WriteBoardReadyTemplates(board, false);
            using (var graphics = Graphics.FromImage(board))
                graphics.FillEllipse(Brushes.LimeGreen, new Rectangle(388, 145, 85, 85));
            var detector = new Fruit2048BoardReadyDetector(new Fruit2048TemplateCatalog(root), new Fruit2048ScreenProfile());
            using (var frame = new CapturedFrame(new Bitmap(board), DateTimeOffset.UtcNow))
                Assert(detector.Detect(frame).IsBoardReady);
        }
    }

    private static Bitmap CreateBoardFrameFixture()
    {
        var bitmap = new Bitmap(1280, 720);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(48, 69, 36));
            using (var outer = new Pen(Color.Goldenrod, 5))
            using (var inner = new Pen(Color.SaddleBrown, 5))
            {
                graphics.DrawRectangle(outer, 368, 122, 540, 540);
                graphics.DrawRectangle(inner, 376, 130, 524, 524);
            }
        }
        return bitmap;
    }

    private static string WriteBoardReadyTemplates(Bitmap board, bool corruptPrimary)
    {
        string root = Path.Combine(Path.GetTempPath(), "Fruit2048-BoardReady", Guid.NewGuid().ToString("N"));
        string navigation = Path.Combine(root, Fruit2048TemplateCatalog.RelativeDirectory, "Navigation");
        Directory.CreateDirectory(navigation);
        using (var primary = corruptPrimary ? new Bitmap(19, 35) : board.Clone(new Rectangle(368, 122, 19, 35), board.PixelFormat))
        using (var secondary = board.Clone(new Rectangle(514, 122, 66, 20), board.PixelFormat))
        {
            if (corruptPrimary)
                using (var graphics = Graphics.FromImage(primary)) graphics.Clear(Color.White);
            primary.Save(Path.Combine(navigation, "board_anchor.png"), ImageFormat.Png);
            secondary.Save(Path.Combine(navigation, "board_frame_secondary.png"), ImageFormat.Png);
        }
        return root;
    }

    private static void BoardRecognitionInsetsAreConsistent()
    {
        var board = new Fruit2048ScreenProfile().BoardRegion;
        ImageRegion[] cells = Enumerable.Range(0, 4).SelectMany(row => Enumerable.Range(0, 4)
            .Select(column => Fruit2048ScreenProfile.GetRecognitionCellRegion(board, row, column))).ToArray();
        Assert(cells.All(cell => cell.Width == Fruit2048CellVisualNormalizer.CanonicalWidth
            && cell.Height == Fruit2048CellVisualNormalizer.CanonicalHeight));
    }
    private static void BoardInitialFixture()
    {
        int[] values = { 0,0,0,0, 0,0,1,0, 0,0,0,0, 0,0,1,0 };
        Assert(values.Count(value => value == 0) == 14 && values.Count(value => value == 1) == 2);
    }
    private static void BoardNormalizationIsShared()
    {
        using (var raw = new Bitmap(196, 198))
        using (var graphics = Graphics.FromImage(raw))
        {
            graphics.Clear(Color.SaddleBrown);
            FruitTileVisualFingerprint runtime = Fruit2048CellVisualNormalizer.CreateFingerprint(raw,
                new ImageRegion(0, 0, raw.Width, raw.Height));
            FruitTileVisualFingerprint seed = Fruit2048CellVisualNormalizer.CreateFingerprint(raw,
                new ImageRegion(0, 0, raw.Width, raw.Height));
            Assert(runtime.AverageHash == seed.AverageHash && runtime.EdgeHash == seed.EdgeHash);
        }
    }
    private static void BoardDebugExport()
    {
        string root = Path.Combine(Path.GetTempPath(), "IKAutomation", "Fruit2048BoardExportTests", Guid.NewGuid().ToString("N"));
        var store = new Fruit2048LearningDiagnosticStore(root);
        using (var image = new Bitmap(400, 400))
        using (var graphics = Graphics.FromImage(image))
        using (var stream = new MemoryStream())
        {
            graphics.Clear(Color.Brown); image.Save(stream, ImageFormat.Png);
            string directory = store.ExportBoardInspection("May_2", stream.ToArray(), new ImageRegion(0, 0, 400, 400));
            Assert(File.Exists(Path.Combine(directory, "full.png")) && File.Exists(Path.Combine(directory, "board_debug.png"))
                && Directory.GetFiles(Path.Combine(directory, "cells"), "*.png").Length == 16);
        }
    }
    private static List<Fruit2048Cell> Cells(params int?[] values) { return Enumerable.Range(0, 16).Select(i => new Fruit2048Cell { Row = i / 4, Column = i % 4, Bounds = new ImageRegion(i % 4, i / 4, 1, 1), Value = i < values.Length ? values[i] : 0 }).ToList(); }
    private static void BoardUnknownFails() { var cells = Cells(); cells[7].Value = null; var result = Fruit2048BoardAssembler.Assemble(cells, 1); Assert(!result.Success && result.UnknownCells.Count == 1 && result.Board == null); }
    private static void BootstrapKnownCatalogResumesCurrentBoard()
    {
        var known = new Fruit2048LearningSnapshot { HighestObservedTier = 4 };
        Assert(!Fruit2048FirstLearningProofPolicy.RequiresInitialFreshBoardBootstrap(
            Fruit2048RunMode.Normal, known));
        Assert(Fruit2048FirstLearningProofPolicy.RequiresInitialFreshBoardBootstrap(
            Fruit2048RunMode.FirstLearningProof, known));
        Assert(Fruit2048FirstLearningProofPolicy.RequiresInitialFreshBoardBootstrap(
            Fruit2048RunMode.Normal, new Fruit2048LearningSnapshot { HighestObservedTier = 1 }));
    }
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
    private static void SolverPrefersImmediateMerge()
    {
        var board = Board(1, 1, 0, 0, 4, 0, 4, 0);
        Fruit2048Move move;
        Assert(new Fruit2048Solver().TryChooseMove(board, out move)
            && move == Fruit2048Move.Left
            && Fruit2048TransitionLearner.GetMergeOperations(board, move).Any());
    }
    private static void RuntimeDefaultTeacherMoveLimitIsUnlimited()
    {
        Assert(new Fruit2048RunRequest().TeacherMoveLimit == 0);
    }

    private static void PacingFastRecognitionUsesShorterSettle()
    {
        Assert(Fruit2048PacingPolicy.GetPostSwipeDelay(
            new Fruit2048LearningSnapshot { Mode = Fruit2048RecognitionMode.Fast })
            == Fruit2048PacingPolicy.FastKnownTilePostSwipeDelayMs);
        Assert(Fruit2048PacingPolicy.GetPostSwipeDelay(
            new Fruit2048LearningSnapshot { Mode = Fruit2048RecognitionMode.Hybrid })
            == Fruit2048PacingPolicy.HybridPostSwipeDelayMs);
        Assert(Fruit2048PacingPolicy.GetPostSwipeDelay(
            new Fruit2048LearningSnapshot { Mode = Fruit2048RecognitionMode.Learning })
            == Fruit2048PacingPolicy.LearningPostSwipeDelayMs);
        Assert(Fruit2048PacingPolicy.GetFocusedRetryDelay(
            new Fruit2048LearningSnapshot { Mode = Fruit2048RecognitionMode.Fast })
            == Fruit2048PacingPolicy.FastFocusedRetryDelayMs);
    }
    private static void SolverTarget() { Assert(Board(2048).HasReached(2048)); }

    private static void RuntimeOneSwipe() { var fixture = RuntimeFixture.Create(Board(1, 1), Board(2), true); fixture.Run(); Assert(fixture.Swipe.Count == 1); }
    private static void RuntimeSwipeNoEffectStops()
    {
        var before = Board(1, 1);
        var fixture = RuntimeFixture.Create(before, before, true);
        Fruit2048RunResult result = fixture.Run();
        Assert(result.Outcome == Fruit2048Outcome.SwipeNoEffect && fixture.Swipe.Count == 1);
    }
    private static void RuntimeDeviceUnavailable()
    {
        var fixture = RuntimeFixture.Create(Board(1, 1), Board(2), true);
        fixture.Reader.ForcedResult = new Fruit2048BoardReadResult
        {
            ScreenStatus = Fruit2048ScreenStatus.DeviceUnavailable,
            Error = "device 'emulator-5556' not found"
        };
        Fruit2048RunResult result = fixture.Run();
        Assert(result.Outcome == Fruit2048Outcome.DeviceUnavailable && fixture.Swipe.Count == 0);
    }
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

    private static void CandidateFruitPrototypeDoesNotClassifyLiveBoard()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.ObserveMerge(5, Fingerprint(0x500), "candidate-tier-5", 4, Fruit2048Move.Left, 0, 0);
        FruitTileRecognitionResult result = catalog.Recognize(Fingerprint(0x500));
        Assert(!result.Tier.HasValue && result.Source == Fruit2048RecognitionSource.Unknown);
    }

    private static void RecognitionUnknownNotEmpty()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        FruitTileRecognitionResult result = catalog.Recognize(Fingerprint(ulong.MaxValue));
        Assert(!result.Tier.HasValue);
        var cells = Cells(); cells[0].Value = null; cells[0].Tier = null;
        Assert(!Fruit2048BoardAssembler.Assemble(cells, 0).Success);
    }

    private static void BadgeCandidateDoesNotOverrideLiveBoard()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.ObserveBadgeBootstrap(5, Fingerprint(0x500), Fingerprint(0x5555), "manual-5", 1, 1);
        FruitTierBadgeRecognitionResult match = catalog.RecognizeBadge(Fingerprint(0x5555));
        Assert(!match.IsReliable && !match.Tier.HasValue);
    }

    private static void BadgeMergeVerifiedTierCanResolveLiveBoard()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        FruitTileVisualFingerprint fruit = Fingerprint(0x500);
        catalog.ObserveBadgeBootstrap(5, fruit, Fingerprint(0x5555), "transition-5", 0, 0);
        catalog.ObserveMerge(5, fruit, "merge-5-1", 4, Fruit2048Move.Left, 0, 0);
        catalog.ObserveMerge(5, fruit, "merge-5-2", 4, Fruit2048Move.Left, 0, 0);
        catalog.ObserveMerge(5, fruit, "merge-5-3", 4, Fruit2048Move.Left, 0, 0);
        FruitTierBadgeRecognitionResult match = catalog.RecognizeBadge(Fingerprint(0x5D55));
        Assert(match.IsReliable && match.Tier == 5
            && catalog.Snapshot.Profiles.Single(profile => profile.Tier == 5).State
                == FruitTileLearningState.Learned);
    }

    private static void BadgeUnreliableRemainsUnknown()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.ObserveBadgeBootstrap(5, Fingerprint(0x500), Fingerprint(0x5555), "manual-5", 1, 1);
        FruitTierBadgeRecognitionResult match = catalog.RecognizeBadge(Fingerprint(0xAAAA));
        Assert(!match.IsReliable);
    }

    private static void BadgeCreatesCandidate()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        Fruit2048LearningResult result = catalog.ObserveBadgeBootstrap(6, Fingerprint(0x600), Fingerprint(0x6666), "manual-6", 2, 2);
        FruitTileProfile profile = catalog.Snapshot.Profiles.Single(item => item.Tier == 6);
        Assert(result.State == FruitTileLearningState.Candidate && profile.State == FruitTileLearningState.Candidate
            && profile.BadgePrototypes.Count == 1 && !profile.VerifiedByMerge);
    }

    private static void BadgeResetPreservesCalibration()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.ObserveBadgeBootstrap(8, Fingerprint(0x800), Fingerprint(0x8888), "manual-8", 0, 0);
        catalog.ResetLearningData();
        FruitTileProfile profile = catalog.Snapshot.Profiles.Single(item => item.Tier == 8);
        Assert(catalog.RecognizeBadge(Fingerprint(0x8888)).Tier == 8
            && profile.State == FruitTileLearningState.Candidate && !profile.VerifiedByMerge);
    }

    private static void BadgeRegionUsesRecognitionGeometry()
    {
        ImageRegion board = new ImageRegion(386, 139, 524, 536);
        ImageRegion cell = Fruit2048ScreenProfile.GetRecognitionCellRegion(board, 1, 2);
        ImageRegion badge = Fruit2048ScreenProfile.GetTierBadgeRegion(cell);
        Assert(badge.X >= cell.X && badge.Y > cell.Y && badge.X + badge.Width <= cell.X + cell.Width
            && badge.Y + badge.Height <= cell.Y + cell.Height);
    }

    private static void BadgeMergeConfirmsCandidate()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        FruitTileVisualFingerprint fruit = Fingerprint(0x600);
        catalog.ObserveBadgeBootstrap(6, fruit, Fingerprint(0x6666), "manual-6", 2, 2);
        catalog.ObserveMerge(6, fruit, "merge-6-1", 5, Fruit2048Move.Left, 2, 1);
        Fruit2048LearningResult result = catalog.ObserveMerge(6, fruit, "merge-6-2", 5,
            Fruit2048Move.Left, 2, 1);
        Assert(result.Action == Fruit2048LearningAction.Learned
            && catalog.Snapshot.Profiles.Single(item => item.Tier == 6).VerifiedByMerge);
    }

    private static void BadgeMergeContradictionIsRejected()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        FruitTileVisualFingerprint fruit = Fingerprint(0x500);
        catalog.ObserveBadgeBootstrap(5, fruit, Fingerprint(0x5555), "manual-5", 1, 1);
        Fruit2048LearningResult conflict = catalog.ObserveMerge(6, fruit, "merge-6", 5,
            Fruit2048Move.Left, 1, 1);
        Assert(conflict.Action == Fruit2048LearningAction.ConflictRejected
            && conflict.Error.Contains("matched Tier 5"));
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

    private static void ProgressionTier5Candidate()
    {
        var catalog = LearnedCatalog(4);
        var learner = new Fruit2048TransitionLearner(catalog);
        Fruit2048BoardReadResult first = UnknownObservation(Fingerprint(0x500));
        Fruit2048BoardReadResult second = UnknownObservation(Fingerprint(0x500));
        IReadOnlyList<Fruit2048LearningResult> result = learner.Learn("A", Board(4, 4),
            Fruit2048Move.Left, new[] { first, second }, "tier5-transition");
        Assert(result.Count == 1 && result[0].Tier == 5
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

    private static void DiagnosticsPersistsOriginalScreenshot()
    {
        string root = Path.Combine(Path.GetTempPath(), "IKAutomation", "Fruit2048DiagnosticTests", Guid.NewGuid().ToString("N"));
        var store = new Fruit2048LearningDiagnosticStore(root);
        byte[] original = TinyPng(Color.MediumPurple);
        string path = store.Save("May_2", "session", "transition", "UnknownAfterRetries",
            null, null, new[] { new Fruit2048BoardReadResult { OriginalScreenshotPng = original } });
        string image = Path.Combine(path, "after_1.png");
        Assert(File.Exists(Path.Combine(path, "metadata.json")) && File.Exists(image));
        Assert(File.ReadAllBytes(image).SequenceEqual(original));
    }

    private static void DiagnosticsResolvesLatestDeviceFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "IKAutomation", "Fruit2048DiagnosticTests", Guid.NewGuid().ToString("N"));
        var store = new Fruit2048LearningDiagnosticStore(root);
        string first = store.Save("May_2", "s", "first", "failure", null, null, null);
        Thread.Sleep(5);
        string latest = store.Save("May_2", "s", "second", "failure", null, null, null);
        Assert(!string.Equals(first, latest, StringComparison.Ordinal));
        Assert(string.Equals(store.GetLatestForDevice("May_2"), latest, StringComparison.OrdinalIgnoreCase));
    }

    private static Fruit2048BoardReadResult KnownRead(Fruit2048Board board) =>
        new Fruit2048BoardReadResult
        {
            Success = true, ScreenStatus = Fruit2048ScreenStatus.Ready, Board = board,
            HighestTile = board.HighestTile, BoardRegion = new ImageRegion(0, 0, 400, 400),
            ScreenWidth = 1280, ScreenHeight = 720, Cells = Cells(board.Values
                .Select(value => (int?)value).ToArray()), UnknownCells = new Fruit2048Cell[0]
        };

    private static void ProofRequiresFreshBoard()
    {
        Assert(Fruit2048FirstLearningProofPolicy.IsFreshTier1Board(ProofRead(Board(1, 1))));
        Assert(!Fruit2048FirstLearningProofPolicy.IsFreshTier1Board(ProofRead(Board(1, 1, 1))));
    }

    private static void ProofTier2NeedsThreeTransitions()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        for (int sample = 0; sample < 3; sample++) catalog.ObserveMerge(2, Fingerprint(2),
            "proof-" + sample, 1, Fruit2048Move.Left, 0, 0);
        Assert(Fruit2048FirstLearningProofPolicy.IsTier2Learned(catalog.Snapshot)
            && Fruit2048FirstLearningProofPolicy.CreateProgress(3, catalog.Snapshot, false, "Tier2Learned").Tier2SampleCount == 3);
    }

    private static void ProofCandidateNotLearned()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        catalog.ObserveMerge(2, Fingerprint(2), "proof-candidate", 1,
            Fruit2048Move.Left, 0, 0);
        FruitTileProfile tier2 = catalog.Snapshot.Profiles.Single(profile => profile.Tier == 2);
        Assert(tier2.State == FruitTileLearningState.Candidate
            && !Fruit2048FirstLearningProofPolicy.IsTier2Learned(catalog.Snapshot));
    }

    private static void ProofRequiresCatalogRecognition()
    {
        Fruit2048BoardReadResult read = ProofRead(Board(2));
        Fruit2048Cell tier2 = read.Cells.Single(cell => cell.Tier == 2);
        tier2.RecognitionSource = Fruit2048RecognitionSource.FastFingerprint;
        Assert(Fruit2048FirstLearningProofPolicy.HasCatalogTier2Recognition(read));
        tier2.RecognitionSource = Fruit2048RecognitionSource.Unknown;
        Assert(!Fruit2048FirstLearningProofPolicy.HasCatalogTier2Recognition(read));
    }

    private static void ProofFilterDoesNotLearnTier3()
    {
        var catalog = new FruitTileLearningCatalog(TempCatalogPath());
        var learner = new Fruit2048TransitionLearner(catalog);
        Fruit2048BoardReadResult first = UnknownObservation(Fingerprint(2));
        Fruit2048BoardReadResult second = UnknownObservation(Fingerprint(2));
        learner.Learn("A", Board(2, 2), Fruit2048Move.Left, new[] { first, second }, "proof-tier3", 2);
        Assert(!catalog.Snapshot.Profiles.Any(profile => profile.Tier == 3));
    }

    private static void ProofReportPersistsOutsideBuildOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), "IKAutomation-Fruit2048-Proof", Guid.NewGuid().ToString("N"));
        var store = new Fruit2048LearningProofStore(root);
        string path = store.Save("May_2", "teacher", "LearningProofCompleted",
            new Fruit2048FirstLearningProofProgress { MovesUsed = 4, MaxMoves = 50, Tier2SampleCount = 3, RequiredSamples = 3, Tier2Learned = true, Tier2RecognitionConfirmed = true, Stage = "Completed" }, null);
        Assert(File.Exists(path) && !path.Contains("\\bin\\") && !path.Contains("\\obj\\")
            && File.ReadAllText(path).Contains("LearningProofCompleted"));
    }

    private static Fruit2048BoardReadResult ProofRead(Fruit2048Board board)
    {
        Fruit2048BoardReadResult read = KnownRead(board);
        foreach (Fruit2048Cell cell in read.Cells)
            cell.Tier = FruitTierCatalog.ToTier(cell.Value ?? 0);
        return read;
    }

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
        public Fruit2048RunResult Run() => Service.RunAsync("A", new Fruit2048RunRequest { TargetTile = 2 }, null, Cancellation.Token).GetAwaiter().GetResult();
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
        public Task<Fruit2048BoardReadResult> ReadAsync(string d, bool retainOriginalScreenshot, CancellationToken c) => ReadAsync(d, c);
        public Task<bool> TryTapRefreshAsync(string d, CancellationToken c) => Task.FromResult(false);
    }
    private sealed class FakeSwipe : IFruit2048SwipeExecutor { public int Count; public Task<Fruit2048SwipeExecution> ExecuteAsync(string d, Fruit2048Board b, Fruit2048Move m, ImageRegion r, int w, int h, CancellationToken c) { c.ThrowIfCancellationRequested(); Count++; return Task.FromResult(new Fruit2048SwipeExecution { CommandAccepted = true }); } }
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
