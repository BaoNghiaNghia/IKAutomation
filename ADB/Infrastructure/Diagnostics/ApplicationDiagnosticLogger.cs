using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Helpers;
using System;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics
{
    public sealed class ApplicationDiagnosticLogger : IDiagnosticLogger
    {
        public void Info(string message)
        {
            Logger.LogInfo(message);
        }

        public void Error(string message, Exception exception)
        {
            Logger.LogError($"{message}{Environment.NewLine}{exception}");
        }
    }
}
