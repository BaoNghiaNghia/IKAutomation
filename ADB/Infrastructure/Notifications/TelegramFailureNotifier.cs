using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Notifications;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Notifications
{
    public sealed class TelegramFailureNotifier : IAutomationFailureNotifier
    {
        public const string BotTokenEnvironmentVariable = "IKAUTOMATION_TELEGRAM_BOT_TOKEN";
        public const string ChatIdEnvironmentVariable = "IKAUTOMATION_TELEGRAM_CHAT_ID";
        private const int TelegramMessageLimit = 4096;
        private readonly HttpClient httpClient;
        private readonly string botToken;
        private readonly string chatId;
        private readonly IDiagnosticLogger logger;

        public TelegramFailureNotifier(HttpClient httpClient, string botToken,
            string chatId, IDiagnosticLogger logger)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.botToken = botToken?.Trim();
            this.chatId = chatId?.Trim();
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(botToken)
            && !string.IsNullOrWhiteSpace(chatId);

        public async Task NotifyAsync(AutomationFailureNotification notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsConfigured || notification == null) return;
            try
            {
                string endpoint = "https://api.telegram.org/bot" + botToken + "/sendMessage";
                using (var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["chat_id"] = chatId,
                    ["text"] = Format(notification),
                    ["disable_web_page_preview"] = "true"
                }))
                using (HttpResponseMessage response = await httpClient.PostAsync(
                    endpoint, content, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                }
                logger.Info($"[Telegram Notification] DeviceName='{notification.DeviceName}', Outcome='{notification.Outcome}', Sent=True");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                // Never pass the transport exception through because its request URI can
                // contain the bot token. Only the exception type is safe to persist.
                logger.Error("[Telegram Notification] Delivery failed. Bot token was not logged.",
                    new InvalidOperationException(
                        "Telegram transport failure: " + exception.GetType().Name));
            }
        }

        public static string Format(AutomationFailureNotification notification)
        {
            var builder = new StringBuilder();
            builder.AppendLine("IKAutomation error");
            Append(builder, "Device", notification.DeviceName);
            Append(builder, "Outcome", notification.Outcome);
            Append(builder, "Step", notification.Step);
            Append(builder, "Resource", notification.Resource);
            Append(builder, "Level", notification.Level);
            Append(builder, "Team", notification.Team);
            Append(builder, "Message", notification.Message);
            Append(builder, "Error", notification.Error);
            Append(builder, "Diagnostic", notification.DiagnosticPath);
            string result = builder.ToString().TrimEnd();
            return result.Length <= TelegramMessageLimit
                ? result : result.Substring(0, TelegramMessageLimit - 16) + "\n...[truncated]";
        }

        private static void Append(StringBuilder builder, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                builder.Append(label).Append(": ").AppendLine(value.Trim());
        }
    }
}
