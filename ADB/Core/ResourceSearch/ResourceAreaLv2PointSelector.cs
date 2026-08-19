using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public sealed class ResourceAreaLv2PointSelector
    {
        private static readonly Point[] CityLevel7Points =
        {
            new Point(650,954),new Point(644,926),new Point(642,899),new Point(658,877),
            new Point(672,865),new Point(682,881),new Point(688,907),new Point(698,925),
            new Point(705,946),new Point(678,947),new Point(674,914),new Point(709,883),
            new Point(728,908),new Point(718,862),new Point(745,867),new Point(748,889)
        };

        private static readonly Point[] CityLevel8Points =
        {
            new Point(380,837),new Point(374,852),new Point(374,872),new Point(391,887),
            new Point(402,904),new Point(423,911),new Point(444,896),new Point(474,880),
            new Point(474,851),new Point(453,841),new Point(425,833),new Point(402,841),
            new Point(402,857),new Point(425,862),new Point(448,867),new Point(464,867),
            new Point(588,811),new Point(676,836),new Point(568,853),new Point(558,872),
            new Point(568,889),new Point(580,904),new Point(589,923),new Point(605,939),
            new Point(606,893),new Point(612,861),new Point(599,829),new Point(594,857),
            new Point(592,879),new Point(616,810),new Point(647,817),new Point(639,786),
            new Point(659,765),new Point(685,751),new Point(689,768),new Point(693,789),
            new Point(705,812),new Point(718,834),new Point(690,834),new Point(670,812),
            new Point(677,778),new Point(715,762),new Point(737,761),new Point(734,781),
            new Point(737,808),new Point(757,827),new Point(771,846),new Point(797,843),
            new Point(814,823),new Point(804,788),new Point(793,761),new Point(767,758),
            new Point(757,775),new Point(778,794),new Point(783,816)
        };

        private static readonly Point[] CityLevel9Points =
        {
            new Point(524,747),new Point(513,763),new Point(502,776),new Point(492,786),
            new Point(489,808),new Point(481,819),new Point(496,836),new Point(503,848),
            new Point(518,868),new Point(536,850),new Point(549,817),new Point(562,793),
            new Point(575,777),new Point(543,776),new Point(530,789),new Point(520,809)
        };

        private static readonly Point[] CityLevel10Points =
        {
            new Point(397,615),new Point(388,628),new Point(377,635),new Point(365,643),
            new Point(354,659),new Point(335,672),new Point(330,682),new Point(313,706),
            new Point(323,718),new Point(328,727),new Point(335,736),new Point(352,748),
            new Point(370,741),new Point(388,716),new Point(408,693),new Point(419,671),
            new Point(392,681),new Point(365,686),new Point(363,714),new Point(381,662),
            new Point(583,681),new Point(564,694),new Point(544,707),new Point(535,722),
            new Point(545,734),new Point(554,743),new Point(575,752),new Point(595,762),
            new Point(612,774),new Point(631,752),new Point(654,731),new Point(637,710),
            new Point(605,703),new Point(577,701),new Point(591,716),new Point(615,733)
        };

        private static readonly int[] ResourceLevel6CityLevels = { 7, 8 };
        private static readonly int[] ResourceLevel7CityLevels = { 7, 8, 9, 10 };
        private static readonly int[] ResourceLevel8CityLevels = { 8, 9, 10 };
        private static readonly int[] NoCityLevels = new int[0];
        private static readonly Point[] NoPoints = new Point[0];

        private static readonly Point[] ResourceLevel6Points =
            CityLevel7Points.Concat(CityLevel8Points).Distinct().ToArray();
        private static readonly Point[] ResourceLevel7Points =
            CityLevel7Points.Concat(CityLevel8Points).Concat(CityLevel9Points)
                .Concat(CityLevel10Points).Distinct().ToArray();
        private static readonly Point[] ResourceLevel8Points =
            CityLevel8Points.Concat(CityLevel9Points).Concat(CityLevel10Points)
                .Distinct().ToArray();
        private static readonly Point[] AllPoints = ResourceLevel7Points;

        private static readonly IReadOnlyList<Point> CityLevel7PointView = Array.AsReadOnly(CityLevel7Points);
        private static readonly IReadOnlyList<Point> CityLevel8PointView = Array.AsReadOnly(CityLevel8Points);
        private static readonly IReadOnlyList<Point> CityLevel9PointView = Array.AsReadOnly(CityLevel9Points);
        private static readonly IReadOnlyList<Point> CityLevel10PointView = Array.AsReadOnly(CityLevel10Points);
        private static readonly IReadOnlyList<Point> ResourceLevel6PointView = Array.AsReadOnly(ResourceLevel6Points);
        private static readonly IReadOnlyList<Point> ResourceLevel7PointView = Array.AsReadOnly(ResourceLevel7Points);
        private static readonly IReadOnlyList<Point> ResourceLevel8PointView = Array.AsReadOnly(ResourceLevel8Points);
        private static readonly IReadOnlyList<Point> AllPointView = Array.AsReadOnly(AllPoints);
        private static readonly IReadOnlyList<Point> NoPointView = Array.AsReadOnly(NoPoints);
        private static readonly IReadOnlyList<int> ResourceLevel6CityLevelView = Array.AsReadOnly(ResourceLevel6CityLevels);
        private static readonly IReadOnlyList<int> ResourceLevel7CityLevelView = Array.AsReadOnly(ResourceLevel7CityLevels);
        private static readonly IReadOnlyList<int> ResourceLevel8CityLevelView = Array.AsReadOnly(ResourceLevel8CityLevels);
        private static readonly IReadOnlyList<int> NoCityLevelView = Array.AsReadOnly(NoCityLevels);

        private readonly Dictionary<string, List<Point>> bags = new Dictionary<string, List<Point>>();
        private readonly Random random;
        private readonly object sync = new object();

        public const int MaxResourceAreaLv2PointAttempts = 3;

        public ResourceAreaLv2PointSelector(Random random = null)
        {
            this.random = random ?? new Random();
        }

        public static IReadOnlyList<Point> AllMapPoints => AllPointView;

        public static IReadOnlyList<Point> GetPointsForCityLevel(int cityLevel)
        {
            switch (cityLevel)
            {
                case 7: return CityLevel7PointView;
                case 8: return CityLevel8PointView;
                case 9: return CityLevel9PointView;
                case 10: return CityLevel10PointView;
                default: return NoPointView;
            }
        }

        public static IReadOnlyList<int> GetCityLevelsForResourceLevel(int resourceLevel)
        {
            switch (resourceLevel)
            {
                case 6: return ResourceLevel6CityLevelView;
                case 7: return ResourceLevel7CityLevelView;
                case 8: return ResourceLevel8CityLevelView;
                default: return NoCityLevelView;
            }
        }

        public static IReadOnlyList<Point> GetPointsForResourceLevel(int resourceLevel)
        {
            switch (resourceLevel)
            {
                case 6: return ResourceLevel6PointView;
                case 7: return ResourceLevel7PointView;
                case 8: return ResourceLevel8PointView;
                default: return NoPointView;
            }
        }

        public ResourceAreaLv2PointSelection Next(string runId, string deviceName,
            ResourceType resource, int level, int areaEpoch, int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException();

            IReadOnlyList<Point> eligiblePoints = GetPointsForResourceLevel(level);
            string key = string.Join("|", runId ?? string.Empty, deviceName ?? string.Empty,
                resource, level, areaEpoch);

            lock (sync)
            {
                List<Point> bag;
                if (!bags.TryGetValue(key, out bag))
                {
                    bag = eligiblePoints.OrderBy(_ => random.Next()).ToList();
                    bags.Add(key, bag);
                }

                int attempt = eligiblePoints.Count - bag.Count + 1;
                if (bag.Count == 0 || attempt > MaxResourceAreaLv2PointAttempts)
                {
                    return new ResourceAreaLv2PointSelection
                    {
                        MaxAttempts = MaxResourceAreaLv2PointAttempts,
                        ActualResolution = new Size(width, height),
                        Exhausted = true
                    };
                }

                Point mapPoint = bag[0];
                bag.RemoveAt(0);
                return new ResourceAreaLv2PointSelection
                {
                    Attempt = attempt,
                    MaxAttempts = MaxResourceAreaLv2PointAttempts,
                    BasePoint = mapPoint,
                    // These are game-map X/Y values entered into the coordinate fields.
                    // They must not be screen-scaled or clamped to the screenshot size.
                    ScaledPoint = mapPoint,
                    ActualResolution = new Size(width, height),
                    RemainingPointCount = bag.Count,
                    Exhausted = attempt >= MaxResourceAreaLv2PointAttempts
                };
            }
        }

        public void Clear(string runId, string deviceName, ResourceType resource,
            int level, int areaEpoch)
        {
            lock (sync)
            {
                bags.Remove(string.Join("|", runId ?? string.Empty, deviceName ?? string.Empty,
                    resource, level, areaEpoch));
            }
        }
    }

    public sealed class ResourceAreaLv2PointSelection
    {
        public int Attempt { get; set; }
        public int MaxAttempts { get; set; }
        public Point BasePoint { get; set; }
        public Point ScaledPoint { get; set; }
        public Size ActualResolution { get; set; }
        public int RemainingPointCount { get; set; }
        public bool Exhausted { get; set; }
    }
}
