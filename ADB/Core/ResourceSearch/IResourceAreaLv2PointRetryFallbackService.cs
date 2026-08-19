using System.Threading;
using System.Threading.Tasks;
using IK_Auto_ADB.Core.TeamSelection;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public interface IResourceAreaLv2PointRetryFallbackService
    {
        Task<ResourceLevelFallbackResult> SearchSingleLevelForPointRetryAsync(
            string deviceName, ResourceType resourceType, int level,
            bool unoccupiedOnly, string runId, int areaEpoch,
            TeamNumber? expectedTeam, CancellationToken cancellationToken);
    }
}
