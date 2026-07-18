using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
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

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch
{
    public sealed class ResourceSearchConfigurationService : IResourceSearchConfigurationService
    {
        private static readonly TemplateId[] RequiredTemplates =
        {
            TemplateId.ResourceSearchPanelAnchor, TemplateId.SearchButtonEnabled,
            TemplateId.ResourceTabSelected, TemplateId.ResourceTabUnselected,
            TemplateId.LevelMinusButton, TemplateId.LevelPlusButton,
            TemplateId.UnoccupiedFilterChecked, TemplateId.UnoccupiedFilterUnchecked
        };

        private readonly IWorldMapNavigationService navigationService;
        private readonly IGameStateDetector detector;
        private readonly ILdPlayerClient ldPlayerClient;
        private readonly ITemplateRegistry templateRegistry;
        private readonly IImageMatcher imageMatcher;
        private readonly ResourceSearchConfigurationOptions options;
        private readonly IDeviceOperationLock operationLock;
        private readonly IDiagnosticLogger logger;
        private readonly IResourceTemplateProfileProvider profileProvider;

        public ResourceSearchConfigurationService(IWorldMapNavigationService navigationService,
            IGameStateDetector detector, ILdPlayerClient ldPlayerClient,
            ITemplateRegistry templateRegistry, IImageMatcher imageMatcher,
            ResourceSearchConfigurationOptions options, IDeviceOperationLock operationLock,
            IDiagnosticLogger logger)
            : this(navigationService, detector, ldPlayerClient, templateRegistry, imageMatcher,
                options, operationLock, logger, new ResourceTemplateProfileProvider(templateRegistry))
        {
        }

        public ResourceSearchConfigurationService(IWorldMapNavigationService navigationService,
            IGameStateDetector detector, ILdPlayerClient ldPlayerClient,
            ITemplateRegistry templateRegistry, IImageMatcher imageMatcher,
            ResourceSearchConfigurationOptions options, IDeviceOperationLock operationLock,
            IDiagnosticLogger logger, IResourceTemplateProfileProvider profileProvider)
        {
            this.navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.ldPlayerClient = ldPlayerClient ?? throw new ArgumentNullException(nameof(ldPlayerClient));
            this.templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
            this.imageMatcher = imageMatcher ?? throw new ArgumentNullException(nameof(imageMatcher));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.profileProvider = profileProvider ?? throw new ArgumentNullException(nameof(profileProvider));
        }

        public Task<ResourceSearchConfigurationResult> ConfigureAsync(string deviceName,
            ResourceSearchConfigurationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("LDPlayer device name is required.", nameof(deviceName));

            string validationError = ValidateRequest(request);
            if (validationError != null)
                return Task.FromResult(InvalidResult(request, validationError));

            string templateError = profileProvider.IsSupported(request.ResourceType)
                ? ValidateTemplates(request.ResourceType, request.TargetLevel)
                : profileProvider.GetUnsupportedReason(request.ResourceType);
            if (templateError != null)
                return Task.FromResult(InvalidResult(request, templateError));

            return operationLock.RunAsync(deviceName,
                token => ConfigureCoreAsync(deviceName.Trim(), request, token), cancellationToken);
        }

        private async Task<ResourceSearchConfigurationResult> ConfigureCoreAsync(string deviceName,
            ResourceSearchConfigurationRequest request, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var steps = new List<ConfigurationStepResult>();
            var result = NewResult(request, steps);
            try
            {
                LogStart(deviceName, request);
                NavigationResult navigation = await navigationService.OpenResourceSearchPanelAsync(deviceName, cancellationToken);
                result.InitialState = navigation.InitialState;
                if (!navigation.Success)
                {
                    AddStep(steps, "EnsurePanel", false, navigation.Attempts, null,
                        navigation.Message, navigation.ErrorMessage);
                    return Complete(result, watch, "ResourceSearchPanel could not be opened.", navigation.ErrorMessage);
                }

                GameDetectionResult panel = await detector.DetectAsync(deviceName, cancellationToken);
                List<ConfigurationTemplateEvidence> panelEvidence = PanelEvidence(panel);
                if (!IsVerifiedPanel(panel))
                {
                    AddStep(steps, "EnsurePanel", false, 1, panelEvidence,
                        "ResourceSearchPanel did not have both required evidence signals.", panel.ErrorMessage);
                    result.FinalState = panel.State;
                    return Complete(result, watch, "Panel verification failed.", panel.ErrorMessage);
                }
                result.FinalState = panel.State;
                AddStep(steps, "EnsurePanel", true, 1, panelEvidence,
                    "ResourceSearchPanel verified.", null);

                if (!await EnsureResourceTabAsync(deviceName, result, steps, cancellationToken))
                    return Complete(result, watch, "Resource search tab could not be selected.", LastError(steps));
                if (!await ConfigureResourceAsync(deviceName, result, steps, cancellationToken))
                    return Complete(result, watch, $"{request.ResourceType} selection failed.", LastError(steps));
                if (!await ConfigureLevelAsync(deviceName, result, steps, cancellationToken))
                    return Complete(result, watch, "Level configuration failed.", LastError(steps));
                if (!await ConfigureFilterAsync(deviceName, request.UnoccupiedOnly, result, steps, cancellationToken))
                    return Complete(result, watch, "Unoccupied filter configuration failed.", LastError(steps));
                if (!await VerifyFinalAsync(deviceName, request, result, steps, cancellationToken))
                    return Complete(result, watch, "Final configuration verification failed.", LastError(steps));

                result.Success = true;
                return Complete(result, watch, $"{request.ResourceType} level {request.TargetLevel} search criteria verified; Search was not pressed.", null);
            }
            catch (OperationCanceledException)
            {
                logger.Info($"[Resource Search Configuration] DeviceName='{deviceName}', Cancellation=true, "
                    + $"TapCount={result.TapCount}, DurationMs={watch.Elapsed.TotalMilliseconds:F0}");
                throw;
            }
            catch (Exception exception)
            {
                logger.Error($"[Resource Search Configuration] DeviceName='{deviceName}', Error='{exception.Message}', "
                    + $"TapCount={result.TapCount}, DurationMs={watch.Elapsed.TotalMilliseconds:F0}", exception);
                return Complete(result, watch, "Configuration failed.", exception.Message);
            }
        }

        private async Task<bool> EnsureResourceTabAsync(string deviceName,
            ResourceSearchConfigurationResult result, IList<ConfigurationStepResult> steps,
            CancellationToken cancellationToken)
        {
            byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            ConfigurationTemplateEvidence selected = Match(screenshot, TemplateId.ResourceTabSelected);
            ConfigurationTemplateEvidence unselected = Match(screenshot, TemplateId.ResourceTabUnselected);
            var evidence = new List<ConfigurationTemplateEvidence> { selected, unselected };
            if (selected.Found)
            {
                AddStep(steps, "SelectResourceTab", true, 1, evidence,
                    "Resource tab was already selected; no Tap was sent.", null);
                return true;
            }
            if (!HasBounds(unselected))
            {
                AddStep(steps, "SelectResourceTab", false, 1, evidence,
                    "Resource tab was not found with valid bounds; no fallback Tap was sent.", null);
                return false;
            }

            await TapAsync(deviceName, unselected, "SelectResourceTab", result, cancellationToken);
            ConfigurationTemplateEvidence verified = await PollForAsync(
                deviceName, TemplateId.ResourceTabSelected, cancellationToken);
            evidence.Add(verified);
            AddStep(steps, "SelectResourceTab", verified.Found, 1, evidence,
                verified.Found ? "Resource tab verified after Tap."
                    : "Resource tab was not verified before timeout.", null);
            return verified.Found;
        }

        private async Task<bool> ConfigureResourceAsync(string deviceName,
            ResourceSearchConfigurationResult result, IList<ConfigurationStepResult> steps,
            CancellationToken cancellationToken)
        {
            var evidence = new List<ConfigurationTemplateEvidence>();
            for (int attempt = 1; attempt <= options.MaxSelectionAttempts; attempt++)
            {
                byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                ConfigurationTemplateEvidence search = Match(screenshot, TemplateId.SearchButtonEnabled);
                ResourceTemplateProfile profile = profileProvider.Get(result.RequestedResource);
                TemplateId selectedId = profile.SelectedTemplate;
                TemplateId unselectedId = profile.UnselectedTemplate;
                ConfigurationTemplateEvidence selected = MatchResourceState(screenshot, selectedId, search);
                ConfigurationTemplateEvidence unselected = MatchResourceState(screenshot, unselectedId, search);
                evidence.Add(selected); evidence.Add(unselected);
                if (selected.Found)
                {
                    string message = unselected.Found
                        ? $"{result.RequestedResource} selected verified; unselected also matched and was ignored as ambiguous evidence."
                        : $"{result.RequestedResource} selected verified without input.";
                    result.ResourceVerified = true;
                    AddStep(steps, "SelectResource", true, attempt, evidence, message, null);
                    return true;
                }
                if (!HasBounds(unselected))
                {
                    AddStep(steps, "SelectResource", false, attempt, evidence,
                        $"{result.RequestedResource} unselected template was not found with valid bounds; no Tap was sent.", null);
                    return false;
                }

                await TapAsync(deviceName, unselected, "SelectResource", result, cancellationToken);
                ConfigurationTemplateEvidence verified = await PollForResourceStateAsync(
                    deviceName, selectedId, search, cancellationToken);
                evidence.Add(verified);
                if (verified.Found)
                {
                    result.ResourceVerified = true;
                    AddStep(steps, "SelectResource", true, attempt, evidence,
                        $"{result.RequestedResource} selected template verified after Tap.", null);
                    return true;
                }
            }
            AddStep(steps, "SelectResource", false, options.MaxSelectionAttempts, evidence,
                $"{result.RequestedResource} selected template was not verified before the attempt limit.", null);
            return false;
        }

        private async Task<bool> ConfigureLevelAsync(string deviceName,
            ResourceSearchConfigurationResult result, IList<ConfigurationStepResult> steps,
            CancellationToken cancellationToken)
        {
            byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            TemplateId levelTemplateId = GetLevelTemplateId(result.RequestedLevel);
            ConfigurationTemplateEvidence minus = Match(screenshot, TemplateId.LevelMinusButton);
            ConfigurationTemplateEvidence plus = Match(screenshot, TemplateId.LevelPlusButton);
            ConfigurationTemplateEvidence search = Match(screenshot, TemplateId.SearchButtonEnabled);
            ConfigurationTemplateEvidence currentLevel = MatchLevel(
                screenshot, levelTemplateId, minus, plus, search);
            var evidence = new List<ConfigurationTemplateEvidence>
            {
                currentLevel, minus, plus, search
            };
            if (currentLevel.Found)
            {
                result.LevelVerified = true;
                AddStep(steps, "SetLevel", true, 1, evidence,
                    $"Level {result.RequestedLevel} was already verified; no level input was sent.", null);
                return true;
            }

            if (!HasBounds(minus) || !HasBounds(plus))
            {
                AddStep(steps, "SetLevel", false, 1, evidence,
                    "Level minus/plus controls were not found with valid bounds; no level input was sent.", null);
                return false;
            }

            for (int index = 0; index < options.ResetMinusTapCount; index++)
            {
                if (!await TapFreshAsync(deviceName, TemplateId.LevelMinusButton,
                    minus, "ResetLevel", result, cancellationToken))
                {
                    AddStep(steps, "SetLevel", false, 1, evidence,
                        "Fresh LevelMinusButton bounds were unavailable; level sequence stopped.", null);
                    return false;
                }
                await Task.Delay(options.TapIntervalMs, cancellationToken);
            }
            int plusTapCount = result.RequestedLevel - options.MinimumLevel;
            for (int index = 0; index < plusTapCount; index++)
            {
                if (!await TapFreshAsync(deviceName, TemplateId.LevelPlusButton,
                    plus, "IncreaseLevel", result, cancellationToken))
                {
                    AddStep(steps, "SetLevel", false, 1, evidence,
                        "Fresh LevelPlusButton bounds were unavailable; level sequence stopped.", null);
                    return false;
                }
                await Task.Delay(options.TapIntervalMs, cancellationToken);
            }

            ConfigurationTemplateEvidence level = await PollForLevelAsync(
                deviceName, levelTemplateId, minus, plus, cancellationToken);
            evidence.Add(level);
            result.LevelVerified = level.Found;
            AddStep(steps, "SetLevel", level.Found, 1, evidence,
                level.Found
                    ? $"Level {result.RequestedLevel} verified after {options.ResetMinusTapCount} minus and {plusTapCount} plus Taps."
                    : $"{levelTemplateId} was not verified after the bounded Tap sequence.", null);
            return level.Found;
        }

        private async Task<bool> ConfigureFilterAsync(string deviceName, bool requestedChecked,
            ResourceSearchConfigurationResult result, IList<ConfigurationStepResult> steps,
            CancellationToken cancellationToken)
        {
            TemplateId desiredId = requestedChecked
                ? TemplateId.UnoccupiedFilterChecked : TemplateId.UnoccupiedFilterUnchecked;
            TemplateId otherId = requestedChecked
                ? TemplateId.UnoccupiedFilterUnchecked : TemplateId.UnoccupiedFilterChecked;
            byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            ConfigurationTemplateEvidence search = Match(screenshot, TemplateId.SearchButtonEnabled);
            ConfigurationTemplateEvidence desired = MatchFilterState(screenshot, desiredId, search);
            ConfigurationTemplateEvidence other = MatchFilterState(screenshot, otherId, search);
            var evidence = new List<ConfigurationTemplateEvidence> { desired, other };
            if (desired.Found)
            {
                result.FilterVerified = true;
                AddStep(steps, "SetUnoccupiedFilter", true, 1, evidence,
                    "Requested filter state was already verified; no Tap was sent.", null);
                return true;
            }
            if (!HasBounds(other))
            {
                // The unchecked control has a translucent center, so its template can
                // change with the map rendered behind the panel. For the supported
                // UnoccupiedOnly workflow, use the verified Search button as a local
                // anchor and still require the checked template after the Tap. This is
                // bounded to the known 1280x720 panel layout and is never a blind Tap.
                if (!requestedChecked || !HasBounds(search))
                {
                    AddStep(steps, "SetUnoccupiedFilter", false, 1, evidence,
                        "Opposite filter state was not found with valid bounds; no Tap was sent.", null);
                    return false;
                }

                other = CreateSearchRelativeUncheckedFilterEvidence(search);
                if (!HasBounds(other))
                {
                    AddStep(steps, "SetUnoccupiedFilter", false, 1, evidence,
                        "Unchecked filter fallback bounds could not be derived; no Tap was sent.", null);
                    return false;
                }
                evidence.Add(other);
            }
            await TapAsync(deviceName, other, "SetUnoccupiedFilter", result, cancellationToken);
            ConfigurationTemplateEvidence verified = await PollForFilterStateAsync(
                deviceName, desiredId, search, cancellationToken);
            evidence.Add(verified);
            result.FilterVerified = verified.Found;
            AddStep(steps, "SetUnoccupiedFilter", verified.Found, 1, evidence,
                verified.Found ? "Requested filter state verified after Tap."
                    : "Requested filter state was not verified before timeout.", null);
            return verified.Found;
        }

        private static ConfigurationTemplateEvidence CreateSearchRelativeUncheckedFilterEvidence(
            ConfigurationTemplateEvidence search)
        {
            const int controlWidth = 22;
            const int controlHeight = 23;
            int x = search.X - 3;
            int y = search.Y - 49;
            ImageMatchResult match = x >= 0 && y >= 0
                ? ImageMatchResult.FoundAt(x, y, controlWidth, controlHeight)
                : ImageMatchResult.NotFound();
            return Evidence(TemplateId.UnoccupiedFilterUnchecked, match,
                match.Found
                    ? "Unchecked filter control bounds were derived from the verified Search button anchor."
                    : "Unchecked filter control bounds could not be derived from the Search button anchor.");
        }

        private async Task<bool> VerifyFinalAsync(string deviceName,
            ResourceSearchConfigurationRequest request, ResourceSearchConfigurationResult result,
            IList<ConfigurationStepResult> steps, CancellationToken cancellationToken)
        {
            GameDetectionResult state = await detector.DetectAsync(deviceName, cancellationToken);
            result.FinalState = state.State;
            byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            ConfigurationTemplateEvidence minus = Match(screenshot, TemplateId.LevelMinusButton);
            ConfigurationTemplateEvidence plus = Match(screenshot, TemplateId.LevelPlusButton);
            ConfigurationTemplateEvidence search = Match(screenshot, TemplateId.SearchButtonEnabled);
            var evidence = new List<ConfigurationTemplateEvidence>
            {
                Match(screenshot, TemplateId.ResourceTabSelected),
                MatchResourceState(screenshot, profileProvider.Get(request.ResourceType).SelectedTemplate, search),
                MatchLevel(screenshot, GetLevelTemplateId(request.TargetLevel), minus, plus, search),
                MatchFilterState(screenshot, request.UnoccupiedOnly
                    ? TemplateId.UnoccupiedFilterChecked : TemplateId.UnoccupiedFilterUnchecked, search),
                search
            };
            bool success = IsVerifiedPanel(state) && evidence.All(item => item.Found);
            result.ResourceVerified = evidence[1].Found;
            result.LevelVerified = evidence[2].Found;
            result.FilterVerified = evidence[3].Found;
            AddStep(steps, "FinalVerification", success, 1, evidence,
                success ? "Panel and all requested criteria were verified; Search was not pressed."
                    : "Panel or one or more requested criteria could not be verified.", state.ErrorMessage);
            return success;
        }

        private async Task<ConfigurationTemplateEvidence> PollForAsync(string deviceName,
            TemplateId templateId, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            ConfigurationTemplateEvidence last = Evidence(templateId, null, "Not checked.");
            while (watch.Elapsed < TimeSpan.FromSeconds(options.ActionVerificationTimeoutSeconds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
                last = Match(screenshot, templateId);
                if (last.Found) return last;
                await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            }
            return last;
        }

        private async Task<ConfigurationTemplateEvidence> PollForLevelAsync(string deviceName,
            TemplateId levelTemplateId,
            ConfigurationTemplateEvidence minus, ConfigurationTemplateEvidence plus,
            CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            ConfigurationTemplateEvidence last = Evidence(
                levelTemplateId, null, "Not checked.");
            while (watch.Elapsed < TimeSpan.FromSeconds(options.ActionVerificationTimeoutSeconds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                    deviceName, cancellationToken);
                ConfigurationTemplateEvidence currentMinus = Match(
                    screenshot, TemplateId.LevelMinusButton);
                ConfigurationTemplateEvidence currentPlus = Match(
                    screenshot, TemplateId.LevelPlusButton);
                ConfigurationTemplateEvidence search = Match(
                    screenshot, TemplateId.SearchButtonEnabled);
                last = MatchLevel(screenshot, levelTemplateId,
                    currentMinus, currentPlus, search);
                if (last.Found) return last;
                await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            }
            return last;
        }

        private async Task<ConfigurationTemplateEvidence> PollForFilterStateAsync(string deviceName,
            TemplateId templateId, ConfigurationTemplateEvidence search,
            CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            ConfigurationTemplateEvidence last = Evidence(templateId, null, "Not checked.");
            while (watch.Elapsed < TimeSpan.FromSeconds(options.ActionVerificationTimeoutSeconds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                    deviceName, cancellationToken);
                last = MatchFilterState(screenshot, templateId, search);
                if (last.Found) return last;
                await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            }
            return last;
        }

        private async Task<ConfigurationTemplateEvidence> PollForResourceStateAsync(string deviceName,
            TemplateId templateId, ConfigurationTemplateEvidence search,
            CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            ConfigurationTemplateEvidence last = Evidence(templateId, null, "Not checked.");
            while (watch.Elapsed < TimeSpan.FromSeconds(options.ActionVerificationTimeoutSeconds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(
                    deviceName, cancellationToken);
                last = MatchResourceState(screenshot, templateId, search);
                if (last.Found) return last;
                await Task.Delay(options.StatePollIntervalMs, cancellationToken);
            }
            return last;
        }

        private ConfigurationTemplateEvidence MatchLevel(byte[] screenshot, TemplateId levelTemplateId,
            ConfigurationTemplateEvidence minus, ConfigurationTemplateEvidence plus,
            ConfigurationTemplateEvidence search)
        {
            ConfigurationTemplateEvidence direct = Match(screenshot, levelTemplateId);
            if (direct.Found) return direct;

            ImageRegion? region = CreateLevelValueRegion(minus, plus)
                ?? CreateSearchRelativeLevelValueRegion(search);
            if (!region.HasValue) return direct;

            byte[] sourceTemplate = templateRegistry.LoadBytes(levelTemplateId);
            byte[] stableTemplate = TryCreateStableLevelTemplate(sourceTemplate) ?? sourceTemplate;
            ImageMatchResult match = imageMatcher.Find(screenshot, stableTemplate, region);
            if (match == null || !match.Found)
                match = TryMatchBinarizedCurrentLevel(
                    screenshot, sourceTemplate, minus, plus, region.Value);
            return Evidence(levelTemplateId, match, match != null && match.Found
                ? $"{levelTemplateId} matched inside the current-value region using stable UI text."
                : $"{levelTemplateId} did not match directly, by its stable chip, or by binary UI text.");
        }

        private static ImageRegion? CreateSearchRelativeLevelValueRegion(
            ConfigurationTemplateEvidence search)
        {
            if (!HasBounds(search)) return null;

            // The level value and Search button share the fixed 1280x720 panel overlay.
            // This ROI is used only for matching the already-visible level; it is never
            // used as an input coordinate fallback.
            int left = Math.Max(0, search.X - 480);
            int top = Math.Max(0, search.Y + 70);
            const int width = 330;
            const int height = 70;
            return new ImageRegion(left, top, width, height);
        }

        private static ImageRegion? CreateLevelValueRegion(
            ConfigurationTemplateEvidence minus, ConfigurationTemplateEvidence plus)
        {
            ConfigurationTemplateEvidence leftControl = minus.X <= plus.X ? minus : plus;
            ConfigurationTemplateEvidence rightControl = minus.X <= plus.X ? plus : minus;
            int left = leftControl.X + leftControl.Width;
            int right = rightControl.X;
            int top = Math.Max(0, Math.Min(minus.Y, plus.Y) - 100);
            int bottom = Math.Max(minus.Y + minus.Height, plus.Y + plus.Height) + 20;
            return right > left && bottom > top
                ? new ImageRegion(left, top, right - left, bottom - top)
                : (ImageRegion?)null;
        }

        private static byte[] TryCreateStableLevelTemplate(byte[] templateBytes)
        {
            try
            {
                using (var input = new MemoryStream(templateBytes, writable: false))
                using (var source = new Bitmap(input))
                {
                    int left = source.Width * 35 / 100;
                    int right = source.Width * 72 / 100;
                    int width = right - left;
                    if (width <= 0) return null;

                    using (Bitmap stable = source.Clone(
                        new Rectangle(left, 0, width, source.Height), PixelFormat.Format32bppArgb))
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

        private ImageMatchResult TryMatchBinarizedCurrentLevel(byte[] screenshot,
            byte[] templateBytes, ConfigurationTemplateEvidence minus,
            ConfigurationTemplateEvidence plus, ImageRegion fallbackRegion)
        {
            try
            {
                ImageRegion currentValueRegion;
                if (HasBounds(minus) && HasBounds(plus))
                {
                    int controlCenterX = (minus.X + minus.Width / 2
                        + plus.X + plus.Width / 2) / 2;
                    int top = Math.Max(0, Math.Min(minus.Y, plus.Y) - 110);
                    currentValueRegion = new ImageRegion(
                        Math.Max(0, controlCenterX - 80), top, 100, 130);
                }
                else
                {
                    currentValueRegion = fallbackRegion;
                }

                using (var screenshotStream = new MemoryStream(screenshot, writable: false))
                using (var screenshotBitmap = new Bitmap(screenshotStream))
                using (var templateStream = new MemoryStream(templateBytes, writable: false))
                using (var templateBitmap = new Bitmap(templateStream))
                {
                    if ((long)currentValueRegion.X + currentValueRegion.Width > screenshotBitmap.Width
                        || (long)currentValueRegion.Y + currentValueRegion.Height > screenshotBitmap.Height)
                        return ImageMatchResult.NotFound();

                    int glyphLeft = templateBitmap.Width * 32 / 100;
                    int glyphRight = templateBitmap.Width * 68 / 100;
                    int glyphWidth = glyphRight - glyphLeft;
                    if (glyphWidth <= 0) return ImageMatchResult.NotFound();

                    using (Bitmap currentValue = screenshotBitmap.Clone(
                        new Rectangle(currentValueRegion.X, currentValueRegion.Y,
                            currentValueRegion.Width, currentValueRegion.Height),
                        PixelFormat.Format32bppArgb))
                    using (Bitmap glyph = templateBitmap.Clone(
                        new Rectangle(glyphLeft, 0, glyphWidth, templateBitmap.Height),
                        PixelFormat.Format32bppArgb))
                    {
                        byte[] binaryCurrentValue = CreateWhiteTextMaskPng(currentValue);
                        byte[] binaryGlyph = CreateWhiteTextMaskPng(glyph);
                        ImageMatchResult local = imageMatcher.Find(
                            binaryCurrentValue, binaryGlyph, null);
                        return local != null && local.Found
                            ? ImageMatchResult.FoundAt(
                                currentValueRegion.X + local.X,
                                currentValueRegion.Y + local.Y,
                                local.Width, local.Height)
                            : ImageMatchResult.NotFound();
                    }
                }
            }
            catch (ArgumentException)
            {
                return ImageMatchResult.NotFound();
            }
        }

        private static byte[] CreateWhiteTextMaskPng(Bitmap source)
        {
            using (var mask = new Bitmap(source.Width, source.Height,
                PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < source.Height; y++)
                {
                    for (int x = 0; x < source.Width; x++)
                    {
                        Color pixel = source.GetPixel(x, y);
                        bool isLightUiText = pixel.R > 150
                            && pixel.G > 150 && pixel.B > 150;
                        mask.SetPixel(x, y, isLightUiText ? Color.White : Color.Black);
                    }
                }

                using (var output = new MemoryStream())
                {
                    mask.Save(output, ImageFormat.Png);
                    return output.ToArray();
                }
            }
        }

        private ConfigurationTemplateEvidence MatchFilterState(byte[] screenshot,
            TemplateId templateId, ConfigurationTemplateEvidence search)
        {
            ConfigurationTemplateEvidence direct = Match(screenshot, templateId);
            if (direct.Found || !HasBounds(search)) return direct;

            ImageRegion region = new ImageRegion(
                Math.Max(0, search.X - 40), Math.Max(0, search.Y - 110),
                search.Width + 80, 110);
            byte[] sourceTemplate = templateRegistry.LoadBytes(templateId);
            byte[] stableTemplate = TryCreateStableFilterTemplate(sourceTemplate) ?? sourceTemplate;
            ImageMatchResult match = imageMatcher.Find(screenshot, stableTemplate, region);
            return Evidence(templateId, match, match != null && match.Found
                ? $"Template '{templateId}' matched by its stable control center inside the Search-relative region."
                : $"Template '{templateId}' did not match directly or by its stable control center.");
        }

        private ConfigurationTemplateEvidence MatchResourceState(byte[] screenshot,
            TemplateId templateId, ConfigurationTemplateEvidence search)
        {
            ConfigurationTemplateEvidence direct = Match(screenshot, templateId);
            if (direct.Found || !HasBounds(search)) return direct;

            int left = Math.Max(0, search.X - 700);
            int top = Math.Max(0, search.Y - 180);
            ImageRegion region = new ImageRegion(left, top, search.X - left, 210);
            byte[] sourceTemplate = templateRegistry.LoadBytes(templateId);
            ImageMatchResult match = ImageMatchResult.NotFound();
            int matchedDivisor = 0;
            bool stableCropCreated = false;
            foreach (int divisor in new[] { 4, 3 })
            {
                byte[] stableTemplate = TryCreateStableResourceTemplate(sourceTemplate, divisor);
                if (stableTemplate == null) continue;
                stableCropCreated = true;
                match = imageMatcher.Find(screenshot, stableTemplate, region);
                if (match != null && match.Found)
                {
                    matchedDivisor = divisor;
                    break;
                }
            }
            if (!stableCropCreated)
                match = imageMatcher.Find(screenshot, sourceTemplate, region);
            string matchedMessage = matchedDivisor > 0
                ? $"Template '{templateId}' matched by its stable icon center crop (1/{matchedDivisor} margins) inside the Search-relative region."
                : $"Template '{templateId}' matched inside the Search-relative region.";
            return Evidence(templateId, match, match != null && match.Found
                ? matchedMessage
                : $"Template '{templateId}' did not match directly or by either stable icon center crop.");
        }

        private static byte[] TryCreateStableResourceTemplate(byte[] templateBytes, int marginDivisor)
        {
            try
            {
                using (var input = new MemoryStream(templateBytes, writable: false))
                using (var source = new Bitmap(input))
                {
                    int marginX = source.Width / marginDivisor;
                    int marginY = source.Height / marginDivisor;
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

        private static byte[] TryCreateStableFilterTemplate(byte[] templateBytes)
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

        private ConfigurationTemplateEvidence Match(byte[] screenshot, TemplateId templateId)
        {
            ImageMatchResult match = imageMatcher.Find(screenshot, templateRegistry.LoadBytes(templateId), null);
            return Evidence(templateId, match, match != null && match.Found
                ? $"Template '{templateId}' matched."
                : $"Template '{templateId}' did not match.");
        }

        private async Task TapAsync(string deviceName, ConfigurationTemplateEvidence evidence,
            string action, ResourceSearchConfigurationResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = evidence.X + evidence.Width / 2;
            int y = evidence.Y + evidence.Height / 2;
            logger.Info($"[Resource Search Configuration] DeviceName='{deviceName}', Action='{action}', "
                + $"Template='{evidence.TemplateId}', Bounds=({evidence.X},{evidence.Y},{evidence.Width},{evidence.Height}), "
                + $"Tap=({x},{y}), Cancellation=false");
            await ldPlayerClient.TapAsync(deviceName, x, y, cancellationToken);
            result.TapCount++;
        }

        private async Task<bool> TapFreshAsync(string deviceName, TemplateId templateId,
            ConfigurationTemplateEvidence lastVerifiedBounds,
            string action, ResourceSearchConfigurationResult result,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] screenshot = await ldPlayerClient.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            ConfigurationTemplateEvidence fresh = Match(screenshot, templateId);
            if (!HasBounds(fresh) && HasBounds(lastVerifiedBounds))
                fresh = MatchStableLevelControl(screenshot, templateId, lastVerifiedBounds);
            if (!HasBounds(fresh)) return false;
            await TapAsync(deviceName, fresh, action, result, cancellationToken);
            return true;
        }

        private ConfigurationTemplateEvidence MatchStableLevelControl(byte[] screenshot,
            TemplateId templateId, ConfigurationTemplateEvidence lastVerifiedBounds)
        {
            const int padding = 16;
            int left = Math.Max(0, lastVerifiedBounds.X - padding);
            int top = Math.Max(0, lastVerifiedBounds.Y - padding);
            int width = lastVerifiedBounds.Width + padding * 2;
            int height = lastVerifiedBounds.Height + padding * 2;
            var region = new ImageRegion(left, top, width, height);
            byte[] sourceTemplate = templateRegistry.LoadBytes(templateId);
            byte[] stableTemplate = TryCreateStableLevelControlTemplate(sourceTemplate) ?? sourceTemplate;

            ImageMatchResult match = imageMatcher.Find(screenshot, stableTemplate, region);
            return Evidence(templateId, match, match != null && match.Found
                ? $"Template '{templateId}' matched by its stable center inside the last verified local bounds."
                : $"Template '{templateId}' did not match directly or by its stable center inside the last verified local bounds.");
        }

        private static byte[] TryCreateStableLevelControlTemplate(byte[] templateBytes)
        {
            try
            {
                using (var input = new MemoryStream(templateBytes, writable: false))
                using (var source = new Bitmap(input))
                {
                    int marginX = source.Width / 4;
                    int marginY = source.Height / 4;
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

        private string ValidateRequest(ResourceSearchConfigurationRequest request)
        {
            if (request == null) return "ResourceSearchConfigurationRequest is required.";
            if (!Enum.IsDefined(typeof(ResourceType), request.ResourceType))
                return $"Resource type '{request.ResourceType}' is not supported.";
            if (request.TargetLevel < options.MinimumLevel || request.TargetLevel > options.MaximumLevel)
                return $"TargetLevel must be between {options.MinimumLevel} and {options.MaximumLevel}.";
            if (request.TargetLevel != 5 && request.TargetLevel != 6 && request.TargetLevel != 7)
                return "Only target levels 5, 6, and 7 are supported by the available verification templates.";
            return null;
        }

        private string ValidateTemplates(ResourceType resourceType, int targetLevel)
        {
            foreach (TemplateId templateId in RequiredTemplates.Concat(new[]
            {
                profileProvider.Get(resourceType).SelectedTemplate,
                profileProvider.Get(resourceType).UnselectedTemplate,
                GetLevelTemplateId(targetLevel)
            }))
            {
                try
                {
                    string path = templateRegistry.GetPath(templateId);
                    if (!templateRegistry.Exists(templateId))
                        return $"Required template '{templateId}' was not found at '{path}'.";
                }
                catch (Exception exception)
                {
                    return $"Required template '{templateId}' could not be resolved: {exception.Message}";
                }
            }
            return null;
        }

        private static TemplateId GetLevelTemplateId(int level)
        {
            switch (level)
            {
                case 5: return TemplateId.LevelValue5;
                case 6: return TemplateId.LevelValue6;
                case 7: return TemplateId.LevelValue7;
                default: throw new ArgumentOutOfRangeException(nameof(level), level,
                    "Only target levels 5, 6, and 7 are supported.");
            }
        }

        private static bool IsVerifiedPanel(GameDetectionResult result)
        {
            return result != null && result.IsSuccessful && result.State == GameState.ResourceSearchPanel
                && result.Evidence != null
                && result.Evidence.Any(item =>
                    (item.TemplateId == TemplateId.ResourceSearchPanelAnchor
                        || item.TemplateId == TemplateId.LevelMinusButton
                        || item.TemplateId == TemplateId.ResourceTabSelected
                        || item.TemplateId == TemplateId.ResourceTabUnselected) && item.Found)
                && result.Evidence.Any(item => item.TemplateId == TemplateId.SearchButtonEnabled && item.Found);
        }

        private static List<ConfigurationTemplateEvidence> PanelEvidence(GameDetectionResult result)
        {
            var evidence = new List<ConfigurationTemplateEvidence>();
            if (result?.Evidence == null) return evidence;
            foreach (GameDetectionEvidence item in result.Evidence.Where(item =>
                item.TemplateId == TemplateId.ResourceSearchPanelAnchor
                || item.TemplateId == TemplateId.LevelMinusButton
                || item.TemplateId == TemplateId.ResourceTabSelected
                || item.TemplateId == TemplateId.ResourceTabUnselected
                || item.TemplateId == TemplateId.SearchButtonEnabled))
                evidence.Add(Evidence(item.TemplateId, item.MatchResult, item.Message));
            return evidence;
        }

        private static ConfigurationTemplateEvidence Evidence(TemplateId id,
            ImageMatchResult match, string message)
        {
            return new ConfigurationTemplateEvidence
            {
                TemplateId = id, Found = match != null && match.Found,
                X = match?.X ?? 0, Y = match?.Y ?? 0,
                Width = match?.Width ?? 0, Height = match?.Height ?? 0,
                Confidence = match?.Confidence, Message = message
            };
        }

        private static bool HasBounds(ConfigurationTemplateEvidence evidence) =>
            evidence != null && evidence.Found && evidence.Width > 0 && evidence.Height > 0;

        private static void AddStep(IList<ConfigurationStepResult> steps, string name,
            bool success, int attempts, IEnumerable<ConfigurationTemplateEvidence> evidence,
            string message, string error)
        {
            steps.Add(new ConfigurationStepResult
            {
                StepName = name, Success = success, Attempts = attempts,
                TemplateEvidence = (evidence ?? new ConfigurationTemplateEvidence[0]).ToList().AsReadOnly(),
                Message = message, ErrorMessage = error
            });
        }

        private static string LastError(IList<ConfigurationStepResult> steps) =>
            steps.Count == 0 ? null : steps[steps.Count - 1].ErrorMessage ?? steps[steps.Count - 1].Message;

        private ResourceSearchConfigurationResult InvalidResult(
            ResourceSearchConfigurationRequest request, string error)
        {
            var result = NewResult(request, new List<ConfigurationStepResult>());
            result.Message = "Request was rejected before input.";
            result.ErrorMessage = error;
            return result;
        }

        private static ResourceSearchConfigurationResult NewResult(
            ResourceSearchConfigurationRequest request, List<ConfigurationStepResult> steps)
        {
            return new ResourceSearchConfigurationResult
            {
                RequestedResource = request?.ResourceType ?? ResourceType.Iron,
                RequestedLevel = request?.TargetLevel ?? 0,
                RequestedUnoccupiedOnly = request != null && request.UnoccupiedOnly,
                InitialState = GameState.Unknown, FinalState = GameState.Unknown,
                Steps = steps.AsReadOnly()
            };
        }

        private ResourceSearchConfigurationResult Complete(ResourceSearchConfigurationResult result,
            Stopwatch watch, string message, string error)
        {
            result.Duration = watch.Elapsed;
            result.Message = message;
            result.ErrorMessage = error;
            logger.Info($"[Resource Search Configuration] RequestedResource='{result.RequestedResource}', "
                + $"RequestedLevel={result.RequestedLevel}, UnoccupiedOnly={result.RequestedUnoccupiedOnly}, "
                + $"InitialState='{result.InitialState}', FinalState='{result.FinalState}', "
                + $"ResourceVerified={result.ResourceVerified}, LevelVerified={result.LevelVerified}, "
                + $"FilterVerified={result.FilterVerified}, TapCount={result.TapCount}, "
                + $"DurationMs={result.Duration.TotalMilliseconds:F0}, Success={result.Success}, "
                + $"Error='{error ?? string.Empty}'");
            return result;
        }

        private void LogStart(string deviceName, ResourceSearchConfigurationRequest request)
        {
            logger.Info($"[Resource Search Configuration] DeviceName='{deviceName}', "
                + $"RequestedResource='{request.ResourceType}', RequestedLevel={request.TargetLevel}, "
                + $"UnoccupiedOnly={request.UnoccupiedOnly}, Cancellation=false, Phase='Starting'");
        }
    }
}
