using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace IK_Auto_ADB.Infrastructure.Diagnostics
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
