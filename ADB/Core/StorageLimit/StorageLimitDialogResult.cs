using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using System.Collections.Generic;
using System;

namespace ADB_Tool_Automation_Post_FB.Core.StorageLimit
{
    public sealed class StorageLimitDialogResult
    {
        public StorageLimitDialogOutcome Outcome { get; set; }
        public StorageLimitPolicy Policy { get; set; }
        public bool DialogVerified { get; set; }
        public bool ConfirmButtonVerified { get; set; }
        public bool CancelButtonVerified { get; set; }
        public bool Success { get; set; }
        public int ActionTapCount { get; set; }
        public GameState StateAfterConfirmation { get; set; }
        public GameState InitialState { get; set; }
        public GameState StateAfterCancel { get; set; }
        public GameState FinalState { get; set; }
        public bool ModalClosed { get; set; }
        public bool ReturnedToWorldMap { get; set; }
        public bool ReturnedToSearchPanel { get; set; }
        public bool ReturnedToTeamSelection { get; set; }
        public bool BackSent { get; set; }
        public int BackCount { get; set; }
        public int RecoveryTransitions { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
        public TimeSpan Duration { get; set; }
        public IReadOnlyList<GameDetectionEvidence> Evidence { get; set; }
    }
}
