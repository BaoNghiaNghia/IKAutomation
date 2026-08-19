using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public interface IExactResourceLevelFallbackService
    {
        Task<ResourceLevelFallbackResult> SearchSingleLevelAsync(
            string deviceName, ResourceType resourceType, int level,
            bool unoccupiedOnly, string runId, CancellationToken cancellationToken);
    }
}
