using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public interface IResourceLevelFallbackService
    {
        Task<ResourceLevelFallbackResult> SearchAsync(string deviceName,
            ResourceType resourceType, ResourceLevelFallbackPolicy policy,
            bool unoccupiedOnly, CancellationToken cancellationToken);
    }
}
