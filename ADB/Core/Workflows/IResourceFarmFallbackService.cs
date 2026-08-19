using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IResourceFarmFallbackService
    {
        Task<ResourceFarmFallbackResult> RunAsync(string deviceName,
            OneShotFarmRequest request, GameState initialState,
            IProgress<ResourceFarmFallbackProgress> progress,
            CancellationToken cancellationToken);
    }

    public sealed class ResourceFarmFallbackProgress
    {
        public int RecoveryAttempt { get; set; }
        public string TerritoryColorSummary { get; set; }
        public OneShotFarmStep? CurrentStep { get; set; }
        public bool ClearTerritoryColor { get; set; }
        public MapRepositionState MapRepositionState { get; set; }
        public ResourceType? CurrentResource { get; set; }
        public int? EffectiveLevel { get; set; }
        public string FarmRunId { get; set; }
        public string ResourceToastVariant { get; set; }
        public DateTimeOffset? ResourceToastDetectedAt { get; set; }
        public string ResourceToastState { get; set; }
    }
}
