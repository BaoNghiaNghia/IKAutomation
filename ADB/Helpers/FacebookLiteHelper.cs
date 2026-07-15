using Auto_LDPlayer;
using Auto_LDPlayer.Enums;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    public static class FacebookLiteHelper
    {
        public static void SetupFacebookLiteEnvironment(string deviceName)
        {
            DeviceHelper.InstallAdbKeyboard(LDType.Name, deviceName);
            // Set ADB keyboard
            DeviceHelper.SetAdbKeyboard(LDType.Name, deviceName);


            // Grant permissions
            GrantStoragePermissions(deviceName);

            // Open settings (manual grant fallback)
            OpenAppSettings(deviceName);
        }

        private static void GrantStoragePermissions(string deviceName)
        {
            string writePermission = "shell pm grant com.facebook.lite android.permission.WRITE_EXTERNAL_STORAGE";
            string readPermission = "shell pm grant com.facebook.lite android.permission.READ_EXTERNAL_STORAGE";

            LDPlayer.Adb(LDType.Name, deviceName, writePermission);
            LDPlayer.Adb(LDType.Name, deviceName, readPermission);
        }

        private static void OpenAppSettings(string deviceName)
        {
            string command = "am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d package:com.facebook.lite";
            LDPlayer.Adb(LDType.Name, deviceName, command);
        }
    }
}
