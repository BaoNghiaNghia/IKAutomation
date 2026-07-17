using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.ResourcePopup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IKAutomation.ResourcePopup.Tests
{
    internal static class Program
    {
        private static readonly CancellationToken Token = new CancellationToken(false);
        private static int passed, failed;
        private static int Main()
        {
            Run("All signals produce Ready", AllSignalsReady);
            Run("Anchor and title produce NotReady", AnchorTitleNotReady);
            Run("Anchor and Gather without Iron is not Ready", AnchorGatherNotReady);
            Run("Gather alone is NotDetected", GatherAloneNotDetected);
            Run("Header and action templates use separate regions", MatcherUsesRoi);
            Run("Bounds use screenshot coordinates", BoundsPreserved);
            Run("Missing template fails before capture", MissingTemplateFails);
            Run("Verification sends no input", SendsNoInput);
            Run("Cancellation is returned", CancellationReturned);
            Run("Timeout is bounded", TimeoutBounded);
            Run("Diagnostic failure does not replace outcome", DiagnosticFailureSafe);
            Run("Ready frame requirement is honored", ReadyFramesHonored);
            Run("Options validate polling", OptionsValidatePolling);
            Run("Diagnostic path is sanitized and unique", DiagnosticPathSafe);
            Run("Day and night pixels do not change popup rule", DayNightSafe);
            Run("Initial and final state are returned", StatesReturned);
            Run("Stone popup uses Stone title", StonePopupReady);
            Run("Stone title verifies embedded popup anchor", StoneEmbeddedAnchorReady);
            Run("Missing Stone title fails before capture", MissingStoneTitleFails);
            Run("Wood popup uses Wood title", WoodPopupReady);
            Run("Food popup uses Food title", FoodPopupReady);
            Run("Different resource title returns controlled mismatch", PopupMismatch);
            Run("Iron levels 5 6 and 7 use the same title template", IronLevelsUseSameTitle);
            Run("Missing Iron title message names template and region", MissingIronTitleMessage);
            Run("Service contains no default cancellation token", NoCancellationNone);
            Console.WriteLine($"Resource popup tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        { try { test(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL: " + name + " - " + ex); } }

        private static void AllSignalsReady()
        { Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle, TemplateId.GatherButtonEnabled); var r = Run(f); Equal(ResourcePopupOutcome.ResourcePopupReady, r.Outcome); Assert(r.Success && r.PopupAnchorVerified && r.IronResourceVerified && r.GatherButtonVerified, r.Message); }
        private static void AnchorTitleNotReady()
        { Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle); var r = Run(f); Equal(ResourcePopupOutcome.ResourcePopupDetectedButNotReady, r.Outcome); Assert(!r.Success && !r.GatherButtonVerified, r.Message); }
        private static void AnchorGatherNotReady()
        { Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.GatherButtonEnabled); var r = Run(f); Equal(ResourcePopupOutcome.ResourcePopupDetectedButNotReady, r.Outcome); Assert(!r.IronResourceVerified, r.Message); }
        private static void GatherAloneNotDetected()
        { Fixture f = Setup(TemplateId.GatherButtonEnabled); var r = Run(f); Equal(ResourcePopupOutcome.ResourcePopupNotDetected, r.Outcome); }
        private static void MatcherUsesRoi()
        { Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle, TemplateId.GatherButtonEnabled); Run(f); Equal(new ImageRegion(450,230,680,310),f.Matcher.Regions[TemplateId.ResourcePopupInfoAnchor].Value);Equal(new ImageRegion(450,230,680,310),f.Matcher.Regions[TemplateId.ResourcePopupIronTitle].Value);Equal(new ImageRegion(560,430,500,260),f.Matcher.Regions[TemplateId.GatherButtonEnabled].Value); }
        private static void BoundsPreserved()
        { Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle, TemplateId.GatherButtonEnabled); var r = Run(f); Equal(700, r.GatherButtonMatch.X); Equal(520, r.GatherButtonMatch.Y); }
        private static void MissingTemplateFails()
        { Fixture f = Setup(); f.Registry.Missing = TemplateId.ResourcePopupIronTitle; var r = Run(f); Equal(ResourcePopupOutcome.Failed, r.Outcome); Assert(r.ErrorMessage.Contains("ResourcePopupIronTitle"), r.ErrorMessage); Equal(0, f.Client.Captures); }
        private static void SendsNoInput()
        { Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle, TemplateId.GatherButtonEnabled); Run(f); Equal(0, f.Client.InputCalls); }
        private static void CancellationReturned()
        { Fixture f = Setup(); using (var source = new CancellationTokenSource()) { source.Cancel(); var r = Run(f, source.Token); Equal(ResourcePopupOutcome.Cancelled, r.Outcome); } }
        private static void TimeoutBounded()
        { Fixture f = Setup(); var watch = Stopwatch.StartNew(); Run(f); Assert(watch.Elapsed < TimeSpan.FromSeconds(2), "Timeout was not bounded."); }
        private static void DiagnosticFailureSafe()
        { Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle); f.Store.Throw = true; var r = Run(f); Equal(ResourcePopupOutcome.ResourcePopupDetectedButNotReady, r.Outcome); }
        private static void ReadyFramesHonored()
        { Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle, TemplateId.GatherButtonEnabled, readyFrames: 2); var r = Run(f); Equal(ResourcePopupOutcome.ResourcePopupReady, r.Outcome); Assert(r.ObservedFrameCount >= 2, "Consecutive frames ignored."); }
        private static void OptionsValidatePolling()
        { Throws<ArgumentOutOfRangeException>(() => Options(0)); Throws<ArgumentOutOfRangeException>(() => Options(1, 4)); }
        private static void DiagnosticPathSafe()
        {
            string root = Path.Combine(Path.GetTempPath(), "IKPopupTests", Guid.NewGuid().ToString("N"));
            try { var store = new ResourcePopupDiagnosticStore(root); byte[] png = Png(Color.Black); string a = store.SaveAsync("../bad/device", ResourcePopupOutcome.ResourcePopupNotDetected, png, Token).GetAwaiter().GetResult(); string b = store.SaveAsync("../bad/device", ResourcePopupOutcome.ResourcePopupNotDetected, png, Token).GetAwaiter().GetResult(); Assert(File.Exists(a) && File.Exists(b) && a != b, "Diagnostic paths invalid."); Assert(Path.GetFullPath(a).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "Path escaped root."); }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static void DayNightSafe()
        { Fixture day = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle, TemplateId.GatherButtonEnabled); day.Client.Screenshot = Png(Color.SandyBrown); Fixture night = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle, TemplateId.GatherButtonEnabled); night.Client.Screenshot = Png(Color.FromArgb(25, 25, 35)); Equal(Run(day).Outcome, Run(night).Outcome); }
        private static void StatesReturned()
        { Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle, TemplateId.GatherButtonEnabled); f.Detector.State = GameState.ResourcePopup; var r = Run(f); Equal(GameState.ResourcePopup, r.InitialState); Equal(GameState.ResourcePopup, r.FinalState); }
        private static void StonePopupReady()
        {
            Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupStoneTitle, TemplateId.GatherButtonEnabled);
            var result = ((IResourceAwarePopupVerificationService)f.Service)
                .VerifyAsync("LDPlayer", ResourceType.Stone, Token).GetAwaiter().GetResult();
            Equal(ResourcePopupOutcome.ResourcePopupReady, result.Outcome);
            Assert(result.ResourceVerified && result.ResourceType == ResourceType.Stone, result.Message);
        }
        private static void StoneEmbeddedAnchorReady()
        {
            Fixture f = Setup(TemplateId.ResourcePopupStoneTitle, TemplateId.GatherButtonEnabled);
            var result = ((IResourceAwarePopupVerificationService)f.Service)
                .VerifyAsync("LDPlayer", ResourceType.Stone, Token).GetAwaiter().GetResult();
            Equal(ResourcePopupOutcome.ResourcePopupReady, result.Outcome);
            Assert(result.PopupAnchorVerified && !result.PopupAnchorFound
                && result.ExpectedResourceVerified && result.GatherButtonVerified, result.Message);
        }
        private static void MissingStoneTitleFails()
        {
            Fixture f = Setup(); f.Registry.Missing = TemplateId.ResourcePopupStoneTitle;
            var result = ((IResourceAwarePopupVerificationService)f.Service)
                .VerifyAsync("LDPlayer", ResourceType.Stone, Token).GetAwaiter().GetResult();
            Equal(ResourcePopupOutcome.Failed, result.Outcome);
            Assert(result.ErrorMessage.Contains("ResourcePopupStoneTitle"), result.ErrorMessage);
            Equal(0, f.Client.Captures);
        }
        private static void WoodPopupReady() => AssertPopupReady(ResourceType.Wood, TemplateId.ResourcePopupWoodTitle);
        private static void FoodPopupReady() => AssertPopupReady(ResourceType.Food, TemplateId.ResourcePopupFoodTitle);
        private static void AssertPopupReady(ResourceType resource, TemplateId title)
        {
            Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, title, TemplateId.GatherButtonEnabled);
            var result = ((IResourceAwarePopupVerificationService)f.Service)
                .VerifyAsync("LDPlayer", resource, Token).GetAwaiter().GetResult();
            Equal(ResourcePopupOutcome.ResourcePopupReady, result.Outcome);
            Assert(result.ExpectedResource == resource && result.ExpectedResourceVerified
                && result.ExpectedPopupTitleTemplate == title, result.Message);
        }
        private static void PopupMismatch()
        {
            Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle,
                TemplateId.GatherButtonEnabled);
            var result = ((IResourceAwarePopupVerificationService)f.Service)
                .VerifyAsync("LDPlayer", ResourceType.Wood, Token).GetAwaiter().GetResult();
            Equal(ResourcePopupOutcome.ResourcePopupMismatch, result.Outcome);
            Assert(!result.Success && result.MismatchedResource == ResourceType.Iron, result.Message);
            Equal(0, f.Client.InputCalls);
        }
        private static void IronLevelsUseSameTitle()
        {
            foreach (int level in new[] { 5, 6, 7 })
            {
                Fixture f = Setup(TemplateId.ResourcePopupInfoAnchor,
                    TemplateId.ResourcePopupIronTitle, TemplateId.GatherButtonEnabled);
                ResourcePopupVerificationResult result = Run(f);
                Equal(TemplateId.ResourcePopupIronTitle, result.ExpectedPopupTitleTemplate,
                    "Iron Lv" + level);
                Equal(ResourcePopupOutcome.ResourcePopupReady, result.Outcome);
            }
        }
        private static void MissingIronTitleMessage()
        { Fixture f=Setup(TemplateId.ResourcePopupInfoAnchor,TemplateId.GatherButtonEnabled);f.Detector.State=GameState.ResourcePopup;var r=Run(f);Equal(ResourcePopupOutcome.ResourcePopupDetectedButNotReady,r.Outcome);Assert(r.Message.Contains("ResourcePopupIronTitle")&&r.Message.Contains("HeaderRegion"),r.Message); }
        private static void NoCancellationNone()
        { string s=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","Infrastructure","ResourcePopup","ResourcePopupVerificationService.cs"));Assert(!s.Contains("CancellationToken"+".None"),"default token bypass"); }

        private static Fixture Setup(params TemplateId[] matches) => Setup(matches, 1);
        private static Fixture Setup(TemplateId a, TemplateId b, TemplateId c, int readyFrames)
            => Setup(new[] { a, b, c }, readyFrames);
        private static Fixture Setup(TemplateId[] matches, int readyFrames)
        {
            var f = new Fixture(); foreach (var id in matches) f.Matcher.Matches.Add(id);
            f.Service = new ResourcePopupVerificationService(f.Detector, f.Client, f.Registry, f.Matcher,
                Options(1, readyFrames), f.Store, new FakeLogger()); return f;
        }
        private static ResourcePopupVerificationOptions Options(int poll = 1, int ready = 1) =>
            new ResourcePopupVerificationOptions(poll, 1, ready,
                new ImageRegion(450, 230, 680, 310), new ImageRegion(560, 430, 500, 260),
                true, "Diagnostics/ResourcePopup");
        private static ResourcePopupVerificationResult Run(Fixture f, CancellationToken? token = null) =>
            f.Service.VerifyAsync("LDPlayer", token ?? Token).GetAwaiter().GetResult();
        private static TemplateId[] Required() => new[] { TemplateId.ResourcePopupInfoAnchor, TemplateId.ResourcePopupIronTitle, TemplateId.GatherButtonEnabled };
        private static byte[] Png(Color color) { using (var b = new Bitmap(1280, 720)) using (var g = Graphics.FromImage(b)) using (var s = new MemoryStream()) { g.Clear(color); b.Save(s, ImageFormat.Png); return s.ToArray(); } }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual, string message = null) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(message ?? $"Expected {expected}, actual {actual}."); }
        private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

        private sealed class Fixture
        { public FakeClient Client = new FakeClient(); public FakeDetector Detector = new FakeDetector(); public FakeRegistry Registry = new FakeRegistry(); public FakeMatcher Matcher = new FakeMatcher(); public FakeStore Store = new FakeStore(); public IResourcePopupVerificationService Service; }
        private sealed class FakeDetector : IGameStateDetector
        { public GameState State = GameState.Unknown; public GameDetectionResult Detect(byte[] p) => new GameDetectionResult { State = State, IsSuccessful = true, Evidence = new GameDetectionEvidence[0] }; public Task<GameDetectionResult> DetectAsync(string d, CancellationToken t) => Task.FromResult(Detect(null)); }
        private sealed class FakeRegistry : ITemplateRegistry
        { public TemplateId? Missing; public bool UseImageIronTitleTemplate; public TemplateDefinition GetDefinition(TemplateId id) => new TemplateDefinition(id, id + ".png", .8); public string GetPath(TemplateId id) => Path.Combine("templates", id + ".png"); public byte[] LoadBytes(TemplateId id) => id==TemplateId.ResourcePopupIronTitle&&UseImageIronTitleTemplate?CreateTitleTemplate():new[] { (byte)id }; public bool Exists(TemplateId id) => Missing != id; private static byte[] CreateTitleTemplate(){using(var b=new Bitmap(184,81))using(var g=Graphics.FromImage(b))using(var s=new MemoryStream()){g.Clear(Color.LightBlue);g.DrawString("Sắt",SystemFonts.DefaultFont,Brushes.DarkBlue,110,8);b.Save(s,ImageFormat.Png);return s.ToArray();}} }
        private sealed class FakeMatcher : IImageMatcher
        { public HashSet<TemplateId> Matches = new HashSet<TemplateId>(); public Dictionary<TemplateId, ImageRegion?> Regions = new Dictionary<TemplateId, ImageRegion?>(); public bool StableIronTitleOnly; public ImageMatchResult Find(byte[] s, byte[] t, ImageRegion? r = null) { if(t.Length>1&&t[0]==137){using(var stream=new MemoryStream(t))using(var bitmap=new Bitmap(stream))return StableIronTitleOnly&&bitmap.Width<100?ImageMatchResult.FoundAt(850,240,bitmap.Width,bitmap.Height):ImageMatchResult.NotFound();} var id = (TemplateId)t[0]; Regions[id] = r; return Matches.Contains(id) ? ImageMatchResult.FoundAt(id == TemplateId.GatherButtonEnabled ? 700 : 600, id == TemplateId.GatherButtonEnabled ? 520 : 430, 80, 40) : ImageMatchResult.NotFound(); } }
        private sealed class FakeStore : IResourcePopupDiagnosticStore
        { public bool Throw; public Task<string> SaveAsync(string d, ResourcePopupOutcome o, byte[] p, CancellationToken t) { if (Throw) throw new IOException("disk full"); return Task.FromResult("popup.png"); } }
        private sealed class FakeLogger : IDiagnosticLogger
        { public void Info(string m) { } public void Error(string m, Exception e) { } }
        private sealed class FakeClient : ILdPlayerClient
        {
            public byte[] Screenshot = Png(Color.Black); public int Captures, InputCalls;
            public Task<byte[]> CaptureScreenshotPngAsync(string d, CancellationToken t) { Captures++; return Task.FromResult(Screenshot); }
            public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<string>>(new[] { "LDPlayer" });
            public Task<bool> IsRunningAsync(string d, CancellationToken t) => Task.FromResult(true); public Task OpenAsync(string d, CancellationToken t) => Task.CompletedTask; public Task CloseAsync(string d, CancellationToken t) => Task.CompletedTask; public Task RunAppAsync(string d, string p, CancellationToken t) => Task.CompletedTask;
            private Task Input() { InputCalls++; return Task.CompletedTask; }
            public Task TapAsync(string d, int x, int y, CancellationToken t) => Input(); public Task TapByPercentAsync(string d, double x, double y, CancellationToken t) => Input(); public Task LongPressAsync(string d, int x, int y, int m, CancellationToken t) => Input(); public Task SwipeByPercentAsync(string d, double a, double b, double c, double e, int m, CancellationToken t) => Input(); public Task BackAsync(string d, CancellationToken t) => Input(); public Task InputTextAsync(string d, string s, CancellationToken t) => Input(); public Task PressKeyAsync(string d, AndroidKeyCode k, CancellationToken t) => Input();
        }
    }
}
