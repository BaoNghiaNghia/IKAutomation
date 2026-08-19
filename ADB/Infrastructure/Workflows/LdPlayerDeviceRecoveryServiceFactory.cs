using IK_Auto_ADB.Core.Workflows;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.LDPlayer;

namespace IK_Auto_ADB.Infrastructure.Workflows
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
