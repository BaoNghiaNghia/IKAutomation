using System.Runtime.Serialization;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics
{
    [DataContract]
    public sealed class ScreenshotMetadata
    {
        [DataMember(Name = "deviceName", Order = 1)]
        public string DeviceName { get; set; }

        [DataMember(Name = "capturedAt", Order = 2)]
        public string CapturedAt { get; set; }

        [DataMember(Name = "stateName", Order = 3)]
        public string StateName { get; set; }

        [DataMember(Name = "note", Order = 4)]
        public string Note { get; set; }

        [DataMember(Name = "width", Order = 5)]
        public int Width { get; set; }

        [DataMember(Name = "height", Order = 6)]
        public int Height { get; set; }
    }
}
