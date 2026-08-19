using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public interface IResourceSearchExecutionService
    {
        Task<ResourceSearchExecutionResult> ExecuteAsync(
            string deviceName,
            ResourceSearchExecutionRequest request,
            CancellationToken cancellationToken);
    }
}
