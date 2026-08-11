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
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
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
                    existing.RefreshPresentation();
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
                    item.IsConnected = await playerClient.IsRunningAsync(name, windowLifetime.Token);
                    item.Owner = ownership.GetOwner(name);
                    item.RefreshPresentation();
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

        private void SelectAvailable_Click(object sender, RoutedEventArgs e)
        {
            foreach (Fruit2048DeviceItem item in devices)
                item.IsSelected = item.CanSelect;
        }

        private async void StartSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (Fruit2048DeviceItem item in devices.Where(value => value.IsSelected && value.CanSelect).ToArray())
                StartDevice(item);
            await RefreshDevicesAsync();
        }

        private void StopSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (Fruit2048DeviceItem item in devices.Where(value => value.IsSelected && value.IsRunning))
                item.Cancellation?.Cancel();
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
            if (RunModeCombo.SelectedIndex == 1 && !string.Equals(learningCoordinator.Teacher?.DeviceName,
                item.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                item.LearningMessage = "Burn-in chỉ chạy trên thiết bị học đã chọn.";
                UpdateDetail(item);
                return;
            }
            if (!templateCatalog.SeedAvailability.IsReady)
            {
                item.Status = Fruit2048RuntimeStatus.MissingSeeds;
                item.LastError = "Thiếu mẫu nhận diện Empty/Tier 1.";
                item.BootstrapState = Fruit2048BootstrapState.MissingSeedAssets;
                item.LearningMessage = item.LastError;
                UpdateDetail(item);
                return;
            }
            item.Cancellation = new CancellationTokenSource();
            item.IsRunning = true;
            item.Status = Fruit2048RuntimeStatus.Starting;
            item.LearningMessage = string.Empty;
            item.UnknownCellCount = 0;
            item.RefreshPresentation();
            int target = int.Parse(((ComboBoxItem)TargetCombo.SelectedItem).Content.ToString());
            var request = new Fruit2048RunRequest
            {
                TargetTile = target,
                AutoRefresh = AutoRefreshCheck.IsChecked == true,
                TeacherMoveLimit = ParseTeacherMoveLimit(),
                Mode = RunModeCombo.SelectedIndex == 1 ? Fruit2048RunMode.BurnIn : Fruit2048RunMode.Normal
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
                    item.LastError = result.Error;
                    if (result.Outcome == Fruit2048Outcome.TargetReached) item.Status = Fruit2048RuntimeStatus.TargetReached;
                    else if (result.Outcome == Fruit2048Outcome.NoMoves) item.Status = Fruit2048RuntimeStatus.NoMoves;
                    else if (result.Outcome == Fruit2048Outcome.Cancelled) item.Status = Fruit2048RuntimeStatus.Cancelled;
                    else if (result.Outcome == Fruit2048Outcome.Disconnected) item.Status = Fruit2048RuntimeStatus.Disconnected;
                    else if (result.Outcome == Fruit2048Outcome.MissingSeedAssets) item.Status = Fruit2048RuntimeStatus.MissingSeeds;
                    else if (result.Outcome == Fruit2048Outcome.NeedsFreshBoard) item.Status = Fruit2048RuntimeStatus.NeedsFreshBoard;
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

        private void ApplyProgress(Fruit2048DeviceItem item, Fruit2048Progress progress)
        {
            if (!Dispatcher.CheckAccess())
            { Dispatcher.BeginInvoke(new Action(() => ApplyProgress(item, progress))); return; }
            item.Status = progress.Status;
            item.Board = progress.Board;
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
            if (!string.IsNullOrWhiteSpace(progress.LearningMessage))
                item.LearningMessage = progress.LearningMessage;
            item.RefreshPresentation();
            if (DeviceList.SelectedItem == item) UpdateDetail(item);
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
            item.Board = read.Board;
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
            int[] values = item?.Board?.Values.ToArray();
            BoardItems.ItemsSource = values == null
                ? Enumerable.Repeat("?", 16).ToArray()
                : values.Select(value => value.ToString()).ToArray();
            HighestTileText.Text = (item?.HighestTile ?? 0).ToString();
            MoveSummaryText.Text = (item?.MoveCount ?? 0) + " · " + (item?.LastMoveText ?? "—");
            LearningSummaryText.Text = "Nhận diện: " + (item?.RecognitionMode.ToString() ?? "Learning")
                + " · Đã học " + (item?.KnownTierCount ?? 0) + "/" + (item?.HighestObservedTier ?? 0);
            BootstrapText.Text = "Bootstrap: " + BootstrapLabel(item?.BootstrapState
                ?? Fruit2048BootstrapState.MissingSeedAssets);
            HighestObservedText.Text = "Tier cao nhất: " + (item?.HighestObservedTier ?? 0)
                + " · Unknown: " + (item?.UnknownCellCount ?? 0);
            LearningMessageText.Text = item?.LearningMessage ?? string.Empty;
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
            bool manual = item != null && item.IsConnected && !item.IsRunning
                && item.Owner == DeviceAutomationOwner.None;
            ScanButton.IsEnabled = manual;
            CalibrationButton.IsEnabled = manual && calibration != null;
            UpButton.IsEnabled = DownButton.IsEnabled = LeftButton.IsEnabled = RightButton.IsEnabled = manual;
            StartButton.IsEnabled = item != null && item.CanSelect
                && templateCatalog.SeedAvailability.IsReady;
            StopButton.IsEnabled = item != null && item.IsRunning;
            ExportLastFailureButton.IsEnabled = item != null
                && !string.IsNullOrWhiteSpace(learningDiagnosticStore?.GetLatestForDevice(item.DeviceName));
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

        public string DeviceName { get; set; }
        public bool IsSelected { get => isSelected; set { isSelected = value; OnChanged(); } }
        public bool IsConnected { get => isConnected; set { isConnected = value; OnChanged(); OnChanged(nameof(CanSelect)); } }
        public bool IsRunning { get => isRunning; set { isRunning = value; OnChanged(); OnChanged(nameof(CanSelect)); } }
        public DeviceAutomationOwner Owner { get => owner; set { owner = value; OnChanged(); OnChanged(nameof(CanSelect)); } }
        public Fruit2048RuntimeStatus Status { get => status; set { status = value; OnChanged(); } }
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
        public CancellationTokenSource Cancellation { get; set; }
        public Task RunningTask { get; set; }
        public bool CanSelect => IsConnected && Owner == DeviceAutomationOwner.None && !IsRunning;
        public string ConnectionText => IsConnected ? "Đã kết nối" : "Mất kết nối";
        public string LastMoveText => LastMove.HasValue ? MoveSymbol(LastMove.Value) : "—";
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
                    case Fruit2048RuntimeStatus.BoardUnknown: return "Không đọc được bàn";
                    case Fruit2048RuntimeStatus.NeedsFreshBoard: return "Cần board mới";
                    case Fruit2048RuntimeStatus.MissingSeeds: return "Thiếu seed";
                    case Fruit2048RuntimeStatus.Failed: return "Lỗi";
                    default: return "Sẵn sàng";
                }
            }
        }
        public Brush StatusBrush => Owner == DeviceAutomationOwner.Farm
            ? Brushes.DarkOrange : (IsConnected ? Brushes.SeaGreen : Brushes.Gray);

        public void RefreshPresentation()
        {
            if (!CanSelect) IsSelected = IsRunning && IsSelected;
            OnChanged(nameof(CanSelect)); OnChanged(nameof(ConnectionText));
            OnChanged(nameof(DisplayStatus)); OnChanged(nameof(StatusBrush));
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
