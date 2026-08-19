using IK_Auto_ADB.Core.TeamSelection;
using IK_Auto_ADB.Infrastructure.Concurrency;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.GameDetection;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.ResourcePopup;
using IK_Auto_ADB.Infrastructure.Vision;

namespace IK_Auto_ADB.Infrastructure.TeamSelection
{
    public static class OpenTeamSelectionServiceFactory
    {
        public static IOpenTeamSelectionService CreateFromAppConfig()
        {
            var client = new AutoLdPlayerClient();
            var registry = new TemplateRegistry();
            var matcher = new KAutoImageMatcher();
            var logger = new ApplicationDiagnosticLogger();
            var detectionOptions = AppConfigGameDetectionOptionsProvider.Load();
            var detector = new GameStateDetector(client, registry, matcher, detectionOptions,
                new UnknownScreenshotStore(detectionOptions.UnknownScreenshotDirectory), logger);
            var popupOptions = AppConfigResourcePopupVerificationOptionsProvider.Load();
            var popupVerifier = new ResourcePopupVerificationService(detector, client, registry, matcher,
                popupOptions, new ResourcePopupDiagnosticStore(popupOptions.FailureScreenshotDirectory), logger);
            var options = AppConfigOpenTeamSelectionOptionsProvider.Load(popupOptions.PopupRegion);
            return new OpenTeamSelectionService(popupVerifier, detector, client, registry, matcher,
                DeviceOperationLock.Shared, options,
                new OpenTeamSelectionDiagnosticStore(options.FailureScreenshotDirectory), logger);
        }
    }
}
