using IK_Auto_ADB.Core.Workflows;
using IK_Auto_ADB.Exceptions;
using IK_Auto_ADB.Helpers;
using IK_Auto_ADB.Infrastructure.Concurrency;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.Notifications;
using IK_Auto_ADB.Infrastructure.Workflows;
using IK_Auto_ADB.UI;
using System;
using System.Configuration;
using System.Windows;
using System.Windows.Media;

namespace IK_Auto_ADB
{
    public partial class MainWindow : Window
    {
        private DeviceDiagnosticWindow farmControlWindow;
        private static readonly Brush ClosedBrush = new SolidColorBrush(Color.FromRgb(126, 87, 194));
        private static readonly Brush OpenBrush = new SolidColorBrush(Color.FromRgb(30, 27, 75));

        public MainWindow()
        {
            InitializeComponent();
            ConfigureLdConsole();
            TaskExceptions.RegisterGlobalHandlers();
            ContentRendered += (sender, args) =>
            {
                Left = SystemParameters.WorkArea.Right - ActualWidth;
                Top = SystemParameters.WorkArea.Bottom - ActualHeight;
            };
        }

        private static void ConfigureLdConsole()
        {
            try
            {
                string path = AutoLdPlayerClient.ConfigureLdConsolePath(
                    ConfigurationManager.AppSettings["LDCONSOLE_PATH"]);
                Logger.LogInfo("LDPlayer console configured: " + path);
            }
            catch (Exception exception)
            {
                Logger.LogWarning("LDPlayer console configuration failed: " + exception.Message);
            }
        }

        private void Button_Click_ShowLog(object sender, RoutedEventArgs e) => UIEventHelper.ShowLogFile();

        private void Button_Click_DeviceDiagnostic(object sender, RoutedEventArgs e)
        {
            if (farmControlWindow != null)
            {
                if (farmControlWindow.WindowState == WindowState.Minimized)
                    farmControlWindow.WindowState = WindowState.Normal;
                farmControlWindow.Show();
                farmControlWindow.Activate();
                farmControlWindow.Focus();
                return;
            }
            DeviceAutomationOwnershipService.Shared.SetLogger(new ApplicationDiagnosticLogger());
            var adaptive = AppConfigAdaptiveConcurrencyOptionsProvider.LoadConfiguration();
            var gate = new AdaptiveConcurrencyGate(adaptive.Options, null, Logger.LogInfo);
            var runner = new MultiDeviceOneShotFarmRunner(() => OneShotFarmWorkflowFactory.CreateFromAppConfig(),
                () => OneShotFarmWorkflowFactory.CreateTeamAvailabilityFromAppConfig(), adaptive.Options.MaximumConcurrency,
                gate, Logger.LogInfo, AppConfigMultiDeviceOneShotFarmRunnerOptionsProvider.Load(), null, null,
                new PreflightConcurrencyGate(AppConfigPreflightConcurrencyOptionsProvider.Load()));
            farmControlWindow = new DeviceDiagnosticWindow(DeviceDiagnosticServiceFactory.CreateFromAppConfig(),
                new LdPlayerLaunchConfigurationService(), runner,
                new ContinuousFarmSupervisor(runner, LdPlayerDeviceRecoveryServiceFactory.CreateFromAppConfig(),
                    AppConfigContinuousFarmSupervisorOptionsProvider.Load(), AppConfigOperationalMaintenanceOptionsProvider.Create(),
                    new LocalAppDataContinuousFarmCheckpointStore(new LocalAppDataContinuousFarmCheckpointPathProvider(), new ApplicationDiagnosticLogger()), gate,
                    TelegramFailureNotifierFactory.CreateFromEnvironment()), AppConfigOneShotFarmWorkflowOptionsProvider.LoadRequest(),
                AppConfigReadyTeamGateOptionsProvider.Load(), new LocalAppDataFarmUiPreferencesStore(new LocalAppDataFarmUiPreferencesPathProvider(),
                    new ApplicationDiagnosticLogger()), TelegramFailureNotifierFactory.CreateFromEnvironment()) { Owner = this };
            farmControlWindow.Closed += (closedSender, closedArgs) => { farmControlWindow = null; SetFarmButton(false); };
            farmControlWindow.FarmRunStateChanged += (stateSender, stateArgs) =>
                SetFarmButton(farmControlWindow != null && farmControlWindow.IsFarmRunning);
            SetFarmButton(false);
            farmControlWindow.Show();
        }

        private void SetFarmButton(bool running)
        {
            DeviceDiagnosticButton.Tag = running ? "Running" : null;
            DeviceDiagnosticButton.Background = running ? OpenBrush : ClosedBrush;
            DeviceDiagnosticButton.BorderBrush = DeviceDiagnosticButton.Background;
            DeviceDiagnosticButtonLabel.Text = running ? "Farm đang chạy" : "Điều khiển Farm";
        }
    }
}
