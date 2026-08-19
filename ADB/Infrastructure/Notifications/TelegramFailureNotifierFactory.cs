using IK_Auto_ADB.Infrastructure.Diagnostics;
using System;
using System.Net.Http;

namespace IK_Auto_ADB.Infrastructure.Notifications
{
    public static class TelegramFailureNotifierFactory
    {
        public static TelegramFailureNotifier CreateFromEnvironment()
        {
            TelegramLocalSettings local = TelegramLocalSettings.Load(
                TelegramLocalSettings.DefaultPath);
            string botToken = Environment.GetEnvironmentVariable(
                TelegramFailureNotifier.BotTokenEnvironmentVariable);
            string chatId = Environment.GetEnvironmentVariable(
                TelegramFailureNotifier.ChatIdEnvironmentVariable);

            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            return new TelegramFailureNotifier(client,
                string.IsNullOrWhiteSpace(botToken) ? local.BotToken : botToken,
                string.IsNullOrWhiteSpace(chatId) ? local.ChatId : chatId,
                new ApplicationDiagnosticLogger());
        }
    }
}
