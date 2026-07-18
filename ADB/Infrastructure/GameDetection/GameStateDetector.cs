using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection
{
    public sealed class GameStateDetector : IGameStateDetector
    {
        private static readonly TemplateId[] DetectionTemplates =
        {
            TemplateId.ResourceExpiryDialogAnchor,
            TemplateId.StorageLimitDialogAnchor,
            TemplateId.StorageLimitCancelButton,
            TemplateId.TeamSelectionPanelAnchor,
            TemplateId.TeamAdjustFormationButton,
            TemplateId.TeamActionButtonEnabled,
            TemplateId.ResourceSearchPanelAnchor,
            TemplateId.SearchButtonEnabled,
            TemplateId.LevelMinusButton,
            TemplateId.ResourceTabSelected,
            TemplateId.ResourceTabUnselected,
            TemplateId.ResourcePopupInfoAnchor,
            TemplateId.ResourcePopupIronTitle,
            TemplateId.GatherButtonEnabled,
            TemplateId.ContinentMapTitle,
            TemplateId.CityToWorldMapButton,
            TemplateId.WorldMapAnchor
        };

        private readonly ILdPlayerClient ldPlayerClient;
        private readonly ITemplateRegistry templateRegistry;
        private readonly IImageMatcher imageMatcher;
        private readonly GameDetectionOptions options;
        private readonly IUnknownScreenshotStore unknownScreenshotStore;
        private readonly IDiagnosticLogger logger;

        public GameStateDetector(
            ILdPlayerClient ldPlayerClient,
            ITemplateRegistry templateRegistry,
            IImageMatcher imageMatcher,
            GameDetectionOptions options,
            IUnknownScreenshotStore unknownScreenshotStore,
            IDiagnosticLogger logger)
        {
            this.ldPlayerClient = ldPlayerClient ?? throw new ArgumentNullException(nameof(ldPlayerClient));
            this.templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
            this.imageMatcher = imageMatcher ?? throw new ArgumentNullException(nameof(imageMatcher));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.unknownScreenshotStore = unknownScreenshotStore
                ?? throw new ArgumentNullException(nameof(unknownScreenshotStore));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<GameDetectionResult> DetectAsync(
            string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeviceName(deviceName);
            var stopwatch = Stopwatch.StartNew();
            byte[] screenshot;

            try
            {
                screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                GameDetectionResult failure = Failure(
                    $"Failed to capture screenshot for LDPlayer device '{deviceName}': {exception.Message}");
                LogResult(deviceName, failure, stopwatch.Elapsed, exception);
                return failure;
            }

            GameDetectionResult result = DetectCore(screenshot, deviceName);
            if (result.IsSuccessful
                && result.State == GameState.Unknown
                && options.SaveUnknownScreenshots)
            {
                try
                {
                    result.ScreenshotPath = await unknownScreenshotStore.SaveAsync(
                        deviceName,
                        screenshot,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.Error(
                        $"[Game State Detection] DeviceName='{deviceName}', "
                        + $"Failed to save Unknown screenshot: {exception.Message}",
                        exception);
                }
            }

            LogResult(deviceName, result, stopwatch.Elapsed, null);
            return result;
        }

        public GameDetectionResult Detect(byte[] screenshotPng)
        {
            return DetectCore(screenshotPng, "offline-screenshot");
        }

        private GameDetectionResult DetectCore(byte[] screenshotPng, string deviceName)
        {
            DateTimeOffset detectedAt = DateTimeOffset.Now;
            if (screenshotPng == null || screenshotPng.Length == 0)
                return Failure($"Screenshot PNG for '{deviceName}' is null or empty.", detectedAt);

            int width;
            int height;
            try
            {
                ReadDimensions(screenshotPng, out width, out height);
            }
            catch (Exception exception)
            {
                return Failure(
                    $"Screenshot PNG for '{deviceName}' could not be decoded: {exception.Message}",
                    detectedAt);
            }

            if (options.RequireExpectedResolution
                && (width != options.ExpectedWidth || height != options.ExpectedHeight))
            {
                return new GameDetectionResult
                {
                    State = GameState.Unknown,
                    Evidence = new GameDetectionEvidence[0],
                    DetectedAt = detectedAt,
                    ScreenshotWidth = width,
                    ScreenshotHeight = height,
                    IsSuccessful = false,
                    ErrorMessage = $"LDPlayer device '{deviceName}' screenshot resolution mismatch. "
                        + $"Expected {options.ExpectedWidth}x{options.ExpectedHeight}, actual {width}x{height}."
                };
            }

            var evidence = new List<GameDetectionEvidence>(DetectionTemplates.Length);
            var configurationErrors = new List<string>();
            foreach (TemplateId templateId in DetectionTemplates)
            {
                evidence.Add(MatchTemplate(
                    screenshotPng, templateId, width, height, configurationErrors));
            }

            if (configurationErrors.Count > 0)
            {
                return new GameDetectionResult
                {
                    State = GameState.Unknown,
                    Evidence = evidence.AsReadOnly(),
                    DetectedAt = detectedAt,
                    ScreenshotWidth = width,
                    ScreenshotHeight = height,
                    IsSuccessful = false,
                    ErrorMessage = string.Join(" ", configurationErrors)
                };
            }

            GameDetectionEvidence panelAnchor = FindEvidence(evidence, TemplateId.ResourceSearchPanelAnchor);
            GameDetectionEvidence resourceExpiryDialog = FindEvidence(evidence, TemplateId.ResourceExpiryDialogAnchor);
            GameDetectionEvidence storageDialog = FindEvidence(evidence, TemplateId.StorageLimitDialogAnchor);
            GameDetectionEvidence storageCancel = FindEvidence(evidence, TemplateId.StorageLimitCancelButton);
            GameDetectionEvidence teamPanel = FindEvidence(evidence, TemplateId.TeamSelectionPanelAnchor);
            GameDetectionEvidence teamAdjust = FindEvidence(evidence, TemplateId.TeamAdjustFormationButton);
            GameDetectionEvidence teamAction = FindEvidence(evidence, TemplateId.TeamActionButtonEnabled);
            GameDetectionEvidence searchButton = FindEvidence(evidence, TemplateId.SearchButtonEnabled);
            GameDetectionEvidence levelMinusButton = FindEvidence(evidence, TemplateId.LevelMinusButton);
            GameDetectionEvidence resourceTabSelected = FindEvidence(evidence, TemplateId.ResourceTabSelected);
            GameDetectionEvidence resourceTabUnselected = FindEvidence(evidence, TemplateId.ResourceTabUnselected);
            GameDetectionEvidence popupAnchor = FindEvidence(evidence, TemplateId.ResourcePopupInfoAnchor);
            GameDetectionEvidence popupIron = FindEvidence(evidence, TemplateId.ResourcePopupIronTitle);
            GameDetectionEvidence gatherButton = FindEvidence(evidence, TemplateId.GatherButtonEnabled);
            GameDetectionEvidence worldAnchor = FindEvidence(evidence, TemplateId.WorldMapAnchor);
            GameDetectionEvidence continentTitle = FindEvidence(evidence, TemplateId.ContinentMapTitle);
            GameDetectionEvidence cityMapButton = FindEvidence(evidence, TemplateId.CityToWorldMapButton);
            bool panelChromeFound = panelAnchor.Found || levelMinusButton.Found
                || resourceTabSelected.Found || resourceTabUnselected.Found;
            bool panelConfirmed = panelChromeFound && searchButton.Found;
            int popupSignals = (popupAnchor.Found ? 1 : 0)
                + (popupIron.Found ? 1 : 0) + (gatherButton.Found ? 1 : 0);
            bool popupConfirmed = popupSignals >= 2 && (popupAnchor.Found || popupIron.Found);
            bool teamSelectionConfirmed = teamPanel.Found && (teamAdjust.Found || teamAction.Found);
            bool storageLimitConfirmed = storageDialog.Found && storageCancel.Found;
            bool resourceExpiryConfirmed = resourceExpiryDialog.Found && storageCancel.Found;
            GameState state = resourceExpiryConfirmed
                ? GameState.ResourceExpiryDialog
                : storageLimitConfirmed
                ? GameState.StorageLimitDialog
                : teamSelectionConfirmed
                ? GameState.TeamSelection
                : panelConfirmed ? GameState.ResourceSearchPanel
                : popupConfirmed ? GameState.ResourcePopup
                    : continentTitle.Found ? GameState.ContinentMap
                        : worldAnchor.Found ? GameState.WorldMap
                            : cityMapButton.Found ? GameState.City : GameState.Unknown;

            teamPanel.Message += teamSelectionConfirmed
                ? resourceExpiryConfirmed
                    ? " ResourceExpiryDialog has priority over TeamSelection."
                    : storageLimitConfirmed
                    ? " StorageLimitDialog has priority over TeamSelection."
                    : " Rule TeamSelection satisfied with a team action control."
                : " Rule TeamSelection requires the panel anchor and an Adjust Formation or Team Action button.";
            storageDialog.Message += storageLimitConfirmed
                ? " Rule StorageLimitDialog satisfied with a fresh Cancel button signal."
                : " Rule StorageLimitDialog requires dialog and Cancel button anchors.";
            storageCancel.Message += storageLimitConfirmed
                ? " StorageLimitDialog has highest state priority."
                : resourceExpiryConfirmed
                    ? " ResourceExpiryDialog has highest state priority."
                    : " Cancel button alone does not identify a resource-switch dialog.";
            resourceExpiryDialog.Message += resourceExpiryConfirmed
                ? " Rule ResourceExpiryDialog satisfied with a fresh Cancel button signal."
                : " Rule ResourceExpiryDialog requires its stable text anchor and Cancel button.";
            teamAdjust.Message += teamSelectionConfirmed && teamAdjust.Found
                ? " Adjust Formation contributed TeamSelection evidence."
                : " Adjust Formation alone does not confirm TeamSelection.";
            teamAction.Message += teamSelectionConfirmed && teamAction.Found
                ? " Team Action contributed TeamSelection evidence."
                : " Team Action alone does not confirm TeamSelection.";
            panelAnchor.Message += panelConfirmed
                ? " Rule ResourceSearchPanel satisfied with SearchButtonEnabled."
                : " Rule ResourceSearchPanel requires SearchButtonEnabled and a stable panel control.";
            searchButton.Message += panelConfirmed
                ? " Rule ResourceSearchPanel satisfied with a stable panel anchor."
                : " Rule ResourceSearchPanel requires SearchButtonEnabled and a stable panel control.";
            levelMinusButton.Message += panelConfirmed
                ? " Rule ResourceSearchPanel accepted LevelMinusButton as a stable fallback anchor."
                : " LevelMinusButton can provide stable panel evidence when the slider-inclusive anchor changes.";
            resourceTabSelected.Message += panelConfirmed && resourceTabSelected.Found
                ? " Selected resource tab provided stable ResourceSearchPanel evidence."
                : " Selected resource tab was checked as panel evidence.";
            resourceTabUnselected.Message += panelConfirmed && resourceTabUnselected.Found
                ? " Unselected resource tab provided stable ResourceSearchPanel evidence."
                : " Unselected resource tab was checked as panel evidence.";
            popupAnchor.Message += teamSelectionConfirmed
                ? " TeamSelection has priority over ResourcePopup."
                : popupConfirmed
                ? " ResourcePopup selected from at least two popup signals."
                : " ResourcePopup requires at least two signals and cannot be confirmed by GatherButtonEnabled alone.";
            popupIron.Message += popupConfirmed
                ? " ResourcePopup has priority over WorldMap."
                : " Iron popup title was not sufficient to confirm the popup.";
            gatherButton.Message += popupConfirmed
                ? " Gather button contributed popup evidence."
                : gatherButton.Found
                    ? " Gather button alone is ambiguous and does not confirm ResourcePopup."
                    : " Gather button was not found.";
            worldAnchor.Message += state == GameState.WorldMap
                ? " Rule WorldMap selected because ResourceSearchPanel was not confirmed."
                : teamSelectionConfirmed
                    ? " TeamSelection has priority over WorldMap."
                : panelConfirmed
                    ? " ResourceSearchPanel has priority over WorldMap."
                    : popupConfirmed
                        ? " ResourcePopup has priority over WorldMap."
                    : continentTitle.Found
                        ? " ContinentMap has priority over WorldMap."
                        : " Rule WorldMap not satisfied.";
            continentTitle.Message += state == GameState.ContinentMap
                ? " Rule ContinentMap selected because ResourceSearchPanel was not confirmed."
                : panelConfirmed
                    ? " ResourceSearchPanel has priority over ContinentMap."
                    : popupConfirmed
                        ? " ResourcePopup has priority over ContinentMap."
                    : " Rule ContinentMap not satisfied.";
            cityMapButton.Message += state == GameState.City
                ? " Rule City selected from the lower-left World Map navigation button."
                : worldAnchor.Found
                    ? " WorldMap has priority over City."
                    : " Rule City not satisfied.";

            return new GameDetectionResult
            {
                State = state,
                Evidence = evidence.AsReadOnly(),
                DetectedAt = detectedAt,
                ScreenshotWidth = width,
                ScreenshotHeight = height,
                IsSuccessful = true
            };
        }

        private GameDetectionEvidence MatchTemplate(
            byte[] screenshotPng,
            TemplateId templateId,
            int screenshotWidth,
            int screenshotHeight,
            ICollection<string> configurationErrors)
        {
            string path;
            try
            {
                path = templateRegistry.GetPath(templateId);
                if (!templateRegistry.Exists(templateId))
                {
                    string message = $"Required template '{templateId}' was not found at '{path}'.";
                    configurationErrors.Add(message);
                    return ErrorEvidence(templateId, false, message);
                }
            }
            catch (Exception exception)
            {
                string message = $"Template '{templateId}' configuration could not be resolved: {exception.Message}";
                configurationErrors.Add(message);
                return ErrorEvidence(templateId, false, message);
            }

            try
            {
                byte[] template = templateRegistry.LoadBytes(templateId);
                ImageRegion? searchRegion = IsStorageLimitTemplate(templateId)
                    ? options.StorageLimitDialogRegion
                    : IsTeamSelectionTemplate(templateId)
                    ? options.TeamSelectionRegion
                    : IsPopupActionTemplate(templateId)
                    ? options.ResourcePopupActionRegion
                    : IsPopupTemplate(templateId)
                    ? options.ResourcePopupRegion
                    : templateId == TemplateId.CityToWorldMapButton
                    ? new ImageRegion(0, screenshotHeight / 2,
                        screenshotWidth / 2, screenshotHeight - screenshotHeight / 2)
                    : IsSearchPanelTemplate(templateId)
                        ? new ImageRegion(0, screenshotHeight / 2,
                            screenshotWidth, screenshotHeight - screenshotHeight / 2)
                        : (ImageRegion?)null;
                ImageMatchResult match = imageMatcher.Find(screenshotPng, template, searchRegion);
                bool usedStableWorldMapAnchor = false;
                if (templateId == TemplateId.WorldMapAnchor && (match == null || !match.Found))
                {
                    byte[] stableTemplate = TryCreateStableWorldMapTemplate(template) ?? template;
                    var lowerLeftRegion = new ImageRegion(
                        0, screenshotHeight / 2,
                        screenshotWidth / 2, screenshotHeight - screenshotHeight / 2);
                    match = imageMatcher.Find(screenshotPng, stableTemplate, lowerLeftRegion);
                    usedStableWorldMapAnchor = match != null && match.Found;
                }
                return new GameDetectionEvidence
                {
                    TemplateId = templateId,
                    TemplateExists = true,
                    Found = match != null && match.Found,
                    MatchResult = match,
                    Confidence = match?.Confidence,
                    SearchRegion = searchRegion,
                    Message = match != null && match.Found
                        ? usedStableWorldMapAnchor
                            ? "Template 'WorldMapAnchor' matched by its stable icon center in the lower-left region."
                            : searchRegion.HasValue
                                ? $"Template '{templateId}' matched inside its configured ROI."
                                : $"Template '{templateId}' matched."
                        : $"Template '{templateId}' was checked and did not match."
                };
            }
            catch (Exception exception)
            {
                string message = $"Existing template '{templateId}' could not be loaded or matched: {exception.Message}";
                configurationErrors.Add(message);
                return ErrorEvidence(templateId, true, message);
            }
        }

        private static bool IsPopupTemplate(TemplateId id) =>
            id == TemplateId.ResourcePopupInfoAnchor
            || id == TemplateId.ResourcePopupIronTitle
            || id == TemplateId.GatherButtonEnabled;

        private static bool IsPopupActionTemplate(TemplateId id) =>
            id == TemplateId.GatherButtonEnabled;

        private static bool IsStorageLimitTemplate(TemplateId id) =>
            id == TemplateId.StorageLimitDialogAnchor
            || id == TemplateId.ResourceExpiryDialogAnchor
            || id == TemplateId.StorageLimitCancelButton;

        private static bool IsTeamSelectionTemplate(TemplateId id) =>
            id == TemplateId.TeamSelectionPanelAnchor
            || id == TemplateId.TeamAdjustFormationButton
            || id == TemplateId.TeamActionButtonEnabled;

        private static bool IsSearchPanelTemplate(TemplateId id) =>
            id == TemplateId.ResourceSearchPanelAnchor
            || id == TemplateId.SearchButtonEnabled
            || id == TemplateId.LevelMinusButton
            || id == TemplateId.ResourceTabSelected
            || id == TemplateId.ResourceTabUnselected;

        private static byte[] TryCreateStableWorldMapTemplate(byte[] templateBytes)
        {
            try
            {
                using (var input = new MemoryStream(templateBytes, writable: false))
                using (var source = new Bitmap(input))
                {
                    int marginX = source.Width / 5;
                    int marginY = source.Height / 5;
                    int width = source.Width - marginX * 2;
                    int height = source.Height - marginY * 2;
                    if (width <= 0 || height <= 0) return null;

                    using (Bitmap stable = source.Clone(
                        new Rectangle(marginX, marginY, width, height), PixelFormat.Format32bppArgb))
                    using (var output = new MemoryStream())
                    {
                        stable.Save(output, ImageFormat.Png);
                        return output.ToArray();
                    }
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static GameDetectionEvidence ErrorEvidence(
            TemplateId templateId,
            bool templateExists,
            string message)
        {
            return new GameDetectionEvidence
            {
                TemplateId = templateId,
                TemplateExists = templateExists,
                Found = false,
                Confidence = null,
                Message = message
            };
        }

        private static GameDetectionEvidence FindEvidence(
            IEnumerable<GameDetectionEvidence> evidence,
            TemplateId templateId)
        {
            return evidence.First(item => item.TemplateId == templateId);
        }

        private static void ReadDimensions(byte[] screenshotPng, out int width, out int height)
        {
            using (var stream = new MemoryStream(screenshotPng, writable: false))
            using (Image image = Image.FromStream(stream, false, true))
            {
                width = image.Width;
                height = image.Height;
            }
        }

        private static GameDetectionResult Failure(string message, DateTimeOffset? detectedAt = null)
        {
            return new GameDetectionResult
            {
                State = GameState.Unknown,
                Evidence = new GameDetectionEvidence[0],
                DetectedAt = detectedAt ?? DateTimeOffset.Now,
                IsSuccessful = false,
                ErrorMessage = message
            };
        }

        private void LogResult(
            string deviceName,
            GameDetectionResult result,
            TimeSpan duration,
            Exception exception)
        {
            string evidenceSummary = string.Join(
                "; ",
                result.Evidence.Select(item =>
                    $"{item.TemplateId}:Exists={item.TemplateExists},Found={item.Found},Confidence="
                    + (item.Confidence.HasValue ? item.Confidence.Value.ToString("F3") : "n/a")));
            string message = $"[Game State Detection] DeviceName='{deviceName}', "
                + $"DetectedState='{result.State}', DurationMs={duration.TotalMilliseconds:F0}, "
                + $"Resolution={result.ScreenshotWidth}x{result.ScreenshotHeight}, "
                + $"Evidence='{evidenceSummary}', UnknownScreenshotPath='{result.ScreenshotPath ?? string.Empty}', "
                + $"Error='{result.ErrorMessage ?? string.Empty}'";

            if (exception != null || !result.IsSuccessful)
                logger.Error(message, exception ?? new InvalidOperationException(result.ErrorMessage));
            else
                logger.Info(message);
        }

        private static void ValidateDeviceName(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("LDPlayer device name is required.", nameof(deviceName));
        }
    }
}
