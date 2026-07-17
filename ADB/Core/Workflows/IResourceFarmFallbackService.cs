using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IResourceFarmFallbackService
    {
        Task<ResourceFarmFallbackResult> RunAsync(string deviceName,
            OneShotFarmRequest request, GameState initialState, CancellationToken cancellationToken);
    }
}
