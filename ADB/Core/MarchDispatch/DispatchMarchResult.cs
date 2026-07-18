using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.StorageLimit;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public sealed class DispatchMarchResult
    {
        public DispatchMarchOutcome Outcome { get; set; }
        public bool Success { get; set; }
        public TeamNumber ExpectedTeam { get; set; }
        public TeamNumber? DispatchedTeam { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public bool TeamSelectionVerified { get; set; }
        public bool ExpectedTeamSelectedBeforeTap { get; set; }
        public bool ActionButtonVerified { get; set; }
        public bool TeamSelectionClosed { get; set; }
        public bool WorldMapVerified { get; set; }
        public bool SelectedBorderDisappeared { get; set; }
        public bool TeamRegionChanged { get; set; }
        public bool BusyStatusVerified { get; set; }
        public bool MarchTimerVerified { get; set; }
        public bool ExpectedTeamReadyBeforeDispatch { get; set; }
        public bool ExpectedTeamReadyAfterDispatch { get; set; }
        public bool TimerContentBeforeDispatch { get; set; }
        public bool DirectMarchVerified { get; set; }
        public bool StructuralMarchVerified { get; set; }
        public bool ReadyAnchorDisappeared { get; set; }
        public bool ExpectedTeamTimerVerified { get; set; }
        public double FinalTimerForegroundRatio { get; set; }
        public double FinalTimerDifferenceRatio { get; set; }
        public MarchVerificationMode VerificationMode { get; set; }
        public bool MarchStartedVerified { get; set; }
        public bool StorageLimitDialogDetected { get; set; }
        public bool ResourceExpiryDialogDetected { get; set; }
        public bool ResourceExpiryCancelled { get; set; }
        public bool StorageLimitConfirmed { get; set; }
        public bool StorageLimitCancelled { get; set; }
        public bool ResourceSwitchRequired { get; set; }
        public ResourceType? StorageFullResource { get; set; }
        public StorageLimitDialogResult StorageLimitResult { get; set; }
        public double? TeamRegionDifference { get; set; }
        public int ActionTapCount { get; set; }
        public int ObservedFrameCount { get; set; }
        public int ConsecutiveSuccessFrames { get; set; }
        public int TransientUnknownFrameCount { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
        public IReadOnlyList<MarchDispatchObservation> Observations { get; set; }
    }
}
