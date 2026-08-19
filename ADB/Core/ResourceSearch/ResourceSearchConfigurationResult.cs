using IK_Auto_ADB.Core.GameDetection;
using System;
using System.Collections.Generic;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public sealed class ResourceSearchConfigurationResult
    {
        public bool Success { get; set; }
        public ResourceType RequestedResource { get; set; }
        public int RequestedLevel { get; set; }
        public int? ObservedLevel { get; set; }
        public int? EffectiveLevel { get; set; }
        public bool RequestedLevelReached { get; set; }
        public bool LevelCapped { get; set; }
        public bool RequestedUnoccupiedOnly { get; set; }
        public bool ResourceVerified { get; set; }
        public bool LevelVerified { get; set; }
        public bool AccountCeilingAccepted { get; set; }
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
