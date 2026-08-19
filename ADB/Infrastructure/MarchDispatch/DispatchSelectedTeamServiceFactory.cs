using IK_Auto_ADB.Core.MarchDispatch;
using IK_Auto_ADB.Infrastructure.Concurrency;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.GameDetection;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.TeamSelection;
using IK_Auto_ADB.Infrastructure.Vision;
using IK_Auto_ADB.Infrastructure.StorageLimit;

namespace IK_Auto_ADB.Infrastructure.MarchDispatch
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
            var storageOptions = AppConfigStorageLimitDialogOptionsProvider.Load();
            var storageHandler = new StorageLimitDialogService(client, detector, registry,
                matcher, storageOptions, logger);
            return new DispatchSelectedTeamService(detector, client, registry, matcher,
                new FrameStabilityDetector(options.TeamRegionChangeThreshold),
                DeviceOperationLock.Shared, AppConfigFarmTeamSelectionOptionsProvider.Load(), options,
                new DispatchMarchDiagnosticStore(options.FailureScreenshotDirectory), logger,
                storageHandler, new TeamMarchTimerDetector(options));
        }
    }
}
