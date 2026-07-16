using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourcePopup
{
    public static class ResourcePopupVerificationServiceFactory
    {
        public static IResourcePopupVerificationService CreateFromAppConfig()
        {
            var client = new AutoLdPlayerClient();
            var registry = new TemplateRegistry();
            var matcher = new KAutoImageMatcher();
            var logger = new ApplicationDiagnosticLogger();
            var detectionOptions = AppConfigGameDetectionOptionsProvider.Load();
            var detector = new GameStateDetector(client, registry, matcher, detectionOptions,
                new UnknownScreenshotStore(detectionOptions.UnknownScreenshotDirectory), logger);
            ResourcePopupVerificationOptions options = AppConfigResourcePopupVerificationOptionsProvider.Load();
            return new ResourcePopupVerificationService(detector, client, registry, matcher, options,
                new ResourcePopupDiagnosticStore(options.FailureScreenshotDirectory), logger);
        }
    }
}
