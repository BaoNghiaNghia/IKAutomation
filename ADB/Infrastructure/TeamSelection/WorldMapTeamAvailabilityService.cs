using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
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
            const int firstRowOffset = 8;
            const int rowHeight = 52;
            var badgeMatches = new Dictionary<TeamNumber, ImageMatchResult>();
            var readyMatchesByTeam = new Dictionary<TeamNumber, ImageMatchResult>();
            int verifiedFrameCount = 0;
            GameDetectionResult lastState = null;
            for (int frame = 0; frame < ObservationFrameCount; frame++)
            {
                if (frame > 0)
                    await Task.Delay(ObservationIntervalMs, cancellationToken);

                byte[] screenshot = await client.CaptureScreenshotPngAsync(
                    deviceName, cancellationToken);
                lastState = detector.Detect(screenshot);
                if (lastState == null || !lastState.IsSuccessful
                    || lastState.State != GameState.WorldMap)
                    continue;

                verifiedFrameCount++;
                for (int index = 0; index < teams.Length; index++)
                {
                    TeamNumber team = teams[index];
                    ImageRegion rowRegion = RosterRowRegion(
                        index, firstRowOffset, rowHeight);
                    ImageMatchResult badgeMatch = matcher.Find(
                        screenshot, registry.LoadBytes(BadgeTemplate(team)),
                        rowRegion) ?? ImageMatchResult.NotFound();
                    if (badgeMatch.Found && badgeMatch.Width > 0
                        && badgeMatch.Height > 0)
                        badgeMatches[team] = badgeMatch;
                }

                // Merge two close observations. A moving map unit can cover one
                // "Sẵn sàng" label for a single frame; a positive match is latched
                // for this check, while no input is sent between observations.
                for (int index = 0; index < teams.Length; index++)
                {
                    TeamNumber team = teams[index];
                    ImageRegion rowRegion = RosterRowRegion(
                        index, firstRowOffset, rowHeight);
                    ImageMatchResult rowMatch = matcher.Find(
                        screenshot, readyTemplate, rowRegion)
                        ?? ImageMatchResult.NotFound();
                    if (rowMatch.Found && rowMatch.Width > 0
                        && rowMatch.Height > 0)
                        readyMatchesByTeam[team] = rowMatch;
                }
            }

            if (verifiedFrameCount == 0)
                return Failed("Fresh screenshots were not verified as WorldMap; "
                    + "readiness was not inferred.", lastState?.ErrorMessage,
                    lastState?.State ?? GameState.Unknown);

            int detectedTeamCount = badgeMatches.Keys
                .Concat(readyMatchesByTeam.Keys)
                .Select(team => (int)team)
                .DefaultIfEmpty(0)
                .Max();
            if (detectedTeamCount == 0)
            {
                return Failed("No team rows could be verified in the WorldMap roster; "
                    + "team availability was not inferred.", null, GameState.WorldMap);
            }

            // Team rows are contiguous from Team1. If Team3 is visible or ready,
            // Team1 and Team2 also exist even when their badge/ready state differs.
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
                + $"{options.TeamRosterRegion.Width},{options.TeamRosterRegion.Height}), Cancellation=false");
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

        private ImageRegion RosterRowRegion(int index, int firstRowOffset, int rowHeight)
        {
            int rowTop = options.TeamRosterRegion.Y + firstRowOffset
                + (index * rowHeight);
            int rowBottom = Math.Min(options.TeamRosterRegion.Y
                + options.TeamRosterRegion.Height, rowTop + rowHeight);
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
