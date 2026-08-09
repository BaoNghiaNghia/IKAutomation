using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.Navigation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IKAutomation.Navigation.Tests
{
    internal static class Program
    {
        private static readonly CancellationToken Token = new CancellationToken(false);
        private static readonly Color TerritoryGreen = Color.FromArgb(72, 132, 82);
        private static readonly Color TerritoryRed = Color.FromArgb(150, 70, 64);
        private static readonly Color TerritoryBlue = Color.FromArgb(92, 151, 190);
        private static int passed, failed;
        private static int Main()
        {
            Run("Ensure WorldMap succeeds immediately", EnsureWorldImmediate);
            Run("Ensure WorldMap sends no input when already there", EnsureWorldNoInput);
            Run("Panel uses one Back and verifies WorldMap", PanelBackOnce);
            Run("ContinentMap uses one Back and verifies WorldMap", ContinentBackOnce);
            Run("TeamSelection never sends Android Back", TeamSelectionDoesNotSendBack);
            Run("City taps fresh map button and verifies WorldMap", CityMapButtonOnce);
            Run("City without fresh map-button bounds sends no input", CityMissingBoundsNoInput);
            Run("Unknown fails without input", UnknownNoInput);
            Run("Verified WorldMap leave dialog is cancelled", VerifiedWorldMapLeaveDialogIsCancelled);
            Run("Cancel-only Unknown still sends no input", CancelOnlyUnknownNoInput);
            Run("Already-open panel succeeds without Tap", AlreadyPanel);
            Run("Resource tab fallback verifies open panel", ResourceTabFallbackPanel);
            Run("Unverified panel state fails without input", UnverifiedPanelNoInput);
            Run("Open panel taps exact evidence center", TapEvidenceCenter);
            Run("Transient Unknown frame is tolerated", TransientUnknownIsTolerated);
            Run("One SearchButton frame is not accepted immediately", PartialPanelEvidenceIsNotAcceptedImmediately);
            Run("Two stable SearchButton frames confirm the panel", PartialThenConfirmedPanelSucceeds);
            Run("Panel opening failure preserves a screenshot-probe reason", PartialPanelFailureHasReason);
            Run("Panel retry never sends Back", RetryRecoveryCleansBlockingDialog);
            Run("City detection fails without Back recovery", CityDetectionNoBack);
            Run("Tap success requires verified panel", TapRequiresVerification);
            Run("Timeout returns failure", TimeoutFailure);
            Run("Retry never exceeds configured maximum", RetryBounded);
            Run("Unknown after Tap prevents retry", UnknownPreventsRetry);
            Run("Missing bounds prevents Tap", MissingBounds);
            Run("Strict territory reposition blocks unvalidated legacy anchors", TerritoryRepositionTapsMatchedAnchors);
            Run("Territory reposition prefers fresh nearby animated pins", TerritoryRepositionPrefersNearbyPins);
            Run("Territory reposition confirms nearby target with fresh coordinate pin", TerritoryRepositionConfirmsNearbyPinSelection);
            Run("Territory reposition accepts Unknown with fresh continent pin", TerritoryRepositionAcceptsUnknownWithContinentPin);
            Run("Territory reposition accepts Unknown with bounded coordinate pin", TerritoryRepositionAcceptsUnknownWithCoordinatePin);
            Run("Territory reposition latches bouncing home pin across adjacent frames", TerritoryRepositionLatchesBouncingHomePin);
            Run("Territory reposition falls back when pin templates were checked but unmatched", TerritoryRepositionFallsBackFromUnmatchedPins);
            Run("Territory reposition falls back when one animated pin is missed", TerritoryRepositionFallsBackFromPartialPinPair);
            Run("Coordinate fallback enters X then Y before tapping the map pin", CoordinateFallbackEntersXThenYThenPin);
            Run("Territory reposition rejects a far yellow search pin", TerritoryRepositionRejectsFarSearchPin);
            Run("Same territory color allows detected-pin movement", SameTerritoryColorAllowsMovement);
            Run("Different territory color blocks detected-pin movement", DifferentTerritoryColorBlocksMovement);
            Run("Unknown territory color blocks detected-pin movement", UnknownTerritoryColorBlocksMovement);
            Run("Relative screen candidates are the default primary strategy", SameTerritoryCoordinatesArePrimary);
            Run("Relative screen recovery keeps the candidate budget bounded", ScreenRecoveryCandidateBudgetIsBounded);
            Run("Relative screen recovery does not call coordinate fallback", ScreenRecoveryDoesNotCallCoordinateFallback);
            Run("Fallback coordinates require matching territory color", FallbackCoordinatesRequireMatchingTerritoryColor);
            Run("Fallback samples the displaced X/Y pin instead of screen center", FallbackSamplesDisplacedDestinationPin);
            Run("Fallback reports color around the located yellow X/Y pin", FallbackReportsLocatedYellowPinColor);
            Run("Fallback locates cyan home pin when its template misses", FallbackLocatesCyanHomePinPixels);
            Run("Fallback locates the compact cyan home-pin core", FallbackLocatesCompactCyanHomePinPixels);
            Run("Fallback blocks when the yellow X/Y pin cannot be located", FallbackBlocksUnknownYellowPin);
            Run("Fallback rolls back mismatched X/Y and retries", FallbackRollsBackAndRetriesMatchingTone);
            Run("Pin icon color is excluded from territory sampling", PinIconColorIsExcludedFromTerritorySampling);
            Run("Strict reposition keeps blocking-dialog recovery bounded", TerritoryRepositionCancelsBlockingDialog);
            Run("Territory reposition rejects far territory marker", TerritoryRepositionRejectsFarTerritoryMarker);
            Run("Territory reposition without map-pin bounds sends no input", TerritoryRepositionMissingMapPin);
            Run("Strict reposition refreshes incomplete fast-path evidence", TerritoryRepositionRefreshesIncompleteFastPathEvidence);
            Run("WorldMap point tap validates bounds", TapWorldMapPointRejectsOutsideFrame);
            Run("WorldMap point tap sends one verified tap", TapWorldMapPointSendsVerifiedTap);
            Run("WorldMap point tap requires final WorldMap verification", TapWorldMapPointRequiresVerification);
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
        private static void TeamSelectionDoesNotSendBack() { var f = Setup(State(GameState.TeamSelection)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"TeamSelection must require controlled recovery."); Equal(0,f.Client.TotalInput,"TeamSelection must not receive blind input."); Assert(r.Message.Contains("TeamSelection is still open"),"Structured recovery message."); }
        private static void CityMapButtonOnce() { var f=Setup(State(GameState.City,true),State(GameState.WorldMap)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Failed."); Equal(1,f.Client.TapCalls,"Tap count."); Equal(25,f.Client.LastX,"Tap X."); Equal(40,f.Client.LastY,"Tap Y."); }
        private static void CityMissingBoundsNoInput() { var f=Setup(State(GameState.City)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unexpected success."); Equal(0,f.Client.TotalInput,"Blind input."); }
        private static void UnknownNoInput() { var f=Setup(State(GameState.Unknown)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unexpected success."); Equal(0,f.Client.TotalInput,"Blind input."); }
        private static void VerifiedWorldMapLeaveDialogIsCancelled() { var f=Setup(UnknownWithWorldMapCancel(),State(GameState.WorldMap)); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Verified leave dialog should be cancelled."); Equal(1,f.Client.TapCalls,"Cancel Tap count."); Equal(495,f.Client.LastX,"Cancel Tap X."); Equal(500,f.Client.LastY,"Cancel Tap Y."); Equal(0,f.Client.BackCalls,"Back must not be sent while the dialog is already open."); }
        private static void CancelOnlyUnknownNoInput() { var f=Setup(UnknownWithCancel()); var r=f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Cancel evidence without underlying WorldMap must remain Unknown."); Equal(0,f.Client.TotalInput,"Ambiguous cancel button must not be tapped."); }
        private static void AlreadyPanel() { var f=Setup(State(GameState.ResourceSearchPanel)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Failed."); Equal(0,f.Client.TapCalls,"Extra Tap."); }
        private static void ResourceTabFallbackPanel() { var f=Setup(PanelFromResourceTab()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Resource tab fallback was not accepted."); Equal(0,f.Client.TapCalls,"Unexpected Tap."); }
        private static void UnverifiedPanelNoInput() { var f=Setup(UnverifiedPanel()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unverified panel state succeeded."); Equal(0,f.Client.TotalInput,"Input sent for unverified panel state."); }
        private static void TapEvidenceCenter() { var f=Setup(State(GameState.WorldMap, true), State(GameState.ResourceSearchPanel)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Failed."); Equal(25,f.Client.LastX,"Tap X."); Equal(40,f.Client.LastY,"Tap Y."); Assert(r.ScreenshotConfirmed,"Strong screenshot confirmation."); Equal("DedicatedAnchorPlusSearchButton",r.ConfirmationMode,"Confirmation mode."); Assert(!r.SearchButtonBoundsComparisonPerformed,"Strong path must not require bounds comparison."); Equal(0,r.BackCount,"Back count."); }
        private static void TransientUnknownIsTolerated() { var f=Setup(State(GameState.WorldMap,true),PartialPanel(),PartialPanel()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Two stable SearchButton observations should confirm despite a weak detector state."); Equal(1,f.Client.TapCalls,"Unexpected additional Tap."); }
        private static void PartialPanelEvidenceIsNotAcceptedImmediately() { var f=SetupWithOptions(new WorldMapNavigationOptions(1,1,1),State(GameState.WorldMap,true),PartialPanel(),State(GameState.Unknown)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"SearchButton-only evidence must not be accepted from one frame."); Equal(0,f.Client.BackCalls,"Probe must not send Back."); }
        private static void PartialThenConfirmedPanelSucceeds() { var f=SetupWithOptions(new WorldMapNavigationOptions(1,1,1),State(GameState.WorldMap,true),PartialPanel(),PartialPanel()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Two stable SearchButton frames should confirm the panel."); Assert(r.ScreenshotConfirmed,"Screenshot confirmation flag."); Equal("StableSearchButtonPair",r.ConfirmationMode,"Confirmation mode."); Equal(2,r.ConfirmationFrames,"Confirmation frame count."); Assert(r.SearchButtonBoundsComparisonPerformed,"Fallback path must compare bounds."); Assert(r.SearchButtonBoundsStable,"Fallback bounds should be stable."); Equal(0,r.BackCount,"Back count."); Equal(1,f.Client.TapCalls,"Unexpected second tap."); }
        private static void PartialPanelFailureHasReason() { var states=new List<GameDetectionResult>{State(GameState.WorldMap,true),PartialPanel(),State(GameState.Unknown),State(GameState.WorldMap,true),PartialPanel(),State(GameState.Unknown)}; var f=SetupWithOptions(new WorldMapNavigationOptions(1,1,2),states.ToArray()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Two partial attempts must fail safely."); Equal("SearchButtonNotFoundInExpectedRegion",r.FailureReason,"Screenshot-probe reason."); Equal(2,f.Client.TapCalls,"Opening attempts must be bounded to two."); Equal(0,f.Client.BackCalls,"No Back recovery."); }
        private static void RetryRecoveryCleansBlockingDialog() { var states=new List<GameDetectionResult>{State(GameState.WorldMap,true),PartialPanel(),State(GameState.Unknown),State(GameState.WorldMap,true),PartialPanel(),State(GameState.Unknown)}; var f=SetupWithOptions(new WorldMapNavigationOptions(1,1,2),states.ToArray()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unconfirmed SearchButton evidence should fail safely."); Equal(2,f.Client.TapCalls,"Expected two bounded opening taps."); Equal(0,f.Client.BackCalls,"No Back recovery should be sent."); }
        private static void CityDetectionNoBack() { var f=SetupWithOptions(new WorldMapNavigationOptions(1,1,2),State(GameState.WorldMap,true),State(GameState.City,true)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"City must not be accepted as the resource panel."); Equal("CityDetectedDuringPanelOpen",r.FailureReason,"City failure reason."); Equal(0,f.Client.BackCalls,"City detection must not trigger Back."); }
        private static void TapRequiresVerification() { var f=Setup(State(GameState.WorldMap,true), State(GameState.Unknown)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Tap alone counted as success."); }
        private static void TimeoutFailure() { var f=SetupWithOptions(new WorldMapNavigationOptions(250,1,1), State(GameState.WorldMap,true)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Timeout should fail."); Equal(1,f.Client.TapCalls,"Tap count."); }
        private static void RetryBounded() { var states=new List<GameDetectionResult>{State(GameState.WorldMap,true)}; for(int i=0;i<8;i++)states.Add(State(GameState.WorldMap,true)); var f=SetupWithOptions(new WorldMapNavigationOptions(250,1,2),states.ToArray()); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Expected failure."); Equal(2,f.Client.TapCalls,"Exceeded retry max."); }
        private static void UnknownPreventsRetry() { var f=SetupWithOptions(new WorldMapNavigationOptions(10,1,3),State(GameState.WorldMap,true),State(GameState.Unknown)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Expected failure."); Equal(1,f.Client.TapCalls,"Retried after Unknown."); }
        private static void MissingBounds() { var f=Setup(State(GameState.WorldMap,false)); var r=f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Expected failure."); Equal(0,f.Client.TapCalls,"Fallback Tap sent."); }
        private static void TerritoryRepositionTapsMatchedAnchors() { var f=Setup(WorldMapWithPin(),ContinentMapWithAnchors(),ContinentMapWithAnchors(),State(GameState.WorldMap)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Strict mode must not use an unvalidated legacy territory anchor."); }
        private static void TerritoryRepositionPrefersNearbyPins() { var f=Setup(WorldMapWithPin(),ContinentMapWithPinPair(670,390,710,350),ContinentMapWithPinPair(676,406,714,365),State(GameState.WorldMap)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Expected nearby-pin recovery success."); Equal(2,f.Client.TapCalls,"Tap count."); Equal(723,f.Client.LastX,"Fresh yellow pin X."); Equal(380,f.Client.LastY,"Fresh yellow pin Y."); }
        private static void TerritoryRepositionConfirmsNearbyPinSelection() { var f=Setup(WorldMapWithPin(),ContinentMapWithPinPair(670,390,710,350),ContinentMapWithPinPair(676,406,714,365),ContinentMapWithAnchors(),State(GameState.WorldMap)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Expected selected nearby pin to be confirmed through the coordinate bar."); Equal(3,f.Client.TapCalls,"Tap count."); Equal(197,f.Client.LastX,"Coordinate pin X."); Equal(37,f.Client.LastY,"Coordinate pin Y."); }
        private static void TerritoryRepositionAcceptsUnknownWithContinentPin() { var f=Setup(WorldMapWithPin(),UnknownWithContinentHomePin(),ContinentMapWithPinPair(670,390,710,350),ContinentMapWithPinPair(676,406,714,365),State(GameState.WorldMap)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Fresh continent pin should verify the transition."); Equal(2,f.Client.TapCalls,"Tap count."); Equal(723,f.Client.LastX,"Fresh yellow pin X."); Equal(380,f.Client.LastY,"Fresh yellow pin Y."); }
        private static void TerritoryRepositionAcceptsUnknownWithCoordinatePin() { var f=Setup(WorldMapWithPin(),UnknownWithContinentCoordinatePin(),ContinentMapWithPinPair(670,390,710,350),ContinentMapWithPinPair(676,406,714,365),State(GameState.WorldMap)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Bounded coordinate pin should verify ContinentMap."); Equal(2,f.Client.TapCalls,"Tap count."); }
        private static void TerritoryRepositionLatchesBouncingHomePin() { var f=Setup(WorldMapWithPin(),UnknownWithContinentHomePin(),ContinentMapWithSearchPin(710,350),ContinentMapWithSearchPin(714,365),State(GameState.WorldMap)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Adjacent-frame home pin should be latched."); Equal(2,f.Client.TapCalls,"Tap count."); Equal(723,f.Client.LastX,"Fresh yellow pin X."); Equal(380,f.Client.LastY,"Fresh yellow pin Y."); }
        private static void TerritoryRepositionFallsBackFromUnmatchedPins() { var f=Setup(WorldMapWithPin(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCoordinateTargetAndAnchors(),State(GameState.WorldMap)); f.Client.FocusedValues.Enqueue(622); f.Client.FocusedValues.Enqueue(342); f.Client.Screenshots.Enqueue(SolidScreenshot(TerritoryGreen)); f.Client.Screenshots.Enqueue(FallbackTerritoryScreenshot(TerritoryGreen,TerritoryGreen)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"The matched green home-territory pin must supply the fallback color reference."); Equal(6,f.Client.TapCalls,"Expected map, original X/Y reads, X/Y edits, and final move Taps."); Equal(2,f.Client.InputCalls,"Coordinate input count."); Equal(2,f.Client.EnterCalls,"Coordinate confirmation count."); }
        private static void TerritoryRepositionFallsBackFromPartialPinPair() { var f=Setup(WorldMapWithPin(),ContinentMapWithOnlyHomePinAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCoordinateTargetAndAnchors(),State(GameState.WorldMap)); f.Client.FocusedValues.Enqueue(361); f.Client.FocusedValues.Enqueue(424); f.Client.Screenshots.Enqueue(SolidScreenshot(TerritoryGreen)); f.Client.Screenshots.Enqueue(FallbackTerritoryScreenshot(TerritoryGreen,TerritoryGreen)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"A missed yellow pin after both templates were checked must use coordinate fallback."); Equal(6,f.Client.TapCalls,"Tap count."); Equal(2,f.Client.InputCalls,"Coordinate input count."); Assert(int.Parse(f.Client.InputValues[0])!=361&&Math.Abs(int.Parse(f.Client.InputValues[0])-361)<=100,"X offset."); Assert(int.Parse(f.Client.InputValues[1])!=424&&Math.Abs(int.Parse(f.Client.InputValues[1])-424)<=100,"Y offset."); }
        private static void CoordinateFallbackEntersXThenYThenPin()
        {
            string source=ReadNavigationSource();
            string apply=Between(source,
                "private async Task<GameDetectionResult> ApplyCoordinateCandidateAsync",
                "private async Task SetCoordinateValueVerifiedAsync");
            int x=apply.IndexOf("\"X\", transaction.CurrentX",StringComparison.Ordinal);
            int y=apply.IndexOf("\"Y\", transaction.CurrentY",StringComparison.Ordinal);
            int pin=apply.IndexOf("ContinentMapPinButtonAfterCoordinateEntry",StringComparison.Ordinal);
            Assert(x>=0&&y>x&&pin>y,"Coordinate entry must be ordered X, Y, then map pin.");

            string replace=Between(source,
                "private async Task ReplaceFocusedCoordinateAsync",
                "private async Task<TerritoryValidation> ValidateNearbyPinTerritoriesAsync");
            Assert(!replace.Contains("AndroidKeyCode.Enter"),
                "Coordinate fields must not submit individually with Enter.");

            var f=Setup(ContinentMapWithAnchors(),ContinentMapWithAnchors(),
                ContinentMapWithAnchors(),ContinentMapWithAnchors());
            Type serviceType=typeof(WorldMapNavigationService);
            Type transactionType=serviceType.GetNestedType("CoordinateEditTransaction",
                System.Reflection.BindingFlags.NonPublic);
            Type candidateType=serviceType.GetNestedType("CoordinateCandidate",
                System.Reflection.BindingFlags.NonPublic);
            object transaction=Activator.CreateInstance(transactionType,new object[]{601,428});
            object candidate=Activator.CreateInstance(candidateType,new object[]{1,650,450,49,22});
            var transitions=new List<NavigationTransition>();
            var method=serviceType.GetMethod("ApplyCoordinateCandidateAsync",
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var task=(Task<GameDetectionResult>)method.Invoke(f.Service,
                new object[]{"d",transaction,candidate,transitions,Token});
            task.GetAwaiter().GetResult();
            Equal(0,f.Client.EnterCalls,"Enter must not submit either coordinate field.");
            Equal(5,f.Client.Actions.Count,"Unexpected coordinate action count.");
            Equal("Tap:63,37",f.Client.Actions[0],"X field Tap.");
            Equal("Input:650",f.Client.Actions[1],"X input.");
            Equal("Tap:143,37",f.Client.Actions[2],"Y field Tap.");
            Equal("Input:450",f.Client.Actions[3],"Y input.");
            Equal("Tap:197,37",f.Client.Actions[4],"Map-pin Tap.");
        }
        private static void TerritoryRepositionRejectsFarSearchPin() { var f=Setup(WorldMapWithPin(),ContinentMapWithPinPair(300,300,900,600)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Far target unexpectedly succeeded."); Equal(1,f.Client.TapCalls,"Far yellow pin must not be tapped."); }
        private static void SameTerritoryColorAllowsMovement() { var f=Setup(WorldMapWithPin(),ContinentMapWithPinPair(670,390,710,350),ContinentMapWithPinPair(676,406,714,365),State(GameState.WorldMap)); f.Client.Screenshots.Enqueue(PinTerritoryScreenshot(TerritoryGreen,TerritoryGreen,false)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Matching territory groups should allow the yellow-pin Tap."); Equal(2,f.Client.TapCalls,"Expected ContinentMap and yellow-pin Taps."); }
        private static void DifferentTerritoryColorBlocksMovement() { var f=Setup(WorldMapWithPin(),ContinentMapWithPinPair(670,390,710,350),ContinentMapWithPinPair(676,406,714,365)); f.Client.Screenshots.Enqueue(PinTerritoryScreenshot(TerritoryGreen,TerritoryRed,false)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Different territory groups must block movement."); Equal(1,f.Client.TapCalls,"Yellow destination pin must not be tapped."); }
        private static void UnknownTerritoryColorBlocksMovement() { var f=Setup(WorldMapWithPin(),ContinentMapWithPinPair(670,390,710,350),ContinentMapWithPinPair(676,406,714,365)); f.Client.Screenshots.Enqueue(SolidScreenshot(Color.FromArgb(128,128,128))); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Low-saturation territory must be unknown and block movement."); Equal(1,f.Client.TapCalls,"Yellow destination pin must not be tapped."); }
        private static void SameTerritoryCoordinatesArePrimary() { string source=ReadNavigationSource(); string core=Between(source,"private async Task<NavigationResult> RepositionToAllianceTerritoryCoreAsync","private async Task<HomeLocationEvidence>"); Assert(core.Contains("RelativeScreenCandidates"),"Screen-point strategy was not wired into the recovery core."); Assert(!core.Contains("TrySameTerritoryCoordinateAsync"),"Coordinate fallback must remain isolated from the primary recovery path."); }
        private static void ScreenRecoveryCandidateBudgetIsBounded() { var type=typeof(WorldMapNavigationService); var offsets=(Array)type.GetField("SameTerritoryScreenOffsets",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null); var max=(int)type.GetField("SameTerritoryMaximumCandidates",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null); Equal(16,offsets.Length,"Candidate offset count."); Equal(3,max,"Maximum candidate attempts."); }
        private static void ScreenRecoveryDoesNotCallCoordinateFallback() { string source=ReadNavigationSource(); string method=Between(source,"private async Task<NavigationResult> TryScreenPointFallbackAsync","private static NavigationResult ScreenPointFailure"); Assert(method.Contains("ContinentMapSearchTargetPin"),"Screen recovery must verify the destination pin."); Assert(method.Contains("TryClassifyPinTerritory"),"Screen recovery must verify destination territory color."); Assert(!method.Contains("TrySameTerritoryCoordinateAsync"),"Screen recovery must not invoke legacy coordinate fallback."); }
        private static void FallbackCoordinatesRequireMatchingTerritoryColor() { var f=Setup(WorldMapWithPin(),ContinentMapWithOnlyHomePinAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCoordinateTargetAndAnchors()); QueueFocusedCoordinates(f,361,424,5); f.Client.Screenshots.Enqueue(SolidScreenshot(TerritoryGreen)); for(int i=0;i<5;i++)f.Client.Screenshots.Enqueue(FallbackTerritoryScreenshot(TerritoryGreen,TerritoryRed)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Fallback destination on a different territory must be blocked."); Equal(23,f.Client.TapCalls,"Original X/Y are read once and every rejected candidate is rolled back without a final move Tap."); Equal(20,f.Client.EnterCalls,"Each rejected X/Y candidate must be applied and rolled back."); Assert(r.Message.Contains("Không tìm thấy tọa độ cùng màu"),"Bounded retry exhaustion must report rollback."); }
        private static void FallbackSamplesDisplacedDestinationPin() { var f=Setup(WorldMapWithPin(),ContinentMapWithOnlyHomePinAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCoordinateTargetAndAnchors()); QueueFocusedCoordinates(f,623,349,5); f.Client.Screenshots.Enqueue(SolidScreenshot(TerritoryGreen)); for(int i=0;i<5;i++)f.Client.Screenshots.Enqueue(FallbackTerritoryScreenshot(TerritoryGreen,TerritoryBlue,620,160)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"A blue displaced destination pin must not inherit the green screen-center color."); Equal(23,f.Client.TapCalls,"Rejected displaced destinations must only cause coordinate edits and rollbacks after reading original X/Y."); Assert(r.Message.Contains("khác màu lãnh thổ nhà"),"Expected color mismatch failure."); Assert(r.Transitions.Any(x=>x.Operation=="TerritoryColor"&&x.Message.Contains("Destination=Blue")&&x.Message.Contains("Source=YellowPinPixels")),"The displaced pin's blue territory was not recorded."); }
        private static void FallbackReportsLocatedYellowPinColor() { var f=Setup(WorldMapWithPin(),ContinentMapWithOnlyHomePinAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),State(GameState.WorldMap)); f.Detector.ScreenshotStates.Enqueue(ContinentMapWithCheckedPinsAndAnchors());QueueFocusedCoordinates(f,623,349,1); f.Client.Screenshots.Enqueue(SolidScreenshot(TerritoryGreen)); f.Client.Screenshots.Enqueue(FallbackTerritoryScreenshot(TerritoryGreen,TerritoryGreen,760,180)); var p=new RecordingProgress();var r=f.Service.RepositionToAllianceTerritoryAsync("d",p,Token).GetAwaiter().GetResult(); Assert(r.Success,"The located X/Y destination pin should allow classification when the animated template misses."); Equal(6,f.Client.TapCalls,"Expected map, original X/Y reads, X/Y edits, and final move Taps without rollback."); Assert(r.Transitions.Any(x=>x.Operation=="TerritoryColor"&&x.Message.Contains("Source=YellowPinPixels")&&x.Message.Contains("Result=Match")),"The located yellow pin color was not recorded."); Assert(p.Items.Any(x=>x.Message.Contains("Candidate=1/5")&&x.Message.Contains("X=")&&x.Message.Contains("Y=")&&x.Message.Contains("Destination=Green")),"The X/Y candidate color was not reported live."); }
        private static void FallbackLocatesCyanHomePinPixels() { var method=typeof(WorldMapNavigationService).GetMethod("TryLocateHomeLocationPin",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);Assert(method!=null,"Cyan pin locator is missing.");var args=new object[]{HomePinScreenshot(TerritoryGreen),null};bool found=(bool)method.Invoke(null,args);var evidence=args[1] as GameDetectionEvidence;Assert(found&&evidence!=null&&evidence.Found,"Cyan home pin pixels were not located.");Equal(TemplateId.ContinentMapHomeLocationPin,evidence.TemplateId,"Template id.");Assert(evidence.MatchResult.Width>=9&&evidence.MatchResult.Height>evidence.MatchResult.Width,"Invalid cyan pin bounds."); }
        private static void FallbackLocatesCompactCyanHomePinPixels() { var method=typeof(WorldMapNavigationService).GetMethod("TryLocateHomeLocationPin",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);var args=new object[]{CompactHomePinScreenshot(TerritoryGreen),null};bool found=(bool)method.Invoke(null,args);var evidence=args[1] as GameDetectionEvidence;Assert(found&&evidence!=null&&evidence.Found,"Compact cyan pin core was not located.");Assert(evidence.MatchResult.Width>=5&&evidence.MatchResult.Height>=12,"Compact cyan bounds were rejected."); }
        private static void FallbackBlocksUnknownYellowPin() { var f=Setup(WorldMapWithPin(),ContinentMapWithOnlyHomePinAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCoordinateTargetAndAnchors()); QueueFocusedCoordinates(f,623,349,5); f.Client.Screenshots.Enqueue(SolidScreenshot(TerritoryGreen)); for(int i=0;i<5;i++)f.Client.Screenshots.Enqueue(SolidScreenshot(TerritoryGreen)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"A missing yellow destination pin must fail safely."); Equal(23,f.Client.TapCalls,"Missing pins must only cause bounded coordinate edits and rollbacks after original X/Y reads."); Assert(r.Transitions.Any(x=>x.Operation=="TerritoryColor"&&x.Message.Contains("Destination=Unknown")&&x.Message.Contains("Reason=PinNotLocatedConfidently")),"Missing yellow pin was not reported as unknown."); }
        private static void FallbackRollsBackAndRetriesMatchingTone() { var f=Setup(WorldMapWithPin(),ContinentMapWithOnlyHomePinAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCheckedPinsAndAnchors(),ContinentMapWithCoordinateTargetAndAnchors(),ContinentMapWithCoordinateTargetAndAnchors(),ContinentMapWithCoordinateTargetAndAnchors(),ContinentMapWithCoordinateTargetAndAnchors(),ContinentMapWithCoordinateTargetAndAnchors(),State(GameState.WorldMap)); QueueFocusedCoordinates(f,623,349,2); f.Client.Screenshots.Enqueue(SolidScreenshot(TerritoryGreen)); f.Client.Screenshots.Enqueue(FallbackTerritoryScreenshot(TerritoryGreen,TerritoryRed)); f.Client.Screenshots.Enqueue(FallbackTerritoryScreenshot(TerritoryGreen,Color.FromArgb(86,150,91))); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Second candidate with a similar green tone should be accepted."); Equal(10,f.Client.TapCalls,"Original X/Y are read before first candidate rollback and the verified move Tap."); Equal(6,f.Client.EnterCalls,"First candidate should be rolled back before applying the second."); Equal("623",f.Client.InputValues[2],"X rollback value."); Equal("349",f.Client.InputValues[3],"Y rollback value."); Assert(!r.Transitions.Any(x=>x.Operation=="TerritoryColor"&&x.Message.Contains("Source=ViewportCenter")),"An unrelated green center sample must never allow movement."); }
        private static void PinIconColorIsExcludedFromTerritorySampling() { var f=Setup(WorldMapWithPin(),ContinentMapWithPinPair(670,390,710,350),ContinentMapWithPinPair(676,406,714,365),State(GameState.WorldMap)); f.Client.Screenshots.Enqueue(PinTerritoryScreenshot(TerritoryGreen,TerritoryGreen,true)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(r.Success,"Purple icon pixels must not override the green background below the pins."); Equal(2,f.Client.TapCalls,"Expected movement to be allowed."); }
        private static void TerritoryRepositionCancelsBlockingDialog() { var f=Setup(State(GameState.ResourceSearchPanel),UnknownWithWorldMapCancel(),WorldMapWithPin(),ContinentMapWithAnchors(),ContinentMapWithAnchors(),State(GameState.WorldMap)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Strict mode must still block the unvalidated final legacy move."); Equal(1,f.Client.BackCalls,"Back count."); }
        private static void TerritoryRepositionRejectsFarTerritoryMarker() { var f=Setup(WorldMapWithPin(),ContinentMapWithFarTerritoryAnchor()); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unexpected success."); Equal(2,f.Client.TapCalls,"Map open plus bounded coordinate-reader availability probe."); }
        private static void TerritoryRepositionMissingMapPin() { var f=Setup(State(GameState.WorldMap,true)); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Unexpected success."); Equal(0,f.Client.TapCalls,"Blind input."); }
        private static void TerritoryRepositionRefreshesIncompleteFastPathEvidence() { var f=Setup(State(GameState.WorldMap,true),ContinentMapWithAnchors(),ContinentMapWithAnchors(),State(GameState.WorldMap)); f.Detector.ScreenshotStates.Enqueue(WorldMapWithPin()); var r=f.Service.RepositionToAllianceTerritoryAsync("d",Token).GetAwaiter().GetResult(); Assert(!r.Success,"Strict mode must not turn a refreshed legacy anchor into a movement target."); Equal(1,f.Detector.FrameCalls,"Full-frame refresh count."); Assert(r.Transitions.Any(x=>x.Message.Contains("bounded full-frame evidence refresh")),"Refresh transition was not recorded."); }
        private static void TapWorldMapPointRejectsOutsideFrame() { var f=Setup(State(GameState.WorldMap,true)); var r=f.Service.TapWorldMapPointAsync("d",-1,100,Token).GetAwaiter().GetResult(); Assert(!r.Success,"Out-of-bounds point must fail safely."); Equal(0,f.Client.TapCalls,"Out-of-bounds point must not be tapped."); Equal("PointOutsideFrame",r.FailureReason,"Failure reason."); Equal(0,r.TapCount,"Tap count."); }
        private static void TapWorldMapPointSendsVerifiedTap() { var f=Setup(State(GameState.WorldMap,true),State(GameState.WorldMap)); var r=f.Service.TapWorldMapPointAsync("d",100,120,Token).GetAwaiter().GetResult(); Assert(r.Success,"Verified WorldMap point tap should succeed."); Equal(1,f.Client.TapCalls,"Tap count."); Equal(100,f.Client.LastX,"Tap X."); Equal(120,f.Client.LastY,"Tap Y."); Equal(1,r.TapCount,"Result tap count."); Assert(r.VerificationSucceeded,"Verification flag."); }
        private static void TapWorldMapPointRequiresVerification() { var f=Setup(State(GameState.WorldMap,true),State(GameState.City)); var r=f.Service.TapWorldMapPointAsync("d",100,120,Token).GetAwaiter().GetResult(); Assert(!r.Success,"Point tap without final WorldMap verification must fail."); Equal(1,f.Client.TapCalls,"Tap count."); Assert(!r.VerificationSucceeded,"Verification flag."); }
        private static void PollCancellation() { var f=SetupWithOptions(new WorldMapNavigationOptions(100,1,1),State(GameState.ResourceSearchPanel)); using(var c=new CancellationTokenSource(30)) Throws<OperationCanceledException>(()=>f.Service.EnsureWorldMapAsync("d",c.Token).GetAwaiter().GetResult()); }
        private static void LockCancellation() { var f=SetupDelayed(250); Task first=f.Service.EnsureWorldMapAsync("same",Token); using(var c=new CancellationTokenSource(30)) Throws<OperationCanceledException>(()=>f.Service.EnsureWorldMapAsync("same",c.Token).GetAwaiter().GetResult()); first.GetAwaiter().GetResult(); }
        private static void SameDeviceSerialized() { var f=SetupDelayed(100); Task.WaitAll(f.Service.EnsureWorldMapAsync("same",Token),f.Service.EnsureWorldMapAsync("same",Token)); Equal(1,f.Detector.MaxActive,"Same device overlapped."); }
        private static void DifferentDevicesParallel() { var f=SetupDelayed(100); Task.WaitAll(f.Service.EnsureWorldMapAsync("a",Token),f.Service.EnsureWorldMapAsync("b",Token)); Assert(f.Detector.MaxActive>=2,"Different devices globally blocked."); }
        private static void NoProhibitedInput() { var f=Setup(State(GameState.WorldMap,true),State(GameState.ResourceSearchPanel)); f.Service.OpenResourceSearchPanelAsync("d",Token).GetAwaiter().GetResult(); Equal(0,f.Client.ProhibitedCalls,"Prohibited input called."); }

        private static byte[] PinTerritoryScreenshot(Color home,Color destination,bool paintIcons)
        {
            using(var bitmap=new Bitmap(1280,720))
            using(var graphics=Graphics.FromImage(bitmap))
            {
                graphics.Clear(home);
                using(var destinationBrush=new SolidBrush(destination))
                {
                    graphics.FillRectangle(destinationBrush,698,384,12,12);
                    graphics.FillRectangle(destinationBrush,736,384,12,12);
                }
                if(paintIcons)
                {
                    using(var iconBrush=new SolidBrush(Color.FromArgb(145,45,180)))
                    {
                        graphics.FillRectangle(iconBrush,676,406,18,31);
                        graphics.FillRectangle(iconBrush,714,365,18,31);
                    }
                }
                return ToPng(bitmap);
            }
        }
        private static byte[] FallbackTerritoryScreenshot(
            Color home,Color destination,int pinX=680,int pinY=350)
        {
            using(var bitmap=new Bitmap(1280,720))
            using(var graphics=Graphics.FromImage(bitmap))
            {
                graphics.Clear(home);
                using(var brush=new SolidBrush(destination))
                {
                    graphics.FillRectangle(brush,pinX-16,pinY+19,12,12);
                    graphics.FillRectangle(brush,pinX+22,pinY+19,12,12);
                }
                using(var pinBrush=new SolidBrush(Color.FromArgb(255,211,148)))
                {
                    graphics.FillEllipse(pinBrush,pinX,pinY,18,18);
                    graphics.FillPolygon(pinBrush,new[]
                    {
                        new Point(pinX+2,pinY+10),
                        new Point(pinX+16,pinY+10),
                        new Point(pinX+9,pinY+30)
                    });
                }
                return ToPng(bitmap);
            }
        }
        private static byte[] HomePinScreenshot(Color territory)
        {
            using(var bitmap=new Bitmap(1280,720))
            using(var graphics=Graphics.FromImage(bitmap))
            {
                graphics.Clear(territory);
                using(var pinBrush=new SolidBrush(Color.FromArgb(65,220,190)))
                {
                    graphics.FillEllipse(pinBrush,670,390,18,18);
                    graphics.FillPolygon(pinBrush,new[]
                    {
                        new Point(672,400),new Point(686,400),new Point(679,420)
                    });
                }
                return ToPng(bitmap);
            }
        }
        private static byte[] CompactHomePinScreenshot(Color territory)
        {
            using(var bitmap=new Bitmap(1280,720))
            using(var graphics=Graphics.FromImage(bitmap))
            {
                graphics.Clear(territory);
                using(var pinBrush=new SolidBrush(Color.FromArgb(65,220,190)))
                {
                    graphics.FillEllipse(pinBrush,588,252,7,7);
                    graphics.FillPolygon(pinBrush,new[]
                    {
                        new Point(589,257),new Point(594,257),new Point(591,269)
                    });
                }
                return ToPng(bitmap);
            }
        }
        private static byte[] SolidScreenshot(Color color)
        {
            using(var bitmap=new Bitmap(1280,720))
            using(var graphics=Graphics.FromImage(bitmap))
            {
                graphics.Clear(color);
                return ToPng(bitmap);
            }
        }
        private static void QueueFocusedCoordinates(Fixture fixture,int x,int y,int attempts)
        {
            fixture.Client.FocusedValues.Enqueue(x);
            fixture.Client.FocusedValues.Enqueue(y);
        }
        private static byte[] ToPng(Bitmap bitmap)
        {
            using(var stream=new MemoryStream())
            {
                bitmap.Save(stream,ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static NavigationResult RunEnsure(GameDetectionResult[] states) { var f=Setup(states); return f.Service.EnsureWorldMapAsync("d",Token).GetAwaiter().GetResult(); }
        private static Fixture Setup(params GameDetectionResult[] states)=>SetupWithOptions(new WorldMapNavigationOptions(10,1,2,false,5,100,20,3000,1,true),states);
        private static Fixture SetupDelayed(int delay) { var d=new FakeDetector(State(GameState.WorldMap)){DelayMs=delay}; return FixtureOf(d,new WorldMapNavigationOptions(10,1,2)); }
        private static Fixture SetupWithOptions(WorldMapNavigationOptions options,params GameDetectionResult[] states)=>FixtureOf(new FakeDetector(states),options);
        private static Fixture FixtureOf(FakeDetector detector,WorldMapNavigationOptions options) { var client=new FakeClient{DefaultScreenshot=SolidScreenshot(TerritoryGreen)}; return new Fixture{Client=client,Detector=detector,Service=new WorldMapNavigationService(client,detector,options,new FakeLogger())}; }
        private static GameDetectionResult State(GameState state,bool bounds=false) { var evidence=new List<GameDetectionEvidence>(); if(state==GameState.WorldMap)evidence.Add(new GameDetectionEvidence{TemplateId=TemplateId.WorldMapAnchor,TemplateExists=true,Found=bounds,MatchResult=bounds?ImageMatchResult.FoundAt(10,20,30,40):null,Message="world"}); if(state==GameState.City)evidence.Add(new GameDetectionEvidence{TemplateId=TemplateId.CityToWorldMapButton,TemplateExists=true,Found=bounds,MatchResult=bounds?ImageMatchResult.FoundAt(10,20,30,40):null,Message="city"}); if(state==GameState.ResourceSearchPanel){evidence.Add(new GameDetectionEvidence{TemplateId=TemplateId.ResourceSearchPanelAnchor,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(700,300,100,60),Message="panel"});evidence.Add(new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(933,549,185,70),SearchRegion=new ImageRegion(900,500,300,180),Message="search"});} return new GameDetectionResult{State=state,IsSuccessful=true,Evidence=evidence.AsReadOnly()}; }
        private static GameDetectionResult WorldMapWithPin() => new GameDetectionResult{State=GameState.WorldMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.WorldMapAnchor,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(10,20,30,40),Message="world"},new GameDetectionEvidence{TemplateId=TemplateId.WorldMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,500,40,40),Message="pin map"}}.AsReadOnly()};
        private static GameDetectionResult ContinentMapWithAnchors() => new GameDetectionResult{State=GameState.ContinentMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapTitle,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,10,120,40),Message="continent"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeTerritoryAnchor,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(700,120,30,30),Message="home territory"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(180,20,34,34),Message="coordinate pin"}}.AsReadOnly()};
        private static GameDetectionResult ContinentMapWithCheckedPinsAndAnchors() => new GameDetectionResult{State=GameState.ContinentMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapTitle,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,10,120,40),Message="continent"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeTerritoryAnchor,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(700,120,30,30),Message="home territory"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(180,20,34,34),Message="coordinate pin"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeLocationPin,TemplateExists=true,Found=false,Message="cyan pin checked"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapSearchTargetPin,TemplateExists=true,Found=false,Message="yellow pin checked"}}.AsReadOnly()};
        private static GameDetectionResult ContinentMapWithCoordinateTargetAndAnchors() => new GameDetectionResult{State=GameState.ContinentMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapTitle,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,10,120,40),Message="continent"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeTerritoryAnchor,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(700,120,30,30),Message="home territory"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(180,20,34,34),Message="coordinate pin"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeLocationPin,TemplateExists=true,Found=false,Message="cyan pin checked"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapSearchTargetPin,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(680,350,18,31),Message="fresh X/Y destination pin"}}.AsReadOnly()};
        private static GameDetectionResult ContinentMapWithOnlyHomePinAndAnchors() => new GameDetectionResult{State=GameState.ContinentMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapTitle,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,10,120,40),Message="continent"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(180,20,34,34),Message="coordinate pin"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeLocationPin,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(650,330,18,31),Message="cyan pin"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapSearchTargetPin,TemplateExists=true,Found=false,Message="yellow pin checked"}}.AsReadOnly()};
        private static GameDetectionResult ContinentMapWithPinPair(int homeX,int homeY,int targetX,int targetY) => new GameDetectionResult{State=GameState.ContinentMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapTitle,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,10,120,40),Message="continent"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeLocationPin,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(homeX,homeY,18,31),Message="cyan home pin"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapSearchTargetPin,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(targetX,targetY,18,31),Message="yellow search pin"}}.AsReadOnly()};
        private static GameDetectionResult UnknownWithContinentHomePin() => new GameDetectionResult{State=GameState.Unknown,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeLocationPin,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(670,390,18,31),Message="cyan home pin"}}.AsReadOnly()};
        private static GameDetectionResult UnknownWithContinentCoordinatePin() => new GameDetectionResult{State=GameState.Unknown,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(180,20,34,34),Message="coordinate pin"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeLocationPin,TemplateExists=true,Found=false,Message="cyan pin checked"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapSearchTargetPin,TemplateExists=true,Found=false,Message="yellow pin checked"}}.AsReadOnly()};
        private static GameDetectionResult ContinentMapWithSearchPin(int targetX,int targetY) => new GameDetectionResult{State=GameState.ContinentMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapSearchTargetPin,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(targetX,targetY,18,31),Message="yellow search pin"}}.AsReadOnly()};
        private static GameDetectionResult ContinentMapWithFarTerritoryAnchor() => new GameDetectionResult{State=GameState.ContinentMap,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapTitle,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,10,120,40),Message="continent"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapHomeTerritoryAnchor,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(1100,40,30,30),Message="far territory"},new GameDetectionEvidence{TemplateId=TemplateId.ContinentMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(180,20,34,34),Message="coordinate pin"}}.AsReadOnly()};
        private static GameDetectionResult UnknownWithCancel() => new GameDetectionResult{State=GameState.Unknown,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.StorageLimitCancelButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(390,470,210,60),Message="cancel"}}.AsReadOnly()};
        private static GameDetectionResult UnknownWithWorldMapCancel() => new GameDetectionResult{State=GameState.Unknown,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.StorageLimitCancelButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(390,470,210,60),Message="cancel"},new GameDetectionEvidence{TemplateId=TemplateId.WorldMapPinButton,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(100,500,40,40),Message="pin map under dialog"}}.AsReadOnly()};
        private static GameDetectionResult UnverifiedPanel() => new GameDetectionResult{State=GameState.ResourceSearchPanel,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>().AsReadOnly()};
        private static GameDetectionResult PartialPanel() => new GameDetectionResult{State=GameState.Unknown,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,TemplateExists=true,Found=true,MatchResult=ImageMatchResult.FoundAt(933,549,185,70),SearchRegion=new ImageRegion(900,500,300,180),Message="search"}}.AsReadOnly()};
        private static GameDetectionResult PanelFromResourceTab() => new GameDetectionResult{State=GameState.ResourceSearchPanel,IsSuccessful=true,Evidence=new List<GameDetectionEvidence>{new GameDetectionEvidence{TemplateId=TemplateId.ResourceTabUnselected,TemplateExists=true,Found=true,Message="resource tab"},new GameDetectionEvidence{TemplateId=TemplateId.SearchButtonEnabled,TemplateExists=true,Found=true,Message="search"}}.AsReadOnly()};
        private static string ReadNavigationSource() { DirectoryInfo directory=new DirectoryInfo(AppContext.BaseDirectory); while(directory!=null) { string path=Path.Combine(directory.FullName,"ADB","Infrastructure","Navigation","WorldMapNavigationService.cs"); if(File.Exists(path)) return File.ReadAllText(path); directory=directory.Parent; } throw new InvalidOperationException("WorldMapNavigationService source was not found."); }
        private static string Between(string source,string start,string end) { int begin=source.IndexOf(start,StringComparison.Ordinal); int finish=source.IndexOf(end,begin+start.Length,StringComparison.Ordinal); Assert(begin>=0&&finish>begin,"Expected source markers were not found."); return source.Substring(begin,finish-begin); }
        private static void Assert(bool c,string m){if(!c)throw new Exception(m);} private static void Equal<T>(T e,T a,string m){if(!EqualityComparer<T>.Default.Equals(e,a))throw new Exception($"{m} Expected={e}, Actual={a}");} private static void Throws<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
        private sealed class Fixture{public FakeClient Client;public FakeDetector Detector;public WorldMapNavigationService Service;}
        private sealed class RecordingProgress:IProgress<NavigationTransition>{public readonly List<NavigationTransition> Items=new List<NavigationTransition>();public void Report(NavigationTransition value){Items.Add(value);}}
        private sealed class FakeLogger:IDiagnosticLogger{public void Info(string m){}public void Error(string m,Exception e){}}
        private sealed class FakeDetector:IGameStateDetector,IFrameGameStateDetector
        { private readonly Queue<GameDetectionResult> q; private GameDetectionResult last; private int active; public readonly Queue<GameDetectionResult> ScreenshotStates=new Queue<GameDetectionResult>();public int DelayMs; public int MaxActive,FrameCalls; public FakeDetector(params GameDetectionResult[] s){q=new Queue<GameDetectionResult>(s);last=s.Length>0?s[s.Length-1]:State(GameState.Unknown);} public async Task<GameDetectionResult> DetectAsync(string d,CancellationToken t){int now=Interlocked.Increment(ref active);MaxActive=Math.Max(MaxActive,now);try{if(DelayMs>0)await Task.Delay(DelayMs,t);lock(q){if(q.Count>0)last=q.Dequeue();return last;}}finally{Interlocked.Decrement(ref active);}} public GameDetectionResult Detect(byte[] p){return NextScreenshotState();}public GameDetectionResult Detect(CapturedFrame f,string d,GameStateDetectionContext c){FrameCalls++;return NextScreenshotState();}private GameDetectionResult NextScreenshotState(){if(ScreenshotStates.Count>0)last=ScreenshotStates.Dequeue();return last;} }
        private sealed class FakeClient:ILdPlayerClient,IFocusedInputValueReader,IFrameCapturingLdPlayerClient
        {public int BackCalls,TapCalls,ProhibitedCalls,InputCalls,DeleteCalls,EnterCalls,LastX,LastY;public byte[] DefaultScreenshot;public readonly Queue<byte[]> Screenshots=new Queue<byte[]>();public readonly Queue<int> FocusedValues=new Queue<int>();public readonly List<string> InputValues=new List<string>();public readonly List<string> Actions=new List<string>();public int TotalInput=>BackCalls+TapCalls+ProhibitedCalls+InputCalls+DeleteCalls+EnterCalls;public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t)=>Task.FromResult<IReadOnlyList<string>>(new[]{"LDPlayer"});public Task BackAsync(string d,CancellationToken t){BackCalls++;return Task.CompletedTask;}public Task TapAsync(string d,int x,int y,CancellationToken t){TapCalls++;LastX=x;LastY=y;Actions.Add($"Tap:{x},{y}");return Task.CompletedTask;}public Task<bool> IsRunningAsync(string d,CancellationToken t)=>Task.FromResult(true);public Task<byte[]> CaptureScreenshotPngAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();return Task.FromResult(Screenshots.Count>0?Screenshots.Dequeue():DefaultScreenshot);}public async Task<CapturedFrame> CaptureFrameAsync(string d,CancellationToken t){byte[] png=await CaptureScreenshotPngAsync(d,t);using(var stream=new MemoryStream(png,false))using(var source=new Bitmap(stream))return new CapturedFrame(new Bitmap(source),DateTimeOffset.UtcNow);}public Task OpenAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task CloseAsync(string d,CancellationToken t)=>Task.CompletedTask;public Task RunAppAsync(string d,string p,CancellationToken t)=>Task.CompletedTask;public Task TapByPercentAsync(string d,double x,double y,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task LongPressAsync(string d,int x,int y,int ms,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task SwipeByPercentAsync(string d,double sx,double sy,double ex,double ey,int ms,CancellationToken t){ProhibitedCalls++;return Task.CompletedTask;}public Task InputTextAsync(string d,string s,CancellationToken t){InputCalls++;InputValues.Add(s);Actions.Add("Input:"+s);return Task.CompletedTask;}public Task<int> ReadFocusedIntegerAsync(string d,CancellationToken t){t.ThrowIfCancellationRequested();return Task.FromResult(FocusedValues.Count>0?FocusedValues.Dequeue():int.Parse(InputValues[InputValues.Count-1]));}public Task PressKeyAsync(string d,AndroidKeyCode k,CancellationToken t){if(k==AndroidKeyCode.Delete)DeleteCalls++;else if(k==AndroidKeyCode.Enter)EnterCalls++;else ProhibitedCalls++;return Task.CompletedTask;}}
    }
}
