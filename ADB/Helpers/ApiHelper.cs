using ADB_Tool_Automation_Post_FB.Exceptions;
using ADB_Tool_Automation_Post_FB.Models;
using Auto_LDPlayer.Enums;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    public static class ApiHelper
    {
        private static readonly string API_BASE_URL = "https://boostgamemobile.com/service";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static async Task<bool> RotateAllProxiesAsync(HttpClient client)
        {
            try
            {
                string url = API_BASE_URL + "/proxy/rotate/all";
                Logger.LogInfo("🔁 Calling proxy rotate API: " + url);

                var response = await client.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                Logger.LogInfo("Response proxy rotate API: " + content); // Log the response content

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("❌ Rotate Proxy API failed. StatusCode: " + response.StatusCode);
                    return false;
                }

                var json = JsonDocument.Parse(content);
                if (json.RootElement.TryGetProperty("success", out var successProp))
                {
                    return successProp.GetBoolean();
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError("❌ Exception while rotating proxies: " + ex.Message);
                return false;
            }
        }

        public static async Task<ApiResponseLDPlayerDevices> FetchAvailableLDPlayerDevicesAsync(HttpClient client, int limit, string pcRunner)
        {
            try
            {
                string url = API_BASE_URL + "/ldplayer_devices/available?limit=" + limit + "&pc_runner=" + pcRunner;
                Logger.LogInfo("Calling API: " + url);

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return new ApiResponseLDPlayerDevices();
                }

                string json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<ApiResponseLDPlayerDevices>(json, JsonOptions);
                return data ?? new ApiResponseLDPlayerDevices();
            }
            catch (Exception ex)
            {
                Logger.LogError("Error fetching devices: " + ex.Message);
                return new ApiResponseLDPlayerDevices();
            }
        }

        public static async Task<List<DailyPostItem>> FetchDailyPostsContentAsync(HttpClient client, List<int> listDeviceID)
        {
            var result = new List<DailyPostItem>();
            try
            {
                string ids = string.Join(",", listDeviceID);
                string url = API_BASE_URL + "/daily_posts_content/available?list_devices=" + Uri.EscapeDataString(ids);
                Logger.LogInfo("Calling API: " + url);

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return result;
                }

                string content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<DailyPostsContentResponse>(content, JsonOptions);
                return data != null && data.Items != null ? data.Items : result;
            }
            catch (Exception ex)
            {
                Logger.LogError("Error fetching daily posts: " + ex.Message);
                return result;
            }
        }

        public static async Task<List<GameFanpage>> FetchGameFanpagesAsync(HttpClient client)
        {
            var result = new List<GameFanpage>();
            try
            {
                string url = API_BASE_URL + "/game_fanpages?page=1&limit=300&priority=2";
                Logger.LogInfo("Calling API: " + url);

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return result;
                }

                string content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<GameFanpageResponse>(content, JsonOptions);
                return data != null && data.Data != null && data.Data.Items != null ? data.Data.Items : result;
            }
            catch (Exception ex)
            {
                Logger.LogError("Error fetching game fanpages: " + ex.Message);
                return result;
            }
        }

        public static async Task<Dictionary<string, List<FriendRequestProfile>>> FetchFriendListAsync(HttpClient client, List<int> listDeviceID)
        {
            var result = new Dictionary<string, List<FriendRequestProfile>>();
            try
            {
                string ids = string.Join(",", listDeviceID);
                string url = API_BASE_URL + "/friend_list_group_game/available?list_devices=" + Uri.EscapeDataString(ids);
                Logger.LogInfo("Calling API: " + url);

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return result;
                }

                string content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<ApiResponseFriendList>(content, JsonOptions);
                return data != null && data.Data != null ? data.Data : result;
            }
            catch (Exception ex)
            {
                Logger.LogError("Error fetching friend list: " + ex.Message);
                return result;
            }
        }

        public static async Task CompleteTaskAsync(string deviceName, int deviceID, string postID)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    string url1 = API_BASE_URL + "/ldplayer_devices/complete-task/by-name";
                    var payload = new { device_name = deviceName };
                    var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var res1 = await client.PutAsync(url1, jsonContent);
                    res1.EnsureSuccessStatusCode();


                    // Replace the hardcoded path-delimiter with the constant
                    string url2 = string.Format(API_BASE_URL + "/daily_posts_content/update-device/{0}/{1}", postID, deviceID);
                    var res2 = await client.PutAsync(url2, null);
                    res2.EnsureSuccessStatusCode();

                    Logger.LogInfo("✅ Task completed for " + deviceName);

                    await DelayHelper.DelayAsync(2, 3);
                    Auto_LDPlayer.LDPlayer.Close(LDType.Name, deviceName);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error completing task: " + ex.Message);
                }
            }
        }

        public static async Task CompleteFriendRequestAsync(string deviceName, string uuid)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    string url = API_BASE_URL + "/friend_list_group_game/increment-used-count/" + uuid;
                    var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
                    var response = await client.PutAsync(url, content);
                    response.EnsureSuccessStatusCode();

                    Logger.LogInfo("✅ Friend request marked as used for " + deviceName);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error completing friend request: " + ex.Message);
                }
            }
        }

        public static async Task DeleteFriendRequestAsync(string deviceName, string uuid)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    string url = API_BASE_URL + "/friend_list_group_game/delete/" + uuid;
                    var response = await client.DeleteAsync(url);
                    response.EnsureSuccessStatusCode();

                    Logger.LogInfo("✅ Friend request deleted for " + deviceName);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error deleting friend request: " + ex.Message);
                }
            }
        }
    }
}
