using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceLevelFallbackResult
    {
        public ResourceLevelFallbackOutcome Outcome { get; set; }
        public bool Success { get; set; }
        public ResourceType ResourceType { get; set; }
        public int? LocatedLevel { get; set; }
        public int? LastAttemptedLevel { get; set; }
        public IReadOnlyList<int> RequestedLevels { get; set; }
        public IReadOnlyList<ResourceLevelAttemptResult> Attempts { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
    }
}
