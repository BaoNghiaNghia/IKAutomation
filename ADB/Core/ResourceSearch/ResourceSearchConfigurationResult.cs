using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceSearchConfigurationResult
    {
        public bool Success { get; set; }
        public ResourceType RequestedResource { get; set; }
        public int RequestedLevel { get; set; }
        public int? ObservedLevel { get; set; }
        public bool RequestedUnoccupiedOnly { get; set; }
        public bool ResourceVerified { get; set; }
        public bool LevelVerified { get; set; }
        public bool FilterVerified { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public int TapCount { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public IReadOnlyList<ConfigurationStepResult> Steps { get; set; }
    }
}
