using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ConfigurationStepResult
    {
        public string StepName { get; set; }
        public bool Success { get; set; }
        public int Attempts { get; set; }
        public IReadOnlyList<ConfigurationTemplateEvidence> TemplateEvidence { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }
}
