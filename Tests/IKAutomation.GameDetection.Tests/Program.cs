using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IKAutomation.GameDetection.Tests
{
    internal static class Program
    {
        private static readonly CancellationToken TestToken = new CancellationToken(false);
        private static int passed;
        private static int failed;

        private static int Main()
        {
            Run("ResourceSearchPanel requires both signals", PanelRequiresBothSignals);
            Run("ResourceSearchPanel has priority over WorldMap", PanelHasPriority);
            Run("WorldMap from WorldMapAnchor only", WorldMapFromAnchor);
            Run("ContinentMap from ContinentMapTitle", ContinentMapFromTitle);
            Run("Unknown when no template matches", UnknownWhenNoMatches);
            Run("Unknown is not an exception", UnknownIsSuccessful);
            Run("Capture failure returns ErrorMessage", CaptureFailureReturnsError);
            Run("Empty device name is rejected", EmptyDeviceRejected);
            Run("Cancellation is respected and propagated", CancellationRespected);
            Run("Wrong resolution stops matching", WrongResolutionStopsMatching);
            Run("Evidence contains all four templates", EvidenceContainsThreeTemplates);
            Run("Unknown screenshot path is correct", UnknownScreenshotPathIsCorrect);
            Run("Unknown screenshot save failure does not crash", UnknownSaveFailureDoesNotCrash);
            Run("Detector never sends input", DetectorNeverSendsInput);
            Run("Missing template produces configuration evidence", MissingTemplateProducesEvidence);
            Run("Runtime registry excludes fixture", RuntimeRegistryExcludesFixture);
            Run("Real fixture detects ResourceSearchPanel", RealFixtureDetectsPanel);
            Console.WriteLine($"Game detection tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS: " + name); }
            catch (Exception exception) { failed++; Console.Error.WriteLine($"FAIL: {name} - {exception}"); }
        }

        private static void PanelRequiresBothSignals()
        {
            GameDetectionResult result = DetectWithMatches(
                TemplateId.ResourceSearchPanelAnchor,
                TemplateId.SearchButtonEnabled);
            Equal(GameState.ResourceSearchPanel, result.State, "Both panel signals should confirm panel.");
            Equal(
                GameState.Unknown,
                DetectWithMatches(TemplateId.ResourceSearchPanelAnchor).State,
                "Panel anchor alone must not confirm the panel.");
        }

        private static void PanelHasPriority()
        {
            GameDetectionResult result = DetectWithMatches(
                TemplateId.ResourceSearchPanelAnchor,
                TemplateId.SearchButtonEnabled,
                TemplateId.WorldMapAnchor);
            Equal(GameState.ResourceSearchPanel, result.State, "Panel should have priority.");
        }

        private static void WorldMapFromAnchor()
        {
            Equal(GameState.WorldMap, DetectWithMatches(TemplateId.WorldMapAnchor).State, "World map rule failed.");
        }

        private static void ContinentMapFromTitle()
        {
            Equal(GameState.ContinentMap, DetectWithMatches(TemplateId.ContinentMapTitle).State, "Continent map rule failed.");
        }


        private static void UnknownWhenNoMatches()
        {
            Equal(GameState.Unknown, DetectWithMatches().State, "No match should return Unknown.");
        }

        private static void UnknownIsSuccessful()
        {
            GameDetectionResult result = DetectWithMatches();
            Assert(result.IsSuccessful, "Unknown without technical errors must be successful.");
            Assert(string.IsNullOrEmpty(result.ErrorMessage), "Unknown should not contain a technical error.");
        }

        private static void CaptureFailureReturnsError()
        {
            var client = new FakeLdPlayerClient { CaptureException = new InvalidOperationException("camera unavailable") };
            GameDetectionResult result = CreateDetector(client).DetectAsync("IK-1", TestToken).GetAwaiter().GetResult();
            Assert(!result.IsSuccessful, "Capture failure must be unsuccessful.");
            Contains(result.ErrorMessage, "camera unavailable", "Capture error was not returned.");
        }

        private static void EmptyDeviceRejected()
        {
            Throws<ArgumentException>(() => CreateDetector(new FakeLdPlayerClient())
                .DetectAsync(" ", TestToken).GetAwaiter().GetResult());
        }

        private static void CancellationRespected()
        {
            var client = new FakeLdPlayerClient();
            var detector = CreateDetector(client);
            Throws<OperationCanceledException>(() => detector
                .DetectAsync("IK-1", new CancellationToken(true)).GetAwaiter().GetResult());
            Equal(0, client.CaptureCalls, "Pre-canceled detection should not capture.");

            using (var source = new CancellationTokenSource())
            {
                detector.DetectAsync("IK-1", source.Token).GetAwaiter().GetResult();
                Equal(source.Token, client.CaptureToken, "Capture did not receive the caller token.");
            }
        }

        private static void WrongResolutionStopsMatching()
        {
            var matcher = new FakeImageMatcher();
            var client = new FakeLdPlayerClient { Screenshot = CreatePng(960, 540) };
            GameDetectionResult result = CreateDetector(client, matcher: matcher)
                .DetectAsync("IK-Resolution", TestToken).GetAwaiter().GetResult();
            Assert(!result.IsSuccessful, "Wrong resolution should be a technical error.");
            Contains(result.ErrorMessage, "1280x720", "Expected resolution is missing from error.");
            Contains(result.ErrorMessage, "960x540", "Actual resolution is missing from error.");
            Contains(result.ErrorMessage, "IK-Resolution", "Device name is missing from error.");
            Equal(0, matcher.FindCalls, "Matcher must not run for wrong resolution.");
        }

        private static void EvidenceContainsThreeTemplates()
        {
            GameDetectionResult result = DetectWithMatches();
            Equal(4, result.Evidence.Count, "Detector must check exactly four templates.");
            foreach (TemplateId id in RequiredIds())
                Assert(result.Evidence.Any(item => item.TemplateId == id), "Missing evidence for " + id);
        }

        private static void UnknownScreenshotPathIsCorrect()
        {
            string root = TempDirectory();
            try
            {
                var time = new DateTimeOffset(2026, 7, 16, 1, 2, 3, 456, TimeSpan.FromHours(7));
                var store = new UnknownScreenshotStore(root, () => time);
                string path = store.SaveAsync("Device/1", CreatePng(1280, 720), TestToken).GetAwaiter().GetResult();
                string expected = Path.Combine(root, "Device_1", "2026-07-16", "01-02-03-456_unknown.png");
                Equal(Path.GetFullPath(expected), path, "Unknown screenshot path is incorrect.");
                Assert(File.Exists(path), "Unknown screenshot was not saved.");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void DetectorNeverSendsInput()
        {
            var client = new FakeLdPlayerClient();
            CreateDetector(client).DetectAsync("IK-1", TestToken).GetAwaiter().GetResult();
            Equal(0, client.InputCalls, "Detector sent an input command.");
        }

        private static void UnknownSaveFailureDoesNotCrash()
        {
            var detector = new GameStateDetector(
                new FakeLdPlayerClient(), new FakeTemplateRegistry(), new FakeImageMatcher(),
                Options(true), new FailingUnknownStore(), new FakeLogger());
            GameDetectionResult result = detector.DetectAsync("IK-1", TestToken).GetAwaiter().GetResult();
            Assert(result.IsSuccessful, "Unknown screenshot save failure must not fail detection.");
            Equal(GameState.Unknown, result.State, "Save failure changed the detected state.");
        }

        private static void MissingTemplateProducesEvidence()
        {
            var registry = new FakeTemplateRegistry { Missing = TemplateId.SearchButtonEnabled };
            GameDetectionResult result = CreateDetector(new FakeLdPlayerClient(), registry: registry)
                .DetectAsync("IK-1", TestToken).GetAwaiter().GetResult();
            Assert(!result.IsSuccessful, "Missing required template should be a configuration error.");
            Contains(result.ErrorMessage, nameof(TemplateId.SearchButtonEnabled), "Missing template ID not reported.");
            GameDetectionEvidence item = result.Evidence.First(e => e.TemplateId == TemplateId.SearchButtonEnabled);
            Assert(!item.TemplateExists, "Missing template evidence incorrectly reports existence.");
        }

        private static void RuntimeRegistryExcludesFixture()
        {
            var registry = new TemplateRegistry(DataRoot());
            Equal("Global/world_map_anchor.png", registry.GetDefinition(TemplateId.WorldMapAnchor).RelativePath, "World template path mismatch.");
            Equal("Global/continent_map_title.png", registry.GetDefinition(TemplateId.ContinentMapTitle).RelativePath, "Continent template path mismatch.");
            Equal("Search/resource_search_panel_anchor.png", registry.GetDefinition(TemplateId.ResourceSearchPanelAnchor).RelativePath, "Panel template path mismatch.");
            Equal("Search/search_button_enabled.png", registry.GetDefinition(TemplateId.SearchButtonEnabled).RelativePath, "Button template path mismatch.");
            foreach (TemplateId id in RequiredIds())
                Assert(registry.GetDefinition(id).RelativePath.IndexOf("resource_search_screen", StringComparison.OrdinalIgnoreCase) < 0, "Fixture used as runtime template.");
        }

        private static void RealFixtureDetectsPanel()
        {
            string fixture = Path.Combine(DataRoot(), "Fixtures", "resource_search_screen.png");
            var detector = new GameStateDetector(
                new FakeLdPlayerClient(), new TemplateRegistry(DataRoot()), new KAutoImageMatcher(),
                Options(false), new FakeUnknownStore(), new FakeLogger());
            GameDetectionResult result = detector.Detect(File.ReadAllBytes(fixture));
            Equal(GameState.ResourceSearchPanel, result.State, "Real fixture should match both panel signals.");
        }

        private static GameDetectionResult DetectWithMatches(params TemplateId[] matches)
        {
            var matcher = new FakeImageMatcher();
            foreach (TemplateId id in matches) matcher.Matches.Add(id);
            return CreateDetector(new FakeLdPlayerClient(), matcher: matcher)
                .DetectAsync("IK-1", TestToken).GetAwaiter().GetResult();
        }

        private static GameStateDetector CreateDetector(
            FakeLdPlayerClient client,
            FakeTemplateRegistry registry = null,
            FakeImageMatcher matcher = null)
        {
            return new GameStateDetector(
                client, registry ?? new FakeTemplateRegistry(), matcher ?? new FakeImageMatcher(),
                Options(false), new FakeUnknownStore(), new FakeLogger());
        }

        private static GameDetectionOptions Options(bool saveUnknown)
            => new GameDetectionOptions(1280, 720, true, saveUnknown, "Diagnostics/UnknownStates");
        private static TemplateId[] RequiredIds() => new[] { TemplateId.ResourceSearchPanelAnchor, TemplateId.SearchButtonEnabled, TemplateId.ContinentMapTitle, TemplateId.WorldMapAnchor };
        private static string DataRoot() => Path.Combine(AppContext.BaseDirectory, "Data", "InfinityKingdom", "1280x720", "vi");
        private static string TempDirectory() { string path = Path.Combine(Path.GetTempPath(), "IKGameDetectionTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }

        private static byte[] CreatePng(int width, int height)
        {
            using (var bitmap = new Bitmap(width, height))
            using (var stream = new MemoryStream()) { bitmap.Save(stream, ImageFormat.Png); return stream.ToArray(); }
        }

        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message} Expected={expected}, Actual={actual}."); }
        private static void Contains(string actual, string expected, string message) { if (actual == null || actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0) throw new Exception(message + " Actual=" + actual); }
        private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

        private sealed class FakeTemplateRegistry : ITemplateRegistry
        {
            public TemplateId? Missing { get; set; }
            public TemplateDefinition GetDefinition(TemplateId id) => new TemplateDefinition(id, id + ".png", 0.8);
            public string GetPath(TemplateId id) => Path.Combine("templates", id + ".png");
            public bool Exists(TemplateId id) => !Missing.HasValue || Missing.Value != id;
            public byte[] LoadBytes(TemplateId id) { if (!Exists(id)) throw new FileNotFoundException(id.ToString()); return new[] { (byte)id }; }
        }

        private sealed class FakeImageMatcher : IImageMatcher
        {
            public HashSet<TemplateId> Matches { get; } = new HashSet<TemplateId>();
            public int FindCalls { get; private set; }
            public ImageMatchResult Find(byte[] screenshot, byte[] template, ImageRegion? region = null)
            {
                FindCalls++;
                TemplateId id = (TemplateId)template[0];
                return Matches.Contains(id) ? ImageMatchResult.FoundAt(10, 20, 30, 40) : ImageMatchResult.NotFound();
            }
        }

        private sealed class FakeUnknownStore : IUnknownScreenshotStore
        { public Task<string> SaveAsync(string device, byte[] png, CancellationToken token) => Task.FromResult("unknown.png"); }
        private sealed class FailingUnknownStore : IUnknownScreenshotStore
        { public Task<string> SaveAsync(string device, byte[] png, CancellationToken token) { throw new IOException("disk full"); } }
        private sealed class FakeLogger : IDiagnosticLogger
        { public void Info(string message) { } public void Error(string message, Exception exception) { } }

        private sealed class FakeLdPlayerClient : ILdPlayerClient
        {
            public byte[] Screenshot { get; set; } = CreatePng(1280, 720);
            public Exception CaptureException { get; set; }
            public int CaptureCalls { get; private set; }
            public int InputCalls { get; private set; }
            public CancellationToken CaptureToken { get; private set; }
            public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<string>>(new[] { "LDPlayer" });
            public Task<byte[]> CaptureScreenshotPngAsync(string d, CancellationToken t) { CaptureCalls++; CaptureToken = t; if (CaptureException != null) throw CaptureException; return Task.FromResult(Screenshot); }
            public Task<bool> IsRunningAsync(string d, CancellationToken t) => Task.FromResult(true);
            public Task OpenAsync(string d, CancellationToken t) => Task.CompletedTask;
            public Task CloseAsync(string d, CancellationToken t) => Task.CompletedTask;
            public Task RunAppAsync(string d, string p, CancellationToken t) => Task.CompletedTask;
            public Task TapAsync(string d, int x, int y, CancellationToken t) { InputCalls++; return Task.CompletedTask; }
            public Task TapByPercentAsync(string d, double x, double y, CancellationToken t) { InputCalls++; return Task.CompletedTask; }
            public Task LongPressAsync(string d, int x, int y, int ms, CancellationToken t) { InputCalls++; return Task.CompletedTask; }
            public Task SwipeByPercentAsync(string d, double sx, double sy, double ex, double ey, int ms, CancellationToken t) { InputCalls++; return Task.CompletedTask; }
            public Task BackAsync(string d, CancellationToken t) { InputCalls++; return Task.CompletedTask; }
            public Task InputTextAsync(string d, string text, CancellationToken t) { InputCalls++; return Task.CompletedTask; }
            public Task PressKeyAsync(string d, AndroidKeyCode key, CancellationToken t) { InputCalls++; return Task.CompletedTask; }
        }
    }
}
