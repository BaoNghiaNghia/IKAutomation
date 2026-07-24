using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Models
{
    // Temporary local-only models retained until the remaining Facebook
    // automation methods are removed. They no longer represent an API contract.
    public sealed class LdPlayerDevices
    {
        public int ID { get; set; }
        public ProxyDetails Proxy { get; set; }
    }

    public sealed class ProxyDetails
    {
        public string ProxyOriginIp { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public sealed class DailyPostItem
    {
        public string Content { get; set; }
        public List<string> PostImage { get; set; }
    }

    public sealed class FriendRequestProfile
    {
        public string ProfileLink { get; set; }
    }

    public sealed class GameFanpage
    {
        public string MyGroup { get; set; }
        public string Comment { get; set; }
    }
}
