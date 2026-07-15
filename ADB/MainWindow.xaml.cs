using ADB_Tool_Automation_Post_FB.Exceptions;
using ADB_Tool_Automation_Post_FB.Helpers;
using ADB_Tool_Automation_Post_FB.Models;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.UI;
using Auto_LDPlayer;
using Auto_LDPlayer.Enums;
using KAutoHelper;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;


namespace ADB_Tool_Automation_Post_FB
{
    public partial class MainWindow : Window
    {
        #region data
        private byte[] TRUY_CAP_BMP;
        private byte[] THAM_GIA_BMP;
        private byte[] POST_GO_BACK_BTN;

        Bitmap FANPAGE_BOOST_GAME_STORE_NAME;
        private byte[] FANPAGE_LIKE_PAGE_BUTTON;

        private byte[] PROXY_OK_BUTTON;
        private byte[] PROXY_START_SERVICE_BUTTON;

        Bitmap INPUT_WRITE_POST_BMP_1;
        Bitmap INPUT_WRITE_POST_BMP_2;
        private byte[] LIKE_POST_FACEBOOK_BUTTON_BMP;

        Bitmap SELECT_TIENG_VIET_BUTTON;
        Bitmap ACCEPT_NGON_NGU_BUTTON;
        private byte[] FACEBOOK_ACCEPT_FRIENDS_BUTTON;
        private byte[] FACEBOOK_ADD_FRIEND_BUTTON_IN_NEWFEED_TAB;
        private byte[] FACEBOOK_ADD_FRIEND_BUTTON_IN_FRIENDS_TAB;

        private byte[] EMOJI_THICH_BUTTON;
        private byte[] EMOJI_HAHA_BUTTON;
        private byte[] EMOJI_WOW_BUTTON;
        private byte[] EMOJI_YEU_THICH_BUTTON;
        private byte[] EMOJI_THUONG_THUONG_BUTTON;

        Bitmap FACEBOOK_ADD_FRIENDS_REQUEST_BUTTON;
        private byte[] FACEBOOK_FRIENDS_FOLLOWER_REQUEST_BUTTON;
        private byte[] FACEBOOK_FRIEND_VIEW_PROFILE_BUTTON;
        private byte[] FACEBOOK_POST_0_COMMENT_ICON;
        private byte[] FACEBOOK_POST_1_COMMENT_ICON;
        private byte[] FACEBOOK_SEND_COMMENT_BUTTON;

        Bitmap FACEBOOK_POST_IMPORT_IMAGE_1_BMP;
        Bitmap FACEBOOK_POST_IMPORT_IMAGE_2_BMP;
        Bitmap FACEBOOK_ACCEPT_ACCESS_CAMERA_BMP;
        Bitmap FACEBOOK_ALLOW_ACCESS_BMP;
        Bitmap FACEBOOK_POST_IMAGE_SELECTION_1_BMP;

        Bitmap FACEBOOK_SHARE_POST_FEED;
        Bitmap SELECTION_POST_INTO_FACEBOOK;
        #endregion


        List<string> ldplayerDevicesAvailable = new List<string>();
        List<int> listDeviceID = new List<int>();
        List<DailyPostItem> dailyPostAvailable = new List<DailyPostItem>();
        ApiResponseLDPlayerDevices allLDPlayerDevices = new ApiResponseLDPlayerDevices();
        List<GameFanpage> listGameFanpage = new List<GameFanpage>();
        Dictionary<string, List<FriendRequestProfile>> listFriendRequestProfile = new Dictionary<string, List<FriendRequestProfile>>();

        // Thêm vào class MainWindow
        private static readonly Dictionary<string, int> commentCountByDevice = new Dictionary<string, int>();

        private readonly int limitDevice = 10;

        bool isStop = false;
        private bool isReactionSelected = true;
        private bool isPostGroupProcess = false;
        private bool isCommentProcess = false;
        private readonly bool isSeedingFanpageProcess = true;

        private readonly bool isAccountMantainanceProcess = true;
        private readonly bool isFriendRequestProcess = true;
        private readonly bool isSharePostProcess = true;

        private string pcRunner = "pc_1"; // Default value for pc_runner

        private static readonly Random _random = new Random();

        private readonly List<byte[]> emojiButtonsRaw = new List<byte[]>(5);

        private static readonly object emojiLock = new object();

        public MainWindow()
        {
            InitializeComponent();
            LoadDataImage();
            LoadStaticLDConsole();

            emojiButtonsRaw.Add(EMOJI_THICH_BUTTON);
            emojiButtonsRaw.Add(EMOJI_HAHA_BUTTON);
            emojiButtonsRaw.Add(EMOJI_WOW_BUTTON);
            emojiButtonsRaw.Add(EMOJI_YEU_THICH_BUTTON);
            emojiButtonsRaw.Add(EMOJI_THUONG_THUONG_BUTTON);

            TaskExceptions.RegisterGlobalHandlers(); // ✅ Gọi handler tập trung

            this.ContentRendered += MainWindow_ContentRendered;
        }

        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            var workArea = SystemParameters.WorkArea;
            this.Left = workArea.Right - this.ActualWidth;
            this.Top = workArea.Bottom - this.ActualHeight;
        }

        public static void LoadStaticLDConsole()
        {
            Auto_LDPlayer.LDPlayer.PathLD = @"D:\LDPlayer\LDPlayer9\ldconsole.exe";
        }

        private void LoadDataImage()
        {
            PROXY_OK_BUTTON = BitmapHelper.LoadAsBytes("Data\\proxy_ok_button.png");
            PROXY_START_SERVICE_BUTTON = BitmapHelper.LoadAsBytes("Data\\btn_start_service.png");

            FANPAGE_BOOST_GAME_STORE_NAME = (Bitmap)Bitmap.FromFile("Data\\boost_game_nte_fanpage.png");
            FANPAGE_LIKE_PAGE_BUTTON = BitmapHelper.LoadAsBytes("Data\\fanpage_like_button.png");

            TRUY_CAP_BMP = BitmapHelper.LoadAsBytes("Data\\Access_img_1.png");
            THAM_GIA_BMP = BitmapHelper.LoadAsBytes("Data\\Join_img_1.png");
            INPUT_WRITE_POST_BMP_1 = (Bitmap)Bitmap.FromFile("Data\\Write_img_1.png");
            INPUT_WRITE_POST_BMP_2 = (Bitmap)Bitmap.FromFile("Data\\Write_img_2.png");

            SELECT_TIENG_VIET_BUTTON = (Bitmap)Bitmap.FromFile("Data\\set_tieng_viet_button.png");
            ACCEPT_NGON_NGU_BUTTON = (Bitmap)Bitmap.FromFile("Data\\accept_ngon_ngu_button.png");
            FACEBOOK_ACCEPT_FRIENDS_BUTTON = BitmapHelper.LoadAsBytes("Data\\accept_friends_button.png");

            LIKE_POST_FACEBOOK_BUTTON_BMP = BitmapHelper.LoadAsBytes("Data\\like_button.png");
            EMOJI_THICH_BUTTON = BitmapHelper.LoadAsBytes("Data\\emoji_thich_button.png");
            EMOJI_HAHA_BUTTON = BitmapHelper.LoadAsBytes("Data\\emoji_haha_button.png");
            EMOJI_WOW_BUTTON = BitmapHelper.LoadAsBytes("Data\\emoji_wow_button.png");
            EMOJI_YEU_THICH_BUTTON = BitmapHelper.LoadAsBytes("Data\\emoji_yeu_thich_button.png");
            EMOJI_THUONG_THUONG_BUTTON = BitmapHelper.LoadAsBytes("Data\\emoji_thuong_thuong_button.png");

            FACEBOOK_ADD_FRIENDS_REQUEST_BUTTON = (Bitmap)Bitmap.FromFile("Data\\add_friends_request_button.png");
            FACEBOOK_FRIENDS_FOLLOWER_REQUEST_BUTTON = BitmapHelper.LoadAsBytes("Data\\friends_follower_request_button.png");
            FACEBOOK_FRIEND_VIEW_PROFILE_BUTTON = BitmapHelper.LoadAsBytes("Data\\friends_view_profile_button.png");
            FACEBOOK_ADD_FRIEND_BUTTON_IN_NEWFEED_TAB = BitmapHelper.LoadAsBytes("Data\\btn_them_ban_be_newfeed_tab.png");
            FACEBOOK_ADD_FRIEND_BUTTON_IN_FRIENDS_TAB = BitmapHelper.LoadAsBytes("Data\\btn_them_ban_be_friend_tab.png");
            FACEBOOK_POST_0_COMMENT_ICON = BitmapHelper.LoadAsBytes("Data\\post_0_comment_icon.png");
            FACEBOOK_POST_1_COMMENT_ICON = BitmapHelper.LoadAsBytes("Data\\post_1_comment_icon.png");

            FACEBOOK_POST_IMPORT_IMAGE_1_BMP = (Bitmap)Bitmap.FromFile("Data\\post_import_image_1.png");
            FACEBOOK_POST_IMPORT_IMAGE_2_BMP = (Bitmap)Bitmap.FromFile("Data\\post_import_image_2.png");

            FACEBOOK_ACCEPT_ACCESS_CAMERA_BMP = (Bitmap)Bitmap.FromFile("Data\\facebook_accept_access_camera.png");
            FACEBOOK_ALLOW_ACCESS_BMP = (Bitmap)Bitmap.FromFile("Data\\facebook_allow_access.png");
            FACEBOOK_POST_IMAGE_SELECTION_1_BMP = (Bitmap)(Bitmap.FromFile("Data\\post_image_selection_1.png"));

            FACEBOOK_SHARE_POST_FEED = (Bitmap)Bitmap.FromFile("Data\\share_post_button.png");
            SELECTION_POST_INTO_FACEBOOK = (Bitmap)Bitmap.FromFile("Data\\selection_post_into_facebook.png");

            FACEBOOK_SEND_COMMENT_BUTTON = BitmapHelper.LoadAsBytes("Data\\btn_send_comment.png");

            POST_GO_BACK_BTN = BitmapHelper.LoadAsBytes("Data\\btn_go_back.png");
        }


        public async Task FetchResourceForDeviceRunAPI()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // 🔁 Gọi API xoay proxy
                    bool rotated = await ApiHelper.RotateAllProxiesAsync(client);
                    if (!rotated)
                    {
                        Logger.LogWarning("⚠️ Không thể xoay proxy. Dừng 2 phút.");
                        await Dispatcher.InvokeAsync(() =>
                        {
                            deviceStatusLabel.Content = "⚠️ Không thể xoay proxy. Chờ 2 phút.";
                        }, System.Windows.Threading.DispatcherPriority.Background);
                        return;
                    }

                    // ✅ Fetch danh sách thiết bị
                    allLDPlayerDevices = await ApiHelper.FetchAvailableLDPlayerDevicesAsync(client, limitDevice, pcRunner);

                    if (allLDPlayerDevices != null && allLDPlayerDevices.Items != null && allLDPlayerDevices.Items.Count > 0)
                    {
                        // 👉 Lấy danh sách ID
                        ldplayerDevicesAvailable = allLDPlayerDevices.Items.Select(d => d.DeviceName).ToList();
                        listDeviceID = allLDPlayerDevices.Items.Select(d => d.ID).ToList();

                        // ✅ Fetch các nguồn liên quan
                        dailyPostAvailable = await ApiHelper.FetchDailyPostsContentAsync(client, listDeviceID);
                        listGameFanpage = await ApiHelper.FetchGameFanpagesAsync(client);
                        listFriendRequestProfile = await ApiHelper.FetchFriendListAsync(client, listDeviceID);
                    }
                    else
                    {
                        Logger.LogInfo("⚠️ Không có thiết bị khả dụng.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"❌ Error in FetchResourceForDeviceRunAPI: {ex.Message}");
            }
        }


        // ---------------- UI Event Handlers ---------------- //
        private void PcRunnerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UIEventHelper.PcRunnerComboBoxChanged(sender, e);
            pcRunner = UIEventHelper.PcRunner;
        }

        private void TaskTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UIEventHelper.TaskTypeComboBoxChanged(sender, e);
            isReactionSelected = UIEventHelper.IsReactionSelected;
            isPostGroupProcess = UIEventHelper.IsPostGroupProcess;
        }

        private void Button_Click_ShowLog(object sender, RoutedEventArgs e)
        {
            UIEventHelper.ShowLogFile();
        }

        private void Button_Click_DeviceDiagnostic(object sender, RoutedEventArgs e)
        {
            var diagnosticWindow = new DeviceDiagnosticWindow(
                DeviceDiagnosticServiceFactory.CreateFromAppConfig())
            {
                Owner = this
            };
            diagnosticWindow.Show();
        }

        private void Button_Click_Start(object sender, RoutedEventArgs e)
        {
            UIEventHelper.StartAutomation(Button_Start, Button_Stop, deviceStatusLabel, MainAutoRunFunction);
            isStop = false;
        }

        private async void Button_Click_Stop(object sender, RoutedEventArgs e)
        {
            await UIEventHelper.StopAutomationAsync(Button_Start, Button_Stop, deviceStatusLabel, () => ldplayerDevicesAvailable);
            isStop = true;
        }
        // ---------------- UI Event Handlers ---------------- //


        private async Task MainAutoRunFunction()
        {
            while (!isStop)
            {
                // Bước 1: Fetch dữ liệu thiết bị
                await FetchResourceForDeviceRunAPI();

                // Bước 2: Hiển thị thống kê
                DeviceHelper.SafeUpdateUI(countDevices, $"Tổng cộng: {allLDPlayerDevices?.Statistics[pcRunner].TotalCount ?? 0} facebook");
                var statusBreakdown = allLDPlayerDevices?.Statistics[pcRunner].StatusBreakdown;
                if (statusBreakdown != null)
                {
                    string breakdownText = string.Join("  ·  ", statusBreakdown.Select(s => $"{s.Status}: {s.Count}"));
                    DeviceHelper.SafeUpdateUI(statusDevices, $"{breakdownText}");
                }

                // Bước 3: Nếu không có thiết bị → đợi
                if (ldplayerDevicesAvailable.Count == 0)
                {
                    Logger.LogInfo("Không có thiết bị khả dụng. Chờ 10s...");
                    await DeviceHelper.WaitWithCountdown(deviceStatusLabel, 10);
                    continue;
                }

                DeviceHelper.SafeUpdateUI(deviceStatusLabel, $"Đang khởi tạo {ldplayerDevicesAvailable.Count} thiết bị");

                // Bước 4: Mở từng LDPlayer cách nhau 2 giây (tuần tự)
                foreach (var deviceName in ldplayerDevicesAvailable)
                {
                    // Kiểm tra nếu dừng
                    if (isStop)
                    {
                        Logger.LogInfo("⛔ Đã nhận tín hiệu dừng, ngắt khởi tạo LDPlayer.");
                        break;
                    }

                    var device = allLDPlayerDevices?.Items?.FirstOrDefault(d => d.DeviceName == deviceName);
                    if (device == null) continue;

                    var postItem = dailyPostAvailable?.FirstOrDefault(d => d.LDPlayerDevicesId == device.ID);
                    if (postItem != null)
                    {
                        await FileHelper.FetchPostImageSaveIntoSDCard(deviceName, postItem.PostImage);
                    }

                    if (!Auto_LDPlayer.LDPlayer.IsDeviceRunning(LDType.Name, deviceName))
                    {
                        Logger.LogInfo($"🚀 Đang mở LDPlayer: {deviceName}");
                        Auto_LDPlayer.LDPlayer.Open(LDType.Name, deviceName);
                        await DelayHelper.DelayAsync(1, 2);
                    }
                    else
                    {
                        Logger.LogInfo($"✅ {deviceName} đã mở.");
                    }

                    await Task.Delay(2000); // Chờ 2s trước khi mở thiết bị tiếp theo
                }

                // Bước 5: Sắp xếp cửa sổ
                await DelayHelper.DelayAsync(2*ldplayerDevicesAvailable.Count, 3*ldplayerDevicesAvailable.Count);
                DeviceHelper.SortWnd();
                await DelayHelper.DelayAsync(5, 2 * ldplayerDevicesAvailable.Count);
                DeviceHelper.SafeUpdateUI(deviceStatusLabel, $"Đang chạy: {DeviceHelper.CountTotalRunningDevices(LDType.Name, ldplayerDevicesAvailable)} thiết bị.");

                // Bước 6: Xử lý song song từng thiết bị
                var taskList = new List<Task>();
                foreach (var deviceName in ldplayerDevicesAvailable)
                {
                    // Kiểm tra nếu dừng
                    if (isStop)
                    {
                        Logger.LogInfo("⛔ Đã nhận tín hiệu dừng, ngắt xử lý thiết bị.");
                        break;
                    }

                    var device = allLDPlayerDevices?.Items?.FirstOrDefault(d => d.DeviceName == deviceName);
                    if (device == null) continue;

                    var postItem = dailyPostAvailable?.FirstOrDefault(p => p.LDPlayerDevicesId == device.ID);
                    if (postItem == null)
                    {
                        Logger.LogInfo($"❌ Không có bài viết cho thiết bị {deviceName}, bỏ qua.");
                        continue;
                    }

                    listFriendRequestProfile.TryGetValue(device.ID.ToString(), out var friendRequests);

                    taskList.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // Gán fanpage riêng cho từng thiết bị bên trong Task
                            GameFanpage fanpage;
                            if (isPostGroupProcess)
                            {
                                fanpage = listGameFanpage?.FirstOrDefault(f => f.Id == postItem.GameFanpageId);
                            }
                            else
                            {
                                fanpage = listGameFanpage != null && listGameFanpage.Count > 0
                                    ? listGameFanpage[_random.Next(listGameFanpage.Count)]
                                    : null;
                            }

                            Logger.LogInfo($"📌 Device {deviceName} dùng fanpage MyGroup: {fanpage?.MyGroup ?? "null"}");

                            await ProcessDeviceAsync(
                                deviceName,
                                device,
                                postItem,
                                fanpage,
                                friendRequests ?? new List<FriendRequestProfile>(),
                                CancellationToken.None
                            );
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"[MainAutoRunFunction] Lỗi xử lý {deviceName}: {ex.Message}");
                            Auto_LDPlayer.LDPlayer.Close(LDType.Name, deviceName);
                        }
                    }));

                }

                await Task.WhenAll(taskList);
                Logger.LogInfo("🎯 Đã xử lý xong toàn bộ thiết bị.");

                // Bước 7: Đóng toàn bộ LDPlayer
                Auto_LDPlayer.LDPlayer.CloseAll();

                // Bước 8: Đợi ngẫu nhiên trước vòng tiếp theo
                int randomDelay = new Random().Next(10000, 20000); // 10–20s
                int countdown = randomDelay / 1000;
                await DeviceHelper.WaitWithCountdown(deviceStatusLabel, countdown);
                await Task.Delay(randomDelay - countdown * 1000);
            }
        }


        private static FriendRequestProfile GetRandomProfileDetail(List<FriendRequestProfile> listFriendRequest)
        {
            if (listFriendRequest == null || !listFriendRequest.Any())
            {
                Logger.LogInfo("No friend requests available to select a profile detail.");
                return null; // Return null to indicate no valid profile
            }

            Random random = new Random();
            int randomIndex = random.Next(listFriendRequest.Count);

            // Return the randomly selected FriendRequestProfile
            return listFriendRequest[randomIndex];
        }


        public async Task SelectReactionOption(string deviceName)
        {
            int chance = _random.Next(0, 100);  // 0-99

            try
            {
                if (chance < 30)
                {
                    Logger.LogInfo("Option 1 selected: Liking the post.");

                    if (LIKE_POST_FACEBOOK_BUTTON_BMP == null || LIKE_POST_FACEBOOK_BUTTON_BMP.Length == 0)
                    {
                        Logger.LogError("❌ LIKE_POST_FACEBOOK_BUTTON_BMP is null or empty.");
                        return;
                    }

                    DeviceHelper.TapImgBytes(LDType.Name, deviceName, LIKE_POST_FACEBOOK_BUTTON_BMP);
                }
                else if (chance < 95)
                {
                    Logger.LogInfo("Option 2 selected: Choosing emoji.");

                    using (var screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(LDType.Name, deviceName))
                    {
                        if (screenshot == null)
                        {
                            Logger.LogError($"❌ Could not capture screenshot from device: {deviceName}");
                            return;
                        }

                        using (var ms = new MemoryStream(LIKE_POST_FACEBOOK_BUTTON_BMP))
                        using (var subBmp = new Bitmap(ms))
                        {
                            System.Drawing.Point? pt = KAutoHelper.ImageScanOpenCV.FindOutPoint(screenshot, subBmp);

                            if (!pt.HasValue)
                            {
                                Logger.LogWarning("⚠️ Could not find like button image in screenshot.");
                                return;
                            }

                            Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, pt.Value.X, pt.Value.Y, 1000);
                            await DelayHelper.DelayAsync(2, 3);
                        }

                        byte[] emojiBytes;
                        lock (emojiLock)
                        {
                            int emojiChoice = _random.Next(0, emojiButtonsRaw.Count);
                            emojiBytes = emojiButtonsRaw[emojiChoice];
                        }

                        if (emojiBytes != null && emojiBytes.Length > 0)
                        {
                            DeviceHelper.TapImgBytes(LDType.Name, deviceName, emojiBytes);
                        }
                        else
                        {
                            Logger.LogWarning("⚠️ Selected emoji image is empty or null.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"❌ Error selecting reaction option for {deviceName}: {ex}");
            }
        }


        public async Task RequestFriendInNewfeedTab(string deviceName)
        {
            for (int i = 0; i < 2; i++)
            {
                Logger.LogInfo($"Click add friend in new feed - Attempt {i + 1}");
                DeviceHelper.TapImgBytes(LDType.Name, deviceName, FACEBOOK_ADD_FRIEND_BUTTON_IN_NEWFEED_TAB);
                await DelayHelper.DelayAsync(2, 3);
            }
        }


        public async Task SharePostAction(string deviceName)
        {
            if (DeviceHelper.TapImgAsync(LDType.Name, deviceName, FACEBOOK_SHARE_POST_FEED))
            {
                await DelayHelper.DelayAsync(4, 10);

                DeviceHelper.TapImgAsync(LDType.Name, deviceName, SELECTION_POST_INTO_FACEBOOK);

                await DelayHelper.DelayAsync(4, 10);

                // Step 11: Final tap to submit the post
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 86.7, 6.7);

                await DelayHelper.DelayAsync(4, 6);

                DeviceHelper.TapByPercent(LDType.Name, deviceName, 8.5, 7);
            }
        }


        // Helper function to tap on a specific location
        private static async Task TapLocationAsync(string deviceName, double x, double y)
        {
            DeviceHelper.TapByPercent(LDType.Name, deviceName, x, y);
            await DelayHelper.DelayAsync(4, 8);
        }

        // Helper function to go back to the Facebook home page
        private static async Task GoBackToHomePageAsync(string deviceName, CancellationToken cancellationToken)
        {
            Logger.LogInfo("Going back to Facebook home page...");
            for (int i = 0; i < 4; i++)
            {
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 6.4, 7.2);
                int delayTime = new Random().Next(3, 5) * 1000; // Random delay between 4 and 10 seconds
                await DelayHelper.DelayAsync(delayTime / 1000, delayTime / 1000);

                if (cancellationToken.IsCancellationRequested) return;
            }

            await DelayHelper.DelayAsync(7, 15);
        }


        private static async Task SwipeByPercentSafe(
            LDType ldType,
            string deviceName,
            double startXPercent,
            double startYPercent,
            double endXPercent,
            double endYPercent,
            CancellationToken cancellationToken = default)
        {
            int minDurationMs = 450;
            int maxDurationMs = 600;

            const double minDelta = 20.0;  // Ensure at least 20% movement to avoid click action
            var rand = new Random();

            // Ensure sufficient movement on the X-axis
            while (Math.Abs(startXPercent - endXPercent) < minDelta)
            {
                endXPercent = startXPercent + (rand.Next(0, 2) == 0 ? minDelta : -minDelta);
            }

            // Ensure sufficient movement on the Y-axis
            while (Math.Abs(startYPercent - endYPercent) < minDelta)
            {
                endYPercent = startYPercent + (rand.Next(0, 2) == 0 ? minDelta : -minDelta);
            }

            // Select a random duration for the swipe
            int duration = rand.Next(minDurationMs, maxDurationMs + 1);

            // Perform the swipe action
            DeviceHelper.SwipeByPercent(
                ldType,
                deviceName,
                startXPercent, startYPercent,
                endXPercent, endYPercent,
                duration
            );

            await Task.Delay(duration / 2, cancellationToken);

            string currentPackage = DeviceHelper.GetCurrentPackage(ldType, deviceName);

            if (string.IsNullOrEmpty(currentPackage) || !currentPackage.Contains("com.facebook.lite"))
            {
                Logger.LogInfo($"Device {deviceName} switched to another app: {currentPackage}. Returning to Facebook...");
                Auto_LDPlayer.LDPlayer.RunApp(ldType, deviceName, "com.facebook.lite"); // Launch Facebook Lite
                await Task.Delay(30, cancellationToken); // Wait a moment for the app to load
            }
        }


        private async Task AcceptFriendRequestWithRetry(string deviceName, CancellationToken cancellationToken)
        {
            const int maxRetry = 15;
            const int scrollEvery = 5;
            int acceptedCount = 0;

            for (int retry = 0; retry < maxRetry; retry++)
            {
                using (var subAcceptFriendBmp = BitmapHelper.CreateFromBytes(FACEBOOK_ACCEPT_FRIENDS_BUTTON))
                {
                    var screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(LDType.Name, deviceName);
                    if (screenshot == null)
                    {
                        Logger.LogError($"❌ Cannot capture screen for {deviceName} (FACEBOOK_ACCEPT_FRIENDS_BUTTON)");
                        continue;
                    }

                    var pt = ImageScanOpenCV.FindOutPoint(screenshot, subAcceptFriendBmp);
                    screenshot.Dispose();

                    if (!pt.HasValue)
                        continue;

                    Auto_LDPlayer.LDPlayer.Tap(LDType.Name, deviceName, pt.Value.X, pt.Value.Y);
                    acceptedCount++;
                }

                await DelayHelper.DelayAsync(1, 2);

                using (var subAddNewBmp = BitmapHelper.CreateFromBytes(FACEBOOK_ADD_FRIEND_BUTTON_IN_FRIENDS_TAB))
                {
                    var screenshotAdd = Auto_LDPlayer.LDPlayer.ScreenShoot(LDType.Name, deviceName);
                    if (screenshotAdd == null)
                    {
                        Logger.LogError($"❌ Cannot capture screen for {deviceName} (FACEBOOK_ADD_FRIEND_BUTTON_IN_FRIENDS_TAB)");
                        continue;
                    }

                    var pt = ImageScanOpenCV.FindOutPoint(screenshotAdd, subAddNewBmp);
                    screenshotAdd.Dispose();

                    if (!pt.HasValue)
                        continue;

                    Auto_LDPlayer.LDPlayer.Tap(LDType.Name, deviceName, pt.Value.X, pt.Value.Y);
                }

                await DelayHelper.DelayAsync(1, 2);

                if (acceptedCount % scrollEvery == 0)
                {
                    Logger.LogInfo($"✅ Đã accept {acceptedCount} bạn – thực hiện scroll xuống để tải thêm...");
                    await SwipeByPercentSafe(
                        LDType.Name,
                        deviceName,
                        50, 85,
                        50, 40,
                        cancellationToken
                    );
                    await DelayHelper.DelayAsync(2, 4);
                }

                if (cancellationToken.IsCancellationRequested)
                    return;
            }
        }



        public async Task RequestFriendInFriendsTab(string deviceName)
        {
            const int requestCount = 5;

            // Bước 1: Tap theo ảnh mẫu (dạng byte[])
            for (int i = 0; i < requestCount; i++)
            {
                Logger.LogInfo($"🔁 Click add friend in friends tab - Lần {i + 1}");

                using (var friendBmp = BitmapHelper.CreateFromBytes(FACEBOOK_ADD_FRIEND_BUTTON_IN_FRIENDS_TAB))
                {
                    DeviceHelper.TapImgAsync(LDType.Name, deviceName, friendBmp);
                }

                await DelayHelper.DelayAsync(2, 3);
            }

            // Bước 2: Scroll nhẹ để tải thêm bạn bè
            Logger.LogInfo("🔽 Scroll xuống để tải thêm bạn trong tab bạn bè...");
            await SwipeByPercentSafe(
                LDType.Name,
                deviceName,
                50, 85,
                50, 40
            );
            await DelayHelper.DelayAsync(2, 4);

            // Bước 3: OCR nội bộ từ thiết bị
            var positions = DeviceHelper.GetAllTextCoordinatesFromDevice(LDType.Name, deviceName, "thêm bạn bè");

            if (positions != null && positions.Any())
            {
                Logger.LogInfo($"🟢 Tìm thấy {positions.Count} vị trí chứa 'thêm bạn bè'. Đang lần lượt tap...");

                foreach (var (x, y) in positions)
                {
                    Auto_LDPlayer.LDPlayer.Tap(LDType.Name, deviceName, x, y);
                    await DelayHelper.DelayAsync(2, 3);
                }
            }
            else
            {
                Logger.LogInfo("❌ Không tìm thấy chuỗi 'thêm bạn bè' nào trong ảnh.");
            }
        }


        public async Task HumanityActionSimulationScript1(string deviceName, CancellationToken cancellationToken)
        {
            var rand = new Random();

            try
            {
                Logger.LogInfo($"Scroll in 'Story' tab on {deviceName}");
                for (int i = 0; i < 2; i++)
                {
                    double startX = rand.NextDouble() * 40 + 30;
                    double endX = rand.NextDouble() * 40 + 30;
                    const double y = 41.8;
                    while (Math.Abs(startX - endX) < 20)
                        endX = rand.NextDouble() * 40 + 30;

                    await SwipeByPercentSafe(
                        LDType.Name,
                        deviceName,
                        startX, y,
                        endX, y,
                        cancellationToken
                    );

                    await DelayHelper.DelayAsync(5, 12);
                    if (cancellationToken.IsCancellationRequested) return;
                }

                await DelayHelper.DelayAsync(7, 15);
                if (cancellationToken.IsCancellationRequested) return;

                Logger.LogInfo($"Tapping 'Home' tab on {deviceName}");
                int swipeCount = rand.Next(5, 16);
                int sharePostCount = 0; // Initialize a counter for sharing posts
                for (int i = 0; i < swipeCount; i++)
                {
                    double startY = rand.NextDouble() * 30 + 50;
                    double endY = rand.NextDouble() * 30 + 10;
                    const double x = 50;

                    int chance = _random.Next(0, 100);  // 0-99

                    await SwipeByPercentSafe(
                        LDType.Name,
                        deviceName,
                        x, startY,
                        x, endY,
                        cancellationToken
                    );

                    if (isReactionSelected && chance < 20)
                    {
                        await SelectReactionOption(deviceName);
                    }
                    await RequestFriendInNewfeedTab(deviceName);
                    await DelayHelper.DelayAsync(4, 10);
                    if (cancellationToken.IsCancellationRequested) return;

                    if (isSharePostProcess && sharePostCount < 3 && chance < 10)
                    {
                        await SharePostAction(deviceName);
                        sharePostCount++;
                    }
                }

                // Tap to Home or FaceBook icon
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 8.5, 7);
                await DelayHelper.DelayAsync(4, 8);
                if (cancellationToken.IsCancellationRequested) return;

                Logger.LogInfo($"Tapping 'Friends' tab on {deviceName}");
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 24.3, 15);
                await DelayHelper.DelayAsync(2, 4);
                if (cancellationToken.IsCancellationRequested) return;

                await AcceptFriendRequestWithRetry(deviceName, cancellationToken);
                await RequestFriendInFriendsTab(deviceName);
                await DelayHelper.DelayAsync(3, 5);
                if (cancellationToken.IsCancellationRequested) return;

                Logger.LogInfo($"Tapping 'Home' tab on {deviceName}");
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 8.5, 7);
                await DelayHelper.DelayAsync(5, 10);
                if (cancellationToken.IsCancellationRequested) return;

                Logger.LogInfo($"Tapping 'Watch' tab on {deviceName}");
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 58.6, 15);
                await DelayHelper.DelayAsync(5, 7);
                if (cancellationToken.IsCancellationRequested) return;

                if (DeviceHelper.TapImgAsync(LDType.Name, deviceName, SELECT_TIENG_VIET_BUTTON))
                {
                    await DelayHelper.DelayAsync(4, 7);
                    if (cancellationToken.IsCancellationRequested) return;
                    DeviceHelper.TapImgAsync(LDType.Name, deviceName, ACCEPT_NGON_NGU_BUTTON);
                    await DelayHelper.DelayAsync(3, 5);
                }

                swipeCount = rand.Next(4, 10);
                for (int i = 0; i < swipeCount; i++)
                {
                    double startY = rand.NextDouble() * 20 + 50;
                    double endY = rand.NextDouble() * 20 + 10;
                    const double x = 50;

                    await SwipeByPercentSafe(
                        LDType.Name,
                        deviceName,
                        x, startY,
                        x, endY,
                        cancellationToken
                    );

                    await DelayHelper.DelayAsync(4, 10);
                    if (cancellationToken.IsCancellationRequested) return;

                    await DelayHelper.DelayAsync(2, 4);
                    if (cancellationToken.IsCancellationRequested) return;
                }

                await DelayHelper.DelayAsync(5, 8);
                if (cancellationToken.IsCancellationRequested) return;

                // Tap to Home or FaceBook icon
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 8.5, 7);
                await DelayHelper.DelayAsync(4, 8);
                if (cancellationToken.IsCancellationRequested) return;

                Logger.LogInfo($"Tapping 'Notification' tab on {deviceName}");
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 76, 15);
                await DelayHelper.DelayAsync(5, 7);
                if (cancellationToken.IsCancellationRequested) return;

                await AcceptFriendRequestWithRetry(deviceName, cancellationToken);
                await DelayHelper.DelayAsync(3, 5);

                swipeCount = rand.Next(2, 5);
                for (int i = 0; i < swipeCount; i++)
                {
                    await SwipeByPercentSafe(
                        LDType.Name,
                        deviceName,
                        50, 86.8,
                        50, 50.7,
                        cancellationToken
                    );

                    await DelayHelper.DelayAsync(4, 10);
                    if (cancellationToken.IsCancellationRequested) return;
                }

                await DelayHelper.DelayAsync(5, 8);
                if (cancellationToken.IsCancellationRequested) return;

                Logger.LogInfo($"Tapping back into 'Home' tab on {deviceName}");
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 8.2, 7.2);

                await DelayHelper.DelayAsync(3, 5);  // Short delay after tap
                if (cancellationToken.IsCancellationRequested) return;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[HumanityActionSimulationScript1] Error during simulation: {ex}");
                // You can add more specific error handling or actions here
            }
        }


        public static async Task CheckAndLoginIfNeeded(string deviceName)
        {
            try
            {
                // Chụp ảnh màn hình và dùng OCR để trích xuất văn bản
                string extractedText = DeviceHelper.ExtractTextFromImage(deviceName);

                // Log the extracted text for debugging purposes
                Logger.LogInfo($"[OCR - Facebook Login] Extracted text from {deviceName}: {extractedText}");

                // Kiểm tra xem có chữ "login" hay không
                var loginMarkers = new[] { "mobile number or email", "số di động hoặc email" };
                if (!string.IsNullOrEmpty(extractedText) && loginMarkers.Any(marker => extractedText.ToLower().Contains(marker)))
                {
                    Logger.LogInfo($"[OCR] Found 'login' in screenshot of {deviceName}");

                    // Bắt đầu thao tác đăng nhập
                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 15.1, 44); // Focus vào input user
                    await DelayHelper.DelayAsync(2, 4);
                    await DeviceHelper.SendBroadcastMessage(LDType.Name, deviceName, deviceName);
                    await DelayHelper.DelayAsync(3, 5);

                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 15.1, 55); // Focus vào input password
                    await DelayHelper.DelayAsync(3, 5);
                    await DeviceHelper.SendBroadcastMessage(LDType.Name, deviceName, "Tuan123456@");
                    await DelayHelper.DelayAsync(3, 5);

                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 49.9, 64.7); // Tap nút login
                    await DelayHelper.DelayAsync(2, 3);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CheckAndLoginIfNeeded] Error on {deviceName}: {ex.Message}\n{ex.StackTrace}");
            }
        }


        public async Task HumanityActionSimulationScript2(string deviceName, CancellationToken cancellationToken)
        {
            var rand = new Random();

            int swipeCount = rand.Next(2, 4);

            for (int i = 0; i < swipeCount; i++)
            {
                double startY = rand.NextDouble() * 30 + 50;  // Start between 50% and 100%
                double endY = rand.NextDouble() * 30 + 10;    // End between 10% and 60%

                double x = 50;  // Swipe stays vertically centered

                await SwipeByPercentSafe(
                    LDType.Name,
                    deviceName,
                    x, startY,
                    x, endY,
                    cancellationToken: cancellationToken
                );

                int delayTime = rand.Next(4, 11) * 1000;

                if (isReactionSelected)
                {
                    await SelectReactionOption(deviceName);
                }

                await DelayHelper.DelayAsync(delayTime / 1000, delayTime / 1000);
                if (cancellationToken.IsCancellationRequested) return;
            }
        }


        private async Task SetupProxyAsync(string deviceName, ProxyDetails proxy)
        {
            // 1. Mở app Proxy
            Auto_LDPlayer.LDPlayer.RunApp(LDType.Name, deviceName, "com.cell47.College_Proxy");
            Logger.LogInfo($"[Proxy] {deviceName} dùng proxy: {proxy.Ip}:{proxy.Port}");

            // 2. Đợi app load
            await DelayHelper.DelayAsync(25, 35);

            // 3. OCR cấu hình ban đầu
            var config = DeviceHelper.ExtractProxyConfigFromDevice(deviceName);
            Logger.LogInfo($"[Proxy][{deviceName}] 📝 Cấu hình OCR lần 1: Host='{config.Host}', Port='{config.Port}', User='{config.Username}', Pass='{(string.IsNullOrWhiteSpace(config.Password) ? "[null]" : "[có]")}'");


            // 4. Nhập nếu sai
            if (config.Host != proxy.ProxyOriginIp)
                await DeviceHelper.InputFieldAsyncProxy(deviceName, 79.5, 52.7, proxy.ProxyOriginIp.ToString());

            if (config.Port != proxy.Port.ToString())
                await DeviceHelper.InputFieldAsyncProxy(deviceName, 82.5, 59.4, proxy.Port.ToString());

            if (config.Username != proxy.Username)
                await DeviceHelper.InputFieldAsyncProxy(deviceName, 82.5, 66.6, proxy.Username.ToString());

            await DeviceHelper.InputFieldAsyncProxy(deviceName, 82.5, 73.5, proxy.Password.ToString());

            // 5. Chờ nhập xong
            await Task.Delay(500);

            // 6. OCR lần 2
            var recheck = DeviceHelper.ExtractProxyConfigFromDevice(deviceName);
            Logger.LogInfo($"[Proxy][{deviceName}] 📝 Cấu hình OCR lần 1: Host='{recheck.Host}', Port='{recheck.Port}', User='{recheck.Username}', Pass='{(string.IsNullOrWhiteSpace(recheck.Password) ? "[null]" : "[có]")}'");


            // 7. Nhập lại chỗ nào còn sai
            bool hasMismatch = false;

            if (recheck.Host != proxy.ProxyOriginIp)
            {
                Logger.LogWarning($"[Proxy] Host vẫn sai sau lần nhập → Nhập lại.");
                await DeviceHelper.InputFieldAsyncProxy(deviceName, 79.5, 52.7, proxy.ProxyOriginIp);
                hasMismatch = true;
            }

            if (recheck.Port != proxy.Port.ToString())
            {
                Logger.LogWarning($"[Proxy] Port vẫn sai sau lần nhập → Nhập lại.");
                await DeviceHelper.InputFieldAsyncProxy(deviceName, 82.5, 59.4, proxy.Port.ToString());
                hasMismatch = true;
            }

            if (recheck.Username != proxy.Username)
            {
                Logger.LogWarning($"[Proxy] Username vẫn sai sau lần nhập → Nhập lại.");
                await DeviceHelper.InputFieldAsyncProxy(deviceName, 82.5, 66.6, proxy.Username);
                hasMismatch = true;
            }

            if (string.IsNullOrWhiteSpace(recheck.Password))
            {
                Logger.LogWarning($"[Proxy] Password vẫn trống sau lần nhập → Nhập lại.");
                await DeviceHelper.InputFieldAsyncProxy(deviceName, 82.5, 73.5, proxy.Password);
                hasMismatch = true;
            }

            if (hasMismatch)
            {
                Logger.LogInfo($"[Proxy] ⚠️ Đã nhập lại các field sai sau lần kiểm tra.");
                await Task.Delay(500);
            }

            // 8. Nhấn Submit
            DeviceHelper.TapByPercent(LDType.Name, deviceName, 49.4, 82.6);
            await DelayHelper.DelayAsync(4, 7);

            // 9. Start service
            DeviceHelper.TapByPercent(LDType.Name, deviceName, 72.5, 68.8);
            await DelayHelper.DelayAsync(4, 7);

            // 10. Tap OK
            DeviceHelper.TapImgBytes(LDType.Name, deviceName, PROXY_OK_BUTTON);
            await DelayHelper.DelayAsync(6, 9);

            // 11. Start Service lần nữa
            DeviceHelper.TapImgBytes(LDType.Name, deviceName, PROXY_START_SERVICE_BUTTON);
            await DelayHelper.DelayAsync(2, 3);

            DeviceHelper.TapByPercent(LDType.Name, deviceName, 72.1, 60.4);
            await DelayHelper.DelayAsync(2, 3);

            DeviceHelper.TapImgBytes(LDType.Name, deviceName, PROXY_START_SERVICE_BUTTON);

            // 10. Tap OK
            DeviceHelper.TapImgBytes(LDType.Name, deviceName, PROXY_OK_BUTTON);
            await DelayHelper.DelayAsync(6, 9);

            // 11. Start Service lần nữa
            DeviceHelper.TapImgBytes(LDType.Name, deviceName, PROXY_START_SERVICE_BUTTON);
            await DelayHelper.DelayAsync(2, 3);
        }


        private async Task SwipeGroupRandomlyAsync(
            GameFanpage fanpage,
            string deviceName,
            int swipeCount,
            bool reverse,
            bool reactAfterSwipe,
            CancellationToken cancellationToken)
        {
            var rand = new Random();
            var comments = RealisticCommentGenerator.GetValidComments(fanpage?.Comment);
            bool canComment = comments.Count > 0;


            if (!canComment)
                Logger.LogWarning("⚠️ No valid comments found. Skipping comment actions.");

            for (int i = 0; i < swipeCount; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                // Swipe position logic
                const double x = 50;
                double startY = reverse ? rand.NextDouble() * 30 + 20 : rand.NextDouble() * 30 + 50;
                double endY = reverse ? rand.NextDouble() * 30 + 50 : rand.NextDouble() * 30 + 20;

                await SwipeByPercentSafe(LDType.Name, deviceName, x, startY, x, endY, cancellationToken);

                if (reactAfterSwipe)
                    await SelectReactionOption(deviceName);

                // Comment mỗi 3 lần swipe nếu có comment
                //if (canComment && (i + 1) % 3 == 0 && isCommentProcess)
                //{
                //    string comment = rand.NextDouble() < 0.4
                //        ? comments[rand.Next(comments.Count)]
                //        : RealisticCommentGenerator.GenerateComment();

                //    await TryClickCommentIconAsync(deviceName, comment);
                //    await Task.Delay(rand.Next(1000, 2000)); // Optional delay sau khi comment
                //}

                //await DelayHelper.DelayAsync(3, 7);
            }
        }

 
        private static bool TryTapAnyCommentIcon(string deviceName, byte[][] commentIcons)
        {
            foreach (var icon in commentIcons)
            {
                if (icon == null || icon.Length == 0) continue;

                if (DeviceHelper.TapImgBytes(LDType.Name, deviceName, icon))
                {
                    return true; // đã tap vào icon hợp lệ
                }
            }
            return false;
        }



        private async Task TryClickCommentIconAsync(string deviceName, string commentText)
        {
            try
            {
                // Tạo danh sách các icon có thể dùng
                var commentIcons = new byte[][]
                {
                    FACEBOOK_POST_0_COMMENT_ICON,
                    FACEBOOK_POST_1_COMMENT_ICON
                };

                // Nếu không có icon nào khả dụng thì bỏ qua
                if (commentIcons.All(icon => icon == null || icon.Length == 0))
                {
                    return;
                }

                // Kiểm tra giới hạn comment
                if (commentCountByDevice.TryGetValue(deviceName, out int count) && count >= 4)
                {
                    Logger.LogInfo($"🛑 Đã đạt giới hạn 4 bình luận trên {deviceName}, bỏ qua.");
                    return;
                }

                // Cố gắng click vào 1 trong các icon
                bool tapped = false;
                for (int i = 0; i < 2 && !tapped; i++)
                {
                    tapped = TryTapAnyCommentIcon(deviceName, commentIcons);
                    if (!tapped)
                        await DelayHelper.DelayAsync(1, 2);
                }

                if (!tapped)
                {
                    return;
                }

                // Nhập nội dung bình luận
                await DelayHelper.DelayAsync(2, 4);
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 23.5, 88.0);
                await DelayHelper.DelayAsync(4, 6);

                await DeviceHelper.SendBroadcastMessage(LDType.Name, deviceName, commentText);
                await DelayHelper.DelayAsync(2, 3);

                DeviceHelper.TapImgBytes(LDType.Name, deviceName, FACEBOOK_SEND_COMMENT_BUTTON);
                await DelayHelper.DelayAsync(2, 3);

                // Quay lại màn hình trước
                if (DeviceHelper.CheckImgExistsInTopHalf(LDType.Name, deviceName, POST_GO_BACK_BTN))
                {
                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 6.2, 7.2);
                }
                else
                {
                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 42.9, 7.6);
                }

                // Cập nhật số lần bình luận
                if (!commentCountByDevice.ContainsKey(deviceName))
                    commentCountByDevice[deviceName] = 1;
                else
                    commentCountByDevice[deviceName]++;

                Logger.LogInfo($"💬 Đã bình luận trên {deviceName}: {commentText} (Lần {commentCountByDevice[deviceName]})");

                await DelayHelper.DelayAsync(3, 5);
            }
            catch (Exception ex)
            {
                Logger.LogError($"❌ [TryClickCommentIconAsync] Lỗi khi comment trên {deviceName}: {ex.Message}");
            }
        }


        private async Task ProcessDeviceAsync(string deviceName, LdPlayerDevices deviceFounded, DailyPostItem postSelectedByDevice, GameFanpage fanpage, List<FriendRequestProfile> listFriendRequest, CancellationToken cancellationToken)
        {
            int currentStep = 0;
            string postID = postSelectedByDevice?.Id;
            int deviceID = deviceFounded.ID;
            string nameOfGroup = fanpage?.MyGroup;

            try
            {
                // Step 1: Setup the device
                FacebookLiteHelper.SetupFacebookLiteEnvironment(deviceName);
                

                await DelayHelper.DelayAsync(10, 15);

                if (deviceFounded == null)
                {
                    Logger.LogError($"❌ deviceFounded is null for deviceName: {deviceName}");
                    return;
                }

                if (deviceFounded.Proxy == null)
                {
                    Logger.LogError($"❌ Proxy is null for device ID {deviceFounded.ID}, deviceName: {deviceName}");
                    return;
                }
                await SetupProxyAsync(deviceName, deviceFounded.Proxy);


                await DelayHelper.DelayAsync(10, 15);
                if (cancellationToken.IsCancellationRequested) return;

                // Step 2: Run app Facebook
                Auto_LDPlayer.LDPlayer.RunApp(LDType.Name, deviceName, "com.facebook.lite");

                await DelayHelper.DelayAsync(10, 15);
                if (cancellationToken.IsCancellationRequested) return;


                // Kiểm tra nếu user chưa login thì tiến hành đăng 
                await CheckAndLoginIfNeeded(deviceName);

                if (isAccountMantainanceProcess)
                {
                    await HumanityActionSimulationScript1(deviceName, cancellationToken);
                }



                // ------------------------------------ Tương tác group------------------------------------ //
                // Step 3: Tap search button
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 79.5, 6.7);

                await DelayHelper.DelayAsync(4, 8);
                if (cancellationToken.IsCancellationRequested) return;

                // Step 4: Tap for text input
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 26.5, 7.9);

                await DelayHelper.DelayAsync(3, 5);
                if (cancellationToken.IsCancellationRequested) return;

                // Step 5: Input search text for group
                Auto_LDPlayer.LDPlayer.InputText(LDType.Name, deviceName, nameOfGroup);

                await DelayHelper.DelayAsync(3, 5);
                if (cancellationToken.IsCancellationRequested) return;

                // Step 6: Press Enter key to submit
                Auto_LDPlayer.LDPlayer.PressKey(LDType.Name, deviceName, LDKeyEvent.KEYCODE_ENTER);

                await DelayHelper.DelayAsync(5, 9);
                if (cancellationToken.IsCancellationRequested) return;

                // Step 7: Thử tối đa 3 lần tìm và tap TRUY_CAP_BMP
                const int maxAttemptsGroup = 3;
                bool tappedGroup = false;

                for (int attempt = 1; attempt <= maxAttemptsGroup; attempt++)
                {
                    using (var truyCapBmp = BitmapHelper.CreateFromBytes(TRUY_CAP_BMP))
                    {
                        if (DeviceHelper.TapImgHalf(LDType.Name, deviceName, truyCapBmp))
                        {
                            tappedGroup = true;

                            // -------------------- Scroll and comment on posts -------------------- //
                            await SwipeGroupRandomlyAsync(fanpage, deviceName, 50, reverse: false, isReactionSelected, cancellationToken); // Swipe xuống
                            // -------------------- Scroll and comment on posts -------------------- //

                            break;
                        }
                    }

                    // Nếu chưa tìm thấy, thử bấm THAM_GIA_BMP
                    using (var thamGiaBmp = BitmapHelper.CreateFromBytes(THAM_GIA_BMP))
                    {
                        DeviceHelper.TapImgHalf(LDType.Name, deviceName, thamGiaBmp);
                    }

                    // Đợi ngắn rồi thử lại
                    await DelayHelper.DelayAsync(2, 4);
                    if (cancellationToken.IsCancellationRequested) return;

                    var randSwipe = new Random();
                    double startY = randSwipe.NextDouble() * 30 + 10;
                    double endY = randSwipe.NextDouble() * 30 + 50;
                    const double x = 50;

                    await SwipeByPercentSafe(
                        LDType.Name,
                        deviceName,
                        x, startY,
                        x, endY,
                        cancellationToken
                    );
                }


                if (tappedGroup && isPostGroupProcess)
                {
                    // Found & tapped, proceed to viết bài
                    await HandleActionPostIntoGroup(deviceName, postSelectedByDevice, cancellationToken);
                }
                else
                {
                    Logger.LogError($"Device {deviceName}: TRUY_CAP_BMP not found after {maxAttemptsGroup} attempts");
                }

                // Go back into facebook home page
                for (int i = 0; i < 4; i++)
                {
                    Auto_LDPlayer.LDPlayer.Back(LDType.Name, deviceName);

                    int delayTime = new Random().Next(3, 5) * 1000; // Random delay between 4 and 10 seconds
                    await DelayHelper.DelayAsync(delayTime / 1000, delayTime / 1000);
                    if (cancellationToken.IsCancellationRequested) return;
                }

                await DelayHelper.DelayAsync(7, 15);
                if (cancellationToken.IsCancellationRequested) return;

                await HumanityActionSimulationScript2(deviceName, cancellationToken);
                // ------------------------------------ Tương tác group------------------------------------ //


                // Tương tác fanpage chính
                Random randomSeedingFanpageProcess = new Random();
                int chanceSeedingFanpageProcess = randomSeedingFanpageProcess.Next(0, 100); // Generates a number between 0 and 99
                if (isSeedingFanpageProcess && (chanceSeedingFanpageProcess < 30))
                {
                    // Step 3: Tap search button
                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 79.5, 6.7);

                    await DelayHelper.DelayAsync(4, 8);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 4: Tap for text input
                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 26.5, 7.9);

                    await DelayHelper.DelayAsync(3, 5);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 5: Input search text for group
                    Auto_LDPlayer.LDPlayer.InputText(LDType.Name, deviceName, "https://www.facebook.com/BoostgameNTE");

                    await DelayHelper.DelayAsync(3, 5);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 6: Press Enter key to submit
                    Auto_LDPlayer.LDPlayer.PressKey(LDType.Name, deviceName, LDKeyEvent.KEYCODE_ENTER);

                    await DelayHelper.DelayAsync(5, 9);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 7: Thử tối đa 3 lần tìm và tap TRUY_CAP_BMP
                    const int maxAttempts = 3;
                    bool tapped = false;

                    for (int attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        if (DeviceHelper.TapImgHalf(LDType.Name, deviceName, FANPAGE_BOOST_GAME_STORE_NAME))
                        {
                            tapped = true;
                            break;
                        }
                    }

                    await DelayHelper.DelayAsync(3, 5);
                    if (cancellationToken.IsCancellationRequested) return;

                    // -------------------- Scroll and comment on posts -------------------- //
                    var rand = new Random();
                    int swipeCounter = 0;
                    int sharePostPageCount = 0; // Initialize a counter for sharing posts
                    for (int i = 0; i < 30; i++)
                    {
                        double startY = rand.NextDouble() * 30 + 50;
                        double endY = rand.NextDouble() * 30 + 20;
                        const double x = 50;

                        int chanceSharePage = _random.Next(0, 100);  // 0-99

                        await SwipeByPercentSafe(LDType.Name, deviceName, x, startY, x, endY, cancellationToken);

                        using (var fanpagelikeBmp = BitmapHelper.CreateFromBytes(FANPAGE_LIKE_PAGE_BUTTON))
                        {
                            DeviceHelper.TapImgHalf(LDType.Name, deviceName, fanpagelikeBmp);
                        }

                        if (isReactionSelected)
                        {
                            await SelectReactionOption(deviceName);
                        }

                        swipeCounter++;

                        if (swipeCounter % 5 == 0 && isCommentProcess)
                        {
                            await TryClickCommentIconAsync(deviceName, RealisticCommentGenerator.GenerateComment());
                        }

                        await DelayHelper.DelayAsync(1, 2);

                        if (isSharePostProcess && sharePostPageCount < 2 && chanceSharePage < 10)
                        {
                            await SharePostAction(deviceName);
                            sharePostPageCount++;
                        }

                        await DelayHelper.DelayAsync(3, 5);

                        if (cancellationToken.IsCancellationRequested)
                            return;
                    }

                    Auto_LDPlayer.LDPlayer.Back(LDType.Name, deviceName);
                    // -------------------- Scroll and comment on posts -------------------- //



                    if (tapped)
                    {
                        // Found & tapped, proceed to viết bài
                        await HandleActionPostIntoGroup(deviceName, postSelectedByDevice, cancellationToken);
                    }
                    else
                    {
                        Logger.LogError($"Device {deviceName}: TRUY_CAP_BMP not found after {maxAttempts} attempts");
                    }

                    // Go back into facebook home page
                    for (int i = 0; i < 4; i++)
                    {
                        DeviceHelper.TapByPercent(LDType.Name, deviceName, 6.4, 7.2);

                        int delayTime = new Random().Next(3, 5) * 1000; // Random delay between 4 and 10 seconds
                        await DelayHelper.DelayAsync(delayTime / 1000, delayTime / 1000);
                        if (cancellationToken.IsCancellationRequested) return;
                    }

                    await DelayHelper.DelayAsync(7, 15);
                    if (cancellationToken.IsCancellationRequested) return;

                    await HumanityActionSimulationScript2(deviceName, cancellationToken);
                }

                // Step 5: Handle friend requests
                Random randomFriendRequestProcess = new Random();
                int chanceFriendRequestProcess = randomFriendRequestProcess.Next(0, 100); // Generates a number between 0 and 99
                if (isFriendRequestProcess && (chanceFriendRequestProcess < 15))
                {
                    await HandleFriendRequestProcessAsync(deviceName, listFriendRequest, cancellationToken);
                }

                // Final: Complete the task after submitting the post
                await ApiHelper.CompleteTaskAsync(deviceName, deviceID, postID);

                await DelayHelper.DelayAsync(3, 5);
                if (cancellationToken.IsCancellationRequested) return;
            }
            catch (Exception ex)
            {
                // Log the error                                                            
                Logger.LogError($"[ProcessDeviceAsync] Unexpected error at Step {currentStep} for device {deviceName}: {ex}");

                Auto_LDPlayer.LDPlayer.Close(LDType.Name, deviceName);
                Auto_LDPlayer.LDPlayer.SortWnd();
                Logger.LogInfo($"Device {deviceName} closed due to error.");
            }
        }


        // Helper function to handle friend requests
        private async Task HandleFriendRequestProcessAsync(string deviceName, List<FriendRequestProfile> listFriendRequest, CancellationToken cancellationToken)
        {
            // Get a random profile link from the friend request list
            FriendRequestProfile profileFriendRequestLink = GetRandomProfileDetail(listFriendRequest);
            string profileFriendLink = profileFriendRequestLink?.ProfileLink;

            if (profileFriendRequestLink != null && !string.IsNullOrEmpty(profileFriendLink))
            {
                // Step: Tap search button
                await TapLocationAsync(deviceName, 77.5, 7.1);

                // Step: Tap for text input
                await TapLocationAsync(deviceName, 26.5, 7.9);

                // Press delete key to clear text
                Auto_LDPlayer.LDPlayer.PressKey(LDType.Name, deviceName, LDKeyEvent.KEYCODE_DEL);
                await DelayHelper.DelayAsync(5, 9);
                if (cancellationToken.IsCancellationRequested) return;

                // Input the profile link
                await DeviceHelper.SendBroadcastMessage(LDType.Name, deviceName, profileFriendLink);
                await DelayHelper.DelayAsync(6, 10);
                if (cancellationToken.IsCancellationRequested) return;

                // Press Enter key to submit
                Auto_LDPlayer.LDPlayer.PressKey(LDType.Name, deviceName, LDKeyEvent.KEYCODE_ENTER);
                await DelayHelper.DelayAsync(6, 9);
                if (cancellationToken.IsCancellationRequested) return;

                // Check if either of the two buttons exists in the top half of the screen
                if (DeviceHelper.CheckImgExistsInTopHalf(LDType.Name, deviceName, FACEBOOK_FRIENDS_FOLLOWER_REQUEST_BUTTON) ||
                    DeviceHelper.CheckImgExistsInTopHalf(LDType.Name, deviceName, FACEBOOK_FRIEND_VIEW_PROFILE_BUTTON))
                {
                    // If found, delete the friend request
                    await ApiHelper.DeleteFriendRequestAsync(deviceName, profileFriendRequestLink?.Uuid);
                }
                else if (DeviceHelper.TapImgHalf(LDType.Name, deviceName, FACEBOOK_ADD_FRIENDS_REQUEST_BUTTON))
                {
                    // If neither of the above, complete the friend request
                    await ApiHelper.CompleteFriendRequestAsync(deviceName, profileFriendRequestLink?.Uuid);
                }


                await DelayHelper.DelayAsync(6, 9);
                if (cancellationToken.IsCancellationRequested) return;

                // Go back to Facebook home page
                await GoBackToHomePageAsync(deviceName, cancellationToken);
            }
        }


        private async Task HandleActionPostIntoGroup(string deviceName, DailyPostItem postSelectedByDevice, CancellationToken cancellationToken)
        {
            string postContent = postSelectedByDevice?.Content;
            List<string> postImage = postSelectedByDevice?.PostImage;

            try
            {
                await DelayHelper.DelayAsync(5, 8);
                if (cancellationToken.IsCancellationRequested) return;

                // Step 8: Try tapping the first image for writing a post
                if (!DeviceHelper.TapImgAsync(LDType.Name, deviceName, INPUT_WRITE_POST_BMP_1))
                {
                    Logger.LogInfo($"Device {deviceName}: INPUT_WRITE_POST_BMP_1 not found, trying INPUT_WRITE_POST_BMP_2...");
                    if (!DeviceHelper.TapImgAsync(LDType.Name, deviceName, INPUT_WRITE_POST_BMP_2))
                    {
                        DeviceHelper.TapByPercent(LDType.Name, deviceName, 46.1, 77.0);
                    }
                    else
                    {
                        Logger.LogError($"Device {deviceName}: INPUT_WRITE_POST_BMP_2 not found");
                    }
                }

                await DelayHelper.DelayAsync(5, 8);
                if (cancellationToken.IsCancellationRequested) return;

                // Step 9: Tap on the location to input the post content
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 48.2, 44.0);

                await DelayHelper.DelayAsync(5, 8);
                if (cancellationToken.IsCancellationRequested) return;

                // Step 10: Send the post content as a broadcast message
                await DeviceHelper.SendBroadcastMessage(LDType.Name, deviceName, postContent);

                await DelayHelper.DelayAsync(8, 10);
                if (cancellationToken.IsCancellationRequested) return;

                // Check if has image, will post image with count
                if (postImage.Count > 0)
                {
                    if (!DeviceHelper.TapImgAsync(LDType.Name, deviceName, FACEBOOK_POST_IMPORT_IMAGE_1_BMP))
                    {
                        DeviceHelper.TapImgAsync(LDType.Name, deviceName, FACEBOOK_POST_IMPORT_IMAGE_2_BMP);
                    }

                    await DelayHelper.DelayAsync(3, 5);
                    if (cancellationToken.IsCancellationRequested) return;

                    if (DeviceHelper.TapImgAsync(LDType.Name, deviceName, FACEBOOK_ACCEPT_ACCESS_CAMERA_BMP))
                    {
                        await DelayHelper.DelayAsync(3, 5);
                        DeviceHelper.TapImgAsync(LDType.Name, deviceName, FACEBOOK_ALLOW_ACCESS_BMP);
                    }

                    await DelayHelper.DelayAsync(3, 5);

                    Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)264.3, (int)276.0, 200);
                    if (postImage.Count == 2)
                    {
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)458.1, 199, 200);
                    }
                    else if (postImage.Count == 3)
                    {
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)458.1, 199, 200);
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)84.5, (int)382.0, 200);
                    }
                    else if (postImage.Count == 4)
                    {
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)458.1, 199, 200);
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)84.5, (int)382.0, 200);
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)269.1, (int)388.9, 200);
                    }
                    else if (postImage.Count == 5)
                    {
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)458.1, 199, 200);
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)84.5, (int)382.0, 200);
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)269.1, (int)388.9, 200);
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)445.2, (int)388.9, 200);
                    }

                    await DelayHelper.DelayAsync(7, 9);
                    if (cancellationToken.IsCancellationRequested) return;


                    if (DeviceHelper.TapImgAsync(LDType.Name, deviceName, FACEBOOK_POST_IMAGE_SELECTION_1_BMP))
                    {
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.TapByPercent(LDType.Name, deviceName, 49.6, 95.2);
                    }
                    else
                    {
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)264.3, (int)276.0, 200);

                        if (DeviceHelper.TapImgAsync(LDType.Name, deviceName, FACEBOOK_POST_IMAGE_SELECTION_1_BMP))
                        {
                            await DelayHelper.DelayAsync(1, 2);
                            Auto_LDPlayer.LDPlayer.TapByPercent(LDType.Name, deviceName, 49.6, 95.2);
                        }
                        else
                        {
                            Auto_LDPlayer.LDPlayer.Back(LDType.Name, deviceName);
                        }
                    }

                    await DelayHelper.DelayAsync(3, 5);
                    if (cancellationToken.IsCancellationRequested) return;

                    Auto_LDPlayer.LDPlayer.Back(LDType.Name, deviceName);
                }


                await DelayHelper.DelayAsync(3, 5);
                if (cancellationToken.IsCancellationRequested) return;

                // Step 11: Final tap to submit the post
                DeviceHelper.TapByPercent(LDType.Name, deviceName, 86.7, 6.7);

                await DelayHelper.DelayAsync(8, 10);
                if (cancellationToken.IsCancellationRequested) return;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[HandleActionPostIntoGroup] Error processing device {deviceName}: {ex.Message}");
            }
        }
    }
}
