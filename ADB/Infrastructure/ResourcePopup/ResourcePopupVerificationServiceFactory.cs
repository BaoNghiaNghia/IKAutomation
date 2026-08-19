using IK_Auto_ADB.Core.ResourcePopup;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.GameDetection;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.Vision;

namespace IK_Auto_ADB.Infrastructure.ResourcePopup
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
