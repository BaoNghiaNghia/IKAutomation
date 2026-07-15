using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics
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
