using System.Threading;
using System.Threading.Tasks;
using IK_Auto_ADB.Core.Vision;

namespace IK_Auto_ADB.Core.GameDetection
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
