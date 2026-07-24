using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using System;
using System.Net.Http;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Notifications
{
    public static class TelegramFailureNotifierFactory
    {
        public static TelegramFailureNotifier CreateFromEnvironment()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            return new TelegramFailureNotifier(client,
                Environment.GetEnvironmentVariable(
                    TelegramFailureNotifier.BotTokenEnvironmentVariable),
                Environment.GetEnvironmentVariable(
                    TelegramFailureNotifier.ChatIdEnvironmentVariable),
                new ApplicationDiagnosticLogger());
        }
    }
}
