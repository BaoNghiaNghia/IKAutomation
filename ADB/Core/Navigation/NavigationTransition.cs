using System;

namespace IK_Auto_ADB.Core.Navigation
{
    public sealed class NavigationTransition
    {
        public string Operation { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public string Message { get; set; }
    }
}
