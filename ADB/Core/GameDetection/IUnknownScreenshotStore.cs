using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.GameDetection
{
    public interface IUnknownScreenshotStore
    {
        Task<string> SaveAsync(
            string deviceName,
            byte[] screenshotPng,
            CancellationToken cancellationToken);
    }
}
