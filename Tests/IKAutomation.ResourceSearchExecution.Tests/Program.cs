using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
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
            Run("Resource tab fallback allows Search tap", ResourceTabFallbackAllowsSearch);
            Run("Missing Search bounds fails without Tap", MissingSearchBounds);
            Run("Outcome prevents a second Tap", OutcomeStopsTap);
            Run("Retry is bounded", RetryBounded);
            Run("NotFound latch prevents retry", NotFoundNoRetry);
            Run("Both toast anchors produce NotFound", BothToastAnchors);
            Run("Alternate toast pair produces NotFound", AlternateToastPair);
            Run("Target-level-too-low toast switches resource", TargetLevelTooLowToast);
            Run("Target-level-too-low single anchor is insufficient", TargetLevelTooLowSingleAnchor);
            Run("Target-level-too-low toast prevents Search retry", TargetLevelTooLowNoRetry);
            Run("Alternate toast in one frame is latched", AlternateOneFrameToast);
            Run("Alternate toast latch survives disappearance", AlternateLatchSurvivesDisappearance);
            Run("Short toast anchor alone is insufficient", ShortOnly);
            Run("Other-region toast anchor alone is insufficient", OtherRegionOnly);
            Run("Alternate toast anchors too far apart are ambiguous", AlternateToastYFar);
            Run("Alternate pair in ROI with panel open is verified", AlternatePairInRoiWithPanel);
            Run("Alternate pair accepts adjacent panel confirmation", AlternatePairWithAdjacentPanel);
            Run("Alternate templates are resource and level independent", AlternateTemplatesAreGeneric);
            Run("Alternate NotFound prevents Search retry", AlternateNotFoundNoRetry);
            Run("First toast capture waits for render after Search tap", FirstToastCaptureWaitsForRender);
            Run("Immediate transient toast is captured before fast-poll delay", ImmediateTransientToast);
            Run("Missing alternate templates preserve legacy variant", MissingAlternateTemplatesPreserveLegacy);
            Run("Service has no default cancellation token bypass", NoCancellationNone);
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
            Run("Open panel after one attempt is Timeout", OpenPanelTimeout);
            Run("Verified retries infer NotFound when transient toast is missed", RetryPanelNoChangeInfersNotFound);
            Run("Verified retries infer NotFound after transient panel change", RetryPanelChangeInfersNotFound);
            Run("Verified retries infer NotFound from a partial toast anchor", RetryPartialToastInfersNotFound);
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
            Run("ResourcePopupReady ends search early", PopupReadyEndsEarly);
            Run("ResourcePopupReady returns popup final state", PopupReadyFinalState);
            Run("ResourcePopupReady supersedes camera stability at observation timeout", PopupReadyAtTimeout);
            Run("WorldMap overlay popup is verified without camera movement", PopupReadyWithoutMovement);
            Run("ResourceNotFound has priority over ResourcePopup", NotFoundBeforePopup);
            Run("Popup integration sends no Gather input", PopupSendsNoGatherInput);
            Console.WriteLine($"Resource search execution tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        { try { test(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL: " + name + " - " + e); } }

        private static void ConfigurationFailure() { Fixture f=Setup(); f.Configuration.Success=false; var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.Failed,"outcome"); Eq(0,f.Client.TapCalls,"tap"); }
        private static void SearchCenter() { Fixture f=ToastFixture(); Execute(f); Eq(110,f.Client.LastX,"x"); Eq(220,f.Client.LastY,"y"); }
        private static void ResourceTabFallbackAllowsSearch() { Fixture f=Setup(windowMs:3); f.Detector.SetStates(PanelFromResourceTab(),World()); var r=Execute(f); Eq(1,r.SearchTapCount,"search tap count"); Eq(1,f.Client.TapCalls,"client tap count"); }
        private static void MissingSearchBounds() { Fixture f=Setup(); f.Matcher.InvalidSearchBounds=true; var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.Failed,"outcome"); Eq(0,f.Client.TapCalls,"tap"); }
        private static void OutcomeStopsTap() { Fixture f=ToastFixture(); Execute(f); Eq(1,f.Client.TapCalls,"tap"); }
        private static void RetryBounded() { Fixture f=Setup(maxAttempts:2); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound,"outcome"); Eq(2,f.Client.TapCalls,"tap"); }
        private static void NotFoundNoRetry() { Fixture f=ToastFixture(); var r=Execute(f); Is(r.NotFoundObserved,"latch"); Eq(1,f.Client.TapCalls,"tap"); }
        private static void BothToastAnchors() { var r=Execute(ToastFixture()); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound,"outcome"); }
        private static void AlternateToastPair() { var r=Execute(AlternateToastFixture()); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound&&r.MatchedNotFoundVariant=="SearchOtherRegion","outcome"); }
        private static void TargetLevelTooLowToast() { var r=Execute(TargetLevelTooLowToastFixture()); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound&&r.MatchedNotFoundVariant=="TargetLevelTooLow","outcome"); Is(r.NotFoundToastVerified,"toast"); }
        private static void TargetLevelTooLowSingleAnchor() { Fixture f=TargetLevelTooLowToastFixture(maxAttempts:1); f.Matcher.SeasonMap=false; Is(!Execute(f).NotFoundObserved,"latch"); }
        private static void TargetLevelTooLowNoRetry() { Fixture f=TargetLevelTooLowToastFixture(); Execute(f); Eq(1,f.Client.TapCalls,"tap"); }
        private static void AlternateOneFrameToast() { Fixture f=AlternateToastFixture(); var r=Execute(f); Is(r.NotFoundObserved,"latch"); Eq(1,r.ObservedFrameCount,"frames"); }
        private static void AlternateLatchSurvivesDisappearance() { Fixture f=AlternateToastFixture(); f.Matcher.ToastFrames.Clear(); f.Matcher.ToastFrames.Add(2); var r=Execute(f); Is(r.NotFoundObserved&&r.NotFoundToastVerified,"latch"); Eq(1,r.ObservedFrameCount,"poll stopped"); }
        private static void ShortOnly() { Fixture f=AlternateToastFixture(maxAttempts:1); f.Matcher.Other=false; Is(!Execute(f).NotFoundObserved,"latch"); }
        private static void OtherRegionOnly() { Fixture f=AlternateToastFixture(maxAttempts:1); f.Matcher.Short=false; Is(!Execute(f).NotFoundObserved,"latch"); }
        private static void AlternateToastYFar() { Fixture f=AlternateToastFixture(maxAttempts:1); f.Matcher.OtherY=400; Is(!Execute(f).NotFoundObserved,"latch"); }
        private static void AlternatePairInRoiWithPanel() { Fixture f=AlternateToastFixture(); var r=Execute(f); Is(r.NotFoundToastVerified&&r.Observations[0].SearchPanelConfirmed,"verification"); }
        private static void AlternatePairWithAdjacentPanel() { Fixture f=AlternateToastFixture(); f.Detector.SetStates(Panel(),World()); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound&&!r.Observations[0].SearchPanelConfirmed,"adjacent panel"); }
        private static void AlternateTemplatesAreGeneric() { var registry=new TemplateRegistry(Path.GetTempPath()); foreach(TemplateId id in new[]{TemplateId.ResourceNotFoundToastShortAnchor,TemplateId.ResourceNotFoundToastOtherRegionAnchor}){string path=registry.GetDefinition(id).RelativePath.ToLowerInvariant();Is(!path.Contains("iron")&&!path.Contains("wood")&&!path.Contains("stone")&&!path.Contains("lv"),"resource-specific template");} }
        private static void AlternateNotFoundNoRetry() { Fixture f=AlternateToastFixture(); Execute(f); Eq(1,f.Client.TapCalls,"tap"); }
        private static void FirstToastCaptureWaitsForRender()
        {
            Fixture f=Setup(maxAttempts:1,windowMs:50,fastPollMs:20);
            f.Matcher.Short=true; f.Matcher.Other=true; f.Matcher.RequirePostTapReadyCapture=true;
            f.Client.MinimumPostTapCaptureDelayMs=10;
            var result=Execute(f);
            Is(result.Outcome==ResourceSearchOutcome.ResourceNotFound,"outcome");
            Eq(1,result.SearchTapCount,"retry");
        }
        private static void ImmediateTransientToast()
        {
            Fixture f=Setup(maxAttempts:1,windowMs:80,fastPollMs:30);
            f.Matcher.Short=true; f.Matcher.Other=true; f.Matcher.RequireImmediatePostTapCapture=true;
            f.Client.MaximumPostTapCaptureDelayMs=10;
            var result=Execute(f);
            Is(result.Outcome==ResourceSearchOutcome.ResourceNotFound,"outcome");
            Eq(1,result.ObservedFrameCount,"frames");
        }
        private static void MissingAlternateTemplatesPreserveLegacy() { Fixture f=ToastFixture(); f.Registry.MissingAlternate=true; var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound&&r.MatchedNotFoundVariant=="LegacyMoveArea","legacy"); }
        private static void NoCancellationNone() { string source=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","Infrastructure","ResourceSearch","ResourceSearchExecutionService.cs")); Is(!source.Contains("CancellationToken"+".None"),"token bypass"); }
        private static void OneFrameToast() { Fixture f=ToastFixture(); f.Matcher.ToastFrames.Add(2); var r=Execute(f); Is(r.NotFoundObserved,"latch"); Eq(1,r.ObservedFrameCount,"frames"); }
        private static void NoConsecutiveToastRequirement() { var r=Execute(ToastFixture()); Eq(1,r.ObservedFrameCount,"frames"); }
        private static void ToastYWithin() { Fixture f=ToastFixture(); f.Matcher.ActionY=250; var r=Execute(f); Is(r.NotFoundToastVerified,"toast"); }
        private static void ToastYFar() { Fixture f=ToastFixture(maxAttempts:1); f.Matcher.ActionY=400; var r=Execute(f); Is(!r.NotFoundObserved,"latch"); }
        private static void ToastOutsideRoi() { Fixture f=ToastFixture(maxAttempts:1); f.Matcher.ToastOutsideRegion=true; var r=Execute(f); Is(!r.NotFoundObserved,"outside toast"); }
        private static void PrimaryOnly() { Fixture f=ToastFixture(maxAttempts:1); f.Matcher.Action=false; Is(!Execute(f).NotFoundObserved,"latch"); }
        private static void ActionOnly() { Fixture f=ToastFixture(maxAttempts:1); f.Matcher.Primary=false; Is(!Execute(f).NotFoundObserved,"latch"); }
        private static void ResourceNameIndependent() { var r=Execute(ToastFixture()); Is(r.NotFoundObserved,"generic anchors"); }
        private static void NotFoundPriority() { Fixture f=LocatedFixture(); f.Matcher.Primary=true; f.Matcher.Action=true; f.Matcher.ToastFrames.Add(2); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound,"priority"); }
        private static void LatchRemainsTrue() { var r=Execute(ToastFixture()); Is(r.NotFoundObserved&&r.NotFoundToastVerified,"latch"); }
        private static void NotFoundNotException() { ResourceSearchExecutionResult r=Execute(ToastFixture()); Is(r.ErrorMessage==null,"error"); }
        private static void OpenPanelNotLocated() { var r=Execute(Setup()); Is(!r.Success,"success"); }
        private static void OpenPanelTimeout() { var r=Execute(Setup(maxAttempts:1)); Is(r.Outcome==ResourceSearchOutcome.Timeout,"outcome"); }
        private static void RetryPanelNoChangeInfersNotFound() { Fixture f=Setup(maxAttempts:2,windowMs:3); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound&&r.NotFoundObserved,"outcome"); Is(!r.NotFoundToastVerified&&r.MatchedNotFoundVariant=="VerifiedRetryPanelStayedOpen","inference evidence"); Eq(2,r.SearchTapCount,"bounded retries"); }
        private static void RetryPanelChangeInfersNotFound() { Fixture f=Setup(maxAttempts:2,windowMs:3); f.Stability.Differences.Enqueue(.05); f.Stability.Differences.Enqueue(.05); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound&&r.NotFoundObserved,"outcome"); Is(!r.NotFoundToastVerified&&r.MatchedNotFoundVariant=="VerifiedRetryPanelStayedOpen","inference evidence"); Eq(2,r.SearchTapCount,"bounded retries"); Is(!r.CameraMovementObserved,"panel animation was treated as camera movement"); }
        private static void RetryPartialToastInfersNotFound() { Fixture f=Setup(maxAttempts:2,windowMs:3); f.Matcher.Short=true; f.Matcher.ToastFrames.Add(2); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound&&r.NotFoundObserved,"outcome"); Is(!r.NotFoundToastVerified&&r.MatchedNotFoundVariant=="PartialToastPanelStayedOpen","partial evidence"); Eq(2,r.SearchTapCount,"bounded retries"); }
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
        private static void PopupReadyEndsEarly() { Fixture f=PopupFixture(); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceLocated&&r.Success,"outcome"); Eq(1,r.ObservedFrameCount,"frames"); }
        private static void PopupReadyFinalState() { var r=Execute(PopupFixture()); Is(r.FinalState==GameState.ResourcePopup&&r.PanelClosed,"popup final"); }
        private static void PopupReadyAtTimeout() { Fixture f=Setup(requiredStable:100,windowMs:5); f.Detector.SetStates(Panel(),World(),World(),World()); f.Stability.Differences.Enqueue(.1); f.Stability.Differences.Enqueue(.001); f.Stability.Differences.Enqueue(.001); f.PopupVerifier.Result=ReadyPopup(); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceLocated&&r.Success,"outcome"); Is(r.FinalState==GameState.ResourcePopup,"final state"); Eq(1,f.PopupVerifier.Calls,"popup verification"); Eq(1,f.Client.TapCalls,"Search tap only"); }
        private static void PopupReadyWithoutMovement() { Fixture f=Setup(requiredStable:1,windowMs:5); f.Detector.SetStates(Panel(),World(),World()); f.Stability.Differences.Enqueue(.001); f.Stability.Differences.Enqueue(.001); f.PopupVerifier.Result=ReadyPopup(); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceLocated&&r.Success,"outcome"); Is(!r.CameraMovementObserved,"movement"); Is(r.FinalState==GameState.ResourcePopup,"final state"); Eq(1,f.PopupVerifier.Calls,"popup verification"); Eq(1,f.Client.TapCalls,"Search tap only"); }
        private static void NotFoundBeforePopup() { Fixture f=PopupFixture(); f.Matcher.Primary=true; f.Matcher.Action=true; f.Matcher.ToastFrames.Add(2); var r=Execute(f); Is(r.Outcome==ResourceSearchOutcome.ResourceNotFound,"priority"); Eq(0,f.PopupVerifier.Calls,"popup calls"); }
        private static void PopupSendsNoGatherInput() { Fixture f=PopupFixture(); Execute(f); Eq(1,f.Client.TapCalls,"Search tap only"); Eq(0,f.Client.ProhibitedCalls,"prohibited"); }

        private static Fixture ToastFixture(bool saveResult=false,int maxAttempts=2) { Fixture f=Setup(saveResult:saveResult,maxAttempts:maxAttempts); f.Matcher.Primary=true; f.Matcher.Action=true; f.Matcher.ToastFrames.Add(2); return f; }
        private static Fixture AlternateToastFixture(int maxAttempts=2) { Fixture f=Setup(maxAttempts:maxAttempts); f.Matcher.Short=true; f.Matcher.Other=true; f.Matcher.ToastFrames.Add(2); return f; }
        private static Fixture TargetLevelTooLowToastFixture(int maxAttempts=2) { Fixture f=Setup(maxAttempts:maxAttempts); f.Matcher.TargetLevelTooLow=true; f.Matcher.SeasonMap=true; f.Matcher.ToastFrames.Add(2); return f; }
        private static Fixture LocatedFixture() { Fixture f=Setup(requiredStable:3,windowMs:30); f.Detector.SetStates(Panel(),World(),World(),World(),World()); f.Stability.Differences.Enqueue(.1); f.Stability.Differences.Enqueue(.001); f.Stability.Differences.Enqueue(.001); f.Stability.Differences.Enqueue(.001); return f; }
        private static Fixture PopupFixture() { Fixture f=Setup(windowMs:30); f.Detector.SetStates(Panel(),Popup()); f.PopupVerifier.Result=ReadyPopup(); return f; }

        private static Fixture Setup(int maxAttempts=2,int maxUnknown=5,int requiredStable=3,int windowMs=5,bool saveResult=false,bool burst=false,int burstMax=2,int fastPollMs=1)
        {
            var client=new FakeClient{Frame=Png(1280,720,Color.Black)}; var config=new FakeConfiguration(); var detector=new FakeDetector(); var registry=new FakeRegistry(); var matcher=new FakeMatcher(client); var stability=new FakeStability(); var store=new FakeStore(); var popup=new FakePopupVerifier();
            var service=new ResourceSearchExecutionService(config,detector,client,registry,matcher,stability,new DeviceOperationLock(),Options(maxAttempts,maxUnknown,requiredStable,windowMs,saveResult,burst,burstMax,fastPollMs),store,new FakeLogger(),popup);
            return new Fixture{Client=client,Configuration=config,Detector=detector,Registry=registry,Matcher=matcher,Stability=stability,Store=store,PopupVerifier=popup,Service=service};
        }
        private static ResourceSearchExecutionOptions Options(int maxAttempts=2,int maxUnknown=5,int requiredStable=3,int windowMs=5,bool saveResult=false,bool burst=false,int burstMax=2,int fastPollMs=1,double movement=.04,double stable=.015) => new ResourceSearchExecutionOptions(windowMs,fastPollMs,1,1,maxAttempts,1,new ImageRegion(220,120,840,360),140,movement,stable,requiredStable,maxUnknown,saveResult,burst,burstMax,"Diagnostics/SearchResults","Diagnostics/SearchObservation",1280,720,new ImageRegion(160,80,960,440));
        private static ResourceSearchExecutionRequest Request()=>new ResourceSearchExecutionRequest{ConfigureBeforeSearch=true,Configuration=new ResourceSearchConfigurationRequest{ResourceType=ResourceType.Iron,TargetLevel=7,UnoccupiedOnly=true}};
        private static ResourceSearchExecutionResult Execute(Fixture f,ResourceSearchExecutionRequest q=null,CancellationToken? token=null)=>f.Service.ExecuteAsync("LDPlayer",q??Request(),token??Token).GetAwaiter().GetResult();

        private static GameDetectionResult Panel()=>State(GameState.ResourceSearchPanel);
        private static GameDetectionResult PanelFromResourceTab()=>new GameDetectionResult{State=GameState.ResourceSearchPanel,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ResourceTabUnselected,Found=true},new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,Found=true}}.AsReadOnly()};
        private static GameDetectionResult World()=>State(GameState.WorldMap);
        private static GameDetectionResult Popup()=>State(GameState.ResourcePopup);
        private static ResourcePopupVerificationResult ReadyPopup()=>new ResourcePopupVerificationResult{Outcome=ResourcePopupOutcome.ResourcePopupReady,Success=true,InitialState=GameState.ResourcePopup,FinalState=GameState.ResourcePopup,Evidence=new GameDetectionEvidence[0],Message="ready"};
        private static GameDetectionResult State(GameState state)
        { var e=new List<GameDetectionEvidence>(); if(state==GameState.ResourceSearchPanel){e.Add(new GameDetectionEvidence{TemplateId=TemplateId.LevelMinusButton,Found=true});e.Add(new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,Found=true});} if(state==GameState.WorldMap)e.Add(new GameDetectionEvidence{TemplateId=TemplateId.WorldMapAnchor,Found=true}); return new GameDetectionResult{State=state,IsSuccessful=true,Evidence=e.AsReadOnly()}; }
        private static byte[] Png(int w,int h,Color c) { using(var b=new Bitmap(w,h)){using(Graphics g=Graphics.FromImage(b))g.Clear(c);using(var s=new MemoryStream()){b.Save(s,ImageFormat.Png);return s.ToArray();}} }
        private static byte[] PngWithCorner() { using(var b=new Bitmap(32,32)){using(Graphics g=Graphics.FromImage(b)){g.Clear(Color.Black);g.FillRectangle(Brushes.White,16,16,16,16);}using(var s=new MemoryStream()){b.Save(s,ImageFormat.Png);return s.ToArray();}} }
        private static void Is(bool c,string m){if(!c)throw new Exception(m);} private static void Eq<T>(T e,T a,string m){if(!EqualityComparer<T>.Default.Equals(e,a))throw new Exception($"{m}: expected={e}, actual={a}");} private static void Throws<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}

        private sealed class Fixture { public FakeClient Client; public FakeConfiguration Configuration; public FakeDetector Detector; public FakeRegistry Registry; public FakeMatcher Matcher; public FakeStability Stability; public FakeStore Store; public FakePopupVerifier PopupVerifier; public IResourceSearchExecutionService Service; }
        private sealed class FakePopupVerifier:IResourcePopupVerificationService
        { public int Calls; public ResourcePopupVerificationResult Result=new ResourcePopupVerificationResult{Outcome=ResourcePopupOutcome.ResourcePopupNotDetected,Evidence=new GameDetectionEvidence[0],Message="not detected"}; public Task<ResourcePopupVerificationResult> VerifyAsync(string d,CancellationToken t){Calls++;return Task.FromResult(Result);} }
        private sealed class FakeConfiguration:IResourceSearchConfigurationService
        { private int active; public bool Success=true; public int DelayMs,MaxActive; public async Task<ResourceSearchConfigurationResult> ConfigureAsync(string d,ResourceSearchConfigurationRequest q,CancellationToken t){int n=Interlocked.Increment(ref active);MaxActive=Math.Max(MaxActive,n);try{if(DelayMs>0)await Task.Delay(DelayMs,t);return new ResourceSearchConfigurationResult{Success=Success,InitialState=GameState.WorldMap,FinalState=GameState.ResourceSearchPanel,ErrorMessage=Success?null:"configuration failed",Steps=new ConfigurationStepResult[0]};}finally{Interlocked.Decrement(ref active);}} }
        private sealed class FakeDetector:IGameStateDetector
        { private readonly Queue<GameDetectionResult> states=new Queue<GameDetectionResult>(); private GameDetectionResult last=Panel(); private int calls; public int ErrorAtCall; public GameDetectionResult AsyncResult=Panel(); public void SetStates(params GameDetectionResult[] s){states.Clear();foreach(var x in s)states.Enqueue(x);if(s.Length>0)last=s[s.Length-1];} public Task<GameDetectionResult> DetectAsync(string d,CancellationToken t)=>Task.FromResult(AsyncResult); public GameDetectionResult Detect(byte[] p){calls++;if(ErrorAtCall==calls)return new GameDetectionResult{State=GameState.Unknown,IsSuccessful=false,Evidence=new GameDetectionEvidence[0],ErrorMessage="detector error"};if(states.Count>0)return states.Dequeue();return last;} }
        private sealed class FakeRegistry:ITemplateRegistry
        { public TemplateId? Missing; public bool MissingAlternate; public TemplateDefinition GetDefinition(TemplateId id)=>new TemplateDefinition(id,"Search/"+id+".png",.8); public string GetPath(TemplateId id)=>Path.Combine("templates",id+".png"); public byte[] LoadBytes(TemplateId id)=>new[]{(byte)id}; public bool Exists(TemplateId id)=>Missing!=id&&(!MissingAlternate||(id!=TemplateId.ResourceNotFoundToastShortAnchor&&id!=TemplateId.ResourceNotFoundToastOtherRegionAnchor)); }
        private sealed class FakeMatcher:IImageMatcher
        { private readonly FakeClient client; private int searchMatches; public bool Primary,Action,Short,Other,TargetLevelTooLow,SeasonMap,InvalidSearchBounds,ToastOutsideRegion,MoveSearchOnRetry,RequirePostTapReadyCapture,RequireImmediatePostTapCapture; public int PrimaryY=200,ActionY=230,ShortY=200,OtherY=230,TargetLevelTooLowY=200,SeasonMapY=200; public HashSet<int> ToastFrames=new HashSet<int>(); public FakeMatcher(FakeClient c){client=c;} public ImageMatchResult Find(byte[] s,byte[] t,ImageRegion? r=null){TemplateId id=(TemplateId)t[0];if(id==TemplateId.SearchButtonEnabled){searchMatches++;int x=MoveSearchOnRetry&&searchMatches>1?200:100;return InvalidSearchBounds?ImageMatchResult.FoundAt(x,200,0,0):ImageMatchResult.FoundAt(x,200,20,40);}bool active=(ToastFrames.Count==0||ToastFrames.Contains(client.CaptureCount))&&(!RequirePostTapReadyCapture||client.LastCaptureWasPostTapReady)&&(!RequireImmediatePostTapCapture||client.LastCaptureWasWithinPostTapWindow);if(ToastOutsideRegion&&r.HasValue)return ImageMatchResult.NotFound();if(id==TemplateId.ResourceNotFoundToastAnchor&&Primary&&active)return ImageMatchResult.FoundAt(300,PrimaryY,100,20);if(id==TemplateId.ResourceNotFoundToastActionAnchor&&Action&&active)return ImageMatchResult.FoundAt(320,ActionY,100,20);if(id==TemplateId.ResourceNotFoundToastShortAnchor&&Short&&active)return ImageMatchResult.FoundAt(280,ShortY,100,20);if(id==TemplateId.ResourceNotFoundToastOtherRegionAnchor&&Other&&active)return ImageMatchResult.FoundAt(500,OtherY,140,20);if(id==TemplateId.ResourceTargetLevelTooLowToastAnchor&&TargetLevelTooLow&&active)return ImageMatchResult.FoundAt(300,TargetLevelTooLowY,180,20);if(id==TemplateId.ResourceTargetLevelSeasonMapToastAnchor&&SeasonMap&&active)return ImageMatchResult.FoundAt(650,SeasonMapY,180,20);return ImageMatchResult.NotFound();} }
        private sealed class FakeStability:IFrameStabilityDetector
        { public Queue<double> Differences=new Queue<double>(); public FrameComparisonResult Compare(byte[] a,byte[] b,ImageRegion? r=null){double d=Differences.Count>0?Differences.Dequeue():0;return new FrameComparisonResult{DifferenceRatio=d,IsStable=d<=.015};} }
        private sealed class FakeStore:IResourceSearchDiagnosticStore
        { public bool Throw; public int ObservationSaves; public Task<string> SaveResultAsync(string d,ResourceSearchOutcome o,byte[] p,CancellationToken t){if(Throw)throw new IOException("save failed");return Task.FromResult("result.png");} public Task SaveObservationAsync(string d,DateTimeOffset ts,int i,byte[] p,CancellationToken t){ObservationSaves++;return Task.CompletedTask;} }
        private sealed class FakeClient:ILdPlayerClient
        { public byte[] Frame; public int CaptureCount,TapCalls,LastX,LastY,ProhibitedCalls,MinimumPostTapCaptureDelayMs,MaximumPostTapCaptureDelayMs; public bool LastCaptureWasPostTapReady=true,LastCaptureWasWithinPostTapWindow=true; private DateTimeOffset lastTapAt; public List<int> TapXs=new List<int>(); public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();CaptureCount++;double elapsed=TapCalls==0?0:(DateTimeOffset.UtcNow-lastTapAt).TotalMilliseconds;LastCaptureWasPostTapReady=TapCalls==0||elapsed>=MinimumPostTapCaptureDelayMs;LastCaptureWasWithinPostTapWindow=TapCalls==0||MaximumPostTapCaptureDelayMs<=0||elapsed<=MaximumPostTapCaptureDelayMs;return Task.FromResult(Frame);} public Task TapAsync(string d,int x,int y,CancellationToken t){t.ThrowIfCancellationRequested();TapCalls++;LastX=x;LastY=y;TapXs.Add(x);lastTapAt=DateTimeOffset.UtcNow;return Task.CompletedTask;} public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult<IReadOnlyList<string>>(new[]{"LDPlayer"}); public Task<bool> IsRunningAsync(string d,CancellationToken t)=>Task.FromResult(true); public Task OpenAsync(string d,CancellationToken t)=>Task.CompletedTask; public Task CloseAsync(string d,CancellationToken t)=>Task.CompletedTask; public Task RunAppAsync(string d,string p,CancellationToken t)=>Task.CompletedTask; public Task TapByPercentAsync(string d,double x,double y,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} public Task LongPressAsync(string d,int x,int y,int ms,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} public Task SwipeByPercentAsync(string d,double sx,double sy,double ex,double ey,int ms,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} public Task BackAsync(string d,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} public Task InputTextAsync(string d,string s,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;} }
        private sealed class FakeLogger:IDiagnosticLogger { public void Info(string m){} public void Error(string m,Exception e){} }
    }
}
