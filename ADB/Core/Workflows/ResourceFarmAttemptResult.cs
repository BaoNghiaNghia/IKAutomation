using IK_Auto_ADB.Core.MarchDispatch;
using IK_Auto_ADB.Core.ResourcePopup;
using IK_Auto_ADB.Core.ResourceSearch;
using IK_Auto_ADB.Core.TeamSelection;
using System;
using System.Collections.Generic;

namespace IK_Auto_ADB.Core.Workflows
{
    public sealed class ResourceFarmAttemptResult
    {
        public ResourceType ResourceType { get; set; }
        public IReadOnlyList<int> AttemptedLevels { get; set; }
        public int? LocatedLevel { get; set; }
        public bool SearchLevelsExhausted { get; set; }
        public bool StorageLimitDetected { get; set; }
        public bool ResourceExpiryDetected { get; set; }
        public bool StorageLimitConfirmed { get; set; }
        public bool MarkedStorageFull { get; set; }
        public bool RecoverySucceeded { get; set; }
        public ResourceLevelFallbackResult LevelFallbackResult { get; set; }
        public ResourcePopupVerificationResult PopupResult { get; set; }
        public OpenTeamSelectionResult OpenTeamResult { get; set; }
        public SelectFarmTeamResult SelectTeamResult { get; set; }
        public DispatchMarchResult DispatchResult { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }
}
