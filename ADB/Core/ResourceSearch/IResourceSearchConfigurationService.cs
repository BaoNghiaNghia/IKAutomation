using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public interface IResourceSearchConfigurationService
    {
        Task<ResourceSearchConfigurationResult> ConfigureAsync(
            string deviceName,
            ResourceSearchConfigurationRequest request,
            CancellationToken cancellationToken);
    }
}
