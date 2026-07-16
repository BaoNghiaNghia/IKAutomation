using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ADB_Tool_Automation_Post_FB.UI
{
    public partial class DeviceDiagnosticWindow : Window
    {
        private readonly IDeviceDiagnosticService diagnosticService;
        private readonly IGameStateDetector gameStateDetector;
        private readonly IWorldMapNavigationService navigationService;
        private readonly IResourceSearchConfigurationService resourceSearchConfigurationService;
        private readonly IResourceSearchExecutionService resourceSearchExecutionService;
        private readonly IResourcePopupVerificationService resourcePopupVerificationService;
        private readonly IOpenTeamSelectionService openTeamSelectionService;
        private readonly ISelectFarmTeamService selectFarmTeamService;
        private readonly TeamSelectionRequest defaultFarmTeamRequest;
        private readonly IDispatchSelectedTeamService dispatchSelectedTeamService;
        private readonly DispatchMarchRequest defaultDispatchRequest;
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();

        public DeviceDiagnosticWindow(
            IDeviceDiagnosticService diagnosticService,
            IGameStateDetector gameStateDetector,
            IWorldMapNavigationService navigationService,
            IResourceSearchConfigurationService resourceSearchConfigurationService,
            IResourceSearchExecutionService resourceSearchExecutionService,
            IResourcePopupVerificationService resourcePopupVerificationService,
            IOpenTeamSelectionService openTeamSelectionService,
            ISelectFarmTeamService selectFarmTeamService,
            TeamSelectionRequest defaultFarmTeamRequest,
            IDispatchSelectedTeamService dispatchSelectedTeamService,
            DispatchMarchRequest defaultDispatchRequest)
        {
            this.diagnosticService = diagnosticService
                ?? throw new ArgumentNullException(nameof(diagnosticService));
            this.gameStateDetector = gameStateDetector
                ?? throw new ArgumentNullException(nameof(gameStateDetector));
            this.navigationService = navigationService
                ?? throw new ArgumentNullException(nameof(navigationService));
            this.resourceSearchConfigurationService = resourceSearchConfigurationService
                ?? throw new ArgumentNullException(nameof(resourceSearchConfigurationService));
            this.resourceSearchExecutionService = resourceSearchExecutionService
                ?? throw new ArgumentNullException(nameof(resourceSearchExecutionService));
            this.resourcePopupVerificationService = resourcePopupVerificationService
                ?? throw new ArgumentNullException(nameof(resourcePopupVerificationService));
            this.openTeamSelectionService = openTeamSelectionService
                ?? throw new ArgumentNullException(nameof(openTeamSelectionService));
            this.selectFarmTeamService = selectFarmTeamService
                ?? throw new ArgumentNullException(nameof(selectFarmTeamService));
            this.defaultFarmTeamRequest = defaultFarmTeamRequest
                ?? throw new ArgumentNullException(nameof(defaultFarmTeamRequest));
            this.dispatchSelectedTeamService = dispatchSelectedTeamService
                ?? throw new ArgumentNullException(nameof(dispatchSelectedTeamService));
            this.defaultDispatchRequest = defaultDispatchRequest
                ?? throw new ArgumentNullException(nameof(defaultDispatchRequest));

            InitializeComponent();
            PackageNameTextBox.Text = string.IsNullOrWhiteSpace(diagnosticService.Configuration.PackageName)
                ? "(not configured)"
                : diagnosticService.Configuration.PackageName;
            Loaded += async (sender, args) => await RefreshDeviceListAsync();
            Closed += (sender, args) => lifetimeCancellation.Cancel();
        }

        private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDeviceListAsync();
        }

        private async Task RefreshDeviceListAsync()
        {
            string currentDevice = DeviceNameComboBox.Text?.Trim();
            await RunOperationAsync(async cancellationToken =>
            {
                IReadOnlyList<string> deviceNames = await diagnosticService.GetDeviceNamesAsync(cancellationToken);
                DeviceNameComboBox.ItemsSource = deviceNames;

                string selectedDevice = deviceNames.FirstOrDefault(name =>
                    string.Equals(name, currentDevice, StringComparison.OrdinalIgnoreCase))
                    ?? deviceNames.FirstOrDefault();
                DeviceNameComboBox.Text = selectedDevice ?? currentDevice ?? string.Empty;

                return deviceNames.Count == 0
                    ? "No LDPlayer instances were found. Check LDCONSOLE_PATH and create an instance in LDPlayer."
                    : $"Found {deviceNames.Count} LDPlayer instance(s). Selected: {DeviceNameComboBox.Text}.";
            });
        }

        private async void CheckDevice_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                DeviceDiagnosticResult result = await diagnosticService.CheckDeviceAsync(
                    GetSelectedDeviceName(),
                    cancellationToken);
                return FormatCheckResult(result);
            });
        }

        private async void LaunchGame_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                await diagnosticService.LaunchGameAsync(GetSelectedDeviceName(), cancellationToken);
                return "Game launch command sent. Use Capture Screenshot when the desired screen is visible.";
            });
        }

        private async void CaptureScreenshot_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                ScreenshotCaptureResult result = await diagnosticService.CaptureScreenshotAsync(
                    GetSelectedDeviceName(),
                    StateNameComboBox.Text,
                    NoteTextBox.Text,
                    cancellationToken);
                return $"Screenshot saved: {result.ScreenshotPath}{Environment.NewLine}"
                    + $"Metadata: {result.MetadataPath}{Environment.NewLine}"
                    + $"Resolution: {result.Width}x{result.Height}";
            });
        }

        private async void DetectCurrentState_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                GameDetectionResult result = await gameStateDetector.DetectAsync(
                    GetSelectedDeviceName(),
                    cancellationToken);
                return FormatDetectionResult(result);
            });
        }

        private async void EnsureWorldMap_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatNavigationResult(
                await navigationService.EnsureWorldMapAsync(GetSelectedDeviceName(), cancellationToken)));
        }

        private async void OpenSearchPanel_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatNavigationResult(
                await navigationService.OpenResourceSearchPanelAsync(GetSelectedDeviceName(), cancellationToken)));
        }

        private async void ConfigureSearch_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatConfigurationResult(
                await resourceSearchConfigurationService.ConfigureAsync(
                    GetSelectedDeviceName(),
                    new ResourceSearchConfigurationRequest
                    {
                        ResourceType = ResourceType.Iron,
                        TargetLevel = 7,
                        UnoccupiedOnly = true
                    },
                    cancellationToken)));
        }

        private async void ExecuteSearch_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatExecutionResult(
                await resourceSearchExecutionService.ExecuteAsync(
                    GetSelectedDeviceName(),
                    new ResourceSearchExecutionRequest
                    {
                        ConfigureBeforeSearch = true,
                        Configuration = new ResourceSearchConfigurationRequest
                        {
                            ResourceType = ResourceType.Iron,
                            TargetLevel = 7,
                            UnoccupiedOnly = true
                        }
                    },
                    cancellationToken)));
        }

        private async void VerifyResourcePopup_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatPopupResult(
                await resourcePopupVerificationService.VerifyAsync(
                    GetSelectedDeviceName(), cancellationToken)));
        }

        private async void OpenTeamSelection_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatOpenTeamSelectionResult(
                await openTeamSelectionService.OpenAsync(
                    GetSelectedDeviceName(), cancellationToken)));
        }

        private async void SelectFarmTeam_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatSelectFarmTeamResult(
                await selectFarmTeamService.SelectAsync(GetSelectedDeviceName(),
                    defaultFarmTeamRequest, cancellationToken)));
        }

        private async void DispatchSelectedTeam_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken => FormatDispatchMarchResult(
                await dispatchSelectedTeamService.DispatchAsync(GetSelectedDeviceName(),
                    defaultDispatchRequest, cancellationToken)));
        }


        private async void TapTest_Click(object sender, RoutedEventArgs e)
        {
            await RunOperationAsync(async cancellationToken =>
            {
                if (TapModeComboBox.SelectedIndex == 0)
                {
                    await diagnosticService.TapAsync(
                        GetSelectedDeviceName(),
                        ParseInteger(TapXTextBox.Text, "Tap X"),
                        ParseInteger(TapYTextBox.Text, "Tap Y"),
                        cancellationToken);
                    return "Pixel tap command sent.";
                }

                await diagnosticService.TapByPercentAsync(
                    GetSelectedDeviceName(),
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
                    GetSelectedDeviceName(),
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
                await diagnosticService.BackAsync(GetSelectedDeviceName(), cancellationToken);
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

        private string GetSelectedDeviceName()
        {
            string deviceName = DeviceNameComboBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                throw new InvalidOperationException(
                    "No LDPlayer instance is selected. Start LDPlayer, click Refresh, then select a device.");
            }

            return deviceName;
        }

        private static string FormatDetectionResult(GameDetectionResult result)
        {
            string evidence = string.Join(
                Environment.NewLine,
                System.Linq.Enumerable.Select(result.Evidence, item =>
                    $"- {item.TemplateId}: exists={item.TemplateExists}, found={item.Found}, "
                    + $"confidence={(item.Confidence.HasValue ? item.Confidence.Value.ToString("F3") : "n/a")}; "
                    + item.Message));
            return $"Detected state: {result.State}{Environment.NewLine}"
                + $"Successful: {result.IsSuccessful}{Environment.NewLine}"
                + $"Resolution: {result.ScreenshotWidth}x{result.ScreenshotHeight}{Environment.NewLine}"
                + $"Unknown screenshot: {result.ScreenshotPath ?? string.Empty}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}"
                + $"Evidence:{Environment.NewLine}{evidence}";
        }

        private static string FormatNavigationResult(NavigationResult result)
        {
            string evidence = string.Join(Environment.NewLine,
                System.Linq.Enumerable.Select(result.FinalEvidence, item =>
                    $"- {item.TemplateId}: found={item.Found}; {item.Message}"));
            return $"Success: {result.Success}{Environment.NewLine}Initial: {result.InitialState}{Environment.NewLine}"
                + $"Final: {result.FinalState}{Environment.NewLine}Attempts: {result.Attempts}{Environment.NewLine}"
                + $"Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}Message: {result.Message}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Evidence:{Environment.NewLine}{evidence}";
        }

        private static string FormatConfigurationResult(ResourceSearchConfigurationResult result)
        {
            string steps = string.Join(Environment.NewLine, result.Steps.Select(step =>
                $"- {step.StepName}: success={step.Success}, attempts={step.Attempts}; "
                + $"{step.Message}; error={step.ErrorMessage ?? string.Empty}"));
            return $"Success: {result.Success}{Environment.NewLine}Initial: {result.InitialState}{Environment.NewLine}"
                + $"Final: {result.FinalState}{Environment.NewLine}Resource verified: {result.ResourceVerified}{Environment.NewLine}"
                + $"Level verified: {result.LevelVerified}{Environment.NewLine}Filter verified: {result.FilterVerified}{Environment.NewLine}"
                + $"Tap count: {result.TapCount}{Environment.NewLine}Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Message: {result.Message}{Environment.NewLine}Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}"
                + $"Steps:{Environment.NewLine}{steps}";
        }

        private static string FormatExecutionResult(ResourceSearchExecutionResult result)
        {
            string observations = string.Join(Environment.NewLine, result.Observations.Select((item, index) =>
                $"- #{index + 1} {item.State}: toast={item.ToastAnchorFound}/{item.ToastActionAnchorFound}, "
                + $"panel={item.SearchPanelConfirmed}, diff={item.FrameDifference?.ToString("F4") ?? "n/a"}, "
                + $"stable={item.IsStable}; {item.Message}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Search taps: {result.SearchTapCount}{Environment.NewLine}Panel closed: {result.PanelClosed}{Environment.NewLine}"
                + $"Camera movement: {result.CameraMovementObserved}{Environment.NewLine}Camera stable: {result.CameraStabilityVerified}{Environment.NewLine}"
                + $"Not found observed: {result.NotFoundObserved}{Environment.NewLine}Toast verified: {result.NotFoundToastVerified}{Environment.NewLine}"
                + $"Observed frames: {result.ObservedFrameCount}{Environment.NewLine}Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}Message: {result.Message}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Observations:{Environment.NewLine}{observations}";
        }

        private static string FormatPopupResult(ResourcePopupVerificationResult result)
        {
            string gatherBounds = result.GatherButtonMatch != null && result.GatherButtonMatch.Found
                ? $"({result.GatherButtonMatch.X},{result.GatherButtonMatch.Y},"
                    + $"{result.GatherButtonMatch.Width},{result.GatherButtonMatch.Height})"
                : string.Empty;
            string evidence = string.Join(Environment.NewLine, result.Evidence.Select(item =>
                $"- {item.TemplateId}: found={item.Found}, bounds="
                + (item.MatchResult != null && item.MatchResult.Found
                    ? $"({item.MatchResult.X},{item.MatchResult.Y},{item.MatchResult.Width},{item.MatchResult.Height})"
                    : string.Empty)
                + $"; {item.Message}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Popup anchor verified: {result.PopupAnchorVerified}{Environment.NewLine}"
                + $"Iron resource verified: {result.IronResourceVerified}{Environment.NewLine}"
                + $"Gather button verified: {result.GatherButtonVerified}{Environment.NewLine}"
                + $"Gather bounds: {gatherBounds}{Environment.NewLine}Observed frames: {result.ObservedFrameCount}{Environment.NewLine}"
                + $"Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}"
                + $"Message: {result.Message}{Environment.NewLine}Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}"
                + $"Evidence:{Environment.NewLine}{evidence}";
        }

        private static string FormatOpenTeamSelectionResult(OpenTeamSelectionResult result)
        {
            string gatherBounds = result.GatherButtonMatch != null && result.GatherButtonMatch.Found
                ? $"({result.GatherButtonMatch.X},{result.GatherButtonMatch.Y},"
                    + $"{result.GatherButtonMatch.Width},{result.GatherButtonMatch.Height})"
                : string.Empty;
            string observations = string.Join(Environment.NewLine, result.Observations.Select((item, index) =>
                $"- #{index + 1} {item.State}: panel={item.PanelAnchorFound}, adjust={item.AdjustFormationButtonFound}, "
                + $"action={item.TeamActionButtonFound}, confirmed={item.TeamSelectionConfirmed}, ready={item.TeamSelectionReady}; {item.Message}"));
            string evidence = string.Join(Environment.NewLine, result.FinalEvidence.Select(item =>
                $"- {item.TemplateId}: exists={item.TemplateExists}, found={item.Found}, bounds="
                + (item.MatchResult != null && item.MatchResult.Found
                    ? $"({item.MatchResult.X},{item.MatchResult.Y},{item.MatchResult.Width},{item.MatchResult.Height})"
                    : string.Empty)
                + $", confidence={(item.Confidence.HasValue ? item.Confidence.Value.ToString("F3") : "n/a")}; {item.Message}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Resource popup verified: {result.ResourcePopupVerified}{Environment.NewLine}"
                + $"Gather button verified: {result.GatherButtonVerified}{Environment.NewLine}"
                + $"Team selection verified: {result.TeamSelectionVerified}{Environment.NewLine}Team selection ready: {result.TeamSelectionReady}{Environment.NewLine}"
                + $"Panel anchor: {result.PanelAnchorVerified}{Environment.NewLine}Adjust formation: {result.AdjustFormationButtonVerified}{Environment.NewLine}"
                + $"Team action: {result.TeamActionButtonVerified}{Environment.NewLine}Gather taps: {result.GatherTapCount}{Environment.NewLine}"
                + $"Observed frames: {result.ObservedFrameCount}{Environment.NewLine}Transient unknown frames: {result.TransientUnknownFrameCount}{Environment.NewLine}"
                + $"Gather bounds: {gatherBounds}{Environment.NewLine}Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}Message: {result.Message}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Observations:{Environment.NewLine}{observations}{Environment.NewLine}"
                + $"Final evidence:{Environment.NewLine}{evidence}";
        }

        private static string FormatSelectFarmTeamResult(SelectFarmTeamResult result)
        {
            string attempted = string.Join(", ", result.AttemptedTeams.Select(team => ((int)team).ToString()));
            string attempts = string.Join(Environment.NewLine, result.Attempts.Select((item, index) =>
                $"- #{index + 1} {item.TeamNumber}: badge={item.BadgeFound}, disabled={item.DisabledDetected}, "
                + $"already={item.AlreadySelected}, tap={item.TapSent}, selected={item.SelectedVerified}, "
                + $"badgeBounds={Bounds(item.BadgeMatch)}, selectedBounds={Bounds(item.SelectedBorderMatch)}; {item.Message}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Selected team: {result.SelectedTeam?.ToString() ?? string.Empty}{Environment.NewLine}Attempted teams: {attempted}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Team taps: {result.TeamTapCount}{Environment.NewLine}Team Selection verified: {result.TeamSelectionScreenVerified}{Environment.NewLine}"
                + $"Selected state verified: {result.SelectedStateVerified}{Environment.NewLine}Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}Message: {result.Message}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Attempts:{Environment.NewLine}{attempts}";
        }

        private static string Bounds(ADB_Tool_Automation_Post_FB.Core.Vision.ImageMatchResult match) =>
            match != null && match.Found
                ? $"({match.X},{match.Y},{match.Width},{match.Height})" : string.Empty;

        private static string FormatDispatchMarchResult(DispatchMarchResult result)
        {
            string observations = string.Join(Environment.NewLine, result.Observations.Select((item, index) =>
                $"- #{index + 1} {item.State}: panel={item.TeamSelectionFound}, world={item.WorldMapFound}, "
                + $"badge={item.ExpectedTeamBadgeFound}, selected={item.SelectedBorderFound}, busy={item.BusyStatusFound}, "
                + $"timer={item.MarchTimerFound}, diff={item.TeamRegionDifference?.ToString("F4") ?? "n/a"}, "
                + $"changed={item.TeamRegionChanged}, success={item.SuccessRuleMatched}; {item.Message}"));
            return $"Outcome: {result.Outcome}{Environment.NewLine}Success: {result.Success}{Environment.NewLine}"
                + $"Expected team: {result.ExpectedTeam}{Environment.NewLine}Dispatched team: {result.DispatchedTeam?.ToString() ?? string.Empty}{Environment.NewLine}"
                + $"Initial: {result.InitialState}{Environment.NewLine}Final: {result.FinalState}{Environment.NewLine}"
                + $"Team Selection verified: {result.TeamSelectionVerified}{Environment.NewLine}Expected team selected: {result.ExpectedTeamSelectedBeforeTap}{Environment.NewLine}"
                + $"Action button verified: {result.ActionButtonVerified}{Environment.NewLine}Team Selection closed: {result.TeamSelectionClosed}{Environment.NewLine}"
                + $"World Map verified: {result.WorldMapVerified}{Environment.NewLine}Selected border disappeared: {result.SelectedBorderDisappeared}{Environment.NewLine}"
                + $"Team region changed: {result.TeamRegionChanged}{Environment.NewLine}Team region difference: {result.TeamRegionDifference?.ToString("F4") ?? "n/a"}{Environment.NewLine}"
                + $"Busy status: {result.BusyStatusVerified}{Environment.NewLine}March timer: {result.MarchTimerVerified}{Environment.NewLine}"
                + $"March started: {result.MarchStartedVerified}{Environment.NewLine}Action taps: {result.ActionTapCount}{Environment.NewLine}"
                + $"Observed frames: {result.ObservedFrameCount}{Environment.NewLine}Consecutive success: {result.ConsecutiveSuccessFrames}{Environment.NewLine}"
                + $"Transient Unknown: {result.TransientUnknownFrameCount}{Environment.NewLine}Duration: {result.Duration.TotalMilliseconds:F0} ms{Environment.NewLine}"
                + $"Diagnostic: {result.DiagnosticScreenshotPath ?? string.Empty}{Environment.NewLine}Message: {result.Message}{Environment.NewLine}"
                + $"Error: {result.ErrorMessage ?? string.Empty}{Environment.NewLine}Observations:{Environment.NewLine}{observations}";
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
