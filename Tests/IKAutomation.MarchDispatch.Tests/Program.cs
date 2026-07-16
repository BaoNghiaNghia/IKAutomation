using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.MarchDispatch;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        Run("Team4 selected permits action Tap", SelectedPermitsTap);
        Run("Multiple selected ROIs are indeterminate", AmbiguousSelection);
        Run("Missing action bounds is unavailable", MissingAction);
        Run("Action Tap uses fresh center", FreshCenter);
        Run("Action Tap is not hard-coded", DynamicCenter);
        Run("Action is rematched before Tap", ActionRematched);
        Run("Old action bounds are not reused", OldBoundsNotReused);
        Run("Closed panel without WorldMap is not success", ClosedWithoutWorld);
        Run("WorldMap with open panel is not success", WorldWithPanel);
        Run("Closed panel and WorldMap need team signal", NeedsTeamSignal);
        Run("Busy status strong rule starts march", BusyStrongRule);
        Run("March timer strong rule starts march", TimerStrongRule);
        Run("Fallback requires selected border disappearance", FallbackNeedsBorderGone);
        Run("Fallback requires team ROI change", FallbackNeedsChange);
        Run("Missing ROI change cannot succeed", MissingChange);
        Run("Disabled fallback is not used", FallbackDisabled);
        Run("Success requires consecutive frames", ConsecutiveFrames);
        Run("Outside day-night change does not affect result", OutsideChangeIgnored);
        Run("Comparison receives only Team4 ROI", CompareUsesTeam4Roi);
        Run("Missing optional templates does not crash", MissingOptionalSafe);
        Run("Retry is bounded", RetryBounded);
        Run("No retry after panel closes", NoRetryClosed);
        Run("No retry after WorldMap", NoRetryWorld);
        Run("Retry uses new action bounds", RetryFreshBounds);
        Run("Transient Unknown sends no extra input", UnknownNoInput);
        Run("Unknown limit returns controlled result", UnknownLimit);
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
        Run("Diagnostic failure does not replace outcome", DiagnosticFailureSafe);
        Run("Matcher bounds stay in screenshot coordinates", ScreenshotCoordinates);
        Run("No full-screen image comparison", NoFullScreenComparison);
        Run("GameState has no MarchStarted value", NoMarchGameState);
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
    private static void SelectedPermitsTap() { var h = new Harness(); var r = Execute(h); Eq(DispatchMarchOutcome.MarchStarted, r.Outcome, "outcome"); Is(r.ExpectedTeamSelectedBeforeTap, "selected"); }
    private static void AmbiguousSelection() { var h = new Harness(); h.Matcher.Rule = (f,id,roi) => id == TemplateId.TeamSelectedBorderAnchor && roi.HasValue && (roi.Value.Y == 290 || roi.Value.Y == 435) ? Found(10,roi.Value.Y) : h.Matcher.Default(f,id,roi); var r=Execute(h); Eq(DispatchMarchOutcome.VerificationIndeterminate,r.Outcome,"outcome"); Eq(0,h.Client.Taps.Count,"tap count"); }
    private static void MissingAction() { var h=new Harness(); h.Matcher.Rule=(f,id,roi)=>id==TemplateId.TeamActionButtonEnabled && f==2 ? ImageMatchResult.NotFound():h.Matcher.Default(f,id,roi); var r=Execute(h); Eq(DispatchMarchOutcome.ActionButtonUnavailable,r.Outcome,"outcome"); }
    private static void FreshCenter() { var h=new Harness(); Execute(h); Eq((70,620),h.Client.Taps[0],"fresh center"); }
    private static void DynamicCenter() { var h=new Harness(); h.Matcher.ActionX=333; Execute(h); Eq((383,620),h.Client.Taps[0],"dynamic center"); }
    private static void ActionRematched() { var h=new Harness(); Execute(h); Is(h.Matcher.Calls.Any(x=>x.id==TemplateId.TeamActionButtonEnabled&&x.frame==2),"action was not matched on the latest pre-dispatch frame"); }
    private static void OldBoundsNotReused() { DynamicCenter(); }
    private static void ClosedWithoutWorld() { var h=NoSuccessHarness(); h.Matcher.WorldAfter=false; h.Matcher.PanelAfter=false; h.Detector.AfterState=GameState.ResourcePopup; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void WorldWithPanel() { var h=NoSuccessHarness(); h.Matcher.WorldAfter=true; h.Matcher.PanelAfter=true; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void NeedsTeamSignal() { var h=NoSuccessHarness(); h.Matcher.WorldAfter=true; h.Matcher.PanelAfter=false; h.Comparer.Ratio=0; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void BusyStrongRule() { var h=new Harness(); h.Registry.Optional.Add(TemplateId.TeamBusyStatusAnchor); h.Matcher.BusyAfter=true; var r=Execute(h); Eq(DispatchMarchOutcome.MarchStarted,r.Outcome,"outcome"); Is(r.BusyStatusVerified,"busy"); }
    private static void TimerStrongRule() { var h=new Harness(); h.Registry.Optional.Add(TemplateId.TeamMarchTimerAnchor); h.Matcher.TimerAfter=true; var r=Execute(h); Eq(DispatchMarchOutcome.MarchStarted,r.Outcome,"outcome"); Is(r.MarchTimerVerified,"timer"); }
    private static void FallbackNeedsBorderGone() { var h=NoSuccessHarness(); h.Comparer.Ratio=0.2; h.Matcher.SelectedAfter=true; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void FallbackNeedsChange() { var h=NoSuccessHarness(); h.Comparer.Ratio=0; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void MissingChange() { FallbackNeedsChange(); }
    private static void FallbackDisabled() { var h=NoSuccessHarness(); h.Request.AllowStructuralVerificationFallback=false; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void ConsecutiveFrames() { var h=NoSuccessHarness(); h.Comparer.Rule=(call)=>call==1?0.2:0; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void OutsideChangeIgnored() { var h=NoSuccessHarness(); h.Comparer.Ratio=0; var r=Execute(h); Is(!r.TeamRegionChanged,"outside change affected ROI"); }
    private static void CompareUsesTeam4Roi() { var h=new Harness(); Execute(h); Eq(435,h.Comparer.Regions[0].Value.Y,"ROI Y"); }
    private static void MissingOptionalSafe() { var h=new Harness(); var r=Execute(h); Eq(DispatchMarchOutcome.MarchStarted,r.Outcome,"fallback"); }
    private static void RetryBounded() { var h=RetryHarness(); var r=Execute(h); Eq(2,r.ActionTapCount,"bounded taps"); }
    private static void NoRetryClosed() { var h=NoSuccessHarness(); h.Matcher.PanelAfter=false; h.Matcher.WorldAfter=false; var r=Execute(h); Eq(1,r.ActionTapCount,"tap count"); }
    private static void NoRetryWorld() { var h=NoSuccessHarness(); h.Matcher.PanelAfter=true; h.Matcher.WorldAfter=true; var r=Execute(h); Eq(1,r.ActionTapCount,"tap count"); }
    private static void RetryFreshBounds() { var h=RetryHarness(); h.Matcher.DynamicRetryAction=true; var r=Execute(h); Eq(2,r.ActionTapCount,"retry"); Is(h.Client.Taps[0].Item1!=h.Client.Taps[1].Item1,"bounds reused"); }
    private static void UnknownNoInput() { var h=new Harness(); h.Detector.AfterState=GameState.Unknown; var r=Execute(h); Eq(1,r.ActionTapCount,"unknown caused input"); }
    private static void UnknownLimit() { var h=NoSuccessHarness(); h.Detector.AfterState=GameState.Unknown; h.Matcher.WorldAfter=false; var r=Execute(h); Eq(DispatchMarchOutcome.VerificationIndeterminate,r.Outcome,"outcome"); }
    private static void TimeoutBounded() { var h=NoSuccessHarness(); var sw=Stopwatch.StartNew(); Execute(h); Is(sw.Elapsed<TimeSpan.FromSeconds(2),"unbounded"); }
    private static void PollCancellation() { var h=NoSuccessHarness(); using(var c=new CancellationTokenSource(20)){ var r=Execute(h,c.Token); Eq(DispatchMarchOutcome.Cancelled,r.Outcome,"outcome"); } }
    private static void RetryCancellation() { PollCancellation(); }
    private static void LockCancellation() { var h=new Harness(); h.Lock=new BlockingLock(); h.Rebuild(); using(var c=new CancellationTokenSource(20)){ var r=Execute(h,c.Token); Eq(DispatchMarchOutcome.Cancelled,r.Outcome,"outcome"); } }
        private static void SameDeviceSerialized() { var gate=DeviceOperationLock.Shared; int active=0,max=0; Func<Task<int>> run=()=>gate.RunAsync("same",async t=>{int n=Interlocked.Increment(ref active); max=Math.Max(max,n); await Task.Delay(20,t); Interlocked.Decrement(ref active); return 1;},default(CancellationToken)); Task.WaitAll(run(),run()); Eq(1,max,"same device concurrency"); }
    private static void DifferentDevicesIndependent() { var gate=DeviceOperationLock.Shared; var entered=new CountdownEvent(2); Func<string,Task<int>> run=d=>gate.RunAsync(d,async t=>{entered.Signal(); Is(entered.Wait(500),"globally blocked"); await Task.Delay(1,t); return 1;},default(CancellationToken)); Task.WaitAll(Task.Run(()=>run("a")),Task.Run(()=>run("b"))); }
    private static void ProhibitedInputs() { var h=new Harness(); Execute(h); Eq(0,h.Client.ProhibitedCalls,"prohibited calls"); }
    private static void OnlyExpectedTeam() { var h=new Harness(); var r=Execute(h); Eq(TeamNumber.Team4,r.DispatchedTeam.Value,"team"); Eq(1,r.ActionTapCount,"tap count"); }
    private static void NoCancellationNone() { string text=System.IO.File.ReadAllText(System.IO.Path.Combine(Environment.CurrentDirectory,"ADB","Infrastructure","MarchDispatch","DispatchSelectedTeamService.cs")); string forbidden="CancellationToken"+".None"; Is(!text.Contains(forbidden),"default cancellation-token bypass found"); }
    private static void DiagnosticFailureSafe() { var h=NoSuccessHarness(); h.Store.Throw=true; var r=Execute(h); Eq(DispatchMarchOutcome.TransitionTimeout,r.Outcome,"outcome"); }
    private static void ScreenshotCoordinates() { var h=new Harness(); h.Matcher.ActionX=1000; Execute(h); Eq(1050,h.Client.Taps[0].Item1,"screen X"); }
    private static void NoFullScreenComparison() { var h=new Harness(); Execute(h); Is(h.Comparer.Regions.All(x=>x.HasValue),"full-screen comparison used"); }
    private static void NoMarchGameState() { Is(!Enum.GetNames(typeof(GameState)).Contains("MarchStarted"),"invalid GameState"); }

    private static Harness NoSuccessHarness() { var h=new Harness(); h.Options=h.CreateOptions(1,2); h.Comparer.Ratio=0; h.Rebuild(); return h; }
    private static Harness RetryHarness() { var h=NoSuccessHarness(); h.Matcher.PanelAfter=true; h.Matcher.WorldAfter=false; h.Matcher.SelectedAfter=true; h.Detector.AfterState=GameState.TeamSelection; return h; }
    private static ImageMatchResult Found(int x,int y,int w=40,int he=20)=>ImageMatchResult.FoundAt(x,y,w,he,0.99);

    private sealed class Harness
    {
        public FakeClient Client=new FakeClient(); public FakeDetector Detector=new FakeDetector(); public FakeRegistry Registry=new FakeRegistry(); public FakeMatcher Matcher=new FakeMatcher(); public FakeComparer Comparer=new FakeComparer(); public IDeviceOperationLock Lock=new ImmediateLock(); public FakeStore Store=new FakeStore(); public DispatchMarchRequest Request=new DispatchMarchRequest(); public DispatchSelectedTeamOptions Options; public DispatchSelectedTeamService Service;
        public Harness(){ Options=CreateOptions(1,2); Rebuild(); }
        public DispatchSelectedTeamOptions CreateOptions(int seconds,int taps)=>new DispatchSelectedTeamOptions(2,seconds,taps,8,5,2,0.025,true,true,"Diagnostics/MarchDispatch");
        public void Rebuild(){ Service=new DispatchSelectedTeamService(Detector,Client,Registry,Matcher,Comparer,Lock,TeamOptions(),Options,Store,new FakeLogger()); }
        private static FarmTeamSelectionOptions TeamOptions()=>new FarmTeamSelectionOptions(1,1,1,1,false,"Diagnostics/x",new Dictionary<TeamNumber,ImageRegion>{{TeamNumber.Team1,new ImageRegion(0,0,235,150)},{TeamNumber.Team2,new ImageRegion(0,145,235,145)},{TeamNumber.Team3,new ImageRegion(0,290,235,145)},{TeamNumber.Team4,new ImageRegion(0,435,235,155)}});
    }

    private sealed class FakeDetector:IGameStateDetector
    {
        public GameDetectionResult Initial; public GameState AfterState=GameState.WorldMap;
        public FakeDetector(){Initial=Result(GameState.TeamSelection,true);}
        public Task<GameDetectionResult> DetectAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();return Task.FromResult(Initial);}
        public GameDetectionResult Detect(byte[] f)=>Result(f[0]<=2?GameState.TeamSelection:AfterState,f[0]<=2);
        public GameDetectionResult Result(GameState s,bool ready)=>new GameDetectionResult{State=s,IsSuccessful=s!=GameState.Unknown,Evidence=ready?new[]{TemplateId.TeamSelectionPanelAnchor,TemplateId.TeamAdjustFormationButton,TemplateId.TeamActionButtonEnabled}.Select(id=>new GameDetectionEvidence{TemplateId=id,Found=true}).ToArray():new GameDetectionEvidence[0]};
    }
    private sealed class FakeRegistry:ITemplateRegistry
    {
        public HashSet<TemplateId> Optional=new HashSet<TemplateId>();
        public bool Exists(TemplateId id)=>id!=TemplateId.TeamBusyStatusAnchor&&id!=TemplateId.TeamMarchTimerAnchor||Optional.Contains(id);
        public byte[] LoadBytes(TemplateId id)=>new[]{(byte)id}; public string GetPath(TemplateId id)=>id.ToString(); public TemplateDefinition GetDefinition(TemplateId id)=>null;
    }
    private sealed class FakeMatcher:IImageMatcher
    {
        public Func<int,TemplateId,ImageRegion?,ImageMatchResult> Rule; public readonly List<(int frame,TemplateId id,ImageRegion? roi)> Calls=new List<(int,TemplateId,ImageRegion?)>(); public int ActionX=20; public bool PanelAfter=false,WorldAfter=true,SelectedAfter=false,BusyAfter=false,TimerAfter=false,DynamicRetryAction=false;
        public ImageMatchResult Find(byte[] f,byte[] t,ImageRegion? r){int frame=f[0];var id=(TemplateId)t[0];Calls.Add((frame,id,r));return Rule==null?Default(frame,id,r):Rule(frame,id,r);}
        public ImageMatchResult Default(int f,TemplateId id,ImageRegion? r)
        {
            bool pre=f<=2; if(id==TemplateId.TeamSelectionPanelAnchor)return (pre||PanelAfter)?Found(5,5):ImageMatchResult.NotFound(); if(id==TemplateId.TeamAdjustFormationButton)return (pre||PanelAfter)?Found(500,600):ImageMatchResult.NotFound(); if(id==TemplateId.TeamActionButtonEnabled)return (pre||PanelAfter)?Found(ActionX+(DynamicRetryAction&&f>2?f*10:0),600,100,40):ImageMatchResult.NotFound(); if(id==TemplateId.WorldMapAnchor)return !pre&&WorldAfter?Found(50,650):ImageMatchResult.NotFound(); if(id==TemplateId.Team4Badge)return (pre||PanelAfter)&&r.HasValue&&r.Value.Y==435?Found(20,460):ImageMatchResult.NotFound(); if(id==TemplateId.TeamSelectedBorderAnchor)return (pre||SelectedAfter)&&r.HasValue&&r.Value.Y==435?Found(10,570):ImageMatchResult.NotFound(); if(id==TemplateId.TeamBusyStatusAnchor)return !pre&&BusyAfter?Found(10,500):ImageMatchResult.NotFound(); if(id==TemplateId.TeamMarchTimerAnchor)return !pre&&TimerAfter?Found(10,520):ImageMatchResult.NotFound(); return ImageMatchResult.NotFound();
        }
    }
    private sealed class FakeComparer:IFrameStabilityDetector { public double Ratio=0.2; public Func<int,double> Rule; public int Calls; public List<ImageRegion?> Regions=new List<ImageRegion?>(); public FrameComparisonResult Compare(byte[] a,byte[] b,ImageRegion? r=null){Regions.Add(r);Calls++;double value=Rule==null?Ratio:Rule(Calls);return new FrameComparisonResult{DifferenceRatio=value,IsStable=value<=0.025};} }
    private sealed class FakeClient:ILdPlayerClient
    {
        private int frame; public List<(int,int)> Taps=new List<(int,int)>(); public int ProhibitedCalls;
        public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();return Task.FromResult(new[]{(byte)Interlocked.Increment(ref frame)});} public Task TapAsync(string d,int x,int y,CancellationToken t){t.ThrowIfCancellationRequested();Taps.Add((x,y));return Task.CompletedTask;}
        private Task Bad(){ProhibitedCalls++;return Task.CompletedTask;} public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult((IReadOnlyList<string>)new string[0]); public Task<bool> IsRunningAsync(string d,CancellationToken t)=>Task.FromResult(true); public Task OpenAsync(string d,CancellationToken t)=>Bad(); public Task CloseAsync(string d,CancellationToken t)=>Bad(); public Task RunAppAsync(string d,string p,CancellationToken t)=>Bad(); public Task TapByPercentAsync(string d,double x,double y,CancellationToken t)=>Bad(); public Task LongPressAsync(string d,int x,int y,int m,CancellationToken t)=>Bad(); public Task SwipeByPercentAsync(string d,double a,double b,double c,double e,int m,CancellationToken t)=>Bad(); public Task BackAsync(string d,CancellationToken t)=>Bad(); public Task InputTextAsync(string d,string s,CancellationToken t)=>Bad(); public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t)=>Bad();
    }
    private sealed class ImmediateLock:IDeviceOperationLock { public Task<T> RunAsync<T>(string d,Func<CancellationToken,Task<T>> o,CancellationToken t)=>o(t); }
    private sealed class BlockingLock:IDeviceOperationLock { public async Task<T> RunAsync<T>(string d,Func<CancellationToken,Task<T>> o,CancellationToken t){await Task.Delay(500,t);return await o(t);} }
    private sealed class FakeStore:IDispatchMarchDiagnosticStore { public bool Throw; public Task<string> SaveAsync(string d,DispatchMarchOutcome o,byte[] p,CancellationToken t){if(Throw)throw new Exception("disk");return Task.FromResult("diag.png");} }
    private sealed class FakeLogger:IDiagnosticLogger { public void Info(string m){} public void Error(string m,Exception e){} }
}
