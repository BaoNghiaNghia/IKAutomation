using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IKAutomation.TeamSelection.Tests
{
    internal static class Program
    {
        private static readonly CancellationToken Token = new CancellationToken(false);
        private static int passed, failed;

        private static int Main()
        {
            Run("Already open does not tap", AlreadyOpen);
            Run("Unverified TeamSelection does not tap", UnverifiedAlreadyOpen);
            Run("Non popup state does not tap", NonPopup);
            Run("Food popup can supersede generic WorldMap state", FoodPopupFromWorldMap);
            Run("Stone stable title is rematched before Gather tap", StoneStableTitleFreshRematch);
            Run("Popup not ready does not tap", PopupNotReady);
            Run("Missing fresh Gather bounds does not tap", MissingGatherBounds);
            Run("Fresh Gather center is tapped", FreshGatherCenter);
            Run("Panel plus Adjust confirms", () => TwoSignalSuccess(TemplateId.TeamAdjustFormationButton));
            Run("Panel plus Action confirms", () => TwoSignalSuccess(TemplateId.TeamActionButtonEnabled));
            Run("All Team signals are ready", AllSignalsReady);
            Run("Pre-tap work does not consume transition timeout", PreTapWorkDoesNotConsumeTransitionTimeout);
            Run("Panel alone is not confirmed", PanelAlone);
            Run("Controls without panel are not confirmed", ControlsWithoutPanel);
            Run("Ready required returns OpenedButNotReady", ReadyRequired);
            Run("Retry is bounded and rematches Gather", RetryRematches);
            Run("Transient Unknown sends no extra input", UnknownBounded);
            Run("Cancelled token returns Cancelled", Cancelled);
            Run("Missing template fails before capture", MissingTemplate);
            Run("Team ROI is used", TeamRoiUsed);
            Run("No prohibited input API is called", NoProhibitedInput);
            Run("Options validate bounds", OptionsValidate);
            Run("Diagnostic save failure does not replace outcome", DiagnosticFailureSafe);
            Console.WriteLine($"Team Selection tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS: " + name); }
            catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL: " + name + " - " + exception); }
        }

        private static void AlreadyOpen()
        {
            Fixture f = Setup();
            f.Detector.Initial = State(GameState.TeamSelection, true,
                TemplateId.TeamSelectionPanelAnchor, TemplateId.TeamAdjustFormationButton);
            OpenTeamSelectionResult r = Execute(f);
            Equal(OpenTeamSelectionOutcome.AlreadyOpen, r.Outcome); Equal(0, f.Client.Taps);
        }

        private static void UnverifiedAlreadyOpen()
        {
            Fixture f = Setup(); f.Detector.Initial = State(GameState.TeamSelection, true);
            f.Client.Frames.Enqueue(Frame(9));
            OpenTeamSelectionResult r = Execute(f);
            Equal(OpenTeamSelectionOutcome.ResourcePopupNotReady, r.Outcome); Equal(0, f.Client.Taps);
        }

        private static void NonPopup()
        { Fixture f = Setup(); f.Detector.Initial = State(GameState.WorldMap, true); f.Popup.Result = Popup(false); Equal(OpenTeamSelectionOutcome.ResourcePopupNotReady, Execute(f).Outcome); Equal(0, f.Client.Taps); }

        private static void FoodPopupFromWorldMap()
        {
            Fixture f = Setup(); f.Detector.Initial = State(GameState.WorldMap, true);
            f.Popup.Result = Popup(true, ResourceType.Food);
            f.Client.Frames.Enqueue(Frame(1)); f.Detector.Offline[1] = GameState.WorldMap;
            MatchPopup(f, 1, 700, 520, true, ResourceType.Food);
            PrepareTeamFrame(f, 2, TemplateId.TeamSelectionPanelAnchor,
                TemplateId.TeamAdjustFormationButton, TemplateId.TeamActionButtonEnabled);
            OpenTeamSelectionResult r = Execute(f, ResourceType.Food);
            Equal(OpenTeamSelectionOutcome.TeamSelectionOpened, r.Outcome);
            Equal(ResourceType.Food, f.Popup.LastResource); Equal(1, f.Client.Taps);
        }

        private static void StoneStableTitleFreshRematch()
        {
            Fixture f = Setup();
            f.Popup.Result = Popup(true, ResourceType.Stone);
            f.Registry.UseImagePopupTitleTemplate = true;
            f.Matcher.StablePopupTitleOnly = true;
            f.Client.Frames.Enqueue(Frame(1));
            f.Detector.Offline[1] = GameState.ResourcePopup;
            f.Matcher.Add(1, TemplateId.GatherButtonEnabled, 700, 520, 80, 40);
            PrepareTeamFrame(f, 2, TemplateId.TeamSelectionPanelAnchor,
                TemplateId.TeamAdjustFormationButton, TemplateId.TeamActionButtonEnabled);

            OpenTeamSelectionResult result = Execute(f, ResourceType.Stone);

            Equal(OpenTeamSelectionOutcome.TeamSelectionOpened, result.Outcome);
            Equal(1, f.Client.Taps);
            Equal(740, f.Client.LastX);
            Equal(540, f.Client.LastY);
        }

        private static void PopupNotReady()
        { Fixture f = Setup(); f.Popup.Result = Popup(false); Equal(OpenTeamSelectionOutcome.ResourcePopupNotReady, Execute(f).Outcome); Equal(0, f.Client.Taps); }

        private static void MissingGatherBounds()
        {
            Fixture f = Setup(); PrepareFreshPopup(f, false); OpenTeamSelectionResult r = Execute(f);
            Equal(OpenTeamSelectionOutcome.GatherButtonNotAvailable, r.Outcome); Equal(0, f.Client.Taps);
        }

        private static void FreshGatherCenter()
        {
            Fixture f = ReadyFlow(); OpenTeamSelectionResult r = Execute(f);
            Equal(OpenTeamSelectionOutcome.TeamSelectionOpened, r.Outcome);
            Equal(1, f.Client.Taps); Equal(740, f.Client.LastX); Equal(540, f.Client.LastY);
            Assert(r.GatherButtonMatch.X == 700 && r.GatherButtonMatch.Width == 80, "Latest bounds were not returned.");
        }

        private static void TwoSignalSuccess(TemplateId second)
        {
            Fixture f = Setup(requireReady: false); PrepareFreshPopup(f, true); PrepareTeamFrame(f, 2,
                TemplateId.TeamSelectionPanelAnchor, second);
            OpenTeamSelectionResult r = Execute(f);
            Equal(OpenTeamSelectionOutcome.TeamSelectionOpened, r.Outcome); Assert(r.TeamSelectionVerified, r.Message);
        }

        private static void AllSignalsReady()
        { Fixture f = ReadyFlow(); OpenTeamSelectionResult r = Execute(f); Assert(r.TeamSelectionReady && r.Success, r.Message); }

        private static void PreTapWorkDoesNotConsumeTransitionTimeout()
        {
            Fixture f = ReadyFlow(); f.Popup.DelayMs = 1100;
            OpenTeamSelectionResult r = Execute(f);
            Equal(OpenTeamSelectionOutcome.TeamSelectionOpened, r.Outcome);
            Assert(r.ObservedFrameCount > 0, "No post-tap frame was observed.");
        }

        private static void PanelAlone()
        {
            Fixture f = Setup(); PrepareFreshPopup(f, true); PrepareTeamFrame(f, 2, TemplateId.TeamSelectionPanelAnchor);
            f.Detector.Offline[2] = GameState.Unknown;
            OpenTeamSelectionResult r = Execute(f); Assert(!r.TeamSelectionVerified && !r.Success, r.Message);
        }

        private static void ControlsWithoutPanel()
        {
            Fixture f = Setup(); PrepareFreshPopup(f, true); PrepareTeamFrame(f, 2,
                TemplateId.TeamAdjustFormationButton, TemplateId.TeamActionButtonEnabled);
            f.Detector.Offline[2] = GameState.Unknown;
            Assert(!Execute(f).TeamSelectionVerified, "Panel anchor was not required.");
        }

        private static void ReadyRequired()
        {
            Fixture f = Setup(maxUnknown: 5000); PrepareFreshPopup(f, true);
            PrepareTeamFrame(f, 2, TemplateId.TeamSelectionPanelAnchor, TemplateId.TeamAdjustFormationButton);
            OpenTeamSelectionResult r = Execute(f);
            Equal(OpenTeamSelectionOutcome.TeamSelectionOpenedButNotReady, r.Outcome); Assert(!r.Success, r.Message);
        }

        private static void RetryRematches()
        {
            Fixture f = Setup(); PrepareFreshPopup(f, true);
            f.Client.Frames.Enqueue(Frame(3)); f.Detector.Offline[3] = GameState.ResourcePopup;
            MatchPopup(f, 3, 710, 510);
            f.Client.Frames.Enqueue(Frame(4)); f.Detector.Offline[4] = GameState.ResourcePopup;
            MatchPopup(f, 4, 760, 560);
            PrepareTeamFrame(f, 2, TemplateId.TeamSelectionPanelAnchor,
                TemplateId.TeamAdjustFormationButton, TemplateId.TeamActionButtonEnabled);
            OpenTeamSelectionResult r = Execute(f);
            Equal(2, r.GatherTapCount); Equal(800, f.Client.LastX); Equal(580, f.Client.LastY);
        }

        private static void UnknownBounded()
        {
            Fixture f = Setup(maxUnknown: 0); PrepareFreshPopup(f, true); f.Client.Frames.Enqueue(Frame(8)); f.Detector.Offline[8] = GameState.Unknown;
            OpenTeamSelectionResult r = Execute(f); Equal(OpenTeamSelectionOutcome.TransitionTimeout, r.Outcome); Equal(1, f.Client.Taps);
        }

        private static void Cancelled()
        {
            Fixture f = Setup(); using (var source = new CancellationTokenSource()) { source.Cancel(); Equal(OpenTeamSelectionOutcome.Cancelled, Execute(f, source.Token).Outcome); }
            Equal(0, f.Client.Taps);
        }

        private static void MissingTemplate()
        { Fixture f = Setup(); f.Registry.Missing = TemplateId.TeamSelectionPanelAnchor; OpenTeamSelectionResult r = Execute(f); Equal(OpenTeamSelectionOutcome.Failed, r.Outcome); Assert(r.ErrorMessage.Contains("TeamSelectionPanelAnchor"), r.ErrorMessage); Equal(0, f.Client.Captures); }

        private static void TeamRoiUsed()
        { Fixture f = ReadyFlow(); Execute(f); ImageRegion? roi = f.Matcher.Regions[TemplateId.TeamSelectionPanelAnchor]; Assert(roi.HasValue && roi.Value.Width == 780 && roi.Value.Height == 720, "Team ROI not used."); }

        private static void NoProhibitedInput()
        { Fixture f = ReadyFlow(); Execute(f); Equal(0, f.Client.ProhibitedInputs); Equal(1, f.Client.Taps); }

        private static void OptionsValidate()
        { Throws<ArgumentOutOfRangeException>(() => Options(ready: true, maxUnknown: 5, region: new ImageRegion(0, 0, 1281, 720))); Throws<ArgumentOutOfRangeException>(() => new OpenTeamSelectionOptions(0, 1, 1, 1, 0, 2, true, true, "x", new ImageRegion(0, 0, 780, 720), new ImageRegion(650, 150, 550, 450))); }

        private static void DiagnosticFailureSafe()
        {
            Fixture f = Setup(); f.Detector.Initial = State(GameState.WorldMap, true);
            f.Client.Frames.Enqueue(Frame(7)); f.Store.Throw = true;
            Equal(OpenTeamSelectionOutcome.ResourcePopupNotReady, Execute(f).Outcome);
        }

        private static Fixture ReadyFlow()
        {
            Fixture f = Setup(); PrepareFreshPopup(f, true); PrepareTeamFrame(f, 2,
                TemplateId.TeamSelectionPanelAnchor, TemplateId.TeamAdjustFormationButton,
                TemplateId.TeamActionButtonEnabled); return f;
        }

        private static Fixture Setup(bool requireReady = true, int maxUnknown = 5)
        {
            var f = new Fixture(); f.Detector.Initial = State(GameState.ResourcePopup, true);
            f.Popup.Result = Popup(true);
            f.Service = new OpenTeamSelectionService(f.Popup, f.Detector, f.Client, f.Registry,
                f.Matcher, f.Lock, Options(requireReady, maxUnknown), f.Store, new FakeLogger());
            return f;
        }

        private static OpenTeamSelectionOptions Options(bool ready = true, int maxUnknown = 5, ImageRegion? region = null) =>
            new OpenTeamSelectionOptions(1, 1, 2, 1, maxUnknown, 2, ready, true, "Diagnostics/TeamSelection",
                region ?? new ImageRegion(0, 0, 780, 720), new ImageRegion(650, 150, 550, 450));

        private static void PrepareFreshPopup(Fixture f, bool gatherBounds)
        {
            f.Client.Frames.Enqueue(Frame(1)); f.Detector.Offline[1] = GameState.ResourcePopup;
            MatchPopup(f, 1, gatherBounds ? 700 : 0, gatherBounds ? 520 : 0, gatherBounds);
        }

        private static void MatchPopup(Fixture f, byte marker, int x, int y, bool gather = true,
            ResourceType resource = ResourceType.Iron)
        {
            f.Matcher.Add(marker, TemplateId.ResourcePopupInfoAnchor, 680, 200, 30, 30);
            f.Matcher.Add(marker, ResourceTemplateMap.PopupTitle(resource), 760, 260, 70, 25);
            if (gather) f.Matcher.Add(marker, TemplateId.GatherButtonEnabled, x, y, 80, 40);
        }

        private static void PrepareTeamFrame(Fixture f, byte marker, params TemplateId[] matches)
        {
            f.Client.Frames.Enqueue(Frame(marker)); f.Detector.Offline[marker] = GameState.TeamSelection;
            foreach (TemplateId id in matches) f.Matcher.Add(marker, id, 100 + (int)id, 300, 100, 40);
        }

        private static ResourcePopupVerificationResult Popup(bool ready,
            ResourceType resource = ResourceType.Iron) => new ResourcePopupVerificationResult
        {
            Outcome = ready ? ResourcePopupOutcome.ResourcePopupReady : ResourcePopupOutcome.ResourcePopupDetectedButNotReady,
            Success = ready, PopupAnchorVerified = ready, IronResourceVerified = ready,
            ResourceVerified = ready, ExpectedResourceVerified = ready, ResourceType = resource,
            ExpectedResource = resource,
            GatherButtonVerified = ready, GatherButtonMatch = ready ? ImageMatchResult.FoundAt(900, 600, 40, 20) : null,
            Evidence = new GameDetectionEvidence[0]
        };

        private static GameDetectionResult State(GameState state, bool success, params TemplateId[] found)
        {
            var evidence = new List<GameDetectionEvidence>();
            foreach (TemplateId id in found) evidence.Add(new GameDetectionEvidence { TemplateId = id, TemplateExists = true, Found = true, MatchResult = ImageMatchResult.FoundAt(1, 1, 10, 10) });
            return new GameDetectionResult { State = state, IsSuccessful = success, Evidence = evidence };
        }

        private static byte[] Frame(byte marker) => new[] { marker };
        private static OpenTeamSelectionResult Execute(Fixture f, CancellationToken? token = null) => f.Service.OpenAsync("LDPlayer", token ?? Token).GetAwaiter().GetResult();
        private static OpenTeamSelectionResult Execute(Fixture f, ResourceType resource) =>
            ((IResourceAwareOpenTeamSelectionService)f.Service).OpenAsync("LDPlayer", resource, Token).GetAwaiter().GetResult();
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}."); }
        private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

        private sealed class Fixture
        {
            public readonly FakeClient Client = new FakeClient(); public readonly FakeDetector Detector = new FakeDetector();
            public readonly FakePopup Popup = new FakePopup(); public readonly FakeRegistry Registry = new FakeRegistry();
            public readonly FakeMatcher Matcher = new FakeMatcher(); public readonly FakeStore Store = new FakeStore();
            public readonly IDeviceOperationLock Lock = new DeviceOperationLock(); public IOpenTeamSelectionService Service;
        }

        private sealed class FakeDetector : IGameStateDetector
        {
            public GameDetectionResult Initial; public readonly Dictionary<byte, GameState> Offline = new Dictionary<byte, GameState>();
            public Task<GameDetectionResult> DetectAsync(string d, CancellationToken t) { t.ThrowIfCancellationRequested(); return Task.FromResult(Initial); }
            public GameDetectionResult Detect(byte[] p) { GameState state = Offline.TryGetValue(p[0], out GameState value) ? value : GameState.Unknown; return State(state, true); }
        }

        private sealed class FakePopup : IResourceAwarePopupVerificationService
        {
            public ResourcePopupVerificationResult Result; public int DelayMs;
            public ResourceType LastResource = ResourceType.Iron;
            public async Task<ResourcePopupVerificationResult> VerifyAsync(string d, CancellationToken t)
            { return await VerifyAsync(d, ResourceType.Iron, t); }
            public async Task<ResourcePopupVerificationResult> VerifyAsync(string d, ResourceType resource, CancellationToken t)
            { LastResource = resource; t.ThrowIfCancellationRequested(); if (DelayMs > 0) await Task.Delay(DelayMs, t); return Result; }
        }

        private sealed class FakeRegistry : ITemplateRegistry
        {
            public TemplateId? Missing; public bool UseImagePopupTitleTemplate; public TemplateDefinition GetDefinition(TemplateId id) => new TemplateDefinition(id, id + ".png", .8);
            public string GetPath(TemplateId id) => Path.Combine("Data", id + ".png"); public bool Exists(TemplateId id) => Missing != id;
            public byte[] LoadBytes(TemplateId id) => UseImagePopupTitleTemplate && IsPopupTitle(id)
                ? CreateTitleTemplate() : new[] { (byte)id };
            private static bool IsPopupTitle(TemplateId id) => id == TemplateId.ResourcePopupIronTitle
                || id == TemplateId.ResourcePopupStoneTitle || id == TemplateId.ResourcePopupWoodTitle
                || id == TemplateId.ResourcePopupFoodTitle;
            private static byte[] CreateTitleTemplate()
            {
                using (var bitmap = new Bitmap(184, 81))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var stream = new MemoryStream())
                {
                    graphics.Clear(Color.LightBlue);
                    graphics.DrawString("Mỏ Đá", SystemFonts.DefaultFont, Brushes.DarkBlue, 110, 8);
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        private sealed class FakeMatcher : IImageMatcher
        {
            private readonly Dictionary<string, ImageMatchResult> matches = new Dictionary<string, ImageMatchResult>();
            public readonly Dictionary<TemplateId, ImageRegion?> Regions = new Dictionary<TemplateId, ImageRegion?>();
            public bool StablePopupTitleOnly;
            public void Add(byte marker, TemplateId id, int x, int y, int w, int h) => matches[marker + ":" + id] = ImageMatchResult.FoundAt(x, y, w, h);
            public ImageMatchResult Find(byte[] screenshot, byte[] template, ImageRegion? region = null)
            {
                if (template.Length > 1 && template[0] == 137)
                {
                    using (var stream = new MemoryStream(template))
                    using (var bitmap = new Bitmap(stream))
                        return StablePopupTitleOnly && bitmap.Width < 100
                            ? ImageMatchResult.FoundAt(760, 260, bitmap.Width, bitmap.Height)
                            : ImageMatchResult.NotFound();
                }
                TemplateId id = (TemplateId)template[0]; Regions[id] = region; return matches.TryGetValue(screenshot[0] + ":" + id, out ImageMatchResult value) ? value : ImageMatchResult.NotFound();
            }
        }

        private sealed class FakeStore : IOpenTeamSelectionDiagnosticStore
        { public bool Throw; public Task<string> SaveAsync(string d, OpenTeamSelectionOutcome o, byte[] p, CancellationToken t) { if (Throw) throw new IOException("disk full"); return Task.FromResult("diagnostic.png"); } }
        private sealed class FakeLogger : IDiagnosticLogger { public void Info(string m) { } public void Error(string m, Exception e) { } }

        private sealed class FakeClient : ILdPlayerClient
        {
            public readonly Queue<byte[]> Frames = new Queue<byte[]>(); public int Captures, Taps, LastX, LastY, ProhibitedInputs;
            public Task<byte[]> CaptureScreenshotPngAsync(string d, CancellationToken t) { t.ThrowIfCancellationRequested(); Captures++; return Task.FromResult(Frames.Count > 0 ? Frames.Dequeue() : Frame(255)); }
            public Task TapAsync(string d, int x, int y, CancellationToken t) { t.ThrowIfCancellationRequested(); Taps++; LastX = x; LastY = y; return Task.CompletedTask; }
            private Task Prohibited() { ProhibitedInputs++; return Task.CompletedTask; }
            public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<string>>(new[] { "LDPlayer" });
            public Task<bool> IsRunningAsync(string d, CancellationToken t) => Task.FromResult(true); public Task OpenAsync(string d, CancellationToken t) => Task.CompletedTask;
            public Task CloseAsync(string d, CancellationToken t) => Task.CompletedTask; public Task RunAppAsync(string d, string p, CancellationToken t) => Task.CompletedTask;
            public Task TapByPercentAsync(string d, double x, double y, CancellationToken t) => Prohibited(); public Task LongPressAsync(string d, int x, int y, int m, CancellationToken t) => Prohibited();
            public Task SwipeByPercentAsync(string d, double a, double b, double c, double e, int m, CancellationToken t) => Prohibited(); public Task BackAsync(string d, CancellationToken t) => Prohibited();
            public Task InputTextAsync(string d, string s, CancellationToken t) => Prohibited(); public Task PressKeyAsync(string d, AndroidKeyCode k, CancellationToken t) => Prohibited();
        }
    }
}
