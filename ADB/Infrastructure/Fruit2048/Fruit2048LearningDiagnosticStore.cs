using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    /// <summary>Bounded persistence for terminal Fruit anomalies only.</summary>
    public sealed class Fruit2048LearningDiagnosticStore
    {
        private const int MaxDiagnosticTransitions = 100;
        private readonly string root;

        public Fruit2048LearningDiagnosticStore(string root = null)
        {
            this.root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IKAutomation", "Fruit2048", "LearningDiagnostics");
        }

        public string Save(string deviceName, string sessionId, string transitionId, string reason,
            Fruit2048Board before, Fruit2048Board expected, IReadOnlyList<Fruit2048BoardReadResult> frames)
        {
            string safeDevice = string.IsNullOrWhiteSpace(deviceName) ? "unknown" : deviceName.Replace(Path.DirectorySeparatorChar, '_');
            string directory = Path.Combine(root, safeDevice, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff")
                + "_" + (transitionId ?? "board") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string stagingDirectory = directory + ".tmp";
            Directory.CreateDirectory(stagingDirectory);
            var usable = (frames ?? new Fruit2048BoardReadResult[0]).Where(value => value != null).ToArray();
            try
            {
                for (int index = 0; index < usable.Length; index++)
                {
                    byte[] png = usable[index].OriginalScreenshotPng;
                    if (png == null || png.Length == 0) continue;
                    string name = transitionId == null ? "board_" + (index + 1) + ".png" : "after_" + (index + 1) + ".png";
                    File.WriteAllBytes(Path.Combine(stagingDirectory, name), png);
                }
                string metadata = "{\"DeviceName\":\"" + Escape(deviceName) + "\",\"FruitSessionId\":\"" + Escape(sessionId)
                    + "\",\"TransitionId\":\"" + Escape(transitionId) + "\",\"TimestampUtc\":\"" + DateTimeOffset.UtcNow.ToString("o")
                    + "\",\"FailureReason\":\"" + Escape(reason) + "\",\"BoardBefore\":\"" + Escape(before?.ToString())
                    + "\",\"ExpectedBoardAfterMove\":\"" + Escape(expected?.ToString()) + "\",\"CaptureCount\":" + usable.Length + "}";
                File.WriteAllText(Path.Combine(stagingDirectory, "metadata.json"), metadata);
                Directory.Move(stagingDirectory, directory);
            }
            catch
            {
                if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true);
                throw;
            }
            Trim();
            return directory;
        }

        public string GetLatestForDevice(string deviceName)
        {
            string deviceDirectory = Path.Combine(root, deviceName ?? string.Empty);
            return Directory.Exists(deviceDirectory)
                ? Directory.GetDirectories(deviceDirectory).OrderByDescending(Directory.GetCreationTimeUtc).FirstOrDefault()
                : null;
        }

        private void Trim()
        {
            if (!Directory.Exists(root)) return;
            string[] directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Where(directory => File.Exists(Path.Combine(directory, "metadata.json")))
                .OrderBy(directory => Directory.GetCreationTimeUtc(directory)).ToArray();
            foreach (string directory in directories.Take(Math.Max(0, directories.Length - MaxDiagnosticTransitions)))
                Directory.Delete(directory, true);
        }

        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
