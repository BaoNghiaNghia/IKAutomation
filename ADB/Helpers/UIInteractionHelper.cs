using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    public static class UIEventHelper
    {
        public static void ShowLogFile()
        {
            string logFilePath = Logger.LogFilePath;

            if (File.Exists(logFilePath))
            {
                Process.Start("notepad.exe", logFilePath);
                return;
            }

            MessageBox.Show(
                "Log file not found.",
                "IKAutomation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
