using IK_Auto_ADB.Core.ResourceSearch;
using IK_Auto_ADB.Infrastructure.Concurrency;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.GameDetection;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.Navigation;
using IK_Auto_ADB.Infrastructure.Vision;

namespace IK_Auto_ADB.Infrastructure.ResourceSearch
{
    public static class ResourceSearchConfigurationServiceFactory
    {
        public static IResourceSearchConfigurationService CreateFromAppConfig()
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
            return new ResourceSearchConfigurationService(navigation, detector, client,
                registry, matcher, AppConfigResourceSearchConfigurationOptionsProvider.Load(),
                operationLock, logger);
        }
    }
}
