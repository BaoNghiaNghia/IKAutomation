using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.Navigation;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public static class ResourceSearchExecutionServiceFactory
    {
        public static IResourceSearchExecutionService CreateFromAppConfig()
        {
            var client = new AutoLdPlayerClient();
            var registry = new TemplateRegistry();
            var matcher = new KAutoImageMatcher();
            var logger = new ApplicationDiagnosticLogger();
            var detectionOptions = AppConfigGameDetectionOptionsProvider.Load();
            var detector = new GameStateDetector(client, registry, matcher, detectionOptions,
                new UnknownScreenshotStore(detectionOptions.UnknownScreenshotDirectory), logger);
            var operationLock = DeviceOperationLock.Shared;
            var navigation = new WorldMapNavigationService(client, detector,
                AppConfigWorldMapNavigationOptionsProvider.Load(), logger, operationLock);
            var configuration = new ResourceSearchConfigurationService(navigation, detector,
                client, registry, matcher, AppConfigResourceSearchConfigurationOptionsProvider.Load(),
                operationLock, logger);
            ResourceSearchExecutionOptions options = AppConfigResourceSearchExecutionOptionsProvider.Load();
            return new ResourceSearchExecutionService(configuration, detector, client, registry,
                matcher, new FrameStabilityDetector(options.CameraStableThreshold), operationLock,
                options, new ResourceSearchDiagnosticStore(options.ResultScreenshotDirectory,
                    options.ObservationBurstDirectory), logger);
        }
    }
}
