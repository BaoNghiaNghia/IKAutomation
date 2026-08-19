using System;

namespace IK_Auto_ADB.Core.Workflows
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
