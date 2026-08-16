using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Abstractions;
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
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ADB_Tool_Automation_Post_FB.UI
{
    public partial class DeviceDiagnosticWindow : Window
    {
        private readonly IDeviceDiagnosticService diagnosticService;
        private readonly ILdPlayerLaunchConfigurationService launchConfigurationService;
        private readonly IMultiDeviceOneShotFarmRunner multiDeviceFarmRunner;
        private readonly IContinuousFarmSupervisor continuousFarmSupervisor;
        private readonly OneShotFarmRequest defaultOneShotFarmRequest;
        private readonly IFarmUiPreferencesStore farmPreferencesStore;
        private readonly IAutomationFailureNotifier failureNotifier;
        private readonly FarmUiPreferences defaultFarmPreferences;
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private readonly DispatcherTimer oneShotFarmProgressTimer;
        private readonly DispatcherTimer continuousProgressFlushTimer;
        private readonly object continuousProgressSync = new object();
        private readonly Dictionary<string, PendingContinuousUpdate> pendingContinuousUpdates =
            new Dictionary<string, PendingContinuousUpdate>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> lastContinuousUiFingerprints =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTimeOffset> lastContinuousUiAppliedAt =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        private const int MaxContinuousDeviceUpdatesPerFlush = 5;
        private const int ContinuousUiTimeBudgetMs = 10;
        private static readonly TimeSpan OffscreenContinuousUiInterval =
            TimeSpan.FromSeconds(2);
        private PendingContinuousUpdate pendingContinuousHealth;
        private DateTimeOffset lastContinuousHealthFlush = DateTimeOffset.MinValue;
        private string lastContinuousHealthFingerprint = string.Empty;
        private bool isBatchingContinuousUi;
        private bool deviceSummaryRefreshPending;
        private bool progressOverviewRefreshPending;
        private CancellationTokenSource oneShotFarmCancellation;
        private bool oneShotFarmCancellationRequested;
        private long oneShotFarmRunGeneration;
        private readonly ObservableCollection<DeviceSelectionItem> deviceSelections =
            new ObservableCollection<DeviceSelectionItem>();
        private readonly ObservableCollection<DeviceFarmProgressItem> farmProgressItems =
            new ObservableCollection<DeviceFarmProgressItem>();
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
            ILdPlayerLaunchConfigurationService launchConfigurationService,
            IMultiDeviceOneShotFarmRunner multiDeviceFarmRunner,
            IContinuousFarmSupervisor continuousFarmSupervisor,
            OneShotFarmRequest defaultOneShotFarmRequest,
            ReadyTeamGateOptions defaultReadyTeamGateOptions,
            IFarmUiPreferencesStore farmPreferencesStore,
            IAutomationFailureNotifier failureNotifier)
        {
            this.diagnosticService = diagnosticService
                ?? throw new ArgumentNullException(nameof(diagnosticService));
            this.launchConfigurationService = launchConfigurationService
                ?? throw new ArgumentNullException(nameof(launchConfigurationService));
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
            FarmProgressItemsControl.ItemsSource = farmProgressItems;
            oneShotFarmProgressTimer = new DispatcherTimer(DispatcherPriority.Background)
                { Interval = TimeSpan.FromSeconds(2) };
            oneShotFarmProgressTimer.Tick += OneShotFarmProgressTimer_Tick;
            continuousProgressFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
                { Interval = TimeSpan.FromMilliseconds(500) };
            continuousProgressFlushTimer.Tick += ContinuousProgressFlushTimer_Tick;
            ApplyFarmPreferences(defaultFarmPreferences);
            Loaded += DeviceDiagnosticWindow_Loaded;
            Closed += (sender, args) =>
            {
                oneShotFarmCancellation?.Cancel();
                oneShotFarmProgressTimer.Stop();
                continuousProgressFlushTimer.Stop();
                lifetimeCancellation.Cancel();
            };
        }

        private async void DeviceDiagnosticWindow_Loaded(object sender, RoutedEventArgs e)
        {
            StartInitialLoadingSpinner();
            // Let WPF paint the loading overlay before configuration and LDPlayer
            // discovery begin. Without this render yield the new window can remain
            // visually blank while the first asynchronous operation starts.
            await Dispatcher.Yield(DispatcherPriority.Render);
            try
            {
                await LoadFarmPreferencesAndRefreshAsync();
            }
            finally
            {
                StopInitialLoadingSpinner();
                InitialLoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void StartInitialLoadingSpinner()
        {
            var animation = new DoubleAnimation(0, 360,
                TimeSpan.FromMilliseconds(800))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            InitialLoadingSpinnerRotate.BeginAnimation(
                RotateTransform.AngleProperty, animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void StopInitialLoadingSpinner()
        {
            InitialLoadingSpinnerRotate.BeginAnimation(
                RotateTransform.AngleProperty, null);
        }

        private async Task LoadFarmPreferencesAndRefreshAsync()
        {
            string warning = null;
            try
            {
                FarmUiPreferencesLoadResult load = await Task.Run(() =>
                    farmPreferencesStore.LoadAsync(defaultFarmPreferences,
                        lifetimeCancellation.Token), lifetimeCancellation.Token);
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
            DeviceLoadingPanel.Visibility = Visibility.Visible;
            DeviceSummaryTextBlock.Visibility = Visibility.Collapsed;
            try
            {
                await RunOperationAsync(async cancellationToken =>
                {
                    IReadOnlyList<string> deviceNames = await Task.Run(() =>
                        diagnosticService.GetDeviceNamesAsync(cancellationToken),
                        cancellationToken);

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
                        Status = found ? existing.Status : "Đã tắt"
                    });
                }

                await RefreshDeviceRuntimeStateAsync(deviceNames, cancellationToken);
                RequestDeviceSummaryRefresh();

                    return deviceNames.Count == 0
                        ? "No LDPlayer instances were found. Check LDCONSOLE_PATH and create an instance in LDPlayer."
                        : $"Found {deviceNames.Count} LDPlayer instance(s).";
                });
            }
            finally
            {
                DeviceLoadingPanel.Visibility = Visibility.Collapsed;
                DeviceSummaryTextBlock.Visibility = Visibility.Visible;
                UpdateDeviceSummary();
            }
        }

        private async Task RefreshDeviceRuntimeStateAsync(
            IReadOnlyList<string> deviceNames,
            CancellationToken cancellationToken)
        {
            using (var gate = new SemaphoreSlim(4, 4))
            {
                Task[] checks = deviceNames.Select(async deviceName =>
                {
                    await gate.WaitAsync(cancellationToken);
                    try
                    {
                        DeviceDiagnosticResult result = await Task.Run(() =>
                            diagnosticService.CheckDeviceAsync(deviceName, cancellationToken),
                            cancellationToken);
                        DeviceSelectionItem item = deviceSelections.FirstOrDefault(value =>
                            string.Equals(value.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
                        if (item == null) return;

                        item.IsRunning = result.IsRunning;
                        item.IsInGame = result.IsRunning
                            && result.ScreenshotSucceeded
                            && result.MatchesExpectedResolution;
                        if (!item.IsInGame)
                            item.IsSelected = false;
                        if (!activeDeviceNames.Contains(deviceName))
                            item.Status = item.IsRunning ? "Đang mở" : "Đã tắt";
                    }
                    finally
                    {
                        gate.Release();
                    }
                }).ToArray();

                await Task.WhenAll(checks);
            }
        }

        private void UpdateDeviceSummary()
        {
            int total = deviceSelections.Count;
            int open = deviceSelections.Count(item => item.IsRunning);
            int inGame = deviceSelections.Count(item => item.IsInGame);
            DeviceSummaryTextBlock.Text = $"Tổng: {total} · Đang mở: {open} · Trong game: {inGame}";
        }

        private async void RunContinuousFarm_Click(object sender, RoutedEventArgs e)
        {
            if (oneShotFarmCancellation != null)
            {
                StopOneShotFarm_Click(sender, e);
                return;
            }
            if (!TryReadFarmPreferences(out FarmUiPreferences preferences,
                out string validationError))
            {
                StatusTextBlock.Text = validationError;
                return;
            }

            string[] allDevices = deviceSelections.Select(item => item.DeviceName).ToArray();
            if (allDevices.Length == 0)
            {
                StatusTextBlock.Text = "Không có thiết bị LDPlayer để kiểm tra.";
                return;
            }
            StatusTextBlock.Text = "Đang kiểm tra cấu hình 1280x720 DPI 240 và Debug ADB cho toàn bộ thiết bị...";
            LdPlayerLaunchConfigurationResult configuration = await Task.Run(() =>
                launchConfigurationService.ConfigureAsync(allDevices, lifetimeCancellation.Token),
                lifetimeCancellation.Token);
            foreach (LdPlayerInstanceConfigurationResult result in configuration.Devices)
            {
                DeviceSelectionItem item = deviceSelections.FirstOrDefault(value =>
                    string.Equals(value.DeviceName, result.DeviceName,
                        StringComparison.OrdinalIgnoreCase));
                if (item != null) item.Status = result.Success ? "Đã cấu hình" : "Kiểm tra lỗi";
            }
            if (!configuration.Success)
            {
                StatusTextBlock.Text = "Chưa chạy farm vì có thiết bị cấu hình thất bại: "
                    + string.Join("; ", configuration.Devices.Where(item => !item.Success)
                        .Select(item => item.DeviceName + " - " + item.Message));
                return;
            }
            await RefreshDeviceRuntimeStateAsync(allDevices, lifetimeCancellation.Token);
            string[] selectedDevices = deviceSelections.Where(item => item.IsSelected && item.IsInGame)
                .Select(item => item.DeviceName).ToArray();
            if (selectedDevices.Length == 0)
            {
                StatusTextBlock.Text = "Cấu hình xong nhưng chưa có thiết bị đã chọn sẵn sàng chạy.";
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
                lastContinuousUiFingerprints.Remove(deviceName);
                lastContinuousUiAppliedAt.Remove(deviceName);
            }
            var progress = new DirectProgress<ContinuousFarmSupervisorProgress>(value =>
                QueueContinuousFarmProgress(runGeneration, runCancellation,
                    attemptVersions, value));
            continuousProgressFlushTimer.Start();
            SetFarmActionButtonRunning();
            OneShotFarmResourcesGroupBox.IsEnabled = false;
            StatusTextBlock.Text = $"Continuous supervisor đang quản lý {deviceNames.Length} thiết bị...";
            // The supervisor performs synchronous checkpoint preparation before its
            // first await and many legacy workflow awaits intentionally have no WPF
            // awareness. Starting it from this event handler would therefore inherit
            // the Dispatcher context and let automation continuations compete with
            // layout/input when many devices run. Paint the running state first, then
            // keep the complete supervisor call behind a background boundary. UI
            // progress still reaches WPF only through the bounded coalescing timer.
            await Dispatcher.Yield(DispatcherPriority.Render);
            try
            {
                ContinuousFarmSupervisorResult result =
                    await Task.Run(async () =>
                        await continuousFarmSupervisor.RunAsync(deviceNames, request,
                            progress, runCancellation.Token).ConfigureAwait(false),
                        runCancellation.Token);
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
                continuousProgressFlushTimer.Stop();
                FlushContinuousProgress(true);
                if (ReferenceEquals(oneShotFarmCancellation, runCancellation))
                    oneShotFarmCancellation = null;
                runCancellation.Dispose();
                SetFarmActionButtonIdle();
                OneShotFarmResourcesGroupBox.IsEnabled = true;
            }
        }

        private void ApplyContinuousFarmProgress(long runGeneration,
            CancellationTokenSource runCancellation,
            IReadOnlyDictionary<string, long> attemptVersions,
            ContinuousFarmSupervisorProgress progress)
        {
            if (progress == null || !OneShotFarmProgressUtilities.IsCurrentRun(
                runGeneration, oneShotFarmRunGeneration, runCancellation,
                oneShotFarmCancellation)) return;
            if (progress.Device == null
                || !IsCurrentDeviceAttempt(progress.Device.DeviceName,
                    attemptVersions)) return;
            ContinuousFarmDeviceSnapshot snapshot = progress.Device;
            DeviceSelectionItem item = deviceSelections.FirstOrDefault(value =>
                string.Equals(value.DeviceName, snapshot.DeviceName,
                    StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.IsRunning = true;
                item.IsInGame = true;
                item.Status = FarmProgressVietnamese.Stage(snapshot.State.ToString());
                RequestDeviceSummaryRefresh();
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
                    snapshot.DeviceName, progress.FarmProgress.DeviceProgress);
            }
            else
            {
                GetOrCreateFarmProgress(snapshot.DeviceName)
                    .ApplySupervisorSnapshot(snapshot);
            }
            if (snapshot.State == ContinuousFarmDeviceState.Waiting
                && snapshot.NextAttemptAt.HasValue)
                oneShotFarmProgressTimer.Start();
        }

        private void QueueContinuousFarmProgress(long runGeneration,
            CancellationTokenSource runCancellation,
            IReadOnlyDictionary<string, long> attemptVersions,
            ContinuousFarmSupervisorProgress progress)
        {
            if (progress == null) return;
            var update = new PendingContinuousUpdate(runGeneration, runCancellation,
                attemptVersions, progress, BuildContinuousUiFingerprint(progress));
            lock (continuousProgressSync)
            {
                if (progress.Device != null)
                {
                    string deviceName = progress.Device.DeviceName;
                    PendingContinuousUpdate pending;
                    bool hasPending = pendingContinuousUpdates.TryGetValue(
                        deviceName, out pending);
                    // Keep a resolved roster/recovery transition until the next
                    // bounded UI flush. A later noisy Running snapshot may not
                    // overwrite it, but it also must not enqueue an unbounded
                    // Dispatcher callback for every progress event.
                    if (!hasPending || IsCriticalContinuousUpdate(progress)
                        || !IsCriticalContinuousUpdate(pending.Progress))
                        pendingContinuousUpdates[deviceName] = update;
                }
                if (progress.Health != null) pendingContinuousHealth = update;
            }
        }

        private static bool IsCriticalContinuousUpdate(
            ContinuousFarmSupervisorProgress progress)
        {
            if (progress?.Device == null) return false;
            OneShotFarmProgressStage? farmStage =
                progress.FarmProgress?.DeviceProgress?.Stage;
            return progress.Device.State == ContinuousFarmDeviceState.Recovering
                || progress.Device.State == ContinuousFarmDeviceState.Quarantined
                || progress.Device.State == ContinuousFarmDeviceState.Stopped
                || farmStage == OneShotFarmProgressStage.ReadyTeamFound
                || farmStage == OneShotFarmProgressStage.WaitingForReadyTeam
                // A verified resource toast is short-lived and immediately followed by
                // recovery progress. Preserve it through the bounded UI queue so the
                // device card can show why the mapped-area retry started.
                || !string.IsNullOrWhiteSpace(
                    progress.FarmProgress?.DeviceProgress?.ResourceToastVariant)
                || !string.IsNullOrWhiteSpace(
                    progress.FarmProgress?.DeviceProgress?.ResourceToastText);
        }

        private static string BuildContinuousUiFingerprint(
            ContinuousFarmSupervisorProgress progress)
        {
            ContinuousFarmDeviceSnapshot device = progress?.Device;
            OneShotFarmProgress farm = progress?.FarmProgress?.DeviceProgress;
            if (device == null) return string.Empty;
            return string.Join("|", new[]
            {
                device.DeviceName ?? string.Empty,
                device.State.ToString(),
                device.CycleCount.ToString(CultureInfo.InvariantCulture),
                device.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture),
                device.CurrentOperation ?? string.Empty,
                device.CurrentResource ?? string.Empty,
                device.CurrentLevel?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                device.CurrentTeam ?? string.Empty,
                device.CurrentExpectedTeam ?? string.Empty,
                device.CurrentSelectedTeam ?? string.Empty,
                device.ConfirmedRosterCount.ToString(CultureInfo.InvariantCulture),
                device.RosterConfidence ?? string.Empty,
                FormatTeamFingerprint(device.DetectedTeams),
                FormatTeamFingerprint(device.ReadyTeams),
                FormatTeamFingerprint(device.BusyTeams),
                FormatTeamFingerprint(device.LockedTeams),
                device.MapRepositionState.ToString(),
                device.TerritoryColorSummary ?? string.Empty,
                device.NextAttemptAt?.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                device.Message ?? string.Empty,
                device.LastError ?? string.Empty,
                farm?.Stage.ToString() ?? string.Empty,
                farm?.Message ?? string.Empty,
                farm?.CurrentStep?.ToString() ?? string.Empty,
                farm?.CurrentResource?.ToString() ?? string.Empty,
                farm?.CurrentLevel?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                farm?.CurrentTeam?.ToString() ?? string.Empty,
                farm?.CurrentExpectedTeam?.ToString() ?? string.Empty,
                farm?.CurrentSelectedTeam?.ToString() ?? string.Empty,
                farm?.ConfirmedRosterCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                farm?.RosterConfidence ?? string.Empty,
                FormatTeamFingerprint(farm?.AllowedTeams),
                FormatTeamFingerprint(farm?.DetectedTeams),
                FormatTeamFingerprint(farm?.ReadyTeams),
                FormatTeamFingerprint(farm?.BusyTeams),
                FormatTeamFingerprint(farm?.LockedTeams),
                FormatTeamFingerprint(farm?.EligibleReadyTeams),
                farm?.ResourceToastText ?? string.Empty,
                farm?.ResourceToastVariant ?? string.Empty,
                farm?.ResourceToastState ?? string.Empty,
                farm?.NextCheckAt?.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                farm?.WaitDeadline?.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            });
        }

        private static string FormatTeamFingerprint(
            IReadOnlyList<TeamNumber> teams) => teams == null
                ? string.Empty
                : string.Join(",", teams.Select(team =>
                    ((int)team).ToString(CultureInfo.InvariantCulture)));

        private static string BuildContinuousHealthFingerprint(
            ContinuousFarmHealthSnapshot health)
        {
            if (health == null) return string.Empty;
            return string.Join("|", new[]
            {
                health.TotalDevices.ToString(CultureInfo.InvariantCulture),
                health.HealthyDevices.ToString(CultureInfo.InvariantCulture),
                health.PreflightDevices.ToString(CultureInfo.InvariantCulture),
                health.ReadyDevices.ToString(CultureInfo.InvariantCulture),
                health.RunningDevices.ToString(CultureInfo.InvariantCulture),
                health.WaitingDevices.ToString(CultureInfo.InvariantCulture),
                health.RecoveringDevices.ToString(CultureInfo.InvariantCulture),
                health.QuarantinedDevices.ToString(CultureInfo.InvariantCulture),
                health.StoppedDevices.ToString(CultureInfo.InvariantCulture),
                health.DevicesWithFailures.ToString(CultureInfo.InvariantCulture),
                health.LowDiskDevices.ToString(CultureInfo.InvariantCulture),
                health.ActiveExecutions.ToString(CultureInfo.InvariantCulture),
                health.ConcurrencyLimit.ToString(CultureInfo.InvariantCulture),
                health.FarmQueued.ToString(CultureInfo.InvariantCulture),
                health.PreflightActive.ToString(CultureInfo.InvariantCulture),
                health.PreflightQueued.ToString(CultureInfo.InvariantCulture),
                health.ScreenshotActive.ToString(CultureInfo.InvariantCulture),
                health.ScreenshotQueued.ToString(CultureInfo.InvariantCulture),
                health.VisionActive.ToString(CultureInfo.InvariantCulture),
                health.VisionQueued.ToString(CultureInfo.InvariantCulture),
                health.LastHeartbeatSucceeded?.ToString() ?? string.Empty,
                health.HeartbeatMessage ?? string.Empty
            });
        }

        private void ContinuousProgressFlushTimer_Tick(object sender, EventArgs e) =>
            FlushContinuousProgress();

        private void FlushContinuousProgress(bool drainAll = false)
        {
            PendingContinuousUpdate[] updates;
            PendingContinuousUpdate health = null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (continuousProgressSync)
            {
                updates = pendingContinuousUpdates.Values.ToArray();
                pendingContinuousUpdates.Clear();
                if (now - lastContinuousHealthFlush >= TimeSpan.FromSeconds(2))
                {
                    health = pendingContinuousHealth;
                    pendingContinuousHealth = null;
                    lastContinuousHealthFlush = now;
                }
            }
            updates = updates
                .OrderByDescending(update => IsCriticalContinuousUpdate(update.Progress))
                .ThenByDescending(update => IsFarmProgressVisible(
                    update.Progress?.Device?.DeviceName))
                .ThenBy(update => GetLastContinuousUiAppliedAt(
                    update.Progress?.Device?.DeviceName))
                .ToArray();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int appliedCount = 0;
            isBatchingContinuousUi = true;
            try
            {
                foreach (PendingContinuousUpdate update in updates)
                {
                    if (!IsCurrentContinuousUpdate(update)) continue;
                    string deviceName = update.Progress.Device.DeviceName;
                    bool critical = IsCriticalContinuousUpdate(update.Progress);
                    string previousFingerprint;
                    if (lastContinuousUiFingerprints.TryGetValue(deviceName,
                        out previousFingerprint)
                        && string.Equals(previousFingerprint, update.UiFingerprint,
                            StringComparison.Ordinal))
                        continue;
                    if (!drainAll && !critical && !IsFarmProgressVisible(deviceName)
                        && now - GetLastContinuousUiAppliedAt(deviceName)
                            < OffscreenContinuousUiInterval)
                    {
                        RequeueContinuousUpdate(update);
                        continue;
                    }
                    if (!drainAll && (appliedCount >= MaxContinuousDeviceUpdatesPerFlush
                        || stopwatch.ElapsedMilliseconds >= ContinuousUiTimeBudgetMs))
                    {
                        RequeueContinuousUpdate(update);
                        continue;
                    }
                    ApplyContinuousFarmProgress(update.RunGeneration,
                        update.RunCancellation, update.AttemptVersions,
                        update.Progress);
                    lastContinuousUiFingerprints[deviceName] = update.UiFingerprint;
                    lastContinuousUiAppliedAt[deviceName] = now;
                    appliedCount++;
                }
                ApplyContinuousHealth(health);
            }
            finally
            {
                isBatchingContinuousUi = false;
                FlushAggregateUiRefreshes();
            }
        }

        private bool IsCurrentContinuousUpdate(PendingContinuousUpdate update) =>
            update?.Progress?.Device != null
            && OneShotFarmProgressUtilities.IsCurrentRun(
                update.RunGeneration, oneShotFarmRunGeneration,
                update.RunCancellation, oneShotFarmCancellation)
            && IsCurrentDeviceAttempt(update.Progress.Device.DeviceName,
                update.AttemptVersions);

        private void RequeueContinuousUpdate(PendingContinuousUpdate update)
        {
            if (update?.Progress?.Device == null) return;
            string deviceName = update.Progress.Device.DeviceName;
            lock (continuousProgressSync)
            {
                PendingContinuousUpdate newer;
                if (!pendingContinuousUpdates.TryGetValue(deviceName, out newer))
                    pendingContinuousUpdates[deviceName] = update;
            }
        }

        private bool IsFarmProgressVisible(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            DeviceFarmProgressItem item = farmProgressItems.FirstOrDefault(value =>
                string.Equals(value.DeviceName, deviceName,
                    StringComparison.OrdinalIgnoreCase));
            if (item == null) return true;
            FrameworkElement container = FarmProgressItemsControl.ItemContainerGenerator
                .ContainerFromItem(item) as FrameworkElement;
            return container != null && container.IsVisible;
        }

        private DateTimeOffset GetLastContinuousUiAppliedAt(string deviceName)
        {
            DateTimeOffset value;
            return !string.IsNullOrWhiteSpace(deviceName)
                && lastContinuousUiAppliedAt.TryGetValue(deviceName, out value)
                    ? value : DateTimeOffset.MinValue;
        }

        private void ApplyContinuousHealth(PendingContinuousUpdate update)
        {
            if (update?.Progress?.Health == null
                || !OneShotFarmProgressUtilities.IsCurrentRun(
                    update.RunGeneration, oneShotFarmRunGeneration,
                    update.RunCancellation, oneShotFarmCancellation)) return;
            string fingerprint = BuildContinuousHealthFingerprint(
                update.Progress.Health);
            if (string.Equals(lastContinuousHealthFingerprint, fingerprint,
                StringComparison.Ordinal)) return;
            lastContinuousHealthFingerprint = fingerprint;
            ApplyHealthDashboard(update.Progress.Health);
        }

        private void RequestDeviceSummaryRefresh()
        {
            if (!isBatchingContinuousUi)
            {
                UpdateDeviceSummary();
                return;
            }
            deviceSummaryRefreshPending = true;
        }

        private void RequestProgressOverviewRefresh()
        {
            if (!isBatchingContinuousUi)
            {
                UpdateProgressOverview();
                return;
            }
            progressOverviewRefreshPending = true;
        }

        private void FlushAggregateUiRefreshes()
        {
            if (deviceSummaryRefreshPending)
            {
                deviceSummaryRefreshPending = false;
                UpdateDeviceSummary();
            }
            if (progressOverviewRefreshPending)
            {
                progressOverviewRefreshPending = false;
                UpdateProgressOverview();
            }
        }

        private void ApplyHealthDashboard(ContinuousFarmHealthSnapshot health)
        {
            bool needsAttention = health.DevicesWithFailures > 0
                || health.LowDiskDevices > 0
                || health.QuarantinedDevices > 0
                || health.RecoveringDevices > 0;
            int preparingDevices = health.PreflightDevices + health.ReadyDevices;
            int operationalDevices = health.RunningDevices + health.WaitingDevices;
            HealthOverviewTextBlock.Text = health.TotalDevices <= 0
                ? "Chưa chạy"
                : needsAttention
                    ? "Cần chú ý"
                    : preparingDevices > 0
                        ? $"Đang chuẩn bị {preparingDevices}/{health.TotalDevices}"
                        : $"{operationalDevices}/{health.TotalDevices} hoạt động";
            HealthOverviewTextBlock.Foreground = needsAttention
                ? Brushes.DarkOrange
                : preparingDevices > 0 ? Brushes.RoyalBlue : Brushes.SeaGreen;
            HealthOverviewBadge.Background = needsAttention
                ? Brushes.OldLace
                : preparingDevices > 0
                    ? Brushes.AliceBlue
                    : Brushes.Honeydew;
            HealthStatesTextBlock.Text = FormatHealthStates(health);
            HealthPressureTextBlock.Text = FormatHealthPressure(health);
            HealthLoadTextBlock.Text = health.ConcurrencyLimit > 0
                ? $"Farm {health.ActiveExecutions}/{health.ConcurrencyLimit}"
                    + FormatQueue(health.FarmQueued)
                    + $" · Ảnh {health.ScreenshotActive}/{health.ScreenshotLimit}"
                    + FormatQueue(health.ScreenshotQueued)
                    + $" · Nhận diện {health.VisionActive}/{health.VisionLimit}"
                    + FormatQueue(health.VisionQueued)
                : "Chưa có dữ liệu";
            bool heartbeatFailed = health.LastHeartbeatSucceeded == false;
            HealthHeartbeatTextBlock.Text = heartbeatFailed
                ? "Telegram: " + FormatHeartbeat(health) : string.Empty;
            HealthHeartbeatTextBlock.Visibility = heartbeatFailed
                ? Visibility.Visible : Visibility.Collapsed;
            TimeSpan uptime = health.GeneratedAt - health.StartedAt;
            if (uptime < TimeSpan.Zero) uptime = TimeSpan.Zero;
            HealthUptimeTextBlock.Text = uptime.TotalDays >= 1
                ? $"{(int)uptime.TotalDays} ngày {uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}"
                : $"{uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
        }

        private static string FormatQueue(int queued) => queued > 0
            ? $" (+{queued} chờ)" : string.Empty;

        private static string FormatHealthStates(ContinuousFarmHealthSnapshot health)
        {
            var states = new List<string>();
            AddHealthState(states, "Đang chạy", health.RunningDevices);
            AddHealthState(states, "Đang chờ", health.WaitingDevices);
            AddHealthState(states, "Chuẩn bị",
                health.PreflightDevices + health.ReadyDevices);
            AddHealthState(states, "Đang khôi phục", health.RecoveringDevices);
            AddHealthState(states, "Cách ly", health.QuarantinedDevices);
            AddHealthState(states, "Đã dừng", health.StoppedDevices);
            return states.Count > 0
                ? string.Join(" · ", states)
                : health.TotalDevices > 0
                    ? "Không có thiết bị đang hoạt động"
                    : "Chưa bắt đầu";
        }

        private static string FormatHealthPressure(ContinuousFarmHealthSnapshot health)
        {
            var warnings = new List<string>();
            AddHealthState(warnings, "Thiết bị lỗi", health.DevicesWithFailures);
            AddHealthState(warnings, "Thiếu dung lượng", health.LowDiskDevices);
            return warnings.Count > 0
                ? string.Join(" · ", warnings)
                : "Không có cảnh báo";
        }

        private static void AddHealthState(
            ICollection<string> values, string label, int count)
        {
            if (count > 0) values.Add($"{label}: {count}");
        }

        private static string FormatHeartbeat(ContinuousFarmHealthSnapshot health)
        {
            if (!health.LastHeartbeatAttemptAt.HasValue)
                return "Chưa gửi";

            string time = health.LastHeartbeatAttemptAt.Value
                .ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            if (health.LastHeartbeatSucceeded == true)
                return $"{time} · Gửi thành công";
            if (health.LastHeartbeatSucceeded != false)
                return $"{time} · Đã bỏ qua";

            string message = health.HeartbeatMessage ?? string.Empty;
            bool configurationError = message.IndexOf(
                    "token", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("revoked", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("not configured", StringComparison.OrdinalIgnoreCase) >= 0;
            return configurationError
                ? $"{time} · Cấu hình Telegram chưa hợp lệ"
                : $"{time} · Gửi thất bại";
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
                GetOrCreateFarmProgress(deviceName).SetQueued();
            }
            ProgressOverviewTextBlock.Text = $"0/{deviceNames.Length} hoàn tất";
            var progress = new Progress<MultiDeviceOneShotFarmProgress>(value =>
                ApplyMultiDeviceFarmProgress(runGeneration, runCancellation,
                    attemptVersions, value));
            foreach (DeviceSelectionItem item in deviceSelections.Where(item =>
                deviceNames.Contains(item.DeviceName, StringComparer.OrdinalIgnoreCase)))
                item.Status = "Đang xếp hàng";
            SetFarmActionButtonRunning();
            OneShotFarmResourcesGroupBox.IsEnabled = false;
            StatusTextBlock.Text = isRetry
                ? $"Đang chạy lại {deviceNames.Length} thiết bị lỗi; các thiết bị khác không bị ảnh hưởng..."
                : $"Đang chạy {deviceNames.Length} thiết bị; tối đa 25 thiết bị đồng thời...";
            try
            {
                MultiDeviceOneShotFarmResult result = await multiDeviceFarmRunner.RunAsync(
                    deviceNames, request, progress, runCancellation.Token);
                UpdateRetryCandidates(result, request, attemptVersions);
                StatusTextBlock.Text = FormatMultiDeviceFarmResult(result)
                    + (string.IsNullOrWhiteSpace(saveWarning) ? string.Empty
                        : Environment.NewLine + "Cảnh báo: " + saveWarning);
                foreach (MultiDeviceOneShotFarmItemResult item in result.Devices
                    .Where(item => ShouldNotifyFailure(item.Result)))
                    AppendNotificationStatus(await NotifyFailureSafelyAsync(
                        item.DeviceName, item.Result));
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Đã hủy lượt farm.";
            }
            catch (Exception exception)
            {
                foreach (string deviceName in deviceNames.Where(name =>
                    IsCurrentDeviceAttempt(name, attemptVersions)))
                    failedDeviceNames.Add(deviceName);
                retryRequest = request;
                foreach (DeviceSelectionItem item in deviceSelections.Where(item =>
                    failedDeviceNames.Contains(item.DeviceName)))
                    item.Status = "Thất bại: " + UserErrorPresenter.Present(exception).UserMessage;
                StatusTextBlock.Text = "Lỗi: " + UserErrorPresenter.Present(exception).UserMessage;
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
                    SetFarmActionButtonIdle();
                    OneShotFarmResourcesGroupBox.IsEnabled = true;
                    if (failedDeviceNames.Count == 0) retryRequest = null;
                }
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
            {
                item.IsInGame = true;
                item.Status = FarmProgressVietnamese.Stage(progress.Stage.ToString());
                RequestDeviceSummaryRefresh();
            }
            if (progress.Stage == MultiDeviceOneShotFarmStage.Failed)
            {
                activeDeviceNames.Remove(progress.DeviceName);
                failedDeviceNames.Add(progress.DeviceName);
            }
            else if (progress.Stage == MultiDeviceOneShotFarmStage.Completed)
            {
                activeDeviceNames.Remove(progress.DeviceName);
                failedDeviceNames.Remove(progress.DeviceName);
            }
            else if (progress.Stage == MultiDeviceOneShotFarmStage.Cancelled)
            {
                activeDeviceNames.Remove(progress.DeviceName);
            }
            if (progress.DeviceProgress != null)
            {
                ApplyOneShotFarmProgress(runGeneration, runCancellation,
                    progress.DeviceName, progress.DeviceProgress);
            }
            RequestProgressOverviewRefresh();
        }

        private bool IsCurrentDeviceAttempt(string deviceName,
            IReadOnlyDictionary<string, long> attemptVersions)
        {
            return !string.IsNullOrWhiteSpace(deviceName)
                && attemptVersions.TryGetValue(deviceName, out long attemptVersion)
                && deviceAttemptVersions.TryGetValue(deviceName, out long currentVersion)
                && attemptVersion == currentVersion;
        }

        private void StopOneShotFarm_Click(object sender, RoutedEventArgs e)
        {
            CancellationTokenSource currentRun = oneShotFarmCancellation;
            if (currentRun == null || oneShotFarmCancellationRequested) return;
            oneShotFarmCancellationRequested = true;
            SetFarmActionButtonStopping();
            ProgressOverviewTextBlock.Text = "Đang dừng";
            foreach (DeviceFarmProgressItem item in farmProgressItems.Where(item =>
                activeDeviceNames.Contains(item.DeviceName)))
                item.SetStopping();
            StopOneShotFarmProgressTimer();
            currentRun.Cancel();
        }

        private void SetFarmActionButtonIdle()
        {
            RunContinuousFarmButton.Content = "▶  Chạy liên tục";
            RunContinuousFarmButton.Background = new SolidColorBrush(
                Color.FromRgb(15, 118, 110));
            RunContinuousFarmButton.IsEnabled = true;
        }

        private void SetFarmActionButtonRunning()
        {
            RunContinuousFarmButton.Content = "■  Dừng";
            RunContinuousFarmButton.Background = new SolidColorBrush(
                Color.FromRgb(220, 38, 38));
            RunContinuousFarmButton.IsEnabled = true;
        }

        private void SetFarmActionButtonStopping()
        {
            RunContinuousFarmButton.Content = "■  Đang dừng...";
            RunContinuousFarmButton.Background = new SolidColorBrush(
                Color.FromRgb(185, 28, 28));
            RunContinuousFarmButton.IsEnabled = false;
        }

        private void ApplyOneShotFarmProgress(long runGeneration,
            CancellationTokenSource runCancellation, string deviceName,
            OneShotFarmProgress progress)
        {
            if (progress == null || !OneShotFarmProgressUtilities.IsCurrentRun(
                runGeneration, oneShotFarmRunGeneration, runCancellation,
                oneShotFarmCancellation)) return;
            try
            {
                DeviceFarmProgressItem progressItem = GetOrCreateFarmProgress(deviceName);
                progressItem.Apply(progress);
                if (progressItem.HasVisibleResourceToast)
                    oneShotFarmProgressTimer.Start();

                if (progress.Stage == OneShotFarmProgressStage.WaitingForReadyTeam)
                {
                    oneShotFarmProgressTimer.Start();
                }
                else if (progress.Stage == OneShotFarmProgressStage.ReadyTeamFound
                    || progress.Stage == OneShotFarmProgressStage.Completed
                    || progress.Stage == OneShotFarmProgressStage.Failed
                    || progress.Stage == OneShotFarmProgressStage.Cancelled)
                {
                    if (!farmProgressItems.Any(item => item.IsWaiting))
                        StopOneShotFarmProgressTimer();
                }
                RequestProgressOverviewRefresh();
            }
            catch (Exception)
            {
                // The dispatcher can be shutting down; progress must not fail gameplay.
            }
        }

        private void OneShotFarmProgressTimer_Tick(object sender, EventArgs e) =>
            UpdateOneShotFarmCountdown();

        private void UpdateOneShotFarmCountdown()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (DeviceFarmProgressItem item in farmProgressItems)
                item.UpdateCountdown(now);
            if (!farmProgressItems.Any(item => item.IsWaiting
                || item.HasVisibleResourceToast))
                oneShotFarmProgressTimer.Stop();
        }

        private void StopOneShotFarmProgressTimer()
        {
            if (farmProgressItems.Any(item => item.IsWaiting
                || item.HasVisibleResourceToast)) return;
            oneShotFarmProgressTimer.Stop();
        }

        private DeviceFarmProgressItem GetOrCreateFarmProgress(string deviceName)
        {
            string normalized = string.IsNullOrWhiteSpace(deviceName)
                ? "Thiết bị" : deviceName.Trim();
            DeviceFarmProgressItem item = farmProgressItems.FirstOrDefault(value =>
                string.Equals(value.DeviceName, normalized,
                    StringComparison.OrdinalIgnoreCase));
            if (item != null) return item;
            item = new DeviceFarmProgressItem(normalized);
            farmProgressItems.Add(item);
            return item;
        }

        private void UpdateProgressOverview()
        {
            if (farmProgressItems.Count == 0)
            {
                ProgressOverviewTextBlock.Text = "Chưa chạy";
                return;
            }
            int active = farmProgressItems.Count(item => item.IsActive);
            int failed = farmProgressItems.Count(item =>
                string.Equals(item.Stage, OneShotFarmProgressStage.Failed.ToString(),
                    StringComparison.Ordinal));
            ProgressOverviewTextBlock.Text = active > 0
                ? $"{active} đang chạy"
                : failed > 0 ? $"{failed} lỗi" : "Hoàn tất";
        }

        private void ApplyFarmPreferences(FarmUiPreferences preferences)
        {
            IronResourceCheckBox.IsChecked = preferences.Iron;
            StoneResourceCheckBox.IsChecked = preferences.Stone;
            WoodResourceCheckBox.IsChecked = preferences.Wood;
            FoodResourceCheckBox.IsChecked = preferences.Food;
        }

        private bool TryReadFarmPreferences(out FarmUiPreferences preferences,
            out string error)
        {
            error = null;
            preferences = new FarmUiPreferences
            {
                Iron = IronResourceCheckBox.IsChecked == true,
                Stone = StoneResourceCheckBox.IsChecked == true,
                Wood = WoodResourceCheckBox.IsChecked == true,
                Food = FoodResourceCheckBox.IsChecked == true,
                LevelPriority = defaultFarmPreferences.LevelPriority.ToArray(),
                TeamPriority = defaultFarmPreferences.TeamPriority.ToArray(),
                AllowTeam1 = defaultFarmPreferences.AllowTeam1,
                ReadyCheckIntervalMinutes =
                    FarmUiPreferences.DefaultReadyCheckIntervalMinutes,
                ReadyMaxWaitHours = defaultFarmPreferences.ReadyMaxWaitHours,
                UnoccupiedOnly = true
            };
            FarmUiPreferencesValidationResult validation = FarmUiPreferencesMapper.Validate(preferences);
            error = validation.IsValid ? null : validation.Message;
            return validation.IsValid;
        }


        private async Task RunOperationAsync(Func<CancellationToken, Task<string>> operation)
        {
            IsEnabled = false;
            StatusTextBlock.Text = "Đang xử lý...";
            try
            {
                StatusTextBlock.Text = await operation(lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Thao tác đã được hủy.";
            }
            catch (Exception exception)
            {
                StatusTextBlock.Text = "Lỗi: " + UserErrorPresenter.Present(exception).UserMessage;
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
                string outcome = item.Result != null
                    ? VietnameseDisplayNames.Outcome(item.Result.Outcome)
                    : string.IsNullOrWhiteSpace(item.ErrorMessage) ? "-"
                        : "Đã xảy ra lỗi trong quá trình xử lý.";
                return $"- {item.DeviceName}: "
                    + $"{VietnameseDisplayNames.Stage(item.Stage)} ({outcome})";
            }));
            string concurrency = result != null && result.AdaptiveConcurrencyEnabled
                ? $"đồng thời thích ứng: {result.FinalConcurrencyLimit}/{result.MaximumConcurrency}"
                : $"giới hạn đồng thời: {result?.MaximumConcurrency ?? 0}";
            return $"Đã xử lý {devices.Length} thiết bị, {concurrency}"
                + $"{Environment.NewLine}Hoàn tất: {completed}; Thất bại: {failed}; Đã hủy: {cancelled}"
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

        private sealed class PendingContinuousUpdate
        {
            public PendingContinuousUpdate(long runGeneration,
                CancellationTokenSource runCancellation,
                IReadOnlyDictionary<string, long> attemptVersions,
                ContinuousFarmSupervisorProgress progress,
                string uiFingerprint)
            {
                RunGeneration = runGeneration;
                RunCancellation = runCancellation;
                AttemptVersions = attemptVersions;
                Progress = progress;
                UiFingerprint = uiFingerprint ?? string.Empty;
            }
            public long RunGeneration { get; }
            public CancellationTokenSource RunCancellation { get; }
            public IReadOnlyDictionary<string, long> AttemptVersions { get; }
            public ContinuousFarmSupervisorProgress Progress { get; }
            public string UiFingerprint { get; }
        }

        private sealed class DirectProgress<T> : IProgress<T>
        {
            private readonly Action<T> callback;
            public DirectProgress(Action<T> callback)
            { this.callback = callback ?? throw new ArgumentNullException(nameof(callback)); }
            public void Report(T value) { callback(value); }
        }
    }

    internal sealed class DeviceSelectionItem : INotifyPropertyChanged
    {
        private static readonly Tuple<Brush, Brush, Brush> FailureColors =
            Colors(185, 28, 28, 254, 242, 242, 254, 202, 202);
        private static readonly Tuple<Brush, Brush, Brush> RecoveryColors =
            Colors(194, 65, 12, 255, 247, 237, 254, 215, 170);
        private static readonly Tuple<Brush, Brush, Brush> WaitingColors =
            Colors(180, 83, 9, 255, 251, 235, 253, 230, 138);
        private static readonly Tuple<Brush, Brush, Brush> RunningColors =
            Colors(29, 78, 216, 239, 246, 255, 191, 219, 254);
        private static readonly Tuple<Brush, Brush, Brush> ReadyColors =
            Colors(4, 120, 87, 236, 253, 245, 167, 243, 208);
        private static readonly Tuple<Brush, Brush, Brush> CheckingColors =
            Colors(3, 105, 161, 240, 249, 255, 186, 230, 253);
        private static readonly Tuple<Brush, Brush, Brush> NeutralColors =
            Colors(71, 85, 105, 248, 250, 252, 226, 232, 240);
        private bool isSelected;
        private string status;
        private bool isRunning;
        private bool isInGame;

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
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(StatusIcon)));
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(StatusForeground)));
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(StatusBackground)));
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(StatusBorderBrush)));
            }
        }

        public Brush StatusForeground => StatusColors().Item1;
        public Brush StatusBackground => StatusColors().Item2;
        public Brush StatusBorderBrush => StatusColors().Item3;
        public string StatusIcon => GetStatusIcon(status);

        private static string GetStatusIcon(string statusValue)
        {
            string value = statusValue ?? string.Empty;
            if (value.StartsWith("Thất bại", StringComparison.Ordinal)
                || value.StartsWith("Kiểm tra lỗi", StringComparison.Ordinal)
                || value.StartsWith("Tạm cách ly", StringComparison.Ordinal))
                return "!";
            if (value.StartsWith("Đang khôi phục", StringComparison.Ordinal)
                || value.StartsWith("Đang dừng", StringComparison.Ordinal))
                return "↻";
            if (value.StartsWith("Đang chờ", StringComparison.Ordinal)
                || value.StartsWith("Đang xếp hàng", StringComparison.Ordinal))
                return "◷";
            if (value.StartsWith("Đang chạy", StringComparison.Ordinal))
                return "▶";
            if (value.StartsWith("Đang kiểm tra", StringComparison.Ordinal)
                || value.StartsWith("Đang chuẩn bị", StringComparison.Ordinal))
                return "◎";
            if (value.StartsWith("Sẵn sàng", StringComparison.Ordinal)
                || value.StartsWith("Hoàn tất", StringComparison.Ordinal)
                || value.StartsWith("Đã tìm thấy đội", StringComparison.Ordinal))
                return "✓";
            if (value.StartsWith("Đang mở", StringComparison.Ordinal))
                return "●";
            if (value.StartsWith("Đã dừng", StringComparison.Ordinal)
                || value.StartsWith("Đã tắt", StringComparison.Ordinal))
                return "■";
            return "•";
        }

        private Tuple<Brush, Brush, Brush> StatusColors()
        {
            string value = status ?? string.Empty;
            if (value.StartsWith("Thất bại", StringComparison.Ordinal)
                || value.StartsWith("Tạm cách ly", StringComparison.Ordinal))
                return FailureColors;
            if (value.StartsWith("Đang khôi phục", StringComparison.Ordinal)
                || value.StartsWith("Đang dừng", StringComparison.Ordinal))
                return RecoveryColors;
            if (value.StartsWith("Đang chờ", StringComparison.Ordinal)
                || value.StartsWith("Đang xếp hàng", StringComparison.Ordinal))
                return WaitingColors;
            if (value.StartsWith("Đang chạy", StringComparison.Ordinal))
                return RunningColors;
            if (value.StartsWith("Sẵn sàng", StringComparison.Ordinal)
                || value.StartsWith("Hoàn tất", StringComparison.Ordinal)
                || value.StartsWith("Đã tìm thấy đội", StringComparison.Ordinal)
                || value.StartsWith("Đang mở", StringComparison.Ordinal))
                return ReadyColors;
            if (value.StartsWith("Đang kiểm tra", StringComparison.Ordinal)
                || value.StartsWith("Đang chuẩn bị", StringComparison.Ordinal))
                return CheckingColors;
            return NeutralColors;
        }

        private static Tuple<Brush, Brush, Brush> Colors(
            byte foregroundR, byte foregroundG, byte foregroundB,
            byte backgroundR, byte backgroundG, byte backgroundB,
            byte borderR, byte borderG, byte borderB)
        {
            Brush foreground = FrozenBrush(foregroundR, foregroundG, foregroundB);
            Brush background = FrozenBrush(backgroundR, backgroundG, backgroundB);
            Brush border = FrozenBrush(borderR, borderG, borderB);
            return Tuple.Create(foreground, background, border);
        }

        private static Brush FrozenBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }

        public bool IsRunning
        {
            get => isRunning;
            set
            {
                if (isRunning == value) return;
                isRunning = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
            }
        }

        public bool IsInGame
        {
            get => isInGame;
            set
            {
                if (isInGame == value) return;
                isInGame = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInGame)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal static class FarmProgressVietnamese
    {
        private static readonly IReadOnlyDictionary<string, string> Stages =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Queued"] = "Đang xếp hàng",
                ["Preflight"] = "Đang kiểm tra",
                ["PreflightFailed"] = "Kiểm tra lỗi",
                ["ReadyForGameplay"] = "Sẵn sàng",
                ["DispatchingTeam"] = "Đang điều đội",
                ["Requeued"] = "Đang chờ",
                ["Ready"] = "Sẵn sàng",
                ["Running"] = "Đang chạy",
                ["Waiting"] = "Đang chờ",
                ["Recovering"] = "Đang khôi phục",
                ["Quarantined"] = "Tạm cách ly",
                ["Stopped"] = "Đã dừng",
                ["CheckingTeamAvailability"] = "Đang kiểm tra đội",
                ["WaitingForReadyTeam"] = "Đang chờ đội",
                ["ReadyTeamFound"] = "Đã tìm thấy đội",
                ["PreparingFarm"] = "Đang chuẩn bị",
                ["RunningFarmStep"] = "Đang chạy",
                ["Stopping"] = "Đang dừng",
                ["Completed"] = "Hoàn tất",
                ["Failed"] = "Thất bại",
                ["Cancelled"] = "Đã hủy"
            };

        private static readonly IReadOnlyDictionary<string, string> Messages =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Screenshot capture recovered. A fresh preflight will run before retry."] =
                    "Đã khôi phục chụp màn hình. Hệ thống sẽ kiểm tra lại thiết bị trước khi thử tiếp.",
                ["Cycle failed; this device will retry independently."] =
                    "Chu kỳ chưa hoàn tất; thiết bị này sẽ tự thử lại.",
                ["Running the resource and level fallback plan."] =
                    "Đang tìm tài nguyên và thử các cấp phù hợp.",
                ["Level configuration failed."] =
                    "Không thể thiết lập cấp tài nguyên.",
                ["Checking WorldMap, screenshot, roster and eligible teams."] =
                    "Đang kiểm tra bản đồ, ảnh chụp và các đội có thể sử dụng.",
                ["Preparing the one-shot farm workflow."] =
                    "Đang chuẩn bị chu kỳ farm.",
                ["Checking the initial game state."] =
                    "Đang kiểm tra trạng thái ban đầu của trò chơi.",
                ["Ensuring World Map."] =
                    "Đang bảo đảm trò chơi ở bản đồ thế giới.",
                ["Opening resource search panel."] =
                    "Đang mở bảng tìm tài nguyên.",
                ["Searching the configured resource levels."] =
                    "Đang tìm theo các cấp tài nguyên đã cấu hình.",
                ["Verifying resource popup."] =
                    "Đang xác minh bảng thông tin tài nguyên.",
                ["Opening team selection."] =
                    "Đang mở màn hình chọn đội.",
                ["Selecting an eligible farm team."] =
                    "Đang chọn đội farm phù hợp.",
                ["No allowed team is ready; waiting before the next check."] =
                    "Chưa có đội được phép nào sẵn sàng; sẽ kiểm tra lại.",
                ["Watchdog detected no progress; starting recovery ladder."] =
                    "Không ghi nhận tiến triển; đang bắt đầu khôi phục thiết bị.",
                ["Cycle failed; starting recovery ladder."] =
                    "Chu kỳ gặp lỗi; đang bắt đầu khôi phục thiết bị.",
                ["Cycle completed; waiting for the next supervised cycle."] =
                    "Chu kỳ đã hoàn tất; đang chờ chu kỳ tiếp theo.",
                ["Continuous supervisor stopped."] =
                    "Luồng chạy liên tục đã dừng.",
                ["Continuous supervisor started."] =
                    "Đã bắt đầu luồng chạy liên tục.",
                ["Starting cycle"] = "Đang bắt đầu chu kỳ",
                ["Farm progress"] = "Đang thực hiện chu kỳ farm",
                ["Restart preflight"] = "Kiểm tra lại sau khi khởi động",
                ["Checkpoint restored; a fresh preflight is required before any input."] =
                    "Đã khôi phục trạng thái; cần kiểm tra lại thiết bị trước khi thao tác.",
                ["March verification was inconclusive; waiting for the next team availability check."] =
                    "Chưa xác minh được hành quân; đang chờ lần kiểm tra đội tiếp theo.",
                ["All selected resource storages are full; waiting 6 hours before checking again."] =
                    "Kho của các tài nguyên đã chọn đều đầy; sẽ kiểm tra lại sau 6 giờ.",
                ["Search areas were exhausted; waiting for the next cycle before selecting a new area."] =
                    "Đã tìm hết khu vực hiện tại; chu kỳ sau sẽ chọn khu vực mới.",
                ["No same-tone X/Y destination was found; waiting for the next scheduled cycle before trying new coordinates."] =
                    "Chưa tìm thấy điểm X/Y cùng tone màu; sẽ chờ tới chu kỳ kế tiếp rồi mới thử tọa độ mới.",
                ["Screenshot captured."] = "Đã chụp màn hình.",
                ["Screenshot was empty."] = "Ảnh chụp màn hình bị trống.",
                ["LDPlayer is running."] = "LDPlayer đang chạy.",
                ["LDPlayer is not running."] = "LDPlayer chưa chạy.",
                ["Game relaunch and screenshot preflight succeeded."] =
                    "Đã mở lại trò chơi và kiểm tra ảnh chụp thành công.",
                ["LDPlayer restart and screenshot preflight succeeded."] =
                    "Đã khởi động lại LDPlayer và kiểm tra ảnh chụp thành công.",
                ["Device recovery ladder was exhausted."] =
                    "Đã thử hết các bước khôi phục thiết bị nhưng chưa thành công."
            };

        public static string Stage(string value) =>
            Translate(Stages, value);

        public static string Message(string value)
        {
            string translated = Translate(Messages, value);
            if (!string.Equals(translated, value, StringComparison.Ordinal))
                return translated;
            if (value != null && value.StartsWith(
                "Starting supervised cycle ", StringComparison.Ordinal))
                return "Đang bắt đầu " + value.Replace(
                    "Starting supervised cycle ", "chu kỳ tự động ");
            if (value != null && value.StartsWith(
                "Checking allowed teams (attempt ", StringComparison.Ordinal))
                return value.Replace("Checking allowed teams (attempt ",
                    "Đang kiểm tra các đội được phép (lần ")
                    .Replace(").", ").");
            if (value != null && value.StartsWith(
                "Dispatching Team", StringComparison.Ordinal))
                return value.Replace("Dispatching Team", "Đang điều Đội ");
            return string.IsNullOrWhiteSpace(value) ? "-" : LooksLikeEnglishUserMessage(value)
                ? "Đã xảy ra lỗi trong quá trình xử lý." : value;
        }

        public static string TerritoryColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var fields = value.Split(';')
                .Select(part => part.Trim().Split(new[] { '=' }, 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1],
                    StringComparer.OrdinalIgnoreCase);
            fields.TryGetValue("Home", out string home);
            fields.TryGetValue("Destination", out string destination);
            fields.TryGetValue("Result", out string result);
            fields.TryGetValue("Candidate", out string candidate);
            fields.TryGetValue("X", out string x);
            fields.TryGetValue("Y", out string y);
            string comparison = string.Equals(result, "Match",
                    StringComparison.OrdinalIgnoreCase)
                ? "cùng tone"
                : string.Equals(result, "Different",
                    StringComparison.OrdinalIgnoreCase)
                    ? "khác tone"
                    : "chưa xác định";
            string location = string.IsNullOrWhiteSpace(x)
                || string.IsNullOrWhiteSpace(y)
                ? string.Empty
                : $" · X/Y: {x}/{y}";
            string prefix = string.IsNullOrWhiteSpace(candidate)
                ? "Màu vùng — Nhà:"
                : $"Màu vùng — Lần {candidate} · Nhà:";
            return $"{prefix} {ColorName(home)} · Điểm tài nguyên: "
                + $"{ColorName(destination)} · {comparison}"
                + location;
        }

        private static string ColorName(string value)
        {
            switch (value)
            {
                case "Green": return "xanh lá";
                case "Red": return "đỏ";
                case "Gold": return "vàng";
                case "Blue": return "xanh lam";
                case "Purple": return "tím";
                default: return "chưa rõ";
            }
        }

        public static string Step(string value)
        {
            switch (value)
            {
                case "Preflight": return "Kiểm tra ban đầu";
                case "EnsureWorldMap": return "Mở bản đồ thế giới";
                case "OpenSearchPanel": return "Mở bảng tìm tài nguyên";
                case "ConfigureSearch": return "Thiết lập tìm kiếm";
                case "ExecuteSearch": return "Thực hiện tìm kiếm";
                case "SearchWithLevelFallback": return "Thử các cấp tài nguyên";
                case "ResourceFarmFallback": return "Tìm tài nguyên và cấp phù hợp";
                case "VerifyResourcePopup": return "Xác minh tài nguyên";
                case "OpenTeamSelection": return "Mở chọn đội";
                case "SelectTeam": return "Chọn đội";
                case "DispatchTeam": return "Điều đội";
                case "FinalVerification": return "Xác minh kết quả";
                case "Completed": return "Hoàn tất";
                default: return Message(value);
            }
        }

        public static string Resource(string value)
        {
            switch (value)
            {
                case "Iron": return "Sắt";
                case "Stone": return "Đá";
                case "Wood": return "Gỗ";
                case "Food": return "Lương thực";
                default: return string.IsNullOrWhiteSpace(value) ? "-" : value;
            }
        }

        public static string Team(string value)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && value.StartsWith("Team", StringComparison.Ordinal))
                return "Đội " + value.Substring("Team".Length);
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string Translate(
            IReadOnlyDictionary<string, string> translations, string value) =>
            !string.IsNullOrWhiteSpace(value)
                && translations.TryGetValue(value, out string translated)
                    ? translated
                    : value;

        private static bool LooksLikeEnglishUserMessage(string value)
        {
            return new[] { " failed", "Failed", " error", "Error", "Waiting", "Checking",
                " started", "Started", " cancelled", "Cancelled", " returned", "returned",
                " no result", "No allowed", "Resource", "WorldMap", "Screenshot",
                "Timeout", "Exception" }.Any(word => value.IndexOf(word,
                    StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    internal sealed class DeviceFarmProgressItem : INotifyPropertyChanged
    {
        private static readonly TimeSpan ResourceToastDisplayDuration =
            TimeSpan.FromMinutes(2);
        private static readonly IReadOnlyDictionary<string, Brush> StageBrushes =
            CreateStageBrushes();

        private string stage = "Queued";
        private string message = "Đang chờ thực thi.";
        private string detail = "-";
        private string schedule = string.Empty;
        private string territoryColor = string.Empty;
        private string teamsSummary = string.Empty;
        private string resourceToastText = string.Empty;
        private string resourceToastVariant = string.Empty;
        private DateTimeOffset? resourceToastDetectedAt;
        private DateTimeOffset? resourceToastExpiresAt;
        private string resourceToastState = string.Empty;
        private string activeFarmRunId = string.Empty;
        private string activeTeamOperationRunId = string.Empty;
        private DateTimeOffset? nextCheckAt;
        private DateTimeOffset? waitDeadline;
        private bool rosterScanCompleted;
        private string synchronizedTeamRosterKey = string.Empty;

        public DeviceFarmProgressItem(string deviceName)
        {
            DeviceName = deviceName;
            Teams = new ObservableCollection<TeamFarmProgressItem>();
        }

        public string DeviceName { get; }
        public ObservableCollection<TeamFarmProgressItem> Teams { get; }
        public string Stage
        {
            get => stage;
            private set
            {
                if (string.Equals(stage, value, StringComparison.Ordinal)) return;
                stage = value;
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(Stage)));
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(StageDisplay)));
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(StageBrush)));
            }
        }
        public string StageDisplay => FarmProgressVietnamese.Stage(Stage);
        public Brush StageBrush => ResolveStageBrush(Stage);
        public string Message { get => message; private set => Set(ref message, value, nameof(Message)); }
        public string Detail { get => detail; private set => Set(ref detail, value, nameof(Detail)); }
        public string Schedule { get => schedule; private set => Set(ref schedule, value, nameof(Schedule)); }
        public string TerritoryColor { get => territoryColor; private set => Set(
            ref territoryColor, value, nameof(TerritoryColor)); }
        public string ResourceToastText
        {
            get => resourceToastText;
            private set
            {
                if (string.Equals(resourceToastText, value, StringComparison.Ordinal)) return;
                resourceToastText = value ?? string.Empty;
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(ResourceToastText)));
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(ResourceToastDisplayText)));
            }
        }
        public string ResourceToastDisplayText => string.IsNullOrWhiteSpace(ResourceToastText)
            ? string.Empty
            : $"Toast: \"{ResourceToastText}\"";
        public bool HasVisibleResourceToast =>
            !string.IsNullOrWhiteSpace(ResourceToastText);
        public string ResourceToastVariant { get => resourceToastVariant; private set => Set(
            ref resourceToastVariant, value, nameof(ResourceToastVariant)); }
        public DateTimeOffset? ResourceToastDetectedAt { get => resourceToastDetectedAt; private set => Set(
            ref resourceToastDetectedAt, value, nameof(ResourceToastDetectedAt)); }
        public string ResourceToastState { get => resourceToastState; private set => Set(
            ref resourceToastState, value, nameof(ResourceToastState)); }
        public string ActiveFarmRunId { get => activeFarmRunId; private set => Set(
            ref activeFarmRunId, value, nameof(ActiveFarmRunId)); }
        public string ActiveTeamOperationRunId { get => activeTeamOperationRunId; private set => Set(
            ref activeTeamOperationRunId, value, nameof(ActiveTeamOperationRunId)); }
        public string TeamsSummary { get => teamsSummary; private set => Set(
            ref teamsSummary, value, nameof(TeamsSummary)); }
        public bool IsWaiting => string.Equals(Stage,
            OneShotFarmProgressStage.WaitingForReadyTeam.ToString(), StringComparison.Ordinal);
        public bool IsActive => Stage == "Queued" || Stage == "Stopping"
            || string.Equals(Stage, OneShotFarmProgressStage.CheckingTeamAvailability.ToString(), StringComparison.Ordinal)
            || string.Equals(Stage, OneShotFarmProgressStage.WaitingForReadyTeam.ToString(), StringComparison.Ordinal)
            || string.Equals(Stage, OneShotFarmProgressStage.ReadyTeamFound.ToString(), StringComparison.Ordinal)
            || string.Equals(Stage, OneShotFarmProgressStage.PreparingFarm.ToString(), StringComparison.Ordinal)
            || string.Equals(Stage, OneShotFarmProgressStage.RunningFarmStep.ToString(), StringComparison.Ordinal);

        public void SetQueued()
        {
            Stage = "Queued";
            Message = "Đang chờ thực thi.";
            Detail = "-";
            Schedule = string.Empty;
            TerritoryColor = string.Empty;
            // Keep the last detected resource toast on this device card while
            // the supervisor queues or reschedules its next cycle. Queued and
            // supervisor snapshots do not carry toast payloads, so clearing it
            // here made a valid per-device toast disappear immediately.
            ActiveFarmRunId = string.Empty;
            ActiveTeamOperationRunId = string.Empty;
            TeamsSummary = string.Empty;
            Teams.Clear();
            synchronizedTeamRosterKey = string.Empty;
            rosterScanCompleted = false;
        }

        public void SetStopping()
        {
            Stage = "Stopping";
            Message = "Đang dừng an toàn...";
        }

        public void Apply(OneShotFarmProgress progress)
        {
            if (progress == null) return;
            if (!string.IsNullOrWhiteSpace(progress.FarmRunId))
            {
                if (!string.IsNullOrWhiteSpace(ActiveFarmRunId)
                    && !string.Equals(ActiveFarmRunId, progress.FarmRunId,
                        StringComparison.OrdinalIgnoreCase))
                    return;
                ActiveFarmRunId = progress.FarmRunId;
            }
            if (!string.IsNullOrWhiteSpace(progress.TeamOperationRunId))
                ActiveTeamOperationRunId = progress.TeamOperationRunId;
            Stage = progress.Stage.ToString();
            Message = FarmProgressVietnamese.Message(progress.Message);
            var details = new List<string>();
            if (progress.TeamAvailabilityChecks > 0)
                details.Add($"Kiểm tra đội: {progress.TeamAvailabilityChecks}");
            details.Add($"Bước: {FarmProgressVietnamese.Step(progress.CurrentStep?.ToString())}");
            details.Add("Tài nguyên: "
                + $"{FarmProgressVietnamese.Resource(progress.CurrentResource?.ToString())}"
                + (progress.CurrentLevel.HasValue
                    ? $" · cấp {progress.CurrentLevel.Value}" : string.Empty));
            Detail = string.Join(" · ", details);
            ApplyResourceToast(progress);
            if (progress.MapRepositionState != MapRepositionState.None
                && !string.IsNullOrWhiteSpace(progress.TerritoryColorSummary))
                TerritoryColor = FarmProgressVietnamese.TerritoryColor(
                    progress.TerritoryColorSummary);
            else
                TerritoryColor = string.Empty;
            nextCheckAt = progress.NextCheckAt;
            waitDeadline = progress.WaitDeadline;
            IReadOnlyList<TeamNumber> allowed = progress.AllowedTeams ?? new TeamNumber[0];
            IReadOnlyList<TeamNumber> detected = progress.DetectedTeams ?? new TeamNumber[0];
            IReadOnlyList<TeamNumber> ready = progress.ReadyTeams ?? new TeamNumber[0];
            IReadOnlyList<TeamNumber> busy = progress.BusyTeams ?? new TeamNumber[0];
            IReadOnlyList<TeamNumber> locked = progress.LockedTeams ?? new TeamNumber[0];
            IReadOnlyList<TeamNumber> eligible = progress.EligibleReadyTeams ?? new TeamNumber[0];
            bool isAvailabilityUpdate =
                progress.Stage == OneShotFarmProgressStage.CheckingTeamAvailability
                || progress.Stage == OneShotFarmProgressStage.WaitingForReadyTeam
                || progress.Stage == OneShotFarmProgressStage.ReadyTeamFound;
            bool rosterUncertain = isAvailabilityUpdate && detected.Count == 0
                && string.Equals(progress.RosterConfidence, "Uncertain",
                    StringComparison.OrdinalIgnoreCase);
            if (isAvailabilityUpdate)
            {
                // WaitingForReadyTeam is also emitted after a complete, confident
                // roster scan.  Treating only ReadyTeamFound as complete left the
                // last row at "Chưa rõ" when a four-team account had no ready team,
                // even though the availability result classified all four as busy.
                bool scanCompleted = progress.Stage == OneShotFarmProgressStage.ReadyTeamFound
                    || progress.Stage == OneShotFarmProgressStage.WaitingForReadyTeam;
                rosterScanCompleted = scanCompleted && !rosterUncertain && detected.Count > 0;
                // Keep the last confirmed rows visible while a new scan is in
                // progress. Clearing the collection here made a device such
                // as May_3 lose every team badge during the map step. Cached
                // rows are deliberately shown as unverified; they are never
                // relabeled as busy until a fresh scan confirms readiness.
                if (detected.Count > 0)
                    SynchronizeTeams(ExpandToConfirmedRoster(detected,
                        progress.ConfirmedRosterCount));
                else if (progress.ConfirmedRosterCount > 0)
                    SynchronizeTeams(ExpandToConfirmedRoster(
                        new TeamNumber[0], progress.ConfirmedRosterCount));
                foreach (TeamFarmProgressItem item in Teams)
                {
                    bool isAllowed = allowed.Contains(item.Team);
                    bool isReady = ready.Contains(item.Team);
                    bool isBusy = busy.Contains(item.Team);
                    bool isLocked = locked.Contains(item.Team);
                    bool isEligible = eligible.Contains(item.Team);
                    // Readiness is valid evidence even while a follow-up scan is
                    // being published.  The previous ordering overwrote every
                    // badge with "Chưa kiểm tra" during that window, hiding the
                    // statuses that had just been detected.
                    string status = isLocked ? "Khóa"
                        : isBusy ? "Bận"
                        : isEligible ? "Sẵn sàng"
                        : isReady && isAllowed ? "Sẵn sàng"
                        : isReady ? "Sẵn sàng · không chọn"
                        : isAvailabilityUpdate && (!scanCompleted || rosterUncertain)
                            ? (string.IsNullOrWhiteSpace(item.Status)
                                ? "Chưa kiểm tra" : item.Status)
                        : isAllowed ? "Bận"
                        : "Không dùng";
                    item.SetStatus(status, isEligible || isReady);
                }
            }
            else if (progress.CurrentSelectedTeam.HasValue
                || progress.CurrentExpectedTeam.HasValue
                || progress.CurrentTeam.HasValue)
            {
                TeamNumber activeTeam = progress.CurrentSelectedTeam
                    ?? progress.CurrentExpectedTeam ?? progress.CurrentTeam.Value;
                // Running-step progress intentionally carries only the active
                // team, not the full roster snapshot. Keep the header useful
                // while the next availability scan is pending by materializing
                // that team as an unverified badge instead of rendering none.
                EnsureTeamVisible(activeTeam);
                TeamFarmProgressItem current = Teams.FirstOrDefault(
                    item => item.Team == activeTeam);
                current?.SetStatus("Đang xử lý", true);
            }
            TeamsSummary = !isAvailabilityUpdate
                ? string.Join(" · ", Teams.Select(item =>
                    $"{item.TeamName}: {ShortTeamStatus(item.Status)}"))
                : rosterUncertain
                    ? "Chưa xác định đủ số lượng đội; hệ thống sẽ kiểm tra lại."
                    : progress.Stage != OneShotFarmProgressStage.ReadyTeamFound
                        ? "Đang quét đội..."
                        : string.Join(" · ", Teams.Select(item =>
                            $"{item.TeamName}: {ShortTeamStatus(item.Status)}"));
            UpdateCountdown(DateTimeOffset.UtcNow);
        }

        private static string BuildResourceToastText(OneShotFarmProgress progress)
        {
            if (progress == null
                || string.IsNullOrWhiteSpace(progress.ResourceToastVariant))
                return string.Empty;
            string resource = FarmProgressVietnamese.Resource(
                progress.CurrentResource?.ToString());
            bool isLv2Redirect = string.Equals(progress.ResourceToastVariant,
                "ResourceAreaLv2Redirect", StringComparison.Ordinal);
            if (progress.CurrentLevel.HasValue && progress.CurrentLevel.Value > 0)
            {
                string notFound = $"Không tìm thấy {resource} Lv{progress.CurrentLevel.Value} chưa ai khai thác";
                return isLv2Redirect
                    ? notFound + ", hãy đến khu tài nguyên Lv2 để tìm."
                    : notFound + ".";
            }
            return isLv2Redirect
                ? "Không tìm thấy tài nguyên chưa ai khai thác, hãy đến khu tài nguyên Lv2 để tìm."
                : "Không tìm thấy tài nguyên chưa ai khai thác.";
        }

        private void ApplyResourceToast(OneShotFarmProgress progress)
        {
            bool hasText = !string.IsNullOrWhiteSpace(progress.ResourceToastText);
            bool hasVariant = !string.IsNullOrWhiteSpace(progress.ResourceToastVariant);
            if (!hasText && !hasVariant) return;

            string replacement = hasVariant
                ? BuildResourceToastText(progress)
                : progress.ResourceToastText;
            if (string.IsNullOrWhiteSpace(replacement))
                replacement = progress.ResourceToastText;
            if (string.IsNullOrWhiteSpace(replacement)) return;

            // The expiry belongs to the currently displayed value. Replacing a
            // toast always creates a fresh two-minute display window, so an old
            // expiry can never clear a newer toast.
            resourceToastExpiresAt = DateTimeOffset.UtcNow
                .Add(ResourceToastDisplayDuration);
            ResourceToastVariant = progress.ResourceToastVariant ?? string.Empty;
            ResourceToastDetectedAt = progress.ResourceToastDetectedAt
                ?? DateTimeOffset.UtcNow;
            ResourceToastState = progress.ResourceToastState ?? string.Empty;
            ResourceToastText = replacement;
        }

        private void ExpireResourceToast(DateTimeOffset now)
        {
            if (!resourceToastExpiresAt.HasValue
                || now < resourceToastExpiresAt.Value) return;

            resourceToastExpiresAt = null;
            ResourceToastText = string.Empty;
            ResourceToastVariant = string.Empty;
            ResourceToastDetectedAt = null;
            ResourceToastState = string.Empty;
        }

        public void ApplySupervisorSnapshot(ContinuousFarmDeviceSnapshot snapshot)
        {
            if (snapshot == null) return;

            SynchronizeSnapshotTeams(snapshot);

            bool waitingForUncertainRoster = snapshot.State == ContinuousFarmDeviceState.Waiting
                && string.Equals(snapshot.RosterConfidence, "Uncertain",
                    StringComparison.OrdinalIgnoreCase)
                && !HasSnapshotRoster(snapshot);
            Stage = waitingForUncertainRoster
                ? OneShotFarmProgressStage.CheckingTeamAvailability.ToString()
                : snapshot.State == ContinuousFarmDeviceState.Waiting
                ? OneShotFarmProgressStage.WaitingForReadyTeam.ToString()
                : snapshot.State.ToString();
            Message = FarmProgressVietnamese.Message(snapshot.Message);
            string activeTeam = snapshot.CurrentSelectedTeam
                ?? snapshot.CurrentExpectedTeam ?? "-";
            Detail = $"Chu kỳ: {snapshot.CycleCount}; bước: "
                + $"{FarmProgressVietnamese.Message(snapshot.CurrentOperation)}; "
                + "tài nguyên/cấp/đội: "
                + $"{FarmProgressVietnamese.Resource(snapshot.CurrentResource)}/"
                + $"{snapshot.CurrentLevel?.ToString(CultureInfo.InvariantCulture) ?? "-"}/"
                + $"{FarmProgressVietnamese.Team(activeTeam)}";
            if (!string.IsNullOrWhiteSpace(snapshot.CurrentExpectedTeam))
                Detail += $" · đội dự kiến: {FarmProgressVietnamese.Team(snapshot.CurrentExpectedTeam)}";
            if (!string.IsNullOrWhiteSpace(snapshot.CurrentSelectedTeam))
                Detail += $" · đội đã chọn: {FarmProgressVietnamese.Team(snapshot.CurrentSelectedTeam)}";
            TerritoryColor = snapshot.MapRepositionState == MapRepositionState.None
                ? string.Empty : FarmProgressVietnamese.TerritoryColor(
                    snapshot.TerritoryColorSummary);
            nextCheckAt = snapshot.NextAttemptAt;
            waitDeadline = snapshot.NextAttemptAt;
            UpdateCountdown(DateTimeOffset.UtcNow);
        }

        private void SynchronizeSnapshotTeams(ContinuousFarmDeviceSnapshot snapshot)
        {
            TeamNumber? activeTeam = ParseTeam(snapshot.CurrentSelectedTeam)
                ?? ParseTeam(snapshot.CurrentExpectedTeam)
                ?? ParseTeam(snapshot.CurrentTeam);
            if (HasSnapshotRoster(snapshot))
            {
                IReadOnlyList<TeamNumber> detected = snapshot.DetectedTeams
                    ?? new TeamNumber[0];
                IReadOnlyList<TeamNumber> ready = snapshot.ReadyTeams
                    ?? new TeamNumber[0];
                IReadOnlyList<TeamNumber> busy = snapshot.BusyTeams
                    ?? new TeamNumber[0];
                IReadOnlyList<TeamNumber> locked = snapshot.LockedTeams
                    ?? new TeamNumber[0];
                TeamNumber[] knownTeams = detected.Concat(ready).Concat(busy)
                    .Concat(locked).Distinct().OrderBy(team => (int)team).ToArray();
                int snapshotKnownCount = Math.Max(snapshot.ConfirmedRosterCount,
                    knownTeams.Select(team => (int)team).DefaultIfEmpty(0).Max());
                SynchronizeTeams(ExpandToConfirmedRoster(knownTeams, snapshotKnownCount));
                foreach (TeamFarmProgressItem item in Teams)
                {
                    bool isLocked = locked.Contains(item.Team);
                    bool isBusy = busy.Contains(item.Team);
                    bool isReady = ready.Contains(item.Team);
                    if (isLocked) item.SetStatus("Khóa", false);
                    else if (isBusy) item.SetStatus("Bận", false);
                    else if (isReady) item.SetStatus("Sẵn sàng", true);
                    else if (detected.Contains(item.Team)) item.SetStatus("Bận", false);
                }
                TeamsSummary = string.Join(" · ", Teams.Select(item =>
                    $"{item.TeamName}: {ShortTeamStatus(item.Status)}"));
                return;
            }
            // A preflight snapshot may carry the last/default roster count.
            // Never render it as current until the fresh availability scan
            // has completed and published its progress result. Still retain a
            // visible active-team badge so a running device never loses its
            // status while the roster is being refreshed.
            if (!rosterScanCompleted)
            {
                if (snapshot.ConfirmedRosterCount > 0)
                    SynchronizeTeams(ExpandToConfirmedRoster(
                        Teams.Select(item => item.Team).ToArray(),
                        snapshot.ConfirmedRosterCount));
                if (activeTeam.HasValue)
                {
                    EnsureTeamVisible(activeTeam.Value);
                    TeamFarmProgressItem active = Teams.FirstOrDefault(
                        item => item.Team == activeTeam.Value);
                    active?.SetStatus("Đang xử lý", true);
                }
                TeamsSummary = "Đang quét đội...";
                return;
            }
            int knownCount = Math.Min(4, Math.Max(0, snapshot.ConfirmedRosterCount));
            if (activeTeam.HasValue)
                knownCount = Math.Max(knownCount, (int)activeTeam.Value);
            if (knownCount == 0) return;

            SynchronizeTeams(Enumerable.Range(1, knownCount)
                .Select(value => (TeamNumber)value).ToArray());
            foreach (TeamFarmProgressItem item in Teams)
            {
                if (activeTeam.HasValue && item.Team == activeTeam.Value)
                    item.SetStatus("Đang xử lý", true);
                else if (ParseTeam(snapshot.CurrentExpectedTeam) == item.Team)
                    item.SetStatus("Sẵn sàng", true);
                else if (string.IsNullOrWhiteSpace(item.Status)
                    || item.Status == "Đang xử lý")
                    item.SetStatus("Chưa rõ", false);
            }
            TeamsSummary = string.Join(" · ", Teams.Select(item =>
                $"{item.TeamName}: {ShortTeamStatus(item.Status)}"));
        }

        private static bool HasSnapshotRoster(ContinuousFarmDeviceSnapshot snapshot)
        {
            if (snapshot == null) return false;
            return (snapshot.DetectedTeams?.Length ?? 0) > 0
                || (snapshot.ReadyTeams?.Length ?? 0) > 0
                || (snapshot.BusyTeams?.Length ?? 0) > 0
                || (snapshot.LockedTeams?.Length ?? 0) > 0;
        }

        private static TeamNumber? ParseTeam(string value)
        {
            TeamNumber parsed;
            return Enum.TryParse(value, true, out parsed)
                && Enum.IsDefined(typeof(TeamNumber), parsed)
                ? parsed : (TeamNumber?)null;
        }

        private void SynchronizeTeams(IReadOnlyList<TeamNumber> visibleTeams)
        {
            TeamNumber[] ordered = visibleTeams.Distinct()
                .OrderBy(team => (int)team).ToArray();
            string rosterKey = string.Join(",", ordered.Select(team =>
                ((int)team).ToString(CultureInfo.InvariantCulture)));
            if (string.Equals(synchronizedTeamRosterKey, rosterKey,
                StringComparison.Ordinal)
                && Teams.Count == ordered.Length
                && Teams.Select(item => item.Team).SequenceEqual(ordered))
                return;
            foreach (TeamFarmProgressItem obsolete in Teams
                .Where(item => !ordered.Contains(item.Team)).ToArray())
                Teams.Remove(obsolete);
            for (int index = 0; index < ordered.Length; index++)
            {
                TeamFarmProgressItem existing = Teams
                    .FirstOrDefault(item => item.Team == ordered[index]);
                if (existing == null)
                    Teams.Insert(Math.Min(index, Teams.Count),
                        new TeamFarmProgressItem(ordered[index]));
                else
                {
                    int currentIndex = Teams.IndexOf(existing);
                    if (currentIndex != index) Teams.Move(currentIndex, index);
                }
            }
            synchronizedTeamRosterKey = rosterKey;
        }

        private static IReadOnlyList<TeamNumber> ExpandToConfirmedRoster(
            IReadOnlyList<TeamNumber> detectedTeams, int confirmedRosterCount)
        {
            TeamNumber[] detected = (detectedTeams ?? new TeamNumber[0])
                .Where(team => Enum.IsDefined(typeof(TeamNumber), team))
                .Distinct().ToArray();
            int highestDetected = detected.Select(team => (int)team)
                .DefaultIfEmpty(0).Max();
            int count = Math.Min(4, Math.Max(highestDetected,
                Math.Max(0, confirmedRosterCount)));
            if (count == 0) return detected;
            return Enumerable.Range(1, count).Select(value => (TeamNumber)value)
                .Union(detected).OrderBy(team => (int)team).ToArray();
        }

        private void EnsureTeamVisible(TeamNumber team)
        {
            if (Teams.Any(item => item.Team == team)) return;
            int insertIndex = Teams.Count == 0
                ? 0
                : Teams.TakeWhile(item => (int)item.Team < (int)team).Count();
            Teams.Insert(Math.Min(insertIndex, Teams.Count),
                new TeamFarmProgressItem(team));
            synchronizedTeamRosterKey = string.Empty;
        }

        public void UpdateCountdown(DateTimeOffset now)
        {
            ExpireResourceToast(now);
            TimeSpan next = OneShotFarmProgressUtilities.Remaining(now, nextCheckAt);
            TimeSpan wait = OneShotFarmProgressUtilities.Remaining(now, waitDeadline);
            Schedule = nextCheckAt.HasValue
                ? $"Kiểm tra tiếp: {nextCheckAt.Value.ToLocalTime():HH:mm:ss} · "
                    + (next == TimeSpan.Zero ? "đến thời điểm kiểm tra"
                        : $"còn {FormatRemaining(next)} · thời gian chờ {FormatDuration(wait)}")
                : string.Empty;
        }

        private static string FormatRemaining(TimeSpan value) =>
            $"{Math.Max(0, (int)value.TotalHours * 60 + value.Minutes):00}:{value.Seconds:00}";

        private static string FormatDuration(TimeSpan value) =>
            $"{Math.Max(0, (int)value.TotalHours):00}:{value.Minutes:00}:{value.Seconds:00}";

        private static string ShortTeamStatus(string value)
        {
            if (value == "Được phép · bận/chưa xác minh") return "đang bận";
            if (value == "Sẵn sàng · không được phép") return "sẵn sàng, không dùng";
            if (value == "Không được phép") return "không dùng";
            return string.IsNullOrWhiteSpace(value) ? "chưa rõ" : value.ToLowerInvariant();
        }

        private static Brush ResolveStageBrush(string value)
        {
            Brush brush;
            return StageBrushes.TryGetValue(value ?? string.Empty, out brush)
                ? brush : StageBrushes["Queued"];
        }

        private static IReadOnlyDictionary<string, Brush> CreateStageBrushes()
        {
            Brush slate = CreateFrozenBrush(71, 85, 105);
            Brush blue = CreateFrozenBrush(37, 99, 235);
            Brush green = CreateFrozenBrush(5, 150, 105);
            Brush amber = CreateFrozenBrush(180, 83, 9);
            Brush stopped = CreateFrozenBrush(100, 116, 139);
            return new Dictionary<string, Brush>(StringComparer.Ordinal)
            {
                ["Queued"] = slate,
                ["Preflight"] = CreateFrozenBrush(3, 105, 161),
                ["Ready"] = green,
                ["Running"] = blue,
                ["Waiting"] = amber,
                ["Recovering"] = CreateFrozenBrush(234, 88, 12),
                ["Quarantined"] = CreateFrozenBrush(185, 28, 28),
                ["Stopped"] = stopped,
                ["Stopping"] = CreateFrozenBrush(194, 65, 12),
                ["CheckingTeamAvailability"] = CreateFrozenBrush(2, 132, 199),
                ["WaitingForReadyTeam"] = amber,
                ["ReadyTeamFound"] = green,
                ["PreparingFarm"] = CreateFrozenBrush(79, 70, 229),
                ["RunningFarmStep"] = blue,
                ["Completed"] = CreateFrozenBrush(21, 128, 61),
                ["Failed"] = CreateFrozenBrush(220, 38, 38),
                ["Cancelled"] = stopped
            };
        }

        private static Brush CreateFrozenBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }

        private void Set(ref string field, string value, string propertyName)
        {
            if (string.Equals(field, value, StringComparison.Ordinal)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Set<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class TeamFarmProgressItem : INotifyPropertyChanged
    {
        private string status = "Chưa kiểm tra";
        private Brush statusBrush = Brushes.SlateGray;

        public TeamFarmProgressItem(TeamNumber team)
        {
            Team = team;
        }

        public TeamNumber Team { get; }
        public string TeamName => FarmProgressVietnamese.Team(Team.ToString());
        public string Status
        {
            get => status;
            private set
            {
                if (status == value) return;
                status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }
        public Brush StatusBrush
        {
            get => statusBrush;
            private set
            {
                if (Equals(statusBrush, value)) return;
                statusBrush = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
            }
        }

        public void SetStatus(string value, bool positive)
        {
            Status = value;
            StatusBrush = positive ? Brushes.SeaGreen
                : string.Equals(value, "Bận", StringComparison.Ordinal)
                    ? Brushes.DarkGoldenrod : Brushes.SlateGray;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
