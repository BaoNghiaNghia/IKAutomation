using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using System;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    /// <summary>Process-wide serialized writer for shared Fruit learning knowledge.</summary>
    public sealed class Fruit2048LearningCoordinator : IFruit2048LearningCoordinator
    {
        private readonly object sync = new object();
        private readonly IFruitTileLearningCatalog catalog;
        private readonly IDiagnosticLogger logger;
        private Fruit2048TeacherSnapshot teacher = new Fruit2048TeacherSnapshot { Status = Fruit2048TeacherStatus.None };
        private long version;
        public Fruit2048LearningCoordinator(IFruitTileLearningCatalog catalog, IDiagnosticLogger logger) { this.catalog = catalog; this.logger = logger; }
        public Fruit2048TeacherSnapshot Teacher { get { lock (sync) return new Fruit2048TeacherSnapshot { DeviceName = teacher.DeviceName, SessionId = teacher.SessionId, Status = teacher.Status, CatalogVersion = version }; } }
        public bool SelectTeacher(string deviceName, out string reason)
        {
            lock (sync)
            {
                if (teacher.Status == Fruit2048TeacherStatus.Learning) { reason = "Thiết bị học hiện tại đang chạy. Hãy dừng trước khi đổi."; return false; }
                teacher = new Fruit2048TeacherSnapshot { DeviceName = deviceName, Status = Fruit2048TeacherStatus.Paused, CatalogVersion = version };
                reason = string.Empty; return true;
            }
        }
        public bool TryAcquireTeacher(string deviceName, out string sessionId, out string reason)
        {
            lock (sync)
            {
                if (!string.Equals(teacher.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                { sessionId = null; reason = "Thiết bị học hiện tại đang chạy. Hãy dừng trước khi đổi."; return false; }
                teacher = new Fruit2048TeacherSnapshot { DeviceName = deviceName, SessionId = Guid.NewGuid().ToString("N"), Status = Fruit2048TeacherStatus.Learning, CatalogVersion = version };
                sessionId = teacher.SessionId; reason = string.Empty; Log(deviceName, sessionId, "Acquire", teacher.Status, reason); return true;
            }
        }
        public void ReleaseTeacher(string deviceName, string sessionId, string reason)
        {
            lock (sync) if (string.Equals(teacher.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase) && teacher.SessionId == sessionId)
            { Log(deviceName, sessionId, "Release", Fruit2048TeacherStatus.None, reason); teacher = new Fruit2048TeacherSnapshot { Status = Fruit2048TeacherStatus.None, CatalogVersion = version }; }
        }
        public bool CanLearn(string deviceName) { lock (sync) return teacher.Status == Fruit2048TeacherStatus.Learning && string.Equals(teacher.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase); }
        public Fruit2048LearningResult ObserveMerge(string deviceName, int tier, FruitTileVisualFingerprint fingerprint, string transitionId, int sourceTier, Fruit2048Move move, int sourceRow, int sourceColumn)
        {
            lock (sync)
            {
                if (!CanLearnUnsafe(deviceName)) return new Fruit2048LearningResult { Tier = tier, Action = Fruit2048LearningAction.DuplicateIgnored, Error = "ConsumerReadOnly" };
                Fruit2048LearningResult result = catalog.ObserveMerge(tier, fingerprint, transitionId, sourceTier, move, sourceRow, sourceColumn);
                if (result.Action != Fruit2048LearningAction.DuplicateIgnored) version++;
                logger?.Info($"[Fruit2048 Catalog Update] CatalogVersionBefore={version - 1}, CatalogVersionAfter={version}, Tier={tier}, Action='{result.Action}', State='{result.State}', PrototypeCount=0");
                return result;
            }
        }
        private bool CanLearnUnsafe(string d) => teacher.Status == Fruit2048TeacherStatus.Learning && string.Equals(teacher.DeviceName, d, StringComparison.OrdinalIgnoreCase);
        private void Log(string d,string s,string a,Fruit2048TeacherStatus st,string r) => logger?.Info($"[Fruit2048 Teacher] DeviceName='{d}', TeacherSessionId='{s}', Action='{a}', Status='{st}', Reason='{r}'");
    }
}
