using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Models
{
    // Classes for Daily Posts Content API response
    public class DailyPostItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("fanpage")]
        public string Fanpage { get; set; }

        [JsonPropertyName("ldplayer_devices_id")]
        public int LDPlayerDevicesId { get; set; }

        [JsonPropertyName("game_fanpages_id")]
        public int GameFanpageId { get; set; }

        [JsonPropertyName("post_id")]
        public string PostId { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }

        [JsonPropertyName("clone_version")]
        public string CloneVersion { get; set; }

        [JsonPropertyName("img_path")]
        public string ImgPath { get; set; }

        [JsonPropertyName("image_blob")]
        public string ImageBlob { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; }

        [JsonPropertyName("post_comments")]
        public List<string> PostComments { get; set; }

        [JsonPropertyName("post_image")]
        public List<string> PostImage { get; set; }
    }

    public class DailyPostsContentResponse
    {
        [JsonPropertyName("messages")]
        public string Messages { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("items")]
        public List<DailyPostItem> Items { get; set; }
    }
}
