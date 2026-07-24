using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Workflows;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public interface IFarmUiPreferencesPathProvider
    {
        string GetPath();
    }

    public sealed class LocalAppDataFarmUiPreferencesPathProvider : IFarmUiPreferencesPathProvider
    {
        public string GetPath() => Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "IKAutomation",
            "farm-ui-preferences.json");
    }

    public interface IFarmUiPreferencesFileCommitter
    {
        void Commit(string temporaryPath, string destinationPath);
    }

    public sealed class AtomicFarmUiPreferencesFileCommitter : IFarmUiPreferencesFileCommitter
    {
        public void Commit(string temporaryPath, string destinationPath)
        {
            if (File.Exists(destinationPath)) File.Replace(temporaryPath, destinationPath, null);
            else File.Move(temporaryPath, destinationPath);
        }
    }

    public sealed class LocalAppDataFarmUiPreferencesStore : IFarmUiPreferencesStore
    {
        private readonly IFarmUiPreferencesPathProvider pathProvider;
        private readonly IDiagnosticLogger logger;
        private readonly IFarmUiPreferencesFileCommitter committer;

        public LocalAppDataFarmUiPreferencesStore(IFarmUiPreferencesPathProvider pathProvider,
            IDiagnosticLogger logger) : this(pathProvider, logger,
                new AtomicFarmUiPreferencesFileCommitter())
        {
        }

        public LocalAppDataFarmUiPreferencesStore(IFarmUiPreferencesPathProvider pathProvider,
            IDiagnosticLogger logger, IFarmUiPreferencesFileCommitter committer)
        {
            this.pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.committer = committer ?? throw new ArgumentNullException(nameof(committer));
        }

        public Task<FarmUiPreferencesLoadResult> LoadAsync(FarmUiPreferences defaults,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));
            string path = pathProvider.GetPath();
            if (!File.Exists(path)) return Task.FromResult(Default(defaults,
                "No saved farm preferences were found; current application defaults are used."));
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                FarmUiPreferences preferences;
                var serializer = new DataContractJsonSerializer(typeof(PreferencesDocument));
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read))
                {
                    preferences = ((PreferencesDocument)serializer.ReadObject(stream)).ToModel();
                }
                FarmUiPreferencesValidationResult validation = FarmUiPreferencesMapper.Validate(preferences);
                if (preferences.Version != 1 || !validation.IsValid)
                    throw new SerializationException(preferences.Version != 1
                        ? "Unsupported farm preference version." : validation.Message);
                return Task.FromResult(new FarmUiPreferencesLoadResult
                {
                    Success = true, Preferences = preferences, UsedDefaults = false,
                    Message = "Saved farm preferences loaded."
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                TryMoveInvalid(path);
                logger.Error("Farm preference file is invalid; application defaults are used.",
                    exception);
                FarmUiPreferencesLoadResult result = Default(defaults,
                    "Cấu hình farm bị lỗi; đã khôi phục mặc định.");
                result.RecoveredInvalidFile = true;
                result.ErrorMessage = exception.Message;
                return Task.FromResult(result);
            }
        }

        public Task<FarmUiPreferencesSaveResult> SaveAsync(FarmUiPreferences preferences,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FarmUiPreferencesValidationResult validation = FarmUiPreferencesMapper.Validate(preferences);
            if (!validation.IsValid) return Task.FromResult(new FarmUiPreferencesSaveResult
            {
                Success = false, Message = validation.Message, ErrorMessage = validation.Message
            });

            string temporaryPath = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = pathProvider.GetPath();
                string directory = Path.GetDirectoryName(path);
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory,
                    ".farm-ui-preferences-" + Guid.NewGuid().ToString("N") + ".tmp");
                var serializer = new DataContractJsonSerializer(typeof(PreferencesDocument));
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    serializer.WriteObject(stream, PreferencesDocument.From(preferences));
                    stream.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                committer.Commit(temporaryPath, path);
                return Task.FromResult(new FarmUiPreferencesSaveResult
                {
                    Success = true, Message = "Farm settings saved."
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.Error("Farm preferences could not be saved; in-memory settings remain usable.",
                    exception);
                return Task.FromResult(new FarmUiPreferencesSaveResult
                {
                    Success = false, Message = "Không thể lưu cấu hình farm; vẫn dùng cấu hình hiện tại.",
                    ErrorMessage = exception.Message
                });
            }
            finally
            {
                try { if (!string.IsNullOrWhiteSpace(temporaryPath)
                        && File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (Exception exception)
                {
                    logger.Error("Temporary farm preference file could not be removed.", exception);
                }
            }
        }

        public Task ResetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = pathProvider.GetPath();
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        private bool TryMoveInvalid(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                string invalid = Path.Combine(directory, "farm-ui-preferences.invalid-"
                    + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                if (File.Exists(invalid)) invalid = Path.Combine(directory,
                    "farm-ui-preferences.invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                    + "-" + Guid.NewGuid().ToString("N") + ".json");
                File.Move(path, invalid);
                return true;
            }
            catch (Exception exception)
            {
                logger.Error("Invalid farm preference file could not be renamed.", exception);
                return false;
            }
        }

        private static FarmUiPreferencesLoadResult Default(FarmUiPreferences defaults,
            string message) => new FarmUiPreferencesLoadResult
            {
                Success = true, Preferences = PreferencesDocument.From(defaults).ToModel(),
                UsedDefaults = true, Message = message
            };

        [DataContract]
        private sealed class PreferencesDocument
        {
            [DataMember(Order = 1)] public int Version { get; set; }
            [DataMember(Order = 2)] public bool Iron { get; set; }
            [DataMember(Order = 3)] public bool Stone { get; set; }
            [DataMember(Order = 4)] public bool Wood { get; set; }
            [DataMember(Order = 5)] public bool Food { get; set; }
            [DataMember(Order = 6)] public int[] LevelPriority { get; set; }
            [DataMember(Order = 7)] public TeamNumber[] TeamPriority { get; set; }
            [DataMember(Order = 8)] public bool AllowTeam1 { get; set; }
            [DataMember(Order = 9)] public int ReadyCheckIntervalMinutes { get; set; }
            [DataMember(Order = 10)] public int ReadyMaxWaitHours { get; set; }
            [DataMember(Order = 11)] public bool UnoccupiedOnly { get; set; }

            public FarmUiPreferences ToModel() => new FarmUiPreferences
            {
                Version = Version, Iron = Iron, Stone = Stone, Wood = Wood, Food = Food,
                LevelPriority = LevelPriority ?? new int[0],
                TeamPriority = TeamPriority ?? new TeamNumber[0], AllowTeam1 = AllowTeam1,
                ReadyCheckIntervalMinutes = ReadyCheckIntervalMinutes,
                ReadyMaxWaitHours = ReadyMaxWaitHours, UnoccupiedOnly = UnoccupiedOnly
            };

            public static PreferencesDocument From(FarmUiPreferences value) =>
                new PreferencesDocument
                {
                    Version = value.Version, Iron = value.Iron, Stone = value.Stone,
                    Wood = value.Wood, Food = value.Food,
                    LevelPriority = value.LevelPriority == null ? new int[0]
                        : new System.Collections.Generic.List<int>(value.LevelPriority).ToArray(),
                    TeamPriority = value.TeamPriority == null ? new TeamNumber[0]
                        : new System.Collections.Generic.List<TeamNumber>(value.TeamPriority).ToArray(),
                    AllowTeam1 = value.AllowTeam1,
                    ReadyCheckIntervalMinutes = value.ReadyCheckIntervalMinutes,
                    ReadyMaxWaitHours = value.ReadyMaxWaitHours,
                    UnoccupiedOnly = value.UnoccupiedOnly
                };
        }
    }
}
