using System;
using System.IO;

namespace IK_Auto_ADB.Infrastructure.Notifications
{
    internal sealed class TelegramLocalSettings
    {
        internal const string FileName = "telegram.local.settings";

        internal string BotToken { get; private set; }
        internal string ChatId { get; private set; }

        internal static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IKAutomation", FileName);

        internal static TelegramLocalSettings Load(string path)
        {
            var settings = new TelegramLocalSettings();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return settings;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                return settings;
            }
            catch (UnauthorizedAccessException)
            {
                return settings;
            }

            foreach (string rawLine in lines)
            {
                string line = rawLine?.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (key.Equals("BotToken", StringComparison.OrdinalIgnoreCase))
                    settings.BotToken = value;
                else if (key.Equals("ChatId", StringComparison.OrdinalIgnoreCase))
                    settings.ChatId = value;
            }

            return settings;
        }
    }
}
