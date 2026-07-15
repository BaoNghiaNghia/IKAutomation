using ADB_Tool_Automation_Post_FB.Helpers;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ADB_Tool_Automation_Post_FB.Exceptions
{
    internal static class TaskExceptions
    {
        public static void RegisterGlobalHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += HandleDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
            Application.Current.DispatcherUnhandledException += HandleDispatcherUnhandledException;
        }

        private static void HandleDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as System.Exception;
            if (exception != null)
            {
                Logger.LogError($"Unhandled Exception: {exception.Message}\n{exception.StackTrace}");
            }
            else
            {
                Logger.LogError("Unhandled Exception: Unknown exception object.");
            }
        }

        private static void HandleUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.LogError($"Unobserved Task Exception: {e.Exception.Message}\n{e.Exception.StackTrace}");
            e.SetObserved(); // Prevents the process from terminating.
        }

        private static void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.LogError($"UI Thread Exception: {e.Exception.Message}\n{e.Exception.StackTrace}");
            e.Handled = true; // Prevent crash
        }
    }
}
