using System.Threading;
using System.Threading.Tasks;
using ADB_Tool_Automation_Post_FB.Core.Vision;

namespace ADB_Tool_Automation_Post_FB.Core.GameDetection
{
    public interface IGameStateDetector
    {
        Task<GameDetectionResult> DetectAsync(string deviceName, CancellationToken cancellationToken);
        GameDetectionResult Detect(byte[] screenshotPng);
    }

    public interface IFrameGameStateDetector
    {
        GameDetectionResult Detect(CapturedFrame frame, string deviceName,
            GameStateDetectionContext context);
    }
}
