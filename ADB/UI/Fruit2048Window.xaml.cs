using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics;

namespace ADB_Tool_Automation_Post_FB.UI
{
    public partial class Fruit2048Window : Window
    {
        private readonly ILdPlayerClient playerClient;
        private readonly IFruit2048AutomationService automation;
        private readonly IDeviceAutomationOwnershipService ownership;
        private readonly IFruitTileLearningCatalog learningCatalog;
        private readonly Fruit2048TemplateCatalog templateCatalog;
        private readonly IFruit2048SeedCalibrationService calibration;
        private readonly IFruit2048LearningCoordinator learningCoordinator;
        private readonly Fruit2048LearningDiagnosticStore learningDiagnosticStore;
        private readonly ObservableCollection<Fruit2048DeviceItem> devices =
            new ObservableCollection<Fruit2048DeviceItem>();
        private readonly DispatcherTimer refreshTimer;
        private readonly CancellationTokenSource windowLifetime = new CancellationTokenSource();
        private bool refreshInProgress;
        private bool closingAfterCancellation;
        private bool windowCleanedUp;

        public Fruit2048Window(Fruit2048Feature feature)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            InitializeComponent();
            playerClient = feature.PlayerClient;
            automation = feature.AutomationService;
            ownership = feature.OwnershipService;
            learningCatalog = feature.LearningCatalog;
            templateCatalog = feature.TemplateCatalog;
            calibration = feature.CalibrationService;
            learningCoordinator = feature.LearningCoordinator;
            learningDiagnosticStore = feature.LearningDiagnosticStore;
            DeviceList.ItemsSource = devices;
            BoardItems.ItemsSource = Enumerable.Repeat("?", 16).ToArray();
            ownership.OwnershipChanged += Ownership_OwnershipChanged;
            // Device enumeration calls into the LDPlayer bridge synchronously.
            // A short polling period makes this window compete with screenshot
            // capture and needlessly re-render every device row.
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            refreshTimer.Tick += RefreshTimer_Tick;
            Loaded += async (sender, args) => { await RefreshDevicesAsync(); refreshTimer.Start(); };
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e) => await RefreshDevicesAsync();

        private async Task RefreshDevicesAsync()
        {
            if (refreshInProgress) return;
            refreshInProgress = true;
            try
            {
                IReadOnlyList<string> names = await playerClient.GetDeviceNamesAsync(windowLifetime.Token);
                var discovered = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                foreach (Fruit2048DeviceItem existing in devices.Where(item =>
                    !discovered.Contains(item.DeviceName)))
                {
                    existing.IsConnected = false;
                }
                foreach (string name in names.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    Fruit2048DeviceItem item = devices.FirstOrDefault(value =>
                        string.Equals(value.DeviceName, name, StringComparison.OrdinalIgnoreCase));
                    if (item == null)
                    {
                        item = new Fruit2048DeviceItem { DeviceName = name };
                        devices.Add(item);
                    }
                    // The automation owns an active device while a Fruit session is running.
                    // Polling the LDPlayer bridge from this UI timer competes with screenshot
                    // capture, so retain the last connection state until the run itself reports
                    // a capture/device failure.
                    if (!item.IsRunning)
                        item.IsConnected = await playerClient.IsRunningAsync(name, windowLifetime.Token);
                    item.Owner = ownership.GetOwner(name);
                }
            }
            catch { }
            finally { refreshInProgress = false; UpdateDetailButtons(); }
        }

        private void Ownership_OwnershipChanged(object sender,
            DeviceAutomationOwnershipChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Fruit2048DeviceItem item = Find(e.DeviceName);
                if (item == null) return;
                item.Owner = e.Owner;
                item.RefreshPresentation();
                UpdateDetailButtons();
            }));
        }

        private async void BulkAction_Click(object sender, RoutedEventArgs e)
        {
            Fruit2048DeviceItem[] selected = devices.Where(item => item.IsSelected).ToArray();
            Fruit2048DeviceItem[] running = selected.Where(item => item.IsRunning).ToArray();
            if (running.Length > 0)
            {
                foreach (Fruit2048DeviceItem item in running) item.Cancellation?.Cancel();
                UpdateDetailButtons();
                return;
            }

            Fruit2048DeviceItem[] availableSelected = selected.Where(item => item.CanSelect).ToArray();
            if (availableSelected.Length > 0)
            {
                foreach (Fruit2048DeviceItem item in availableSelected) StartDevice(item);
                await RefreshDevicesAsync();
                return;
            }

            foreach (Fruit2048DeviceItem item in devices.Where(item => item.CanSelect))
                item.IsSelected = true;
            UpdateDetailButtons();
        }

        private void DeviceSelectionChanged(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateDetailButtons));
        }

        private void StartOne_Click(object sender, RoutedEventArgs e)
        {
            Fruit2048DeviceItem item = DeviceList.SelectedItem as Fruit2048DeviceItem;
            if (item != null && item.CanSelect) StartDevice(item);
        }

        private void StopOne_Click(object sender, RoutedEventArgs e)
        {
            (DeviceList.SelectedItem as Fruit2048DeviceItem)?.Cancellation?.Cancel();
        }

        private void StartDevice(Fruit2048DeviceItem item)
        {
            if (item == null || !item.CanSelect || item.IsRunning) return;
            Fruit2048RunMode mode = SelectedRunMode();
            if ((mode == Fruit2048RunMode.BurnIn || mode == Fruit2048RunMode.FirstLearningProof)
                && !string.Equals(learningCoordinator.Teacher?.DeviceName,
                item.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                item.LearningMessage = mode == Fruit2048RunMode.FirstLearningProof
                    ? "FirstLearningProof chỉ chạy trên thiết bị học đã chọn."
                    : "Burn-in chỉ chạy trên thiết bị học đã chọn.";
                UpdateDetail(item);
                return;
            }
            item.Cancellation = new CancellationTokenSource();
            item.IsRunning = true;
            item.Status = Fruit2048RuntimeStatus.Starting;
            item.LearningMessage = string.Empty;
            item.UnknownCellCount = 0;
            item.FirstLearningProof = null;
            item.RefreshPresentation();
            int target = int.Parse(((ComboBoxItem)TargetCombo.SelectedItem).Content.ToString());
            var request = new Fruit2048RunRequest
            {
                TargetTile = target,
                AutoRefresh = AutoRefreshCheck.IsChecked == true,
                TeacherMoveLimit = ParseTeacherMoveLimit(),
                Mode = mode
            };
            var progress = new Progress<Fruit2048Progress>(value => ApplyProgress(item, value));
            item.RunningTask = RunDeviceAsync(item, request, progress, item.Cancellation.Token);
        }

        private async Task RunDeviceAsync(Fruit2048DeviceItem item, Fruit2048RunRequest request,
            IProgress<Fruit2048Progress> progress, CancellationToken token)
        {
            try
            {
                Fruit2048RunResult result = await automation.RunAsync(item.DeviceName, request, progress, token);
                await Dispatcher.InvokeAsync(() =>
                {
                    item.LastError = result.Outcome == Fruit2048Outcome.MoveLimitReached ? null : result.Error;
                    if (result.Outcome == Fruit2048Outcome.TargetReached) item.Status = Fruit2048RuntimeStatus.TargetReached;
                    else if (result.Outcome == Fruit2048Outcome.LearningProofCompleted
                        || result.Outcome == Fruit2048Outcome.MoveLimitReached)
                    {
                        item.Status = Fruit2048RuntimeStatus.Completed;
                        if (result.Outcome == Fruit2048Outcome.MoveLimitReached)
                            item.LearningMessage = result.Error;
                    }
                    else if (result.Outcome == Fruit2048Outcome.NoMoves) item.Status = Fruit2048RuntimeStatus.NoMoves;
                    else if (result.Outcome == Fruit2048Outcome.Cancelled) item.Status = Fruit2048RuntimeStatus.Cancelled;
                    else if (result.Outcome == Fruit2048Outcome.Disconnected
                        || result.Outcome == Fruit2048Outcome.DeviceUnavailable)
                    {
                        item.Status = Fruit2048RuntimeStatus.Disconnected;
                        item.IsConnected = false;
                    }
                    else if (result.Outcome == Fruit2048Outcome.MissingSeedAssets) item.Status = Fruit2048RuntimeStatus.MissingSeeds;
                    else if (result.Outcome == Fruit2048Outcome.NeedsFreshBoard) item.Status = Fruit2048RuntimeStatus.NeedsFreshBoard;
                    else if (result.Outcome == Fruit2048Outcome.BoardReadFailed && item.UnknownCellCount > 0)
                        item.Status = Fruit2048RuntimeStatus.BoardUnknown;
                    else item.Status = Fruit2048RuntimeStatus.Failed;
                });
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    item.IsRunning = false;
                    item.Cancellation?.Dispose();
                    item.Cancellation = null;
                    item.RunningTask = null;
                    item.Owner = ownership.GetOwner(item.DeviceName);
                    item.RefreshPresentation();
                    UpdateDetail(item);
                });
            }
        }

        private int ParseTeacherMoveLimit()
        {
            ComboBoxItem selected = TeacherMoveLimitCombo.SelectedItem as ComboBoxItem;
            int limit;
            return selected != null && int.TryParse(selected.Content?.ToString(), out limit) ? limit : 0;
        }

        private void AdvancedToolsExpander_Expanded(object sender, RoutedEventArgs e)
        {
            AdvancedSettingsPanel.Visibility = Visibility.Visible;
        }

        private void AdvancedToolsExpander_Collapsed(object sender, RoutedEventArgs e)
        {
            AdvancedSettingsPanel.Visibility = Visibility.Collapsed;
        }

        private void ApplyProgress(Fruit2048DeviceItem item, Fruit2048Progress progress)
        {
            if (!Dispatcher.CheckAccess())
            { Dispatcher.BeginInvoke(new Action(() => ApplyProgress(item, progress))); return; }
            item.Status = progress.Status;
            bool validatedTransition = progress.TransitionStatus == Fruit2048TransitionValidationStatus.Valid
                || progress.TransitionStatus == Fruit2048TransitionValidationStatus.ValidWithSpawn;
            bool terminalStatus = progress.Status == Fruit2048RuntimeStatus.Failed
                || progress.Status == Fruit2048RuntimeStatus.Completed
                || progress.Status == Fruit2048RuntimeStatus.Cancelled
                || progress.Status == Fruit2048RuntimeStatus.Disconnected
                || progress.Status == Fruit2048RuntimeStatus.Paused;
            // Status-only progress (for example, "Đang đọc bàn") has no board.
            // Keep the last verified 4x4 board visible instead of replacing it
            // with sixteen question marks while the next screenshot is captured.
            // Auto deliberately redraws the 4x4 only after a validated move (or
            // after the session ends). Intermediate animation/retry reads do not
            // need a WPF board render and otherwise create needless UI work.
            if (progress.Board != null && (!item.IsRunning || validatedTransition || terminalStatus))
                item.Board = progress.Board;
            if (progress.Cells != null && (!item.IsRunning || validatedTransition || terminalStatus))
            {
                item.HasBoardRead = true;
                item.ObservedCellCount = progress.Cells.Count;
            }
            item.HighestTile = progress.HighestTile;
            item.MoveCount = progress.MoveCount;
            item.LastMove = progress.LastMove;
            item.LastError = progress.Error;
            item.RecognitionMode = progress.RecognitionMode;
            item.KnownTierCount = progress.KnownTierCount;
            item.HighestObservedTier = progress.HighestObservedTier;
            item.LearningProfiles = progress.LearningProfiles;
            item.BootstrapState = progress.BootstrapState;
            item.UnknownCellCount = progress.UnknownCellCount;
            if (progress.FirstLearningProof != null)
                item.FirstLearningProof = progress.FirstLearningProof;
            item.BadgeBootstrapCells = progress.Cells == null ? 0 : progress.Cells.Count(cell =>
                cell.RecognitionSource == Fruit2048RecognitionSource.TierBadgeBootstrap);
            item.FastPathCells = progress.Cells == null ? 0 : progress.Cells.Count(cell =>
                cell.RecognitionSource == Fruit2048RecognitionSource.FastFingerprint);
            if (!string.IsNullOrWhiteSpace(progress.LearningMessage))
                item.LearningMessage = progress.LearningMessage;
            item.RefreshPresentation();
            // During Auto, the visible summary changes only when a transition is
            // settled, a terminal state is reached, or the selected move changes.
            // This keeps capture/recognition off the UI dispatcher hot path.
            if (DeviceList.SelectedItem == item && (!item.IsRunning || validatedTransition
                || terminalStatus || progress.Status == Fruit2048RuntimeStatus.WaitingPostMove))
                UpdateDetail(item);
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            Fruit2048DeviceItem item = DeviceList.SelectedItem as Fruit2048DeviceItem;
            if (item == null || item.IsRunning || item.Owner == DeviceAutomationOwner.Farm) return;
            Fruit2048BoardReadResult read = await automation.ScanAsync(item.DeviceName, windowLifetime.Token);
            ApplyRead(item, read);
        }

        private async void Calibration_Click(object sender, RoutedEventArgs e)
        {
            Fruit2048DeviceItem item = DeviceList.SelectedItem as Fruit2048DeviceItem;
            if (item == null || !item.IsConnected || item.IsRunning || item.Owner != DeviceAutomationOwner.None) return;
            var dialog = new Fruit2048CalibrationWindow(calibration, item.DeviceName, windowLifetime.Token)
            { Owner = this };
            if (dialog.ShowDialog() != true) return;
            item.BootstrapState = templateCatalog.SeedAvailability.IsReady
                ? Fruit2048BootstrapState.ReadyToLearn : Fruit2048BootstrapState.MissingSeedAssets;
            item.LearningMessage = "Hiệu chỉnh hoàn tất. Fruit2048 đã sẵn sàng tự học.";
            item.RefreshPresentation();
            UpdateDetail(item);
            await RefreshDevicesAsync();
        }

        private async void ExportBoardInspection_Click(object sender, RoutedEventArgs e)
        {
            Fruit2048DeviceItem item = DeviceList.SelectedItem as Fruit2048DeviceItem;
            if (item == null || !item.IsConnected || item.IsRunning || item.Owner != DeviceAutomationOwner.None) return;
            Fruit2048CalibrationCapture capture = await calibration.CaptureAsync(item.DeviceName, windowLifetime.Token);
            if (!capture.Success)
            {
                MessageBox.Show(this, capture.Error ?? "Không thể chụp board để kiểm tra.", "Fruit 2048", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string directory = learningDiagnosticStore.ExportBoardInspection(item.DeviceName, capture.ScreenshotPng,
                capture.BoardBounds);
            MessageBox.Show(this, "Đã xuất kiểm tra Board:\n" + directory, "Fruit 2048", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task ManualMoveAsync(Fruit2048Move move)
        {
            Fruit2048DeviceItem item = DeviceList.SelectedItem as Fruit2048DeviceItem;
            if (item == null || item.IsRunning || item.Owner == DeviceAutomationOwner.Farm) return;
            Fruit2048BoardReadResult read = await automation.SendManualMoveAsync(
                item.DeviceName, move, windowLifetime.Token);
            item.LastMove = move;
            ApplyRead(item, read);
        }

        private void ApplyRead(Fruit2048DeviceItem item, Fruit2048BoardReadResult read)
        {
            if (read == null) return;
            item.Board = read.Board;
            if (read.Cells != null)
            {
                item.HasBoardRead = true;
                item.ObservedCellCount = read.Cells.Count;
            }
            item.HighestTile = read.HighestTile;
            item.LastError = read.Error;
            if (read.Learning != null)
            {
                item.RecognitionMode = read.Learning.Mode;
                item.KnownTierCount = read.Learning.KnownTierCount;
                item.HighestObservedTier = read.Learning.HighestObservedTier;
                item.LearningProfiles = read.Learning.Profiles;
            }
            item.BootstrapState = read.BootstrapState;
            item.UnknownCellCount = read.UnknownCells?.Count ?? 0;
            item.BadgeBootstrapCells = read.BadgeBootstrapCells;
            item.FastPathCells = read.FastPathCells;
            item.EmptyCellCount = read.EmptyCells;
            item.Tier1CellCount = read.Tier1Cells;
            item.Status = read.Success ? Fruit2048RuntimeStatus.Idle : Fruit2048RuntimeStatus.BoardUnknown;
            item.RefreshPresentation();
            UpdateDetail(item);
        }

        private async void ManualUp_Click(object sender, RoutedEventArgs e) => await ManualMoveAsync(Fruit2048Move.Up);
        private async void ManualDown_Click(object sender, RoutedEventArgs e) => await ManualMoveAsync(Fruit2048Move.Down);
        private async void ManualLeft_Click(object sender, RoutedEventArgs e) => await ManualMoveAsync(Fruit2048Move.Left);
        private async void ManualRight_Click(object sender, RoutedEventArgs e) => await ManualMoveAsync(Fruit2048Move.Right);

        private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetail(DeviceList.SelectedItem as Fruit2048DeviceItem);
        }

        private void UpdateDetail(Fruit2048DeviceItem item)
        {
            SelectedDeviceText.Text = item?.DeviceName ?? "Chưa chọn thiết bị";
            BoardPanel.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
            bool automaticRun = item != null && item.IsRunning;
            BoardTitleText.Visibility = automaticRun ? Visibility.Collapsed : Visibility.Visible;
            BoardItems.Visibility = automaticRun ? Visibility.Collapsed : Visibility.Visible;
            BoardStatsPanel.Visibility = automaticRun ? Visibility.Collapsed : Visibility.Visible;
            BoardAutoSummaryPanel.Visibility = automaticRun ? Visibility.Visible : Visibility.Collapsed;
            if (automaticRun)
            {
                // Rendering sixteen changing cell controls for every progress callback
                // adds UI work but provides no control input while Auto owns the device.
                AutoBoardSummaryText.Text = "Đang xác minh nước đi " + item.MoveCount
                    + " · Hướng vừa chọn: " + item.LastMoveText + ".";
            }
            else
            {
                int[] values = item?.Board?.Values.ToArray();
                BoardItems.ItemsSource = values == null
                    ? Enumerable.Repeat("?", 16).ToArray()
                    : values.Select(value => value.ToString()).ToArray();
                HighestTileText.Text = (item?.HighestTile ?? 0).ToString();
                MoveSummaryText.Text = (item?.MoveCount ?? 0) + " · " + (item?.LastMoveText ?? "—");
            }
            LearningSummaryText.Text = "Nhận diện: " + (item?.RecognitionMode.ToString() ?? "Learning")
                + " · Đã học " + (item?.KnownTierCount ?? 0) + "/" + (item?.HighestObservedTier ?? 0);
            BootstrapText.Text = "Bootstrap: " + BootstrapLabel(item?.BootstrapState
                ?? Fruit2048BootstrapState.MissingSeedAssets);
            HighestObservedText.Text = "Tier cao nhất đã biết: " + (item?.HighestObservedTier ?? 0)
                + " · Unknown: " + (item?.UnknownCellCount ?? 0);
            if (item == null || !item.HasBoardRead)
            {
                BoardDiagnosticsText.Text = "Board: Chưa quét · Empty: - · Tier1: - · Unknown: - · Geometry: Chưa kiểm tra";
                HighestTileText.Text = "-";
            }
            else
            {
                int unknownCells = item.UnknownCellCount;
                int knownCells = Math.Max(0, item.ObservedCellCount - unknownCells);
                string geometry = item.ObservedCellCount == 16 ? "OK" : "Không đầy đủ";
                BoardDiagnosticsText.Text = "Board: " + knownCells + "/" + item.ObservedCellCount
                    + " · Empty: " + item.EmptyCellCount
                    + " · Tier1: " + item.Tier1CellCount
                    + " · Unknown: " + unknownCells + " · Geometry: " + geometry;
            }
            LearningMessageText.Text = !string.IsNullOrWhiteSpace(item?.LearningMessage)
                ? item.LearningMessage : NavigationFailureText(item?.LastError);
            ProofStatusText.Text = FormatFirstLearningProof(item?.FirstLearningProof);
            LearningTierItems.ItemsSource = FormatLearningProfiles(item?.LearningProfiles);
            UpdateDetailButtons();
        }

        private static string BootstrapLabel(Fruit2048BootstrapState state)
        {
            switch (state)
            {
                case Fruit2048BootstrapState.Ready: return "Ready";
                case Fruit2048BootstrapState.ReadyToLearn: return "Ready to learn";
                case Fruit2048BootstrapState.NeedsFreshBoard: return "Needs fresh board";
                case Fruit2048BootstrapState.Learning: return "Learning";
                default: return "Missing seeds";
            }
        }

        private static string NavigationFailureText(string reason)
        {
            if (string.Equals(reason, "NavigationAnchorsNotDetected", StringComparison.OrdinalIgnoreCase))
                return "Không nhận diện được anchor điều hướng.";
            if (!string.IsNullOrWhiteSpace(reason) && reason.StartsWith("NavigationTemplateInvalid", StringComparison.OrdinalIgnoreCase))
                return "Mẫu điều hướng Fruit2048 không hợp lệ.";
            if (string.Equals(reason, "FruitTabTappedButBoardNotDetected", StringComparison.OrdinalIgnoreCase)
                || string.Equals(reason, "CityEntryTappedButFruitTabNotDetected", StringComparison.OrdinalIgnoreCase))
                return "Không nhận diện được màn hình game sau khi điều hướng.";
            return string.Empty;
        }

        private Fruit2048RunMode SelectedRunMode()
        {
            switch (RunModeCombo.SelectedIndex)
            {
                case 1: return Fruit2048RunMode.BurnIn;
                case 2: return Fruit2048RunMode.FirstLearningProof;
                default: return Fruit2048RunMode.Normal;
            }
        }

        private static string FormatFirstLearningProof(Fruit2048FirstLearningProofProgress proof)
        {
            if (proof == null) return string.Empty;
            return "TỰ HỌC BAN ĐẦU · Board " + proof.InitialKnownCells + "/16 · Tier 2: "
                + (proof.Tier2Learned ? "Learned" : "Candidate") + " "
                + proof.Tier2SampleCount + "/" + proof.RequiredSamples + " · Nước: "
                + proof.MovesUsed + "/" + proof.MaxMoves + " · Catalog v" + proof.CatalogVersion
                + " · Nhận diện Tier 2: " + (proof.Tier2RecognitionConfirmed ? "Đã xác nhận" : "Chưa xác nhận")
                + (proof.Failure == Fruit2048LearningProofFailure.None ? string.Empty : " · Lỗi: " + proof.Failure);
        }

        private async void ResetLearning_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this,
                "Dừng Fruit 2048 và xóa toàn bộ dữ liệu học? Các seed tĩnh sẽ được giữ lại.",
                "Xóa dữ liệu học", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
            await StopAllFruitAsync();
            learningCatalog.ResetLearningData();
            Fruit2048LearningSnapshot snapshot = learningCatalog.Snapshot;
            foreach (Fruit2048DeviceItem item in devices)
            {
                item.RecognitionMode = snapshot.Mode;
                item.KnownTierCount = snapshot.KnownTierCount;
                item.HighestObservedTier = snapshot.HighestObservedTier;
                item.LearningProfiles = snapshot.Profiles;
                item.LearningMessage = "Đã xóa dữ liệu học runtime.";
                item.BootstrapState = templateCatalog.SeedAvailability.IsReady
                    ? Fruit2048BootstrapState.ReadyToLearn
                    : Fruit2048BootstrapState.MissingSeedAssets;
            }
            UpdateDetail(DeviceList.SelectedItem as Fruit2048DeviceItem);
        }

        private void ExportLearning_Click(object sender, RoutedEventArgs e)
        {
            string root = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments), "IKAutomation", "Fruit2048Exports",
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string exported = learningCatalog.ExportLearningDiagnostics(root);
            MessageBox.Show(this, "Đã xuất dữ liệu học:\n" + exported,
                "Fruit 2048", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportLastFailure_Click(object sender, RoutedEventArgs e)
        {
            Fruit2048DeviceItem item = DeviceList.SelectedItem as Fruit2048DeviceItem;
            string folder = item == null ? null : learningDiagnosticStore?.GetLatestForDevice(item.DeviceName);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "Chưa có lỗi Fruit2048 được lưu cho thiết bị này.", "Fruit 2048",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }

        private void SetTeacher_Click(object sender, RoutedEventArgs e)
        {
            Fruit2048DeviceItem item = DeviceList.SelectedItem as Fruit2048DeviceItem;
            if (item == null || item.Owner == DeviceAutomationOwner.Farm || learningCoordinator == null) return;
            string reason;
            if (!learningCoordinator.SelectTeacher(item.DeviceName, out reason))
            { MessageBox.Show(this, reason, "Fruit2048", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            item.LearningMessage = "Thiết bị học"; item.RefreshPresentation(); UpdateDetail(item);
        }

        private void DeleteCalibratedSeeds_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, "Xóa hai mẫu Empty/Tier 1 đã hiệu chỉnh? Dữ liệu học sẽ được giữ theo seed còn khả dụng.",
                "Xóa mẫu hiệu chỉnh", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            calibration.DeleteCalibratedSeeds();
            foreach (Fruit2048DeviceItem item in devices)
            {
                item.BootstrapState = templateCatalog.SeedAvailability.IsReady
                    ? Fruit2048BootstrapState.ReadyToLearn : Fruit2048BootstrapState.MissingSeedAssets;
                item.LearningMessage = templateCatalog.SeedAvailability.IsReady
                    ? "Đã xóa mẫu hiệu chỉnh; đang dùng mẫu đóng gói." : "Thiếu mẫu Empty/Tier 1 — hãy chạy Hiệu chỉnh nhận diện.";
                item.RefreshPresentation();
            }
            UpdateDetail(DeviceList.SelectedItem as Fruit2048DeviceItem);
        }

        private async Task StopAllFruitAsync()
        {
            foreach (Fruit2048DeviceItem item in devices.Where(value => value.IsRunning))
                item.Cancellation?.Cancel();
            Task[] running = devices.Where(item => item.RunningTask != null)
                .Select(item => item.RunningTask).ToArray();
            if (running.Length == 0) return;
            try { await Task.WhenAll(running); } catch { }
        }

        private static IReadOnlyList<string> FormatLearningProfiles(
            IReadOnlyList<FruitTileProfile> profiles)
        {
            if (profiles == null) return new string[0];
            return profiles.Where(profile => profile.Tier > 0).OrderBy(profile => profile.Tier)
                .Select(profile => profile.State == FruitTileLearningState.Learned
                    ? "✓ Tier " + profile.Tier
                    : "○ Tier " + profile.Tier + " — " + profile.SampleCount + "/"
                        + FruitTileLearningCatalog.RequiredSamples + " mẫu")
                .ToArray();
        }

        private void UpdateDetailButtons()
        {
            Fruit2048DeviceItem item = DeviceList.SelectedItem as Fruit2048DeviceItem;
            bool running = item != null && item.IsRunning;
            bool manual = item != null && item.IsConnected && !item.IsRunning
                && item.Owner == DeviceAutomationOwner.None;
            // During Auto the board and recognition summary are the only useful
            // live controls.  Hide setup/manual tools until the run completes.
            ConfigurationPanel.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            ManualControlsPanel.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            ScanButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            StartButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            CalibrationButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            AdvancedToolsExpander.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(StopButton, running ? 0 : 2);
            Grid.SetColumnSpan(StopButton, running ? 3 : 1);
            ScanButton.IsEnabled = manual;
            CalibrationButton.IsEnabled = manual && calibration != null;
            UpButton.IsEnabled = DownButton.IsEnabled = LeftButton.IsEnabled = RightButton.IsEnabled = manual;
            // Start is deliberately available even while bootstrap seeds are missing.
            // RunAsync navigates to the Fruit event first, then reports MissingSeeds
            // safely so the operator can calibrate from the live board.
            StartButton.IsEnabled = item != null && item.CanSelect;
            StopButton.IsEnabled = item != null && item.IsRunning;
            ExportLastFailureButton.IsEnabled = item != null
                && !string.IsNullOrWhiteSpace(learningDiagnosticStore?.GetLatestForDevice(item.DeviceName));
            UpdateBulkActionButton();
        }

        private void UpdateBulkActionButton()
        {
            if (BulkActionButton == null) return;
            Fruit2048DeviceItem[] selected = devices.Where(item => item.IsSelected).ToArray();
            if (selected.Any(item => item.IsRunning))
            {
                BulkActionButton.Content = "■ Dừng đã chọn";
                BulkActionButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                BulkActionButton.IsEnabled = true;
                return;
            }

            if (selected.Any(item => item.CanSelect))
            {
                BulkActionButton.Content = "▶ Chạy đã chọn";
                BulkActionButton.Background = new SolidColorBrush(Color.FromRgb(15, 118, 110));
                BulkActionButton.IsEnabled = true;
                return;
            }

            bool hasAvailable = devices.Any(item => item.CanSelect);
            BulkActionButton.Content = hasAvailable ? "✓ Chọn tất cả khả dụng" : "Không có thiết bị khả dụng";
            BulkActionButton.Background = new SolidColorBrush(Color.FromRgb(71, 85, 105));
            BulkActionButton.IsEnabled = hasAvailable;
        }

        private Fruit2048DeviceItem Find(string deviceName) => devices.FirstOrDefault(item =>
            string.Equals(item.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (closingAfterCancellation) return;
            Task[] running = devices.Where(item => item.IsRunning && item.RunningTask != null)
                .Select(item => item.RunningTask).ToArray();
            if (running.Length == 0)
            {
                CleanupWindow();
                return;
            }
            if (MessageBox.Show(this, "Dừng tất cả tác vụ Fruit 2048 và đóng cửa sổ?",
                "Lễ Hội Trái Cây", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            { e.Cancel = true; return; }
            e.Cancel = true;
            foreach (Fruit2048DeviceItem item in devices) item.Cancellation?.Cancel();
            try { await Task.WhenAll(running); } catch { }
            CleanupWindow();
            closingAfterCancellation = true;
            Close();
        }

        private void CleanupWindow()
        {
            if (windowCleanedUp) return;
            windowCleanedUp = true;
            refreshTimer.Stop();
            ownership.OwnershipChanged -= Ownership_OwnershipChanged;
            windowLifetime.Cancel();
        }
    }

    internal sealed class Fruit2048DeviceItem : INotifyPropertyChanged
    {
        private bool isSelected, isConnected, isRunning;
        private DeviceAutomationOwner owner;
        private Fruit2048RuntimeStatus status;
        private int highestTile, moveCount;
        private Fruit2048Move? lastMove;
        private string lastError;
        private Fruit2048RecognitionMode recognitionMode;
        private int knownTierCount, highestObservedTier;
        private IReadOnlyList<FruitTileProfile> learningProfiles;
        private string learningMessage;
        private Fruit2048BootstrapState bootstrapState;
        private int unknownCellCount;
        private int badgeBootstrapCells, fastPathCells, emptyCellCount, tier1CellCount;
        private bool hasBoardRead;
        private int observedCellCount;
        private Fruit2048FirstLearningProofProgress firstLearningProof;

        public string DeviceName { get; set; }
        public bool IsSelected { get => isSelected; set { isSelected = value; OnChanged(); } }
        public bool IsConnected { get => isConnected; set { if (isConnected == value) return; isConnected = value; OnChanged(); OnChanged(nameof(CanSelect)); RaisePresentationProperties(); } }
        public bool IsRunning { get => isRunning; set { if (isRunning == value) return; isRunning = value; OnChanged(); OnChanged(nameof(CanSelect)); RaisePresentationProperties(); } }
        public DeviceAutomationOwner Owner { get => owner; set { if (owner == value) return; owner = value; OnChanged(); OnChanged(nameof(CanSelect)); RaisePresentationProperties(); } }
        public Fruit2048RuntimeStatus Status { get => status; set { if (status == value) return; status = value; OnChanged(); RaisePresentationProperties(); } }
        public Fruit2048Board Board { get; set; }
        public int HighestTile { get => highestTile; set { highestTile = value; OnChanged(); } }
        public int MoveCount { get => moveCount; set { moveCount = value; OnChanged(); } }
        public Fruit2048Move? LastMove { get => lastMove; set { lastMove = value; OnChanged(); OnChanged(nameof(LastMoveText)); } }
        public string LastError { get => lastError; set { lastError = value; OnChanged(); } }
        public Fruit2048RecognitionMode RecognitionMode { get => recognitionMode; set { recognitionMode = value; OnChanged(); } }
        public int KnownTierCount { get => knownTierCount; set { knownTierCount = value; OnChanged(); } }
        public int HighestObservedTier { get => highestObservedTier; set { highestObservedTier = value; OnChanged(); } }
        public IReadOnlyList<FruitTileProfile> LearningProfiles { get => learningProfiles; set { learningProfiles = value; OnChanged(); } }
        public string LearningMessage { get => learningMessage; set { learningMessage = value; OnChanged(); } }
        public Fruit2048BootstrapState BootstrapState { get => bootstrapState; set { bootstrapState = value; OnChanged(); } }
        public int UnknownCellCount { get => unknownCellCount; set { unknownCellCount = value; OnChanged(); } }
        public int BadgeBootstrapCells { get => badgeBootstrapCells; set { badgeBootstrapCells = value; OnChanged(); } }
        public int FastPathCells { get => fastPathCells; set { fastPathCells = value; OnChanged(); } }
        public int EmptyCellCount { get => emptyCellCount; set { emptyCellCount = value; OnChanged(); } }
        public int Tier1CellCount { get => tier1CellCount; set { tier1CellCount = value; OnChanged(); } }
        public bool HasBoardRead { get => hasBoardRead; set { hasBoardRead = value; OnChanged(); } }
        public int ObservedCellCount { get => observedCellCount; set { observedCellCount = value; OnChanged(); } }
        public Fruit2048FirstLearningProofProgress FirstLearningProof { get => firstLearningProof; set { firstLearningProof = value; OnChanged(); } }
        public CancellationTokenSource Cancellation { get; set; }
        public Task RunningTask { get; set; }
        public bool CanSelect => IsConnected && Owner == DeviceAutomationOwner.None && !IsRunning;
        public string LastMoveText => LastMove.HasValue ? MoveSymbol(LastMove.Value) : "—";
        public string StatusBadgeText => "● " + DisplayStatus;
        public string DisplayStatus
        {
            get
            {
                if (!IsConnected) return "Mất kết nối";
                if (Owner == DeviceAutomationOwner.Farm) return "Đang Farm";
                if (Owner == DeviceAutomationOwner.Fruit2048 || IsRunning) return "Đang chơi Fruit 2048";
                switch (Status)
                {
                    case Fruit2048RuntimeStatus.TargetReached: return "Đã đạt mục tiêu";
                    case Fruit2048RuntimeStatus.NoMoves: return "Không còn nước đi";
                    case Fruit2048RuntimeStatus.BoardUnknown: return "Cần nhận diện thêm";
                    case Fruit2048RuntimeStatus.NeedsFreshBoard: return "Cần board mới";
                    case Fruit2048RuntimeStatus.MissingSeeds: return "Thiếu seed";
                    case Fruit2048RuntimeStatus.Failed: return "Lỗi";
                    default: return "Sẵn sàng";
                }
            }
        }
        public Brush StatusBrush
        {
            get
            {
                if (!IsConnected) return Brushes.Gray;
                if (Owner == DeviceAutomationOwner.Farm) return Brushes.DarkOrange;
                if (Owner == DeviceAutomationOwner.Fruit2048 || IsRunning) return Brushes.RoyalBlue;
                if (Status == Fruit2048RuntimeStatus.Failed || Status == Fruit2048RuntimeStatus.MissingSeeds)
                    return Brushes.Crimson;
                if (Status == Fruit2048RuntimeStatus.BoardUnknown || Status == Fruit2048RuntimeStatus.NeedsFreshBoard)
                    return Brushes.DarkOrange;
                return Brushes.SeaGreen;
            }
        }
        public Brush StatusBadgeForeground => StatusBrush;
        public Brush StatusBadgeBorder => IsErrorStatus
            ? new SolidColorBrush(Color.FromRgb(253, 164, 175))
            : IsWarningStatus ? new SolidColorBrush(Color.FromRgb(253, 186, 116))
            : (!IsConnected ? new SolidColorBrush(Color.FromRgb(203, 213, 225))
            : IsRunning ? new SolidColorBrush(Color.FromRgb(147, 197, 253))
            : new SolidColorBrush(Color.FromRgb(167, 243, 208)));
        public Brush StatusBadgeBackground => IsErrorStatus
            ? new SolidColorBrush(Color.FromRgb(255, 241, 242))
            : IsWarningStatus ? new SolidColorBrush(Color.FromRgb(255, 247, 237))
            : (!IsConnected ? new SolidColorBrush(Color.FromRgb(248, 250, 252))
            : IsRunning ? new SolidColorBrush(Color.FromRgb(239, 246, 255))
            : new SolidColorBrush(Color.FromRgb(236, 253, 245)));
        private bool IsErrorStatus => IsConnected && (Status == Fruit2048RuntimeStatus.Failed
            || Status == Fruit2048RuntimeStatus.MissingSeeds);
        private bool IsWarningStatus => Owner == DeviceAutomationOwner.Farm
            || (IsConnected && (Status == Fruit2048RuntimeStatus.BoardUnknown
                || Status == Fruit2048RuntimeStatus.NeedsFreshBoard));

        public void RefreshPresentation()
        {
            if (!CanSelect) IsSelected = IsRunning && IsSelected;
            OnChanged(nameof(CanSelect));
            RaisePresentationProperties();
        }

        private void RaisePresentationProperties()
        {
            OnChanged(nameof(DisplayStatus));
            OnChanged(nameof(StatusBadgeText));
            OnChanged(nameof(StatusBrush));
            OnChanged(nameof(StatusBadgeForeground));
            OnChanged(nameof(StatusBadgeBorder));
            OnChanged(nameof(StatusBadgeBackground));
        }

        private static string MoveSymbol(Fruit2048Move move)
        {
            switch (move)
            { case Fruit2048Move.Up: return "↑"; case Fruit2048Move.Down: return "↓";
              case Fruit2048Move.Left: return "←"; default: return "→"; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
