using IK_Auto_ADB.Core.Vision;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.Abstractions
{
    /// <summary>Optional high-throughput capture capability for vision hot paths.</summary>
    public interface IFrameCapturingLdPlayerClient
    {
        Task<CapturedFrame> CaptureFrameAsync(string deviceName, CancellationToken cancellationToken);
    }
}
