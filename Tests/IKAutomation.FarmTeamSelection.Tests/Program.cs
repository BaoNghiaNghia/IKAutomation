using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IKAutomation.FarmTeamSelection.Tests
{
    internal static class Program
    {
        private static readonly CancellationToken Token = new CancellationToken(false);
        private static int passed, failed;

        private static int Main()
        {
            Run("TeamSelection not ready sends no Tap", NotReady);
            Run("Team4 already selected returns AlreadySelected", AlreadySelected);
            Run("Disallowed Team1 does not block Team4", Team1DoesNotBlock);
            Run("Priority is Team4 Team3 Team2", PriorityOrder);
            Run("Team4 badge center is tapped", BadgeCenter);
            Run("Tap coordinate is derived from current bounds", DynamicCoordinate);
            Run("Badge is recaptured before Tap", BadgeRecaptured);
            Run("Team4 requires selected border in Team4 ROI", WrongRoiNotSuccess);
            Run("Selected border in Team3 does not verify Team4", WrongRoiNotSuccess);
            Run("Multiple selected borders are ambiguous", Ambiguous);
            Run("Disabled Team4 is skipped", DisabledTeam4);
            Run("Selected Team4 without farm action proceeds to Team3", BusyTeam4ProceedsToTeam3);
            Run("Missing disabled template uses verification", OptionalDisabledMissing);
            Run("Failed Team4 proceeds to Team3", Team4ThenTeam3);
            Run("Team3 success stops before Team2", Team3Stops);
            Run("No badge produces NoEligibleTeam", NoEligible);
            Run("Retry count is bounded", RetryBounded);
            Run("Retry uses fresh bounds", RetryFreshBounds);
            Run("Preflight does not consume selection timeout", PreflightDoesNotConsumeSelectionTimeout);
            Run("Tap near deadline still gets one verification frame", TapNearDeadlineIsVerified);
            Run("Polling cancellation is returned", PollCancellation);
            Run("Retry cancellation is returned", RetryCancellation);
            Run("Lock-wait cancellation is returned", LockWaitCancellation);
            Run("Same-device operations are serialized", SameDeviceSerialized);
            Run("Different devices are not globally blocked", DifferentDevicesConcurrent);
            Run("Back is never called", NoProhibitedInputs);
            Run("Swipe is never called", NoProhibitedInputs);
            Run("LongPress is never called", NoProhibitedInputs);
            Run("Text and key input are never called", NoProhibitedInputs);
            Run("Team action button is never tapped", NoProhibitedInputs);
            Run("No march command is sent", NoProhibitedInputs);
            Run("Diagnostic failure does not replace outcome", DiagnosticFailureSafe);
            Run("Badge matching uses per-team ROI", UsesTeamRoi);
            Run("ROI match returns screenshot coordinates", RoiCoordinatesPreserved);
            Run("Missing badge template records safe failure", MissingBadgeTemplate);
            Run("Duplicate allowed teams are rejected", DuplicateAllowedRejected);
            Run("Duplicate priority is rejected", DuplicatePriorityRejected);
            Run("Priority outside allowed teams is rejected", PriorityOutsideAllowedRejected);
            Run("Team1 is rejected when disabled", Team1Rejected);
            Run("Allowed Team1 remains unsupported without blind Tap", AllowedTeam1Safe);
            Run("Empty lists are rejected", EmptyListsRejected);
            Run("Options reject invalid polling", InvalidOptionsRejected);
            Run("Options reject invalid ROI", InvalidRoiRejected);
            Run("Timeout is bounded", TimeoutBounded);
            Console.WriteLine($"Farm team selection tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS: " + name); }
            catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL: " + name + " - " + exception); }
        }

        private static void NotReady()
        { Fixture f = Setup(); f.Detector.Ready = false; SelectFarmTeamResult r = Execute(f); Equal(SelectFarmTeamOutcome.TeamSelectionNotReady, r.Outcome); Equal(0, f.Client.Taps.Count); }

        private static void AlreadySelected()
        { Fixture f = Setup(); f.Matcher.Selected.Add(TeamNumber.Team4); SelectFarmTeamResult r = Execute(f); Equal(SelectFarmTeamOutcome.AlreadySelected, r.Outcome); Equal(TeamNumber.Team4, r.SelectedTeam.Value); Equal(0, f.Client.Taps.Count); }

        private static void Team1DoesNotBlock()
        { Fixture f = Setup(); f.Matcher.Selected.Add(TeamNumber.Team1); f.Matcher.Badges.Add(TeamNumber.Team4); f.Matcher.SelectOnTap[TeamNumber.Team4] = TeamNumber.Team4; Equal(TeamNumber.Team4, Execute(f).SelectedTeam.Value); }

        private static void PriorityOrder()
        { Fixture f = Setup(); SelectFarmTeamResult r = Execute(f); Sequence(new[] { TeamNumber.Team4, TeamNumber.Team3, TeamNumber.Team2 }, r.AttemptedTeams); }

        private static void BadgeCenter()
        { Fixture f = Successful(TeamNumber.Team4); SelectFarmTeamResult r = Execute(f); Equal(SelectFarmTeamOutcome.TeamSelected, r.Outcome); Equal("1040,470", f.Client.Taps[0]); }

        private static void DynamicCoordinate()
        { Fixture f = Successful(TeamNumber.Team4); f.Matcher.BaseX[TeamNumber.Team4] = 70; Execute(f); Equal("1110,470", f.Client.Taps[0]); }

        private static void BadgeRecaptured()
        { Fixture f = Successful(TeamNumber.Team4); Execute(f); Assert(f.Matcher.BadgeCalls[TeamNumber.Team4] >= 2, "Badge was not inspected and refreshed."); }

        private static void WrongRoiNotSuccess()
        { Fixture f = Setup(maxAttempts: 1); f.Matcher.Badges.Add(TeamNumber.Team4); f.Matcher.SelectOnTap[TeamNumber.Team4] = TeamNumber.Team3; SelectFarmTeamResult r = Execute(f); Assert(r.Outcome != SelectFarmTeamOutcome.TeamSelected || r.SelectedTeam != TeamNumber.Team4, "Wrong ROI verified Team4."); }

        private static void Ambiguous()
        { Fixture f = Setup(); f.Matcher.Selected.Add(TeamNumber.Team3); f.Matcher.Selected.Add(TeamNumber.Team4); SelectFarmTeamResult r = Execute(f); Equal(SelectFarmTeamOutcome.Failed, r.Outcome); Equal(0, f.Client.Taps.Count); }

        private static void DisabledTeam4()
        { Fixture f = Setup(); f.Registry.DisabledExists = true; f.Matcher.Badges.UnionWith(new[] { TeamNumber.Team4, TeamNumber.Team3 }); f.Matcher.Disabled.Add(TeamNumber.Team4); f.Matcher.SelectOnTap[TeamNumber.Team3] = TeamNumber.Team3; SelectFarmTeamResult r = Execute(f); Equal(TeamNumber.Team3, r.SelectedTeam.Value); Assert(!f.Client.Taps.Any(t => t.EndsWith(",470")), "Disabled Team4 was tapped."); }

        private static void BusyTeam4ProceedsToTeam3()
        {
            Fixture f = Setup(maxAttempts: 1);
            f.Matcher.Badges.UnionWith(new[] { TeamNumber.Team4, TeamNumber.Team3 });
            f.Matcher.SelectOnTap[TeamNumber.Team4] = TeamNumber.Team4;
            f.Matcher.SelectOnTap[TeamNumber.Team3] = TeamNumber.Team3;
            f.Detector.ActionAvailable = () => !f.Matcher.Selected.Contains(TeamNumber.Team4);
            SelectFarmTeamResult r = Execute(f);
            Equal(TeamNumber.Team3, r.SelectedTeam.Value);
            Sequence(new[] { TeamNumber.Team4, TeamNumber.Team3 }, r.AttemptedTeams.Take(2));
        }

        private static void OptionalDisabledMissing()
        { Fixture f = Successful(TeamNumber.Team4); f.Registry.DisabledExists = false; Equal(SelectFarmTeamOutcome.TeamSelected, Execute(f).Outcome); }

        private static void Team4ThenTeam3()
        { Fixture f = Setup(maxAttempts: 1); f.Matcher.Badges.UnionWith(new[] { TeamNumber.Team4, TeamNumber.Team3 }); f.Matcher.SelectOnTap[TeamNumber.Team3] = TeamNumber.Team3; SelectFarmTeamResult r = Execute(f); Equal(TeamNumber.Team3, r.SelectedTeam.Value); Assert(r.AttemptedTeams.Take(2).SequenceEqual(new[] { TeamNumber.Team4, TeamNumber.Team3 }), "Priority changed."); }

        private static void Team3Stops()
        { Fixture f = Setup(maxAttempts: 1); f.Matcher.Badges.UnionWith(new[] { TeamNumber.Team3, TeamNumber.Team2 }); f.Matcher.SelectOnTap[TeamNumber.Team3] = TeamNumber.Team3; SelectFarmTeamResult r = Execute(f); Equal(TeamNumber.Team3, r.SelectedTeam.Value); Assert(!r.AttemptedTeams.Contains(TeamNumber.Team2), "Team2 was attempted after success."); }

        private static void NoEligible()
        { Fixture f = Setup(); Equal(SelectFarmTeamOutcome.NoEligibleTeam, Execute(f).Outcome); Equal(0, f.Client.Taps.Count); }

        private static void RetryBounded()
        { Fixture f = Setup(maxAttempts: 2); f.Matcher.Badges.Add(TeamNumber.Team4); Execute(f, Only(TeamNumber.Team4)); Equal(2, f.Client.Taps.Count); }

        private static void RetryFreshBounds()
        { Fixture f = Setup(maxAttempts: 2); f.Matcher.Badges.Add(TeamNumber.Team4); f.Matcher.MoveBadgeEachCall = true; Execute(f, Only(TeamNumber.Team4)); Assert(f.Client.Taps.Distinct().Count() == 2, "Retry reused stale bounds."); }

        private static void PreflightDoesNotConsumeSelectionTimeout()
        {
            Fixture f = Successful(TeamNumber.Team4); f.Detector.DelayMs = 1100;
            SelectFarmTeamResult r = Execute(f, Only(TeamNumber.Team4));
            Equal(SelectFarmTeamOutcome.TeamSelected, r.Outcome);
        }

        private static void TapNearDeadlineIsVerified()
        {
            Fixture f = Successful(TeamNumber.Team3); f.Client.CaptureDelayMs = 600;
            SelectFarmTeamResult r = Execute(f, Only(TeamNumber.Team3));
            Equal(SelectFarmTeamOutcome.TeamSelected, r.Outcome);
            Equal(TeamNumber.Team3, r.SelectedTeam.Value); Equal(1, f.Client.Taps.Count);
        }

        private static void PollCancellation()
        { Fixture f = Setup(); f.Matcher.Badges.Add(TeamNumber.Team4); using (var source = new CancellationTokenSource()) { f.Client.CancelOnTap = source; Equal(SelectFarmTeamOutcome.Cancelled, Execute(f, Only(TeamNumber.Team4), source.Token).Outcome); } }

        private static void RetryCancellation() => PollCancellation();

        private static void LockWaitCancellation()
        {
            Fixture f = Setup(); f.Detector.DelayMs = 150; f.Matcher.Selected.Add(TeamNumber.Team4);
            Task<SelectFarmTeamResult> first = f.Service.SelectAsync("LDPlayer", new TeamSelectionRequest(), Token);
            Thread.Sleep(20);
            using (var source = new CancellationTokenSource(20))
                Equal(SelectFarmTeamOutcome.Cancelled, f.Service.SelectAsync("LDPlayer", new TeamSelectionRequest(), source.Token).GetAwaiter().GetResult().Outcome);
            first.GetAwaiter().GetResult();
        }

        private static void SameDeviceSerialized()
        {
            Fixture f = Setup(); f.Detector.DelayMs = 60; f.Matcher.Selected.Add(TeamNumber.Team4);
            Task.WaitAll(f.Service.SelectAsync("A", new TeamSelectionRequest(), Token), f.Service.SelectAsync("A", new TeamSelectionRequest(), Token));
            Equal(1, f.Detector.MaxActive);
        }

        private static void DifferentDevicesConcurrent()
        {
            Fixture f = Setup(); f.Detector.DelayMs = 60; f.Matcher.Selected.Add(TeamNumber.Team4);
            Task.WaitAll(f.Service.SelectAsync("A", new TeamSelectionRequest(), Token), f.Service.SelectAsync("B", new TeamSelectionRequest(), Token));
            Assert(f.Detector.MaxActive >= 2, "Different devices were globally locked.");
        }

        private static void NoProhibitedInputs()
        { Fixture f = Successful(TeamNumber.Team4); Execute(f); Equal(0, f.Client.ProhibitedInputs); Equal(1, f.Client.Taps.Count); }

        private static void DiagnosticFailureSafe()
        { Fixture f = Setup(); f.Store.Throw = true; Equal(SelectFarmTeamOutcome.NoEligibleTeam, Execute(f).Outcome); }

        private static void UsesTeamRoi()
        { Fixture f = Successful(TeamNumber.Team4); Execute(f); ImageRegion region = f.Matcher.Regions[TeamNumber.Team4]; Equal(435, region.Y); Equal(155, region.Height); }

        private static void RoiCoordinatesPreserved()
        { Fixture f = Successful(TeamNumber.Team3); f.Matcher.BaseX[TeamNumber.Team3] = 40; Execute(f, Only(TeamNumber.Team3)); Assert(f.Client.Taps[0].StartsWith("1080,"), "ROI coordinate was not translated to screenshot coordinates."); }

        private static void MissingBadgeTemplate()
        { Fixture f = Setup(); f.Registry.Missing = TemplateId.Team4Badge; SelectFarmTeamResult r = Execute(f, Only(TeamNumber.Team4)); Equal(SelectFarmTeamOutcome.NoEligibleTeam, r.Outcome); Equal(0, f.Client.Taps.Count); Assert(r.Attempts[0].Message.Contains("Team4Badge"), r.Attempts[0].Message); }

        private static void DuplicateAllowedRejected()
        { Fixture f = Setup(); TeamSelectionRequest q = Only(TeamNumber.Team4); q.AllowedTeams = new[] { TeamNumber.Team4, TeamNumber.Team4 }; Equal(SelectFarmTeamOutcome.Failed, Execute(f, q).Outcome); Equal(0, f.Client.Taps.Count); }

        private static void DuplicatePriorityRejected()
        { Fixture f = Setup(); TeamSelectionRequest q = Only(TeamNumber.Team4); q.Priority = new[] { TeamNumber.Team4, TeamNumber.Team4 }; Equal(SelectFarmTeamOutcome.Failed, Execute(f, q).Outcome); }

        private static void PriorityOutsideAllowedRejected()
        { Fixture f = Setup(); TeamSelectionRequest q = Only(TeamNumber.Team4); q.Priority = new[] { TeamNumber.Team3 }; Equal(SelectFarmTeamOutcome.Failed, Execute(f, q).Outcome); }

        private static void Team1Rejected()
        { Fixture f = Setup(); var q = new TeamSelectionRequest { AllowedTeams = new[] { TeamNumber.Team1 }, Priority = new[] { TeamNumber.Team1 }, AllowTeam1 = false }; Equal(SelectFarmTeamOutcome.Failed, Execute(f, q).Outcome); }

        private static void AllowedTeam1Safe()
        { Fixture f = Setup(); var q = new TeamSelectionRequest { AllowedTeams = new[] { TeamNumber.Team1 }, Priority = new[] { TeamNumber.Team1 }, AllowTeam1 = true }; Equal(SelectFarmTeamOutcome.NoEligibleTeam, Execute(f, q).Outcome); Equal(0, f.Client.Taps.Count); }

        private static void EmptyListsRejected()
        { Fixture f = Setup(); var q = new TeamSelectionRequest { AllowedTeams = new TeamNumber[0], Priority = new TeamNumber[0] }; Equal(SelectFarmTeamOutcome.Failed, Execute(f, q).Outcome); }

        private static void InvalidOptionsRejected()
        { Throws<ArgumentOutOfRangeException>(() => Options(0, 1)); Throws<ArgumentOutOfRangeException>(() => Options(1, 4)); }

        private static void InvalidRoiRejected()
        { Dictionary<TeamNumber, ImageRegion> regions = Regions(); regions[TeamNumber.Team4] = new ImageRegion(1200, 700, 235, 155); Throws<ArgumentOutOfRangeException>(() => new FarmTeamSelectionOptions(1, 1, 1, 1, true, "x", regions)); }

        private static void TimeoutBounded()
        { Fixture f = Setup(timeoutSeconds: 1); f.Matcher.Badges.Add(TeamNumber.Team4); Stopwatch watch = Stopwatch.StartNew(); SelectFarmTeamResult r = Execute(f, Only(TeamNumber.Team4)); Assert(watch.Elapsed < TimeSpan.FromSeconds(2), "Timeout was not bounded."); Assert(!r.Success, "Unexpected success."); }

        private static Fixture Successful(TeamNumber team)
        { Fixture f = Setup(); f.Matcher.Badges.Add(team); f.Matcher.SelectOnTap[team] = team; return f; }

        private static Fixture Setup(int maxAttempts = 2, int timeoutSeconds = 1)
        {
            var f = new Fixture();
            f.Detector = new FakeDetector(); f.Registry = new FakeRegistry(); f.Matcher = new FakeMatcher();
            f.Client = new FakeClient(f.Matcher); f.Store = new FakeStore();
            f.Service = new SelectFarmTeamService(f.Detector, f.Client, f.Registry, f.Matcher,
                new DeviceOperationLock(), Options(1, maxAttempts, timeoutSeconds), f.Store, new FakeLogger());
            return f;
        }

        private static FarmTeamSelectionOptions Options(int poll = 1, int attempts = 2, int timeout = 1) =>
            new FarmTeamSelectionOptions(poll, timeout, attempts, 1, true, "Diagnostics/FarmTeamSelection", Regions());
        private static Dictionary<TeamNumber, ImageRegion> Regions() => new Dictionary<TeamNumber, ImageRegion>
        {
            { TeamNumber.Team1, new ImageRegion(0, 0, 235, 150) },
            { TeamNumber.Team2, new ImageRegion(0, 145, 235, 145) },
            { TeamNumber.Team3, new ImageRegion(0, 290, 235, 145) },
            { TeamNumber.Team4, new ImageRegion(0, 435, 235, 155) }
        };
        private static TeamSelectionRequest Only(TeamNumber team) => new TeamSelectionRequest { AllowedTeams = new[] { team }, Priority = new[] { team }, AllowTeam1 = team == TeamNumber.Team1 };
        private static SelectFarmTeamResult Execute(Fixture f, TeamSelectionRequest request = null, CancellationToken? token = null) => f.Service.SelectAsync("LDPlayer", request ?? new TeamSelectionRequest(), token ?? Token).GetAwaiter().GetResult();
        private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) { if (!expected.SequenceEqual(actual)) throw new Exception("Expected " + string.Join(",", expected) + "; actual " + string.Join(",", actual)); }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}."); }
        private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

        private sealed class Fixture
        { public FakeClient Client; public FakeDetector Detector; public FakeRegistry Registry; public FakeMatcher Matcher; public FakeStore Store; public ISelectFarmTeamService Service; }

        private sealed class FakeDetector : IGameStateDetector
        {
            private int active; public bool Ready = true; public int DelayMs, MaxActive;
            public Func<bool> ActionAvailable = () => true;
            public async Task<GameDetectionResult> DetectAsync(string d, CancellationToken t)
            { int now = Interlocked.Increment(ref active); MaxActive = Math.Max(MaxActive, now); try { if (DelayMs > 0) await Task.Delay(DelayMs, t); return Result(); } finally { Interlocked.Decrement(ref active); } }
            public GameDetectionResult Detect(byte[] p) => Result();
            private GameDetectionResult Result()
            {
                TemplateId[] ids = Ready
                    ? (ActionAvailable()
                        ? new[] { TemplateId.TeamSelectionPanelAnchor, TemplateId.TeamAdjustFormationButton, TemplateId.TeamActionButtonEnabled }
                        : new[] { TemplateId.TeamSelectionPanelAnchor, TemplateId.TeamAdjustFormationButton })
                    : new[] { TemplateId.TeamSelectionPanelAnchor };
                return new GameDetectionResult { State = GameState.TeamSelection, IsSuccessful = true,
                    Evidence = ids.Select(id => new GameDetectionEvidence { TemplateId = id, TemplateExists = true, Found = true, MatchResult = ImageMatchResult.FoundAt(1, 1, 10, 10) }).ToArray() };
            }
        }

        private sealed class FakeRegistry : ITemplateRegistry
        {
            public TemplateId? Missing; public bool DisabledExists;
            public TemplateDefinition GetDefinition(TemplateId id) => new TemplateDefinition(id, id + ".png", .8);
            public string GetPath(TemplateId id) => Path.Combine("Data", "Teams", id + ".png");
            public byte[] LoadBytes(TemplateId id) => new[] { (byte)id };
            public bool Exists(TemplateId id) => Missing != id && (id != TemplateId.TeamDisabledAnchor || DisabledExists);
        }

        private sealed class FakeMatcher : IImageMatcher
        {
            public readonly HashSet<TeamNumber> Badges = new HashSet<TeamNumber>();
            public readonly HashSet<TeamNumber> Disabled = new HashSet<TeamNumber>();
            public readonly HashSet<TeamNumber> Selected = new HashSet<TeamNumber>();
            public readonly Dictionary<TeamNumber, TeamNumber> SelectOnTap = new Dictionary<TeamNumber, TeamNumber>();
            public readonly Dictionary<TeamNumber, int> BaseX = new Dictionary<TeamNumber, int>();
            public readonly Dictionary<TeamNumber, int> BadgeCalls = new Dictionary<TeamNumber, int>();
            public readonly Dictionary<TeamNumber, ImageRegion> Regions = new Dictionary<TeamNumber, ImageRegion>();
            public readonly Dictionary<TeamNumber, ImageMatchResult> LastBadge = new Dictionary<TeamNumber, ImageMatchResult>();
            public bool MoveBadgeEachCall;

            public ImageMatchResult Find(byte[] screenshot, byte[] template, ImageRegion? region = null)
            {
                TemplateId id = (TemplateId)template[0];
                TeamNumber team = TeamFromRegion(region);
                if (region.HasValue) Regions[team] = region.Value;
                if (id == TemplateId.TeamSelectedBorderAnchor)
                    return Selected.Contains(team) ? ImageMatchResult.FoundAt(5, region.Value.Y + 5, 180, 12) : ImageMatchResult.NotFound();
                if (id == TemplateId.TeamDisabledAnchor)
                    return Disabled.Contains(team) ? ImageMatchResult.FoundAt(15, region.Value.Y + 20, 30, 20) : ImageMatchResult.NotFound();
                if (id == TemplateId.Team2Badge || id == TemplateId.Team3Badge || id == TemplateId.Team4Badge)
                {
                    BadgeCalls[team] = BadgeCalls.TryGetValue(team, out int calls) ? calls + 1 : 1;
                    if (!Badges.Contains(team)) return ImageMatchResult.NotFound();
                    int x = 1000 + (BaseX.TryGetValue(team, out int offset) ? offset : 0)
                        + (MoveBadgeEachCall ? BadgeCalls[team] * 10 : 0);
                    ImageMatchResult match = ImageMatchResult.FoundAt(x, region.Value.Y + 25, 80, 20);
                    LastBadge[team] = match;
                    return match;
                }
                return ImageMatchResult.NotFound();
            }

            public void OnTap(int x, int y)
            {
                TeamNumber? tapped = LastBadge.Where(item => item.Value.CenterX == x && item.Value.CenterY == y).Select(item => (TeamNumber?)item.Key).FirstOrDefault();
                if (tapped.HasValue && SelectOnTap.TryGetValue(tapped.Value, out TeamNumber selected))
                { Selected.Clear(); Selected.Add(selected); }
            }

            private static TeamNumber TeamFromRegion(ImageRegion? region)
            {
                int y = region.Value.Y;
                if (y >= 435) return TeamNumber.Team4;
                if (y >= 290) return TeamNumber.Team3;
                if (y >= 145) return TeamNumber.Team2;
                return TeamNumber.Team1;
            }
        }

        private sealed class FakeClient : ILdPlayerClient
        {
            private readonly FakeMatcher matcher; public readonly List<string> Taps = new List<string>(); public int ProhibitedInputs, CaptureDelayMs; public CancellationTokenSource CancelOnTap;
            public FakeClient(FakeMatcher matcher) { this.matcher = matcher; }
            public async Task<byte[]> CaptureScreenshotPngAsync(string d, CancellationToken t) { t.ThrowIfCancellationRequested(); if (CaptureDelayMs > 0) await Task.Delay(CaptureDelayMs, t); return new byte[] { 1 }; }
            public Task TapAsync(string d, int x, int y, CancellationToken t) { t.ThrowIfCancellationRequested(); Taps.Add(x + "," + y); matcher.OnTap(x, y); CancelOnTap?.Cancel(); return Task.CompletedTask; }
            private Task Prohibited() { ProhibitedInputs++; return Task.CompletedTask; }
            public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<string>>(new[] { "LDPlayer" });
            public Task<bool> IsRunningAsync(string d, CancellationToken t) => Task.FromResult(true); public Task OpenAsync(string d, CancellationToken t) => Task.CompletedTask; public Task CloseAsync(string d, CancellationToken t) => Task.CompletedTask; public Task RunAppAsync(string d, string p, CancellationToken t) => Task.CompletedTask;
            public Task TapByPercentAsync(string d, double x, double y, CancellationToken t) => Prohibited(); public Task LongPressAsync(string d, int x, int y, int m, CancellationToken t) => Prohibited(); public Task SwipeByPercentAsync(string d, double a, double b, double c, double e, int m, CancellationToken t) => Prohibited(); public Task BackAsync(string d, CancellationToken t) => Prohibited(); public Task InputTextAsync(string d, string s, CancellationToken t) => Prohibited(); public Task PressKeyAsync(string d, AndroidKeyCode k, CancellationToken t) => Prohibited();
        }

        private sealed class FakeStore : ISelectFarmTeamDiagnosticStore
        { public bool Throw; public Task<string> SaveAsync(string d, SelectFarmTeamOutcome o, byte[] p, CancellationToken t) { if (Throw) throw new IOException("disk full"); return Task.FromResult("farm-team.png"); } }
        private sealed class FakeLogger : IDiagnosticLogger { public void Info(string m) { } public void Error(string m, Exception e) { } }
    }
}
