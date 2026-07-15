using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ADB_Tool_Automation_Post_FB.Models
{
    public class LdPlayerDevices
    {
        [JsonPropertyName("id")]
        public int ID { get; set; }

        [JsonPropertyName("device_name")]
        public string DeviceName { get; set; }

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; }

        [JsonPropertyName("device_model")]
        public string DeviceModel { get; set; }

        [JsonPropertyName("android_version")]
        public string AndroidVersion { get; set; }

        [JsonPropertyName("serial_number")]
        public string SerialNumber { get; set; }

        [JsonPropertyName("udid")]
        public string Udid { get; set; }

        [JsonPropertyName("imei")]
        public string Imei { get; set; }

        [JsonPropertyName("last_run")]
        public string LastRun { get; set; }

        [JsonPropertyName("pc_runner")]
        public string PcRunner { get; set; }

        [JsonPropertyName("count_today")]
        public int CountToday { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("facebook_friend_requests_count")]
        public int FacebookFriendRequestsCount { get; set; }

        [JsonPropertyName("proxy_id")]
        public string ProxyId { get; set; }

        [JsonPropertyName("proxy")]
        public ProxyDetails Proxy { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; }

        [JsonPropertyName("time_diff_seconds")]
        public int TimeDiffSeconds { get; set; }
    }

    public class ProxyDetails
    {
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        [JsonPropertyName("proxy_origin_ip")]
        public string ProxyOriginIp { get; set; }

        [JsonPropertyName("ip")]
        public string Ip { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; }

        [JsonPropertyName("password")]
        public string Password { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("rotate_mode")]
        public string RotateMode { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; }
    }

    public class MetaDataLDPlayerDevices
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("available")]
        public int Available { get; set; }

        [JsonPropertyName("pc_runner")]
        public string PcRunner { get; set; }
    }

    public class ApiResponseLDPlayerDevices
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("meta")]
        public MetaDataLDPlayerDevices Meta { get; set; }

        [JsonPropertyName("items")]
        public List<LdPlayerDevices> Items { get; set; }

        [JsonPropertyName("statistics")]
        public Dictionary<string, StatisticsLDPlayerDevices> Statistics { get; set; }
    }

    public class StatisticsLDPlayerDevices
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("status_breakdown")]
        public List<StatusBreakdownLDPlayerDevices> StatusBreakdown { get; set; }
    }

    public class StatusBreakdownLDPlayerDevices
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }
}
