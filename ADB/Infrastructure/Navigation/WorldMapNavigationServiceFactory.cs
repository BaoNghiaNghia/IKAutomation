using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Navigation
{
    public static class WorldMapNavigationServiceFactory
    {
        public static IWorldMapNavigationService CreateFromAppConfig()
        {
            return new WorldMapNavigationService(new AutoLdPlayerClient(),
                GameStateDetectorFactory.CreateFromAppConfig(),
                AppConfigWorldMapNavigationOptionsProvider.Load(), new ApplicationDiagnosticLogger(),
                DeviceOperationLock.Shared);
        }
    }
}
