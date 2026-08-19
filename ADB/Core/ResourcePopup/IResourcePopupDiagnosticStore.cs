using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.ResourcePopup
{
    public interface IResourcePopupDiagnosticStore
    {
        Task<string> SaveAsync(string deviceName, ResourcePopupOutcome outcome,
            byte[] pngBytes, CancellationToken cancellationToken);
    }
}
