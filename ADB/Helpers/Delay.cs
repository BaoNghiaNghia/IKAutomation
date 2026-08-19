using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Helpers
{
    public static class DelayHelper
    {
        public static async Task DelayAsync(int minSeconds, int maxSeconds)
        {
            Random random = new Random();
            int delay = random.Next(minSeconds, maxSeconds + 1);
            await Task.Delay(TimeSpan.FromSeconds(delay));
        }
    }
}
