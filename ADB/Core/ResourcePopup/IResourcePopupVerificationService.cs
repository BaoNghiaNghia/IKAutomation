using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.ResourcePopup
{
    public interface IResourcePopupVerificationService
    {
        Task<ResourcePopupVerificationResult> VerifyAsync(
            string deviceName, CancellationToken cancellationToken);
    }
}
