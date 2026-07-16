using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.MarchDispatch
{
    public static class DispatchSelectedTeamServiceFactory
    {
        public static IDispatchSelectedTeamService CreateFromAppConfig()
        {
            var client = new AutoLdPlayerClient();
            var registry = new TemplateRegistry();
            var matcher = new KAutoImageMatcher();
            var logger = new ApplicationDiagnosticLogger();
            var detectionOptions = AppConfigGameDetectionOptionsProvider.Load();
            var detector = new GameStateDetector(client, registry, matcher, detectionOptions,
                new UnknownScreenshotStore(detectionOptions.UnknownScreenshotDirectory), logger);
            var options = AppConfigDispatchSelectedTeamOptionsProvider.Load();
            return new DispatchSelectedTeamService(detector, client, registry, matcher,
                new FrameStabilityDetector(options.TeamRegionChangeThreshold),
                DeviceOperationLock.Shared, AppConfigFarmTeamSelectionOptionsProvider.Load(), options,
                new DispatchMarchDiagnosticStore(options.FailureScreenshotDirectory), logger);
        }
    }
}
