using System.Threading;
using System.Threading.Tasks;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public interface IResourceAreaLv2PointRetryFallbackService
    {
        Task<ResourceLevelFallbackResult> SearchSingleLevelForPointRetryAsync(
            string deviceName, ResourceType resourceType, int level,
            bool unoccupiedOnly, string runId, int areaEpoch,
            TeamNumber? expectedTeam, CancellationToken cancellationToken);
    }
}
