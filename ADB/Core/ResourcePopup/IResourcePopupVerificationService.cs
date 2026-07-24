using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.ResourcePopup
{
    public interface IResourcePopupVerificationService
    {
        Task<ResourcePopupVerificationResult> VerifyAsync(
            string deviceName, CancellationToken cancellationToken);
    }
}
