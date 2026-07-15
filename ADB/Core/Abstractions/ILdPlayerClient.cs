using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Abstractions
{
    /// <summary>
    /// Android key codes supported by the LDPlayer automation boundary.
    /// Values match Android KeyEvent codes and are mapped to Auto_LDPlayer.LDKeyEvent
    /// by the infrastructure adapter.
    /// </summary>
    public enum AndroidKeyCode
    {
        Home = 3,
        Back = 4,
        DPadUp = 19,
        DPadDown = 20,
        DPadLeft = 21,
        DPadRight = 22,
        DPadCenter = 23,
        Tab = 61,
        Space = 62,
        Enter = 66,
        Delete = 67,
        Menu = 82,
        AppSwitch = 187
    }

    /// <summary>
    /// Boundary used by new automation code to control an LDPlayer instance.
    /// Implementations identify devices by LDPlayer name.
    /// </summary>
    public interface ILdPlayerClient
    {
        Task<bool> IsRunningAsync(string deviceName, CancellationToken cancellationToken);

        Task OpenAsync(string deviceName, CancellationToken cancellationToken);

        Task CloseAsync(string deviceName, CancellationToken cancellationToken);

        Task RunAppAsync(string deviceName, string packageName, CancellationToken cancellationToken);

        Task<byte[]> CaptureScreenshotPngAsync(string deviceName, CancellationToken cancellationToken);

        Task TapAsync(string deviceName, int x, int y, CancellationToken cancellationToken);

        Task TapByPercentAsync(string deviceName, double xPercent, double yPercent, CancellationToken cancellationToken);

        Task LongPressAsync(string deviceName, int x, int y, int durationMilliseconds, CancellationToken cancellationToken);

        Task SwipeByPercentAsync(
            string deviceName,
            double startXPercent,
            double startYPercent,
            double endXPercent,
            double endYPercent,
            int durationMilliseconds,
            CancellationToken cancellationToken);

        Task BackAsync(string deviceName, CancellationToken cancellationToken);

        Task InputTextAsync(string deviceName, string text, CancellationToken cancellationToken);

        Task PressKeyAsync(string deviceName, AndroidKeyCode keyCode, CancellationToken cancellationToken);
    }
}
