using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
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
            return new SelectFarmTeamService(detector, client, registry, matcher,
                DeviceOperationLock.Shared, options,
                new SelectFarmTeamDiagnosticStore(options.FailureScreenshotDirectory), logger);
        }
    }
}
