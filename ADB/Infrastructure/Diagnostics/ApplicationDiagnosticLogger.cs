using IK_Auto_ADB.Core.Diagnostics;
using IK_Auto_ADB.Helpers;
using System;

namespace IK_Auto_ADB.Infrastructure.Diagnostics
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
