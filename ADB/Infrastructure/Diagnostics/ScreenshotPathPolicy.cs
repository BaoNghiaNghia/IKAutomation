using System;
using System.IO;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics
{
    public static class ScreenshotPathPolicy
    {
        public static string SanitizeDeviceName(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("LDPlayer device name is required.", nameof(deviceName));

            return SanitizeSegment(deviceName, "device");
        }

        public static string SanitizeStateName(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
                throw new ArgumentException("Screenshot state name is required.", nameof(stateName));

            string trimmed = stateName.Trim();
            if (trimmed.IndexOf("..", StringComparison.Ordinal) >= 0
                || trimmed.IndexOf('/') >= 0
                || trimmed.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(
                    "Screenshot state name must not contain path traversal or directory separators.",
                    nameof(stateName));
            }

            return SanitizeSegment(trimmed, "state");
        }

        public static string BuildScreenshotPath(
            string rootDirectory,
            string deviceName,
            string stateName,
            DateTimeOffset capturedAt)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Screenshot root directory is required.", nameof(rootDirectory));

            string safeDeviceName = SanitizeDeviceName(deviceName);
            string safeStateName = SanitizeStateName(stateName);
            string fullRoot = Path.GetFullPath(rootDirectory);
            DateTime localTime = capturedAt.LocalDateTime;
            string directory = Path.Combine(fullRoot, safeDeviceName, localTime.ToString("yyyy-MM-dd"));
            string fileName = $"{localTime:HH-mm-ss}_{safeStateName}.png";
            string fullPath = Path.GetFullPath(Path.Combine(directory, fileName));

            string expectedPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Screenshot path escaped the configured diagnostic directory.");

            return fullPath;
        }

        private static string SanitizeSegment(string value, string fallback)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars()
                .Concat(new[] { '/', '\\', ':' })
                .Distinct()
                .ToArray();

            string sanitized = new string(value.Trim().Select(character =>
                invalidCharacters.Contains(character) || char.IsControl(character)
                    ? '_'
                    : character == ' ' ? '_' : character).ToArray());

            while (sanitized.IndexOf("..", StringComparison.Ordinal) >= 0)
                sanitized = sanitized.Replace("..", "__");

            sanitized = sanitized.Trim('.', '_', ' ');
            return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
        }
    }
}
