using IK_Auto_ADB.Core.ResourceSearch;
using IK_Auto_ADB.Infrastructure.Concurrency;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.GameDetection;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.Navigation;
using IK_Auto_ADB.Infrastructure.ResourcePopup;
using IK_Auto_ADB.Infrastructure.Vision;

namespace IK_Auto_ADB.Infrastructure.ResourceSearch
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
