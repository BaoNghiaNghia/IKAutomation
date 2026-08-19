using IK_Auto_ADB.Core.Abstractions;
using IK_Auto_ADB.Core.Concurrency;
using IK_Auto_ADB.Core.Diagnostics;
using IK_Auto_ADB.Core.GameDetection;
using IK_Auto_ADB.Core.ResourceSearch;
using IK_Auto_ADB.Core.Vision;
using IK_Auto_ADB.Infrastructure.Concurrency;
using IK_Auto_ADB.Infrastructure.ResourceSearch;
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
        Run("Inferred not-found skips nonexistent toast clear", InferredNotFoundSkipsToastClear);
        Run("Toast anchor resets clear count", ToastResetsClearCount);
        Run("Two clear panel frames verify clear", TwoClearFrames);
        Run("One verified clear panel frame can release transient toast latch", OneClearFrame);
        Run("Toast clear timeout stops next level", ClearTimeout);
        Run("Closed panel stops without input", ClosedPanel);
        Run("Configure receives exact levels", ConfigureLevels);
        Run("Execute never configures twice", ConfigureBeforeFalse);
        Run("Verified panel handoff skips full detector", PanelReadySkipsFullDetector);
        Run("Each attempt configures and executes once", OncePerAttempt);
        Run("Configuration failure stops lower levels", ConfigurationFailure);
        Run("Unavailable requested level continues at verified lower level", UnavailableLevelFallsBack);
        Run("Account ceiling outside priority becomes the effective level", CeilingOutsidePriorityExhausts);
        Run("Verified account ceiling is reused within the same run", CeilingReusedWithinRun);
        Run("Unconfirmed observed level does not skip configured levels", UnconfirmedCeilingDoesNotSkipLevels);
        Run("Lower account ceiling preserves configured search depth", LowerCeilingPreservesSearchDepth);
        Run("Cached account ceiling preserves configured search depth", CachedCeilingPreservesSearchDepth);
        Run("Not-found toast during configuration enters mapped point flow", ConfigurationToastSwitchesResource);
        Run("Target-level-too-low toast during configuration enters mapped point flow", ConfigurationTargetLevelTooLowToastSwitchesResource);
        Run("Target-level-too-low toast enters mapped point flow", TargetLevelTooLowSearchSkipsRemainingLevels);
        Run("Search-other-region toast enters mapped point flow", SearchOtherRegionSearchSkipsRemainingLevels);
        Run("ResourceAreaLv2 redirect preserves dedicated outcome", ResourceAreaLv2RedirectPreservesOutcome);
        Run("All verified not-found toast variants route to mapped points", AllVerifiedNotFoundVariantsRouteToMappedPoints);
        Run("Verified generic not-found redirects to mapped city-area points", VerifiedGenericNotFoundRedirectsToMappedPoints);
        Run("Generic not-found start redirects to mapped city-area points", GenericNotFoundStartRedirectsToMappedPoints);
        Run("Unsupported resource level does not enter mapped point flow", UnsupportedLevelDoesNotRedirectToMappedPoints);
        Run("ResourceAreaLv2 city pools contain the configured map coordinates", ResourceAreaLv2CityPoolsContainConfiguredPoints);
        Run("ResourceAreaLv2 resource levels select the intended city pools", ResourceAreaLv2ResourceLevelsSelectCityPools);
        Run("ResourceAreaLv2 map coordinates are not screen scaled", ResourceAreaLv2MapCoordinatesAreNotScreenScaled);
        Run("Ignored Search tap yields to next resource", SearchTapNotAppliedYieldsResource);
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
    static void InferredNotFoundSkipsToastClear(){var h=new H(clearTimeout:1,poll:10);h.Search.NotFoundToastVerified=false;h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);for(int i=1;i<500;i++)h.Matcher.ToastFrames.Add(i);var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceLocated,r.Outcome,"outcome");Eq(2,h.Config.Calls,"next level");Is(r.Attempts[1].ToastClearResult==null,"must not wait for an unobserved toast");}
    static void ToastResetsClearCount(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);h.Matcher.ToastFrames.Add(2);var r=Go(h);Is(r.Attempts[1].ToastClearResult.ObservedFrames>=4,"reset not observed");}
    static void TwoClearFrames(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Go(h);Eq(2,r.Attempts[1].ToastClearResult.ConsecutiveClearFrames,"frames");}
    static void OneClearFrame(){var h=new H(requiredClear:1);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var r=Go(h);Is(r.Attempts[1].ToastClearVerifiedBeforeAttempt,"clear");Eq(1,r.Attempts[1].ToastClearResult.ObservedFrames,"frames");}
    static void ClearTimeout(){var h=new H(clearTimeout:1,poll:10);h.Search.Default=ResourceSearchOutcome.ResourceNotFound;for(int i=1;i<500;i++)h.Matcher.ToastFrames.Add(i);var r=Go(h);Eq(ResourceLevelFallbackOutcome.SearchFailed,r.Outcome,"outcome");Eq(1,h.Config.Calls,"next configured");}
    static void ClosedPanel(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Matcher.PanelClosedFrames.Add(1);var r=Go(h);Eq(ResourceLevelFallbackOutcome.PanelUnavailable,r.Outcome,"outcome");Eq(0,h.Client.InputCalls,"input");}
    static void ConfigureLevels(){DefaultOrder();}
    static void ConfigureBeforeFalse(){var h=new H();Go(h);Is(h.Search.Requests.All(x=>!x.ConfigureBeforeSearch),"double config");}
    static void PanelReadySkipsFullDetector(){var h=new H();var p=new ResourceLevelFallbackPolicy{PanelReady=true};Go(h,p);Eq(0,h.Detector.Calls,"full detector calls");}
    static void OncePerAttempt(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Eq(r.Attempts.Count,h.Config.Calls,"config");Eq(r.Attempts.Count,h.Search.Calls,"search");}
    static void ConfigurationFailure(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Config.FailLevel=6;var r=Go(h);Eq(ResourceLevelFallbackOutcome.ConfigurationFailed,r.Outcome,"outcome");Is(!h.Config.Levels.Contains(5),"level5");}
    static void UnavailableLevelFallsBack(){var h=new H();h.Config.UnavailableLevel=7;h.Config.ObservedLevel=6;var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceLocated,r.Outcome,"outcome");Eq(6,r.LocatedLevel,"located");Is(new[]{7,6}.SequenceEqual(h.Config.Levels),"configured levels");Eq(1,h.Search.Calls,"search count");Eq(6,h.Search.Requests.Single().Configuration.TargetLevel,"searched level");}
    static void CeilingOutsidePriorityExhausts(){var h=new H();h.Config.UnavailableLevel=7;h.Config.ObservedLevel=4;var p=new ResourceLevelFallbackPolicy{Levels=new[]{7},AttemptsPerLevel=1,StopOnFirstLocated=true,WaitForToastClearBetweenAttempts=true};var r=Go(h,p);Eq(ResourceLevelFallbackOutcome.ResourceLocated,r.Outcome,"outcome");Eq(4,r.LocatedLevel,"located level");Eq(1,h.Search.Calls,"search count");}
    static void CeilingReusedWithinRun(){var h=new H();h.Config.UnavailableLevel=7;h.Config.ObservedLevel=6;var p=new ResourceLevelFallbackPolicy{Levels=new[]{7,6},AttemptsPerLevel=1,StopOnFirstLocated=true,WaitForToastClearBetweenAttempts=true,RunId="same-run"};Go(h,p);h.Config.Levels.Clear();Go(h,p);Is(new[]{6}.SequenceEqual(h.Config.Levels),"cached ceiling");}
    static void UnconfirmedCeilingDoesNotSkipLevels(){var h=new H();h.Config.UnavailableLevel=7;h.Config.ObservedLevel=1;var p=new ResourceLevelFallbackPolicy{Levels=new[]{7,6,5},AttemptsPerLevel=1,StopOnFirstLocated=true,WaitForToastClearBetweenAttempts=true,RunId="false-observation"};var r=Go(h,p);Eq(ResourceLevelFallbackOutcome.ResourceLocated,r.Outcome,"outcome");Eq(6,r.LocatedLevel,"located");Is(new[]{7,6}.SequenceEqual(h.Config.Levels),"configured levels");}
    static void LowerCeilingPreservesSearchDepth(){var h=new H();h.Config.UnavailableLevel=7;h.Config.ObservedLevel=6;h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceLevelsExhausted,r.Outcome,"outcome");Is(new[]{7,6,5,4}.SequenceEqual(h.Config.Levels),"adaptive levels");Eq(3,h.Search.Calls,"search depth");}
    static void CachedCeilingPreservesSearchDepth(){var h=new H();h.Config.UnavailableLevel=7;h.Config.ObservedLevel=6;h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var p=new ResourceLevelFallbackPolicy{Levels=new[]{7,6,5},AttemptsPerLevel=1,StopOnFirstLocated=true,WaitForToastClearBetweenAttempts=true,RunId="adaptive-depth"};Go(h,p);h.Config.Levels.Clear();Go(h,p);Is(new[]{6,5,4}.SequenceEqual(h.Config.Levels),"cached adaptive levels");}
    static void ConfigurationToastSwitchesResource(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Config.FailLevel=6;h.Matcher.ToastFrames.Add(3);h.Matcher.MatchedToastTemplates.Clear();h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceNotFoundToastShortAnchor);h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceNotFoundToastOtherRegionAnchor);var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect,r.Outcome,"outcome");Eq("SearchOtherRegion",r.Attempts.Last().MatchedNotFoundVariant,"variant");Eq(1,h.Search.Calls,"search retry");Is(!h.Config.Levels.Contains(5),"level5");}
    static void ConfigurationTargetLevelTooLowToastSwitchesResource(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Config.FailLevel=6;h.Matcher.ToastFrames.Add(3);h.Matcher.MatchedToastTemplates.Clear();h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceTargetLevelTooLowToastAnchor);h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceTargetLevelSeasonMapToastAnchor);var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect,r.Outcome,"outcome");Eq("TargetLevelTooLow",r.Attempts.Last().MatchedNotFoundVariant,"variant");Eq(1,h.Search.Calls,"search retry");}
    static void TargetLevelTooLowSearchSkipsRemainingLevels(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;h.Search.NotFoundVariant="TargetLevelTooLow";var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect,r.Outcome,"outcome");Eq(1,h.Search.Calls,"search count");Is(new[]{7}.SequenceEqual(h.Config.Levels),"configured levels");Eq("TargetLevelTooLow",r.Attempts.First().MatchedNotFoundVariant,"variant");}
    static void SearchOtherRegionSearchSkipsRemainingLevels(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;h.Search.NotFoundVariant="SearchOtherRegion";var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect,r.Outcome,"outcome");Eq(1,h.Search.Calls,"search count");Is(new[]{7}.SequenceEqual(h.Config.Levels),"configured levels");Eq("SearchOtherRegion",r.Attempts.Single().MatchedNotFoundVariant,"variant");}
    static void ResourceAreaLv2RedirectPreservesOutcome(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceAreaLv2Redirect;h.Search.NotFoundVariant="ResourceAreaLv2Redirect";var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect,r.Outcome,"outcome");Eq(1,h.Search.Calls,"single search");Eq(1,h.Config.Calls,"no lower level");Eq(7,r.LastAttemptedLevel,"last level");Eq("ResourceAreaLv2Redirect",r.MatchedNotFoundVariant,"variant");Eq(ResourceSearchFailureReason.ResourceAreaLv2Redirect,r.FailureReason,"reason");}
    static void AllVerifiedNotFoundVariantsRouteToMappedPoints(){foreach(string variant in new[]{"LegacyMoveArea","GenericNotFoundStart","SearchOtherRegion","TargetLevelTooLow","SeasonMapRestriction","ResourceAreaLv2Redirect"}){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;h.Search.NotFoundVariant=variant;var p=new ResourceLevelFallbackPolicy{Levels=new[]{6},AttemptsPerLevel=1,StopOnFirstLocated=true,WaitForToastClearBetweenAttempts=true};var r=Go(h,p);Eq(ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect,r.Outcome,"outcome "+variant);Eq(variant,r.MatchedNotFoundVariant,"variant "+variant);Eq(1,h.Search.Calls,"search count "+variant);}}
    static void VerifiedGenericNotFoundRedirectsToMappedPoints(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;h.Search.NotFoundVariant="LegacyMoveArea";var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect,r.Outcome,"outcome");Eq(1,h.Search.Calls,"search count");Eq("LegacyMoveArea",r.MatchedNotFoundVariant,"variant");Eq(ResourceSearchFailureReason.ResourceAreaLv2Redirect,r.FailureReason,"redirect reason");}
    static void GenericNotFoundStartRedirectsToMappedPoints(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;h.Search.NotFoundVariant="GenericNotFoundStart";var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceAreaLv2Redirect,r.Outcome,"outcome");Eq(1,h.Search.Calls,"search count");Eq("GenericNotFoundStart",r.MatchedNotFoundVariant,"variant");Eq(ResourceSearchFailureReason.ResourceAreaLv2Redirect,r.FailureReason,"redirect reason");}
    static void UnsupportedLevelDoesNotRedirectToMappedPoints(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;h.Search.NotFoundVariant="LegacyMoveArea";var p=new ResourceLevelFallbackPolicy{Levels=new[]{5},AttemptsPerLevel=1,StopOnFirstLocated=true,WaitForToastClearBetweenAttempts=false};var r=Go(h,p);Eq(ResourceLevelFallbackOutcome.ResourceLevelsExhausted,r.Outcome,"outcome");Eq(1,h.Search.Calls,"search count");}
    static void ResourceAreaLv2CityPoolsContainConfiguredPoints()
    {
        Eq(16, ResourceAreaLv2PointSelector.GetPointsForCityLevel(7).Count, "city level 7 count");
        Eq(55, ResourceAreaLv2PointSelector.GetPointsForCityLevel(8).Count, "city level 8 count");
        Eq(16, ResourceAreaLv2PointSelector.GetPointsForCityLevel(9).Count, "city level 9 count");
        Eq(36, ResourceAreaLv2PointSelector.GetPointsForCityLevel(10).Count, "city level 10 count");
        Is(ResourceAreaLv2PointSelector.GetPointsForCityLevel(7).Contains(new System.Drawing.Point(650,954)), "level 7 point");
        Is(ResourceAreaLv2PointSelector.GetPointsForCityLevel(8).Contains(new System.Drawing.Point(783,816)), "level 8 point");
        Is(ResourceAreaLv2PointSelector.GetPointsForCityLevel(9).Contains(new System.Drawing.Point(520,809)), "level 9 point");
        Is(ResourceAreaLv2PointSelector.GetPointsForCityLevel(10).Contains(new System.Drawing.Point(612,774)), "level 10 corrected pair");
        Eq(123, ResourceAreaLv2PointSelector.AllMapPoints.Distinct().Count(), "all configured points unique");
    }

    static void ResourceAreaLv2ResourceLevelsSelectCityPools()
    {
        Is(new[] { 7, 8 }.SequenceEqual(ResourceAreaLv2PointSelector.GetCityLevelsForResourceLevel(6)), "level 6 cities");
        Is(new[] { 7, 8, 9, 10 }.SequenceEqual(ResourceAreaLv2PointSelector.GetCityLevelsForResourceLevel(7)), "level 7 cities");
        Is(new[] { 8, 9, 10 }.SequenceEqual(ResourceAreaLv2PointSelector.GetCityLevelsForResourceLevel(8)), "level 8 cities");
        Eq(71, ResourceAreaLv2PointSelector.GetPointsForResourceLevel(6).Count, "level 6 pool");
        Eq(123, ResourceAreaLv2PointSelector.GetPointsForResourceLevel(7).Count, "level 7 pool");
        Eq(107, ResourceAreaLv2PointSelector.GetPointsForResourceLevel(8).Count, "level 8 pool");
        Eq(0, ResourceAreaLv2PointSelector.GetPointsForResourceLevel(5).Count, "unsupported level pool");
    }

    static void ResourceAreaLv2MapCoordinatesAreNotScreenScaled()
    {
        var selector = new ResourceAreaLv2PointSelector(new Random(7));
        ResourceAreaLv2PointSelection selected = selector.Next("run", "device", ResourceType.Iron, 6, 0, 640, 360);
        Eq(selected.BasePoint, selected.ScaledPoint, "map coordinate must remain unchanged");
        Is(ResourceAreaLv2PointSelector.GetPointsForResourceLevel(6).Contains(selected.BasePoint), "selected point belongs to level 6 pool");
        ResourceAreaLv2PointSelection unsupported = selector.Next("run", "device", ResourceType.Iron, 5, 0, 1280, 720);
        Is(unsupported.Exhausted, "unsupported resource level must not guess a city area");
    }
    static void SearchTapNotAppliedYieldsResource(){var h=new H();h.Search.Default=ResourceSearchOutcome.SearchTapNotApplied;var r=Go(h);Eq(ResourceLevelFallbackOutcome.ResourceLevelsExhausted,r.Outcome,"outcome");Eq(1,h.Search.Calls,"bounded search handoff");Eq(1,h.Config.Calls,"no lower-level loop");Is(r.ErrorMessage==null,"technical failure");}
    static void ConfigurationSingleToastAnchorDoesNotSwitch(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Config.FailLevel=6;h.Matcher.ToastFrames.Add(3);h.Matcher.MatchedToastTemplates.Clear();h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceNotFoundToastShortAnchor);Eq(ResourceLevelFallbackOutcome.ConfigurationFailed,Go(h).Outcome,"outcome");}
    static void ConfigurationDistantToastAnchorsDoNotSwitch(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Config.FailLevel=6;h.Matcher.ToastFrames.Add(3);h.Matcher.MatchedToastTemplates.Clear();h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceNotFoundToastShortAnchor);h.Matcher.MatchedToastTemplates.Add(TemplateId.ResourceNotFoundToastOtherRegionAnchor);h.Matcher.OtherRegionY=500;Eq(ResourceLevelFallbackOutcome.ConfigurationFailed,Go(h).Outcome,"outcome");}
    static void SearchTimeout(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.Timeout);var r=Go(h);Eq(ResourceLevelFallbackOutcome.SearchFailed,r.Outcome,"outcome");Is(!h.Config.Levels.Contains(5),"level5");}
    static void CancelBetweenLevels(){using(var c=new CancellationTokenSource()){var h=new H();h.Search.CancelAfterNotFound=c;h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var r=Go(h,null,c.Token);Eq(ResourceLevelFallbackOutcome.Cancelled,r.Outcome,"outcome");Eq(1,h.Config.Calls,"level6");}}
    static void CancelDuringClear(){using(var c=new CancellationTokenSource()){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;h.Client.CancelOnCapture=c;var r=Go(h,null,c.Token);Eq(ResourceLevelFallbackOutcome.Cancelled,r.Outcome,"outcome");}}
    static void NoNone(){string s=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ADB","Infrastructure","ResourceSearch","ResourceLevelFallbackService.cs"));Is(!s.Contains("CancellationToken"+".None"),"token bypass");}
    static void AttemptsBounded(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;var p=new ResourceLevelFallbackPolicy{Levels=new[]{7},AttemptsPerLevel=2,StopOnFirstLocated=true,WaitForToastClearBetweenAttempts=true};var r=Go(h,p);Eq(2,r.Attempts.Count,"attempts");}
    static void RetryWaitsForClear(){var h=new H();h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceNotFound);h.Search.Outcomes.Enqueue(ResourceSearchOutcome.ResourceLocated);var p=new ResourceLevelFallbackPolicy{Levels=new[]{7},AttemptsPerLevel=2,StopOnFirstLocated=true,WaitForToastClearBetweenAttempts=true};var r=Go(h,p);Is(r.Attempts[1].ToastClearVerifiedBeforeAttempt,"clear");}
    static void DuplicateRejected(){var h=new H();var p=new ResourceLevelFallbackPolicy{Levels=new[]{7,7},AttemptsPerLevel=1};var r=Go(h,p);Eq(0,h.Config.Calls,"input");Is(!r.Success,"success");}
    static void UnsupportedRejected(){var h=new H();var p=new ResourceLevelFallbackPolicy{Levels=new[]{31},AttemptsPerLevel=1};Go(h,p);Eq(0,h.Config.Calls,"input");}
    static void DiagnosticFailure(){var h=new H();h.Search.Default=ResourceSearchOutcome.ResourceNotFound;h.Diag.Throw=true;Eq(ResourceLevelFallbackOutcome.ResourceLevelsExhausted,Go(h).Outcome,"outcome");}
    static void ReentrantLease(){var h=new H();var gate=DeviceOperationLock.Shared;var r=gate.RunAsync("LDPlayer",t=>h.Service.SearchAsync("LDPlayer",ResourceType.Iron,new ResourceLevelFallbackPolicy(),true,t),default(CancellationToken)).GetAwaiter().GetResult();Is(r.Success,"reentrant");}
    static void DifferentDevices(){var gate=DeviceOperationLock.Shared;var entered=new CountdownEvent(2);Func<string,Task<int>> f=d=>gate.RunAsync(d,async t=>{entered.Signal();Is(entered.Wait(1000),"global");await Task.Delay(1,t);return 1;},default(CancellationToken));Task.WaitAll(Task.Run(()=>f("a")),Task.Run(()=>f("b")));}

    sealed class H
    {
        public FakeConfig Config=new FakeConfig();public FakeSearch Search=new FakeSearch();public FakeClient Client=new FakeClient();public FakeMatcher Matcher=new FakeMatcher();public FakeDiag Diag=new FakeDiag();public Detector Detector=new Detector();public ResourceLevelFallbackService Service;
        public H(int clearTimeout=1,int poll=1,int requiredClear=2){var o=new ResourceLevelFallbackOptions(new[]{7,6,5},1,requiredClear,poll,clearTimeout,true,true,true,"Diagnostics/ResourceLevelFallback",new ImageRegion(150,120,980,400));Service=new ResourceLevelFallbackService(Config,Search,Detector,Client,new Registry(),Matcher,DeviceOperationLock.Shared,new ResourceSearchConfigurationOptions(1,1,1,1,30,30,1),o,Diag,new Log());}
    }
    sealed class FakeConfig:IResourceSearchConfigurationService{public int Calls,FailLevel,UnavailableLevel,ObservedLevel;public List<int> Levels=new List<int>();public Task<ResourceSearchConfigurationResult> ConfigureAsync(string d,ResourceSearchConfigurationRequest r,CancellationToken t){t.ThrowIfCancellationRequested();Calls++;Levels.Add(r.TargetLevel);bool unavailable=r.TargetLevel==UnavailableLevel;bool ok=r.TargetLevel!=FailLevel&&!unavailable;return Task.FromResult(new ResourceSearchConfigurationResult{Success=ok,ResourceVerified=ok,LevelVerified=ok,FilterVerified=ok,ObservedLevel=unavailable?(int?)ObservedLevel:ok?(int?)r.TargetLevel:null,FinalState=GameState.ResourceSearchPanel,Message=ok?"configured":"failed",ErrorMessage=ok?null:"configuration"});}}
    sealed class FakeSearch:IResourceSearchExecutionService{public int Calls;public ResourceSearchOutcome Default=ResourceSearchOutcome.ResourceLocated;public string NotFoundVariant="VerifiedRetryPanelStayedOpen";public bool NotFoundToastVerified=true;public Queue<ResourceSearchOutcome> Outcomes=new Queue<ResourceSearchOutcome>();public List<ResourceSearchExecutionRequest> Requests=new List<ResourceSearchExecutionRequest>();public CancellationTokenSource CancelAfterNotFound;public Task<ResourceSearchExecutionResult> ExecuteAsync(string d,ResourceSearchExecutionRequest r,CancellationToken t){t.ThrowIfCancellationRequested();Calls++;Requests.Add(r);var o=Outcomes.Count>0?Outcomes.Dequeue():Default;var reason=NotFoundVariant=="SearchOtherRegion"?ResourceSearchFailureReason.SearchOtherRegion:NotFoundVariant=="TargetLevelTooLow"?ResourceSearchFailureReason.TargetLevelTooLow:NotFoundVariant=="ResourceAreaLv2Redirect"?ResourceSearchFailureReason.ResourceAreaLv2Redirect:ResourceSearchFailureReason.ResourceUnavailable;var x=new ResourceSearchExecutionResult{Outcome=o,Success=o==ResourceSearchOutcome.ResourceLocated,FinalState=o==ResourceSearchOutcome.ResourceLocated?GameState.ResourcePopup:GameState.ResourceSearchPanel,NotFoundToastVerified=o==ResourceSearchOutcome.ResourceNotFound&&NotFoundToastVerified,MatchedNotFoundVariant=o==ResourceSearchOutcome.ResourceNotFound||o==ResourceSearchOutcome.ResourceAreaLv2Redirect?NotFoundVariant:null,FailureReason=o==ResourceSearchOutcome.ResourceNotFound||o==ResourceSearchOutcome.ResourceAreaLv2Redirect?reason:ResourceSearchFailureReason.None,Message=o.ToString()};if(o==ResourceSearchOutcome.ResourceNotFound)CancelAfterNotFound?.Cancel();return Task.FromResult(x);}}
    sealed class Detector:IGameStateDetector{public int Calls;public Task<GameDetectionResult> DetectAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();Calls++;return Task.FromResult(new GameDetectionResult{State=GameState.ResourceSearchPanel,IsSuccessful=true,Evidence=new GameDetectionEvidence[0]});}public GameDetectionResult Detect(byte[] p)=>null;}
    sealed class FakeClient:ILdPlayerClient{int frame;public int InputCalls;public CancellationTokenSource CancelOnCapture;public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();int f=++frame;if(f==1&&CancelOnCapture!=null)CancelOnCapture.Cancel();return Task.FromResult(new[]{(byte)f});}public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult((IReadOnlyList<string>)new string[0]);public Task<bool> IsRunningAsync(string d,CancellationToken t)=>Task.FromResult(true);public Task OpenAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task CloseAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task RunAppAsync(string d,string p,CancellationToken t)=>Task.CompletedTask;public Task TapAsync(string d,int x,int y,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task TapByPercentAsync(string d,double x,double y,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task LongPressAsync(string d,int x,int y,int m,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task SwipeByPercentAsync(string d,double a,double b,double c,double e,int m,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task BackAsync(string d,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task InputTextAsync(string d,string s,CancellationToken t){InputCalls++;return Task.CompletedTask;}public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t){InputCalls++;return Task.CompletedTask;}}
    sealed class Registry:ITemplateRegistry{public TemplateDefinition GetDefinition(TemplateId id)=>new TemplateDefinition(id,id+".png",.8);public string GetPath(TemplateId id)=>id+".png";public byte[] LoadBytes(TemplateId id)=>new[]{(byte)id};public bool Exists(TemplateId id)=>true;}
    sealed class FakeMatcher:IImageMatcher{public HashSet<int> ToastFrames=new HashSet<int>();public HashSet<int> PanelClosedFrames=new HashSet<int>();public HashSet<TemplateId> MatchedToastTemplates=new HashSet<TemplateId>{TemplateId.ResourceNotFoundToastAnchor,TemplateId.ResourceNotFoundToastActionAnchor,TemplateId.ResourceNotFoundToastShortAnchor,TemplateId.ResourceNotFoundToastOtherRegionAnchor,TemplateId.ResourceTargetLevelTooLowToastAnchor,TemplateId.ResourceTargetLevelSeasonMapToastAnchor};public int OtherRegionY=200;public ImageMatchResult Find(byte[] s,byte[] t,ImageRegion? r=null){int f=s[0],id=t[0];bool panel=id==(int)TemplateId.SearchButtonEnabled||id==(int)TemplateId.LevelMinusButton;bool toast=id==(int)TemplateId.ResourceNotFoundToastAnchor||id==(int)TemplateId.ResourceNotFoundToastActionAnchor||id==(int)TemplateId.ResourceNotFoundToastShortAnchor||id==(int)TemplateId.ResourceNotFoundToastOtherRegionAnchor||id==(int)TemplateId.ResourceTargetLevelTooLowToastAnchor||id==(int)TemplateId.ResourceTargetLevelSeasonMapToastAnchor;bool found=panel?!PanelClosedFrames.Contains(f):toast&&ToastFrames.Contains(f)&&MatchedToastTemplates.Contains((TemplateId)id);int y=id==(int)TemplateId.ResourceNotFoundToastOtherRegionAnchor?OtherRegionY:200;return found?ImageMatchResult.FoundAt(200,y,20,10):ImageMatchResult.NotFound();}}
    sealed class FakeDiag:IResourceLevelFallbackDiagnosticStore{public bool Throw;public Task<string> SaveAsync(string d,string s,byte[] p,CancellationToken t){if(Throw)throw new IOException("disk");return Task.FromResult("diag.png");}}
    sealed class Log:IDiagnosticLogger{public void Info(string m){}public void Error(string m,Exception e){}}
}
