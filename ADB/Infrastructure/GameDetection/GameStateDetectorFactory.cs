using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection
{
    public static class GameStateDetectorFactory
    {
        public static IGameStateDetector CreateFromAppConfig()
        {
            GameDetectionOptions options = AppConfigGameDetectionOptionsProvider.Load();
            return new GameStateDetector(
                new AutoLdPlayerClient(),
                new TemplateRegistry(),
                new KAutoImageMatcher(),
                options,
                new UnknownScreenshotStore(options.UnknownScreenshotDirectory),
                new ApplicationDiagnosticLogger());
        }
    }
}
