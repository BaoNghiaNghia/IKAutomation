using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Workflows;
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
        Run("Invalid request calls no services", Invalid); Run("Unknown initial state is precondition failure", Unknown);
        Run("ResourcePopup initial state is rejected", InitialPopup); Run("TeamSelection initial state is rejected", InitialTeam);
        Run("ContinentMap is handled by EnsureWorldMap", Continent); Run("Ensure failure stops before panel", EnsureFail);
        Run("Panel failure stops before fallback", PanelFail); Run("Configure failure stops before search", ConfigureFail);
        Run("Panel composite evidence is accepted without panel anchor", CompositePanelEvidence);
        Run("Execute disables configure-before-search", ConfigureBeforeFalse); Run("Levels exhausted stops before popup", NotFound);
        Run("Levels exhausted is business outcome", NotFoundBusiness); Run("Search timeout stops before popup", SearchTimeout);
        Run("Levels exhausted records fallback as last completed", NotFoundLastCompleted);
        Run("Levels exhausted skips popup team and dispatch", NotFoundSkipsDownstream);
        Run("ResourceLocated verifies popup", LocatedCallsPopup); Run("Popup not ready stops before team", PopupFail);
        Run("TeamSelection not ready stops before select", OpenTeamNotReady); Run("No eligible team stops before dispatch", NoTeam);
        Run("Selected team is passed to dispatch", SelectedPassed); Run("Team3 is not hard-coded to Team4", Team3Passed);
        Run("Verified MarchStarted succeeds", MarchSuccess); Run("Unverified dispatch is not success", UnverifiedDispatch);
        Run("Verified AlreadyMarching succeeds", AlreadyMarching); Run("Steps preserve order", StepOrder);
        Run("Last completed step is preserved on failure", LastCompleted); Run("No step runs after failure", NoStepAfterFailure);
        Run("Workflow is not retried", NoWorkflowRetry); Run("Each service is called once", EachOnce);
        Run("Cancellation before first step calls no service", CancelBefore); Run("Cancellation between steps stops next", CancelBetween);
        Run("Lease releases on success", LeaseSuccess); Run("Lease releases on failure", LeaseFailure);
        Run("Lease releases on cancellation", LeaseCancel); Run("Same-device workflows serialize", SameDevice);
        Run("Different devices have independent leases", DifferentDevices); Run("Workflow has no LDPlayer client dependency", NoClient);
        Run("Workflow has no Auto_LDPlayer call", NoAuto); Run("Workflow has no direct input call", NoInput);
        Run("Workflow has no default token bypass", NoNone); Run("Screenshot failure preserves outcome", ScreenshotFailure);
        Run("ResourceNotFound falls back through requested levels", NoLevelFallback); Run("Success requires MarchStartedVerified", VerifiedRequired);
        Run("Level 6 located continues workflow", Level6Located);
        Run("Level 6 popup failure remains a popup outcome", Level6PopupFailureMapping);
        Run("Structural fallback result is accepted", StructuralAccepted); Run("Success does not start second cycle", NoSecondCycle);
        Run("Iron storage full switches to Stone", IronFullSwitchesStone);
        Run("Iron and Stone storage full exhausts plan", BothStoragesFull);
        Run("Default resource priority has four resources", DefaultResourcePriority);
        Run("Four-resource plan advances after exhausted levels", ExhaustedAdvances);
        Run("All four resources exhausted returns plan exhausted", AllResourcesExhausted);
        Run("All four resources storage full returns all full", AllResourcesStorageFull);
        Run("Mixed fallback reaches Wood", MixedFallbackReachesWood);
        Run("Technical search failure does not switch resource", TechnicalSearchDoesNotSwitch);
        Run("Missing resource templates stop before input", MissingProfileStopsInput);
        Run("Popup mismatch stops before team selection", PopupMismatchStopsTeam);
        Run("March started sends only one team", MarchSendsOneTeam);
        Run("Resource plan runs inside one-shot device lease", ResourcePlanUsesWorkflowLease);
        Run("Initial SearchPanel safely recovers into resource plan", InitialSearchPanelUsesPlan);
        Run("Resource selection defaults to all four resources", SelectionDefaults);
        Run("Resource selection rejects zero resources", SelectionRejectsZero);
        Run("Resource selection rejects one resource", SelectionRejectsOne);
        Run("Resource selection accepts two resources", SelectionAcceptsTwo);
        Run("Resource selection maps and shuffles without additions", SelectionMapsAndShuffles);
        Run("Shuffled priority remains fixed for workflow run", ShuffledPriorityIsFixed);
        Run("Storage full advances in shuffled order", ShuffledStorageFallback);
        Run("Levels exhausted advances in shuffled order", ShuffledLevelFallback);
        Run("Selected subset never tries excluded resources", SelectedSubsetOnly);
        Run("Diagnostic source displays selected and shuffled resources", DiagnosticDisplaysSelection);
        Run("Resource selection code has no default cancellation token", SelectionHasNoNone);
        Run("All selected resource templates are validated before shuffle", PreflightBeforeShuffle);
        Run("Unchecked resources are not validated", UncheckedResourcesIgnored);
        Run("Multiple missing templates are reported without input", MissingTemplatesStopBeforeInput);
        Run("Four popup title source files exist", PopupTitleFilesExist);
        Console.WriteLine($"One-shot farm tests: {pass} passed, {fail} failed."); return fail == 0 ? 0 : 1;
    }
    static void Run(string n, Action a) { try { a(); pass++; Console.WriteLine("PASS: " + n); } catch (Exception e) { fail++; Console.WriteLine("FAIL: " + n + " - " + e); } }
    static void Is(bool v, string m) { if (!v) throw new Exception(m); } static void Eq<T>(T e,T a,string m){if(!Equals(e,a))throw new Exception($"{m} Expected={e}, Actual={a}");}
    static OneShotFarmResult Go(H h, CancellationToken t=default(CancellationToken))=>h.Workflow.RunAsync("LDPlayer",h.Request,t).GetAwaiter().GetResult();
    static ResourceFarmFallbackResult Plan(H h, FakeProfiles profiles=null, CancellationToken t=default(CancellationToken))
        => new ResourceFarmFallbackService(h.Nav,new FakeFallback(h.Config,h.Search),h.Popup,h.Open,h.Select,h.Dispatch,
            profiles??new FakeProfiles(),new ResourceFarmFallbackOptions(),new Log())
            .RunAsync("LDPlayer",h.Request,GameState.WorldMap,t).GetAwaiter().GetResult();

    static void Invalid(){var h=new H();h.Request.ResourceType=(ResourceType)99;var r=Go(h);Eq(OneShotFarmOutcome.PreconditionFailed,r.Outcome,"outcome");Eq(0,h.Total,"calls");}
    static void Unknown(){var h=new H();h.Detector.Initial=GameState.Unknown;var r=Go(h);Eq(OneShotFarmOutcome.PreconditionFailed,r.Outcome,"outcome");Eq(0,h.Nav.EnsureCalls,"input");}
    static void InitialPopup(){var h=new H();h.Detector.Initial=GameState.ResourcePopup;Eq(OneShotFarmOutcome.PreconditionFailed,Go(h).Outcome,"outcome");}
    static void InitialTeam(){var h=new H();h.Detector.Initial=GameState.TeamSelection;Eq(OneShotFarmOutcome.PreconditionFailed,Go(h).Outcome,"outcome");}
    static void Continent(){var h=new H();h.Detector.Initial=GameState.ContinentMap;Is(Go(h).Success,"not handled");Eq(1,h.Nav.EnsureCalls,"ensure");}
    static void EnsureFail(){var h=new H();h.Nav.EnsureSuccess=false;Eq(OneShotFarmOutcome.WorldMapUnavailable,Go(h).Outcome,"outcome");Eq(0,h.Nav.PanelCalls,"panel");}
    static void PanelFail(){var h=new H();h.Nav.PanelSuccess=false;Eq(OneShotFarmOutcome.SearchPanelUnavailable,Go(h).Outcome,"outcome");Eq(0,h.Config.Calls,"config");}
    static void CompositePanelEvidence(){var h=new H();h.Nav.UseFallbackEvidence=true;var r=Go(h);Is(r.Success,"composite panel evidence was rejected");Eq(1,h.Config.Calls,"config");}
    static void ConfigureFail(){var h=new H();h.Config.Success=false;Eq(OneShotFarmOutcome.SearchConfigurationFailed,Go(h).Outcome,"outcome");Eq(0,h.Search.Calls,"search");}
    static void ConfigureBeforeFalse(){var h=new H();Go(h);Is(!h.Search.Last.ConfigureBeforeSearch,"double configure");}
    static void NotFound(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Eq(OneShotFarmOutcome.ResourceLevelsExhausted,r.Outcome,"outcome");Eq(0,h.Popup.Calls,"popup");}
    static void NotFoundBusiness(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Is(!r.Success&&r.ErrorMessage==null,"treated as exception");}
    static void NotFoundLastCompleted(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;Eq(OneShotFarmStep.SearchWithLevelFallback,Go(h).LastCompletedStep,"last");}
    static void NotFoundSkipsDownstream(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;Go(h);Eq(0,h.Popup.Calls+h.Open.Calls+h.Select.Calls+h.Dispatch.Calls,"downstream calls");}
    static void SearchTimeout(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.Timeout;h.Search.Success=false;Eq(OneShotFarmOutcome.SearchExecutionFailed,Go(h).Outcome,"outcome");Eq(0,h.Popup.Calls,"popup");}
    static void LocatedCallsPopup(){var h=new H();Go(h);Eq(1,h.Popup.Calls,"popup");}
    static void PopupFail(){var h=new H();h.Popup.Ready=false;Eq(OneShotFarmOutcome.ResourcePopupNotReady,Go(h).Outcome,"outcome");Eq(0,h.Open.Calls,"team");}
    static void OpenTeamNotReady(){var h=new H();h.Open.Ready=false;Eq(OneShotFarmOutcome.TeamSelectionNotReady,Go(h).Outcome,"outcome");Eq(0,h.Select.Calls,"select");}
    static void NoTeam(){var h=new H();h.Select.Outcome=SelectFarmTeamOutcome.NoEligibleTeam;h.Select.Success=false;var r=Go(h);Eq(OneShotFarmOutcome.NoEligibleTeam,r.Outcome,"outcome");Eq(0,h.Dispatch.Calls,"dispatch");}
    static void SelectedPassed(){var h=new H();Go(h);Eq(TeamNumber.Team4,h.Dispatch.Last.ExpectedTeam,"team");}
    static void Team3Passed(){var h=new H();h.Select.Team=TeamNumber.Team3;var r=Go(h);Eq(TeamNumber.Team3,h.Dispatch.Last.ExpectedTeam,"team");Eq(TeamNumber.Team3,r.SelectedTeam.Value,"selected");}
    static void MarchSuccess(){var h=new H();var r=Go(h);Is(r.Success,"success");Eq(OneShotFarmOutcome.MarchStarted,r.Outcome,"outcome");}
    static void UnverifiedDispatch(){var h=new H();h.Dispatch.Verified=false;Is(!Go(h).Success,"false success");}
    static void AlreadyMarching(){var h=new H();h.Dispatch.Outcome=DispatchMarchOutcome.AlreadyMarching;Is(Go(h).Success,"already marching");}
    static void StepOrder(){var h=new H();var s=Go(h).Steps.Select(x=>x.Step).ToArray();var expected=new[]{OneShotFarmStep.Preflight,OneShotFarmStep.EnsureWorldMap,OneShotFarmStep.OpenSearchPanel,OneShotFarmStep.SearchWithLevelFallback,OneShotFarmStep.VerifyResourcePopup,OneShotFarmStep.OpenTeamSelection,OneShotFarmStep.SelectTeam,OneShotFarmStep.DispatchTeam,OneShotFarmStep.FinalVerification,OneShotFarmStep.Completed};Is(expected.SequenceEqual(s),"order");}
    static void LastCompleted(){var h=new H();h.Config.Success=false;Eq(OneShotFarmStep.OpenSearchPanel,Go(h).LastCompletedStep,"last");}
    static void NoStepAfterFailure(){ConfigureFail();} static void NoWorkflowRetry(){var h=new H();h.Nav.EnsureSuccess=false;Go(h);Eq(1,h.Nav.EnsureCalls,"retry");}
    static void EachOnce(){var h=new H();Go(h);Eq(7,h.Nav.EnsureCalls+h.Nav.PanelCalls+h.Config.Calls+h.Search.Calls+h.Popup.Calls+h.Open.Calls+h.Select.Calls,"pre-dispatch calls");Eq(1,h.Dispatch.Calls,"dispatch");}
    static void CancelBefore(){var h=new H();using(var c=new CancellationTokenSource()){c.Cancel();var r=Go(h,c.Token);Eq(OneShotFarmOutcome.Cancelled,r.Outcome,"outcome");Eq(0,h.Total,"calls");}}
    static void CancelBetween(){var h=new H();using(var c=new CancellationTokenSource()){h.Nav.AfterEnsure=()=>c.Cancel();var r=Go(h,c.Token);Eq(OneShotFarmOutcome.Cancelled,r.Outcome,"outcome");Eq(0,h.Nav.PanelCalls,"panel");}}
    static void LeaseSuccess(){var h=new H();Go(h);Eq(1,h.Lock.Releases,"release");} static void LeaseFailure(){var h=new H();h.Config.Success=false;Go(h);Eq(1,h.Lock.Releases,"release");}
    static void LeaseCancel(){var h=new H();using(var c=new CancellationTokenSource()){h.Nav.AfterEnsure=()=>c.Cancel();Go(h,c.Token);Eq(1,h.Lock.Releases,"release");}}
    static void SameDevice(){var gate=DeviceOperationLock.Shared;int active=0,max=0;Func<Task<int>> f=()=>gate.RunAsync("same",async t=>{max=Math.Max(max,Interlocked.Increment(ref active));await Task.Delay(20,t);Interlocked.Decrement(ref active);return 1;},default(CancellationToken));Task.WaitAll(f(),f());Eq(1,max,"concurrency");}
    static void DifferentDevices(){var gate=DeviceOperationLock.Shared;var entered=new CountdownEvent(2);Func<string,Task<int>> f=d=>gate.RunAsync(d,async t=>{entered.Signal();Is(entered.Wait(500),"global lock");await Task.Delay(1,t);return 1;},default(CancellationToken));Task.WaitAll(Task.Run(()=>f("a")),Task.Run(()=>f("b")));}
    static string Source()=>File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","Infrastructure","Workflows","OneShotFarmWorkflow.cs"));
    static void NoClient(){Is(!Source().Contains("ILdPlayerClient"),"client dependency");} static void NoAuto(){Is(!Source().Contains("Auto_"+"LDPlayer"),"direct adapter");}
    static void NoInput(){string s=Source();Is(!s.Contains("TapAsync")&&!s.Contains("SwipeByPercent")&&!s.Contains("BackAsync"),"direct input");}
    static void NoNone(){Is(!Source().Contains("CancellationToken"+".None"),"token bypass");}
    static void ScreenshotFailure(){var h=new H();h.Diag.Throw=true;Is(Go(h).Success,"diagnostic replaced outcome");}
    static void NoLevelFallback(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Eq(3,h.Config.Calls,"fallback config");Is(new[]{7,6,5}.SequenceEqual(r.AttemptedLevels),"levels");}
    static void Level6Located(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Go(h);Is(r.Success,"workflow");Eq(6,r.LocatedLevel,"located");Is(new[]{7,6}.SequenceEqual(r.AttemptedLevels),"attempted");}
    static void Level6PopupFailureMapping(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);h.Popup.Ready=false;var r=Go(h);Eq(OneShotFarmOutcome.ResourcePopupNotReady,r.Outcome,"outcome");Is(r.Outcome!=OneShotFarmOutcome.SearchExecutionFailed,"wrong mapping");Eq(6,r.LocatedLevel,"located");Is(new[]{7,6}.SequenceEqual(r.AttemptedLevels),"attempted");Eq(OneShotFarmStep.VerifyResourcePopup,r.LastCompletedStep,"last");Eq(0,h.Open.Calls,"team opened");}
    static void VerifiedRequired(){UnverifiedDispatch();} static void StructuralAccepted(){var h=new H();h.Dispatch.Verified=true;Is(Go(h).Success,"structural");}
    static void NoSecondCycle(){var h=new H();Go(h);Eq(1,h.Search.Calls,"cycle count");}
    static void IronFullSwitchesStone()
    {
        var h=new H(); h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);
        h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.MarchStarted);
        var r=Go(h);
        Is(r.Success,"Stone should dispatch"); Eq(OneShotFarmOutcome.MarchStarted,r.Outcome,"outcome");
        Is(new[]{ResourceType.Iron,ResourceType.Stone}.SequenceEqual(r.AttemptedResources),"resource order");
        Is(new[]{ResourceType.Iron}.SequenceEqual(r.StorageFullResources),"storage list");
        Eq((ResourceType?)ResourceType.Stone,r.LocatedResource,"located resource");
        Eq((ResourceType?)ResourceType.Stone,r.DispatchedResource,"dispatched resource");
        Eq(2,h.Dispatch.Calls,"dispatch attempts"); Eq(ResourceType.Stone,h.Dispatch.Last.CurrentResource,"last resource");
    }
    static void BothStoragesFull()
    {
        var h=new H(); h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);
        h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);
        var r=Go(h);
        Eq(OneShotFarmOutcome.AllCandidateStoragesFull,r.Outcome,"outcome");
        Is(new[]{ResourceType.Iron,ResourceType.Stone}.SequenceEqual(r.StorageFullResources),"storage list");
    }
    static void DefaultResourcePriority(){Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}.SequenceEqual(new OneShotFarmRequest().ResourcePriority),"priority");}
    static void ExhaustedAdvances(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Plan(h);Eq(ResourceFarmFallbackOutcome.MarchStarted,r.Outcome,"outcome");Is(new[]{ResourceType.Iron,ResourceType.Stone}.SequenceEqual(r.AttemptedResources),"attempted");Is(new[]{ResourceType.Iron}.SequenceEqual(r.LevelsExhaustedResources),"exhausted");}
    static void AllResourcesExhausted(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;var r=Plan(h);Eq(ResourceFarmFallbackOutcome.ResourcePlanExhausted,r.Outcome,"outcome");Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}.SequenceEqual(r.LevelsExhaustedResources),"exhausted");Eq(0,h.Popup.Calls+h.Open.Calls+h.Select.Calls+h.Dispatch.Calls,"downstream");}
    static void AllResourcesStorageFull(){var h=new H();for(int i=0;i<4;i++)h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);var r=Plan(h);Eq(ResourceFarmFallbackOutcome.AllCandidateStoragesFull,r.Outcome,"outcome");Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}.SequenceEqual(r.StorageFullResources),"storage order");Eq(4,h.Dispatch.Calls,"dispatches");}
    static void MixedFallbackReachesWood(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.MarchStarted);var r=Plan(h);Eq(ResourceFarmFallbackOutcome.MarchStarted,r.Outcome,"outcome");Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood}.SequenceEqual(r.AttemptedResources),"attempted");Is(new[]{ResourceType.Iron}.SequenceEqual(r.StorageFullResources),"storage");Is(new[]{ResourceType.Stone}.SequenceEqual(r.LevelsExhaustedResources),"exhausted");Eq((ResourceType?)ResourceType.Wood,r.DispatchedResource,"resource");Eq((int?)7,r.LocatedLevel,"level");}
    static void TechnicalSearchDoesNotSwitch(){var h=new H();h.Search.Outcome=ResourceSearchOutcome.Timeout;h.Search.Success=false;var r=Plan(h);Eq(ResourceFarmFallbackOutcome.SearchFailed,r.Outcome,"outcome");Is(new[]{ResourceType.Iron}.SequenceEqual(r.AttemptedResources),"switched");}
    static void MissingProfileStopsInput(){var h=new H();var p=new FakeProfiles{Unsupported=ResourceType.Iron};var r=Plan(h,p);Eq(ResourceFarmFallbackOutcome.SearchFailed,r.Outcome,"outcome");Eq(0,h.Nav.PanelCalls,"input");Is(r.ErrorMessage.Contains("ResourcePopupIronTitle"),"template id");}
    static void PopupMismatchStopsTeam(){var h=new H();h.Popup.Ready=false;var r=Plan(h);Eq(ResourceFarmFallbackOutcome.PopupFailed,r.Outcome,"outcome");Eq(0,h.Open.Calls,"team");Is(new[]{ResourceType.Iron}.SequenceEqual(r.AttemptedResources),"switched");}
    static void MarchSendsOneTeam(){var h=new H();var r=Plan(h);Is(r.Success,"success");Eq(1,h.Dispatch.Calls,"multiple teams");Is(new[]{ResourceType.Iron}.SequenceEqual(r.AttemptedResources),"continued plan");}
    static void ResourcePlanUsesWorkflowLease(){var h=new H();var plan=new FakePlan{DuringRun=()=>Is(h.Lock.Active,"lease was released")};var workflow=PlanWorkflow(h,plan);var r=workflow.RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"workflow");Eq(1,plan.Calls,"plan calls");Eq(1,h.Lock.Releases,"lease releases");}
    static void InitialSearchPanelUsesPlan(){var h=new H();h.Detector.Initial=GameState.ResourceSearchPanel;var plan=new FakePlan();var r=PlanWorkflow(h,plan).RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"workflow");Eq(1,plan.Calls,"plan calls");Eq(1,h.Nav.EnsureCalls,"bounded recovery");}
    static void SelectionDefaults(){var s=new OneShotFarmResourceSelection();Is(new[]{ResourceType.Iron,ResourceType.Stone,ResourceType.Wood,ResourceType.Food}.SequenceEqual(s.GetSelectedResources()),"defaults");}
    static void SelectionRejectsZero(){var s=new OneShotFarmResourceSelection{Iron=false,Stone=false,Wood=false,Food=false};ThrowsSelection(s);}
    static void SelectionRejectsOne(){var s=new OneShotFarmResourceSelection{Iron=true,Stone=false,Wood=false,Food=false};ThrowsSelection(s);}
    static void ThrowsSelection(OneShotFarmResourceSelection s){try{s.CreateRequest(new OneShotFarmRequest());throw new Exception("selection was accepted");}catch(ArgumentException e){Is(e.Message.Contains("ít nhất 2"),"validation message");}}
    static void SelectionAcceptsTwo(){var s=new OneShotFarmResourceSelection{Iron=true,Stone=false,Wood=true,Food=false};var r=s.CreateRequest(new OneShotFarmRequest());Eq(2,r.ResourcePriority.Count,"count");Is(r.ShuffleResourcePriority,"shuffle flag");}
    static void SelectionMapsAndShuffles(){var s=new OneShotFarmResourceSelection{Iron=true,Stone=false,Wood=true,Food=true};var r=s.CreateRequest(new OneShotFarmRequest());Is(new[]{ResourceType.Iron,ResourceType.Wood,ResourceType.Food}.SequenceEqual(r.SelectedResources),"selected");Is(!r.ResourcePriority.Contains(ResourceType.Stone),"added Stone");}
    static void ShuffledPriorityIsFixed(){var h=new H();var s=new OneShotFarmResourceSelection{Iron=true,Stone=false,Wood=true,Food=true};h.Request=s.CreateRequest(h.Request);var plan=new FakePlan();var random=new FakeRandom(0,0);var r=PlanWorkflow(h,plan,new FakeRegistry(),random).RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Is(h.Request.ResourcePriority.SequenceEqual(plan.LastPriority),"priority changed");Is(h.Request.ResourcePriority.SequenceEqual(r.ShuffledResourcePriority),"result priority");Is(h.Request.SelectedResources.SequenceEqual(r.SelectedResources),"result selected");Eq(2,random.Calls,"shuffle count");}
    static void ShuffledStorageFallback(){var h=new H();h.Request.ResourcePriority=new[]{ResourceType.Food,ResourceType.Iron,ResourceType.Wood};h.Request.SelectedResources=h.Request.ResourcePriority;h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.StorageLimitResourceSwitchRequired);h.Dispatch.Outcomes.Enqueue(DispatchMarchOutcome.MarchStarted);var r=Plan(h);Is(new[]{ResourceType.Food,ResourceType.Iron}.SequenceEqual(r.AttemptedResources),"storage order");}
    static void ShuffledLevelFallback(){var h=new H();h.Request.ResourcePriority=new[]{ResourceType.Wood,ResourceType.Iron};h.Request.SelectedResources=h.Request.ResourcePriority;for(int i=0;i<3;i++)h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Plan(h);Is(new[]{ResourceType.Wood,ResourceType.Iron}.SequenceEqual(r.AttemptedResources),"level order");}
    static void SelectedSubsetOnly(){var h=new H();h.Request.ResourcePriority=new[]{ResourceType.Iron,ResourceType.Wood};h.Request.SelectedResources=h.Request.ResourcePriority;h.Search.Outcome=ResourceSearchOutcome.ResourceNotFound;var r=Plan(h);Is(new[]{ResourceType.Iron,ResourceType.Wood}.SequenceEqual(r.AttemptedResources),"subset");Is(!r.AttemptedResources.Contains(ResourceType.Stone)&&!r.AttemptedResources.Contains(ResourceType.Food),"excluded resource tried");}
    static void DiagnosticDisplaysSelection(){string s=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","UI","DeviceDiagnosticWindow.xaml.cs"));Is(s.Contains("Selected resources:")&&s.Contains("Shuffled order:"),"status fields");}
    static void SelectionHasNoNone(){string root=Path.Combine(Environment.CurrentDirectory,"ADB");foreach(string f in new[]{Path.Combine(root,"Core","Workflows","OneShotFarmResourceSelection.cs"),Path.Combine(root,"Infrastructure","Workflows","SystemRandomProvider.cs")})Is(!File.ReadAllText(f).Contains("CancellationToken"+".None"),"token bypass");}
    static void PreflightBeforeShuffle(){var h=new H();h.Request=new OneShotFarmResourceSelection().CreateRequest(h.Request);var registry=new FakeRegistry();registry.Missing.Add(TemplateId.ResourcePopupFoodTitle);var random=new FakeRandom(0,0,0);var plan=new FakePlan();var r=PlanWorkflow(h,plan,registry,random).RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Eq(0,random.Calls,"random called");Eq(0,plan.Calls,"fallback called");Eq(0,h.Nav.EnsureCalls,"ensure called");Eq(OneShotFarmOutcome.PreconditionFailed,r.Outcome,"outcome");Eq(OneShotFarmStep.Preflight,r.LastCompletedStep,"last");Eq(0,r.AttemptedResources.Count,"attempted");Eq(0,r.ShuffledResourcePriority.Count,"shuffle result");Is(r.ErrorMessage.Contains("No game input was sent"),"message");}
    static void UncheckedResourcesIgnored(){var h=new H();h.Request=new OneShotFarmResourceSelection{Iron=true,Stone=false,Wood=true,Food=false}.CreateRequest(h.Request);var registry=new FakeRegistry();registry.Missing.UnionWith(new[]{TemplateId.ResourceStoneSelected,TemplateId.ResourcePopupFoodTitle});var random=new FakeRandom(0);var plan=new FakePlan();var r=PlanWorkflow(h,plan,registry,random).RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"unchecked template blocked run");Eq(1,random.Calls,"random");}
    static void MissingTemplatesStopBeforeInput(){var h=new H();h.Request=new OneShotFarmResourceSelection().CreateRequest(h.Request);var registry=new FakeRegistry();registry.Missing.UnionWith(new[]{TemplateId.ResourceStoneSelected,TemplateId.ResourcePopupWoodTitle,TemplateId.ResourcePopupFoodTitle});var plan=new FakePlan();var r=PlanWorkflow(h,plan,registry,new FakeRandom(0,0,0)).RunAsync("LDPlayer",h.Request,default(CancellationToken)).GetAwaiter().GetResult();Eq(3,r.MissingRuntimeTemplates.Count,"missing count");Is(r.MissingRuntimeTemplates.All(x=>!string.IsNullOrWhiteSpace(x.ExpectedPath)),"paths");Eq(0,h.Total,"input services");Eq(0,plan.Calls,"plan");Is(r.Outcome!=OneShotFarmOutcome.SearchExecutionFailed,"wrong mapping");}
    static void PopupTitleFilesExist(){string root=Path.Combine(Environment.CurrentDirectory,"ADB","Data","InfinityKingdom","1280x720","vi","Resources");foreach(string name in new[]{"resource_popup_iron_title.png","resource_popup_stone_title.png","resource_popup_wood_title.png","resource_popup_food_title.png"})Is(File.Exists(Path.Combine(root,name)),"missing "+name);}
    static OneShotFarmWorkflow PlanWorkflow(H h,FakePlan plan,FakeRegistry registry=null,FakeRandom random=null)=>new OneShotFarmWorkflow(h.Nav,new FakeFallback(h.Config,h.Search),h.Popup,h.Open,h.Select,h.Dispatch,h.Detector,h.Lock,new OneShotFarmWorkflowOptions(true,true,"Diagnostics/OneShotFarm"),h.Diag,new Log(),new ResourceFarmFallbackOptions(),plan,registry==null?null:new FakeProfiles(),registry,random);

    sealed class H
    {
        public FakeNav Nav=new FakeNav();public FakeConfig Config=new FakeConfig();public FakeSearch Search=new FakeSearch();public FakePopup Popup=new FakePopup();public FakeOpen Open=new FakeOpen();public FakeSelect Select=new FakeSelect();public FakeDispatch Dispatch=new FakeDispatch();public FakeDetector Detector=new FakeDetector();public RecordingLock Lock=new RecordingLock();public FakeDiag Diag=new FakeDiag();public OneShotFarmRequest Request=new OneShotFarmRequest();public OneShotFarmWorkflow Workflow;
        public H(){Workflow=new OneShotFarmWorkflow(Nav,new FakeFallback(Config,Search),Popup,Open,Select,Dispatch,Detector,Lock,new OneShotFarmWorkflowOptions(true,true,"Diagnostics/OneShotFarm"),Diag,new Log());}
        public int Total=>Nav.EnsureCalls+Nav.PanelCalls+Config.Calls+Search.Calls+Popup.Calls+Open.Calls+Select.Calls+Dispatch.Calls;
    }
    sealed class FakeNav:IWorldMapNavigationService{public int EnsureCalls,PanelCalls;public bool EnsureSuccess=true,PanelSuccess=true,UseFallbackEvidence;public Action AfterEnsure;public Task<NavigationResult> EnsureWorldMapAsync(string d,CancellationToken t){EnsureCalls++;AfterEnsure?.Invoke();return Task.FromResult(new NavigationResult{Success=EnsureSuccess,FinalState=GameState.WorldMap,Message="world"});}public Task<NavigationResult> OpenResourceSearchPanelAsync(string d,CancellationToken t){PanelCalls++;GameDetectionEvidence[] evidence=UseFallbackEvidence?new[]{new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,Found=true},new GameDetectionEvidence{TemplateId=TemplateId.LevelMinusButton,Found=true},new GameDetectionEvidence{TemplateId=TemplateId.ResourceSearchPanelAnchor,Found=false}}:new[]{new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,Found=true},new GameDetectionEvidence{TemplateId=TemplateId.ResourceSearchPanelAnchor,Found=true}};return Task.FromResult(new NavigationResult{Success=PanelSuccess,FinalState=PanelSuccess?GameState.ResourceSearchPanel:GameState.WorldMap,Message="panel",FinalEvidence=PanelSuccess?evidence:new GameDetectionEvidence[0]});}}
    sealed class FakeConfig:IResourceSearchConfigurationService{public int Calls;public bool Success=true;public ResourceSearchConfigurationRequest Last;public Task<ResourceSearchConfigurationResult> ConfigureAsync(string d,ResourceSearchConfigurationRequest r,CancellationToken t){Calls++;Last=r;return Task.FromResult(new ResourceSearchConfigurationResult{Success=Success,ResourceVerified=Success,LevelVerified=Success,FilterVerified=Success,FinalState=GameState.ResourceSearchPanel,Message="config"});}}
    sealed class FakeSearch:IResourceSearchExecutionService{public int Calls;public bool Success=true;public ResourceSearchOutcome Outcome=ResourceSearchOutcome.ResourceLocated;public Queue<ResourceSearchOutcome> Outcomes=new Queue<ResourceSearchOutcome>();public ResourceSearchExecutionRequest Last;public Task<ResourceSearchExecutionResult> ExecuteAsync(string d,ResourceSearchExecutionRequest r,CancellationToken t){Calls++;Last=r;ResourceSearchOutcome o=Outcomes.Count>0?Outcomes.Dequeue():Outcome;return Task.FromResult(new ResourceSearchExecutionResult{Success=Success&&o==ResourceSearchOutcome.ResourceLocated,Outcome=o,FinalState=o==ResourceSearchOutcome.ResourceLocated?GameState.ResourcePopup:GameState.ResourceSearchPanel,Message="search"});}}
    sealed class FakeFallback:IResourceLevelFallbackService{readonly FakeConfig c;readonly FakeSearch s;public FakeFallback(FakeConfig c,FakeSearch s){this.c=c;this.s=s;}public async Task<ResourceLevelFallbackResult> SearchAsync(string d,ResourceType type,ResourceLevelFallbackPolicy p,bool u,CancellationToken t){var a=new List<ResourceLevelAttemptResult>();foreach(int level in p.Levels){for(int n=1;n<=p.AttemptsPerLevel;n++){t.ThrowIfCancellationRequested();var q=new ResourceSearchConfigurationRequest{ResourceType=type,TargetLevel=level,UnoccupiedOnly=u};var cr=await c.ConfigureAsync(d,q,t);var ar=new ResourceLevelAttemptResult{Level=level,AttemptNumber=n,ConfigurationResult=cr,ConfigurationSucceeded=cr.Success&&cr.ResourceVerified&&cr.LevelVerified&&cr.FilterVerified};a.Add(ar);if(!ar.ConfigurationSucceeded)return R(type,ResourceLevelFallbackOutcome.ConfigurationFailed,a,null,cr.Message,cr.ErrorMessage);var sr=await s.ExecuteAsync(d,new ResourceSearchExecutionRequest{Configuration=q,ConfigureBeforeSearch=false},t);ar.SearchResult=sr;ar.SearchOutcome=sr.Outcome;if(sr.Outcome==ResourceSearchOutcome.ResourceLocated)return R(type,ResourceLevelFallbackOutcome.ResourceLocated,a,level,"located",null);if(sr.Outcome!=ResourceSearchOutcome.ResourceNotFound)return R(type,ResourceLevelFallbackOutcome.SearchFailed,a,null,sr.Message,sr.ErrorMessage);}}return R(type,ResourceLevelFallbackOutcome.ResourceLevelsExhausted,a,null,"exhausted",null);}static ResourceLevelFallbackResult R(ResourceType type,ResourceLevelFallbackOutcome o,List<ResourceLevelAttemptResult>a,int?l,string m,string e)=>new ResourceLevelFallbackResult{Outcome=o,Success=o==ResourceLevelFallbackOutcome.ResourceLocated,ResourceType=type,LocatedLevel=l,LastAttemptedLevel=a.LastOrDefault()?.Level,RequestedLevels=new[]{7,6,5},Attempts=a,InitialState=GameState.ResourceSearchPanel,FinalState=o==ResourceLevelFallbackOutcome.ResourceLocated?GameState.ResourcePopup:GameState.ResourceSearchPanel,Message=m,ErrorMessage=e};}
    sealed class FakePopup:IResourceAwarePopupVerificationService{public int Calls;public bool Ready=true;public Task<ResourcePopupVerificationResult> VerifyAsync(string d,CancellationToken t)=>VerifyAsync(d,ResourceType.Iron,t);public Task<ResourcePopupVerificationResult> VerifyAsync(string d,ResourceType resource,CancellationToken t){Calls++;return Task.FromResult(new ResourcePopupVerificationResult{Success=Ready,Outcome=Ready?ResourcePopupOutcome.ResourcePopupReady:ResourcePopupOutcome.ResourcePopupMismatch,PopupAnchorVerified=Ready,IronResourceVerified=Ready,ResourceVerified=Ready,ResourceType=resource,ExpectedResource=resource,ExpectedResourceVerified=Ready,ExpectedPopupTitleTemplate=FakeProfiles.Profile(resource).PopupTitleTemplate,GatherButtonVerified=Ready,FinalState=GameState.ResourcePopup,Message="popup"});}}
    sealed class FakeProfiles:IResourceTemplateProfileProvider{public ResourceType? Unsupported;public ResourceTemplateProfile Get(ResourceType r)=>Profile(r);public bool IsSupported(ResourceType r)=>Unsupported!=r;public string GetUnsupportedReason(ResourceType r)=>$"TemplateId '{Profile(r).PopupTitleTemplate}' is missing at 'Data/missing.png'.";public static ResourceTemplateProfile Profile(ResourceType r){switch(r){case ResourceType.Iron:return new ResourceTemplateProfile{ResourceType=r,SelectedTemplate=TemplateId.ResourceIronSelected,UnselectedTemplate=TemplateId.ResourceIronUnselected,PopupTitleTemplate=TemplateId.ResourcePopupIronTitle,DisplayName="Sắt"};case ResourceType.Stone:return new ResourceTemplateProfile{ResourceType=r,SelectedTemplate=TemplateId.ResourceStoneSelected,UnselectedTemplate=TemplateId.ResourceStoneUnselected,PopupTitleTemplate=TemplateId.ResourcePopupStoneTitle,DisplayName="Mỏ Đá"};case ResourceType.Wood:return new ResourceTemplateProfile{ResourceType=r,SelectedTemplate=TemplateId.ResourceWoodSelected,UnselectedTemplate=TemplateId.ResourceWoodUnselected,PopupTitleTemplate=TemplateId.ResourcePopupWoodTitle,DisplayName="Rừng"};default:return new ResourceTemplateProfile{ResourceType=r,SelectedTemplate=TemplateId.ResourceFoodSelected,UnselectedTemplate=TemplateId.ResourceFoodUnselected,PopupTitleTemplate=TemplateId.ResourcePopupFoodTitle,DisplayName="Đất nông nghiệp"};}}}
    sealed class FakeOpen:IOpenTeamSelectionService{public int Calls;public bool Ready=true;public Task<OpenTeamSelectionResult> OpenAsync(string d,CancellationToken t){Calls++;return Task.FromResult(new OpenTeamSelectionResult{Success=Ready,Outcome=Ready?OpenTeamSelectionOutcome.TeamSelectionOpened:OpenTeamSelectionOutcome.TeamSelectionOpenedButNotReady,FinalState=GameState.TeamSelection,TeamSelectionVerified=true,TeamSelectionReady=Ready,Message="open"});}}
    sealed class FakeSelect:ISelectFarmTeamService{public int Calls;public bool Success=true;public TeamNumber Team=TeamNumber.Team4;public SelectFarmTeamOutcome Outcome=SelectFarmTeamOutcome.TeamSelected;public Task<SelectFarmTeamResult> SelectAsync(string d,TeamSelectionRequest r,CancellationToken t){Calls++;return Task.FromResult(new SelectFarmTeamResult{Success=Success,Outcome=Outcome,SelectedTeam=Outcome==SelectFarmTeamOutcome.NoEligibleTeam?(TeamNumber?)null:Team,SelectedStateVerified=Success,FinalState=GameState.TeamSelection,Message="select"});}}
    sealed class FakeDispatch:IDispatchSelectedTeamService{public int Calls;public bool Verified=true;public DispatchMarchOutcome Outcome=DispatchMarchOutcome.MarchStarted;public Queue<DispatchMarchOutcome> Outcomes=new Queue<DispatchMarchOutcome>();public DispatchMarchRequest Last;public Task<DispatchMarchResult> DispatchAsync(string d,DispatchMarchRequest r,CancellationToken t){Calls++;Last=r;var outcome=Outcomes.Count>0?Outcomes.Dequeue():Outcome;bool storage=outcome==DispatchMarchOutcome.StorageLimitResourceSwitchRequired;return Task.FromResult(new DispatchMarchResult{Success=!storage&&Verified,Outcome=outcome,MarchStartedVerified=!storage&&Verified,DispatchedTeam=storage?(TeamNumber?)null:r.ExpectedTeam,StorageLimitDialogDetected=storage,StorageLimitCancelled=storage,ResourceSwitchRequired=storage,StorageFullResource=storage?(ResourceType?)r.CurrentResource:null,StorageLimitResult=storage?new StorageLimitDialogResult{ReturnedToWorldMap=true,FinalState=GameState.WorldMap,RecoveryTransitions=1}:null,FinalState=GameState.WorldMap,Message="dispatch"});}}
    sealed class FakeDetector:IGameStateDetector{public GameState Initial=GameState.WorldMap;int calls;public Task<GameDetectionResult> DetectAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();calls++;GameState state=calls==1?Initial:GameState.WorldMap;GameDetectionEvidence[] evidence=state==GameState.ResourceSearchPanel?new[]{new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,Found=true},new GameDetectionEvidence{TemplateId=TemplateId.LevelMinusButton,Found=true}}:new GameDetectionEvidence[0];return Task.FromResult(new GameDetectionResult{State=state,IsSuccessful=true,Evidence=evidence});}public GameDetectionResult Detect(byte[] p)=>null;}
    sealed class RecordingLock:IDeviceOperationLock{public int Releases;public bool Active;public async Task<T> RunAsync<T>(string d,Func<CancellationToken,Task<T>> o,CancellationToken t){t.ThrowIfCancellationRequested();Active=true;try{return await o(t);}finally{Active=false;Releases++;}}}
    sealed class FakePlan:IResourceFarmFallbackService{public int Calls;public Action DuringRun;public IReadOnlyList<ResourceType> LastPriority;public Task<ResourceFarmFallbackResult> RunAsync(string d,OneShotFarmRequest r,GameState s,CancellationToken t){t.ThrowIfCancellationRequested();Calls++;LastPriority=r.ResourcePriority.ToArray();DuringRun?.Invoke();return Task.FromResult(new ResourceFarmFallbackResult{Outcome=ResourceFarmFallbackOutcome.MarchStarted,Success=true,RequestedResources=r.ResourcePriority,AttemptedResources=new[]{r.ResourcePriority[0]},StorageFullResources=new ResourceType[0],LevelsExhaustedResources=new ResourceType[0],LocatedResource=r.ResourcePriority[0],LocatedLevel=7,DispatchedResource=r.ResourcePriority[0],DispatchedTeam=TeamNumber.Team4,Attempts=new ResourceFarmAttemptResult[0],InitialState=s,FinalState=GameState.WorldMap,Message="march"});}}
    sealed class FakeRandom:IRandomProvider{readonly Queue<int> values;public int Calls;public FakeRandom(params int[] values){this.values=new Queue<int>(values);}public int Next(int maxExclusive){Calls++;return values.Count==0?0:values.Dequeue();}}
    sealed class FakeRegistry:ITemplateRegistry{public readonly HashSet<TemplateId> Missing=new HashSet<TemplateId>();public TemplateDefinition GetDefinition(TemplateId id)=>new TemplateDefinition(id,id+".png",.8);public string GetPath(TemplateId id)=>Path.Combine("Data","InfinityKingdom",id+".png");public byte[] LoadBytes(TemplateId id)=>new byte[]{1};public bool Exists(TemplateId id)=>!Missing.Contains(id);}
    sealed class FakeDiag:IOneShotFarmDiagnosticService{public bool Throw;public Task<string> CaptureAsync(string d,OneShotFarmStep s,OneShotFarmOutcome o,CancellationToken t){if(Throw)throw new Exception("disk");return Task.FromResult("diag.png");}}
    sealed class Log:IDiagnosticLogger{public void Info(string m){}public void Error(string m,Exception e){}}
}
