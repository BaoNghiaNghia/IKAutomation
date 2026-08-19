using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.ResourcePopup
{
    public interface IResourceAwarePopupVerificationService : IResourcePopupVerificationService
    {
        Task<ResourcePopupVerificationResult> VerifyAsync(string deviceName,
            ResourceType resourceType, CancellationToken cancellationToken);
    }
}
