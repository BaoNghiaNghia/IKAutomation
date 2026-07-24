using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public interface IResourceLevelFallbackService
    {
        Task<ResourceLevelFallbackResult> SearchAsync(string deviceName,
            ResourceType resourceType, ResourceLevelFallbackPolicy policy,
            bool unoccupiedOnly, CancellationToken cancellationToken);
    }
}
