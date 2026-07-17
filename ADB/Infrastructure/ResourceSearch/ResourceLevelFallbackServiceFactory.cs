using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.Navigation;
using ADB_Tool_Automation_Post_FB.Infrastructure.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public static class ResourceLevelFallbackServiceFactory
    {
        public static IResourceLevelFallbackService CreateFromAppConfig()
        {
            var client = new AutoLdPlayerClient(); var registry = new TemplateRegistry();
            var matcher = new KAutoImageMatcher(); var logger = new ApplicationDiagnosticLogger();
            var lockService = DeviceOperationLock.Shared;
            var detectionOptions = AppConfigGameDetectionOptionsProvider.Load();
            var detector = new GameStateDetector(client, registry, matcher, detectionOptions,
                new UnknownScreenshotStore(detectionOptions.UnknownScreenshotDirectory), logger);
            var navigation = new WorldMapNavigationService(client, detector,
                AppConfigWorldMapNavigationOptionsProvider.Load(), logger, lockService);
            var configurationOptions = AppConfigResourceSearchConfigurationOptionsProvider.Load();
            var configuration = new ResourceSearchConfigurationService(navigation, detector,
                client, registry, matcher, configurationOptions, lockService, logger);
            var executionOptions = AppConfigResourceSearchExecutionOptionsProvider.Load();
            var popupOptions = AppConfigResourcePopupVerificationOptionsProvider.Load();
            var popup = new ResourcePopupVerificationService(detector, client, registry, matcher,
                popupOptions, new ResourcePopupDiagnosticStore(popupOptions.FailureScreenshotDirectory), logger);
            var execution = new ResourceSearchExecutionService(configuration, detector, client,
                registry, matcher, new FrameStabilityDetector(executionOptions.CameraStableThreshold),
                lockService, executionOptions, new ResourceSearchDiagnosticStore(
                    executionOptions.ResultScreenshotDirectory, executionOptions.ObservationBurstDirectory),
                logger, popup);
            ResourceLevelFallbackOptions fallbackOptions = AppConfigResourceLevelFallbackOptionsProvider.Load();
            return new ResourceLevelFallbackService(configuration, execution, detector, client,
                registry, matcher, lockService, configurationOptions, fallbackOptions,
                new ResourceLevelFallbackDiagnosticStore(fallbackOptions.ScreenshotDirectory), logger);
        }
    }
}
