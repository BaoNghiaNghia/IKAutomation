using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.Navigation
{
    public sealed class NavigationResult
    {
        public bool Success { get; set; }
        public GameState InitialState { get; set; }
        public GameState FinalState { get; set; }
        public int Attempts { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
        public IReadOnlyList<GameDetectionEvidence> FinalEvidence { get; set; }
        public IReadOnlyList<NavigationTransition> Transitions { get; set; }
    }
}
