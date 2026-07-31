using ADB_Tool_Automation_Post_FB.Core.GameDetection;
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
    }
}
