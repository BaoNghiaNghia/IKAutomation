using ADB_Tool_Automation_Post_FB.Helpers;
using ADB_Tool_Automation_Post_FB.Models;
using Auto_LDPlayer;
using Auto_LDPlayer.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    public static class UIEventHelper
    {
        public static string PcRunner { get; private set; } = "pc_1";

        public static bool IsStop { get; set; }
        public static bool IsReactionSelected { get; set; }
        public static bool IsPostGroupProcess { get; set; }

        public static void PcRunnerComboBoxChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedValue = selectedItem.Content?.ToString() ?? string.Empty;

                switch (selectedValue)
                {
                    case "Máy ngoài 1": PcRunner = "pc_1"; break;
                    case "Máy ngoài 2": PcRunner = "pc_2"; break;
                    case "Máy trong 1": PcRunner = "pc_3"; break;
                }
            }
        }

        public static void TaskTypeComboBoxChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox combo)) return;

            switch (combo.SelectedIndex)
            {
                case 0:
                    IsReactionSelected = true;
                    IsPostGroupProcess = false;
                    break;

                case 1:
                    IsReactionSelected = true;
                    IsPostGroupProcess = true;
                    break;
            }
        }

        public static void ShowLogFile()
        {
            string logFilePath = Logger.LogFilePath;

            if (File.Exists(logFilePath))
                Process.Start("notepad.exe", logFilePath);
            else
                MessageBox.Show("Log file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public static void StartAutomation(
            Button startBtn,
            Button stopBtn,
            Label deviceStatusLabel,
            Func<Task> mainAutoRunFunction)
        {
            startBtn.IsEnabled = false;
            stopBtn.IsEnabled = true;

            IsStop = false;

            deviceStatusLabel.Dispatcher.Invoke(() =>
            {
                deviceStatusLabel.Content = "Đang khởi tạo thiết bị mới";
            });

            Task.Run(mainAutoRunFunction);
        }

        public static async Task StopAutomationAsync(
            Button startBtn,
            Button stopBtn,
            Label deviceStatusLabel,
            Func<List<string>> getRunningDevices)
        {
            stopBtn.IsEnabled = false;
            startBtn.IsEnabled = true;

            IsStop = true;

            Logger.LogInfo("⚠️ Đang dừng tiến trình và đóng các LDPlayer...");

            var devices = getRunningDevices();
            if (devices.Count == 0)
            {
                LDPlayer.CloseAll();
                Logger.LogInfo("✅ Không có thiết bị trong danh sách. Đã đóng toàn bộ bằng CloseAll().");
            }
            else
            {
                var closeTasks = devices.Select(name => Task.Run(() =>
                {
                    try
                    {
                        Logger.LogInfo("⛔ Đóng LDPlayer: " + name);
                        LDPlayer.Close(LDType.Name, name);
                        Logger.LogInfo("✅ Đã đóng xong: " + name);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("❌ Lỗi khi đóng " + name + ": " + ex.Message);
                    }
                }));

                await Task.WhenAll(closeTasks);
                Logger.LogInfo("✅ Đã đóng xong toàn bộ LDPlayer.");
            }

            await deviceStatusLabel.Dispatcher.InvokeAsync(() =>
            {
                deviceStatusLabel.Content = "Đã kết thúc tiến trình";
            });
        }
    }
}
