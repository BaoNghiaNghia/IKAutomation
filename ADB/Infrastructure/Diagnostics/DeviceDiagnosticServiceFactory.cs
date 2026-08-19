using IK_Auto_ADB.Core.Diagnostics;
using IK_Auto_ADB.Infrastructure.LDPlayer;

namespace IK_Auto_ADB.Infrastructure.Diagnostics
{
    public static class DeviceDiagnosticServiceFactory
    {
        public static IDeviceDiagnosticService CreateFromAppConfig()
        {
            DeviceDiagnosticOptions options = AppConfigDiagnosticOptionsProvider.Load();
            return new DeviceDiagnosticService(
                new AutoLdPlayerClient(),
                options,
                new ScreenshotFileStore(options.ScreenshotDirectory),
                new ApplicationDiagnosticLogger());
        }
    }
}
