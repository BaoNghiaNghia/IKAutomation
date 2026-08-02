namespace ADB_Tool_Automation_Post_FB.Core.Vision
{
    public sealed class VisionOperationMetrics
    {
        public long VisionGateWaitMs { get; set; }
        public long VisionProcessingDurationMs { get; set; }
        public long VisionTotalDurationMs { get; set; }
        public int VisionQueueDepth { get; set; }
        public int ActiveVisionOperations { get; set; }
        public long TemplateCount { get; set; }
        public long MatchFound { get; set; }
        public long FailureCount { get; set; }
    }
}
