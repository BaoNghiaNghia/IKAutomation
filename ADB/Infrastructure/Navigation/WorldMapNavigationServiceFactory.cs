using IK_Auto_ADB.Core.Navigation;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.GameDetection;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.Concurrency;

namespace IK_Auto_ADB.Infrastructure.Navigation
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
