using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Exceptions;
using ADB_Tool_Automation_Post_FB.Helpers;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.Notifications;
using ADB_Tool_Automation_Post_FB.Infrastructure.Workflows;
using ADB_Tool_Automation_Post_FB.UI;
using System;
using System.Configuration;
using System.Windows;
using System.Windows.Media;

namespace ADB_Tool_Automation_Post_FB
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
