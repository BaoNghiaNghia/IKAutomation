using System;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceLevelAttemptResult
    {
        public int Level { get; set; }
        public int AttemptNumber { get; set; }
        public bool ConfigurationSucceeded { get; set; }
        public ResourceSearchOutcome? SearchOutcome { get; set; }
        public bool ToastClearVerifiedBeforeAttempt { get; set; }
        public string MatchedNotFoundVariant { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public ToastClearResult ToastClearResult { get; set; }
        public ResourceSearchConfigurationResult ConfigurationResult { get; set; }
        public ResourceSearchExecutionResult SearchResult { get; set; }
    }
}
