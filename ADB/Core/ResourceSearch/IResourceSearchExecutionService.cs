using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public interface IResourceSearchExecutionService
    {
        Task<ResourceSearchExecutionResult> ExecuteAsync(
            string deviceName,
            ResourceSearchExecutionRequest request,
            CancellationToken cancellationToken);
    }
}
