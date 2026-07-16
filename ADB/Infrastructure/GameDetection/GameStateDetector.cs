using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
            TemplateId.ResourceSearchPanelAnchor,
            TemplateId.SearchButtonEnabled,
            TemplateId.ContinentMapTitle,
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
                evidence.Add(MatchTemplate(screenshotPng, templateId, configurationErrors));
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
            GameDetectionEvidence searchButton = FindEvidence(evidence, TemplateId.SearchButtonEnabled);
            GameDetectionEvidence worldAnchor = FindEvidence(evidence, TemplateId.WorldMapAnchor);
            GameDetectionEvidence continentTitle = FindEvidence(evidence, TemplateId.ContinentMapTitle);
            // The selected resource category changes the colors and icons in the tab stack,
            // so the full panel anchor is supporting evidence only. The enabled Search button
            // is the stable, panel-specific signal across resource and enemy search categories.
            bool panelConfirmed = searchButton.Found;
            GameState state = panelConfirmed
                ? GameState.ResourceSearchPanel
                : continentTitle.Found
                    ? GameState.ContinentMap
                    : worldAnchor.Found ? GameState.WorldMap : GameState.Unknown;

            panelAnchor.Message += panelConfirmed
                ? panelAnchor.Found
                    ? " Supporting panel-layout evidence matched."
                    : " Panel layout varies with the selected search category; this evidence is optional."
                : " Rule ResourceSearchPanel was not evaluated as matched without SearchButtonEnabled.";
            searchButton.Message += panelConfirmed
                ? " Rule ResourceSearchPanel satisfied by the stable enabled Search button."
                : " Rule ResourceSearchPanel requires SearchButtonEnabled.";
            worldAnchor.Message += state == GameState.WorldMap
                ? " Rule WorldMap selected because ResourceSearchPanel was not confirmed."
                : panelConfirmed
                    ? " ResourceSearchPanel has priority over WorldMap."
                    : continentTitle.Found
                        ? " ContinentMap has priority over WorldMap."
                        : " Rule WorldMap not satisfied.";
            continentTitle.Message += state == GameState.ContinentMap
                ? " Rule ContinentMap selected because ResourceSearchPanel was not confirmed."
                : panelConfirmed
                    ? " ResourceSearchPanel has priority over ContinentMap."
                    : " Rule ContinentMap not satisfied.";

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
                ImageMatchResult match = imageMatcher.Find(screenshotPng, template, searchRegion: null);
                return new GameDetectionEvidence
                {
                    TemplateId = templateId,
                    TemplateExists = true,
                    Found = match != null && match.Found,
                    MatchResult = match,
                    Confidence = match?.Confidence,
                    Message = match != null && match.Found
                        ? $"Template '{templateId}' matched."
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
