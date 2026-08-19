namespace IK_Auto_ADB.Core.Vision
{
    public sealed class VisionOperationMetrics
    {
        public long VisionGateWaitMs { get; set; }
        public long VisionProcessingDurationMs { get; set; }
        public long VisionTotalDurationMs { get; set; }
        public int VisionQueueDepth { get; set; }
        public int ActiveVisionOperations { get; set; }
        public int VisionGateLimit { get; set; }
        public long VisionOperationCount { get; set; }
        public int PeakActiveVisionOperations { get; set; }
        public long MaxVisionGateWaitMs { get; set; }
        public long TemplateCount { get; set; }
        public long MatchFound { get; set; }
        public long FailureCount { get; set; }
    }
}
