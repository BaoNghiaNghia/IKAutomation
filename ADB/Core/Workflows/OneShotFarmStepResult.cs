using System;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public sealed class OneShotFarmStepResult
    {
        public OneShotFarmStep Step { get; set; }
        public bool Success { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public string DiagnosticScreenshotPath { get; set; }
        public object Detail { get; set; }
    }
}
