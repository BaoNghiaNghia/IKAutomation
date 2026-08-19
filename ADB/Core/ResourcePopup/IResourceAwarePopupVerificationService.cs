using IK_Auto_ADB.Core.ResourceSearch;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.ResourcePopup
{
    public interface IResourceAwarePopupVerificationService : IResourcePopupVerificationService
    {
        Task<ResourcePopupVerificationResult> VerifyAsync(string deviceName,
            ResourceType resourceType, CancellationToken cancellationToken);
    }
}
