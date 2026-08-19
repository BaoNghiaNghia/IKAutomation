using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public interface IContinuousFarmCheckpointPathProvider
    {
        string GetDirectory();
    }

    public sealed class LocalAppDataContinuousFarmCheckpointPathProvider
        : IContinuousFarmCheckpointPathProvider
    {
        public string GetDirectory() => Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "IKAutomation", "Checkpoints");
    }

    public sealed class LocalAppDataContinuousFarmCheckpointStore
        : IContinuousFarmCheckpointStore
    {
        private readonly IContinuousFarmCheckpointPathProvider pathProvider;
        private readonly IDiagnosticLogger logger;
        private readonly ConcurrentDictionary<string, object> deviceLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public LocalAppDataContinuousFarmCheckpointStore(
            IContinuousFarmCheckpointPathProvider pathProvider, IDiagnosticLogger logger)
        {
            this.pathProvider = pathProvider
                ?? throw new ArgumentNullException(nameof(pathProvider));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ContinuousFarmCheckpoint Load(string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = GetPath(deviceName);
            lock (GetDeviceLock(deviceName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(path)) return null;
                try
                {
                    var serializer = new DataContractJsonSerializer(
                        typeof(CheckpointDocument));
                    using (var stream = new FileStream(path, FileMode.Open,
                        FileAccess.Read, FileShare.Read))
                    {
                        CheckpointDocument document =
                            (CheckpointDocument)serializer.ReadObject(stream);
                        ContinuousFarmCheckpoint checkpoint = document?.ToModel();
                        if (!IsValid(checkpoint, deviceName))
                            throw new SerializationException(
                                "Continuous farm checkpoint is invalid or unsupported.");
                        return checkpoint;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    TryMoveInvalid(path);
                    logger.Error("Continuous farm checkpoint could not be loaded for '"
                        + deviceName + "'. A fresh preflight will be used.", exception);
                    return null;
                }
            }
        }

        public void Save(ContinuousFarmCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsValid(checkpoint, checkpoint?.Device?.DeviceName))
                throw new ArgumentException("Checkpoint is invalid.", nameof(checkpoint));
            string deviceName = checkpoint.Device.DeviceName;
            string path = GetPath(deviceName);
            lock (GetDeviceLock(deviceName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = Path.GetDirectoryName(path);
                Directory.CreateDirectory(directory);
                string temporaryPath = Path.Combine(directory, ".checkpoint-"
                    + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    var serializer = new DataContractJsonSerializer(
                        typeof(CheckpointDocument));
                    using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None))
                    {
                        serializer.WriteObject(stream, CheckpointDocument.From(checkpoint));
                        stream.Flush(true);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                    else File.Move(temporaryPath, path);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    logger.Error("Continuous farm checkpoint could not be saved for '"
                        + deviceName + "'. The in-memory supervisor state remains active.",
                        exception);
                    throw;
                }
                finally
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                    catch (Exception exception)
                    {
                        logger.Error("Temporary continuous farm checkpoint could not be removed.",
                            exception);
                    }
                }
            }
        }

        private object GetDeviceLock(string deviceName) => deviceLocks.GetOrAdd(
            deviceName ?? string.Empty, _ => new object());

        private string GetPath(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("Device name is required.", nameof(deviceName));
            string safe = new string(deviceName.Trim().Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
                .ToArray());
            if (safe.Length > 48) safe = safe.Substring(0, 48);
            return Path.Combine(pathProvider.GetDirectory(), safe + "-"
                + Hash(deviceName) + ".json");
        }

        private static string Hash(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
                return string.Concat(hash.Take(6).Select(item => item.ToString("x2")));
            }
        }

        private static bool IsValid(ContinuousFarmCheckpoint checkpoint,
            string expectedDeviceName) => checkpoint != null
            && checkpoint.Version == 1
            && checkpoint.Device != null
            && !string.IsNullOrWhiteSpace(checkpoint.Device.DeviceName)
            && string.Equals(checkpoint.Device.DeviceName, expectedDeviceName,
                StringComparison.OrdinalIgnoreCase)
            && checkpoint.SavedAt != default(DateTimeOffset);

        private void TryMoveInvalid(string path)
        {
            try
            {
                string invalid = Path.Combine(Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path) + ".invalid-"
                    + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
                if (File.Exists(invalid)) invalid += "." + Guid.NewGuid().ToString("N");
                File.Move(path, invalid);
            }
            catch (Exception exception)
            {
                logger.Error("Invalid continuous farm checkpoint could not be renamed.",
                    exception);
            }
        }

        [DataContract]
        private sealed class CheckpointDocument
        {
            [DataMember(Order = 1)] public int Version { get; set; }
            [DataMember(Order = 2)] public DateTimeOffset SavedAt { get; set; }
            [DataMember(Order = 3)] public ContinuousFarmDeviceSnapshot Device { get; set; }
            [DataMember(Order = 4)] public DateTimeOffset[] TechnicalFailures { get; set; }

            public ContinuousFarmCheckpoint ToModel() => new ContinuousFarmCheckpoint
            {
                Version = Version,
                SavedAt = SavedAt,
                Device = Device,
                TechnicalFailureTimestamps = TechnicalFailures ?? new DateTimeOffset[0]
            };

            public static CheckpointDocument From(ContinuousFarmCheckpoint checkpoint) =>
                new CheckpointDocument
                {
                    Version = checkpoint.Version,
                    SavedAt = checkpoint.SavedAt,
                    Device = checkpoint.Device,
                    TechnicalFailures = (checkpoint.TechnicalFailureTimestamps
                        ?? new DateTimeOffset[0]).ToArray()
                };
        }
    }
}
