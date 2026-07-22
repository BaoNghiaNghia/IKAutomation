using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.Notifications;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ADB_Tool_Automation_Post_FB.UI
{
    public partial class DeviceDiagnosticWindow : Window
    {
        private readonly IDeviceDiagnosticService diagnosticService;
        private readonly IGameStateDetector gameStateDetector;
        private readonly IWorldMapNavigationService navigationService;
        private readonly IResourceSearchConfigurationService resourceSearchConfigurationService;
        private readonly IResourceSearchExecutionService resourceSearchExecutionService;
        private readonly IResourcePopupVerificationService resourcePopupVerificationService;
        private readonly IOpenTeamSelectionService openTeamSelectionService;
        private readonly ISelectFarmTeamService selectFarmTeamService;
        private readonly TeamSelectionRequest defaultFarmTeamRequest;
        private readonly IDispatchSelectedTeamService dispatchSelectedTeamService;
        private readonly DispatchMarchRequest defaultDispatchRequest;
        private readonly IMultiDeviceOneShotFarmRunner multiDeviceFarmRunner;
        private readonly IContinuousFarmSupervisor continuousFarmSupervisor;
        private readonly OneShotFarmRequest defaultOneShotFarmRequest;
        private readonly IFarmUiPreferencesStore farmPreferencesStore;
        private readonly IAutomationFailureNotifier failureNotifier;
        private readonly FarmUiPreferences defaultFarmPreferences;
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private readonly DispatcherTimer oneShotFarmProgressTimer;
        private CancellationTokenSource oneShotFarmCancellation;
        private bool oneShotFarmCancellationRequested;
        private long oneShotFarmRunGeneration;
        private DateTimeOffset? oneShotFarmNextCheckAt;
        private DateTimeOffset? oneShotFarmWaitDeadline;
        private readonly ObservableCollection<DeviceSelectionItem> deviceSelections =
            new ObservableCollection<DeviceSelectionItem>();
        private readonly HashSet<string> failedDeviceNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> activeDeviceNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> deviceAttemptVersions =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private OneShotFarmRequest retryRequest;
        private int activeDeviceBatchCount;
        private long nextDeviceAttemptVersion;

        public DeviceDiagnosticWindow(
            IDeviceDiagnosticService diagnosticService,
            IGameStateDetector gameStateDetector,
            IWorldMapNavigationService navigationService,
            IResourceSearchConfigurationService resourceSearchConfigurationService,
            IResourceSearchExecutionService resourceSearchExecutionService,
            IResourcePopupVerificationService resourcePopupVerificationService,
            IOpenTeamSelectionService openTeamSelectionService,
            ISelectFarmTeamService selectFarmTeamService,
            TeamSelectionRequest defaultFarmTeamRequest,
            IDispatchSelectedTeamService dispatchSelectedTeamService,
            DispatchMarchRequest defaultDispatchRequest,
            IMultiDeviceOneShotFarmRunner multiDeviceFarmRunner,
            IContinuousFarmSupervisor continuousFarmSupervisor,
            OneShotFarmRequest defaultOneShotFarmRequest,
            ReadyTeamGateOptions defaultReadyTeamGateOptions,
            IFarmUiPreferencesStore farmPreferencesStore,
            IAutomationFailureNotifier failureNotifier)
        {
            this.diagnosticService = diagnosticService
                ?? throw new ArgumentNullException(nameof(diagnosticService));
            this.gameStateDetector = gameStateDetector
                ?? throw new ArgumentNullException(nameof(gameStateDetector));
            this.navigationService = navigationService
                ?? throw new ArgumentNullException(nameof(navigationService));
            this.resourceSearchConfigurationService = resourceSearchConfigurationService
                ?? throw new ArgumentNullException(nameof(resourceSearchConfigurationService));
            this.resourceSearchExecutionService = resourceSearchExecutionService
                ?? throw new ArgumentNullException(nameof(resourceSearchExecutionService));
            this.resourcePopupVerificationService = resourcePopupVerificationService
                ?? throw new ArgumentNullException(nameof(resourcePopupVerificationService));
            this.openTeamSelectionService = openTeamSelectionService
                ?? throw new ArgumentNullException(nameof(openTeamSelectionService));
            this.selectFarmTeamService = selectFarmTeamService
                ?? throw new ArgumentNullException(nameof(selectFarmTeamService));
            this.defaultFarmTeamRequest = defaultFarmTeamRequest
                ?? throw new ArgumentNullException(nameof(defaultFarmTeamRequest));
            this.dispatchSelectedTeamService = dispatchSelectedTeamService
                ?? throw new ArgumentNullException(nameof(dispatchSelectedTeamService));
            this.defaultDispatchRequest = defaultDispatchRequest
                ?? throw new ArgumentNullException(nameof(defaultDispatchRequest));
            this.multiDeviceFarmRunner = multiDeviceFarmRunner
                ?? throw new ArgumentNullException(nameof(multiDeviceFarmRunner));
            this.continuousFarmSupervisor = continuousFarmSupervisor
                ?? throw new ArgumentNullException(nameof(continuousFarmSupervisor));
            this.defaultOneShotFarmRequest = defaultOneShotFarmRequest
                ?? throw new ArgumentNullException(nameof(defaultOneShotFarmRequest));
            this.farmPreferencesStore = farmPreferencesStore
                ?? throw new ArgumentNullException(nameof(farmPreferencesStore));
            this.failureNotifier = failureNotifier
                ?? throw new ArgumentNullException(nameof(failureNotifier));
            defaultFarmPreferences = FarmUiPreferencesMapper.FromDefaults(
                defaultOneShotFarmRequest, defaultReadyTeamGateOptions
                    ?? throw new ArgumentNullException(nameof(defaultReadyTeamGateOptions)));

            InitializeComponent();
            DeviceSelectionListBox.ItemsSource = deviceSelections;
            oneShotFarmProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            oneShotFarmProgressTimer.Tick += OneShotFarmProgressTimer_Tick;
            ApplyFarmPreferences(defaultFarmPreferences);
            PackageNameTextBox.Text = string.IsNullOrWhiteSpace(diagnosticService.Configuration.PackageName)
                ? "(not configured)"
                : diagnosticService.Configuration.PackageName;
            Loaded += async (sender, args) => await LoadFarmPreferencesAndRefreshAsync();
            Closed += (sender, args) =>
            {
                oneShotFarmCancellation?.Cancel();
                oneShotFarmProgressTimer.Stop();
                lifetimeCancellation.Cancel();
            };
        }

        private async Task LoadFarmPreferencesAndRefreshAsync()
        {
            string warning = null;
            try
            {
                FarmUiPreferencesLoadResult load = await farmPreferencesStore.LoadAsync(
                    defaultFarmPreferences, lifetimeCancellation.Token);
                ApplyFarmPreferences(load.Preferences ?? defaultFarmPreferences);
                if (load.RecoveredInvalidFile || !string.IsNullOrWhiteSpace(load.ErrorMessage))
                    warning = "Cấu hình farm bị lỗi; đã khôi phục mặc định.";
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                ApplyFarmPreferences(defaultFarmPreferences);
                warning = "Không thể tải cấu hình farm; đang dùng mặc định. " + exception.Message;
            }
            await RefreshDeviceListAsync();
            string notificationStatus = failureNotifier.IsConfigured
                ? "Telegram notifications: configured."
                : "Telegram notifications: not configured. Set the bot token and chat ID environment variables, then restart Visual Studio and IKAutomation.";
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(warning)
                ? notificationStatus
                : warning + Environment.NewLine + notificationStatus;
        }

        private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDeviceListAsync();
        }

        private async Task RefreshDeviceListAsync()
        {
            string currentDevice = DeviceNameComboBox.Text?.Trim();
            await RunOperationAsync(async cancellationToken =>
            {
                IReadOnlyList<string> deviceNames = await diagnosticService.GetDeviceNamesAsync(cancellationToken);
                DeviceNameComboBox.ItemsSource = deviceNames;

                var previous = deviceSelections.ToDictionary(item => item.DeviceName,
                    item => item, StringComparer.OrdinalIgnoreCase);
                bool initialLoad = previous.Count == 0;
                deviceSelections.Clear();
                foreach (string deviceName in deviceNames)
                {
                    DeviceSelectionItem existing;
                    bool found = previous.TryGetValue(deviceName, out existing);
                    deviceSelections.Add(new DeviceSelectionItem(deviceName)
                    {
                        IsSelected = found ? existing.IsSelected : initialLoad,
                        Status = found ? existing.Status : "Ready"
                    });
                }

                string selectedDevice = deviceNames.FirstOrDefault(name =>
                    string.Equals(name, currentDevice, StringComparison.OrdinalIgnoreCase))
                    ?? deviceNames.FirstOrDefault();
                DeviceNameComboBox.Text = selectedDevice ?? currentDevice ?? string.Empty;

                return deviceNames.Count == 0
                    ? "No LDPlayer instances were found. Check LDCONSOLE_PATH and create an instance in LDPlayer."
                    : $"Found {deviceNames.Count} LDPlayer instance(s). Selected: {DeviceNameComboBox.Text}.";
            });
        }

        private void SelectAllDevices_Click(object sender, RoutedEventArgs e)
        {
            foreach (DeviceSelectionItem item in deviceSelections) item.IsSelected = true;
        }

        private void ClearDeviceSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (DeviceSelectionItem item in deviceSelections) item.IsSelected = false;
        }

        private async void CheckDevice_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                DeviceDiagnosticResult result = await diagnosticService.CheckDeviceAsync(
                    GetSelectedDeviceName(),
                    cancellationToken);
                return FormatCheckResult(result);
            });
        }

        private async void LaunchGame_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                await diagnosticService.LaunchGameAsync(GetSelectedDeviceName(), cancellationToken);
                return "Game launch command sent. Use Capture Screenshot when the desired screen is visible.";
            });
        }

        private async void CaptureScreenshot_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                ScreenshotCaptureResult result = await diagnosticService.CaptureScreenshotAsync(
                    GetSelectedDeviceName(),
                    StateNameComboBox.Text,
                    NoteTextBox.Text,
                    cancellationToken);
                return $"Screenshot saved: {result.ScreenshotPath}{Environment.NewLine}"
                    + $"Metadata: {result.MetadataPath}{Environment.NewLine}"
                    + $"Resolution: {result.Width}x{result.Height}";
            });
        }

        private async void DetectCurrentState_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                GameDetectionResult result = await gameStateDetector.DetectAsync(
                    GetSelectedDeviceName(),
                    cancellationToken);
                return FormatDetectionResult(result);
            });
        }

        private async void EnsureWorldMap_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatNavigationResult(
                await navigationService.EnsureWorldMapAsync(GetSelectedDeviceName(), cancellationToken)));
        }

        private async void OpenSearchPanel_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatNavigationResult(
                await navigationService.OpenResourceSearchPanelAsync(GetSelectedDeviceName(), cancellationToken)));
        }

        private async void ConfigureSearch_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatConfigurationResult(
                await resourceSearchConfigurationService.ConfigureAsync(
                    GetSelectedDeviceName(),
                    new ResourceSearchConfigurationRequest
                    {
                        ResourceType = ResourceType.Iron,
                        TargetLevel = 7,
                        UnoccupiedOnly = true
                    },
                    cancellationToken)));
        }

        private async void ExecuteSearch_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatExecutionResult(
                await resourceSearchExecutionService.ExecuteAsync(
                    GetSelectedDeviceName(),
                    new ResourceSearchExecutionRequest
                    {
                        ConfigureBeforeSearch = true,
                        Configuration = new ResourceSearchConfigurationRequest
                        {
                            ResourceType = ResourceType.Iron,
                            TargetLevel = 7,
                            UnoccupiedOnly = true
                        }
                    },
                    cancellationToken)));
        }

        private async void VerifyResourcePopup_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatPopupResult(
                await resourcePopupVerificationService.VerifyAsync(
                    GetSelectedDeviceName(), cancellationToken)));
        }

        private async void OpenTeamSelection_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatOpenTeamSelectionResult(
                await openTeamSelectionService.OpenAsync(
                    GetSelectedDeviceName(), cancellationToken)));
        }

        private async void SelectFarmTeam_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatSelectFarmTeamResult(
                await selectFarmTeamService.SelectAsync(GetSelectedDeviceName(),
                    defaultFarmTeamRequest, cancellationToken)));
        }

        private async void DispatchSelectedTeam_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatDispatchMarchResult(
                await dispatchSelectedTeamService.DispatchAsync(GetSelectedDeviceName(),
                    defaultDispatchRequest, cancellationToken)));
        }

        private async void RunOneShotFarm_Click(object sender, RoutedEventArgs e)
        {
            if (oneShotFarmCancellation != null) return;
            string[] selectedDevices = deviceSelections
                .Where(item => item.IsSelected)
                .Select(item => item.DeviceName)
                .ToArray();
            if (selectedDevices.Length == 0)
            {
                StatusTextBlock.Text = "Hãy chọn ít nhất một thiết bị LDPlayer để chạy.";
                return;
            }
            if (!TryReadFarmPreferences(out FarmUiPreferences preferences, out string validationError))
            {
                StatusTextBlock.Text = validationError;
                return;
            }

            FarmUiPreferencesSaveResult saveResult;
            try
            {
                saveResult = await farmPreferencesStore.SaveAsync(
                    preferences, lifetimeCancellation.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                saveResult = new FarmUiPreferencesSaveResult
                {
                    Success = false,
                    Message = "Không thể lưu cấu hình farm; vẫn dùng cấu hình hiện tại. "
                        + exception.Message
                };
            }
            OneShotFarmRequest request = FarmUiPreferencesMapper.CreateRequest(
                preferences, defaultOneShotFarmRequest);
            string saveWarning = saveResult.Success ? null : saveResult.Message;
            failedDeviceNames.Clear();
            retryRequest = request;
            RetryFailedDevicesButton.IsEnabled = false;
            await RunDeviceBatchAsync(selectedDevices, request, saveWarning, false);
        }

        private async void RetryFailedDevices_Click(object sender, RoutedEventArgs e)
        {
            if (oneShotFarmCancellationRequested) return;
            string[] devices = failedDeviceNames
                .Where(name => !activeDeviceNames.Contains(name))
                .Where(name => deviceSelections.Any(item => string.Equals(
                    item.DeviceName, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (devices.Length == 0 || retryRequest == null)
            {
                failedDeviceNames.Clear();
                retryRequest = null;
                RetryFailedDevicesButton.IsEnabled = false;
                StatusTextBlock.Text = "Không còn thiết bị lỗi để chạy lại.";
                return;
            }

            await RunDeviceBatchAsync(devices, retryRequest, null, true);
        }

        private async void RunContinuousFarm_Click(object sender, RoutedEventArgs e)
        {
            if (oneShotFarmCancellation != null) return;
            string[] selectedDevices = deviceSelections
                .Where(item => item.IsSelected)
                .Select(item => item.DeviceName)
                .ToArray();
            if (selectedDevices.Length == 0)
            {
                StatusTextBlock.Text = "Hãy chọn ít nhất một thiết bị LDPlayer để chạy.";
                return;
            }
            if (!TryReadFarmPreferences(out FarmUiPreferences preferences,
                out string validationError))
            {
                StatusTextBlock.Text = validationError;
                return;
            }

            OneShotFarmRequest request = FarmUiPreferencesMapper.CreateRequest(
                preferences, defaultOneShotFarmRequest);
            request.RunUntilNoReadyTeams = true;
            failedDeviceNames.Clear();
            retryRequest = null;
            await RunContinuousSupervisorAsync(selectedDevices, request);
        }

        private async Task RunContinuousSupervisorAsync(string[] deviceNames,
            OneShotFarmRequest request)
        {
            CancellationTokenSource runCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeCancellation.Token);
            oneShotFarmCancellation = runCancellation;
            oneShotFarmCancellationRequested = false;
            oneShotFarmRunGeneration++;
            long runGeneration = oneShotFarmRunGeneration;
            var attemptVersions = new Dictionary<string, long>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string deviceName in deviceNames)
            {
                long attemptVersion = ++nextDeviceAttemptVersion;
                attemptVersions[deviceName] = attemptVersion;
                deviceAttemptVersions[deviceName] = attemptVersion;
                activeDeviceNames.Add(deviceName);
            }
            var progress = new Progress<ContinuousFarmSupervisorProgress>(value =>
                ApplyContinuousFarmProgress(runGeneration, runCancellation,
                    attemptVersions, value));
            RunOneShotFarmButton.IsEnabled = false;
            RunContinuousFarmButton.IsEnabled = false;
            RetryFailedDevicesButton.IsEnabled = false;
            StopOneShotFarmButton.IsEnabled = true;
            OneShotFarmResourcesGroupBox.IsEnabled = false;
            StatusTextBlock.Text = $"Continuous supervisor đang quản lý {deviceNames.Length} thiết bị...";
            try
            {
                ContinuousFarmSupervisorResult result =
                    await continuousFarmSupervisor.RunAsync(deviceNames, request,
                        progress, runCancellation.Token);
                StatusTextBlock.Text = "Continuous supervisor đã dừng."
                    + Environment.NewLine + string.Join(Environment.NewLine,
                        result.Devices.Select(item =>
                            $"- {item.DeviceName}: {item.State}; cycles={item.CycleCount}; failures={item.ConsecutiveFailures}"));
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Continuous supervisor đã dừng.";
            }
            catch (Exception exception)
            {
                StatusTextBlock.Text = "Continuous supervisor error: " + exception.Message;
                AppendNotificationStatus(await NotifyExceptionSafelyAsync(
                    string.Join(",", deviceNames), exception));
            }
            finally
            {
                foreach (string deviceName in deviceNames.Where(name =>
                    IsCurrentDeviceAttempt(name, attemptVersions)))
                    activeDeviceNames.Remove(deviceName);
                StopOneShotFarmProgressTimer();
                if (ReferenceEquals(oneShotFarmCancellation, runCancellation))
                    oneShotFarmCancellation = null;
                runCancellation.Dispose();
                RunOneShotFarmButton.IsEnabled = true;
                RunContinuousFarmButton.IsEnabled = true;
                StopOneShotFarmButton.IsEnabled = false;
                OneShotFarmResourcesGroupBox.IsEnabled = true;
                RefreshRetryButtonState();
            }
        }

        private void ApplyContinuousFarmProgress(long runGeneration,
            CancellationTokenSource runCancellation,
            IReadOnlyDictionary<string, long> attemptVersions,
            ContinuousFarmSupervisorProgress progress)
        {
            if (progress?.Device == null || !OneShotFarmProgressUtilities.IsCurrentRun(
                runGeneration, oneShotFarmRunGeneration, runCancellation,
                oneShotFarmCancellation)
                || !IsCurrentDeviceAttempt(progress.Device.DeviceName,
                    attemptVersions)) return;
            ContinuousFarmDeviceSnapshot snapshot = progress.Device;
            DeviceSelectionItem item = deviceSelections.FirstOrDefault(value =>
                string.Equals(value.DeviceName, snapshot.DeviceName,
                    StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                string retry = snapshot.NextAttemptAt.HasValue
                    ? $"; next={snapshot.NextAttemptAt.Value.ToLocalTime():HH:mm:ss}"
                    : string.Empty;
                string storage = snapshot.DiagnosticWritesSuspended
                    ? "; diagnostics suspended (low disk)" : string.Empty;
                string concurrency = snapshot.ConcurrencyLimit > 0
                    ? $"; load={snapshot.ActiveExecutions}/{snapshot.ConcurrencyLimit}"
                    : string.Empty;
                item.Status = $"{snapshot.State}: {snapshot.Message}{retry}{storage}{concurrency}";
            }
            if (snapshot.State == ContinuousFarmDeviceState.Stopped)
                activeDeviceNames.Remove(snapshot.DeviceName);
            if (snapshot.State == ContinuousFarmDeviceState.Quarantined)
                failedDeviceNames.Add(snapshot.DeviceName);
            else if (snapshot.State == ContinuousFarmDeviceState.Preflight
                || snapshot.State == ContinuousFarmDeviceState.Ready
                || snapshot.State == ContinuousFarmDeviceState.Running
                || snapshot.State == ContinuousFarmDeviceState.Waiting)
                failedDeviceNames.Remove(snapshot.DeviceName);
            if (progress.FarmProgress?.DeviceProgress != null)
            {
                ApplyOneShotFarmProgress(runGeneration, runCancellation,
                    progress.FarmProgress.DeviceProgress);
                ProgressMessageTextBlock.Text = $"[{snapshot.DeviceName}] "
                    + snapshot.Message;
            }
        }

        private async Task RunDeviceBatchAsync(string[] deviceNames,
            OneShotFarmRequest request, string saveWarning, bool isRetry)
        {
            CancellationTokenSource runCancellation = oneShotFarmCancellation;
            if (runCancellation == null)
            {
                runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeCancellation.Token);
                oneShotFarmCancellation = runCancellation;
                oneShotFarmCancellationRequested = false;
                oneShotFarmRunGeneration++;
            }
            long runGeneration = oneShotFarmRunGeneration;
            activeDeviceBatchCount++;
            var attemptVersions = new Dictionary<string, long>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string deviceName in deviceNames)
            {
                long attemptVersion = ++nextDeviceAttemptVersion;
                attemptVersions[deviceName] = attemptVersion;
                deviceAttemptVersions[deviceName] = attemptVersion;
                activeDeviceNames.Add(deviceName);
            }
            var progress = new Progress<MultiDeviceOneShotFarmProgress>(value =>
                ApplyMultiDeviceFarmProgress(runGeneration, runCancellation,
                    attemptVersions, value));
            foreach (DeviceSelectionItem item in deviceSelections.Where(item =>
                deviceNames.Contains(item.DeviceName, StringComparer.OrdinalIgnoreCase)))
                item.Status = "Queued";
            RunOneShotFarmButton.IsEnabled = false;
            RunContinuousFarmButton.IsEnabled = false;
            RetryFailedDevicesButton.IsEnabled = false;
            StopOneShotFarmButton.IsEnabled = true;
            OneShotFarmResourcesGroupBox.IsEnabled = false;
            StatusTextBlock.Text = isRetry
                ? $"Đang chạy lại {deviceNames.Length} thiết bị lỗi; các thiết bị khác không bị ảnh hưởng..."
                : $"Đang chạy {deviceNames.Length} thiết bị; tối đa 20 thiết bị đồng thời...";
            try
            {
                MultiDeviceOneShotFarmResult result = await multiDeviceFarmRunner.RunAsync(
                    deviceNames, request, progress, runCancellation.Token);
                UpdateRetryCandidates(result, request, attemptVersions);
                StatusTextBlock.Text = FormatMultiDeviceFarmResult(result)
                    + (string.IsNullOrWhiteSpace(saveWarning) ? string.Empty
                        : Environment.NewLine + "Warning: " + saveWarning);
                foreach (MultiDeviceOneShotFarmItemResult item in result.Devices
                    .Where(item => ShouldNotifyFailure(item.Result)))
                    AppendNotificationStatus(await NotifyFailureSafelyAsync(
                        item.DeviceName, item.Result));
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "One-Shot Farm canceled.";
            }
            catch (Exception exception)
            {
                foreach (string deviceName in deviceNames.Where(name =>
                    IsCurrentDeviceAttempt(name, attemptVersions)))
                    failedDeviceNames.Add(deviceName);
                retryRequest = request;
                foreach (DeviceSelectionItem item in deviceSelections.Where(item =>
                    failedDeviceNames.Contains(item.DeviceName)))
                    item.Status = "Failed: " + exception.Message;
                StatusTextBlock.Text = $"Error: {exception.Message}";
                AppendNotificationStatus(await NotifyExceptionSafelyAsync(
                    string.Join(",", deviceNames), exception));
            }
            finally
            {
                foreach (string deviceName in deviceNames.Where(name =>
                    IsCurrentDeviceAttempt(name, attemptVersions)))
                    activeDeviceNames.Remove(deviceName);
                activeDeviceBatchCount--;
                if (activeDeviceBatchCount == 0)
                {
                    StopOneShotFarmProgressTimer();
                    if (ReferenceEquals(oneShotFarmCancellation, runCancellation))
                        oneShotFarmCancellation = null;
                    runCancellation.Dispose();
                    RunOneShotFarmButton.IsEnabled = true;
                    RunContinuousFarmButton.IsEnabled = true;
                    StopOneShotFarmButton.IsEnabled = false;
                    OneShotFarmResourcesGroupBox.IsEnabled = true;
                    if (failedDeviceNames.Count == 0) retryRequest = null;
                }
                RefreshRetryButtonState();
            }
        }

        private void UpdateRetryCandidates(MultiDeviceOneShotFarmResult result,
            OneShotFarmRequest request, IReadOnlyDictionary<string, long> attemptVersions)
        {
            foreach (string deviceName in attemptVersions.Keys.Where(name =>
                IsCurrentDeviceAttempt(name, attemptVersions)))
                failedDeviceNames.Remove(deviceName);
            foreach (MultiDeviceOneShotFarmItemResult item in result?.Devices
                ?? new MultiDeviceOneShotFarmItemResult[0])
            {
                if (item.Stage == MultiDeviceOneShotFarmStage.Failed
                    && IsCurrentDeviceAttempt(item.DeviceName, attemptVersions))
                    failedDeviceNames.Add(item.DeviceName);
            }
            retryRequest = failedDeviceNames.Count > 0 || activeDeviceBatchCount > 1
                ? request
                : null;
        }

        private void RefreshRetryButtonState()
        {
            RetryFailedDevicesButton.IsEnabled = !oneShotFarmCancellationRequested
                && retryRequest != null
                && failedDeviceNames.Any(name => !activeDeviceNames.Contains(name));
        }

        private void ApplyMultiDeviceFarmProgress(long runGeneration,
            CancellationTokenSource runCancellation,
            IReadOnlyDictionary<string, long> attemptVersions,
            MultiDeviceOneShotFarmProgress progress)
        {
            if (progress == null || !OneShotFarmProgressUtilities.IsCurrentRun(
                runGeneration, oneShotFarmRunGeneration, runCancellation,
                oneShotFarmCancellation)
                || !IsCurrentDeviceAttempt(progress.DeviceName, attemptVersions)) return;
            DeviceSelectionItem item = deviceSelections.FirstOrDefault(value =>
                string.Equals(value.DeviceName, progress.DeviceName,
                    StringComparison.OrdinalIgnoreCase));
            if (item != null)
                item.Status = string.IsNullOrWhiteSpace(progress.Message)
                    ? progress.Stage.ToString()
                    : $"{progress.Stage}: {progress.Message}";
            if (progress.Stage == MultiDeviceOneShotFarmStage.Failed)
            {
                activeDeviceNames.Remove(progress.DeviceName);
                failedDeviceNames.Add(progress.DeviceName);
                RefreshRetryButtonState();
            }
            else if (progress.Stage == MultiDeviceOneShotFarmStage.Completed)
            {
                activeDeviceNames.Remove(progress.DeviceName);
                failedDeviceNames.Remove(progress.DeviceName);
                RefreshRetryButtonState();
            }
            else if (progress.Stage == MultiDeviceOneShotFarmStage.Cancelled)
            {
                activeDeviceNames.Remove(progress.DeviceName);
                RefreshRetryButtonState();
            }
            if (progress.DeviceProgress != null)
            {
                ApplyOneShotFarmProgress(runGeneration, runCancellation,
                    progress.DeviceProgress);
                ProgressMessageTextBlock.Text = $"[{progress.DeviceName}] "
                    + (progress.DeviceProgress.Message ?? progress.Message ?? "-");
            }
        }

        private bool IsCurrentDeviceAttempt(string deviceName,
            IReadOnlyDictionary<string, long> attemptVersions)
        {
            return !string.IsNullOrWhiteSpace(deviceName)
                && attemptVersions.TryGetValue(deviceName, out long attemptVersion)
                && deviceAttemptVersions.TryGetValue(deviceName, out long currentVersion)
                && attemptVersion == currentVersion;
        }

        private async void SaveFarmSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadFarmPreferences(out FarmUiPreferences preferences, out string error))
            {
                StatusTextBlock.Text = error;
                return;
            }
            try
            {
                FarmUiPreferencesSaveResult result = await farmPreferencesStore.SaveAsync(
                    preferences, lifetimeCancellation.Token);
                StatusTextBlock.Text = result.Success ? "Đã lưu cấu hình farm." : result.Message;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                StatusTextBlock.Text = "Không thể lưu cấu hình farm: " + exception.Message;
            }
        }

        private async void RestoreFarmDefaults_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await farmPreferencesStore.ResetAsync(lifetimeCancellation.Token);
                ApplyFarmPreferences(defaultFarmPreferences);
                StatusTextBlock.Text = "Đã khôi phục cấu hình mặc định.";
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                StatusTextBlock.Text = "Không thể khôi phục cấu hình mặc định: " + exception.Message;
            }
        }

        private void StopOneShotFarm_Click(object sender, RoutedEventArgs e)
        {
            CancellationTokenSource currentRun = oneShotFarmCancellation;
            if (currentRun == null || oneShotFarmCancellationRequested) return;
            oneShotFarmCancellationRequested = true;
            StopOneShotFarmButton.IsEnabled = false;
            RetryFailedDevicesButton.IsEnabled = false;
            ProgressStageTextBlock.Text = OneShotFarmProgressStage.Stopping.ToString();
            ProgressMessageTextBlock.Text = "Stopping One-Shot Farm...";
            StopOneShotFarmProgressTimer();
            currentRun.Cancel();
        }

        private void ApplyOneShotFarmProgress(long runGeneration,
            CancellationTokenSource runCancellation, OneShotFarmProgress progress)
        {
            if (progress == null || !OneShotFarmProgressUtilities.IsCurrentRun(
                runGeneration, oneShotFarmRunGeneration, runCancellation,
                oneShotFarmCancellation)) return;
            try
            {
                ProgressStageTextBlock.Text = progress.Stage.ToString();
                ProgressMessageTextBlock.Text = progress.Message ?? "-";
                ProgressChecksTextBlock.Text = progress.TeamAvailabilityChecks.ToString(CultureInfo.InvariantCulture);
                ProgressAllowedTeamsTextBlock.Text = FormatTeams(progress.AllowedTeams);
                ProgressReadyTeamsTextBlock.Text = FormatTeams(progress.ReadyTeams);
                ProgressEligibleTeamsTextBlock.Text = FormatTeams(progress.EligibleReadyTeams);
                ProgressCurrentStepTextBlock.Text = progress.CurrentStep?.ToString() ?? "-";
                ProgressCurrentContextTextBlock.Text = $"{progress.CurrentResource?.ToString() ?? "-"} / "
                    + $"{progress.CurrentLevel?.ToString(CultureInfo.InvariantCulture) ?? "-"} / "
                    + $"{progress.CurrentTeam?.ToString() ?? "-"}";
                oneShotFarmNextCheckAt = progress.NextCheckAt;
                oneShotFarmWaitDeadline = progress.WaitDeadline;
                ProgressNextCheckTextBlock.Text = progress.NextCheckAt.HasValue
                    ? progress.NextCheckAt.Value.ToLocalTime().ToString(
                        "HH:mm:ss", CultureInfo.InvariantCulture)
                    : "-";

                if (progress.Stage == OneShotFarmProgressStage.WaitingForReadyTeam)
                {
                    UpdateOneShotFarmCountdown();
                    oneShotFarmProgressTimer.Start();
                }
                else if (progress.Stage == OneShotFarmProgressStage.ReadyTeamFound
                    || progress.Stage == OneShotFarmProgressStage.Completed
                    || progress.Stage == OneShotFarmProgressStage.Failed
                    || progress.Stage == OneShotFarmProgressStage.Cancelled)
                {
                    StopOneShotFarmProgressTimer();
                }
            }
            catch (Exception)
            {
                // The dispatcher can be shutting down; progress must not fail gameplay.
            }
        }

        private static string FormatTeams(IReadOnlyList<TeamNumber> teams) =>
            teams == null || teams.Count == 0 ? "-" : string.Join(", ", teams);

        private void OneShotFarmProgressTimer_Tick(object sender, EventArgs e) =>
            UpdateOneShotFarmCountdown();

        private void UpdateOneShotFarmCountdown()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            TimeSpan next = OneShotFarmProgressUtilities.Remaining(now, oneShotFarmNextCheckAt);
            TimeSpan wait = OneShotFarmProgressUtilities.Remaining(now, oneShotFarmWaitDeadline);
            ProgressCountdownTextBlock.Text = next.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
            ProgressWaitRemainingTextBlock.Text = wait.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        private void StopOneShotFarmProgressTimer()
        {
            oneShotFarmProgressTimer.Stop();
            oneShotFarmNextCheckAt = null;
            oneShotFarmWaitDeadline = null;
            ProgressNextCheckTextBlock.Text = "-";
            ProgressCountdownTextBlock.Text = "00:00";
            ProgressWaitRemainingTextBlock.Text = "00:00:00";
        }

        private void ApplyFarmPreferences(FarmUiPreferences preferences)
        {
            IronResourceCheckBox.IsChecked = preferences.Iron;
            StoneResourceCheckBox.IsChecked = preferences.Stone;
            WoodResourceCheckBox.IsChecked = preferences.Wood;
            FoodResourceCheckBox.IsChecked = preferences.Food;
            LevelPriorityTextBox.Text = string.Join(",", preferences.LevelPriority ?? new int[0]);
            TeamPriorityTextBox.Text = string.Join(",", (preferences.TeamPriority
                ?? new TeamNumber[0]).Select(team => (int)team));
            AllowTeam1CheckBox.IsChecked = preferences.AllowTeam1;
            ReadyCheckIntervalTextBox.Text = preferences.ReadyCheckIntervalMinutes
                .ToString(CultureInfo.InvariantCulture);
            ReadyMaxWaitTextBox.Text = preferences.ReadyMaxWaitHours
                .ToString(CultureInfo.InvariantCulture);
            UnoccupiedOnlyCheckBox.IsChecked = preferences.UnoccupiedOnly;
        }

        private bool TryReadFarmPreferences(out FarmUiPreferences preferences,
            out string error)
        {
            preferences = null;
            error = null;
            if (!TryParseIntegerList(LevelPriorityTextBox.Text, out int[] levels)
                || !TryParseIntegerList(TeamPriorityTextBox.Text, out int[] teamValues)
                || !int.TryParse(ReadyCheckIntervalTextBox.Text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int intervalMinutes)
                || !int.TryParse(ReadyMaxWaitTextBox.Text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int maxWaitHours))
            {
                error = "Level, team và thời gian chờ phải là số nguyên hợp lệ.";
                return false;
            }
            preferences = new FarmUiPreferences
            {
                Iron = IronResourceCheckBox.IsChecked == true,
                Stone = StoneResourceCheckBox.IsChecked == true,
                Wood = WoodResourceCheckBox.IsChecked == true,
                Food = FoodResourceCheckBox.IsChecked == true,
                LevelPriority = levels,
                TeamPriority = teamValues.Select(value => (TeamNumber)value).ToArray(),
                AllowTeam1 = AllowTeam1CheckBox.IsChecked == true,
                ReadyCheckIntervalMinutes = intervalMinutes,
                ReadyMaxWaitHours = maxWaitHours,
                UnoccupiedOnly = UnoccupiedOnlyCheckBox.IsChecked == true
            };
            FarmUiPreferencesValidationResult validation = FarmUiPreferencesMapper.Validate(preferences);
            error = validation.IsValid ? null : validation.Message;
            return validation.IsValid;
        }

        private static bool TryParseIntegerList(string text, out int[] values)
        {
            values = new int[0];
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split(',');
            var parsed = new List<int>();
            foreach (string part in parts)
            {
                if (!int.TryParse(part.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int value)) return false;
                parsed.Add(value);
            }
            values = parsed.ToArray();
            return values.Length > 0;
        }


        private async void BackTest_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                await diagnosticService.BackAsync(GetSelectedDeviceName(), cancellationToken);
                return "Back command sent.";
            });
        }

        private async Task RunOperationAsync(Func<CancellationToken, Task<string>> operation)
        {
            IsEnabled = false;
            StatusTextBlock.Text = "Running...";
            try
            {
                StatusTextBlock.Text = await operation(lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Operation canceled.";
            }
            catch (Exception exception)
            {
                StatusTextBlock.Text = $"Error: {exception.Message}";
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private static string FormatCheckResult(DeviceDiagnosticResult result)
        {
            string packageStatus = result.PackageMatches.HasValue
                ? result.PackageMatches.Value.ToString()
                : "unavailable";
            return $"Device: {result.DeviceName}{Environment.NewLine}"
                + $"Running: {result.IsRunning}{Environment.NewLine}"
                + $"Screenshot: {result.ScreenshotSucceeded}{Environment.NewLine}"
                + $"Resolution: {result.ScreenshotWidth}x{result.ScreenshotHeight}{Environment.NewLine}"
                + $"Expected resolution matches: {result.MatchesExpectedResolution}{Environment.NewLine}"
                + $"Expected package: {result.ExpectedPackage}{Environment.NewLine}"
                + $"Current foreground package: {result.CurrentForegroundPackage ?? "unavailable"}{Environment.NewLine}"
                + $"Package matches: {packageStatus}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}";
        }

        private string GetSelectedDeviceName()
        {
            string deviceName = DeviceNameComboBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                throw new InvalidOperationException(
                    "No LDPlayer instance is selected. Start LDPlayer, click Refresh, then select a device.");
            }

            return deviceName;
        }

        private static string FormatDetectionResult(GameDetectionResult result)
        {
            string evidence = string.Join(
                Environment.NewLine,
                System.Linq.Enumerable.Select(result.Evidence, item =>
                    $"- {item.TemplateId}: exists={item.TemplateExists}, found={item.Found}, "
                    + $"confidence={(item.Confidence.HasValue ? item.Confidence.Value.ToString("F3") : "n/a")}; "
                    + item.Message));
            return $"Detected state: {result.State}{Environment.NewLine}"
                + $"Successful: {result.IsSuccessful}{Environment.NewLine}"
                + $"Resolution: {result.ScreenshotWidth}x{result.ScreenshotHeight}{Environment.NewLine}"
                + $"Unknown screenshot: {result.ScreenshotPath ?? string.Empty}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}"
                + $"Evidence:{Environment.NewLine}{evidence}";
        }

        private static string FormatNavigationResult(NavigationResult result)
        {
            string evidence = string.Join(Environment.NewLine,
                System.Linq.Enumerable.Select(result.FinalEvidence, item =>
                    $"- {item.TemplateId}: found={item.Found}; {item.Message}"));
            return $"Success: {result.Success}{Environment.NewLine}Initial: {result.InitialState}{Environment.NewLine}"
                + $"Final: {result.FinalState}{Environment.NewLine}Attempts: {result.Attempts}{Environment.NewLine}"
                + $"Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}Message: {result.Message}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Evidence:{Environment.NewLine}{evidence}";
        }

        private static string FormatConfigurationResult(ResourceSearchConfigurationResult result)
        {
            string steps = string.Join(Environment.NewLine, result.Steps.Select(step =>
                $"- {step.StepName}: success={step.Success}, attempts={step.Attempts}; "
                + $"{step.Message}; error={step.ErrorMessage ?? string.Empty}"));
            return $"Success: {result.Success}{Environment.NewLine}Initial: {result.InitialState}{Environment.NewLine}"
                + $"Final: {result.FinalState}{Environment.NewLine}Resource verified: {result.ResourceVerified}{Environment.NewLine}"
                + $"Level verified: {result.LevelVerified}{Environment.NewLine}Filter verified: {result.FilterVerified}{Environment.NewLine}"
                + $"Tap count: {result.TapCount}{Environment.NewLine}Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Message: {result.Message}{Environment.NewLine}Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}"
                + $"Steps:{Environment.NewLine}{steps}";
        }

        private static string FormatExecutionResult(ResourceSearchExecutionResult result)
        {
            string observations = string.Join(Environment.NewLine, result.Observations.Select((item, index) =>
                $"- #{index + 1} {item.State}: toast={item.ToastAnchorFound}/{item.ToastActionAnchorFound}, "
                + $"alternate={item.ShortAnchorFound}/{item.OtherRegionAnchorFound}, "
                + $"variant={item.MatchedNotFoundVariant ?? string.Empty}, "
                + $"panel={item.SearchPanelConfirmed}, diff={item.FrameDifference?.ToString("F4") ?? "n/a"}, "
                + $"stable={item.IsStable}; {item.Message}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Search taps: {result.SearchTapCount}{Environment.NewLine}Panel closed: {result.PanelClosed}{Environment.NewLine}"
                + $"Camera movement: {result.CameraMovementObserved}{Environment.NewLine}Camera stable: {result.CameraStabilityVerified}{Environment.NewLine}"
                + $"Not found observed: {result.NotFoundObserved}{Environment.NewLine}Toast verified: {result.NotFoundToastVerified}{Environment.NewLine}"
                + $"Not found variant: {result.MatchedNotFoundVariant ?? string.Empty}{Environment.NewLine}"
                + $"Observed frames: {result.ObservedFrameCount}{Environment.NewLine}Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}Message: {result.Message}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Observations:{Environment.NewLine}{observations}";
        }

        private static string FormatPopupResult(ResourcePopupVerificationResult result)
        {
            string gatherBounds = result.GatherButtonMatch != null && result.GatherButtonMatch.Found
                ? $"({result.GatherButtonMatch.X},{result.GatherButtonMatch.Y},"
                    + $"{result.GatherButtonMatch.Width},{result.GatherButtonMatch.Height})"
                : string.Empty;
            string evidence = string.Join(Environment.NewLine, result.Evidence.Select(item =>
                $"- {item.TemplateId}: found={item.Found}, bounds="
                + (item.MatchResult != null && item.MatchResult.Found
                    ? $"({item.MatchResult.X},{item.MatchResult.Y},{item.MatchResult.Width},{item.MatchResult.Height})"
                    : string.Empty)
                + $"; {item.Message}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Expected resource: {result.ExpectedResource}{Environment.NewLine}Expected popup title: {result.ExpectedPopupTitleTemplate}{Environment.NewLine}"
                + $"Popup anchor found: {result.PopupAnchorFound}{Environment.NewLine}Expected resource title found: {result.ExpectedResourceTitleFound}{Environment.NewLine}"
                + $"Gather button found: {result.GatherButtonFound}{Environment.NewLine}Header region: ({result.HeaderRegion.X},{result.HeaderRegion.Y},{result.HeaderRegion.Width},{result.HeaderRegion.Height}){Environment.NewLine}"
                + $"Action region: ({result.ActionRegion.X},{result.ActionRegion.Y},{result.ActionRegion.Width},{result.ActionRegion.Height}){Environment.NewLine}"
                + $"Gather bounds: {gatherBounds}{Environment.NewLine}Observed frames: {result.ObservedFrameCount}{Environment.NewLine}"
                + $"Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}"
                + $"Message: {result.Message}{Environment.NewLine}Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}"
                + $"Evidence:{Environment.NewLine}{evidence}";
        }

        private static string FormatOpenTeamSelectionResult(OpenTeamSelectionResult result)
        {
            string gatherBounds = result.GatherButtonMatch != null && result.GatherButtonMatch.Found
                ? $"({result.GatherButtonMatch.X},{result.GatherButtonMatch.Y},"
                    + $"{result.GatherButtonMatch.Width},{result.GatherButtonMatch.Height})"
                : string.Empty;
            string observations = string.Join(Environment.NewLine, result.Observations.Select((item, index) =>
                $"- #{index + 1} {item.State}: panel={item.PanelAnchorFound}, adjust={item.AdjustFormationButtonFound}, "
                + $"action={item.TeamActionButtonFound}, confirmed={item.TeamSelectionConfirmed}, ready={item.TeamSelectionReady}; {item.Message}"));
            string evidence = string.Join(Environment.NewLine, result.FinalEvidence.Select(item =>
                $"- {item.TemplateId}: exists={item.TemplateExists}, found={item.Found}, bounds="
                + (item.MatchResult != null && item.MatchResult.Found
                    ? $"({item.MatchResult.X},{item.MatchResult.Y},{item.MatchResult.Width},{item.MatchResult.Height})"
                    : string.Empty)
                + $", confidence={(item.Confidence.HasValue ? item.Confidence.Value.ToString("F3") : "n/a")}; {item.Message}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Resource popup verified: {result.ResourcePopupVerified}{Environment.NewLine}"
                + $"Gather button verified: {result.GatherButtonVerified}{Environment.NewLine}"
                + $"Team selection verified: {result.TeamSelectionVerified}{Environment.NewLine}Team selection ready: {result.TeamSelectionReady}{Environment.NewLine}"
                + $"Panel anchor: {result.PanelAnchorVerified}{Environment.NewLine}Adjust formation: {result.AdjustFormationButtonVerified}{Environment.NewLine}"
                + $"Team action: {result.TeamActionButtonVerified}{Environment.NewLine}Gather taps: {result.GatherTapCount}{Environment.NewLine}"
                + $"Observed frames: {result.ObservedFrameCount}{Environment.NewLine}Transient unknown frames: {result.TransientUnknownFrameCount}{Environment.NewLine}"
                + $"Gather bounds: {gatherBounds}{Environment.NewLine}Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}Message: {result.Message}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Observations:{Environment.NewLine}{observations}{Environment.NewLine}"
                + $"Final evidence:{Environment.NewLine}{evidence}";
        }

        private static string FormatSelectFarmTeamResult(SelectFarmTeamResult result)
        {
            string attempted = string.Join(", ", result.AttemptedTeams.Select(team => ((int)team).ToString()));
            string attempts = string.Join(Environment.NewLine, result.Attempts.Select((item, index) =>
                $"- #{index + 1} {item.TeamNumber}: badge={item.BadgeFound}, disabled={item.DisabledDetected}, "
                + $"already={item.AlreadySelected}, tap={item.TapSent}, selected={item.SelectedVerified}, "
                + $"badgeBounds={Bounds(item.BadgeMatch)}, selectedBounds={Bounds(item.SelectedBorderMatch)}; {item.Message}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Selected team: {result.SelectedTeam?.ToString() ?? string.Empty}{Environment.NewLine}Attempted teams: {attempted}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Team taps: {result.TeamTapCount}{Environment.NewLine}Team Selection verified: {result.TeamSelectionScreenVerified}{Environment.NewLine}"
                + $"Selected state verified: {result.SelectedStateVerified}{Environment.NewLine}Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}Message: {result.Message}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Attempts:{Environment.NewLine}{attempts}";
        }

        private static string Bounds(ADB_Tool_Automation_Post_FB.Core.Vision.ImageMatchResult match) =>
            match != null && match.Found
                ? $"({match.X},{match.Y},{match.Width},{match.Height})" : string.Empty;

        private static string FormatDispatchMarchResult(DispatchMarchResult result)
        {
            string observations = string.Join(Environment.NewLine, result.Observations.Select((item, index) =>
                $"- #{index + 1} {item.State}: panel={item.TeamSelectionFound}, world={item.WorldMapFound}, "
                + $"badge={item.ExpectedTeamBadgeFound}, selected={item.SelectedBorderFound}, "
                + $"readyBefore={item.ExpectedTeamReadyBeforeDispatch}, readyAfter={item.ExpectedTeamReadyAfterDispatch}, "
                + $"readyDisappeared={item.ReadyAnchorDisappeared}, timerContent={item.TimerContentDetected}, "
                + $"timerProgression={item.TimerProgressionDetected}, timerForeground={item.TimerForegroundRatio:F4}, "
                + $"timerDifference={item.TimerDifferenceRatio:F4}, timerRegion=({item.TimerRegion.X},{item.TimerRegion.Y},{item.TimerRegion.Width},{item.TimerRegion.Height}), "
                + $"mode={item.VerificationMode}, direct={item.DirectSuccessRuleMatched}, structural={item.StructuralSuccessRuleMatched}, "
                + $"diff={item.TeamRegionDifference?.ToString("F4") ?? "n/a"}, changed={item.TeamRegionChanged}, success={item.SuccessRuleMatched}; {item.Message}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Expected team: {result.ExpectedTeam}{Environment.NewLine}Dispatched team: {result.DispatchedTeam?.ToString() ?? string.Empty}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Team Selection verified: {result.TeamSelectionVerified}{Environment.NewLine}Expected team selected: {result.ExpectedTeamSelectedBeforeTap}{Environment.NewLine}"
                + $"Action button verified: {result.ActionButtonVerified}{Environment.NewLine}Team Selection closed: {result.TeamSelectionClosed}{Environment.NewLine}"
                + $"World Map verified: {result.WorldMapVerified}{Environment.NewLine}Selected border disappeared: {result.SelectedBorderDisappeared}{Environment.NewLine}"
                + $"Team region changed: {result.TeamRegionChanged}{Environment.NewLine}Team region difference: {result.TeamRegionDifference?.ToString("F4") ?? "n/a"}{Environment.NewLine}"
                + $"Ready before dispatch: {result.ExpectedTeamReadyBeforeDispatch}{Environment.NewLine}Ready after dispatch: {result.ExpectedTeamReadyAfterDispatch}{Environment.NewLine}Ready anchor disappeared: {result.ReadyAnchorDisappeared}{Environment.NewLine}"
                + $"Timer content before dispatch: {result.TimerContentBeforeDispatch}{Environment.NewLine}Expected team timer verified: {result.ExpectedTeamTimerVerified}{Environment.NewLine}Timer foreground ratio: {result.FinalTimerForegroundRatio:F4}{Environment.NewLine}Timer difference ratio: {result.FinalTimerDifferenceRatio:F4}{Environment.NewLine}"
                + $"Verification mode: {result.VerificationMode}{Environment.NewLine}Direct march verified: {result.DirectMarchVerified}{Environment.NewLine}Structural march verified: {result.StructuralMarchVerified}{Environment.NewLine}"
                + $"March started: {result.MarchStartedVerified}{Environment.NewLine}Action taps: {result.ActionTapCount}{Environment.NewLine}"
                + $"StorageLimitDialog detected: {result.StorageLimitDialogDetected}{Environment.NewLine}ResourceExpiryDialog detected: {result.ResourceExpiryDialogDetected}{Environment.NewLine}Resource expiry cancelled: {result.ResourceExpiryCancelled}{Environment.NewLine}Storage limit policy: {result.StorageLimitResult?.Policy.ToString() ?? string.Empty}{Environment.NewLine}Storage limit cancelled: {result.StorageLimitCancelled}{Environment.NewLine}"
                + $"Resource switch required: {result.ResourceSwitchRequired}{Environment.NewLine}Storage full resource: {result.StorageFullResource?.ToString() ?? string.Empty}{Environment.NewLine}"
                + $"State after cancel: {result.StorageLimitResult?.StateAfterCancel.ToString() ?? string.Empty}{Environment.NewLine}Returned to TeamSelection: {result.StorageLimitResult?.ReturnedToTeamSelection ?? false}{Environment.NewLine}Back sent: {result.StorageLimitResult?.BackSent ?? false}{Environment.NewLine}Back count: {result.StorageLimitResult?.BackCount ?? 0}{Environment.NewLine}Returned to WorldMap: {result.StorageLimitResult?.ReturnedToWorldMap ?? false}{Environment.NewLine}"
                + $"Observed frames: {result.ObservedFrameCount}{Environment.NewLine}Consecutive success: {result.ConsecutiveSuccessFrames}{Environment.NewLine}"
                + $"Transient Unknown: {result.TransientUnknownFrameCount}{Environment.NewLine}Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}Message: {result.Message}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Observations:{Environment.NewLine}{observations}";
        }

        private static string FormatOneShotFarmResult(OneShotFarmResult result)
        {
            ResourceFarmFallbackResult resourcePlan = result.ResourceFallbackResult;
            string resourceAttempts = resourcePlan?.Attempts == null ? string.Empty
                : string.Join(Environment.NewLine, resourcePlan.Attempts.Select(item =>
                    $"- resource={item.ResourceType}, levels={string.Join(",", item.AttemptedLevels ?? new int[0])}, "
                    + $"locatedLevel={item.LocatedLevel?.ToString() ?? string.Empty}, search={item.LevelFallbackResult?.Outcome.ToString() ?? string.Empty}, "
                    + $"storageFull={item.MarkedStorageFull}, storageConfirmed={item.StorageLimitConfirmed}, resourceExpiry={item.ResourceExpiryDetected}, recovery={item.RecoverySucceeded}, "
                    + $"popup={item.PopupResult?.Outcome.ToString() ?? string.Empty}, selectedTeam={item.SelectTeamResult?.SelectedTeam?.ToString() ?? string.Empty}, "
                    + $"dispatch={item.DispatchResult?.Outcome.ToString() ?? string.Empty}, duration={item.Duration.TotalMilliseconds:F0} ms, "
                    + $"error={item.ErrorMessage ?? string.Empty}"));
            string fallbackAttempts = result.FallbackResult?.Attempts == null ? string.Empty
                : string.Join(Environment.NewLine, result.FallbackResult.Attempts.Select(item =>
                    $"- level={item.Level}, attempt={item.AttemptNumber}, configured={item.ConfigurationSucceeded}, "
                    + $"search={item.SearchOutcome?.ToString() ?? string.Empty}, variant={item.MatchedNotFoundVariant ?? string.Empty}, "
                    + $"toastClear={item.ToastClearVerifiedBeforeAttempt}, clearFrames={item.ToastClearResult?.ConsecutiveClearFrames ?? 0}, "
                    + $"duration={item.Duration.TotalMilliseconds:F0} ms, error={item.ErrorMessage ?? string.Empty}"));
            string steps = string.Join(Environment.NewLine, result.Steps.Select((item, index) =>
                $"- #{index + 1} {item.Step}: success={item.Success}, duration={item.Duration.TotalMilliseconds:F0} ms; "
                + $"{item.Message}; error={item.ErrorMessage ?? string.Empty}; diagnostic={item.DiagnosticScreenshotPath ?? string.Empty}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Last completed step: {result.LastCompletedStep}{Environment.NewLine}Resource: {result.RequestedResource}{Environment.NewLine}"
                + $"Preferred level: {result.RequestedLevel}{Environment.NewLine}Attempted levels: {string.Join(",", result.AttemptedLevels ?? new int[0])}{Environment.NewLine}"
                + $"Located level: {result.LocatedLevel?.ToString() ?? string.Empty}{Environment.NewLine}Fallback outcome: {result.FallbackResult?.Outcome.ToString() ?? string.Empty}{Environment.NewLine}"
                + $"Attempted resources: {string.Join(",", result.AttemptedResources ?? new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0])}{Environment.NewLine}"
                + $"Selected resources: {string.Join(",", result.SelectedResources ?? new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0])}{Environment.NewLine}"
                + $"Shuffled order: {string.Join(",", result.ShuffledResourcePriority ?? new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0])}{Environment.NewLine}"
                + $"Missing templates: {string.Join(Environment.NewLine, (result.MissingRuntimeTemplates ?? new MissingRuntimeTemplate[0]).Select(item => $"resource={item.ResourceType}, TemplateId={item.TemplateId}, ExpectedPath={item.ExpectedPath}"))}{Environment.NewLine}"
                + $"Storage full resources: {string.Join(",", result.StorageFullResources ?? new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0])}{Environment.NewLine}"
                + $"Resource priority: {string.Join(",", resourcePlan?.RequestedResources ?? new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0])}{Environment.NewLine}"
                + $"Levels exhausted resources: {string.Join(",", result.LevelsExhaustedResources ?? new ADB_Tool_Automation_Post_FB.Core.ResourceSearch.ResourceType[0])}{Environment.NewLine}"
                + $"Located resource: {result.LocatedResource?.ToString() ?? string.Empty}{Environment.NewLine}Dispatched resource: {result.DispatchedResource?.ToString() ?? string.Empty}{Environment.NewLine}"
                + $"Recovery transitions: {result.ResourceFallbackResult?.RecoveryTransitions ?? 0}{Environment.NewLine}"
                + $"StorageLimitDialog detected: {result.StorageLimitDialogDetected}{Environment.NewLine}Storage limit cancelled: {result.StorageLimitCancelled}{Environment.NewLine}"
                + $"Resource switch required: {result.ResourceSwitchRequired}{Environment.NewLine}State after cancel: {result.StateAfterCancel}{Environment.NewLine}Returned to TeamSelection: {result.ReturnedToTeamSelection}{Environment.NewLine}Back sent: {result.BackSent}{Environment.NewLine}Back count: {result.BackCount}{Environment.NewLine}Returned to WorldMap: {result.ReturnedToWorldMap}{Environment.NewLine}"
                + $"Current resource: {result.CurrentResource?.ToString() ?? string.Empty}{Environment.NewLine}Next resource: {result.NextResource?.ToString() ?? string.Empty}{Environment.NewLine}"
                + $"Unoccupied only: {result.RequestedUnoccupiedOnly}{Environment.NewLine}Fallback attempts:{Environment.NewLine}{fallbackAttempts}{Environment.NewLine}"
                + $"Resource attempts:{Environment.NewLine}{resourceAttempts}{Environment.NewLine}"
                + $"Selected team: {result.SelectedTeam?.ToString() ?? string.Empty}{Environment.NewLine}Dispatched team: {result.DispatchedTeam?.ToString() ?? string.Empty}{Environment.NewLine}"
                + $"Team availability checks: {result.TeamAvailabilityChecks}{Environment.NewLine}Ready team observed: {result.ReadyTeamObserved}{Environment.NewLine}"
                + $"Detected teams: {string.Join(",", result.DetectedTeams ?? new TeamNumber[0])}{Environment.NewLine}"
                + $"Eligible ready teams: {string.Join(",", result.ReadyTeams ?? new TeamNumber[0])}{Environment.NewLine}"
                + $"Completed dispatches: {result.CompletedDispatches}{Environment.NewLine}"
                + $"Dispatched resources: {string.Join(",", result.DispatchedResources ?? new ResourceType[0])}{Environment.NewLine}"
                + $"Batch dispatched teams: {string.Join(",", result.BatchDispatchedTeams ?? new TeamNumber[0])}{Environment.NewLine}"
                + $"Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}"
                + $"Message: {result.Message}{Environment.NewLine}Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Steps:{Environment.NewLine}{steps}";
        }

        private static string FormatMultiDeviceFarmResult(MultiDeviceOneShotFarmResult result)
        {
            MultiDeviceOneShotFarmItemResult[] devices = result?.Devices?.ToArray()
                ?? new MultiDeviceOneShotFarmItemResult[0];
            int completed = devices.Count(item => item.Stage == MultiDeviceOneShotFarmStage.Completed);
            int failed = devices.Count(item => item.Stage == MultiDeviceOneShotFarmStage.Failed);
            int cancelled = devices.Count(item => item.Stage == MultiDeviceOneShotFarmStage.Cancelled);
            string details = string.Join(Environment.NewLine, devices.Select(item =>
            {
                string outcome = item.Result?.Outcome.ToString()
                    ?? (string.IsNullOrWhiteSpace(item.ErrorMessage) ? "-" : item.ErrorMessage);
                return $"- {item.DeviceName}: {item.Stage} ({outcome})";
            }));
            string concurrency = result != null && result.AdaptiveConcurrencyEnabled
                ? $"adaptive concurrency: {result.FinalConcurrencyLimit}/{result.MaximumConcurrency}"
                : $"concurrency limit: {result?.MaximumConcurrency ?? 0}";
            return $"Multi-device run: {devices.Length} device(s), {concurrency}"
                + $"{Environment.NewLine}Completed: {completed}; Failed: {failed}; Cancelled: {cancelled}"
                + (string.IsNullOrWhiteSpace(details) ? string.Empty
                    : Environment.NewLine + details);
        }

        private static bool ShouldNotifyFailure(OneShotFarmResult result)
        {
            if (result == null || result.Success) return false;
            switch (result.Outcome)
            {
                case OneShotFarmOutcome.ResourceNotFound:
                case OneShotFarmOutcome.ResourceLevelsExhausted:
                case OneShotFarmOutcome.NoEligibleTeam:
                case OneShotFarmOutcome.AllCandidateStoragesFull:
                case OneShotFarmOutcome.ResourcePlanExhausted:
                case OneShotFarmOutcome.TeamAvailabilityWaitTimeout:
                case OneShotFarmOutcome.Cancelled:
                    return false;
                default:
                    return true;
            }
        }

        private async Task<AutomationNotificationDeliveryResult> NotifyFailureSafelyAsync(string deviceName,
            OneShotFarmResult result)
        {
            try
            {
                return await failureNotifier.NotifyAsync(CreateFailureNotification(
                    deviceName, result), lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Closing the window may cancel delivery, but the workflow result remains valid.
                return null;
            }
            catch (Exception)
            {
                // A notifier implementation must never replace the gameplay outcome.
                return new AutomationNotificationDeliveryResult
                {
                    Attempted = true,
                    Success = false,
                    Message = "Failure notification raised an unexpected error."
                };
            }
        }

        private async Task<AutomationNotificationDeliveryResult> NotifyExceptionSafelyAsync(string deviceName,
            Exception exception)
        {
            try
            {
                return await failureNotifier.NotifyAsync(new AutomationFailureNotification
                {
                    DeviceName = deviceName,
                    Outcome = "UnhandledException",
                    Step = "RunOneShotFarm",
                    Message = exception.Message,
                    Error = exception.GetType().Name
                }, lifetimeCancellation.Token);
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception)
            {
                return new AutomationNotificationDeliveryResult
                {
                    Attempted = true,
                    Success = false,
                    Message = "Failure notification raised an unexpected error."
                };
            }
        }

        private void AppendNotificationStatus(AutomationNotificationDeliveryResult delivery)
        {
            if (delivery == null || string.IsNullOrWhiteSpace(delivery.Message)) return;
            StatusTextBlock.Text += Environment.NewLine + "Notification: " + delivery.Message;
        }

        private static AutomationFailureNotification CreateFailureNotification(
            string deviceName, OneShotFarmResult result) => new AutomationFailureNotification
        {
            DeviceName = deviceName,
            Outcome = result.Outcome.ToString(),
            Step = result.LastCompletedStep.ToString(),
            Resource = (result.CurrentResource ?? result.LocatedResource
                ?? result.RequestedResource).ToString(),
            Level = (result.LocatedLevel ?? result.RequestedLevel).ToString(
                CultureInfo.InvariantCulture),
            Team = (result.SelectedTeam ?? result.DispatchedTeam)?.ToString(),
            Message = result.Message,
            Error = result.ErrorMessage,
            DiagnosticPath = result.DiagnosticScreenshotPath
        };
    }

    internal sealed class DeviceSelectionItem : INotifyPropertyChanged
    {
        private bool isSelected;
        private string status;

        public DeviceSelectionItem(string deviceName)
        {
            DeviceName = deviceName ?? throw new ArgumentNullException(nameof(deviceName));
        }

        public string DeviceName { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value) return;
                isSelected = value;
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public string Status
        {
            get => status;
            set
            {
                if (string.Equals(status, value, StringComparison.Ordinal)) return;
                status = value;
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(Status)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
