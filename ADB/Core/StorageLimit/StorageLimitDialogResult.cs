using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.StorageLimit
{
    public sealed class StorageLimitDialogResult
    {
        public StorageLimitDialogOutcome Outcome { get; set; }
        public StorageLimitPolicy Policy { get; set; }
        public bool DialogVerified { get; set; }
        public bool ConfirmButtonVerified { get; set; }
        public int ActionTapCount { get; set; }
        public GameState StateAfterConfirmation { get; set; }
        public bool ModalClosed { get; set; }
        public bool ReturnedToWorldMap { get; set; }
        public bool ReturnedToSearchPanel { get; set; }
        public int RecoveryTransitions { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public IReadOnlyList<GameDetectionEvidence> Evidence { get; set; }
    }
}
