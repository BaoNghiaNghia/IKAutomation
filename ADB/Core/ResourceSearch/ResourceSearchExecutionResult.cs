using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
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
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
        public IReadOnlyList<ResourceSearchObservation> Observations { get; set; }
    }
}
