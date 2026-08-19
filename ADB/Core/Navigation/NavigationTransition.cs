using System;

namespace ADB_Tool_Automation_Post_FB.Core.Navigation
{
    public sealed class NavigationTransition
    {
        public string Operation { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public string Message { get; set; }
    }
}
