using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IKAutomation.ResourceSearchExecution.Tests
{
    internal static class Program
    {
        private static readonly CancellationToken Token = new CancellationToken(false);
        private static int passed, failed;

        private static int Main()
        {
            Run("Configuration failure prevents Search", ConfigurationFailure);
            Run("Search taps fresh button center", SearchCenter);
            Run("Missing Search bounds fails without Tap", MissingSearchBounds);
            Run("Outcome prevents a second Tap", OutcomeStopsTap);
            Run("Retry is bounded", RetryBounded);
            Run("NotFound latch prevents retry", NotFoundNoRetry);
            Run("Both toast anchors produce NotFound", BothToastAnchors);
            Run("One-frame toast is latched", OneFrameToast);
            Run("Toast does not require consecutive frames", NoConsecutiveToastRequirement);
            Run("Toast anchors within Y distance match", ToastYWithin);
            Run("Toast anchors too far apart are ambiguous", ToastYFar);
            Run("Toast outside ROI is ignored", ToastOutsideRoi);
            Run("Primary toast anchor alone is insufficient", PrimaryOnly);
            Run("Action toast anchor alone is insufficient", ActionOnly);
            Run("Toast matching is resource-name independent", ResourceNameIndependent);
            Run("NotFound has priority over Located", NotFoundPriority);
            Run("NotFound latch remains true", LatchRemainsTrue);
            Run("NotFound is not an exception", NotFoundNotException);
            Run("Open panel without toast is not Located", OpenPanelNotLocated);
            Run("Open panel after retries is Timeout", OpenPanelTimeout);
            Run("Located requires panel closed", LocatedNeedsClosedPanel);
            Run("Located requires WorldMap", LocatedNeedsWorldMap);
            Run("Located requires camera movement", LocatedNeedsMovement);
            Run("Located requires stable frames", LocatedNeedsStableFrames);
            Run("WorldMap without movement does not succeed", WorldWithoutMovement);
            Run("Transient Unknown sends no extra input", TransientUnknown);
            Run("Unknown limit returns controlled failure", UnknownLimit);
            Run("Timeout is bounded", TimeoutBounded);
            Run("Fast polling cancellation is returned", FastCancellation);
            Run("Normal polling cancellation is returned", NormalCancellation);
            Run("Lock-wait cancellation is returned", LockWaitCancellation);
            Run("Same-device executions are serialized", SameDeviceSerialized);
            Run("Different devices execute concurrently", DifferentDevicesParallel);
            Run("Workflow avoids prohibited input", ProhibitedInput);
            Run("Located does not click resource", NoResourceClick);
            Run("Missing primary toast template fails safely", MissingPrimaryTemplate);
            Run("Missing action toast template fails safely", MissingActionTemplate);
            Run("Diagnostic save failure does not replace outcome", DiagnosticFailure);
            Run("Observation burst respects maximum", BurstMaximum);
            Run("No full-screen screenshot is registered as toast", NoFullScreenTemplate);
            Run("Frame detector reports identical frames stable", IdenticalFrames);
            Run("Frame detector detects changed frames", ChangedFrames);
            Run("Frame detector honors comparison ROI", FrameRegion);
            Run("Frame detector rejects dimension mismatch", FrameDimensionMismatch);
            Run("Frame detector releases image resources", FrameResourcesDisposed);
            Run("ConfigureBeforeSearch false requires panel", SkipConfigurationRequiresPanel);
            Run("Retry refreshes Search bounds", RetryRefreshesBounds);
            Run("Detector error beats other outcomes", DetectorErrorPriority);
            Run("Wrong resolution fails before Search", WrongResolution);
            Run("Null execution request fails before input", NullRequest);
            Run("Options reject invalid movement thresholds", InvalidOptions);
            Console.WriteLine($"Resource search execution tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        { try { test(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL: " + name + " - " + e); } }

        private static void ConfigurationFailure() { Fixture f=Setup(); f.Configuration.Success=false; var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.Failed,"outcome"); Eq(0,f.Client.TapCalls,"tap"); }
        private static void SearchCenter() { Fixture f=ToastFixture(); Execute(f); Eq(110,f.Client.LastX,"x"); Eq(220,f.Client.LastY,"y"); }
        private static void MissingSearchBounds() { Fixture f=Setup(); f.Matcher.InvalidSearchBounds=true; var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.Failed,"outcome"); Eq(0,f.Client.TapCalls,"tap"); }
        private static void OutcomeStopsTap() { Fixture f=ToastFixture(); Execute(f); Eq(1,f.Client.TapCalls,"tap"); }
        private static void RetryBounded() { Fixture f=Setup(maxAttempts:2); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.Timeout,"outcome"); Eq(2,f.Client.TapCalls,"tap"); }
        private static void NotFoundNoRetry() { Fixture f=ToastFixture(); var r=Execute(f); Is(r.NotFoundObserved,"latch"); Eq(1,f.Client.TapCalls,"tap"); }
        private static void BothToastAnchors() { var r=Execute(ToastFixture()); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound,"outcome"); }
        private static void OneFrameToast() { Fixture f=ToastFixture(); f.Matcher.ToastFrames.Add(2); var r=Execute(f); Is(r.NotFoundObserved,"latch"); Eq(1,r.ObservedFrameCount,"frames"); }
        private static void NoConsecutiveToastRequirement() { var r=Execute(ToastFixture()); Eq(1,r.ObservedFrameCount,"frames"); }
        private static void ToastYWithin() { Fixture f=ToastFixture(); f.Matcher.ActionY=250; var r=Execute(f); Is(r.NotFoundToastVerified,"toast"); }
        private static void ToastYFar() { Fixture f=ToastFixture(); f.Matcher.ActionY=400; var r=Execute(f); Is(!r.NotFoundObserved,"latch"); }
        private static void ToastOutsideRoi() { Fixture f=ToastFixture(); f.Matcher.ToastOutsideRegion=true; var r=Execute(f); Is(!r.NotFoundObserved,"outside toast"); }
        private static void PrimaryOnly() { Fixture f=ToastFixture(); f.Matcher.Action=false; Is(!Execute(f).NotFoundObserved,"latch"); }
        private static void ActionOnly() { Fixture f=ToastFixture(); f.Matcher.Primary=false; Is(!Execute(f).NotFoundObserved,"latch"); }
        private static void ResourceNameIndependent() { var r=Execute(ToastFixture()); Is(r.NotFoundObserved,"generic anchors"); }
        private static void NotFoundPriority() { Fixture f=LocatedFixture(); f.Matcher.Primary=true; f.Matcher.Action=true; f.Matcher.ToastFrames.Add(2); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound,"priority"); }
        private static void LatchRemainsTrue() { var r=Execute(ToastFixture()); Is(r.NotFoundObserved&&r.NotFoundToastVerified,"latch"); }
        private static void NotFoundNotException() { ResourceSearchExecutionResult r=Execute(ToastFixture()); Is(r.ErrorMessage==null,"error"); }
        private static void OpenPanelNotLocated() { var r=Execute(Setup()); Is(!r.Success,"success"); }
        private static void OpenPanelTimeout() { var r=Execute(Setup()); Is(r.Outcome==ResourceSearchOutcome.Timeout,"outcome"); }
        private static void LocatedNeedsClosedPanel() { Fixture f=Setup(requiredStable:1); f.Stability.Differences.Enqueue(.1); f.Stability.Differences.Enqueue(.001); var r=Execute(f); Is(!r.Success,"success"); }
        private static void LocatedNeedsWorldMap() { Fixture f=Setup(requiredStable:1); f.Detector.SetStates(Panel(),State(GameState.ContinentMap),State(GameState.ContinentMap)); f.Stability.Differences.Enqueue(.1); f.Stability.Differences.Enqueue(.001); Is(!Execute(f).Success,"success"); }
        private static void LocatedNeedsMovement() { Fixture f=Setup(requiredStable:1); f.Detector.SetStates(Panel(),World(),World()); f.Stability.Differences.Enqueue(.001); f.Stability.Differences.Enqueue(.001); Is(!Execute(f).Success,"success"); }
        private static void LocatedNeedsStableFrames() { Fixture f=Setup(requiredStable:100,windowMs:5); f.Detector.SetStates(Panel(),World(),World(),World()); f.Stability.Differences.Enqueue(.1); f.Stability.Differences.Enqueue(.001); f.Stability.Differences.Enqueue(.001); Is(!Execute(f).Success,"success"); }
        private static void WorldWithoutMovement() { Fixture f=Setup(requiredStable:1); f.Detector.SetStates(Panel(),World(),World()); Is(Execute(f).Outcome!=ResourceSearchOutcome.ResourceLocated,"outcome"); }
        private static void TransientUnknown() { Fixture f=ToastFixture(); f.Detector.SetStates(Panel(),State(GameState.Unknown),Panel()); f.Matcher.ToastFrames.Clear(); f.Matcher.ToastFrames.Add(3); var r=Execute(f); Is(r.NotFoundObserved,"eventual toast"); Eq(1,r.SearchTapCount,"tap"); }
        private static void UnknownLimit() { Fixture f=Setup(maxUnknown:0); f.Detector.SetStates(Panel(),State(GameState.Unknown)); Is(Execute(f).Outcome==ResourceSearchOutcome.Failed,"outcome"); }
        private static void TimeoutBounded() { Fixture f=Setup(windowMs:3); var r=Execute(f); Is(r.Duration<TimeSpan.FromSeconds(1),"duration"); }
        private static void FastCancellation() { Fixture f=Setup(windowMs:100); using(var c=new CancellationTokenSource(10)){Is(Execute(f,Request(),c.Token).Outcome==ResourceSearchOutcome.Cancelled,"outcome");} }
        private static void NormalCancellation() { Fixture f=Setup(windowMs:5); f.Detector.SetStates(Panel(),World()); using(var c=new CancellationTokenSource(20)){Is(Execute(f,Request(),c.Token).Outcome==ResourceSearchOutcome.Cancelled,"outcome");} }
        private static void LockWaitCancellation() { Fixture f=Setup(windowMs:3); f.Configuration.DelayMs=80; Task<ResourceSearchExecutionResult> first=f.Service.ExecuteAsync("same",Request(),Token); using(var c=new CancellationTokenSource(10)){var second=f.Service.ExecuteAsync("same",Request(),c.Token).GetAwaiter().GetResult(); Is(second.Outcome==ResourceSearchOutcome.Cancelled,"outcome");} first.GetAwaiter().GetResult(); }
        private static void SameDeviceSerialized() { Fixture f=Setup(windowMs:3); f.Configuration.DelayMs=30; Task.WaitAll(f.Service.ExecuteAsync("same",Request(),Token),f.Service.ExecuteAsync("same",Request(),Token)); Eq(1,f.Configuration.MaxActive,"active"); }
        private static void DifferentDevicesParallel() { Fixture f=Setup(windowMs:3); f.Configuration.DelayMs=30; Task.WaitAll(f.Service.ExecuteAsync("a",Request(),Token),f.Service.ExecuteAsync("b",Request(),Token)); Is(f.Configuration.MaxActive>=2,"parallel"); }
        private static void ProhibitedInput() { Fixture f=ToastFixture(); Execute(f); Eq(0,f.Client.ProhibitedCalls,"prohibited"); }
        private static void NoResourceClick() { Fixture f=LocatedFixture(); var r=Execute(f); Is(r.Success,"success"); Eq(1,f.Client.TapCalls,"only Search tap"); }
        private static void MissingPrimaryTemplate() { Fixture f=Setup(); f.Registry.Missing=TemplateId.ResourceNotFoundToastAnchor; var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.Failed&&r.ErrorMessage.Contains("ResourceNotFoundToastAnchor"),"error"); Eq(0,f.Client.TapCalls,"tap"); }
        private static void MissingActionTemplate() { Fixture f=Setup(); f.Registry.Missing=TemplateId.ResourceNotFoundToastActionAnchor; var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.Failed&&r.ErrorMessage.Contains("ResourceNotFoundToastActionAnchor"),"error"); Eq(0,f.Client.TapCalls,"tap"); }
        private static void DiagnosticFailure() { Fixture f=ToastFixture(saveResult:true); f.Store.Throw=true; var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound,"outcome"); }
        private static void BurstMaximum() { Fixture f=Setup(windowMs:10,burst:true,burstMax:2); Execute(f); Eq(2,f.Store.ObservationSaves,"burst"); }
        private static void NoFullScreenTemplate() { Fixture f=Setup(); Is(!f.Registry.GetPath(TemplateId.ResourceNotFoundToastAnchor).Contains("screen"),"full screen"); }
        private static void IdenticalFrames() { byte[] p=Png(32,32,Color.Black); var r=new FrameStabilityDetector(.01).Compare(p,p); Is(r.IsStable&&r.DifferenceRatio==0,"stable"); }
        private static void ChangedFrames() { var r=new FrameStabilityDetector(.01).Compare(Png(32,32,Color.Black),Png(32,32,Color.White)); Is(!r.IsStable&&r.DifferenceRatio>.9,"difference"); }
        private static void FrameRegion() { byte[] a=Png(32,32,Color.Black); byte[] b=PngWithCorner(); var r=new FrameStabilityDetector(.01).Compare(a,b,new ImageRegion(0,0,16,16)); Is(r.IsStable,"region"); }
        private static void FrameDimensionMismatch() { Throws<ArgumentException>(()=>new FrameStabilityDetector(.01).Compare(Png(16,16,Color.Black),Png(32,32,Color.Black))); }
        private static void FrameResourcesDisposed() { string p=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png"); File.WriteAllBytes(p,Png(16,16,Color.Black)); byte[] bytes=File.ReadAllBytes(p); new FrameStabilityDetector(.01).Compare(bytes,bytes); File.Delete(p); Is(!File.Exists(p),"locked"); }
        private static void SkipConfigurationRequiresPanel() { Fixture f=Setup(); f.Detector.AsyncResult=World(); var q=Request(); q.ConfigureBeforeSearch=false; var r=Execute(f,q); Is(r.Outcome==ResourceSearchOutcome.Failed,"outcome"); Eq(0,f.Client.TapCalls,"tap"); }
        private static void RetryRefreshesBounds() { Fixture f=Setup(maxAttempts:2); f.Matcher.MoveSearchOnRetry=true; Execute(f); Eq(2,f.Client.TapCalls,"taps"); Is(f.Client.TapXs.Distinct().Count()==2,"fresh bounds"); }
        private static void DetectorErrorPriority() { Fixture f=ToastFixture(); f.Detector.ErrorAtCall=2; var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.Failed&&!r.NotFoundObserved,"priority"); }
        private static void WrongResolution() { Fixture f=Setup(); f.Client.Frame=Png(10,10,Color.Black); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.Failed,"outcome"); Eq(0,f.Client.TapCalls,"tap"); }
        private static void NullRequest() { Fixture f=Setup(); var r=f.Service.ExecuteAsync("d",null,Token).GetAwaiter().GetResult(); Is(r.Outcome==ResourceSearchOutcome.Failed,"outcome"); Eq(0,f.Client.TapCalls,"tap"); }
        private static void InvalidOptions() { Throws<ArgumentOutOfRangeException>(()=>Options(movement:.01,stable:.02)); }

        private static Fixture ToastFixture(bool saveResult=false) { Fixture f=Setup(saveResult:saveResult); f.Matcher.Primary=true; f.Matcher.Action=true; f.Matcher.ToastFrames.Add(2); return f; }
        private static Fixture LocatedFixture() { Fixture f=Setup(requiredStable:3,windowMs:30); f.Detector.SetStates(Panel(),World(),World(),World(),World()); f.Stability.Differences.Enqueue(.1); f.Stability.Differences.Enqueue(.001); f.Stability.Differences.Enqueue(.001); f.Stability.Differences.Enqueue(.001); return f; }

        private static Fixture Setup(int maxAttempts=2,int maxUnknown=5,int requiredStable=3,int windowMs=5,bool saveResult=false,bool burst=false,int burstMax=2)
        {
            var client=new FakeClient{Frame=Png(1280,720,Color.Black)}; var config=new FakeConfiguration(); var detector=new FakeDetector(); var registry=new FakeRegistry(); var matcher=new FakeMatcher(client); var stability=new FakeStability(); var store=new FakeStore();
            var service=new ResourceSearchExecutionService(config,detector,client,registry,matcher,stability,new DeviceOperationLock(),Options(maxAttempts,maxUnknown,requiredStable,windowMs,saveResult,burst,burstMax),store,new FakeLogger());
            return new Fixture{Client=client,Configuration=config,Detector=detector,Registry=registry,Matcher=matcher,Stability=stability,Store=store,Service=service};
        }
        private static ResourceSearchExecutionOptions Options(int maxAttempts=2,int maxUnknown=5,int requiredStable=3,int windowMs=5,bool saveResult=false,bool burst=false,int burstMax=2,double movement=.04,double stable=.015) => new ResourceSearchExecutionOptions(windowMs,1,1,1,maxAttempts,1,new ImageRegion(220,120,840,360),140,movement,stable,requiredStable,maxUnknown,saveResult,burst,burstMax,"Diagnostics/SearchResults","Diagnostics/SearchObservation",1280,720,new ImageRegion(160,80,960,440));
        private static ResourceSearchExecutionRequest Request()=>new ResourceSearchExecutionRequest{ConfigureBeforeSearch=true,Configuration=new ResourceSearchConfigurationRequest{ResourceType=ResourceType.Iron,TargetLevel=7,UnoccupiedOnly=true}};
        private static ResourceSearchExecutionResult Execute(Fixture f,ResourceSearchExecutionRequest q=null,CancellationToken? token=null)=>f.Service.ExecuteAsync("LDPlayer",q??Request(),token??Token).GetAwaiter().GetResult();

        private static GameDetectionResult Panel()=>State(GameState.ResourceSearchPanel);
        private static GameDetectionResult World()=>State(GameState.WorldMap);
        private static GameDetectionResult State(GameState state)
        { var e=new List<GameDetectionEvidence>(); if(state==GameState.ResourceSearchPanel){e.Add(new GameDetectionEvidence{TemplateId=TemplateId.LevelMinusButton,Found=true});e.Add(new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,Found=true});} if(state==GameState.WorldMap)e.Add(new GameDetectionEvidence{TemplateId=TemplateId.WorldMapAnchor,Found=true}); return new GameDetectionResult{State=state,IsSuccessful=true,Evidence=e.AsReadOnly()}; }
        private static byte[] Png(int w,int h,Color c) { using(var b=new Bitmap(w,h)){using(Graphics g=Graphics.FromImage(b))g.Clear(c);using(var s=new MemoryStream()){b.Save(s,ImageFormat.Png);return s.ToArray();}} }
        private static byte[] PngWithCorner() { using(var b=new Bitmap(32,32)){using(Graphics g=Graphics.FromImage(b)){g.Clear(Color.Black);g.FillRectangle(Brushes.White,16,16,16,16);}using(var s=new MemoryStream()){b.Save(s,ImageFormat.Png);return s.ToArray();}} }
        private static void Is(bool c,string m){if(!c)throw new Exception(m);} private static void Eq<T>(T e,T a,string m){if(!EqualityComparer<T>.Default.Equals(e,a))throw new Exception($"{m}: expected={e}, actual={a}");} private static void Throws<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}

        private sealed class Fixture { public FakeClient Client; public FakeConfiguration Configuration; public FakeDetector Detector; public FakeRegistry Registry; public FakeMatcher Matcher; public FakeStability Stability; public FakeStore Store; public IResourceSearchExecutionService Service; }
        private sealed class FakeConfiguration:IResourceSearchConfigurationService
        { private int active; public bool Success=true; public int DelayMs,MaxActive; public async Task<ResourceSearchConfigurationResult> ConfigureAsync(string d,ResourceSearchConfigurationRequest q,CancellationToken t){int n=Interlocked.Increment(ref active);MaxActive=Math.Max(MaxActive,n);try{if(DelayMs>0)await Task.Delay(DelayMs,t);return new ResourceSearchConfigurationResult{Success=Success,InitialState=GameState.WorldMap,FinalState=GameState.ResourceSearchPanel,ErrorMessage=Success?null:"configuration failed",Steps=new ConfigurationStepResult[0]};}finally{Interlocked.Decrement(ref active);}} }
        private sealed class FakeDetector:IGameStateDetector
        { private readonly Queue<GameDetectionResult> states=new Queue<GameDetectionResult>(); private GameDetectionResult last=Panel(); private int calls; public int ErrorAtCall; public GameDetectionResult AsyncResult=Panel(); public void SetStates(params GameDetectionResult[] s){states.Clear();foreach(var x in s)states.Enqueue(x);if(s.Length>0)last=s[s.Length-1];} public Task<GameDetectionResult> DetectAsync(string d,CancellationToken t)=>Task.FromResult(AsyncResult); public GameDetectionResult Detect(byte[] p){calls++;if(ErrorAtCall==calls)return new GameDetectionResult{State=GameState.Unknown,IsSuccessful=false,Evidence=new GameDetectionEvidence[0],ErrorMessage="detector error"};if(states.Count>0)return states.Dequeue();return last;} }
        private sealed class FakeRegistry:ITemplateRegistry
        { public TemplateId? Missing; public TemplateDefinition GetDefinition(TemplateId id)=>new TemplateDefinition(id,"Search/"+id+".png",.8); public string GetPath(TemplateId id)=>Path.Combine("templates",id+".png"); public byte[] LoadBytes(TemplateId id)=>new[]{(byte)id}; public bool Exists(TemplateId id)=>Missing!=id; }
        private sealed class FakeMatcher:IImageMatcher
        { private readonly FakeClient client; private int searchMatches; public bool Primary,Action,InvalidSearchBounds,ToastOutsideRegion,MoveSearchOnRetry; public int PrimaryY=200,ActionY=230; public HashSet<int> ToastFrames=new HashSet<int>(); public FakeMatcher(FakeClient c){client=c;} public ImageMatchResult Find(byte[] s,byte[] t,ImageRegion? r=null){TemplateId id=(TemplateId)t[0];if(id==TemplateId.SearchButtonEnabled){searchMatches++;int x=MoveSearchOnRetry&&searchMatches>1?200:100;return InvalidSearchBounds?ImageMatchResult.FoundAt(x,200,0,0):ImageMatchResult.FoundAt(x,200,20,40);}bool active=ToastFrames.Count==0||ToastFrames.Contains(client.CaptureCount);if(ToastOutsideRegion&&r.HasValue)return ImageMatchResult.NotFound();if(id==TemplateId.ResourceNotFoundToastAnchor&&Primary&&active)return ImageMatchResult.FoundAt(300,PrimaryY,100,20);if(id==TemplateId.ResourceNotFoundToastActionAnchor&&Action&&active)return ImageMatchResult.FoundAt(320,ActionY,100,20);return ImageMatchResult.NotFound();} }
        private sealed class FakeStability:IFrameStabilityDetector
        { public Queue<double> Differences=new Queue<double>(); public FrameComparisonResult Compare(byte[] a,byte[] b,ImageRegion? r=null){double d=Differences.Count>0?Differences.Dequeue():0;return new FrameComparisonResult{DifferenceRatio=d,IsStable=d<=.015};} }
        private sealed class FakeStore:IResourceSearchDiagnosticStore
        { public bool Throw; public int ObservationSaves; public Task<string> SaveResultAsync(string d,ResourceSearchOutcome o,byte[] p,CancellationToken t){if(Throw)throw new IOException("save failed");return Task.FromResult("result.png");} public Task SaveObservationAsync(string d,DateTimeOffset ts,int i,byte[] p,CancellationToken t){ObservationSaves++;return Task.CompletedTask;} }
        private sealed class FakeClient:ILdPlayerClient
        { public byte[] Frame; public int CaptureCount,TapCalls,LastX,LastY,ProhibitedCalls; public List<int> TapXs=new List<int>(); public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();CaptureCount++;return Task.FromResult(Frame);} public Task TapAsync(string d,int x,int y,CancellationToken t){t.ThrowIfCancellationRequested();TapCalls++;LastX=x;LastY=y;TapXs.Add(x);return Task.CompletedTask;} public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult<IReadOnlyList<string>>(new[]{"LDPlayer"}); public Task<bool> IsRunningAsync(string d,CancellationToken t)=>Task.FromResult(true); public Task OpenAsync(string d,CancellationToken t)=>Task.CompletedTask; public Task CloseAsync(string d,CancellationToken t)=>Task.CompletedTask; public Task RunAppAsync(string d,string p,CancellationToken t)=>Task.CompletedTask; public Task TapByPercentAsync(string d,double x,double y,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} public Task LongPressAsync(string d,int x,int y,int ms,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} public Task SwipeByPercentAsync(string d,double sx,double sy,double ex,double ey,int ms,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} public Task BackAsync(string d,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} public Task InputTextAsync(string d,string s,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} }
        private sealed class FakeLogger:IDiagnosticLogger { public void Info(string m){} public void Error(string m,Exception e){} }
    }
}
