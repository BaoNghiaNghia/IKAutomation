using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Diagnostics
{
    public interface IDeviceDiagnosticService
    {
        DeviceDiagnosticOptions Configuration { get; }

        Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken cancellationToken);

        Task<DeviceDiagnosticResult> CheckDeviceAsync(string deviceName, CancellationToken cancellationToken);
        Task LaunchGameAsync(string deviceName, CancellationToken cancellationToken);
        Task<ScreenshotCaptureResult> CaptureScreenshotAsync(
            string deviceName,
            string stateName,
            string note,
            CancellationToken cancellationToken);
        Task TapAsync(string deviceName, int x, int y, CancellationToken cancellationToken);
        Task TapByPercentAsync(
            string deviceName,
            double xPercent,
            double yPercent,
            CancellationToken cancellationToken);
        Task SwipeByPercentAsync(
            string deviceName,
            double startXPercent,
            double startYPercent,
            double endXPercent,
            double endYPercent,
            int durationMilliseconds,
            CancellationToken cancellationToken);
        Task BackAsync(string deviceName, CancellationToken cancellationToken);
    }
}
