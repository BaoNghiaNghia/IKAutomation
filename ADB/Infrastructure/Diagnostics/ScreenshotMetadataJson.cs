using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics
{
    public static class ScreenshotMetadataJson
    {
        public static byte[] Serialize(ScreenshotMetadata metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            var serializer = new DataContractJsonSerializer(typeof(ScreenshotMetadata));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, metadata);
                return stream.ToArray();
            }
        }
    }
}
