using ADB_Tool_Automation_Post_FB.Core.Vision;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Abstractions
{
    /// <summary>Optional high-throughput capture capability for vision hot paths.</summary>
    public interface IFrameCapturingLdPlayerClient
    {
        Task<CapturedFrame> CaptureFrameAsync(string deviceName, CancellationToken cancellationToken);
    }
}
