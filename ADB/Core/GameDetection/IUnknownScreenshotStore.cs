using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.GameDetection
{
    public interface IUnknownScreenshotStore
    {
        Task<string> SaveAsync(
            string deviceName,
            byte[] screenshotPng,
            CancellationToken cancellationToken);
    }
}
