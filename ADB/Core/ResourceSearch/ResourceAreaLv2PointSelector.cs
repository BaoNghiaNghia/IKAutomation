using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public sealed class ResourceAreaLv2PointSelector
    {
        private static readonly Point[] BasePoints = new[]
        {
            new Point(210,261),new Point(178,276),new Point(199,306),new Point(225,279),new Point(302,265),
            new Point(274,294),new Point(268,327),new Point(279,357),new Point(304,356),new Point(342,339),
            new Point(323,302),new Point(321,240),new Point(339,267),new Point(348,292),new Point(369,303),
            new Point(428,258),new Point(461,245),new Point(469,177),new Point(422,201),new Point(525,204),
            new Point(517,245),new Point(549,267),new Point(567,266),new Point(609,261),new Point(609,218),
            new Point(319,702),new Point(330,681),new Point(340,664),new Point(357,654),new Point(370,636),
            new Point(395,623),new Point(403,634),new Point(407,641),new Point(421,654),new Point(423,669),
            new Point(408,673),new Point(395,680),new Point(398,693),new Point(380,714),new Point(360,693),
            new Point(342,699),new Point(324,707),new Point(325,720),new Point(330,734),new Point(348,747),
            new Point(370,740),new Point(363,714)
        };
        private static readonly IReadOnlyList<Point> BasePointView = Array.AsReadOnly(BasePoints);
        private readonly Dictionary<string, List<Point>> bags = new Dictionary<string, List<Point>>();
        private readonly Random random;
        private readonly object sync = new object();
        public const int MaxResourceAreaLv2PointAttempts = 3;
        public ResourceAreaLv2PointSelector(Random random = null) { this.random = random ?? new Random(); }
        public static IReadOnlyList<Point> Points1280x720 => BasePointView;
        public ResourceAreaLv2PointSelection Next(string runId, string deviceName, ResourceType resource, int level, int areaEpoch, int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
            string key = string.Join("|", runId ?? string.Empty, deviceName ?? string.Empty, resource, level, areaEpoch);
            lock (sync)
            {
                List<Point> bag;
                if (!bags.TryGetValue(key, out bag))
                {
                    bag = BasePoints.OrderBy(_ => random.Next()).ToList();
                    bags.Add(key, bag);
                }
                int attempt = BasePoints.Length - bag.Count + 1;
                if (bag.Count == 0 || attempt > MaxResourceAreaLv2PointAttempts)
                    return new ResourceAreaLv2PointSelection { Exhausted = true, MaxAttempts = MaxResourceAreaLv2PointAttempts };
                Point basePoint = bag[0];
                bag.RemoveAt(0);
                int scaledX = Math.Max(0, Math.Min(width - 1,
                    (int)Math.Round(basePoint.X * width / 1280d)));
                int scaledY = Math.Max(0, Math.Min(height - 1,
                    (int)Math.Round(basePoint.Y * height / 720d)));
                return new ResourceAreaLv2PointSelection
                {
                    Attempt = attempt,
                    MaxAttempts = MaxResourceAreaLv2PointAttempts,
                    BasePoint = basePoint,
                    ScaledPoint = new Point(scaledX, scaledY),
                    ActualResolution = new Size(width, height),
                    RemainingPointCount = bag.Count,
                    Exhausted = attempt >= MaxResourceAreaLv2PointAttempts
                };
            }
        }
        public void Clear(string runId, string deviceName, ResourceType resource, int level, int areaEpoch)
        {
            lock (sync)
                bags.Remove(string.Join("|", runId ?? string.Empty, deviceName ?? string.Empty, resource, level, areaEpoch));
        }
    }
    public sealed class ResourceAreaLv2PointSelection { public int Attempt { get; set; } public int MaxAttempts { get; set; } public Point BasePoint { get; set; } public Point ScaledPoint { get; set; } public Size ActualResolution { get; set; } public int RemainingPointCount { get; set; } public bool Exhausted { get; set; } }
}
