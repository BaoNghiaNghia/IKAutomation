using IK_Auto_ADB.Core.GameDetection;
using IK_Auto_ADB.Core.Vision;
using System;
using System.Collections.Generic;

namespace IK_Auto_ADB.Core.Navigation
{
    public sealed class NavigationResult
    {
        public bool Success { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public int Attempts { get; set; }
        public int TapCount { get; set; }
        public int? TapX { get; set; }
        public int? TapY { get; set; }
        public bool VerificationSucceeded { get; set; }
        public string FailureReason { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public IReadOnlyList<GameDetectionEvidence> FinalEvidence { get; set; }
        public IReadOnlyList<NavigationTransition> Transitions { get; set; }

        // Screenshot-probe diagnostics for resource search panel handoff.
        public bool ScreenshotConfirmed { get; set; }
        public int ConfirmationFrames { get; set; }
        public ImageMatchResult SearchButtonBounds { get; set; }
        public bool SearchButtonBoundsStable { get; set; }
        public bool SearchButtonBoundsComparisonPerformed { get; set; }
        public bool SearchButtonExpectedRegion { get; set; }
        public bool SearchButtonInsideExpectedRegion { get; set; }
        public string ConfirmationMode { get; set; }
        public int SearchIconTapCount { get; set; }
        public int BackCount { get; set; }
    }
}
