using IK_Auto_ADB.Core.Workflows;
using System;

namespace IK_Auto_ADB.Infrastructure.Workflows
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
