using System.Collections.Generic;

namespace IK_Auto_ADB.Core.ResourceSearch
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
