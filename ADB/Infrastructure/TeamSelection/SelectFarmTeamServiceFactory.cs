using IK_Auto_ADB.Core.TeamSelection;
using IK_Auto_ADB.Infrastructure.Concurrency;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.GameDetection;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.Vision;

namespace IK_Auto_ADB.Infrastructure.TeamSelection
{
    public static class SelectFarmTeamServiceFactory
    {
        public static ISelectFarmTeamService CreateFromAppConfig()
        {
            var client = new AutoLdPlayerClient();
            var registry = new TemplateRegistry();
            var matcher = new KAutoImageMatcher();
            var logger = new ApplicationDiagnosticLogger();
            var detectionOptions = AppConfigGameDetectionOptionsProvider.Load();
            var detector = new GameStateDetector(client, registry, matcher,
                detectionOptions,
                new UnknownScreenshotStore(detectionOptions.UnknownScreenshotDirectory),
                logger);
            var options = AppConfigFarmTeamSelectionOptionsProvider.Load();
            ISelectedTeamDetector selectedTeamDetector = new SelectedTeamDetector(
                client, registry, matcher);
            return new SelectFarmTeamService(detector, client, registry, matcher,
                DeviceOperationLock.Shared, options,
                new SelectFarmTeamDiagnosticStore(options.FailureScreenshotDirectory), logger,
                selectedTeamDetector);
        }
    }
}
