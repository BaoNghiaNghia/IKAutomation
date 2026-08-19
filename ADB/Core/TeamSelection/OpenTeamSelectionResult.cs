using IK_Auto_ADB.Core.GameDetection;
using IK_Auto_ADB.Core.Vision;
using System;
using System.Collections.Generic;

namespace IK_Auto_ADB.Core.TeamSelection
{
    public sealed class OpenTeamSelectionResult
    {
        public OpenTeamSelectionOutcome Outcome { get; set; }
        public bool Success { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public bool ResourcePopupVerified { get; set; }
        public bool GatherButtonVerified { get; set; }
        public bool TeamSelectionVerified { get; set; }
        public bool TeamSelectionReady { get; set; }
        public bool PanelAnchorVerified { get; set; }
        public bool AdjustFormationButtonVerified { get; set; }
        public bool TeamActionButtonVerified { get; set; }
        public ImageMatchResult GatherButtonMatch { get; set; }
        public int GatherTapCount { get; set; }
        public int ObservedFrameCount { get; set; }
        public int TransientUnknownFrameCount { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
        public IReadOnlyList<GameDetectionEvidence> FinalEvidence { get; set; }
        public IReadOnlyList<TeamSelectionObservation> Observations { get; set; }
    }
}
