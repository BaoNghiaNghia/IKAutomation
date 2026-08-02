using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    public sealed class SelectedTeamDetector : ISelectedTeamDetector
    {
        private const double TemplateWeight = .35d;
        private const double BorderWeight = .45d;
        private const double ContrastWeight = .20d;
        private const double BorderOnlyWeight = .80d;
        private const double MinimumCoherentContinuity = .55d;
        private const double MinimumBorderContrast = .35d;
        private const int RequiredBorderEdges = 2;
        private const int EdgeSearchBand = 6;

        private readonly ILdPlayerClient client;
        private readonly ITemplateRegistry registry;
        private readonly IImageMatcher matcher;

        public SelectedTeamDetector(ILdPlayerClient client, ITemplateRegistry registry,
            IImageMatcher matcher)
        {
            this.client = client;
            this.registry = registry;
            this.matcher = matcher;
        }

        public SelectedTeamFrameResult DetectFrame(byte[] frame,
            SelectedTeamDetectionContext context)
        {
            if (frame == null || context == null || context.TeamRegions == null)
                return Fail("MissingFrameOrGeometry");

            using (var stream = new MemoryStream(frame, false))
            using (var bitmap = new Bitmap(stream))
            {
                IReadOnlyDictionary<TeamNumber, ImageRegion> rows = ResolveRows(frame,
                    context, bitmap.Width, bitmap.Height);
                if (rows.Count == 0 || rows.Any(item => !Valid(item.Value, bitmap.Width, bitmap.Height))
                    || Overlaps(rows))
                    return Fail("InvalidRowGeometry");

                var details = new Dictionary<TeamNumber, SelectedTeamRowScore>();
                foreach (KeyValuePair<TeamNumber, ImageRegion> item in rows)
                {
                    ImageMatchResult match = matcher.Find(frame,
                        registry.LoadBytes(TemplateId.TeamSelectedBorderAnchor), item.Value);
                    double template = match != null && match.Found
                        ? Clamp(match.Confidence ?? .75d) : 0d;
                    SelectedTeamRowScore score = ScoreRow(bitmap, item.Key, item.Value, template);
                    details[item.Key] = score;
                }

                SelectedTeamRowScore[] ranked = details.Values
                    .OrderByDescending(item => item.EffectiveScore).ToArray();
                SelectedTeamRowScore winner = ranked[0];
                double runner = ranked.Length > 1 ? ranked[1].EffectiveScore : 0d;
                double margin = winner.EffectiveScore - runner;
                bool multipleStrong = ranked.Count(item => item.CandidateQualified
                    && item.EffectiveScore >= context.MinimumScore) > 1;
                bool confident = !multipleStrong && winner.CandidateQualified
                    && winner.EffectiveScore >= context.MinimumScore
                    && margin >= context.WinningMargin;
                string failure = confident ? null
                    : multipleStrong ? "ConflictingStrongRows"
                    : !winner.GeometryValid ? "InvalidGeometry"
                    : winner.BorderEdgesFound < RequiredBorderEdges ? "InsufficientBorderEdges"
                    : winner.BorderContinuityScore < MinimumCoherentContinuity
                        ? "BorderContinuityTooLow"
                    : winner.ContrastScore < MinimumBorderContrast ? "ContrastTooLow"
                    : winner.EffectiveScore < context.MinimumScore ? "ScoreBelowThreshold"
                    : margin < context.WinningMargin ? "WinningMarginTooLow"
                    : "InsufficientSelectionEvidence";
                return new SelectedTeamFrameResult
                {
                    Team = confident ? winner.Team : (TeamNumber?)null,
                    IsConfident = confident,
                    IsAmbiguous = multipleStrong,
                    WinningScore = winner.EffectiveScore,
                    RunnerUpScore = runner,
                    WinningMargin = margin,
                    Rows = rows,
                    RowScores = details.ToDictionary(item => item.Key,
                        item => item.Value.EffectiveScore),
                    RowDetails = details,
                    FailureReason = failure
                };
            }
        }

        public async Task<SelectedTeamConsensusResult> DetectAsync(string device,
            SelectedTeamDetectionContext context, CancellationToken token)
        {
            var frames = new List<SelectedTeamFrameResult>();
            for (int index = 0; index < context.ConsensusFrames; index++)
            {
                token.ThrowIfCancellationRequested();
                frames.Add(DetectFrame(await client.CaptureScreenshotPngAsync(device, token),
                    context));
                if (index + 1 < context.ConsensusFrames)
                    await Task.Delay(context.FrameIntervalMs, token);
            }
            SelectedTeamFrameResult[] strong = frames.Where(item => item.IsConfident).ToArray();
            var groups = strong.GroupBy(item => item.Team).ToArray();
            if (groups.Length != 1 || groups[0].Count() < context.RequiredMatchingFrames)
            {
                SelectedTeamFrameResult best = frames.OrderByDescending(item => item.WinningScore)
                    .FirstOrDefault();
                return new SelectedTeamConsensusResult
                {
                    FramesObserved = frames.Count,
                    MatchingFrames = groups.Length == 1 ? groups[0].Count() : 0,
                    FailureReason = groups.Length > 1 ? "ConflictingFrames" : "InsufficientConsensus",
                    WinningScore = best?.WinningScore ?? 0d,
                    RunnerUpScore = best?.RunnerUpScore ?? 0d,
                    WinningMargin = best?.WinningMargin ?? 0d,
                    Rows = best?.Rows,
                    RowScores = best?.RowScores,
                    RowDetails = best?.RowDetails
                };
            }
            SelectedTeamFrameResult winner = groups[0].First();
            return new SelectedTeamConsensusResult
            {
                Team = winner.Team,
                IsConfident = true,
                WinningScore = winner.WinningScore,
                RunnerUpScore = winner.RunnerUpScore,
                WinningMargin = winner.WinningMargin,
                Rows = winner.Rows,
                RowScores = winner.RowScores,
                RowDetails = winner.RowDetails,
                FramesObserved = frames.Count,
                MatchingFrames = groups[0].Count()
            };
        }

        private static SelectedTeamRowScore ScoreRow(Bitmap bitmap, TeamNumber team,
            ImageRegion row, double template)
        {
            int strip = Math.Max(2, Math.Min(5, row.Width / 20));
            EdgeSample left = BestEdge(bitmap, row, strip, Edge.Left);
            EdgeSample right = BestEdge(bitmap, row, strip, Edge.Right);
            EdgeSample top = BestEdge(bitmap, row, strip, Edge.Top);
            EdgeSample bottom = BestEdge(bitmap, row, strip, Edge.Bottom);
            EdgeSample[] edges = { left, right, top, bottom };
            EdgeSample[] strong = edges.OrderByDescending(item => item.Score).ToArray();
            int edgeCount = edges.Count(item => item.Score >= .60d);
            bool horizontal = top.Score >= .60d || bottom.Score >= .60d;
            bool vertical = left.Score >= .60d || right.Score >= .60d;
            double border = edgeCount >= RequiredBorderEdges
                ? (strong[0].Score + strong[1].Score) / 2d : 0d;
            double continuity = strong.Take(Math.Min(RequiredBorderEdges, strong.Length))
                .Average(item => item.Continuity);
            double contrast = strong.Take(Math.Min(RequiredBorderEdges, strong.Length))
                .Average(item => item.Contrast);
            double templatePath = Clamp(TemplateWeight * template + BorderWeight * border
                + ContrastWeight * contrast);
            double borderPath = Clamp(BorderOnlyWeight * border + ContrastWeight * contrast);
            bool borderOnlyQualified = edgeCount >= RequiredBorderEdges && horizontal && vertical
                && continuity >= MinimumCoherentContinuity && contrast >= MinimumBorderContrast;
            double effective = Math.Max(templatePath, borderPath);
            return new SelectedTeamRowScore
            {
                Team = team,
                RowBounds = row,
                TemplateConfidence = template,
                LeftBorderScore = left.Score,
                RightBorderScore = right.Score,
                TopBorderScore = top.Score,
                BottomBorderScore = bottom.Score,
                BorderEdgesFound = edgeCount,
                BorderEvidenceScore = border,
                BorderContinuityScore = continuity,
                ContrastScore = contrast,
                TemplatePathScore = templatePath,
                BorderPathScore = borderPath,
                EffectiveScore = effective,
                CombinedScore = effective,
                BorderOnlyQualified = borderOnlyQualified,
                CandidateQualified = borderOnlyQualified,
                TopBorderOffset = top.Offset,
                BottomBorderOffset = bottom.Offset,
                LeftBorderOffset = left.Offset,
                RightBorderOffset = right.Offset,
                GeometryValid = true,
                FailureReason = borderOnlyQualified ? null : "InsufficientBorderEvidence"
            };
        }

        private static EdgeSample BestEdge(Bitmap bitmap, ImageRegion row, int strip, Edge edge)
        {
            EdgeSample best = new EdgeSample();
            for (int offset = -EdgeSearchBand; offset <= EdgeSearchBand; offset++)
            {
                ImageRegion border;
                ImageRegion inside;
                if (!TryEdgeRegions(row, strip, edge, offset, bitmap.Width, bitmap.Height,
                    out border, out inside)) continue;
                double bright = BrightRatio(bitmap, border);
                double continuity = Continuity(bitmap, border, edge == Edge.Top || edge == Edge.Bottom);
                // The selected outline is only one or two pixels wide.  Averaging an
                // entire strip dilutes it into the dark row background; compare the
                // brightest outline pixel with the interior instead.
                double contrast = Clamp((PeakLuminance(bitmap, border)
                    - Mean(bitmap, inside)) / .35d);
                double score = Clamp(.45d * contrast + .30d * bright + .25d * continuity);
                if (score > best.Score)
                    best = new EdgeSample { Score = score, Contrast = contrast,
                        Continuity = continuity, Offset = offset };
            }
            return best;
        }

        private static bool TryEdgeRegions(ImageRegion row, int strip, Edge edge, int offset,
            int width, int height, out ImageRegion border, out ImageRegion inside)
        {
            int x = row.X;
            int y = row.Y;
            int w = row.Width;
            int h = row.Height;
            switch (edge)
            {
                case Edge.Left: x += offset; w = strip; break;
                case Edge.Right: x += row.Width - strip + offset; w = strip; break;
                case Edge.Top: y += offset; h = strip; break;
                default: y += row.Height - strip + offset; h = strip; break;
            }
            if (!TryClamp(x, y, w, h, width, height, out border))
            {
                inside = default(ImageRegion);
                return false;
            }
            int dx = edge == Edge.Left ? strip : edge == Edge.Right ? -strip : 0;
            int dy = edge == Edge.Top ? strip : edge == Edge.Bottom ? -strip : 0;
            return TryClamp(border.X + dx, border.Y + dy, border.Width, border.Height,
                width, height, out inside);
        }

        private static bool TryClamp(int x, int y, int width, int height, int maxWidth,
            int maxHeight, out ImageRegion region)
        {
            int left = Math.Max(0, x);
            int top = Math.Max(0, y);
            int right = Math.Min(maxWidth, x + width);
            int bottom = Math.Min(maxHeight, y + height);
            if (right <= left || bottom <= top)
            {
                region = default(ImageRegion);
                return false;
            }
            region = new ImageRegion(left, top, right - left, bottom - top);
            return true;
        }

        private IReadOnlyDictionary<TeamNumber, ImageRegion> ResolveRows(byte[] frame,
            SelectedTeamDetectionContext context, int width, int height)
        {
            var rows = context.TeamRegions.ToDictionary(item => item.Key, item => item.Value);
            if (!context.ResolveRowsFromFreshBadges
                || !context.TeamBadgeSearchRegion.HasValue)
                return rows;

            IReadOnlyDictionary<TeamNumber, ImageRegion> freshRows =
                ResolveFreshBadgeRows(frame, context.TeamBadgeSearchRegion.Value, width, height);
            if (freshRows.Count > 0)
                return freshRows;
            return rows;
        }

        private IReadOnlyDictionary<TeamNumber, ImageRegion> ResolveFreshBadgeRows(
            byte[] frame, ImageRegion roster, int width, int height)
        {
            TeamNumber[] teams = { TeamNumber.Team1, TeamNumber.Team2,
                TeamNumber.Team3, TeamNumber.Team4 };
            var requests = teams.Where(team => registry.Exists(BadgeId(team)))
                .Select(team => new KeyValuePair<TeamNumber, ImageMatchRequest>(team,
                    new ImageMatchRequest(registry.LoadBytes(BadgeId(team)), roster))).ToArray();
            if (requests.Length == 0) return new Dictionary<TeamNumber, ImageRegion>();

            IReadOnlyList<ImageMatchResult> matches = matcher is IBatchImageMatcher batch
                ? batch.FindMany(frame, requests.Select(item => item.Value).ToArray())
                : requests.Select(item => matcher.Find(frame, item.Value.TemplatePng,
                    item.Value.SearchRegion)).ToArray();
            var badges = new List<KeyValuePair<TeamNumber, ImageMatchResult>>();
            for (int index = 0; index < requests.Length; index++)
                if (HasBounds(matches[index])) badges.Add(new KeyValuePair<TeamNumber,
                    ImageMatchResult>(requests[index].Key, matches[index]));
            if (badges.Count == 0) return new Dictionary<TeamNumber, ImageRegion>();

            int left = Math.Max(0, roster.X);
            int right = Math.Min(width, roster.X + roster.Width);
            int bottomLimit = Math.Min(height, roster.Y + roster.Height);
            var ordered = badges.OrderBy(item => item.Value.Y).ToArray();
            var rows = new Dictionary<TeamNumber, ImageRegion>();
            for (int index = 0; index < ordered.Length; index++)
            {
                ImageMatchResult badge = ordered[index].Value;
                int top = Math.Max(roster.Y, badge.Y - 4);
                int bottom = index + 1 < ordered.Length
                    ? Math.Min(bottomLimit, ordered[index + 1].Value.Y - 4)
                    : bottomLimit;
                if (right > left && bottom > top)
                    rows[ordered[index].Key] = new ImageRegion(left, top, right - left,
                        bottom - top);
            }
            return rows;
        }

        private static bool HasBounds(ImageMatchResult match) => match != null && match.Found
            && match.Width > 0 && match.Height > 0;

        private static TemplateId BadgeId(TeamNumber team)
        {
            switch (team)
            {
                case TeamNumber.Team1: return TemplateId.Team1Badge;
                case TeamNumber.Team2: return TemplateId.Team2Badge;
                case TeamNumber.Team3: return TemplateId.Team3Badge;
                case TeamNumber.Team4: return TemplateId.Team4Badge;
                default: throw new ArgumentOutOfRangeException(nameof(team));
            }
        }

        private static bool Valid(ImageRegion row, int width, int height) => row.X >= 0
            && row.Y >= 0 && row.Width > 0 && row.Height > 0
            && row.X + row.Width <= width && row.Y + row.Height <= height;
        private static bool Overlaps(IReadOnlyDictionary<TeamNumber, ImageRegion> rows) =>
            rows.Any(a => rows.Any(b => a.Key != b.Key && a.Value.X < b.Value.X + b.Value.Width
                && b.Value.X < a.Value.X + a.Value.Width && a.Value.Y < b.Value.Y + b.Value.Height
                && b.Value.Y < a.Value.Y + a.Value.Height));
        private static SelectedTeamFrameResult Fail(string reason) =>
            new SelectedTeamFrameResult { FailureReason = reason };
        private static double Mean(Bitmap bitmap, ImageRegion region)
        {
            double sum = 0d;
            int count = 0;
            for (int y = region.Y; y < region.Y + region.Height; y++)
                for (int x = region.X; x < region.X + region.Width; x++)
                { sum += Luminance(bitmap.GetPixel(x, y)); count++; }
            return count == 0 ? 0d : sum / count;
        }
        private static double BrightRatio(Bitmap bitmap, ImageRegion region)
        {
            int bright = 0;
            int count = region.Width * region.Height;
            for (int y = region.Y; y < region.Y + region.Height; y++)
                for (int x = region.X; x < region.X + region.Width; x++)
                    if (Luminance(bitmap.GetPixel(x, y)) >= .75d) bright++;
            return count == 0 ? 0d : bright / (double)count;
        }
        private static double Continuity(Bitmap bitmap, ImageRegion region, bool horizontal)
        {
            int samples = horizontal ? region.Width : region.Height;
            int longest = 0;
            int run = 0;
            for (int index = 0; index < samples; index++)
            {
                double value = horizontal
                    ? PeakLuminance(bitmap, new ImageRegion(region.X + index, region.Y, 1,
                        region.Height))
                    : PeakLuminance(bitmap, new ImageRegion(region.X, region.Y + index,
                        region.Width, 1));
                if (value >= .75d) { run++; longest = Math.Max(longest, run); }
                else run = 0;
            }
            return samples == 0 ? 0d : longest / (double)samples;
        }
        private static double PeakLuminance(Bitmap bitmap, ImageRegion region)
        {
            double peak = 0d;
            for (int y = region.Y; y < region.Y + region.Height; y++)
                for (int x = region.X; x < region.X + region.Width; x++)
                    peak = Math.Max(peak, Luminance(bitmap.GetPixel(x, y)));
            return peak;
        }
        private static double Luminance(Color color) =>
            (.2126d * color.R + .7152d * color.G + .0722d * color.B) / 255d;
        private static double Clamp(double value) => Math.Max(0d, Math.Min(1d, value));
        private enum Edge { Left, Right, Top, Bottom }
        private sealed class EdgeSample
        {
            public double Score { get; set; }
            public double Contrast { get; set; }
            public double Continuity { get; set; }
            public int Offset { get; set; }
        }
    }
}
