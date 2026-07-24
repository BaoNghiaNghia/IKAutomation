using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.ResourcePopup
{
    public sealed class ResourcePopupVerificationResult
    {
        public ResourcePopupOutcome Outcome { get; set; }
        public bool Success { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public bool PopupAnchorVerified { get; set; }
        public bool IronResourceVerified { get; set; }
        public bool ResourceVerified { get; set; }
        public ResourceType ResourceType { get; set; }
        public ResourceType ExpectedResource { get; set; }
        public bool ExpectedResourceVerified { get; set; }
        public TemplateId ExpectedPopupTitleTemplate { get; set; }
        public ResourceType? MismatchedResource { get; set; }
        public bool GatherButtonVerified { get; set; }
        public bool PopupAnchorFound { get; set; }
        public bool ExpectedResourceTitleFound { get; set; }
        public bool GatherButtonFound { get; set; }
        public ImageRegion HeaderRegion { get; set; }
        public ImageRegion ActionRegion { get; set; }
        public ImageMatchResult GatherButtonMatch { get; set; }
        public int ObservedFrameCount { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
        public IReadOnlyList<GameDetectionEvidence> Evidence { get; set; }
    }
}
