using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public sealed class SystemRandomProvider : IRandomProvider
    {
        private readonly Random random = new Random();
        private readonly object sync = new object();

        public int Next(int maxExclusive)
        {
            lock (sync) return random.Next(maxExclusive);
        }
    }
}
