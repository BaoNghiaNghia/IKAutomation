using IK_Auto_ADB.Core.Fruit2048;
using System;
using System.IO;

namespace IK_Auto_ADB.Infrastructure.Fruit2048
{
    /// <summary>Stores the concise result of a FirstLearningProof run; no screenshots are retained.</summary>
    public sealed class Fruit2048LearningProofStore
    {
        private readonly string root;

        public Fruit2048LearningProofStore(string root = null)
        {
            this.root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IKAutomation", "Fruit2048", "LearningProof");
        }

        public string Save(Fruit2048LearningProofReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            Directory.CreateDirectory(root);
            string safeDevice = string.IsNullOrWhiteSpace(report.DeviceName) ? "unknown" : report.DeviceName.Replace(Path.DirectorySeparatorChar, '_');
            string path = Path.Combine(root, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff")
                + "_" + safeDevice + ".json");
            string staging = path + ".tmp";
            string json = "{\"DeviceName\":\"" + Escape(report.DeviceName)
                + "\",\"FruitSessionId\":\"" + Escape(report.FruitSessionId)
                + "\",\"TeacherSessionId\":\"" + Escape(report.TeacherSessionId)
                + "\",\"StartedAtUtc\":\"" + report.StartedAtUtc.ToString("o")
                + "\",\"EndedAtUtc\":\"" + report.EndedAtUtc.ToString("o")
                + "\",\"InitialEmptyCells\":" + report.InitialEmptyCells
                + ",\"InitialTier1Cells\":" + report.InitialTier1Cells
                + ",\"InitialUnknownCells\":" + report.InitialUnknownCells
                + ",\"Moves\":" + report.Moves
                + ",\"Tier1MergeTransitionsObserved\":" + report.Tier1MergeTransitionsObserved
                + ",\"Tier2EvidenceAccepted\":" + report.Tier2EvidenceAccepted
                + ",\"Tier2EvidenceRejected\":" + report.Tier2EvidenceRejected
                + ",\"Tier2Learned\":" + JsonBoolean(report.Tier2Learned)
                + ",\"Tier2LearnedAtMove\":" + NullableInt(report.Tier2LearnedAtMove)
                + ",\"Tier2RecognitionConfirmed\":" + JsonBoolean(report.Tier2RecognitionConfirmed)
                + ",\"CatalogVersionStart\":" + report.CatalogVersionStart
                + ",\"CatalogVersionEnd\":" + report.CatalogVersionEnd
                + ",\"TransitionInvalidCount\":" + report.TransitionInvalidCount
                + ",\"LearningConflictCount\":" + report.LearningConflictCount
                + ",\"Outcome\":\"" + Escape(report.Outcome)
                + "\",\"FailureReason\":\"" + report.FailureReason + "\"}";
            File.WriteAllText(staging, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(staging, path);
            return path;
        }

        // Compatibility helper retained for focused callers created before the
        // structured proof report was introduced.
        public string Save(string deviceName, string teacherSessionId, string outcome,
            Fruit2048FirstLearningProofProgress proof, string failureReason)
        {
            return Save(new Fruit2048LearningProofReport
            {
                DeviceName = deviceName, TeacherSessionId = teacherSessionId,
                StartedAtUtc = DateTimeOffset.UtcNow, EndedAtUtc = DateTimeOffset.UtcNow,
                Moves = proof?.MovesUsed ?? 0,
                Tier2EvidenceAccepted = proof?.Tier2SampleCount ?? 0,
                Tier2Learned = proof?.Tier2Learned ?? false,
                Tier2RecognitionConfirmed = proof?.Tier2RecognitionConfirmed ?? false,
                Outcome = outcome,
                FailureReason = string.IsNullOrWhiteSpace(failureReason)
                    ? Fruit2048LearningProofFailure.None : Fruit2048LearningProofFailure.Unexpected
            });
        }

        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        private static string JsonBoolean(bool value) => value ? "true" : "false";
        private static string NullableInt(int? value) => value.HasValue ? value.Value.ToString() : "null";
    }
}
