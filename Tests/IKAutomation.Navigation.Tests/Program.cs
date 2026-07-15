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
            Run("Unknown fails without input", UnknownNoInput);
            Run("Already-open panel succeeds without Tap", AlreadyPanel);
            Run("Open panel taps exact evidence center", TapEvidenceCenter);
            Run("Tap success requires verified panel", TapRequiresVerification);
            Run("Timeout returns failure", TimeoutFailure);
            Run("Retry never exceeds configured maximum", RetryBounded);
            Run("Unknown after Tap prevents retry", UnknownPreventsRetry);
            Run("Missing bounds prevents Tap", MissingBounds);
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
        private static void UnknownNoInput() { var f=Setup(State(GameState.Unknown)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unexpected success."); Equal(0,f.Client.TotalInput,"Blind input."); }
        private static void AlreadyPanel() { var f=Setup(State(GameState.ResourceSearchPanel)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Failed."); Equal(0,f.Client.TapCalls,"Extra Tap."); }
        private static void TapEvidenceCenter() { var f=Setup(State(GameState.WorldMap, true), State(GameState.ResourceSearchPanel)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Failed."); Equal(25,f.Client.LastX,"Tap X."); Equal(40,f.Client.LastY,"Tap Y."); }
        private static void TapRequiresVerification() { var f=Setup(State(GameState.WorldMap,true), State(GameState.Unknown)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Tap alone counted as success."); }
        private static void TimeoutFailure() { var f=SetupWithOptions(new WorldMapNavigationOptions(250,1,1), State(GameState.WorldMap,true)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Timeout should fail."); Equal(1,f.Client.TapCalls,"Tap count."); }
        private static void RetryBounded() { var states=new List<GameDetectionResult>{State(GameState.WorldMap,true)}; for(int i=0;i<8;i++)states.Add(State(GameState.WorldMap,true)); var f=SetupWithOptions(new WorldMapNavigationOptions(250,1,2),states.ToArray()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Expected failure."); Equal(2,f.Client.TapCalls,"Exceeded retry max."); }
        private static void UnknownPreventsRetry() { var f=SetupWithOptions(new WorldMapNavigationOptions(10,1,3),State(GameState.WorldMap,true),State(GameState.Unknown)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Expected failure."); Equal(1,f.Client.TapCalls,"Retried after Unknown."); }
        private static void MissingBounds() { var f=Setup(State(GameState.WorldMap,false)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Expected failure."); Equal(0,f.Client.TapCalls,"Fallback Tap sent."); }
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
        private static GameDetectionResult State(GameState state,bool bounds=false) { var evidence=new List<GameDetectionEvidence>(); if(state==GameState.WorldMap||bounds)evidence.Add(new GameDetectionEvidence{TemplateId=TemplateId.WorldMapAnchor,TemplateExists=true,Found=bounds,MatchResult=bounds?ImageMatchResult.FoundAt(10,20,30,40):null,Message="world"}); return new GameDetectionResult{State=state,IsSuccessful=true,Evidence=evidence.AsReadOnly()}; }
        private static void Assert(bool c,string m){if(!c)throw new Exception(m);} private static void Equal<T>(T e,T a,string m){if(!EqualityComparer<T>.Default.Equals(e,a))throw new Exception($"{m} Expected={e}, Actual={a}");} private static void Throws<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
        private sealed class Fixture{public FakeClient Client;public FakeDetector Detector;public WorldMapNavigationService Service;}
        private sealed class FakeLogger:IDiagnosticLogger{public void Info(string m){}public void Error(string m,Exception e){}}
        private sealed class FakeDetector:IGameStateDetector
        { private readonly Queue<GameDetectionResult> q; private GameDetectionResult last; private int active; public int DelayMs; public int MaxActive; public FakeDetector(params GameDetectionResult[] s){q=new Queue<GameDetectionResult>(s);last=s.Length>0?s[s.Length-1]:State(GameState.Unknown);} public async Task<GameDetectionResult> DetectAsync(string d,CancellationToken t){int now=Interlocked.Increment(ref active);MaxActive=Math.Max(MaxActive,now);try{if(DelayMs>0)await Task.Delay(DelayMs,t);lock(q){if(q.Count>0)last=q.Dequeue();return last;}}finally{Interlocked.Decrement(ref active);}} public GameDetectionResult Detect(byte[] p)=>last; }
        private sealed class FakeClient:ILdPlayerClient
        {public int BackCalls,TapCalls,ProhibitedCalls,LastX,LastY;public int TotalInput=>BackCalls+TapCalls+ProhibitedCalls;public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult<IReadOnlyList<string>>(new[]{"LDPlayer"});public Task BackAsync(string d,CancellationToken t){BackCalls++;return Task.CompletedTask;}public Task TapAsync(string d,int x,int y,CancellationToken t){TapCalls++;LastX=x;LastY=y;return Task.CompletedTask;}public Task<bool> IsRunningAsync(string d,CancellationToken t)=>Task.FromResult(true);public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t)=>Task.FromResult(new byte[1]);public Task OpenAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task CloseAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task RunAppAsync(string d,string p,CancellationToken t)=>Task.CompletedTask;public Task TapByPercentAsync(string d,double x,double y,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task LongPressAsync(string d,int x,int y,int ms,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task SwipeByPercentAsync(string d,double sx,double sy,double ex,double ey,int ms,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task InputTextAsync(string d,string s,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}}
    }
}
