using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public interface IResourceSearchConfigurationService
    {
        Task<ResourceSearchConfigurationResult> ConfigureAsync(
            string deviceName,
            ResourceSearchConfigurationRequest request,
            CancellationToken cancellationToken);
    }
}
