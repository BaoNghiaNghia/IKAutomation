using System;
using System.Threading;

namespace ADB_Tool_Automation_Post_FB.Core.Diagnostics
{
    public interface IDiagnosticLogger
    {
        void Info(string message);
        void Error(string message, Exception exception);
    }

    public static class DiagnosticStorageGate
    {
        private static int writesEnabled = 1;
        private static string suspensionReason;

        public static bool IsWriteEnabled => Volatile.Read(ref writesEnabled) == 1;
        public static string SuspensionReason => Volatile.Read(ref suspensionReason);

        public static void Suspend(string reason)
        {
            Volatile.Write(ref suspensionReason,
                string.IsNullOrWhiteSpace(reason) ? "Diagnostic storage is unavailable." : reason.Trim());
            Volatile.Write(ref writesEnabled, 0);
        }

        public static void Resume()
        {
            Volatile.Write(ref writesEnabled, 1);
            Volatile.Write(ref suspensionReason, null);
        }
    }
}
