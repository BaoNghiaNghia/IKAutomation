using IK_Auto_ADB.Core.GameDetection;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.Vision;

namespace IK_Auto_ADB.Infrastructure.GameDetection
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
