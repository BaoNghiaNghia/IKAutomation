using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Notifications;
using ADB_Tool_Automation_Post_FB.Infrastructure.Notifications;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace IKAutomation.TelegramNotifications.Tests
{
    internal static class Program
    {
        private static readonly CancellationToken Token = new CancellationToken(false);
        private static int passed;
        private static int failed;

        private static int Main()
        {
            Run("Missing configuration sends no request", MissingConfiguration);
            Run("Configured notifier posts failure summary", SendsSummary);
            Run("Long message is bounded", LongMessageBounded);
            Run("Transport error does not leak token", TransportErrorDoesNotLeakToken);
            Run("Cancelled token sends no request", Cancellation);
            Console.WriteLine($"Telegram notification tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void MissingConfiguration()
        {
            var handler = new FakeHandler();
            var notifier = new TelegramFailureNotifier(new HttpClient(handler), null, null,
                new FakeLogger());
            notifier.NotifyAsync(Notification(), Token).GetAwaiter().GetResult();
            Equal(0, handler.Requests);
            Assert(!notifier.IsConfigured, "Notifier unexpectedly configured.");
        }

        private static void SendsSummary()
        {
            var handler = new FakeHandler();
            var logger = new FakeLogger();
            var notifier = new TelegramFailureNotifier(new HttpClient(handler), "test-token",
                "12345", logger);
            notifier.NotifyAsync(Notification(), Token).GetAwaiter().GetResult();
            Equal(1, handler.Requests);
            Assert(handler.Body.Contains("chat_id=12345"), handler.Body);
            Assert(handler.Body.Contains("IKAutomation+error"), handler.Body);
            Assert(!string.Join("\n", logger.Messages).Contains("test-token"),
                "Token leaked to diagnostic log.");
        }

        private static void LongMessageBounded()
        {
            AutomationFailureNotification notification = Notification();
            notification.Error = new string('x', 5000);
            string text = TelegramFailureNotifier.Format(notification);
            Assert(text.Length <= 4096 && text.EndsWith("...[truncated]"),
                "Telegram length limit was not enforced.");
        }

        private static void TransportErrorDoesNotLeakToken()
        {
            var handler = new FakeHandler { ThrowOnSend = true };
            var logger = new FakeLogger();
            var notifier = new TelegramFailureNotifier(new HttpClient(handler),
                "secret-test-token", "12345", logger);
            notifier.NotifyAsync(Notification(), Token).GetAwaiter().GetResult();
            string log = string.Join("\n", logger.Messages);
            Assert(!log.Contains("secret-test-token") && log.Contains("HttpRequestException"),
                "Transport log was not safely sanitized: " + log);
        }

        private static void Cancellation()
        {
            var handler = new FakeHandler();
            var notifier = new TelegramFailureNotifier(new HttpClient(handler), "test-token",
                "12345", new FakeLogger());
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                try { notifier.NotifyAsync(Notification(), source.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { Equal(0, handler.Requests); return; }
            }
            throw new Exception("Expected cancellation.");
        }

        private static AutomationFailureNotification Notification() =>
            new AutomationFailureNotification
            {
                DeviceName = "LDPlayer", Outcome = "Failed", Step = "ExecuteSearch",
                Resource = "Stone", Level = "6", Message = "Search failed",
                DiagnosticPath = "Diagnostics/failure.png"
            };

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS: " + name); }
            catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL: " + name + " - " + exception); }
        }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}."); }

        private sealed class FakeHandler : HttpMessageHandler
        {
            public int Requests;
            public string Body = string.Empty;
            public bool ThrowOnSend;
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests++;
                if (ThrowOnSend)
                    throw new HttpRequestException(
                        "Request failed for https://api.telegram.org/botsecret-test-token/sendMessage");
                Body = await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }

        private sealed class FakeLogger : IDiagnosticLogger
        {
            public readonly List<string> Messages = new List<string>();
            public void Info(string message) => Messages.Add(message);
            public void Error(string message, Exception exception) =>
                Messages.Add(message + exception.Message);
        }
    }
}
