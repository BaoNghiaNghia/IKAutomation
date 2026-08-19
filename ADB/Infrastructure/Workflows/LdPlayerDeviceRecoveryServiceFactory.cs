using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public static class LdPlayerDeviceRecoveryServiceFactory
    {
        public static IDeviceRecoveryService CreateFromAppConfig()
        {
            string packageName = AppConfigDiagnosticOptionsProvider.Load().PackageName;
            return new LdPlayerDeviceRecoveryService(new AutoLdPlayerClient(),
                new LdPlayerDeviceRecoveryOptions(packageName));
        }
    }
}
