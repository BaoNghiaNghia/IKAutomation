using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.GameDetection
{
    public interface IGameStateDetector
    {
        Task<GameDetectionResult> DetectAsync(string deviceName, CancellationToken cancellationToken);
        GameDetectionResult Detect(byte[] screenshotPng);
    }
}
