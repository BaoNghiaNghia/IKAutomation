using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    public sealed class WorldMapTeamAvailabilityService : IWorldMapTeamAvailabilityService
    {
        private const int ObservationFrameCount = 2;
        private const int ObservationIntervalMs = 120;
        private readonly IWorldMapNavigationService navigation;
        private readonly IGameStateDetector detector;
        private readonly ILdPlayerClient client;
        private readonly ITemplateRegistry registry;
        private readonly IImageMatcher matcher;
        private readonly IDeviceOperationLock operationLock;
        private readonly WorldMapTeamAvailabilityOptions options;
        private readonly IDiagnosticLogger logger;
        private readonly ConcurrentDictionary<string, int> knownRosterCounts =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public WorldMapTeamAvailabilityService(IWorldMapNavigationService navigation,
            IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher,
            IDeviceOperationLock operationLock, WorldMapTeamAvailabilityOptions options,
            IDiagnosticLogger logger)
        {
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<WorldMapTeamAvailabilityResult> CheckAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("LDPlayer device name is required.", nameof(deviceName));

            TemplateId[] requiredTemplates =
            {
                TemplateId.WorldMapTeamReadyAnchor,
                TemplateId.Team1Badge,
                TemplateId.Team2Badge,
                TemplateId.Team3Badge,
                TemplateId.Team4Badge
            };
            TemplateId? missingTemplate = requiredTemplates
                .Where(template => !registry.Exists(template))
                .Select(template => (TemplateId?)template)
                .FirstOrDefault();
            if (missingTemplate.HasValue)
            {
                string path = registry.GetPath(missingTemplate.Value);
                return Failed($"Required template '{missingTemplate.Value}' was not found at '{path}'.");
            }

            NavigationResult navigationResult = await navigation.EnsureWorldMapAsync(
                deviceName.Trim(), cancellationToken);
            if (!navigationResult.Success || navigationResult.FinalState != GameState.WorldMap)
            {
                return Failed("WorldMap could not be verified before checking team availability.",
                    navigationResult.ErrorMessage ?? navigationResult.Message,
                    navigationResult.FinalState);
            }

            return await operationLock.RunAsync(deviceName.Trim(),
                token => CheckCoreAsync(deviceName.Trim(), token), cancellationToken);
        }

        public void ClearKnownRoster(string deviceName = null)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                knownRosterCounts.Clear();
                return;
            }
            knownRosterCounts.TryRemove(deviceName.Trim(), out int ignored);
        }

        private async Task<WorldMapTeamAvailabilityResult> CheckCoreAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] readyTemplate = registry.LoadBytes(TemplateId.WorldMapTeamReadyAnchor);
            var availableTeams = new List<TeamNumber>();
            var readyTeams = new List<TeamNumber>();
            var readyMatches = new List<ImageMatchResult>();
            TeamNumber[] teams =
            {
                TeamNumber.Team1, TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4
            };
            int rowHeight = options.TeamRowHeight;
            var badgeMatches = new Dictionary<TeamNumber, ImageMatchResult>();
            var readyMatchesByTeam = new Dictionary<TeamNumber, ImageMatchResult>();
            int verifiedFrameCount = 0;
            GameDetectionResult lastState = null;
            for (int frame = 0; frame < ObservationFrameCount; frame++)
            {
                if (frame > 0)
                    await Task.Delay(ObservationIntervalMs, cancellationToken);

                using (CapturedFrame screenshot = await CaptureFrameAsync(deviceName, cancellationToken))
                {
                lastState = Detect(screenshot, deviceName,
                    new GameStateDetectionContext(GameState.WorldMap, GameState.WorldMap));
                if (lastState == null || !lastState.IsSuccessful
                    || lastState.State != GameState.WorldMap)
                    continue;

                verifiedFrameCount++;
                int rosterTop = options.TeamRosterRegion.Y;
                var badgeRequests = teams.Select((team, index) => new ImageMatchRequest(
                    registry.LoadBytes(BadgeTemplate(team)), RosterRowRegion(
                        rosterTop + (index * rowHeight), rowHeight))).ToArray();
                IReadOnlyList<ImageMatchResult> badgeResults = FindMany(screenshot, badgeRequests);
                var badgesInFrame = new Dictionary<TeamNumber, ImageMatchResult>();
                for (int index = 0; index < teams.Length; index++)
                {
                    TeamNumber team = teams[index];
                    ImageMatchResult badgeMatch = badgeResults[index] ?? ImageMatchResult.NotFound();
                    if (badgeMatch.Found && badgeMatch.Width > 0
                        && badgeMatch.Height > 0)
                    {
                        badgeMatches[team] = badgeMatch;
                        badgesInFrame[team] = badgeMatch;
                    }
                }

                // Merge two close observations. A moving map unit can cover one
                // "Sẵn sàng" label for a single frame; a positive match is latched
                // for this check, while no input is sent between observations.
                var readyRequests = new List<ImageMatchRequest>(teams.Length);
                var readyRequestTeams = new List<TeamNumber>(teams.Length);
                for (int index = 0; index < teams.Length; index++)
                {
                    TeamNumber team = teams[index];
                    ImageRegion rowRegion = RosterRowRegion(
                        rosterTop + (index * rowHeight), rowHeight);
                    readyRequests.Add(new ImageMatchRequest(readyTemplate, rowRegion));
                    readyRequestTeams.Add(team);
                }
                IReadOnlyList<ImageMatchResult> readyResults = readyRequests.Count == 0
                    ? new ImageMatchResult[0] : FindMany(screenshot, readyRequests);
                for (int index = 0; index < readyResults.Count; index++)
                {
                    TeamNumber team = readyRequestTeams[index];
                    ImageMatchResult rowMatch = readyResults[index] ?? ImageMatchResult.NotFound();
                    if (rowMatch.Found && rowMatch.Width > 0
                        && rowMatch.Height > 0)
                        readyMatchesByTeam[team] = rowMatch;
                }
                }
            }

            if (verifiedFrameCount == 0)
                return Failed("Fresh screenshots were not verified as WorldMap; "
                    + "readiness was not inferred.", lastState?.ErrorMessage,
                    lastState?.State ?? GameState.Unknown);

            int freshTeamCount = badgeMatches.Keys
                .Concat(readyMatchesByTeam.Keys)
                .Select(team => (int)team)
                .DefaultIfEmpty(0)
                .Max();
            int previousKnownCount;
            knownRosterCounts.TryGetValue(deviceName, out previousKnownCount);
            if (freshTeamCount > 0)
            {
                knownRosterCounts.AddOrUpdate(deviceName, freshTeamCount,
                    (_, known) => Math.Max(known, freshTeamCount));
            }
            int knownTeamCount;
            knownRosterCounts.TryGetValue(deviceName, out knownTeamCount);
            int detectedTeamCount = Math.Max(freshTeamCount, knownTeamCount);
            if (detectedTeamCount == 0)
            {
                // Every account has at least Team1. On a one-team account the
                // only row contains a timer while it is gathering, so neither
                // a ready label nor a stable numbered badge may match. A fresh,
                // verified WorldMap frame is therefore sufficient to classify
                // this as a valid one-team roster with no ready team. Returning
                // a technical failure here would bypass the bounded readiness
                // wait and incorrectly stop continuous farming.
                detectedTeamCount = 1;
            }

            // Team rows are contiguous from Team1. The highest freshly verified
            // badge or ready row establishes the roster size, including accounts
            // with fewer than four teams and frames where one badge is obscured.
            foreach (TeamNumber team in teams.Take(detectedTeamCount))
            {
                availableTeams.Add(team);
                if (readyMatchesByTeam.TryGetValue(team, out ImageMatchResult readyMatch))
                {
                    readyTeams.Add(team);
                    readyMatches.Add(readyMatch);
                }
            }

            ImageMatchResult match = readyMatches.FirstOrDefault()
                ?? ImageMatchResult.NotFound();
            bool ready = readyTeams.Count > 0;
            logger.Info($"[WorldMap Team Availability] DeviceName='{deviceName}', "
                + $"Ready={ready}, ReadyTeams='{string.Join(",", readyTeams)}', "
                + $"AvailableTeams='{string.Join(",", availableTeams)}', "
                + $"Bounds=({match.X},{match.Y},{match.Width},{match.Height}), "
                + $"Region=({options.TeamRosterRegion.X},{options.TeamRosterRegion.Y},"
                + $"{options.TeamRosterRegion.Width},{options.TeamRosterRegion.Height}), "
                + $"RowHeight={rowHeight}, FreshRosterCount={freshTeamCount}, "
                + $"PreviousKnownRosterCount={previousKnownCount}, Cancellation=false");
            return new WorldMapTeamAvailabilityResult
            {
                Success = true,
                AnyReadyTeam = ready,
                AvailableTeams = availableTeams.AsReadOnly(),
                ReadyTeams = readyTeams.AsReadOnly(),
                FinalState = GameState.WorldMap,
                ReadyMatch = match,
                ReadyMatches = readyMatches.AsReadOnly(),
                Message = ready
                    ? $"Detected {availableTeams.Count} team(s); ready teams: "
                        + $"{string.Join(", ", readyTeams)}."
                    : $"Detected {availableTeams.Count} team(s); no team is ready."
            };
        }

        private async Task<CapturedFrame> CaptureFrameAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            var frameClient = client as IFrameCapturingLdPlayerClient;
            if (frameClient != null)
                return await frameClient.CaptureFrameAsync(deviceName, cancellationToken);

            byte[] png = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            using (var stream = new MemoryStream(png, writable: false))
            using (var source = new Bitmap(stream))
                return new CapturedFrame(new Bitmap(source), DateTimeOffset.UtcNow);
        }

        private GameDetectionResult Detect(CapturedFrame frame, string deviceName,
            GameStateDetectionContext context)
        {
            var frameDetector = detector as IFrameGameStateDetector;
            return frameDetector != null
                ? frameDetector.Detect(frame, deviceName, context)
                : detector.Detect(frame.GetPngBytes());
        }

        private IReadOnlyList<ImageMatchResult> FindMany(CapturedFrame frame,
            IReadOnlyList<ImageMatchRequest> requests)
        {
            var frameMatcher = matcher as IFrameImageMatcher;
            if (frameMatcher != null) return frameMatcher.FindMany(frame, requests);
            var batchMatcher = matcher as IBatchImageMatcher;
            if (batchMatcher != null) return batchMatcher.FindMany(frame.GetPngBytes(), requests);
            return requests.Select(request => matcher.Find(frame.GetPngBytes(),
                request.TemplatePng, request.SearchRegion)).ToArray();
        }

        private int EstimateRosterTop(
            IReadOnlyDictionary<TeamNumber, ImageMatchResult> badges,
            int rowHeight)
        {
            int badgeTopPadding = options.BadgeTopPadding;
            if (badges == null || badges.Count == 0)
                return options.TeamRosterRegion.Y + badgeTopPadding;

            int[] candidates = badges
                .Select(item => item.Value.Y - badgeTopPadding
                    - (((int)item.Key - 1) * rowHeight))
                .OrderBy(value => value)
                .ToArray();
            int estimated = candidates[candidates.Length / 2];
            int maximum = options.TeamRosterRegion.Y
                + options.TeamRosterRegion.Height - rowHeight;
            return Math.Max(options.TeamRosterRegion.Y,
                Math.Min(maximum, estimated));
        }

        private ImageRegion RosterRowRegion(int requestedTop, int rowHeight)
        {
            int rowTop = Math.Max(options.TeamRosterRegion.Y, requestedTop);
            int rowBottom = Math.Min(options.TeamRosterRegion.Y
                + options.TeamRosterRegion.Height, rowTop + rowHeight);
            if (rowBottom - rowTop < rowHeight)
                rowTop = Math.Max(options.TeamRosterRegion.Y, rowBottom - rowHeight);
            return new ImageRegion(options.TeamRosterRegion.X, rowTop,
                options.TeamRosterRegion.Width, Math.Max(1, rowBottom - rowTop));
        }

        private static TemplateId BadgeTemplate(TeamNumber team)
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

        private static WorldMapTeamAvailabilityResult Failed(string message,
            string error = null, GameState state = GameState.Unknown) =>
            new WorldMapTeamAvailabilityResult
            {
                Success = false,
                AnyReadyTeam = false,
                AvailableTeams = new TeamNumber[0],
                ReadyTeams = new TeamNumber[0],
                FinalState = state,
                Message = message,
                ErrorMessage = error ?? message,
                ReadyMatch = ImageMatchResult.NotFound(),
                ReadyMatches = new ImageMatchResult[0]
            };
    }
}
