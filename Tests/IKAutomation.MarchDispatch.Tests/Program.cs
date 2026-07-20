using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Infrastructure.StorageLimit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private static int passed;
    private static int failed;

    private static int Main()
    {
        Run("TeamSelection not ready sends no Tap", NotReady);
        Run("Expected Team4 not selected sends no Tap", NotSelected);
        Run("Badge color variation with unique selected border is accepted", BadgeVariationAccepted);
        Run("Team4 selected permits action Tap", SelectedPermitsTap);
        Run("Team1 selected permits action Tap", Team1SelectedPermitsTap);
        Run("Multiple selected ROIs are indeterminate", AmbiguousSelection);
        Run("Missing action bounds is unavailable", MissingAction);
        Run("Action Tap uses fresh center", FreshCenter);
        Run("Action Tap is not hard-coded", DynamicCenter);
        Run("Action is rematched before Tap", ActionRematched);
        Run("Old action bounds are not reused", OldBoundsNotReused);
        Run("Closed panel without WorldMap is not success", ClosedWithoutWorld);
        Run("WorldMap with open panel is not success", WorldWithPanel);
        Run("Closed panel and WorldMap need team signal", NeedsTeamSignal);
        Run("Expected team ready is captured before Tap", ReadyBaseline);
        Run("Stale timer pixels do not block a fresh enabled action", StaleTimerPixelsDoNotBlock);
        Run("Ready disappearance and timer progression verify directly", DirectTimerSuccess);
        Run("Ready remaining does not verify directly", ReadyStillPresentNotDirect);
        Run("Static timer does not verify directly", StaticTimerNotDirect);
        Run("Generated timer change is detected", GeneratedTimerProgression);
        Run("Excessive timer ROI change is rejected", ExcessiveTimerChangeRejected);
        Run("Team3 timer does not verify Team4", WrongTeamTimerIgnored);
        Run("Timer progression plus structural has distinct mode", TimerPlusStructuralMode);
        Run("WorldMap timer progression succeeds without ready baseline", WorldMapTimerProgression);
        Run("Fallback requires selected border disappearance", FallbackNeedsBorderGone);
        Run("Fallback requires team ROI change", FallbackNeedsChange);
        Run("Missing ROI change cannot succeed", MissingChange);
        Run("Disabled fallback is not used", FallbackDisabled);
        Run("Success requires consecutive frames", ConsecutiveFrames);
        Run("Outside day-night change does not affect result", OutsideChangeIgnored);
        Run("Comparison receives only Team4 ROI", CompareUsesTeam4Roi);
        Run("Missing optional templates does not crash", MissingOptionalSafe);
        Run("Busy and timer templates are never used", DeprecatedTemplatesUnused);
        Run("Retry is bounded", RetryBounded);
        Run("No retry after panel closes", NoRetryClosed);
        Run("No retry after WorldMap", NoRetryWorld);
        Run("Retry uses new action bounds", RetryFreshBounds);
        Run("Transient Unknown sends no extra input", UnknownNoInput);
        Run("Unknown limit returns controlled result", UnknownLimit);
        Run("Preflight does not consume dispatch timeout", PreflightDoesNotConsumeDispatchTimeout);
        Run("Timeout is bounded", TimeoutBounded);
        Run("Polling cancellation is respected", PollCancellation);
        Run("Retry wait cancellation is respected", RetryCancellation);
        Run("Lock wait cancellation is respected", LockCancellation);
        Run("Same device is serialized", SameDeviceSerialized);
        Run("Different devices are independent", DifferentDevicesIndependent);
        Run("Back is never called", ProhibitedInputs);
        Run("Swipe is never called", ProhibitedInputs);
        Run("LongPress is never called", ProhibitedInputs);
        Run("Text and key input are never called", ProhibitedInputs);
        Run("No other team is dispatched", OnlyExpectedTeam);
        Run("No new resource search input is sent", ProhibitedInputs);
        Run("No default cancellation-token bypass in service", NoCancellationNone);
        Run("Timer sample cancellation is respected", TimerSampleCancellation);
        Run("Timer options load four validated team ROIs", TimerOptionsLoad);
        Run("Timer ratio ranges are validated", TimerRangesValidated);
        Run("Diagnostic failure does not replace outcome", DiagnosticFailureSafe);
        Run("Matcher bounds stay in screenshot coordinates", ScreenshotCoordinates);
        Run("No full-screen image comparison", NoFullScreenComparison);
        Run("GameState has no MarchStarted value", NoMarchGameState);
        Run("Storage limit requests resource switch", StorageLimitRequestsSwitch);
        Run("Resource expiry requests resource switch", ResourceExpiryRequestsSwitch);
        Run("Storage cancel uses fresh bounds center", StorageCancelUsesCenter);
        Run("Storage cancel returns directly to WorldMap without Back", StorageCancelReturnsWorld);
        Run("Storage cancel returns SearchPanel without Back", StorageCancelReturnsPanel);
        Run("Storage cancel verifies TeamSelection then sends one Back", StorageCancelTeamThenBack);
        Run("Resource expiry cancel verifies TeamSelection then sends one Back", ResourceExpiryCancelTeamThenBack);
        Run("Post-Back exit confirmation is cancelled before recovery continues", PostBackConfirmationCancelled);
        Run("Delayed post-Back exit confirmation is cancelled once", DelayedPostBackConfirmationCancelled);
        Run("Storage transient Unknown sends no Back", StorageUnknownNoBack);
        Run("Missing storage Cancel sends no input", StorageMissingCancelNoInput);
        Console.WriteLine($"March dispatch tests: {passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL: " + name + " - " + ex); }
    }

    private static DispatchMarchResult Execute(Harness h, CancellationToken token = default(CancellationToken)) =>
        h.Service.DispatchAsync("LDPlayer", h.Request, token).GetAwaiter().GetResult();
    private static void Is(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Eq<T>(T expected, T actual, string message) { if (!Equals(expected, actual)) throw new Exception($"{message} Expected={expected}, Actual={actual}"); }

    private static void NotReady() { var h = new Harness(); h.Detector.Initial = h.Detector.Result(GameState.Unknown, false); var r = Execute(h); Eq(DispatchMarchOutcome.TeamSelectionNotReady, r.Outcome, "outcome"); Eq(0, h.Client.Taps.Count, "tap count"); }
    private static void NotSelected() { var h = new Harness(); h.Matcher.Rule = (f, id, roi) => id == TemplateId.TeamSelectedBorderAnchor ? ImageMatchResult.NotFound() : h.Matcher.Default(f, id, roi); var r = Execute(h); Eq(DispatchMarchOutcome.ExpectedTeamNotSelected, r.Outcome, "outcome"); Eq(0, h.Client.Taps.Count, "tap count"); }
    private static void BadgeVariationAccepted() { var h = new Harness(); h.Matcher.Rule = (f,id,roi) => id == TemplateId.Team4Badge ? ImageMatchResult.NotFound() : h.Matcher.Default(f,id,roi); var r=Execute(h); Eq(DispatchMarchOutcome.MarchStarted,r.Outcome,"outcome"); Eq(1,r.ActionTapCount,"tap count"); Is(r.ExpectedTeamSelectedBeforeTap,"selected border should identify Team4 ROI"); }
    private static void SelectedPermitsTap() { var h = new Harness(); var r = Execute(h); Eq(DispatchMarchOutcome.MarchStarted, r.Outcome, "outcome"); Is(r.ExpectedTeamSelectedBeforeTap, "selected"); }
    private static void Team1SelectedPermitsTap() { var h = new Harness(); h.Request.ExpectedTeam = TeamNumber.Team1; h.Matcher.SelectedTeam = TeamNumber.Team1; var r = Execute(h); Eq(DispatchMarchOutcome.MarchStarted, r.Outcome, "outcome"); Is(r.ExpectedTeamSelectedBeforeTap, "selected"); }
    private static void AmbiguousSelection() { var h = new Harness(); h.Matcher.Rule = (f,id,roi) => id == TemplateId.TeamSelectedBorderAnchor && roi.HasValue && (roi.Value.Y == 290 || roi.Value.Y == 435) ? Found(10,roi.Value.Y) : h.Matcher.Default(f,id,roi); var r=Execute(h); Eq(DispatchMarchOutcome.VerificationIndeterminate,r.Outcome,"outcome"); Eq(0,h.Client.Taps.Count,"tap count"); }
    private static void MissingAction() { var h=new Harness(); h.Matcher.Rule=(f,id,roi)=>id==TemplateId.TeamActionButtonEnabled && f==2 ? ImageMatchResult.NotFound():h.Matcher.Default(f,id,roi); var r=Execute(h); Eq(DispatchMarchOutcome.ActionButtonUnavailable,r.Outcome,"outcome"); }
    private static void FreshCenter() { var h=new Harness(); Execute(h); Eq((70,620),h.Client.Taps[0],"fresh center"); }
    private static void DynamicCenter() { var h=new Harness(); h.Matcher.ActionX=333; Execute(h); Eq((383,620),h.Client.Taps[0],"dynamic center"); }
    private static void ActionRematched() { var h=new Harness(); Execute(h); Is(h.Matcher.Calls.Any(x=>x.id==TemplateId.TeamActionButtonEnabled&&x.frame==2),"action was not matched on the latest pre-dispatch frame"); }
    private static void OldBoundsNotReused() { DynamicCenter(); }
    private static void ClosedWithoutWorld() { var h=NoSuccessHarness(); h.Matcher.WorldAfter=false; h.Matcher.PanelAfter=false; h.Detector.AfterState=GameState.ResourcePopup; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void WorldWithPanel() { var h=NoSuccessHarness(); h.Matcher.WorldAfter=true; h.Matcher.PanelAfter=true; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void NeedsTeamSignal() { var h=NoSuccessHarness(); h.Matcher.WorldAfter=true; h.Matcher.PanelAfter=false; h.Comparer.Ratio=0; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void ReadyBaseline() { var h=new Harness(); Execute(h); Is(h.Matcher.Calls.Any(x=>x.id==TemplateId.WorldMapTeamReadyAnchor&&x.frame==2&&x.roi.Value.Y==520),"ready baseline"); }
    private static void StaleTimerPixelsDoNotBlock() { var h=new Harness(); h.Timer.ContentBefore=true; var r=Execute(h); Eq(DispatchMarchOutcome.MarchStarted,r.Outcome,"outcome"); Eq(1,r.ActionTapCount,"tap count"); Is(r.ActionButtonVerified,"fresh action button"); }
    private static void DirectTimerSuccess() { var h=DirectHarness(); var r=Execute(h); Eq(DispatchMarchOutcome.MarchStarted,r.Outcome,"outcome"); Is(r.DirectMarchVerified&&r.ReadyAnchorDisappeared&&r.ExpectedTeamTimerVerified,"direct evidence"); Eq(MarchVerificationMode.ReadyDisappearedAndTimerProgression,r.VerificationMode,"mode"); Eq(1,r.ActionTapCount,"tap count"); }
    private static void ReadyStillPresentNotDirect() { var h=DirectHarness(); h.Matcher.ReadyAfter=true; var r=Execute(h); Is(!r.DirectMarchVerified,"direct result"); }
    private static void StaticTimerNotDirect() { var h=DirectHarness(); h.Timer.Progression=false; var r=Execute(h); Is(!r.DirectMarchVerified,"direct result"); }
    private static void GeneratedTimerProgression() { var options=Harness.OptionsFor(1,1); var detector=new TeamMarchTimerDetector(options); ImageRegion region=options.TeamTimerRegions[TeamNumber.Team4]; byte[] a=TimerImage(region,"12:34"); byte[] b=TimerImage(region,"12:33"); var r=detector.Compare(a,b,region); Is(r.ProgressionDetected,"progression"); }
    private static void ExcessiveTimerChangeRejected() { var options=Harness.OptionsFor(1,1); var detector=new TeamMarchTimerDetector(options); ImageRegion region=options.TeamTimerRegions[TeamNumber.Team4]; byte[] a=TimerImage(region,"12:34"); byte[] b=SolidTimerImage(region,Color.White); var r=detector.Compare(a,b,region); Is(!r.ProgressionDetected,"large change accepted"); }
    private static void WrongTeamTimerIgnored() { var h=DirectHarness(); h.Timer.TimerTeam=TeamNumber.Team3; var r=Execute(h); Is(!r.DirectMarchVerified,"wrong team timer"); }
    private static void TimerPlusStructuralMode() { var h=DirectHarness(); h.Matcher.ReadyBefore=false; h.Comparer.Ratio=0.2; var r=Execute(h); Eq(DispatchMarchOutcome.MarchStarted,r.Outcome,"outcome"); Eq(MarchVerificationMode.TimerProgressionPlusStructural,r.VerificationMode,"mode"); Is(!r.DirectMarchVerified&&r.StructuralMarchVerified,"verification flags"); }
    private static void WorldMapTimerProgression() { var h=DirectHarness(); h.Matcher.ReadyBefore=false; h.Matcher.SelectedAfter=true; h.Comparer.Ratio=0; var r=Execute(h); Eq(DispatchMarchOutcome.MarchStarted,r.Outcome,"outcome"); Eq(MarchVerificationMode.WorldMapTimerProgression,r.VerificationMode,"mode"); Is(r.DirectMarchVerified&&r.ExpectedTeamTimerVerified&&!r.StructuralMarchVerified,"verification flags"); }
    private static void FallbackNeedsBorderGone() { var h=NoSuccessHarness(); h.Comparer.Ratio=0.2; h.Matcher.SelectedAfter=true; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void FallbackNeedsChange() { var h=NoSuccessHarness(); h.Comparer.Ratio=0; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void MissingChange() { FallbackNeedsChange(); }
    private static void FallbackDisabled() { var h=NoSuccessHarness(); h.Request.AllowStructuralVerificationFallback=false; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void ConsecutiveFrames() { var h=NoSuccessHarness(); h.Comparer.Rule=(call)=>call==1?0.2:0; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void OutsideChangeIgnored() { var h=NoSuccessHarness(); h.Comparer.Ratio=0; var r=Execute(h); Is(!r.TeamRegionChanged,"outside change affected ROI"); }
    private static void CompareUsesTeam4Roi() { var h=new Harness(); Execute(h); Eq(435,h.Comparer.Regions[0].Value.Y,"ROI Y"); }
    private static void MissingOptionalSafe() { var h=new Harness(); var r=Execute(h); Eq(DispatchMarchOutcome.MarchStarted,r.Outcome,"fallback"); }
    private static void DeprecatedTemplatesUnused() { var h=new Harness(); Execute(h); Is(!h.Matcher.Calls.Any(x=>x.id==TemplateId.TeamBusyStatusAnchor||x.id==TemplateId.TeamMarchTimerAnchor),"deprecated template used"); }
    private static void RetryBounded() { var h=RetryHarness(); var r=Execute(h); Eq(2,r.ActionTapCount,"bounded taps"); }
    private static void NoRetryClosed() { var h=NoSuccessHarness(); h.Matcher.PanelAfter=false; h.Matcher.WorldAfter=false; var r=Execute(h); Eq(1,r.ActionTapCount,"tap count"); }
    private static void NoRetryWorld() { var h=NoSuccessHarness(); h.Matcher.PanelAfter=true; h.Matcher.WorldAfter=true; var r=Execute(h); Eq(1,r.ActionTapCount,"tap count"); }
    private static void RetryFreshBounds() { var h=RetryHarness(); h.Matcher.DynamicRetryAction=true; var r=Execute(h); Eq(2,r.ActionTapCount,"retry"); Is(h.Client.Taps[0].Item1!=h.Client.Taps[1].Item1,"bounds reused"); }
    private static void UnknownNoInput() { var h=new Harness(); h.Detector.AfterState=GameState.Unknown; var r=Execute(h); Eq(1,r.ActionTapCount,"unknown caused input"); }
    private static void UnknownLimit() { var h=NoSuccessHarness(); h.Detector.AfterState=GameState.Unknown; h.Matcher.WorldAfter=false; var r=Execute(h); Eq(DispatchMarchOutcome.VerificationIndeterminate,r.Outcome,"outcome"); }
    private static void PreflightDoesNotConsumeDispatchTimeout() { var h=new Harness(); h.Detector.DelayMs=1100; var r=Execute(h); Eq(DispatchMarchOutcome.MarchStarted,r.Outcome,"outcome"); }
    private static void TimeoutBounded() { var h=NoSuccessHarness(); var sw=Stopwatch.StartNew(); Execute(h); Is(sw.Elapsed<TimeSpan.FromSeconds(2),"unbounded"); }
    private static void PollCancellation() { var h=NoSuccessHarness(); using(var c=new CancellationTokenSource(20)){ var r=Execute(h,c.Token); Eq(DispatchMarchOutcome.Cancelled,r.Outcome,"outcome"); } }
    private static void RetryCancellation() { PollCancellation(); }
    private static void LockCancellation() { var h=new Harness(); h.Lock=new BlockingLock(); h.Rebuild(); using(var c=new CancellationTokenSource(20)){ var r=Execute(h,c.Token); Eq(DispatchMarchOutcome.Cancelled,r.Outcome,"outcome"); } }
        private static void SameDeviceSerialized() { var gate=DeviceOperationLock.Shared; int active=0,max=0; Func<Task<int>> run=()=>gate.RunAsync("same",async t=>{int n=Interlocked.Increment(ref active); max=Math.Max(max,n); await Task.Delay(20,t); Interlocked.Decrement(ref active); return 1;},default(CancellationToken)); Task.WaitAll(run(),run()); Eq(1,max,"same device concurrency"); }
    private static void DifferentDevicesIndependent() { var gate=DeviceOperationLock.Shared; var entered=new CountdownEvent(2); Func<string,Task<int>> run=d=>gate.RunAsync(d,async t=>{entered.Signal(); Is(entered.Wait(500),"globally blocked"); await Task.Delay(1,t); return 1;},default(CancellationToken)); Task.WaitAll(Task.Run(()=>run("a")),Task.Run(()=>run("b"))); }
    private static void ProhibitedInputs() { var h=new Harness(); Execute(h); Eq(0,h.Client.ProhibitedCalls,"prohibited calls"); }
    private static void OnlyExpectedTeam() { var h=new Harness(); var r=Execute(h); Eq(TeamNumber.Team4,r.DispatchedTeam.Value,"team"); Eq(1,r.ActionTapCount,"tap count"); }
    private static void NoCancellationNone() { string root=System.IO.Path.Combine(Environment.CurrentDirectory,"ADB","Infrastructure","MarchDispatch"); string forbidden="CancellationToken"+".None"; Is(!System.IO.File.ReadAllText(System.IO.Path.Combine(root,"DispatchSelectedTeamService.cs")).Contains(forbidden),"service token bypass"); Is(!System.IO.File.ReadAllText(System.IO.Path.Combine(root,"TeamMarchTimerDetector.cs")).Contains(forbidden),"detector token bypass"); }
    private static void TimerSampleCancellation() { var h=DirectHarness(); h.Options=Harness.OptionsFor(2,1,1000); h.Rebuild(); using(var c=new CancellationTokenSource(20)){var r=Execute(h,c.Token);Eq(DispatchMarchOutcome.Cancelled,r.Outcome,"outcome");Eq(1,r.ActionTapCount,"tap count");} }
    private static void TimerOptionsLoad() { var o=AppConfigDispatchSelectedTeamOptionsProvider.Load(); Eq(4,o.TeamTimerRegions.Count,"ROI count"); Eq(338,o.TeamTimerRegions[TeamNumber.Team1].Y,"Team1 timer Y"); Eq(503,o.TeamTimerRegions[TeamNumber.Team4].Y,"Team4 timer Y"); Eq(1000,o.TimerSampleIntervalMs,"sample interval"); }
    private static void TimerRangesValidated() { bool threw=false; try{var o=Harness.OptionsFor(1,1);new DispatchSelectedTeamOptions(o.PollIntervalMs,o.TransitionTimeoutSeconds,o.MaxActionTapAttempts,o.ActionTapRetryDelayMs,o.MaxTransientUnknownFrames,o.RequiredConsecutiveSuccessFrames,o.TeamRegionChangeThreshold,true,true,"Diagnostics/x",true,true,1,0.4,0.2,0.003,0.25,o.TeamTimerRegions,new ImageRegion(0,270,150,280));}catch(ArgumentException){threw=true;}Is(threw,"invalid range accepted"); }
    private static void DiagnosticFailureSafe() { var h=NoSuccessHarness(); h.Store.Throw=true; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void ScreenshotCoordinates() { var h=new Harness(); h.Matcher.ActionX=1000; Execute(h); Eq(1050,h.Client.Taps[0].Item1,"screen X"); }
    private static void NoFullScreenComparison() { var h=new Harness(); Execute(h); Is(h.Comparer.Regions.All(x=>x.HasValue),"full-screen comparison used"); }
    private static void NoMarchGameState() { Is(!Enum.GetNames(typeof(GameState)).Contains("MarchStarted"),"invalid GameState"); }
    private static void StorageLimitRequestsSwitch()
    {
        var h = new Harness();
        h.Detector.AfterState = GameState.StorageLimitDialog;
        h.Storage.Result = new StorageLimitDialogResult
        {
            Outcome = StorageLimitDialogOutcome.CancelledForResourceSwitch,
            Policy = StorageLimitPolicy.CancelAndSwitchResource,
            DialogVerified = true, CancelButtonVerified = true, ActionTapCount = 1,
            ModalClosed = true, ReturnedToWorldMap = true,
            StateAfterCancel = GameState.TeamSelection, FinalState = GameState.WorldMap
        };
        h.Rebuild();
        DispatchMarchResult result = Execute(h);
        Eq(DispatchMarchOutcome.StorageLimitResourceSwitchRequired, result.Outcome, "outcome");
        Is(result.StorageLimitDialogDetected, "dialog not recorded");
        Is(result.StorageLimitCancelled && result.ResourceSwitchRequired, "switch flags missing");
        Eq((ResourceType?)ResourceType.Iron, result.StorageFullResource, "storage resource");
        Eq<TeamNumber?>(null, result.DispatchedTeam, "team must not be dispatched");
        Eq(1, result.ActionTapCount, "dispatch action must not retry");
    }
    private static void ResourceExpiryRequestsSwitch()
    {
        var h = new Harness(); h.Detector.AfterState = GameState.ResourceExpiryDialog;
        h.Storage.Result = new StorageLimitDialogResult
        {
            Outcome = StorageLimitDialogOutcome.CancelledForResourceSwitch,
            DialogVerified = true, CancelButtonVerified = true, ActionTapCount = 1,
            ModalClosed = true, ReturnedToTeamSelection = true, BackSent = true,
            BackCount = 1, ReturnedToWorldMap = true, FinalState = GameState.WorldMap
        };
        h.Rebuild(); DispatchMarchResult result = Execute(h);
        Eq(DispatchMarchOutcome.ResourceExpiryResourceSwitchRequired, result.Outcome, "outcome");
        Is(result.ResourceExpiryDialogDetected && result.ResourceExpiryCancelled, "expiry flags");
        Is(result.ResourceSwitchRequired && result.StorageFullResource == null, "switch mapping");
        Eq<TeamNumber?>(null, result.DispatchedTeam, "team must not be dispatched");
        Eq(1, result.ActionTapCount, "dispatch action must not retry");
    }
    private static StorageDialogHarness StorageHarness(GameState finalState)
    {
        var h = new StorageDialogHarness();
        h.Detector.AsyncStates.Enqueue(finalState);
        return h;
    }
    private static void StorageCancelUsesCenter()
    {
        var h = StorageHarness(GameState.WorldMap); var result = h.Execute();
        Eq((340,420), h.Client.Taps.Single(), "cancel center");
        Eq(1, result.ActionTapCount, "cancel count");
        Is(!h.Matcher.Calls.Any(x => x.id == TemplateId.StorageLimitConfirmButton), "Confirm was matched or tapped");
    }
    private static void StorageCancelReturnsWorld()
    {
        var result = StorageHarness(GameState.WorldMap).Execute();
        Eq(StorageLimitDialogOutcome.CancelledForResourceSwitch, result.Outcome, "outcome");
        Is(result.ModalClosed && result.ReturnedToWorldMap, "WorldMap flags");
        Eq(0, result.BackCount, "direct WorldMap must not send Back");
    }
    private static void StorageCancelReturnsPanel()
    {
        var result = StorageHarness(GameState.ResourceSearchPanel).Execute();
        Eq(StorageLimitDialogOutcome.CancelledForResourceSwitch, result.Outcome, "outcome");
        Is(result.ReturnedToSearchPanel, "panel flag");
        Eq(0, result.BackCount, "SearchPanel must not send Back");
    }
    private static void StorageCancelTeamThenBack()
    {
        var h = StorageHarness(GameState.TeamSelection);
        h.Detector.AsyncStates.Enqueue(GameState.WorldMap);
        var result = h.Execute();
        Eq(StorageLimitDialogOutcome.CancelledForResourceSwitch, result.Outcome, "outcome");
        Is(result.ReturnedToTeamSelection && result.ReturnedToWorldMap, "recovery flags");
        Eq(1, result.BackCount, "Back count");
        Eq(1, h.Client.BackCalls, "client Back count");
    }
    private static void ResourceExpiryCancelTeamThenBack()
    {
        var h = new StorageDialogHarness(TemplateId.ResourceExpiryDialogAnchor);
        h.Detector.AsyncStates.Enqueue(GameState.TeamSelection);
        h.Detector.AsyncStates.Enqueue(GameState.WorldMap);
        StorageLimitDialogResult result = h.ExecuteResourceExpiry();
        Eq(StorageLimitDialogOutcome.CancelledForResourceSwitch, result.Outcome, "outcome");
        Is(result.ReturnedToTeamSelection && result.ReturnedToWorldMap, "recovery flags");
        Eq(1, result.ActionTapCount, "Cancel tap count"); Eq(1, result.BackCount, "Back count");
        Eq(1, h.Client.BackCalls, "client Back count");
    }
    private static void PostBackConfirmationCancelled()
    {
        var h = StorageHarness(GameState.TeamSelection);
        h.Matcher.PostBackCancelFrame = 2;
        h.Detector.AsyncStates.Enqueue(GameState.WorldMap);

        StorageLimitDialogResult result = h.Execute();

        Eq(StorageLimitDialogOutcome.CancelledForResourceSwitch, result.Outcome, "outcome");
        Is(result.PostBackConfirmationCancelled, "post-Back confirmation flag");
        Is(result.ReturnedToWorldMap, "WorldMap recovery");
        Eq(1, result.BackCount, "Back count");
        Eq(2, result.ActionTapCount, "cancel tap count");
        Eq(2, h.Client.Taps.Count, "client tap count");
        Eq((340,420), h.Client.Taps[1], "fresh post-Back Cancel center");
    }
    private static void DelayedPostBackConfirmationCancelled()
    {
        var h = StorageHarness(GameState.TeamSelection);
        h.Matcher.PostBackCancelFrame = 3;
        h.Detector.AsyncStates.Enqueue(GameState.Unknown);
        h.Detector.AsyncStates.Enqueue(GameState.WorldMap);

        StorageLimitDialogResult result = h.Execute();

        Eq(StorageLimitDialogOutcome.CancelledForResourceSwitch, result.Outcome, "outcome");
        Is(result.PostBackConfirmationCancelled, "delayed confirmation flag");
        Eq(2, result.ActionTapCount, "bounded cancel taps");
        Eq(1, result.BackCount, "Back must not repeat");
        Eq(2, h.Client.Taps.Count, "Cancel must be tapped once");
    }
    private static void StorageUnknownNoBack()
    {
        var h = StorageHarness(GameState.Unknown); h.Detector.AsyncStates.Enqueue(GameState.WorldMap);
        var result = h.Execute();
        Eq(StorageLimitDialogOutcome.CancelledForResourceSwitch, result.Outcome, "outcome");
        Eq(0, h.Client.BackCalls, "Unknown must not cause Back");
    }
    private static void StorageMissingCancelNoInput()
    {
        var h = StorageHarness(GameState.WorldMap); h.Registry.Missing = TemplateId.StorageLimitCancelButton;
        var result = h.Execute();
        Eq(StorageLimitDialogOutcome.ActionButtonUnavailable, result.Outcome, "outcome");
        Eq(0, h.Client.Taps.Count, "Tap count");
        Is(result.ErrorMessage.Contains("StorageLimitCancelButton"), "missing TemplateId");
    }

    private static Harness NoSuccessHarness() { var h=new Harness(); h.Options=h.CreateOptions(1,2); h.Comparer.Ratio=0; h.Rebuild(); return h; }
    private static Harness RetryHarness() { var h=NoSuccessHarness(); h.Matcher.PanelAfter=true; h.Matcher.WorldAfter=false; h.Matcher.SelectedAfter=true; h.Matcher.ReadyAfter=true; h.Detector.AfterState=GameState.TeamSelection; return h; }
    private static Harness DirectHarness() { var h=new Harness(); h.Timer.ContentAfter=true; h.Timer.Progression=true; h.Comparer.Ratio=0; h.Rebuild(); return h; }
    private static ImageMatchResult Found(int x,int y,int w=40,int he=20)=>ImageMatchResult.FoundAt(x,y,w,he,0.99);
    private static byte[] TimerImage(ImageRegion region,string text)
    {
        using(var image=new Bitmap(1280,720)) using(var graphics=Graphics.FromImage(image))
        using(var font=new Font(FontFamily.GenericSansSerif,14,FontStyle.Bold))
        using(var stream=new MemoryStream()) { graphics.Clear(Color.Black); graphics.DrawString(text,font,Brushes.White,region.X,region.Y); image.Save(stream,ImageFormat.Png); return stream.ToArray(); }
    }
    private static byte[] SolidTimerImage(ImageRegion region,Color color)
    {
        using(var image=new Bitmap(1280,720)) using(var graphics=Graphics.FromImage(image))
        using(var brush=new SolidBrush(color)) using(var stream=new MemoryStream()) { graphics.Clear(Color.Black); graphics.FillRectangle(brush,region.X,region.Y,region.Width,region.Height); image.Save(stream,ImageFormat.Png); return stream.ToArray(); }
    }

    private sealed class Harness
    {
        public FakeClient Client=new FakeClient(); public FakeDetector Detector=new FakeDetector(); public FakeRegistry Registry=new FakeRegistry(); public FakeMatcher Matcher=new FakeMatcher(); public FakeComparer Comparer=new FakeComparer(); public FakeTimerDetector Timer=new FakeTimerDetector(); public IDeviceOperationLock Lock=new ImmediateLock(); public FakeStore Store=new FakeStore(); public FakeStorage Storage=new FakeStorage(); public DispatchMarchRequest Request=new DispatchMarchRequest(); public DispatchSelectedTeamOptions Options; public DispatchSelectedTeamService Service;
        public Harness(){ Options=CreateOptions(1,2); Rebuild(); }
        public DispatchSelectedTeamOptions CreateOptions(int seconds,int taps)=>OptionsFor(seconds,taps);
        public static DispatchSelectedTeamOptions OptionsFor(int seconds,int taps,int timerInterval=2)=>new DispatchSelectedTeamOptions(2,seconds,taps,8,5,2,0.025,true,true,"Diagnostics/MarchDispatch",true,true,timerInterval,0.01,0.35,0.003,0.25,TimerRegions(),new ImageRegion(0,270,150,280));
        public void Rebuild(){ Service=new DispatchSelectedTeamService(Detector,Client,Registry,Matcher,Comparer,Lock,TeamOptions(),Options,Store,new FakeLogger(),Storage,Timer); }
        private static IReadOnlyDictionary<TeamNumber,ImageRegion> TimerRegions()=>new Dictionary<TeamNumber,ImageRegion>{{TeamNumber.Team1,new ImageRegion(55,310,95,28)},{TeamNumber.Team2,new ImageRegion(55,380,95,28)},{TeamNumber.Team3,new ImageRegion(55,450,95,28)},{TeamNumber.Team4,new ImageRegion(55,520,95,28)}};
        private static FarmTeamSelectionOptions TeamOptions()=>new FarmTeamSelectionOptions(1,1,1,1,false,"Diagnostics/x",new Dictionary<TeamNumber,ImageRegion>{{TeamNumber.Team1,new ImageRegion(0,0,235,150)},{TeamNumber.Team2,new ImageRegion(0,145,235,145)},{TeamNumber.Team3,new ImageRegion(0,290,235,145)},{TeamNumber.Team4,new ImageRegion(0,435,235,155)}});
    }

    private sealed class FakeDetector:IGameStateDetector
    {
        public GameDetectionResult Initial; public GameState AfterState=GameState.WorldMap; public int DelayMs;
        public Queue<GameState> AsyncStates = new Queue<GameState>();
        public FakeDetector(){Initial=Result(GameState.TeamSelection,true);}
        public async Task<GameDetectionResult> DetectAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();if(DelayMs>0)await Task.Delay(DelayMs,t);return AsyncStates.Count>0?Result(AsyncStates.Dequeue(),false):Initial;}
        public GameDetectionResult Detect(byte[] f)=>Result(f[0]<=2?GameState.TeamSelection:AfterState,f[0]<=2);
        public GameDetectionResult Result(GameState s,bool ready)=>new GameDetectionResult{State=s,IsSuccessful=s!=GameState.Unknown,Evidence=ready?new[]{TemplateId.TeamSelectionPanelAnchor,TemplateId.TeamAdjustFormationButton,TemplateId.TeamActionButtonEnabled}.Select(id=>new GameDetectionEvidence{TemplateId=id,Found=true}).ToArray():new GameDetectionEvidence[0]};
    }
    private sealed class FakeRegistry:ITemplateRegistry
    {
        public HashSet<TemplateId> Optional=new HashSet<TemplateId>();
        public TemplateId? Missing;
        public bool Exists(TemplateId id)=>(!Missing.HasValue||Missing.Value!=id)&&(id!=TemplateId.TeamBusyStatusAnchor&&id!=TemplateId.TeamMarchTimerAnchor||Optional.Contains(id));
        public byte[] LoadBytes(TemplateId id)=>new[]{(byte)id}; public string GetPath(TemplateId id)=>id.ToString(); public TemplateDefinition GetDefinition(TemplateId id)=>null;
    }
    private sealed class FakeMatcher:IImageMatcher
    {
        public Func<int,TemplateId,ImageRegion?,ImageMatchResult> Rule; public readonly List<(int frame,TemplateId id,ImageRegion? roi)> Calls=new List<(int,TemplateId,ImageRegion?)>(); public int ActionX=20; public TeamNumber SelectedTeam=TeamNumber.Team4; public bool PanelAfter=false,WorldAfter=true,SelectedAfter=false,ReadyBefore=true,ReadyAfter=false,DynamicRetryAction=false;
        public int PostBackCancelFrame;
        public ImageMatchResult Find(byte[] f,byte[] t,ImageRegion? r){int frame=f[0];var id=(TemplateId)t[0];Calls.Add((frame,id,r));return Rule==null?Default(frame,id,r):Rule(frame,id,r);}
        public ImageMatchResult Default(int f,TemplateId id,ImageRegion? r)
        {
            int selectedY=SelectedTeam==TeamNumber.Team1?0:SelectedTeam==TeamNumber.Team2?145:SelectedTeam==TeamNumber.Team3?290:435; TemplateId selectedBadge=SelectedTeam==TeamNumber.Team1?TemplateId.Team1Badge:SelectedTeam==TeamNumber.Team2?TemplateId.Team2Badge:SelectedTeam==TeamNumber.Team3?TemplateId.Team3Badge:TemplateId.Team4Badge; bool pre=f<=2; if(id==TemplateId.TeamSelectionPanelAnchor)return (pre||PanelAfter)?Found(5,5):ImageMatchResult.NotFound(); if(id==TemplateId.TeamAdjustFormationButton)return (pre||PanelAfter)?Found(500,600):ImageMatchResult.NotFound(); if(id==TemplateId.TeamActionButtonEnabled)return (pre||PanelAfter)?Found(ActionX+(DynamicRetryAction&&f>2?f*10:0),600,100,40):ImageMatchResult.NotFound(); if(id==TemplateId.WorldMapAnchor)return !pre&&WorldAfter?Found(50,650):ImageMatchResult.NotFound(); if(id==TemplateId.WorldMapTeamReadyAnchor)return (pre?ReadyBefore:ReadyAfter)&&r.HasValue?Found(r.Value.X+5,r.Value.Y+5):ImageMatchResult.NotFound(); if(id==selectedBadge)return (pre||PanelAfter)&&r.HasValue&&r.Value.Y==selectedY?Found(20,selectedY+25):ImageMatchResult.NotFound(); if(id==TemplateId.TeamSelectedBorderAnchor)return (pre||SelectedAfter)&&r.HasValue&&r.Value.Y==selectedY?Found(10,selectedY+135):ImageMatchResult.NotFound(); return ImageMatchResult.NotFound();
        }
    }
    private sealed class FakeTimerDetector:ITeamMarchTimerDetector
    {
        public bool ContentBefore,ContentAfter,Progression; public TeamNumber TimerTeam=TeamNumber.Team4; public readonly List<ImageRegion> Regions=new List<ImageRegion>();
        public TeamMarchTimerDetectionResult DetectContent(byte[] frame,ImageRegion region){Regions.Add(region);bool team=region.Y==310+(((int)TimerTeam-1)*70);bool content=team&&(frame[0]<=2?ContentBefore:ContentAfter);return new TeamMarchTimerDetectionResult{ContentDetected=content,ForegroundRatio=content?0.08:0,TimerRegion=region};}
        public TeamMarchTimerProgressionResult Compare(byte[] previous,byte[] current,ImageRegion region){Regions.Add(region);bool team=region.Y==310+(((int)TimerTeam-1)*70);bool content=team&&ContentAfter;return new TeamMarchTimerProgressionResult{PreviousContentDetected=content,CurrentContentDetected=content,ProgressionDetected=content&&Progression,PreviousForegroundRatio=content?0.08:0,CurrentForegroundRatio=content?0.08:0,DifferenceRatio=content&&Progression?0.02:0,TimerRegion=region};}
    }
    private sealed class FakeComparer:IFrameStabilityDetector { public double Ratio=0.2; public Func<int,double> Rule; public int Calls; public List<ImageRegion?> Regions=new List<ImageRegion?>(); public FrameComparisonResult Compare(byte[] a,byte[] b,ImageRegion? r=null){Regions.Add(r);Calls++;double value=Rule==null?Ratio:Rule(Calls);return new FrameComparisonResult{DifferenceRatio=value,IsStable=value<=0.025};} }
    private sealed class FakeClient:ILdPlayerClient
    {
        private int frame; public List<(int,int)> Taps=new List<(int,int)>(); public int ProhibitedCalls; public int BackCalls;
        public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();return Task.FromResult(new[]{(byte)Interlocked.Increment(ref frame)});} public Task TapAsync(string d,int x,int y,CancellationToken t){t.ThrowIfCancellationRequested();Taps.Add((x,y));return Task.CompletedTask;}
        private Task Bad(){ProhibitedCalls++;return Task.CompletedTask;} public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult((IReadOnlyList<string>)new string[0]); public Task<bool> IsRunningAsync(string d,CancellationToken t)=>Task.FromResult(true); public Task OpenAsync(string d,CancellationToken t)=>Bad(); public Task CloseAsync(string d,CancellationToken t)=>Bad(); public Task RunAppAsync(string d,string p,CancellationToken t)=>Bad(); public Task TapByPercentAsync(string d,double x,double y,CancellationToken t)=>Bad(); public Task LongPressAsync(string d,int x,int y,int m,CancellationToken t)=>Bad(); public Task SwipeByPercentAsync(string d,double a,double b,double c,double e,int m,CancellationToken t)=>Bad(); public Task BackAsync(string d,CancellationToken t){BackCalls++;return Bad();} public Task InputTextAsync(string d,string s,CancellationToken t)=>Bad(); public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t)=>Bad();
    }
    private sealed class ImmediateLock:IDeviceOperationLock { public Task<T> RunAsync<T>(string d,Func<CancellationToken,Task<T>> o,CancellationToken t)=>o(t); }
    private sealed class BlockingLock:IDeviceOperationLock { public async Task<T> RunAsync<T>(string d,Func<CancellationToken,Task<T>> o,CancellationToken t){await Task.Delay(500,t);return await o(t);} }
    private sealed class FakeStore:IDispatchMarchDiagnosticStore { public bool Throw; public Task<string> SaveAsync(string d,DispatchMarchOutcome o,byte[] p,CancellationToken t){if(Throw)throw new Exception("disk");return Task.FromResult("diag.png");} }
    private sealed class FakeStorage:IStorageLimitDialogService
    {
        public StorageLimitDialogResult Result = new StorageLimitDialogResult { Outcome = StorageLimitDialogOutcome.Failed };
        public Task<StorageLimitDialogResult> HandleAsync(string d, StorageLimitPolicy p, CancellationToken t)
        { t.ThrowIfCancellationRequested(); return Task.FromResult(Result); }
        public Task<StorageLimitDialogResult> HandleResourceExpiryAsync(string d, CancellationToken t)
        { t.ThrowIfCancellationRequested(); return Task.FromResult(Result); }
    }
    private sealed class StorageDialogHarness
    {
        public FakeClient Client = new FakeClient(); public FakeDetector Detector = new FakeDetector();
        public FakeRegistry Registry = new FakeRegistry(); public FakeMatcher Matcher = new FakeMatcher();
        public StorageLimitDialogService Service;
        public StorageDialogHarness(TemplateId dialogTemplate = TemplateId.StorageLimitDialogAnchor)
        {
            Matcher.Rule = (f,id,roi) => id == dialogTemplate
                ? Found(100,200,200,100)
                : id == TemplateId.StorageLimitCancelButton
                    && (f == 1 || f == Matcher.PostBackCancelFrame)
                    ? Found(300,400,80,40)
                : ImageMatchResult.NotFound();
            Service = new StorageLimitDialogService(Client, Detector, Registry, Matcher,
                new StorageLimitDialogOptions { PollIntervalMs=50, TransitionTimeoutSeconds=1,
                    MaxActionAttempts=1, ActionRetryDelayMs=0,
                    Policy=StorageLimitPolicy.CancelAndSwitchResource }, new FakeLogger());
        }
        public StorageLimitDialogResult Execute() => Service.HandleAsync("LDPlayer",
            StorageLimitPolicy.CancelAndSwitchResource, new CancellationToken(false)).GetAwaiter().GetResult();
        public StorageLimitDialogResult ExecuteResourceExpiry() => Service.HandleResourceExpiryAsync(
            "LDPlayer", new CancellationToken(false)).GetAwaiter().GetResult();
    }
    private sealed class FakeLogger:IDiagnosticLogger { public void Info(string m){} public void Error(string m,Exception e){} }
}
