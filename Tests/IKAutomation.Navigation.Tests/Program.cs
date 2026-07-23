using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.Navigation;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IKAutomation.Navigation.Tests
{
    internal static class Program
    {
        private static readonly CancellationToken Token = new CancellationToken(false);
        private static int passed, failed;
        private static int Main()
        {
            Run("Ensure WorldMap succeeds immediately", EnsureWorldImmediate);
            Run("Ensure WorldMap sends no input when already there", EnsureWorldNoInput);
            Run("Panel uses one Back and verifies WorldMap", PanelBackOnce);
            Run("ContinentMap uses one Back and verifies WorldMap", ContinentBackOnce);
            Run("City taps fresh map button and verifies WorldMap", CityMapButtonOnce);
            Run("City without fresh map-button bounds sends no input", CityMissingBoundsNoInput);
            Run("Unknown fails without input", UnknownNoInput);
            Run("Already-open panel succeeds without Tap", AlreadyPanel);
            Run("Resource tab fallback verifies open panel", ResourceTabFallbackPanel);
            Run("Unverified panel state fails without input", UnverifiedPanelNoInput);
            Run("Open panel taps exact evidence center", TapEvidenceCenter);
            Run("Transient Unknown frame is tolerated", TransientUnknownIsTolerated);
            Run("Tap success requires verified panel", TapRequiresVerification);
            Run("Timeout returns failure", TimeoutFailure);
            Run("Retry never exceeds configured maximum", RetryBounded);
            Run("Unknown after Tap prevents retry", UnknownPreventsRetry);
            Run("Missing bounds prevents Tap", MissingBounds);
            Run("Territory reposition taps only fresh matched anchors", TerritoryRepositionTapsMatchedAnchors);
            Run("Territory reposition rejects far territory marker", TerritoryRepositionRejectsFarTerritoryMarker);
            Run("Territory reposition without map-pin bounds sends no input", TerritoryRepositionMissingMapPin);
            Run("Polling cancellation is respected", PollCancellation);
            Run("Lock-wait cancellation is respected", LockCancellation);
            Run("Same-device actions do not overlap", SameDeviceSerialized);
            Run("Different devices are not globally blocked", DifferentDevicesParallel);
            Run("Workflow sends no prohibited input", NoProhibitedInput);
            Console.WriteLine($"Navigation tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }
        private static void Run(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL: " + name + " - " + e); } }

        private static void EnsureWorldImmediate() { Equal(true, RunEnsure(new[] { State(GameState.WorldMap) }).Success, "Expected success."); }
        private static void EnsureWorldNoInput() { var f = Setup(State(GameState.WorldMap)); f.Service.EnsureWorldMapAsync("d", Token).GetAwaiter().GetResult(); Equal(0, f.Client.TotalInput, "Unexpected input."); }
        private static void PanelBackOnce() { var f = Setup(State(GameState.ResourceSearchPanel), State(GameState.WorldMap)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Failed."); Equal(1,f.Client.BackCalls,"Back count."); Equal(GameState.WorldMap,r.FinalState,"Final state."); }
        private static void ContinentBackOnce() { var f = Setup(State(GameState.ContinentMap), State(GameState.WorldMap)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Failed."); Equal(1,f.Client.BackCalls,"Back count."); }
        private static void CityMapButtonOnce() { var f=Setup(State(GameState.City,true),State(GameState.WorldMap)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Failed."); Equal(1,f.Client.TapCalls,"Tap count."); Equal(25,f.Client.LastX,"Tap X."); Equal(40,f.Client.LastY,"Tap Y."); }
        private static void CityMissingBoundsNoInput() { var f=Setup(State(GameState.City)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unexpected success."); Equal(0,f.Client.TotalInput,"Blind input."); }
        private static void UnknownNoInput() { var f=Setup(State(GameState.Unknown)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unexpected success."); Equal(0,f.Client.TotalInput,"Blind input."); }
        private static void AlreadyPanel() { var f=Setup(State(GameState.ResourceSearchPanel)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Failed."); Equal(0,f.Client.TapCalls,"Extra Tap."); }
        private static void ResourceTabFallbackPanel() { var f=Setup(PanelFromResourceTab()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Resource tab fallback was not accepted."); Equal(0,f.Client.TapCalls,"Unexpected Tap."); }
        private static void UnverifiedPanelNoInput() { var f=Setup(UnverifiedPanel()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unverified panel state succeeded."); Equal(0,f.Client.TotalInput,"Input sent for unverified panel state."); }
        private static void TapEvidenceCenter() { var f=Setup(State(GameState.WorldMap, true), State(GameState.ResourceSearchPanel)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Failed."); Equal(25,f.Client.LastX,"Tap X."); Equal(40,f.Client.LastY,"Tap Y."); }
        private static void TransientUnknownIsTolerated() { var f=Setup(State(GameState.WorldMap,true),State(GameState.Unknown),State(GameState.ResourceSearchPanel)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Transient Unknown should not fail verification."); Equal(1,f.Client.TapCalls,"Unexpected additional Tap."); }
        private static void TapRequiresVerification() { var f=Setup(State(GameState.WorldMap,true), State(GameState.Unknown)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Tap alone counted as success."); }
        private static void TimeoutFailure() { var f=SetupWithOptions(new WorldMapNavigationOptions(250,1,1), State(GameState.WorldMap,true)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Timeout should fail."); Equal(1,f.Client.TapCalls,"Tap count."); }
        private static void RetryBounded() { var states=new List<GameDetectionResult>{State(GameState.WorldMap,true)}; for(int i=0;i<8;i++)states.Add(State(GameState.WorldMap,true)); var f=SetupWithOptions(new WorldMapNavigationOptions(250,1,2),states.ToArray()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Expected failure."); Equal(2,f.Client.TapCalls,"Exceeded retry max."); }
        private static void UnknownPreventsRetry() { var f=SetupWithOptions(new WorldMapNavigationOptions(10,1,3),State(GameState.WorldMap,true),State(GameState.Unknown)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Expected failure."); Equal(1,f.Client.TapCalls,"Retried after Unknown."); }
        private static void MissingBounds() { var f=Setup(State(GameState.WorldMap,false)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Expected failure."); Equal(0,f.Client.TapCalls,"Fallback Tap sent."); }
        private static void TerritoryRepositionTapsMatchedAnchors() { var f=Setup(WorldMapWithPin(),ContinentMapWithAnchors(),ContinentMapWithAnchors(),State(GameState.WorldMap)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Expected recovery success."); Equal(3,f.Client.TapCalls,"Tap count."); Equal(197,f.Client.LastX,"Pin X."); Equal(37,f.Client.LastY,"Pin Y."); }
        private static void TerritoryRepositionRejectsFarTerritoryMarker() { var f=Setup(WorldMapWithPin(),ContinentMapWithFarTerritoryAnchor()); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unexpected success."); Equal(1,f.Client.TapCalls,"Should stop after opening ContinentMap."); }
        private static void TerritoryRepositionMissingMapPin() { var f=Setup(State(GameState.WorldMap,true)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unexpected success."); Equal(0,f.Client.TapCalls,"Blind input."); }
        private static void PollCancellation() { var f=SetupWithOptions(new WorldMapNavigationOptions(100,1,1),State(GameState.ResourceSearchPanel)); using(var c=new CancellationTokenSource(30)) Throws<OperationCanceledException>(()=>f.Service.EnsureWorldMapAsync("d",c.Token).GetAwaiter().GetResult()); }
        private static void LockCancellation() { var f=SetupDelayed(250); Task first=f.Service.EnsureWorldMapAsync("same",Token); using(var c=new CancellationTokenSource(30)) Throws<OperationCanceledException>(()=>f.Service.EnsureWorldMapAsync("same",c.Token).GetAwaiter().GetResult()); first.GetAwaiter().GetResult(); }
        private static void SameDeviceSerialized() { var f=SetupDelayed(100); Task.WaitAll(f.Service.EnsureWorldMapAsync("same",Token),f.Service.EnsureWorldMapAsync("same",Token)); Equal(1,f.Detector.MaxActive,"Same device overlapped."); }
        private static void DifferentDevicesParallel() { var f=SetupDelayed(100); Task.WaitAll(f.Service.EnsureWorldMapAsync("a",Token),f.Service.EnsureWorldMapAsync("b",Token)); Assert(f.Detector.MaxActive>=2,"Different devices globally blocked."); }
        private static void NoProhibitedInput() { var f=Setup(State(GameState.WorldMap,true),State(GameState.ResourceSearchPanel)); f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Equal(0,f.Client.ProhibitedCalls,"Prohibited input called."); }

        private static NavigationResult RunEnsure(GameDetectionResult[] states) { var f=Setup(states); return f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); }
        private static Fixture Setup(params GameDetectionResult[] states)=>SetupWithOptions(new WorldMapNavigationOptions(10,1,2),states);
        private static Fixture SetupDelayed(int delay) { var d=new FakeDetector(State(GameState.WorldMap)){DelayMs=delay}; return FixtureOf(d,new WorldMapNavigationOptions(10,1,2)); }
        private static Fixture SetupWithOptions(WorldMapNavigationOptions options,params GameDetectionResult[] states)=>FixtureOf(new FakeDetector(states),options);
        private static Fixture FixtureOf(FakeDetector detector,WorldMapNavigationOptions options) { var client=new FakeClient(); return new Fixture{Client=client,Detector=detector,Service=new WorldMapNavigationService(client,detector,options,new FakeLogger())}; }
        private static GameDetectionResult State(GameState state,bool bounds=false) { var evidence=new List<GameDetectionEvidence>(); if(state==GameState.WorldMap)evidence.Add(new GameDetectionEvidence{TemplateId=TemplateId.WorldMapAnchor,TemplateExists=true,Found=bounds,MatchResult=bounds?ImageMatchResult.FoundAt(10,20,30,40):null,Message="world"}); if(state==GameState.City)evidence.Add(new GameDetectionEvidence{TemplateId=TemplateId.CityToWorldMapButton,TemplateExists=true,Found=bounds,MatchResult=bounds?ImageMatchResult.FoundAt(10,20,30,40):null,Message="city"}); if(state==GameState.ResourceSearchPanel){evidence.Add(new GameDetectionEvidence{TemplateId=TemplateId.ResourceSearchPanelAnchor,TemplateExists=true,Found=true,Message="panel"});evidence.Add(new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,TemplateExists=true,Found=true,Message="search"});} return new GameDetectionResult{State=state,IsSuccessful=true,Evidence=evidence.AsReadOnly()}; }
        private static GameDetectionResult WorldMapWithPin() => new GameDetectionResult{State=GameState.WorldMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.WorldMapAnchor,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(10,20,30,40),Message="world"},new GameDetectionEvidence{TemplateId=TemplateId.WorldMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,500,40,40),Message="pin map"}}.AsReadOnly()};
        private static GameDetectionResult ContinentMapWithAnchors() => new GameDetectionResult{State=GameState.ContinentMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapTitle,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,10,120,40),Message="continent"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeTerritoryAnchor,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(700,120,30,30),Message="home territory"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(180,20,34,34),Message="coordinate pin"}}.AsReadOnly()};
        private static GameDetectionResult ContinentMapWithFarTerritoryAnchor() => new GameDetectionResult{State=GameState.ContinentMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapTitle,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,10,120,40),Message="continent"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeTerritoryAnchor,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(1100,40,30,30),Message="far territory"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(180,20,34,34),Message="coordinate pin"}}.AsReadOnly()};
        private static GameDetectionResult UnverifiedPanel() => new GameDetectionResult{State=GameState.ResourceSearchPanel,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>().AsReadOnly()};
        private static GameDetectionResult PanelFromResourceTab() => new GameDetectionResult{State=GameState.ResourceSearchPanel,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ResourceTabUnselected,TemplateExists=true,Found=true,Message="resource tab"},new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,TemplateExists=true,Found=true,Message="search"}}.AsReadOnly()};
        private static void Assert(bool c,string m){if(!c)throw new Exception(m);} private static void Equal<T>(T e,T a,string m){if(!EqualityComparer<T>.Default.Equals(e,a))throw new Exception($"{m} Expected={e}, Actual={a}");} private static void Throws<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
        private sealed class Fixture{public FakeClient Client;public FakeDetector Detector;public WorldMapNavigationService Service;}
        private sealed class FakeLogger:IDiagnosticLogger{public void Info(string m){}public void Error(string m,Exception e){}}
        private sealed class FakeDetector:IGameStateDetector
        { private readonly Queue<GameDetectionResult> q; private GameDetectionResult last; private int active; public int DelayMs; public int MaxActive; public FakeDetector(params GameDetectionResult[] s){q=new Queue<GameDetectionResult>(s);last=s.Length>0?s[s.Length-1]:State(GameState.Unknown);} public async Task<GameDetectionResult> DetectAsync(string d,CancellationToken t){int now=Interlocked.Increment(ref active);MaxActive=Math.Max(MaxActive,now);try{if(DelayMs>0)await Task.Delay(DelayMs,t);lock(q){if(q.Count>0)last=q.Dequeue();return last;}}finally{Interlocked.Decrement(ref active);}} public GameDetectionResult Detect(byte[] p)=>last; }
        private sealed class FakeClient:ILdPlayerClient
        {public int BackCalls,TapCalls,ProhibitedCalls,LastX,LastY;public int TotalInput=>BackCalls+TapCalls+ProhibitedCalls;public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult<IReadOnlyList<string>>(new[]{"LDPlayer"});public Task BackAsync(string d,CancellationToken t){BackCalls++;return Task.CompletedTask;}public Task TapAsync(string d,int x,int y,CancellationToken t){TapCalls++;LastX=x;LastY=y;return Task.CompletedTask;}public Task<bool> IsRunningAsync(string d,CancellationToken t)=>Task.FromResult(true);public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t)=>Task.FromResult(new byte[1]);public Task OpenAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task CloseAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task RunAppAsync(string d,string p,CancellationToken t)=>Task.CompletedTask;public Task TapByPercentAsync(string d,double x,double y,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task LongPressAsync(string d,int x,int y,int ms,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task SwipeByPercentAsync(string d,double sx,double sy,double ex,double ey,int ms,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task InputTextAsync(string d,string s,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}}
    }
}
