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

            if (!registry.Exists(TemplateId.WorldMapTeamReadyAnchor))
            {
                string path = registry.GetPath(TemplateId.WorldMapTeamReadyAnchor);
                return Failed($"Required template 'WorldMapTeamReadyAnchor' was not found at '{path}'.");
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
            byte[] screenshot = await client.CaptureScreenshotPngAsync(
                deviceName, cancellationToken);
            GameDetectionResult state = detector.Detect(screenshot);
            if (state == null || !state.IsSuccessful || state.State != GameState.WorldMap)
            {
                return Failed("Fresh screenshot was not verified as WorldMap; readiness was not inferred.",
                    state?.ErrorMessage, state?.State ?? GameState.Unknown);
            }

            byte[] readyTemplate = registry.LoadBytes(TemplateId.WorldMapTeamReadyAnchor);
            var readyTeams = new List<TeamNumber>();
            var readyMatches = new List<ImageMatchResult>();
            TeamNumber[] teams =
            {
                TeamNumber.Team1, TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4
            };
            int rowHeight = options.TeamRosterRegion.Height / teams.Length;
            for (int index = 0; index < teams.Length; index++)
            {
                int rowY = options.TeamRosterRegion.Y + (index * rowHeight);
                int height = index == teams.Length - 1
                    ? options.TeamRosterRegion.Y + options.TeamRosterRegion.Height - rowY
                    : rowHeight;
                var rowRegion = new ImageRegion(options.TeamRosterRegion.X, rowY,
                    options.TeamRosterRegion.Width, height);
                ImageMatchResult rowMatch = matcher.Find(screenshot, readyTemplate,
                    rowRegion) ?? ImageMatchResult.NotFound();
                if (rowMatch.Found && rowMatch.Width > 0 && rowMatch.Height > 0)
                {
                    readyTeams.Add(teams[index]);
                    readyMatches.Add(rowMatch);
                }
            }

            ImageMatchResult match = readyMatches.FirstOrDefault()
                ?? ImageMatchResult.NotFound();
            bool ready = readyTeams.Count > 0;
            logger.Info($"[WorldMap Team Availability] DeviceName='{deviceName}', "
                + $"Ready={ready}, ReadyTeams='{string.Join(",", readyTeams)}', "
                + $"Bounds=({match.X},{match.Y},{match.Width},{match.Height}), "
                + $"Region=({options.TeamRosterRegion.X},{options.TeamRosterRegion.Y},"
                + $"{options.TeamRosterRegion.Width},{options.TeamRosterRegion.Height}), Cancellation=false");
            return new WorldMapTeamAvailabilityResult
            {
                Success = true,
                AnyReadyTeam = ready,
                ReadyTeams = readyTeams.AsReadOnly(),
                FinalState = GameState.WorldMap,
                ReadyMatch = match,
                ReadyMatches = readyMatches.AsReadOnly(),
                Message = ready
                    ? $"Ready teams verified on WorldMap: {string.Join(", ", readyTeams)}."
                    : "No ready team was found in the WorldMap roster."
            };
        }

        private static WorldMapTeamAvailabilityResult Failed(string message,
            string error = null, GameState state = GameState.Unknown) =>
            new WorldMapTeamAvailabilityResult
            {
                Success = false,
                AnyReadyTeam = false,
                ReadyTeams = new TeamNumber[0],
                FinalState = state,
                Message = message,
                ErrorMessage = error ?? message,
                ReadyMatch = ImageMatchResult.NotFound(),
                ReadyMatches = new ImageMatchResult[0]
            };
    }
}
