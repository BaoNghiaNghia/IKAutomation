using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Models
{
    // Define the classes to match the API response structure
    public class ApiResponseFriendList
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("data")]
        public Dictionary<string, List<FriendRequestProfile>> Data { get; set; }
    }

    public class FriendRequestProfile
    {
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        [JsonPropertyName("profile_id")]
        public string ProfileId { get; set; }

        [JsonPropertyName("game_fanpages_id")]
        public int GameFanpagesId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("used_count")]
        public int UsedCount { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; }

        [JsonPropertyName("profile_link")]
        public string ProfileLink { get; set; }
    }
}
