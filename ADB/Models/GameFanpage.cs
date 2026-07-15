using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Models
{
    public class GameFanpage
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("fanpage")]
        public string Fanpage { get; set; }

        [JsonPropertyName("my_group")]
        public string MyGroup { get; set; }

        [JsonPropertyName("name_of_game")]
        public string NameOfGame { get; set; }

        [JsonPropertyName("group_search_name")]
        public string GroupSearchName { get; set; }

        [JsonPropertyName("hashtag")]
        public string Hashtag { get; set; }

        [JsonPropertyName("public_date")]
        public string PublicDate { get; set; }

        [JsonPropertyName("screenshot_path")]
        public string ScreenshotPath { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("task_type")]
        public string TaskType { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        [JsonPropertyName("note")]
        public string Note { get; set; }

        [JsonPropertyName("comment")]
        public string Comment { get; set; }

        [JsonPropertyName("created_date")]
        public string CreatedDate { get; set; }

        [JsonPropertyName("updated_date")]
        public string UpdatedDate { get; set; }

        [JsonPropertyName("action_type")]
        public string ActionType { get; set; }
    }

    public class GameFanpageMeta
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("per_page")]
        public int PerPage { get; set; }

        [JsonPropertyName("current_page")]
        public int CurrentPage { get; set; }

        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; }
    }

    public class GameFanpageData
    {
        [JsonPropertyName("meta")]
        public GameFanpageMeta Meta { get; set; }

        [JsonPropertyName("items")]
        public List<GameFanpage> Items { get; set; }
    }

    public class GameFanpageResponse
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("data")]
        public GameFanpageData Data { get; set; }
    }
}
