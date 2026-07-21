using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    static int pass, fail;
    static int Main()
    {
        Run("Default policy preserves 7 6 5 order", DefaultOrder);
        Run("Level 7 located stops lower levels", Level7Located);
        Run("Level 6 located is recorded", Level6Located);
        Run("Level 5 located is recorded", Level5Located);
        Run("All not found returns levels exhausted", Exhausted);
        Run("Levels exhausted is not exception", ExhaustedBusiness);
        Run("Toast clear runs before next level", ToastClearBeforeNext);
        Run("Toast anchor resets clear count", ToastResetsClearCount);
        Run("Two clear panel frames verify clear", TwoClearFrames);
        Run("Toast clear timeout stops next level", ClearTimeout);
        Run("Closed panel stops without input", ClosedPanel);
        Run("Configure receives exact levels", ConfigureLevels);
        Run("Execute never configures twice", ConfigureBeforeFalse);
        Run("Each attempt configures and executes once", OncePerAttempt);
        Run("Configuration failure stops lower levels", ConfigurationFailure);
        Run("Unavailable requested level continues at verified lower level", UnavailableLevelFallsBack);
        Run("Not-found toast during configuration switches resource", ConfigurationToastSwitchesResource);
        Run("Single toast anchor during configuration does not switch resource", ConfigurationSingleToastAnchorDoesNotSwitch);
        Run("Distant toast anchors during configuration do not switch resource", ConfigurationDistantToastAnchorsDoNotSwitch);
        Run("Search timeout stops lower levels", SearchTimeout);
        Run("Cancellation between levels stops next configure", CancelBetweenLevels);
        Run("Cancellation during toast clear is respected", CancelDuringClear);
        Run("Service has no CancellationToken None", NoNone);
        Run("Attempts per level is bounded", AttemptsBounded);
        Run("Retry waits for toast clear", RetryWaitsForClear);
        Run("Duplicate levels rejected before input", DuplicateRejected);
        Run("Unsupported level rejected before input", UnsupportedRejected);
        Run("Diagnostic failure preserves exhausted outcome", DiagnosticFailure);
        Run("Same device fallback lease is reentrant", ReentrantLease);
        Run("Different devices are not globally blocked", DifferentDevices);
        Console.WriteLine($"Resource level fallback tests: {pass} passed, {fail} failed.");
        return fail == 0 ? 0 : 1;
    }

    static void Run(string n, Action a){try{a();pass++;Console.WriteLine("PASS: "+n);}catch(Exception e){fail++;Console.WriteLine("FAIL: "+n+" - "+e);}}
    static void Is(bool v,string m){if(!v)throw new Exception(m);} static void Eq<T>(T e,T a,string m){if(!Equals(e,a))throw new Exception($"{m} Expected={e}, Actual={a}");}
    static ResourceLevelFallbackResult Go(H h, ResourceLevelFallbackPolicy p=null, CancellationToken t=default(CancellationToken))=>h.Service.SearchAsync("LDPlayer",ResourceType.Iron,p??new ResourceLevelFallbackPolicy(),true,t).GetAwaiter().GetResult();

    static void DefaultOrder(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Is(new[]{7,6,5}.SequenceEqual(h.Config.Levels),"order");Eq(ResourceLevelFallbackOutcome.ResourceLevelsExhausted,r.Outcome,"outcome");}
    static void Level7Located(){var h=new H();var r=Go(h);Eq(7,r.LocatedLevel,"located");Eq(1,h.Config.Calls,"lower level");}
    static void Level6Located(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Go(h);Eq(6,r.LocatedLevel,"located");Is(new[]{7,6}.SequenceEqual(h.Config.Levels),"levels");}
    static void Level5Located(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);Eq(5,Go(h).LocatedLevel,"located");}
    static void Exhausted(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceLevelsExhausted,r.Outcome,"outcome");Is(!r.Success,"success");}
    static void ExhaustedBusiness(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Is(r.ErrorMessage==null,"exception");}
    static void ToastClearBeforeNext(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Go(h);Is(r.Attempts[1].ToastClearVerifiedBeforeAttempt,"clear");}
    static void ToastResetsClearCount(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);h.Matcher.ToastFrames.Add(2);var r=Go(h);Is(r.Attempts[1].ToastClearResult.ObservedFrames>=4,"reset not observed");}
    static void TwoClearFrames(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Go(h);Eq(2,r.Attempts[1].ToastClearResult.ConsecutiveClearFrames,"frames");}
    static void ClearTimeout(){var h=new H(clearTimeout:1,poll:10);h.Search.Default=ResourceSearchOutcome.ResourceNotFound;for(int i=1;i<500;i++)h.Matcher.ToastFrames.Add(i);var r=Go(h);Eq(ResourceLevelFallbackOutcome.SearchFailed,r.Outcome,"outcome");Eq(1,h.Config.Calls,"next configured");}
    static void ClosedPanel(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Matcher.PanelClosedFrames.Add(1);var r=Go(h);Eq(ResourceLevelFallbackOutcome.PanelUnavailable,r.Outcome,"outcome");Eq(0,h.Client.InputCalls,"input");}
    static void ConfigureLevels(){DefaultOrder();}
    static void ConfigureBeforeFalse(){var h=new H();Go(h);Is(h.Search.Requests.All(x=>!x.ConfigureBeforeSearch),"double config");}
    static void OncePerAttempt(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Eq(r.Attempts.Count,h.Config.Calls,"config");Eq(r.Attempts.Count,h.Search.Calls,"search");}
    static void ConfigurationFailure(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Config.FailLevel=6;var r=Go(h);Eq(ResourceLevelFallbackOutcome.ConfigurationFailed,r.Outcome,"outcome");Is(!h.Config.Levels.Contains(5),"level5");}
    static void UnavailableLevelFallsBack(){var h=new H();h.Config.UnavailableLevel=7;h.Config.ObservedLevel=6;var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceLocated,r.Outcome,"outcome");Eq(6,r.LocatedLevel,"located");Is(new[]{7,6}.SequenceEqual(h.Config.Levels),"configured levels");Eq(1,h.Search.Calls,"search count");Eq(6,h.Search.Requests.Single().Configuration.TargetLevel,"searched level");}
    static void ConfigurationToastSwitchesResource(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Config.FailLevel=6;h.Matcher.ToastFrames.Add(3);h.Matcher.MatchedToastTemplates.Clear();h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceNotFoundToastShortAnchor);h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceNotFoundToastOtherRegionAnchor);var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceLevelsExhausted,r.Outcome,"outcome");Eq("SearchOtherRegion",r.Attempts.Last().MatchedNotFoundVariant,"variant");Eq(1,h.Search.Calls,"search retry");Is(!h.Config.Levels.Contains(5),"level5");}
    static void ConfigurationSingleToastAnchorDoesNotSwitch(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Config.FailLevel=6;h.Matcher.ToastFrames.Add(3);h.Matcher.MatchedToastTemplates.Clear();h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceNotFoundToastShortAnchor);Eq(ResourceLevelFallbackOutcome.ConfigurationFailed,Go(h).Outcome,"outcome");}
    static void ConfigurationDistantToastAnchorsDoNotSwitch(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Config.FailLevel=6;h.Matcher.ToastFrames.Add(3);h.Matcher.MatchedToastTemplates.Clear();h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceNotFoundToastShortAnchor);h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceNotFoundToastOtherRegionAnchor);h.Matcher.OtherRegionY=500;Eq(ResourceLevelFallbackOutcome.ConfigurationFailed,Go(h).Outcome,"outcome");}
    static void SearchTimeout(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.Timeout);var r=Go(h);Eq(ResourceLevelFallbackOutcome.SearchFailed,r.Outcome,"outcome");Is(!h.Config.Levels.Contains(5),"level5");}
    static void CancelBetweenLevels(){using(var c=new CancellationTokenSource()){var h=new H();h.Search.CancelAfterNotFound=c;h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var r=Go(h,null,c.Token);Eq(ResourceLevelFallbackOutcome.Cancelled,r.Outcome,"outcome");Eq(1,h.Config.Calls,"level6");}}
    static void CancelDuringClear(){using(var c=new CancellationTokenSource()){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;h.Client.CancelOnCapture=c;var r=Go(h,null,c.Token);Eq(ResourceLevelFallbackOutcome.Cancelled,r.Outcome,"outcome");}}
    static void NoNone(){string s=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","Infrastructure","ResourceSearch","ResourceLevelFallbackService.cs"));Is(!s.Contains("CancellationToken"+".None"),"token bypass");}
    static void AttemptsBounded(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var p=new ResourceLevelFallbackPolicy{Levels=new[]{7},AttemptsPerLevel=2,StopOnFirstLocated=true,WaitForToastClearBetweenAttempts=true};var r=Go(h,p);Eq(2,r.Attempts.Count,"attempts");}
    static void RetryWaitsForClear(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var p=new ResourceLevelFallbackPolicy{Levels=new[]{7},AttemptsPerLevel=2,StopOnFirstLocated=true,WaitForToastClearBetweenAttempts=true};var r=Go(h,p);Is(r.Attempts[1].ToastClearVerifiedBeforeAttempt,"clear");}
    static void DuplicateRejected(){var h=new H();var p=new ResourceLevelFallbackPolicy{Levels=new[]{7,7},AttemptsPerLevel=1};var r=Go(h,p);Eq(0,h.Config.Calls,"input");Is(!r.Success,"success");}
    static void UnsupportedRejected(){var h=new H();var p=new ResourceLevelFallbackPolicy{Levels=new[]{4},AttemptsPerLevel=1};Go(h,p);Eq(0,h.Config.Calls,"input");}
    static void DiagnosticFailure(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;h.Diag.Throw=true;Eq(ResourceLevelFallbackOutcome.ResourceLevelsExhausted,Go(h).Outcome,"outcome");}
    static void ReentrantLease(){var h=new H();var gate=DeviceOperationLock.Shared;var r=gate.RunAsync("LDPlayer",t=>h.Service.SearchAsync("LDPlayer",ResourceType.Iron,new ResourceLevelFallbackPolicy(),true,t),default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"reentrant");}
    static void DifferentDevices(){var gate=DeviceOperationLock.Shared;var entered=new CountdownEvent(2);Func<string,Task<int>> f=d=>gate.RunAsync(d,async t=>{entered.Signal();Is(entered.Wait(1000),"global");await Task.Delay(1,t);return 1;},default(CancellationToken));Task.WaitAll(Task.Run(()=>f("a")),Task.Run(()=>f("b")));}

    sealed class H
    {
        public FakeConfig Config=new FakeConfig();public FakeSearch Search=new FakeSearch();public FakeClient Client=new FakeClient();public FakeMatcher Matcher=new FakeMatcher();public FakeDiag Diag=new FakeDiag();public ResourceLevelFallbackService Service;
        public H(int clearTimeout=1,int poll=1){var o=new ResourceLevelFallbackOptions(new[]{7,6,5},1,2,poll,clearTimeout,true,true,true,"Diagnostics/ResourceLevelFallback",new ImageRegion(150,120,980,400));Service=new ResourceLevelFallbackService(Config,Search,new Detector(),Client,new Registry(),Matcher,DeviceOperationLock.Shared,new ResourceSearchConfigurationOptions(1,1,1,1,7,8,1),o,Diag,new Log());}
    }
    sealed class FakeConfig:IResourceSearchConfigurationService{public int Calls,FailLevel,UnavailableLevel,ObservedLevel;public List<int> Levels=new List<int>();public Task<ResourceSearchConfigurationResult> ConfigureAsync(string d,ResourceSearchConfigurationRequest r,CancellationToken t){t.ThrowIfCancellationRequested();Calls++;Levels.Add(r.TargetLevel);bool unavailable=r.TargetLevel==UnavailableLevel;bool ok=r.TargetLevel!=FailLevel&&!unavailable;return Task.FromResult(new ResourceSearchConfigurationResult{Success=ok,ResourceVerified=ok,LevelVerified=ok,FilterVerified=ok,ObservedLevel=unavailable?(int?)ObservedLevel:ok?(int?)r.TargetLevel:null,FinalState=GameState.ResourceSearchPanel,Message=ok?"configured":"failed",ErrorMessage=ok?null:"configuration"});}}
    sealed class FakeSearch:IResourceSearchExecutionService{public int Calls;public ResourceSearchOutcome Default=ResourceSearchOutcome.ResourceLocated;public Queue<ResourceSearchOutcome> Outcomes=new Queue<ResourceSearchOutcome>();public List<ResourceSearchExecutionRequest> Requests=new List<ResourceSearchExecutionRequest>();public CancellationTokenSource CancelAfterNotFound;public Task<ResourceSearchExecutionResult> ExecuteAsync(string d,ResourceSearchExecutionRequest r,CancellationToken t){t.ThrowIfCancellationRequested();Calls++;Requests.Add(r);var o=Outcomes.Count>0?Outcomes.Dequeue():Default;var x=new ResourceSearchExecutionResult{Outcome=o,Success=o==ResourceSearchOutcome.ResourceLocated,FinalState=o==ResourceSearchOutcome.ResourceLocated?GameState.ResourcePopup:GameState.ResourceSearchPanel,MatchedNotFoundVariant=o==ResourceSearchOutcome.ResourceNotFound?"SearchOtherRegion":null,Message=o.ToString()};if(o==ResourceSearchOutcome.ResourceNotFound)CancelAfterNotFound?.Cancel();return Task.FromResult(x);}}
    sealed class Detector:IGameStateDetector{public Task<GameDetectionResult> DetectAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();return Task.FromResult(new GameDetectionResult{State=GameState.ResourceSearchPanel,IsSuccessful=true,Evidence=new GameDetectionEvidence[0]});}public GameDetectionResult Detect(byte[] p)=>null;}
    sealed class FakeClient:ILdPlayerClient{int frame;public int InputCalls;public CancellationTokenSource CancelOnCapture;public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();int f=++frame;if(f==1&&CancelOnCapture!=null)CancelOnCapture.Cancel();return Task.FromResult(new[]{(byte)f});}public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult((IReadOnlyList<string>)new string[0]);public Task<bool> IsRunningAsync(string d,CancellationToken t)=>Task.FromResult(true);public Task OpenAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task CloseAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task RunAppAsync(string d,string p,CancellationToken t)=>Task.CompletedTask;public Task TapAsync(string d,int x,int y,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task TapByPercentAsync(string d,double x,double y,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task LongPressAsync(string d,int x,int y,int m,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task SwipeByPercentAsync(string d,double a,double b,double c,double e,int m,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task BackAsync(string d,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task InputTextAsync(string d,string s,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t){InputCalls++;return Task.CompletedTask;}}
    sealed class Registry:ITemplateRegistry{public TemplateDefinition GetDefinition(TemplateId id)=>new TemplateDefinition(id,id+".png",.8);public string GetPath(TemplateId id)=>id+".png";public byte[] LoadBytes(TemplateId id)=>new[]{(byte)id};public bool Exists(TemplateId id)=>true;}
    sealed class FakeMatcher:IImageMatcher{public HashSet<int> ToastFrames=new HashSet<int>();public HashSet<int> PanelClosedFrames=new HashSet<int>();public HashSet<TemplateId> MatchedToastTemplates=new HashSet<TemplateId>{TemplateId.ResourceNotFoundToastAnchor,TemplateId.ResourceNotFoundToastActionAnchor,TemplateId.ResourceNotFoundToastShortAnchor,TemplateId.ResourceNotFoundToastOtherRegionAnchor};public int OtherRegionY=200;public ImageMatchResult Find(byte[] s,byte[] t,ImageRegion? r=null){int f=s[0],id=t[0];bool panel=id==(int)TemplateId.SearchButtonEnabled||id==(int)TemplateId.LevelMinusButton;bool toast=id==(int)TemplateId.ResourceNotFoundToastAnchor||id==(int)TemplateId.ResourceNotFoundToastActionAnchor||id==(int)TemplateId.ResourceNotFoundToastShortAnchor||id==(int)TemplateId.ResourceNotFoundToastOtherRegionAnchor;bool found=panel?!PanelClosedFrames.Contains(f):toast&&ToastFrames.Contains(f)&&MatchedToastTemplates.Contains((TemplateId)id);int y=id==(int)TemplateId.ResourceNotFoundToastOtherRegionAnchor?OtherRegionY:200;return found?ImageMatchResult.FoundAt(200,y,20,10):ImageMatchResult.NotFound();}}
    sealed class FakeDiag:IResourceLevelFallbackDiagnosticStore{public bool Throw;public Task<string> SaveAsync(string d,string s,byte[] p,CancellationToken t){if(Throw)throw new IOException("disk");return Task.FromResult("diag.png");}}
    sealed class Log:IDiagnosticLogger{public void Info(string m){}public void Error(string m,Exception e){}}
}
