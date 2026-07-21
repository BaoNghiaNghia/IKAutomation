using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceLevelFallbackPolicy
    {
        public ResourceLevelFallbackPolicy()
        {
            Levels = new[] { 7, 6, 5 };
            AttemptsPerLevel = 1;
            StopOnFirstLocated = true;
            WaitForToastClearBetweenAttempts = true;
        }

        public IReadOnlyList<int> Levels { get; set; }
        public int AttemptsPerLevel { get; set; }
        public bool StopOnFirstLocated { get; set; }
        public bool WaitForToastClearBetweenAttempts { get; set; }
        public string RunId { get; set; }

        public string Validate(int minimumLevel, int maximumLevel)
        {
            if (Levels == null || Levels.Count == 0) return "Levels cannot be empty.";
            if (Levels.Distinct().Count() != Levels.Count) return "Levels cannot contain duplicates.";
            if (Levels.Any(level => level < minimumLevel || level > maximumLevel))
                return $"Every level must be between {minimumLevel} and {maximumLevel}.";
            if (AttemptsPerLevel < 1 || AttemptsPerLevel > 3)
                return "AttemptsPerLevel must be between 1 and 3.";
            return null;
        }
    }
}
