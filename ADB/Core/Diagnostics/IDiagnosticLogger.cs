using System;

namespace ADB_Tool_Automation_Post_FB.Core.Diagnostics
{
    public interface IDiagnosticLogger
    {
        void Info(string message);
        void Error(string message, Exception exception);
    }
}
