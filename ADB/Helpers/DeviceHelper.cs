using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using Tesseract;

using Auto_LDPlayer;
using Auto_LDPlayer.Enums;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    public static class DeviceHelper
    {
        public static string GetAppVersion(LDType ldType, string deviceName, string packageName = "com.facebook.lite")
        {
            // Use dumpsys package and filter for versionName/versionCode
            string cmd = $"shell dumpsys package {packageName} | grep version";
            string output = Auto_LDPlayer.LDPlayer.Adb(ldType, deviceName, cmd);


            var lines = output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("versionName="))
                    return trimmed.Substring("versionName=".Length);
            }
            return null;
        }

        // Hàm Clamp để giới hạn giá trị trong khoảng 0 - 1
        public static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        /// <summary>
        /// Safely updates a WPF Label on the UI thread.
        /// </summary>
        public static void SafeUpdateUI(Label label, string content)
        {
            if (label.Dispatcher.CheckAccess())
            {
                label.Content = content;
            }
            else
            {
                label.Dispatcher.InvokeAsync(() =>
                {
                    label.Content = content;
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }


        /// <summary>
        /// Performs a countdown and updates the label every second.
        /// </summary>
        public static async Task WaitWithCountdown(Label label, int seconds)
        {
            for (int i = seconds; i >= 0; i--)
            {
                int min = i / 60;
                int sec = i % 60;

                string text = (min == 0 && sec == 0)
                    ? "Đang chờ lượt tiếp theo."
                    : $"Đang chờ lượt tiếp theo. \nThời gian còn: {min} phút {sec} giây...";

                SafeUpdateUI(label, text);
                await Task.Delay(1000);
            }
        }

        /// <summary>
        /// Safely clones a Bitmap with locking.
        /// </summary>
        public static Bitmap CloneWithLock(Bitmap bmp, object lockObj)
        {
            lock (lockObj)
            {
                return (Bitmap)bmp.Clone();
            }
        }


        public static Point GetScreenResolution(LDType ldType, string nameOrId, Exception resolutionException, Exception formatException)
        {
            try
            {
                string text = Auto_LDPlayer.LDPlayer.Adb(ldType, nameOrId, "shell dumpsys display | grep \"mCurrentDisplayRect\"");

                if (string.IsNullOrEmpty(text) || !text.Contains("mCurrentDisplayRect"))
                {
                    throw resolutionException;
                }

                int dashIndex = text.IndexOf("- ");
                int endIndex = text.IndexOf(")", dashIndex);

                if (dashIndex < 0 || endIndex < 0 || endIndex <= dashIndex)
                {
                    throw formatException;
                }

                string rectPart = text.Substring(dashIndex + 2, endIndex - dashIndex - 2); // skip "- "
                string[] array = rectPart.Split(',');

                if (array.Length != 2)
                {
                    throw formatException;
                }

                int width = Convert.ToInt32(array[0].Trim());
                int height = Convert.ToInt32(array[1].Trim());

                return new Point(width, height);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetScreenResolution: {ex.Message}");
                return new Point(540, 960); // fallback
            }
        }

        public static void TapByPercent(LDType ldType, string nameOrId, double x, double y, int count = 1)
        {
            Point screenResolution = GetScreenResolution(ldType, nameOrId, new Exception("Failed to retrieve screen resolution from the device."), new Exception("Invalid resolution data format."));
            int x2 = (int)(x * ((double)screenResolution.X * 1.0 / 100.0));
            int y2 = (int)(y * ((double)screenResolution.Y * 1.0 / 100.0));
            Auto_LDPlayer.LDPlayer.Tap(ldType, nameOrId, x2, y2, count);
        }

        public static void SwipeUpdated(LDType ldType, string nameOrId, int x1, int y1, int x2, int y2, int duration = 100)
        {
            // Sử dụng lệnh touchscreen swipe thay vì swipe thông thường
            string cmd = $"shell input touchscreen swipe {x1} {y1} {x2} {y2} {duration}";
            Auto_LDPlayer.LDPlayer.Adb(ldType, nameOrId, cmd, 200);
        }


        public static void SwipeByPercent(LDType ldType, string nameOrId, double x1, double y1, double x2, double y2, int duration = 100)
        {
            Point screenResolution = GetScreenResolution(ldType, nameOrId, new Exception("Failed to retrieve screen resolution from the device."), new Exception("Invalid resolution data format."));
            int x3 = (int)(x1 * ((double)screenResolution.X * 1.0 / 100.0));
            int y3 = (int)(y1 * ((double)screenResolution.Y * 1.0 / 100.0));
            int x4 = (int)(x2 * ((double)screenResolution.X * 1.0 / 100.0));
            int y4 = (int)(y2 * ((double)screenResolution.Y * 1.0 / 100.0));
            SwipeUpdated(ldType, nameOrId, x3, y3, x4, y4, duration);
        }

        public static bool TapImgHalf(LDType ldType, string nameOrId, Bitmap imgFind)
        {
            if (imgFind == null)
            {
                Logger.LogError($"❌ imgFind is null. Device: {nameOrId}");
                return false;
            }

            Logger.LogInfo($"🔍 Using imgFind: Size={imgFind.Width}x{imgFind.Height}, Format={imgFind.PixelFormat}");

            Bitmap screenshot = null;

            try
            {
                screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(ldType, nameOrId);

                if (screenshot == null)
                {
                    Logger.LogError($"❌ Failed to capture screenshot from device: {nameOrId} — ScreenShoot returned null.");
                    return false;
                }

                int topHeight = (int)(screenshot.Height * 0.6);
                Rectangle topRect = new Rectangle(0, 0, screenshot.Width, topHeight);

                using (Bitmap topHalf = screenshot.Clone(topRect, screenshot.PixelFormat))
                using (Bitmap searchBitmap = imgFind.Clone(new Rectangle(0, 0, imgFind.Width, imgFind.Height), imgFind.PixelFormat))
                {
                    var point = KAutoHelper.ImageScanOpenCV.FindOutPoint(topHalf, searchBitmap);
                    if (point.HasValue)
                    {
                        Auto_LDPlayer.LDPlayer.Tap(ldType, nameOrId, point.Value.X, point.Value.Y);
                        Logger.LogInfo($"✅ Tapped at X={point.Value.X}, Y={point.Value.Y}");
                        return true;
                    }
                    else
                    {
                        Logger.LogWarning($"🔍 Image not found in top half of the screenshot from device {nameOrId}.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"❌ Exception in TapImgHalf({nameOrId}): {ex.Message}");
            }
            finally
            {
                screenshot?.Dispose();
            }

            return false;
        }



        /// <summary>
        /// Get the current package name running on the device using ADB command and log it.
        /// </summary>
        public static string GetCurrentPackage(LDType ldType, string deviceName)
        {
            try
            {
                string command = "shell dumpsys window | grep mCurrentFocus";
                string result = Auto_LDPlayer.LDPlayer.Adb(ldType, deviceName, command);

                if (!string.IsNullOrEmpty(result))
                {
                    string[] parts = result.Split(' ');
                    string packageName = parts.Length > 1 ? parts[parts.Length - 1] : string.Empty;

                    if (!string.IsNullOrEmpty(packageName) && packageName.StartsWith("com."))
                    {
                        return packageName;  // e.g., "com.facebook.lite"
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting current package for device {deviceName}: {ex.Message}");
            }

            return string.Empty;
        }


        public static bool CheckImgExistsInTopHalf(LDType ldType, string nameOrId, byte[] imgBytes)
        {
            if (imgBytes == null || imgBytes.Length == 0)
            {
                Logger.LogError($"[ImageCheck] imgBytes is null or empty for device: {nameOrId}");
                return false;
            }

            Bitmap subBitmap;
            try
            {
                using (var ms = new MemoryStream(imgBytes))
                {
                    subBitmap = new Bitmap(ms);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ImageCheck] Failed to convert imgBytes to Bitmap for device: {nameOrId}. Error: {ex.Message}");
                return false;
            }

            Bitmap screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(ldType, nameOrId);
            if (screenshot == null)
            {
                Logger.LogError($"[ImageCheck] Failed to capture screenshot for device: {nameOrId}");
                return false;
            }

            int topHeight = (int)(screenshot.Height * 0.6);
            Rectangle topRect = new Rectangle(0, 0, screenshot.Width, topHeight);

            Bitmap topHalfScreenshot = screenshot.Clone(topRect, screenshot.PixelFormat);
            if (topHalfScreenshot == null)
            {
                Logger.LogError($"[ImageCheck] Failed to clone top 60% screenshot for device: {nameOrId}");
                return false;
            }

            Point? point = KAutoHelper.ImageScanOpenCV.FindOutPoint(topHalfScreenshot, subBitmap);
            return point.HasValue;
        }


        public static void SetAdbKeyboard(LDType ldType, string nameOrId)
        {
            try
            {
                // Command to enable the ADB keyboard
                string enableCommand = "shell ime enable com.android.adbkeyboard/.AdbIME";
                Auto_LDPlayer.LDPlayer.Adb(ldType, nameOrId, enableCommand);

                // Command to set the ADB keyboard as the default input method
                string setCommand = "shell ime set com.android.adbkeyboard/.AdbIME";
                Auto_LDPlayer.LDPlayer.Adb(ldType, nameOrId, setCommand);
            }
            catch (Exception ex)
            {
                // Log error if something goes wrong
                Logger.LogError($"Error setting ADB keyboard for device {nameOrId}: {ex.Message}");
            }
        }

        public static bool IsAdbKeyboardInstalled(LDType ldType, string nameOrId)
        {
            try
            {
                // Command to list installed packages and check for adbkeyboard
                string command = "shell pm list packages | grep com.android.adbkeyboard";
                string result = Auto_LDPlayer.LDPlayer.Adb(ldType, nameOrId, command);

                if (!string.IsNullOrEmpty(result) && result.Contains("com.android.adbkeyboard"))
                {
                    Logger.LogInfo("ADB Keyboard is installed.");
                    return true;
                }
                else
                {
                    Logger.LogInfo("ADB Keyboard is not installed.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Log error if something goes wrong
                Logger.LogInfo($"Error checking if ADB Keyboard is installed: {ex.Message}");
                return false;
            }
        }

        public static void InstallAdbKeyboard(LDType ldType, string nameOrId)
        {
            try
            {
                // Path to the adbkeyboard.apk in the current folder  
                string apkPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "ADBKeyboard.apk");
                // Check if the file exists before trying to install  
                if (File.Exists(apkPath))
                {
                    // Execute the command  
                    Auto_LDPlayer.LDPlayer.InstallAppFile(LDType.Name, nameOrId, apkPath);

                    // Set the ADB Keyboard as the default input method  
                    string enableCommand = "shell ime enable com.android.adbkeyboard/.AdbIME";
                    Auto_LDPlayer.LDPlayer.Adb(ldType, nameOrId, enableCommand);

                    string setCommand = "shell ime set com.android.adbkeyboard/.AdbIME";
                    Auto_LDPlayer.LDPlayer.Adb(ldType, nameOrId, setCommand);

                    Logger.LogInfo("ADB Keyboard set as default input method.");
                }
                else
                {
                    Logger.LogError("ADBKeyboard.apk not found in the current folder.");
                }
            }
            catch (Exception ex)
            {
                // Log error if something goes wrong  
                Logger.LogError($"Error installing or setting ADB Keyboard for device {nameOrId}: {ex.Message}");
            }
        }

        public static void SetDeviceToSilentMode(LDType ldType, string deviceName, bool enableSilent = true)
        {
            try
            {
                string dndCommand = enableSilent ? "shell settings put global zen_mode 1" : "shell settings put global zen_mode 0";

                string result = Auto_LDPlayer.LDPlayer.Adb(ldType, deviceName, dndCommand);
                Logger.LogInfo($"DND Mode for Device: {deviceName} set to {enableSilent}. Result: {result}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error setting device {deviceName} to silent mode or adjusting notifications: {ex.Message}");
            }
        }

        // Function to prepare and fix text before sending
        public static string PrepareTextForBroadcast(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return string.Empty;

            string clean = message.Trim();

            // Chuẩn hóa xuống dòng
            clean = Regex.Replace(clean, @"(\r?\n)+", "\n");

            // Mã hóa URL thông minh
            clean = SmartEncodeUrls(clean);

            // Mã hóa base64
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(clean));
        }

        private static string SmartEncodeUrls(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Regex nhận diện URL
            string pattern = @"https?:\/\/[^\s]+";

            return Regex.Replace(text, pattern, match =>
            {
                try
                {
                    Uri uri = new Uri(match.Value);

                    // Giữ nguyên phần host + scheme
                    string scheme = uri.Scheme;
                    string host = uri.Host;

                    // Encode phần path và query
                    string encodedPath = Uri.EscapeUriString(uri.AbsolutePath);
                    string encodedQuery = Uri.EscapeUriString(uri.Query);

                    return $"{scheme}://{host}{encodedPath}{encodedQuery}";
                }
                catch
                {
                    // Nếu lỗi parse URI, fallback encode toàn bộ
                    return Uri.EscapeUriString(match.Value);
                }
            });
        }


        // Send the broadcast message with retries
        public static async Task SendBroadcastMessage(LDType ldType, string nameOrId, string message, int maxRetries = 3, int retryDelayMs = 1000)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                Logger.LogWarning("❌ Message is empty. Broadcast skipped.");
                return;
            }

            string base64Message = PrepareTextForBroadcast(message);
            string command = $"shell am broadcast -a ADB_INPUT_B64 --es msg {base64Message}";

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    string result = Auto_LDPlayer.LDPlayer.Adb(ldType, nameOrId, command);

                    if (!string.IsNullOrEmpty(result) && result.Contains("Broadcast completed"))
                    {
                        Logger.LogInfo($"✅ Broadcast sent successfully: {message}.");
                        return;
                    }
                    else
                    {
                        Logger.LogWarning($"⚠️ Broadcast failed.Result: {result}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"❌ Exception during broadcast: {ex.Message}");
                }

                await Task.Delay(retryDelayMs);
            }

            Logger.LogError("❌ Failed to send broadcast after maximum retries.");
        }


        public static async Task InputFieldAsyncProxy(string deviceName, double xPercent, double yPercent, string text)
        {
            // 1. Tap để focus vào input field
            TapByPercent(LDType.Name, deviceName, xPercent, yPercent);
            await Task.Delay(100);

            // 2. Xoá ký tự phía trước con trỏ (KEYCODE_DEL = 67)
            for (int i = 0; i < 30; i++)
            {
                Auto_LDPlayer.LDPlayer.PressKey(LDType.Name, deviceName, LDKeyEvent.KEYCODE_DEL);
            }

            // 3. Xoá ký tự phía sau con trỏ (KEYCODE_FORWARD_DEL = 112)
            for (int i = 0; i < 20; i++)
            {
                Auto_LDPlayer.LDPlayer.Adb(LDType.Name, deviceName, "shell input keyevent 112");
            }

            // 4. Gửi nội dung mới
            await SendBroadcastMessage(LDType.Name, deviceName, text);
            await Task.Delay(100);
        }



        public static string ExtractTextFromImage(string deviceName, string lang = "eng+vie")
        {
            Bitmap screenshot = null;

            try
            {
                // Chụp màn hình từ LDPlayer theo deviceName
                screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(LDType.Name, deviceName);
                if (screenshot == null)
                {
                    Logger.LogError($"❌ Failed to capture screenshot from device: {deviceName}");
                    return null;
                }

                // Đường dẫn tới thư mục tessdata
                string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

                // Kiểm tra tất cả các traineddata cần thiết theo lang
                string[] langs = lang.Split('+');
                var missingLangs = langs
                    .Where(l => !File.Exists(Path.Combine(tessdataPath, $"{l}.traineddata")))
                    .ToList();

                if (missingLangs.Any())
                {
                    Logger.LogError($"Missing traineddata files: {string.Join(", ", missingLangs)}");
                    return null;
                }

                // OCR
                using (var engine = new TesseractEngine(tessdataPath, lang, EngineMode.Default))
                using (var pix = PixConverter.ToPix(screenshot))
                {
                    if (pix == null)
                    {
                        Logger.LogError("Failed to convert screenshot to Pix format.");
                        return null;
                    }

                    using (var page = engine.Process(pix))
                    {
                        return page.GetText();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Tesseract OCR] Error while extracting text: {ex.Message}");
                return null;
            }
            finally
            {
                screenshot?.Dispose(); // cleanup
            }
        }



        public static List<(int x, int y)> GetAllTextCoordinatesFromDevice(LDType ldType, string deviceName, string searchText, string lang = "eng+vie")
        {
            var positions = new List<(int x, int y)>();

            if (string.IsNullOrWhiteSpace(searchText))
                return positions;

            Bitmap screenshot = null;

            try
            {
                screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(ldType, deviceName);
                if (screenshot == null)
                {
                    Logger.LogError($"❌ Failed to capture screenshot from device: {deviceName}");
                    return positions;
                }

                string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

                // Kiểm tra traineddata cần thiết
                var langs = lang.Split('+');
                var missingLangs = langs
                    .Where(l => !File.Exists(Path.Combine(tessdataPath, $"{l}.traineddata")))
                    .ToList();

                if (missingLangs.Any())
                {
                    Logger.LogError($"Missing traineddata files: {string.Join(", ", missingLangs)}");
                    return positions;
                }

                using (var engine = new TesseractEngine(tessdataPath, lang, EngineMode.Default))
                using (var pix = PixConverter.ToPix(screenshot))
                using (var page = engine.Process(pix))
                using (var iterator = page.GetIterator())
                {
                    iterator.Begin();
                    string loweredSearch = searchText.ToLowerInvariant();

                    do
                    {
                        string word = iterator.GetText(PageIteratorLevel.Word);

                        if (!string.IsNullOrEmpty(word) &&
                            word.ToLowerInvariant().Contains(loweredSearch) &&
                            iterator.TryGetBoundingBox(PageIteratorLevel.Word, out Rect bounds))
                        {
                            int x = bounds.X1 + bounds.Width / 2;
                            int y = bounds.Y1 + bounds.Height / 2;
                            positions.Add((x, y));
                        }
                    }
                    while (iterator.Next(PageIteratorLevel.Word));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[OCR Error] {ex.Message}");
            }
            finally
            {
                screenshot?.Dispose();
            }

            return positions;
        }



        public static void TapAsync(LDType ldType, string nameOrId, int x, int y, int count = 1)
        {
            if (count <= 0)
            {
                Logger.LogWarning("⚠️ Tap count must be >= 1.");
                return;
            }

            string command = string.Join(" && ", Enumerable.Repeat($"shell input tap {x} {y}", count));
            Auto_LDPlayer.LDPlayer.Adb(ldType, nameOrId, command, 200);

            Logger.LogInfo($"✅ Sent {count} tap(s) to device {nameOrId} at ({x},{y})");
        }

        public static bool TapImgAsync(LDType ldType, string nameOrId, Bitmap imgFind)
        {
            if (imgFind == null)
            {
                Logger.LogError($"❌ imgFind is null. Device: {nameOrId}");
                return false;
            }

            Bitmap screenshot = null;

            try
            {
                screenshot = Auto_LDPlayer.LDPlayer.ScreenShoot(ldType, nameOrId);

                if (screenshot == null)
                {
                    Logger.LogError($"❌ Failed to capture screenshot from device: {nameOrId}");
                    return false;
                }

                using (Bitmap searchBitmap = (Bitmap)imgFind.Clone())
                {
                    Point? point = KAutoHelper.ImageScanOpenCV.FindOutPoint(screenshot, searchBitmap);

                    if (!point.HasValue)
                    {
                        Logger.LogWarning($"🔍 Image not found on screen for device: {nameOrId}");
                        return false;
                    }

                    TapAsync(ldType, nameOrId, point.Value.X, point.Value.Y);
                    Logger.LogInfo($"✅ Tapped at X={point.Value.X}, Y={point.Value.Y} on device: {nameOrId}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"❌ Exception in TapImg({nameOrId}): {ex.Message}");
                return false;
            }
            finally
            {
                screenshot?.Dispose();
            }
        }

        public static bool TapImgBytes(LDType ldType, string nameOrId, byte[] imgBytes)
        {
            if (imgBytes == null || imgBytes.Length == 0)
            {
                Logger.LogError($"❌ TapImgAsync: imgBytes is null or empty. Device: {nameOrId}");
                return false;
            }

            try
            {
                using (var ms = new MemoryStream(imgBytes))
                using (var bitmap = new Bitmap(ms))
                {
                    return TapImgAsync(ldType, nameOrId, bitmap); // Gọi lại hàm đã có
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"❌ Exception in TapImgAsync(byte[]) for device {nameOrId}: {ex.Message}");
                return false;
            }
        }




        public static int CountTotalRunningDevices(LDType ldType, List<string> deviceNamesOrIds)
        {
            if (deviceNamesOrIds == null || deviceNamesOrIds.Count == 0)
                return 0;

            return deviceNamesOrIds.Count(deviceNameOrId => Auto_LDPlayer.LDPlayer.IsDeviceRunning(ldType, deviceNameOrId));
        }

        public static void SortWnd(int countPerRow = 7, int hSpacing = 5, int vSpacing = 5)
        {
            string args = $"sortWnd {countPerRow} {hSpacing} {vSpacing}";
            Auto_LDPlayer.LDPlayer.ExecuteLD(args);
        }


        public static (string Host, string Port, string Username, string Password) ExtractProxyConfigFromDevice(string deviceName, string lang = "eng+vie")
        {
            string extractedText = ExtractTextFromImage(deviceName, lang);
            string host = null, port = null, user = null, pass = null;

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                Logger.LogError($"No OCR text found from device: {deviceName}");
                return (host, port, user, pass);
            }

            foreach (var line in extractedText.Split('\n'))
            {
                var clean = line.Trim();
                if (clean.StartsWith("Proxy Host:", StringComparison.OrdinalIgnoreCase))
                    host = clean.Substring("Proxy Host:".Length).Trim();
                else if (clean.StartsWith("Proxy Port:", StringComparison.OrdinalIgnoreCase))
                    port = clean.Substring("Proxy Port:".Length).Trim();
                else if (clean.StartsWith("Username:", StringComparison.OrdinalIgnoreCase))
                    user = clean.Substring("Username:".Length).Trim();
                else if (clean.StartsWith("Password:", StringComparison.OrdinalIgnoreCase))
                    pass = clean.Substring("Password:".Length).Trim();
            }

            return (host, port, user, pass);
        }
    }
}
