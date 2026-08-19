using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
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
