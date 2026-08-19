namespace IK_Auto_ADB.Core.ResourceSearch
{
    /// <summary>
    /// Positive toast evidence retained for one bounded Search tap attempt.
    /// It is diagnostic data only; workflow routing uses FailureReason.
    /// </summary>
    public sealed class ResourceSearchToastEvidence
    {
        public bool MainAnchorSeen { get; set; }
        public bool ActionAnchorSeen { get; set; }
        public bool ShortAnchorSeen { get; set; }
        public bool OtherRegionAnchorSeen { get; set; }
        public bool TargetLevelTooLowSeen { get; set; }
        public bool SeasonMapSeen { get; set; }
        public int FirstSeenFrame { get; set; }
        public int LastSeenFrame { get; set; }
    }
}
