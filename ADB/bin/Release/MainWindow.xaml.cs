using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Auto_LDPlayer;
using Auto_LDPlayer.Enums;
using KAutoHelper;

using ADB_Tool_Automation_Post_FB.Models;
using ADB_Tool_Automation_Post_FB.Helpers;
using ADB_Tool_Automation_Post_FB.Exceptions;

namespace ADB_Tool_Automation_Post_FB
{
    public partial class MainWindow : Window
    {
        #region data
        Bitmap TRUY_CAP_BMP;
        Bitmap THAM_GIA_BMP;

        Bitmap INPUT_WRITE_POST_BMP_1;
        Bitmap INPUT_WRITE_POST_BMP_2;
        Bitmap LIKE_POST_FACEBOOK_BUTTON_BMP;

        Bitmap SELECT_TIENG_VIET_BUTTON;
        Bitmap ACCEPT_NGON_NGU_BUTTON;
        Bitmap FACEBOOK_ACCEPT_FRIENDS_BUTTON;
        Bitmap FACEBOOK_ADD_FRIEND_BUTTON_IN_NEWFEED_TAB;
        Bitmap FACEBOOK_ADD_FRIEND_BUTTON_IN_FRIENDS_TAB;

        Bitmap EMOJI_THICH_BUTTON;
        Bitmap EMOJI_HAHA_BUTTON;
        Bitmap EMOJI_WOW_BUTTON;
        Bitmap EMOJI_YEU_THICH_BUTTON;
        Bitmap EMOJI_THUONG_THUONG_BUTTON;

        Bitmap FACEBOOK_ADD_FRIENDS_REQUEST_BUTTON;
        Bitmap FACEBOOK_FRIENDS_FOLLOWER_REQUEST_BUTTON;
        Bitmap FACEBOOK_FRIEND_VIEW_PROFILE_BUTTON;
        Bitmap FACEBOOK_POST_COMMENT_ICON;

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

        private readonly int limitDevice = 10;

        bool isStop = false;
        private bool isReactionSelected = true;
        private bool isPostGroupProcess = false;

        private readonly bool isAccountMantainanceProcess = true;
        private readonly bool isFriendRequestProcess = true;
        private readonly bool isCommentPostProcess = true;
        private readonly bool isSharePostProcess = true;

        private string pcRunner = "pc_1"; // Default value for pc_runner

        private static string API_BASE_URL = "https://boostgamemobile.com/service";
        private static string LDCONSOLE_PATH = @"D:\LDPlayer\LDPlayer9\ldconsole.exe";

        private const string DevicesAvailablePath = "/ldplayer_devices/available";
        private const string CompleteTaskInDevicePath = "/ldplayer_devices/complete-task/by-name";
        private const string DailyPostsContentPath = "/daily_posts_content/available?list_devices=";
        private const string DailyPostsContentComplete = "/daily_posts_content/update-device/{0}/{1}";
        private const string gameFanpagePath = "/game_fanpages?page=1&limit=300";
        private const string facebookFriendRequestPath = "/friend_list_group_game/available?list_devices=";
        private const string facebookFriendRequestSuccessPath = "/friend_list_group_game/increment-used-count";
        private const string facebookFriendRequestDeletePath = "/friend_list_group_game/delete";

        private static readonly Random _random = new Random();

        private static readonly Dictionary<int, Bitmap> emojiButtonsRaw = new Dictionary<int, Bitmap>();
        private static readonly object emojiLock = new object();

        public MainWindow()
        {
            InitializeComponent();
            BitmapManager.LoadAll();
            LoadDataImage();
            LoadStaticLDConsole();

            emojiButtonsRaw[0] = EMOJI_THICH_BUTTON;
            emojiButtonsRaw[1] = EMOJI_HAHA_BUTTON;
            emojiButtonsRaw[2] = EMOJI_WOW_BUTTON;
            emojiButtonsRaw[3] = EMOJI_YEU_THICH_BUTTON;
            emojiButtonsRaw[4] = EMOJI_THUONG_THUONG_BUTTON;

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
            Auto_LDPlayer.LDPlayer.PathLD = LDCONSOLE_PATH;
        }

        private void LoadDataImage()
        {
            TRUY_CAP_BMP = (Bitmap)Bitmap.FromFile("Data\\Access_img_1.png");
            INPUT_WRITE_POST_BMP_1 = (Bitmap)Bitmap.FromFile("Data\\Write_img_1.png");
            INPUT_WRITE_POST_BMP_2 = (Bitmap)Bitmap.FromFile("Data\\Write_img_2.png");
            THAM_GIA_BMP = (Bitmap)Bitmap.FromFile("Data\\Join_img_1.png");

            SELECT_TIENG_VIET_BUTTON = (Bitmap)Bitmap.FromFile("Data\\set_tieng_viet_button.png");
            ACCEPT_NGON_NGU_BUTTON = (Bitmap)Bitmap.FromFile("Data\\accept_ngon_ngu_button.png");
            FACEBOOK_ACCEPT_FRIENDS_BUTTON = (Bitmap)Bitmap.FromFile("Data\\accept_friends_button.png");

            LIKE_POST_FACEBOOK_BUTTON_BMP = (Bitmap)Bitmap.FromFile("Data\\like_button.png");
            EMOJI_THICH_BUTTON = (Bitmap)Bitmap.FromFile("Data\\emoji_thich_button.png");
            EMOJI_HAHA_BUTTON = (Bitmap)Bitmap.FromFile("Data\\emoji_haha_button.png");
            EMOJI_WOW_BUTTON = (Bitmap)Bitmap.FromFile("Data\\emoji_wow_button.png");
            EMOJI_YEU_THICH_BUTTON = (Bitmap)Bitmap.FromFile("Data\\emoji_yeu_thich_button.png");
            EMOJI_THUONG_THUONG_BUTTON = (Bitmap)Bitmap.FromFile("Data\\emoji_thuong_thuong_button.png");

            FACEBOOK_ADD_FRIENDS_REQUEST_BUTTON = (Bitmap)Bitmap.FromFile("Data\\add_friends_request_button.png");
            FACEBOOK_FRIENDS_FOLLOWER_REQUEST_BUTTON = (Bitmap)Bitmap.FromFile("Data\\friends_follower_request_button.png");
            FACEBOOK_FRIEND_VIEW_PROFILE_BUTTON = (Bitmap)Bitmap.FromFile("Data\\friends_view_profile_button.png");
            FACEBOOK_ADD_FRIEND_BUTTON_IN_NEWFEED_TAB = (Bitmap)Bitmap.FromFile("Data\\btn_them_ban_be_newfeed_tab.png");
            FACEBOOK_ADD_FRIEND_BUTTON_IN_FRIENDS_TAB = (Bitmap)Bitmap.FromFile("Data\\btn_them_ban_be_friend_tab.png");
            FACEBOOK_POST_COMMENT_ICON = (Bitmap)Bitmap.FromFile("Data\\post_comment_icon.png");

            FACEBOOK_POST_IMPORT_IMAGE_1_BMP = (Bitmap)Bitmap.FromFile("Data\\post_import_image_1.png");
            FACEBOOK_POST_IMPORT_IMAGE_2_BMP = (Bitmap)Bitmap.FromFile("Data\\post_import_image_2.png");

            FACEBOOK_ACCEPT_ACCESS_CAMERA_BMP = (Bitmap)Bitmap.FromFile("Data\\facebook_accept_access_camera.png");
            FACEBOOK_ALLOW_ACCESS_BMP = (Bitmap)Bitmap.FromFile("Data\\facebook_allow_access.png");
            FACEBOOK_POST_IMAGE_SELECTION_1_BMP = (Bitmap)(Bitmap.FromFile("Data\\post_image_selection_1.png"));

            FACEBOOK_SHARE_POST_FEED = (Bitmap)Bitmap.FromFile("Data\\share_post_button.png");
            SELECTION_POST_INTO_FACEBOOK = (Bitmap)Bitmap.FromFile("Data\\selection_post_into_facebook.png");

        }

        public async Task FetchResourceForDeviceRunAPI()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    await FetchAvailableLDPlayerDevicesFromAPI(client);

                    if (allLDPlayerDevices?.Items != null && allLDPlayerDevices.Items.Count > 0)
                    {
                        await FetchDailyPostsContentAPI(client);
                        await FetchGameFanpagesAPI(client);
                        await FetchFriendListAPI(client);
                    }
                    else
                    {
                        Logger.LogInfo("No devices returned; skipping daily-posts, fanpages, and friend-list fetch.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in FetchResourceForDeviceRunAPI: {ex.Message}");
            }
        }


        private async Task FetchPostImageSaveIntoSDCard(string deviceName, List<string> postImage)
        {
            using (HttpClient client = new HttpClient())
            {
                if (dailyPostAvailable == null || dailyPostAvailable.Count == 0 || allLDPlayerDevices?.Items == null || allLDPlayerDevices.Items.Count == 0)
                {
                    return;
                }

                // Define the local folder path based on the current directory and deviceName
                string currentDirectory = Directory.GetCurrentDirectory();
                string localFolderPath = Path.Combine(currentDirectory, "Shared_images_ldplayer", deviceName);

                // Create the local folder if it doesn't exist
                if (!Directory.Exists(localFolderPath))
                {
                    Directory.CreateDirectory(localFolderPath);
                }

                // Delete all files in the folder before downloading new images
                var files = Directory.GetFiles(localFolderPath);
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[{deviceName}] Error deleting image {file}: {ex.Message}");
                    }
                }

                // Download images and save them locally in the specified folder
                foreach (var imageName in postImage)
                {
                    string imageUrl = $"{API_BASE_URL}/daily_posts_content/image?file={Uri.EscapeDataString(imageName)}";

                    try
                    {
                        // Retry logic for image download
                        var imageData = await client.GetByteArrayAsync(imageUrl);

                        // Save image to the device-specific folder
                        string filePath = Path.Combine(localFolderPath, imageName);
                        using (var fs = new FileStream(
                            filePath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 4096,
                            useAsync: true))
                        {
                            await fs.WriteAsync(imageData, 0, imageData.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[{deviceName}] Error processing image {imageName} for post: {ex.Message}");
                    }
                }
            }
        }

        private async Task FetchAvailableLDPlayerDevicesFromAPI(HttpClient client)
        {
            try
            {
                string deviceUrl = $"{API_BASE_URL}{DevicesAvailablePath}?limit={limitDevice}&pc_runner={pcRunner}";
                Logger.LogInfo($"Calling API: {deviceUrl}");

                // Call device API and handle possible failures
                HttpResponseMessage response = await client.GetAsync(deviceUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError($"Error fetching devices. Status code: {response.StatusCode}");
                    return; // Exit early if the API request fails
                }

                string responseBody = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // Deserialize response into ApiResponseLDPlayerDevices
                ApiResponseLDPlayerDevices responseDataAvailableDevices = JsonSerializer.Deserialize<ApiResponseLDPlayerDevices>(responseBody, options);
                allLDPlayerDevices = responseDataAvailableDevices;
                if (responseDataAvailableDevices?.Items == null || !responseDataAvailableDevices.Items.Any())
                {
                    Logger.LogInfo("No devices found in API response.");
                    return;
                }

                // Process available devices

                ldplayerDevicesAvailable = responseDataAvailableDevices?.Items?.Select(device => device.DeviceName).ToList();
                listDeviceID = responseDataAvailableDevices?.Items?.Select(device => device.ID).ToList();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error fetching device list content: {ex.Message}");
            }
        }

        private async Task FetchDailyPostsContentAPI(HttpClient client)
        {
            try
            {
                string deviceIdsParam = string.Join(",", listDeviceID);
                string dailyPostsContentUrl = $"{API_BASE_URL}{DailyPostsContentPath}{Uri.EscapeDataString(deviceIdsParam)}";
                Logger.LogInfo($"Calling API: {dailyPostsContentUrl}");

                HttpResponseMessage dailyResponse = await client.GetAsync(dailyPostsContentUrl);
                if (!dailyResponse.IsSuccessStatusCode)
                {
                    Logger.LogError($"Error fetching daily post content. Status code: {dailyResponse.StatusCode}");
                    return; // Exit early if the API request fails
                }
                dailyResponse.EnsureSuccessStatusCode();

                string dailyContentResponse = await dailyResponse.Content.ReadAsStringAsync();
                var dailyPostsContent = JsonSerializer.Deserialize<DailyPostsContentResponse>(dailyContentResponse);
                if (dailyPostsContent?.Items?.Any() == true)
                {
                    dailyPostAvailable = dailyPostsContent.Items;
                }
                else
                {
                    Logger.LogInfo("No daily posts content items found in API response.");
                    await Dispatcher.InvokeAsync(() =>
                    {
                        deviceStatusLabel.Content = "Không có bài viết mới.";
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error fetching daily posts content: {ex.Message}");
            }
        }

        private async Task FetchGameFanpagesAPI(HttpClient client)
        {
            try
            {
                string gameFanpageUrl = $"{API_BASE_URL}{gameFanpagePath}";
                Logger.LogInfo($"Calling API: {gameFanpageUrl}");

                HttpResponseMessage gameFanpageResponse = await client.GetAsync(gameFanpageUrl);
                if (!gameFanpageResponse.IsSuccessStatusCode)
                {
                    Logger.LogError($"Error fetching game fanpage. Status code: {gameFanpageResponse.StatusCode}");
                    return;
                }

                string gameFanpageContent = await gameFanpageResponse.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var gameFanpageResponseObj = JsonSerializer.Deserialize<GameFanpageResponse>(gameFanpageContent, options);

                if (gameFanpageResponseObj?.Data?.Items != null && gameFanpageResponseObj.Data.Items.Any())
                {
                    listGameFanpage = gameFanpageResponseObj.Data.Items;
                    Logger.LogInfo($"Fetched {listGameFanpage.Count} game fanpages successfully.");
                }
                else
                {
                    Logger.LogInfo("No game fanpages found in API response.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error fetching game fanpages: {ex.Message}");
            }
        }


        private async Task FetchFriendListAPI(HttpClient client)
        {
            try
            {
                string deviceIdsParam = string.Join(",", listDeviceID);
                string apiUrl = $"{API_BASE_URL}{facebookFriendRequestPath}{Uri.EscapeDataString(deviceIdsParam)}";
                Logger.LogInfo($"Calling API: {apiUrl}");

                HttpResponseMessage responseFriendsRequest = await client.GetAsync(apiUrl);
                if (!responseFriendsRequest.IsSuccessStatusCode)
                {
                    Logger.LogError($"Error fetching friends request. Status code: {responseFriendsRequest.StatusCode}");
                    return; // Exit early if the API request fails
                }
                responseFriendsRequest.EnsureSuccessStatusCode();

                string responseContent = await responseFriendsRequest.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<ApiResponseFriendList>(responseContent);

                if (apiResponse?.Data?.Any() == true)
                {
                    listFriendRequestProfile = apiResponse.Data;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error fetching friend list: {ex.Message}");
            }
        }


        private void Button_Click_Start(object sender, RoutedEventArgs e)
        {
            Button_Start.IsEnabled = false;
            Button_Stop.IsEnabled = true;

            isStop = false;
            Dispatcher.InvokeAsync(() =>
            {
                deviceStatusLabel.Content = $"Đang khởi tạo thiết bị mới";
            });

            Task.Run(async () => await MainAutoRunFunction());
        }

        private void Button_Click_Stop(object sender, RoutedEventArgs e)
        {
            // Disable Stop button and enable Start button
            Button_Stop.IsEnabled = false;
            Button_Start.IsEnabled = true;

            isStop = true;

            if (ldplayerDevicesAvailable.Count == 0)
            {
                // If the list is empty, close all devices
                Auto_LDPlayer.LDPlayer.CloseAll();
                Logger.LogInfo("No devices in the list. Closed all devices.");
            }
            else
            {
                // Close devices from the list
                foreach (var device in ldplayerDevicesAvailable)
                {
                    Auto_LDPlayer.LDPlayer.Close(LDType.Name, device);
                }
            }

            // Update UI to show which device is being processed
            Dispatcher.InvokeAsync(() =>
            {
                deviceStatusLabel.Content = $"Đã kết thúc tiến trình";
            });
        }

        private void Button_Click_ShowLog(object sender, RoutedEventArgs e)
        {
            string logFilePath = Logger.LogFilePath; // Path to your log file

            if (File.Exists(logFilePath))
            {
                Process.Start("notepad.exe", logFilePath);
            }
            else
            {
                MessageBox.Show("Log file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void EnvSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            if (comboBox == null)
                return;
            ComboBoxItem selectedItem = comboBox.SelectedItem as ComboBoxItem;
            if (selectedItem == null)
                return;

            string selectedValue = selectedItem.Content != null ? selectedItem.Content.ToString() : string.Empty;

            switch (selectedValue)
            {
                case "Local":
                    API_BASE_URL = "http://127.0.0.1:8080/service";
                    LDCONSOLE_PATH = @"C:\LDPlayer\LDPlayer9\ldconsole.exe";
                    break;
                case "Production":
                    API_BASE_URL = "https://boostgamemobile.com/service";
                    LDCONSOLE_PATH = @"D:\LDPlayer\LDPlayer9\ldconsole.exe";
                    break;
                default:
                    break;
            }

            Auto_LDPlayer.LDPlayer.PathLD = LDCONSOLE_PATH;
        }

        private void PcRunnerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            if (comboBox == null)
                return;
            ComboBoxItem selectedItem = comboBox.SelectedItem as ComboBoxItem;
            if (selectedItem == null)
                return;

            string selectedValue = selectedItem.Content != null ? selectedItem.Content.ToString() : string.Empty;

            switch (selectedValue)
            {
                case "Máy ngoài 1":
                    pcRunner = "pc_1";
                    break;
                case "Máy ngoài 2":
                    pcRunner = "pc_2";
                    break;
                case "Máy trong 1":
                    pcRunner = "pc_3";
                    break;
                default:
                    break;
            }
        }

        private void TaskTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 1) Make sure we actually have a ComboBox
            if (!(sender is ComboBox combo))
                return;

            // 2) Use SelectedIndex instead of string-content comparisons
            switch (combo.SelectedIndex)
            {
                case 0: // “Nuôi Acc”
                    isReactionSelected = true;
                    isPostGroupProcess = false;
                    break;

                case 1: // “Nuôi Acc + Đăng bài + Kết bạn”
                    isReactionSelected = true;
                    isPostGroupProcess = true;
                    break;

                default:
                    isReactionSelected = true;
                    isPostGroupProcess = true;
                    break;
            }
        }


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

                // Bước 4: Mở LDPlayer song song (tối đa 3 máy mỗi lượt)
                var semaphore = new SemaphoreSlim(5); // Tối đa 3 máy mở cùng lúc
                var openTasks = new List<Task>();

                foreach (var deviceName in ldplayerDevicesAvailable)
                {
                    openTasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var device = allLDPlayerDevices?.Items?.FirstOrDefault(d => d.DeviceName == deviceName);
                            if (device == null) return;

                            // Download ảnh bài viết trước
                            await FetchPostImageSaveIntoSDCard(deviceName, dailyPostAvailable?.FirstOrDefault(d => d.LDPlayerDevicesId == device.ID)?.PostImage);

                            // Kiểm tra và mở LDPlayer nếu chưa chạy
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
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(openTasks);

                // Bước 5: Sắp xếp cửa sổ
                Auto_LDPlayer.LDPlayer.SortWnd();
                await DelayHelper.DelayAsync(2, 3);
                DeviceHelper.SafeUpdateUI(deviceStatusLabel, $"Đang chạy: {DeviceHelper.CountTotalRunningDevices(LDType.Name, ldplayerDevicesAvailable)} thiết bị.");

                // Bước 6: Xử lý song song từng thiết bị
                var taskList = new List<Task>();
                foreach (var deviceName in ldplayerDevicesAvailable)
                {
                    var device = allLDPlayerDevices?.Items?.FirstOrDefault(d => d.DeviceName == deviceName);
                    if (device == null) continue;

                    var postItem = dailyPostAvailable?.FirstOrDefault(p => p.LDPlayerDevicesId == device.ID);
                    if (postItem == null)
                    {
                        Logger.LogInfo($"❌ Không có bài viết cho thiết bị {deviceName}, bỏ qua.");
                        continue;
                    }

                    var fanpage = listGameFanpage?.FirstOrDefault(f => f.Id == postItem.GameFanpageId);
                    listFriendRequestProfile.TryGetValue(device.ID.ToString(), out var friendRequests);

                    taskList.Add(Task.Run(async () =>
                    {
                        var deviceSemaphore = new SemaphoreSlim(1, 1);
                        await deviceSemaphore.WaitAsync();
                        try
                        {
                            await ProcessDeviceAsync(
                                deviceName,
                                device,
                                postItem,
                                fanpage?.MyGroup,
                                friendRequests ?? new List<FriendRequestProfile>(),
                                CancellationToken.None
                            );
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"[MainAutoRunFunction] Lỗi xử lý {deviceName}: {ex.Message}");
                            Auto_LDPlayer.LDPlayer.Close(LDType.Name, deviceName);
                        }
                        finally
                        {
                            deviceSemaphore.Release();
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



        private static async Task CompleteTaskAsync(string deviceName, int deviceID, string postID)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    string urlDeviceComplete = $"{API_BASE_URL}{CompleteTaskInDevicePath}";
                    var payload = new
                    {
                        device_name = deviceName.ToString()
                    };
                    var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var responseDeviceComplete = await client.PutAsync(urlDeviceComplete, jsonContent);
                    responseDeviceComplete.EnsureSuccessStatusCode();


                    string urlDailyPostsContentComplete = $"{API_BASE_URL}{string.Format(DailyPostsContentComplete, postID, deviceID)}";
                    var responseDailyPostsContentComplete = await client.PutAsync(urlDailyPostsContentComplete, content: null);
                    responseDailyPostsContentComplete.EnsureSuccessStatusCode();

                    Logger.LogInfo($"Task completed successfully for device '{deviceName}' via PUT!");

                    await DelayHelper.DelayAsync(2, 3);
                    Auto_LDPlayer.LDPlayer.Close(LDType.Name, deviceName);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error completing task for {deviceName}: {ex.Message}");
                }
            }
        }

        private static async Task CompleteFriendRequestAsync(string deviceName, string uuid)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    string urlDeviceComplete = $"{API_BASE_URL}{facebookFriendRequestSuccessPath}/{uuid}";

                    // Create an empty content for the PUT request
                    var emptyContent = new StringContent(string.Empty, Encoding.UTF8, "application/json");

                    var responseDeviceComplete = await client.PutAsync(urlDeviceComplete, emptyContent);
                    responseDeviceComplete.EnsureSuccessStatusCode();

                    Logger.LogInfo($"Task add friend completed successfully for device '{deviceName}' via PUT!");

                    await DelayHelper.DelayAsync(2, 3);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error completing task for {deviceName}: {ex.Message}");
                }
            }
        }

        private static async Task DeleteFriendRequestAsync(string deviceName, string uuid)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    string urlDeviceComplete = $"{API_BASE_URL}{facebookFriendRequestDeletePath}/{uuid}";

                    var responseDeviceComplete = await client.DeleteAsync(urlDeviceComplete);
                    responseDeviceComplete.EnsureSuccessStatusCode();

                    Logger.LogInfo($"Task delete friend request completed successfully for device '{deviceName}' via DELETE!");

                    await DelayHelper.DelayAsync(2, 3);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error deleting friend request for {deviceName}: {ex.Message}");
                }
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
            Bitmap emojiBitmap = null;

            try
            {
                if (chance < 30)
                {
                    Logger.LogInfo("Option 1 selected: Liking the post.");
                    using (var likeBmp = (Bitmap)LIKE_POST_FACEBOOK_BUTTON_BMP.Clone())
                    {
                        Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, likeBmp);
                    }
                }
                else if (chance < 95)
                {
                    Logger.LogInfo("Option 2 selected: Choosing emoji.");
                    using (var subBmp = (Bitmap)LIKE_POST_FACEBOOK_BUTTON_BMP.Clone())
                    {
                        var screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(LDType.Name, deviceName);
                        var pt = ImageScanOpenCV.FindOutPoint(screenshot, subBmp);

                        if (pt.HasValue)
                        {
                            Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, pt.Value.X, pt.Value.Y, 1000);
                            await DelayHelper.DelayAsync(2, 3);

                            // Ensure the emoji image is locked during access
                            lock (emojiLock)
                            {
                                // Randomly select an emoji and clone it safely
                                int emojiChoice = _random.Next(0, emojiButtonsRaw.Count);
                                emojiBitmap = (Bitmap)emojiButtonsRaw[emojiChoice].Clone();
                            }

                            // Now tap the emoji after it has been safely cloned
                            Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, emojiBitmap);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error selecting reaction option: {ex}");
            }
            finally
            {
                // Dispose of the emoji bitmap if it was used
                emojiBitmap?.Dispose();
            }
        }


        public async Task RequestFriendInNewfeedTab(string deviceName)
        {
            for (int i = 0; i < 2; i++)
            {
                Logger.LogInfo($"Click add friend in new feed - Attempt {i + 1}");
                using (var friendNewfeedBmp = (Bitmap)FACEBOOK_ADD_FRIEND_BUTTON_IN_NEWFEED_TAB.Clone())
                {
                    Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, friendNewfeedBmp);
                }
                await DelayHelper.DelayAsync(2, 3);
            }
        }

        public async Task RequestFriendInFriendsTab(string deviceName)
        {
            for (int i = 0; i < 5; i++)
            {
                Logger.LogInfo("Click add friend in new feed");
                using (var friendBmp = (Bitmap)FACEBOOK_ADD_FRIEND_BUTTON_IN_FRIENDS_TAB.Clone())
                {
                    Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, friendBmp);
                }
                await DelayHelper.DelayAsync(2, 3);
            }

            // Chụp màn hình và OCR
            Bitmap screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(LDType.Name, deviceName);
            var position = DeviceHelper.GetTextCoordinatesFromImage(screenshot, "thêm bạn bè");

            if (position.HasValue)
            {
                var (x, y) = position.Value;
                Auto_LDPlayer.LDPlayer.Tap(LDType.Name, deviceName, x, y);
            }
            else
            {
                Logger.LogInfo("Không tìm thấy chuỗi trong ảnh.");
            }
        }


        public async Task SharePostAction(string deviceName)
        {
            if (Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, FACEBOOK_SHARE_POST_FEED))
            {
                await DelayHelper.DelayAsync(4, 10);

                Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, SELECTION_POST_INTO_FACEBOOK);

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
                await Task.Delay(5, cancellationToken); // Wait a moment for the app to load
            }
        }


        private async Task AcceptFriendRequestWithRetry(string deviceName, CancellationToken cancellationToken)
        {
            const int maxRetry = 10;
            for (int retry = 0; retry < maxRetry; retry++)
            {
                // Clone per use to avoid cross-thread conflicts
                using (var subAcceptFriendBmp = (Bitmap)FACEBOOK_ACCEPT_FRIENDS_BUTTON.Clone())
                {
                    var screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(LDType.Name, deviceName);
                    var pt = ImageScanOpenCV.FindOutPoint(screenshot, subAcceptFriendBmp);
                    if (!pt.HasValue)
                        continue;

                    Auto_LDPlayer.LDPlayer.Tap(LDType.Name, deviceName, pt.Value.X, pt.Value.Y);
                }

                await DelayHelper.DelayAsync(1, 2);

                using (var subAddNewBmp = (Bitmap)FACEBOOK_ADD_FRIEND_BUTTON_IN_FRIENDS_TAB.Clone())
                {
                    var screenshotAdd = Auto_LDPlayer.LDPlayer.ScreenShoot(LDType.Name, deviceName);
                    var pt = ImageScanOpenCV.FindOutPoint(screenshotAdd, subAddNewBmp);
                    if (!pt.HasValue)
                        continue;

                    Auto_LDPlayer.LDPlayer.Tap(LDType.Name, deviceName, pt.Value.X, pt.Value.Y);
                }
                await DelayHelper.DelayAsync(1, 2);
                if (cancellationToken.IsCancellationRequested)
                    return;
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
                int swipeCount = rand.Next(15, 20);
                int sharePostCount = 0; // Initialize a counter for sharing posts
                for (int i = 0; i < swipeCount; i++)
                {
                    double startY = rand.NextDouble() * 30 + 50;
                    double endY = rand.NextDouble() * 30 + 10;
                    const double x = 50;

                    await SwipeByPercentSafe(
                        LDType.Name,
                        deviceName,
                        x, startY,
                        x, endY,
                        cancellationToken
                    );

                    if (isReactionSelected)
                    {
                        await SelectReactionOption(deviceName);
                    }
                    await RequestFriendInNewfeedTab(deviceName);
                    await DelayHelper.DelayAsync(4, 10);
                    if (cancellationToken.IsCancellationRequested) return;

                    if (isSharePostProcess && sharePostCount < 3)
                    {
                        int chance = _random.Next(0, 100);  // 0-99
                        if (chance < 10)
                        {
                            await SharePostAction(deviceName);
                            sharePostCount++;
                        }
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
                await DelayHelper.DelayAsync(3, 5);
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

                if (Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, SELECT_TIENG_VIET_BUTTON))
                {
                    await DelayHelper.DelayAsync(4, 7);
                    if (cancellationToken.IsCancellationRequested) return;
                    Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, ACCEPT_NGON_NGU_BUTTON);
                    await DelayHelper.DelayAsync(3, 5);
                }

                swipeCount = rand.Next(12, 20);
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

                    if (isReactionSelected)
                    {
                        await SelectReactionOption(deviceName);
                    }

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
                Bitmap screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(LDType.Name, deviceName);
                string extractedText = DeviceHelper.ExtractTextFromImage(screenshot);

                // Log the extracted text for debugging purposes
                Logger.LogInfo($"[OCR] Extracted text from {deviceName}: {extractedText}");

                // Kiểm tra xem có chữ "login" hay không
                if (!string.IsNullOrEmpty(extractedText) && extractedText.ToLower().Contains("mobile number or email"))
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

            int swipeCount = rand.Next(30, 50);

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


        private async Task ProcessDeviceAsync(string deviceName, LdPlayerDevices deviceFounded, DailyPostItem postSelectedByDevice, string nameOfGroup, List<FriendRequestProfile> listFriendRequest, CancellationToken cancellationToken)
        {
            int currentStep = 0;
            string postID = postSelectedByDevice?.Id;
            int deviceID = deviceFounded.ID;
            var proxySelected = deviceFounded.Proxy;

            try
            {
                // Step 1: Setup the device
                Logger.LogInfo($"Step Init {currentStep++}: Setting up device for post...");
                FacebookLiteHelper.SetupFacebookLiteEnvironment(deviceName);

                await DelayHelper.DelayAsync(2, 3);
                Auto_LDPlayer.LDPlayer.RunApp(LDType.Name, deviceName, "com.android.proxyhandler");

                Logger.LogInfo($"Proxy {proxySelected.Ip}:{proxySelected.Port}");

                await DelayHelper.DelayAsync(200, 250);
                if (cancellationToken.IsCancellationRequested) return;

                // Step 2: Run app Facebook
                Logger.LogInfo($"Step Init {currentStep++}: Open Facebook app");
                Auto_LDPlayer.LDPlayer.RunApp(LDType.Name, deviceName, "com.facebook.lite");

                await DelayHelper.DelayAsync(10, 15);
                if (cancellationToken.IsCancellationRequested) return;


                // Kiểm tra nếu user chưa login thì tiến hành đăng 
                await CheckAndLoginIfNeeded(deviceName);

                if (isAccountMantainanceProcess)
                {
                    await HumanityActionSimulationScript1(deviceName, cancellationToken);
                }

                if (isPostGroupProcess)
                {
                    // Step 3: Tap search button
                    Logger.LogInfo($"Step {currentStep++}: Tapping location 79.5, 6.7...");
                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 79.5, 6.7);

                    await DelayHelper.DelayAsync(4, 8);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 4: Tap for text input
                    Logger.LogInfo($"Step {currentStep++}: Tapping location for text input 26.5, 7.9...");
                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 26.5, 7.9);

                    await DelayHelper.DelayAsync(3, 5);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 5: Input search text for group
                    Logger.LogInfo($"Step {currentStep++}: Inputting search text for group...");
                    Auto_LDPlayer.LDPlayer.InputText(LDType.Name, deviceName, nameOfGroup);

                    await DelayHelper.DelayAsync(3, 5);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 6: Press Enter key to submit
                    Logger.LogInfo($"Step {currentStep++}: Pressing Enter key...");
                    Auto_LDPlayer.LDPlayer.PressKey(LDType.Name, deviceName, LDKeyEvent.KEYCODE_ENTER);

                    await DelayHelper.DelayAsync(5, 9);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 7: Thử tối đa 3 lần tìm và tap TRUY_CAP_BMP
                    Logger.LogInfo($"Step {currentStep++}: Tapping on access image for the post with retry...");
                    const int maxAttempts = 3;
                    bool tapped = false;

                    for (int attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        Logger.LogInfo($"Attempt {attempt}/{maxAttempts} for TRUY_CAP_BMP");
                        if (DeviceHelper.TapImgHalf(LDType.Name, deviceName, TRUY_CAP_BMP))
                        {
                            tapped = true;
                            break;
                        }

                        // Nếu chưa tìm thấy, thử bấm THAM_GIA_BMP
                        DeviceHelper.TapImgHalf(LDType.Name, deviceName, THAM_GIA_BMP);

                        // Đợi ngắn rồi thử lại
                        await DelayHelper.DelayAsync(2, 4);
                        if (cancellationToken.IsCancellationRequested) return;
                    }

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

                Random randomCommentPostProcess = new Random();
                int chanceCommentPostProcess = randomCommentPostProcess.Next(0, 100); // Generates a number between 0 and 99

                if (isCommentPostProcess && (chanceCommentPostProcess < 40))
                {
                    // Step 3: Tap search button
                    Logger.LogInfo($"Step {currentStep++}: Tapping location 79.5, 6.7...");
                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 79.5, 6.7);

                    await DelayHelper.DelayAsync(4, 8);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 4: Tap for text input
                    Logger.LogInfo($"Step {currentStep++}: Tapping location for text input 26.5, 7.9...");
                    DeviceHelper.TapByPercent(LDType.Name, deviceName, 26.5, 7.9);

                    await DelayHelper.DelayAsync(3, 5);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 5: Input search text for group
                    Logger.LogInfo($"Step {currentStep++}: Inputting search text for group...");
                    await DeviceHelper.SendBroadcastMessage(LDType.Name, deviceName, nameOfGroup);

                    await DelayHelper.DelayAsync(3, 5);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 6: Press Enter key to submit
                    Logger.LogInfo($"Step {currentStep++}: Pressing Enter key...");
                    Auto_LDPlayer.LDPlayer.PressKey(LDType.Name, deviceName, LDKeyEvent.KEYCODE_ENTER);

                    await DelayHelper.DelayAsync(5, 9);
                    if (cancellationToken.IsCancellationRequested) return;

                    // Step 7: Thử tối đa 3 lần tìm và tap TRUY_CAP_BMP
                    Logger.LogInfo($"Step {currentStep++}: Tapping on access image for the post with retry...");
                    const int maxAttempts = 3;
                    bool tapped = false;

                    for (int attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        Logger.LogInfo($"Attempt {attempt}/{maxAttempts} for TRUY_CAP_BMP");
                        if (DeviceHelper.TapImgHalf(LDType.Name, deviceName, TRUY_CAP_BMP))
                        {
                            tapped = true;
                            break;
                        }

                        // Nếu chưa tìm thấy, thử bấm THAM_GIA_BMP
                        DeviceHelper.TapImgHalf(LDType.Name, deviceName, THAM_GIA_BMP);

                        // Đợi ngắn rồi thử lại
                        await DelayHelper.DelayAsync(2, 4);
                        if (cancellationToken.IsCancellationRequested) return;
                    }

                    if (tapped)
                    {
                        var rand = new Random();
                        int swipeCount = rand.Next(3, 9);
                        for (int i = 0; i < swipeCount; i++)
                        {
                            double startY = rand.NextDouble() * 30 + 50;
                            double endY = rand.NextDouble() * 30 + 10;
                            const double x = 50;

                            await SwipeByPercentSafe(
                                LDType.Name,
                                deviceName,
                                x, startY,
                                x, endY,
                                cancellationToken
                            );

                            Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, FACEBOOK_POST_COMMENT_ICON);
                            await DelayHelper.DelayAsync(4, 10);
                            if (cancellationToken.IsCancellationRequested) return;

                        }
                    }
                    else
                    {
                        Logger.LogError($"Device {deviceName}: TRUY_CAP_BMP not found after {maxAttempts} attempts");
                    }

                    // Go back into facebook home page
                    for (int i = 0; i < 5; i++)
                    {
                        DeviceHelper.TapByPercent(LDType.Name, deviceName, 6.4, 7.2);

                        int delayTime = new Random().Next(2, 4) * 1000; // Random delay between 4 and 10 seconds
                        await DelayHelper.DelayAsync(delayTime / 1000, delayTime / 1000);
                        if (cancellationToken.IsCancellationRequested) return;
                    }
                }

                // Step 5: Handle friend requests
                if (isFriendRequestProcess)
                {
                    await HandleFriendRequestProcessAsync(deviceName, listFriendRequest, cancellationToken);
                }

                // Final: Complete the task after submitting the post
                await CompleteTaskAsync(deviceName, deviceID, postID);

                await DelayHelper.DelayAsync(7, 15);
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
                    await DeleteFriendRequestAsync(deviceName, profileFriendRequestLink?.Uuid);
                }
                else if (DeviceHelper.TapImgHalf(LDType.Name, deviceName, FACEBOOK_ADD_FRIENDS_REQUEST_BUTTON))
                {
                    // If neither of the above, complete the friend request
                    await CompleteFriendRequestAsync(deviceName, profileFriendRequestLink?.Uuid);
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
                if (!Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, INPUT_WRITE_POST_BMP_1))
                {
                    Logger.LogInfo($"Device {deviceName}: INPUT_WRITE_POST_BMP_1 not found, trying INPUT_WRITE_POST_BMP_2...");
                    if (!Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, INPUT_WRITE_POST_BMP_2))
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
                    if (!Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, FACEBOOK_POST_IMPORT_IMAGE_1_BMP))
                    {
                        Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, FACEBOOK_POST_IMPORT_IMAGE_2_BMP);
                    }

                    await DelayHelper.DelayAsync(3, 5);
                    if (cancellationToken.IsCancellationRequested) return;

                    if (Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, FACEBOOK_ACCEPT_ACCESS_CAMERA_BMP))
                    {
                        await DelayHelper.DelayAsync(3, 5);
                        Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, FACEBOOK_ALLOW_ACCESS_BMP);
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


                    if (Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, FACEBOOK_POST_IMAGE_SELECTION_1_BMP))
                    {
                        await DelayHelper.DelayAsync(1, 2);
                        Auto_LDPlayer.LDPlayer.TapByPercent(LDType.Name, deviceName, 49.6, 95.2);
                    }
                    else
                    {
                        Auto_LDPlayer.LDPlayer.LongPress(LDType.Name, deviceName, (int)264.3, (int)276.0, 200);

                        if (Auto_LDPlayer.LDPlayer.TapImg(LDType.Name, deviceName, FACEBOOK_POST_IMAGE_SELECTION_1_BMP))
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