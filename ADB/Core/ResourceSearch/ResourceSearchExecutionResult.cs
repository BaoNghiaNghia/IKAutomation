using IK_Auto_ADB.Core.GameDetection;
using IK_Auto_ADB.Core.ResourcePopup;
using System;
using System.Collections.Generic;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public sealed class ResourceSearchExecutionResult
    {
        public ResourceSearchOutcome Outcome { get; set; }
        public bool Success { get; set; }
        public ResourceSearchConfigurationResult ConfigurationResult { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public int SearchTapCount { get; set; }
        public int ObservedFrameCount { get; set; }
        public bool SearchButtonVerified { get; set; }
        public bool PanelClosed { get; set; }
        public bool CameraMovementObserved { get; set; }
        public bool CameraStabilityVerified { get; set; }
        public bool NotFoundToastVerified { get; set; }
        public bool NotFoundObserved { get; set; }
        public string MatchedNotFoundVariant { get; set; }
        public ResourceSearchFailureReason FailureReason { get; set; }
        public ResourceSearchToastEvidence ToastEvidence { get; set; }
        public bool PanelRemainedOpen { get; set; }
        public bool MovementDetected { get; set; }
        public bool ShouldRetrySearch { get; set; }
        public bool ShouldTryLowerLevel { get; set; }
        public bool ShouldRepositionMap { get; set; }
        public ResourcePopupVerificationResult PopupVerificationResult { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
        public IReadOnlyList<ResourceSearchObservation> Observations { get; set; }
    }
}
