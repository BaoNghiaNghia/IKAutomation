using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ADB_Tool_Automation_Post_FB.UI
{
    public partial class DeviceDiagnosticWindow : Window
    {
        private readonly IDeviceDiagnosticService diagnosticService;
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();

        public DeviceDiagnosticWindow(IDeviceDiagnosticService diagnosticService)
        {
            this.diagnosticService = diagnosticService
                ?? throw new ArgumentNullException(nameof(diagnosticService));

            InitializeComponent();
            PackageNameTextBox.Text = string.IsNullOrWhiteSpace(diagnosticService.Configuration.PackageName)
                ? "(not configured)"
                : diagnosticService.Configuration.PackageName;
            Closed += (sender, args) => lifetimeCancellation.Cancel();
        }

        private async void CheckDevice_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                DeviceDiagnosticResult result = await diagnosticService.CheckDeviceAsync(
                    DeviceNameTextBox.Text,
                    cancellationToken);
                return FormatCheckResult(result);
            });
        }

        private async void LaunchGame_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                await diagnosticService.LaunchGameAsync(DeviceNameTextBox.Text, cancellationToken);
                return "Game launch command sent. Use Capture Screenshot when the desired screen is visible.";
            });
        }

        private async void CaptureScreenshot_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                ScreenshotCaptureResult result = await diagnosticService.CaptureScreenshotAsync(
                    DeviceNameTextBox.Text,
                    StateNameComboBox.Text,
                    NoteTextBox.Text,
                    cancellationToken);
                return $"Screenshot saved: {result.ScreenshotPath}{Environment.NewLine}"
                    + $"Metadata: {result.MetadataPath}{Environment.NewLine}"
                    + $"Resolution: {result.Width}x{result.Height}";
            });
        }

        private async void TapTest_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                if (TapModeComboBox.SelectedIndex == 0)
                {
                    await diagnosticService.TapAsync(
                        DeviceNameTextBox.Text,
                        ParseInteger(TapXTextBox.Text, "Tap X"),
                        ParseInteger(TapYTextBox.Text, "Tap Y"),
                        cancellationToken);
                    return "Pixel tap command sent.";
                }

                await diagnosticService.TapByPercentAsync(
                    DeviceNameTextBox.Text,
                    ParseDouble(TapXTextBox.Text, "Tap X percent"),
                    ParseDouble(TapYTextBox.Text, "Tap Y percent"),
                    cancellationToken);
                return "Percent tap command sent.";
            });
        }

        private async void SwipeTest_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                await diagnosticService.SwipeByPercentAsync(
                    DeviceNameTextBox.Text,
                    ParseDouble(SwipeStartXTextBox.Text, "Swipe start X"),
                    ParseDouble(SwipeStartYTextBox.Text, "Swipe start Y"),
                    ParseDouble(SwipeEndXTextBox.Text, "Swipe end X"),
                    ParseDouble(SwipeEndYTextBox.Text, "Swipe end Y"),
                    ParseInteger(SwipeDurationTextBox.Text, "Swipe duration"),
                    cancellationToken);
                return "Percent swipe command sent.";
            });
        }

        private async void BackTest_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                await diagnosticService.BackAsync(DeviceNameTextBox.Text, cancellationToken);
                return "Back command sent.";
            });
        }

        private async Task RunOperationAsync(Func<CancellationToken, Task<string>> operation)
        {
            IsEnabled = false;
            StatusTextBlock.Text = "Running...";
            try
            {
                StatusTextBlock.Text = await operation(lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Operation canceled.";
            }
            catch (Exception exception)
            {
                StatusTextBlock.Text = $"Error: {exception.Message}";
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private static string FormatCheckResult(DeviceDiagnosticResult result)
        {
            string packageStatus = result.PackageMatches.HasValue
                ? result.PackageMatches.Value.ToString()
                : "unavailable";
            return $"Device: {result.DeviceName}{Environment.NewLine}"
                + $"Running: {result.IsRunning}{Environment.NewLine}"
                + $"Screenshot: {result.ScreenshotSucceeded}{Environment.NewLine}"
                + $"Resolution: {result.ScreenshotWidth}x{result.ScreenshotHeight}{Environment.NewLine}"
                + $"Expected resolution matches: {result.MatchesExpectedResolution}{Environment.NewLine}"
                + $"Expected package: {result.ExpectedPackage}{Environment.NewLine}"
                + $"Current foreground package: {result.CurrentForegroundPackage ?? "unavailable"}{Environment.NewLine}"
                + $"Package matches: {packageStatus}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}";
        }

        private static int ParseInteger(string value, string fieldName)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                throw new ArgumentException($"{fieldName} must be an integer.");
            return result;
        }

        private static double ParseDouble(string value, string fieldName)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                throw new ArgumentException($"{fieldName} must be a number using '.' as decimal separator.");
            return result;
        }
    }
}
