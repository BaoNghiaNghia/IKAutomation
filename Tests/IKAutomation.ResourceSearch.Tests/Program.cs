using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.GameDetection;
using ADB_Tool_Automation_Post_FB.Core.Navigation;
using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IKAutomation.ResourceSearch.Tests
{
    internal static class Program
    {
        private static readonly CancellationToken Token = new CancellationToken(false);
        private static int passed;
        private static int failed;

        private static int Main()
        {
            Run("Navigation failure sends no input", NavigationFailureNoInput);
            Run("Monster panel switches to resource tab", ResourceTabIsSelected);
            Run("Resource tab evidence verifies open panel", ResourceTabEvidenceVerifiesPanel);
            Run("Selected Iron is not tapped", SelectedIronNoTap);
            Run("Unselected Iron center is tapped", UnselectedIronCenter);
            Run("Iron requires selected verification", IronRequiresVerification);
            Run("Iron verification uses stable icon fallback", IronStableIconFallback);
            Run("Food selection uses compact stable icon fallback", FoodCompactStableIconFallback);
            Run("Missing Iron bounds prevents fallback", MissingIronBounds);
            Run("Level reset taps minus eight times", MinusEight);
            Run("Level seven taps plus six times", PlusSix);
            Run("Level six taps plus five times and verifies", LevelSix);
            Run("Level five taps plus four times and verifies", LevelFive);
            Run("Missing level six template fails before input", MissingLevelSixTemplate);
            Run("Level seven already selected sends no level input", LevelSevenAlreadySelected);
            Run("Level requires LevelValue7 verification", LevelRequiresVerification);
            Run("Level verification uses stable chip fallback", LevelStableChipFallback);
            Run("Level verification tolerates translucent panel background", LevelBinaryTextFallback);
            Run("Level controls use bounded stable-center refresh", LevelControlLocalFallback);
            Run("Out-of-range level rejected before input", InvalidLevelNoInput);
            Run("Checked filter is not tapped", CheckedFilterNoTap);
            Run("Unchecked filter is tapped and verified", UncheckedFilterToggled);
            Run("Filter verification uses stable control fallback", FilterStableControlFallback);
            Run("Unchecked filter uses Search-relative fallback", UncheckedFilterSearchRelativeFallback);
            Run("UnoccupiedOnly false is supported", FalseFilterSupported);
            Run("Final success requires all evidence", FinalEvidenceRequired);
            Run("Search button is never tapped", SearchNeverTapped);
            Run("Polling timeout returns failure", TimeoutFailure);
            Run("Polling cancellation is respected", PollCancellation);
            Run("Level sequence cancellation is respected", LevelCancellation);
            Run("Same-device workflows are serialized", SameDeviceSerialized);
            Run("Different devices are not globally locked", DifferentDevicesParallel);
            Run("Prohibited input methods are not called", NoProhibitedInput);
            Run("Missing template fails without input", MissingTemplateNoInput);
            Run("Null request is rejected before input", NullRequestNoInput);
            Run("Ambiguous resource evidence prefers selected", AmbiguousPrefersSelected);
            Run("Stone selected template is used", StoneSelectedTemplate);
            Run("Missing Stone template prevents input", MissingStoneTemplateNoInput);
            Run("Resource profiles map all four resources", ResourceProfilesMapAll);
            Run("Wood selected and unselected templates are used", WoodTemplatesUsed);
            Run("Food selected and unselected templates are used", FoodTemplatesUsed);
            Run("Missing popup title makes profile unsupported", MissingPopupTitleUnsupported);
            Console.WriteLine($"Resource search tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS: " + name); }
            catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL: " + name + " - " + exception); }
        }

        private static void NavigationFailureNoInput()
        {
            Fixture f = Setup(); f.Navigation.Success = false;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(!r.Success, "Expected failure."); Equal(0, f.Client.TotalInput, "Input sent.");
        }

        private static void ResourceTabIsSelected()
        {
            Fixture f = Setup(); f.Ui.ResourceTabSelected = false;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(r.Success && f.Ui.ResourceTabSelected, r.ErrorMessage);
            Equal(1, f.Client.ResourceTabTaps, "Resource tab tap count.");
            Assert(f.Client.Taps.Contains("210,674"), "Wrong resource tab center.");
        }

        private static void ResourceTabEvidenceVerifiesPanel()
        {
            Fixture f = Setup(); f.Detector.UseResourceTabEvidence = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(r.Success, r.ErrorMessage);
        }

        private static void SelectedIronNoTap()
        {
            Fixture f = Setup(); f.Ui.ResourceSelected = true; f.Ui.FilterChecked = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(r.Success, r.ErrorMessage); Equal(0, f.Client.ResourceTaps, "Resource tapped.");
        }

        private static void UnselectedIronCenter()
        {
            Fixture f = Setup(); Execute(f);
            Equal(1, f.Client.ResourceTaps, "Resource tap count.");
            Assert(f.Client.Taps.Contains("20,25"), "Wrong resource center.");
        }

        private static void IronRequiresVerification()
        {
            Fixture f = Setup(); f.Ui.IgnoreResourceTap = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(!r.Success && !r.ResourceVerified, "Resource falsely verified.");
        }

        private static void LevelControlLocalFallback()
        {
            Fixture f = Setup(); f.Ui.LevelControlsLoseDirectMatchAfterTap = true;
            ResourceSearchConfigurationResult result = Execute(f);
            Assert(result.Success && result.LevelVerified, result.ErrorMessage);
            Equal(8, f.Client.MinusTaps, "minus taps"); Equal(6, f.Client.PlusTaps, "plus taps");
        }

        private static void IronStableIconFallback()
        {
            Fixture f = Setup(); f.Ui.ResourceRequiresStableIcon = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(r.Success && r.ResourceVerified, r.ErrorMessage);
            Equal(1, f.Client.ResourceTaps, "Resource taps.");
        }

        private static void FoodCompactStableIconFallback()
        {
            Fixture f = Setup();
            f.Ui.ResourceRequiresCompactStableIcon = true;
            f.Registry.UseImageFoodUnselectedTemplate = true;
            var request = Request(); request.ResourceType = ResourceType.Food;
            ResourceSearchConfigurationResult result = Execute(f, request);
            Assert(result.Success && result.ResourceVerified, result.ErrorMessage);
            Equal(1, f.Client.ResourceTaps, "Resource taps.");
            Assert(f.Client.Taps.Contains("110,25"), "Compact Food icon center was not tapped.");
        }

        private static void MissingIronBounds()
        {
            Fixture f = Setup(); f.Ui.InvalidResourceBounds = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(!r.Success, "Unexpected success."); Equal(0, f.Client.TotalInput, "Fallback input sent.");
        }

        private static void MinusEight() { Fixture f = Setup(); Execute(f); Equal(8, f.Client.MinusTaps, "Minus taps."); }
        private static void PlusSix() { Fixture f = Setup(); Execute(f); Equal(6, f.Client.PlusTaps, "Plus taps."); }
        private static void LevelSix() { Fixture f = Setup(); ResourceSearchConfigurationRequest q = Request(); q.TargetLevel = 6; ResourceSearchConfigurationResult r = Execute(f, q); Assert(r.Success && r.LevelVerified, r.ErrorMessage); Equal(5, f.Client.PlusTaps, "Plus taps."); }
        private static void LevelFive() { Fixture f = Setup(); ResourceSearchConfigurationRequest q = Request(); q.TargetLevel = 5; ResourceSearchConfigurationResult r = Execute(f, q); Assert(r.Success && r.LevelVerified, r.ErrorMessage); Equal(4, f.Client.PlusTaps, "Plus taps."); }
        private static void MissingLevelSixTemplate() { Fixture f = Setup(); f.Registry.Missing = TemplateId.LevelValue6; ResourceSearchConfigurationRequest q = Request(); q.TargetLevel = 6; ResourceSearchConfigurationResult r = Execute(f, q); Assert(!r.Success && r.ErrorMessage.Contains("LevelValue6"), "missing target template"); Equal(0, f.Client.TotalInput, "input"); }

        private static void LevelSevenAlreadySelected()
        {
            Fixture f = Setup(); f.Ui.Level = 7;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(r.Success && r.LevelVerified, r.ErrorMessage);
            Equal(0, f.Client.MinusTaps, "Minus taps.");
            Equal(0, f.Client.PlusTaps, "Plus taps.");
        }

        private static void LevelRequiresVerification()
        {
            Fixture f = Setup(); f.Ui.HideLevelValue = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(!r.Success && !r.LevelVerified, "Level falsely verified.");
        }

        private static void LevelStableChipFallback()
        {
            Fixture f = Setup(); f.Ui.LevelRequiresStableChip = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(r.Success && r.LevelVerified, r.ErrorMessage);
        }

        private static void LevelBinaryTextFallback()
        {
            Fixture f = Setup();
            f.Ui.ResourceSelected = true;
            f.Ui.FilterChecked = true;
            f.Ui.Level = 7;
            f.Ui.HideLevelValue = true;
            f.Ui.BinaryLevelFallback = true;
            f.Registry.UseImageLevelTemplate = true;
            ResourceSearchConfigurationResult result = Execute(f);
            Assert(result.Success && result.LevelVerified,
                "Binary UI-text fallback did not verify the visible level 7 value.");
        }

        private static void InvalidLevelNoInput()
        {
            Fixture f = Setup(); ResourceSearchConfigurationRequest request = Request(); request.TargetLevel = 8;
            ResourceSearchConfigurationResult r = Execute(f, request);
            Assert(!r.Success, "Unexpected success."); Equal(0, f.Client.TotalInput, "Input sent."); Equal(0, f.Navigation.Calls, "Navigation called.");
        }

        private static void CheckedFilterNoTap()
        {
            Fixture f = Setup(); f.Ui.FilterChecked = true; Execute(f);
            Equal(0, f.Client.FilterTaps, "Filter tapped.");
        }

        private static void UncheckedFilterToggled()
        {
            Fixture f = Setup(); ResourceSearchConfigurationResult r = Execute(f);
            Assert(r.Success && r.FilterVerified, r.ErrorMessage); Equal(1, f.Client.FilterTaps, "Filter taps.");
        }

        private static void FilterStableControlFallback()
        {
            Fixture f = Setup(); f.Ui.FilterRequiresStableControl = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(r.Success && r.FilterVerified, r.ErrorMessage);
            Equal(1, f.Client.FilterTaps, "Filter taps.");
        }

        private static void UncheckedFilterSearchRelativeFallback()
        {
            Fixture f = Setup(); f.Ui.HideUncheckedFilter = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(r.Success && r.FilterVerified, r.ErrorMessage);
            Equal(1, f.Client.FilterTaps, "Filter fallback taps.");
            Assert(f.Client.Taps.Contains("408,362"), "Wrong Search-relative filter fallback center.");
        }

        private static void FalseFilterSupported()
        {
            Fixture f = Setup(); f.Ui.FilterChecked = true; ResourceSearchConfigurationRequest request = Request(); request.UnoccupiedOnly = false;
            ResourceSearchConfigurationResult r = Execute(f, request);
            Assert(r.Success && !f.Ui.FilterChecked, r.ErrorMessage); Equal(1, f.Client.FilterTaps, "Filter taps.");
        }

        private static void FinalEvidenceRequired()
        {
            Fixture f = Setup(); f.Detector.FailOnSecondCall = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(!r.Success, "Final state evidence was ignored.");
        }

        private static void SearchNeverTapped()
        {
            Fixture f = Setup(); Execute(f); Equal(0, f.Client.SearchTaps, "Search was tapped.");
        }

        private static void TimeoutFailure()
        {
            Fixture f = Setup(10, 1); f.Ui.IgnoreResourceTap = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(!r.Success, "Timeout should fail.");
        }

        private static void PollCancellation()
        {
            Fixture f = Setup(10, 2); f.Ui.IgnoreResourceTap = true;
            using (var source = new CancellationTokenSource(40))
                Throws<OperationCanceledException>(() => Execute(f, Request(), source.Token));
        }

        private static void LevelCancellation()
        {
            Fixture f = Setup(10, 2, 30); f.Ui.ResourceSelected = true;
            using (var source = new CancellationTokenSource(70))
                Throws<OperationCanceledException>(() => Execute(f, Request(), source.Token));
            Assert(f.Client.MinusTaps < 8, "Level sequence ignored cancellation.");
        }

        private static void SameDeviceSerialized()
        {
            Fixture f = Setup(); f.Navigation.DelayMs = 80;
            Task<ResourceSearchConfigurationResult> a = f.Service.ConfigureAsync("same", Request(), Token);
            Task<ResourceSearchConfigurationResult> b = f.Service.ConfigureAsync("same", Request(), Token);
            Task.WaitAll(a, b); Equal(1, f.Navigation.MaxActive, "Same device overlapped.");
        }

        private static void DifferentDevicesParallel()
        {
            Fixture f = Setup(); f.Navigation.DelayMs = 80;
            Task.WaitAll(f.Service.ConfigureAsync("a", Request(), Token), f.Service.ConfigureAsync("b", Request(), Token));
            Assert(f.Navigation.MaxActive >= 2, "Different devices globally blocked.");
        }

        private static void NoProhibitedInput()
        {
            Fixture f = Setup(); Execute(f); Equal(0, f.Client.ProhibitedCalls, "Prohibited input called.");
        }

        private static void MissingTemplateNoInput()
        {
            Fixture f = Setup(); f.Registry.Missing = TemplateId.LevelPlusButton;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(!r.Success && r.ErrorMessage.Contains("LevelPlusButton"), "Missing template not reported.");
            Equal(0, f.Client.TotalInput, "Input sent."); Equal(0, f.Navigation.Calls, "Navigation called.");
        }

        private static void NullRequestNoInput()
        {
            Fixture f = Setup(); ResourceSearchConfigurationResult r =
                f.Service.ConfigureAsync("LDPlayer", null, Token).GetAwaiter().GetResult();
            Assert(!r.Success, "Null request accepted."); Equal(0, f.Client.TotalInput, "Input sent.");
        }

        private static void AmbiguousPrefersSelected()
        {
            Fixture f = Setup(); f.Ui.ResourceSelected = true; f.Ui.AmbiguousResource = true; f.Ui.FilterChecked = true;
            ResourceSearchConfigurationResult r = Execute(f);
            Assert(r.Success, r.ErrorMessage); Equal(0, f.Client.ResourceTaps, "Ambiguous selected state was tapped.");
        }

        private static Fixture Setup(int pollMs = 1, int timeoutSeconds = 1, int tapIntervalMs = 1)
        {
            var ui = new FakeUi();
            var client = new FakeClient(ui);
            var navigation = new FakeNavigation();
            var detector = new FakeDetector();
            var registry = new FakeRegistry();
            var service = new ResourceSearchConfigurationService(navigation, detector, client,
                registry, new FakeMatcher(ui),
                new ResourceSearchConfigurationOptions(pollMs, timeoutSeconds, 2, 1, 7, 8, tapIntervalMs),
                new DeviceOperationLock(), new FakeLogger());
            return new Fixture { Ui = ui, Client = client, Navigation = navigation,
                Detector = detector, Registry = registry, Service = service };
        }

        private static ResourceSearchConfigurationRequest Request() => new ResourceSearchConfigurationRequest
        { ResourceType = ResourceType.Iron, TargetLevel = 7, UnoccupiedOnly = true };

        private static void StoneSelectedTemplate()
        {
            Fixture f = Setup(); f.Ui.ResourceSelected = true;
            ResourceSearchConfigurationRequest request = Request(); request.ResourceType = ResourceType.Stone;
            ResourceSearchConfigurationResult result = Execute(f, request);
            Assert(result.Success && result.ResourceVerified, result.ErrorMessage);
        }

        private static void MissingStoneTemplateNoInput()
        {
            Fixture f = Setup(); f.Registry.Missing = TemplateId.ResourceStoneSelected;
            ResourceSearchConfigurationRequest request = Request(); request.ResourceType = ResourceType.Stone;
            ResourceSearchConfigurationResult result = Execute(f, request);
            Assert(!result.Success && result.ErrorMessage.Contains("ResourceStoneSelected"), "missing Stone template");
            Equal(0, f.Client.TotalInput, "input");
        }

        private static void ResourceProfilesMapAll()
        {
            var provider = new ResourceTemplateProfileProvider(new FakeRegistry());
            ResourceTemplateProfile iron = provider.Get(ResourceType.Iron);
            ResourceTemplateProfile stone = provider.Get(ResourceType.Stone);
            ResourceTemplateProfile wood = provider.Get(ResourceType.Wood);
            ResourceTemplateProfile food = provider.Get(ResourceType.Food);
            Equal(TemplateId.ResourceIronSelected, iron.SelectedTemplate, "Iron selected");
            Equal(TemplateId.ResourcePopupIronTitle, iron.PopupTitleTemplate, "Iron popup");
            Equal(TemplateId.ResourceStoneSelected, stone.SelectedTemplate, "Stone selected");
            Equal(TemplateId.ResourcePopupStoneTitle, stone.PopupTitleTemplate, "Stone popup");
            Equal(TemplateId.ResourceWoodSelected, wood.SelectedTemplate, "Wood selected");
            Equal(TemplateId.ResourcePopupWoodTitle, wood.PopupTitleTemplate, "Wood popup");
            Equal(TemplateId.ResourceFoodSelected, food.SelectedTemplate, "Food selected");
            Equal(TemplateId.ResourcePopupFoodTitle, food.PopupTitleTemplate, "Food popup");
        }

        private static void WoodTemplatesUsed()
        {
            Fixture f = Setup(); var request = Request(); request.ResourceType = ResourceType.Wood;
            ResourceSearchConfigurationResult result = Execute(f, request);
            Assert(result.Success && result.ResourceVerified, result.ErrorMessage); Assert(f.Client.Taps.Contains("80,25"), "Wood center was not tapped.");
        }

        private static void FoodTemplatesUsed()
        {
            Fixture f = Setup(); var request = Request(); request.ResourceType = ResourceType.Food;
            ResourceSearchConfigurationResult result = Execute(f, request);
            Assert(result.Success && result.ResourceVerified, result.ErrorMessage); Assert(f.Client.Taps.Contains("110,25"), "Food center was not tapped.");
        }

        private static void MissingPopupTitleUnsupported()
        {
            var registry = new FakeRegistry { Missing = TemplateId.ResourcePopupWoodTitle };
            var provider = new ResourceTemplateProfileProvider(registry);
            Assert(!provider.IsSupported(ResourceType.Wood), "Wood should be unsupported.");
            Assert(provider.GetUnsupportedReason(ResourceType.Wood).Contains("ResourcePopupWoodTitle"), "Missing TemplateId was not reported.");
        }

        private static ResourceSearchConfigurationResult Execute(Fixture fixture,
            ResourceSearchConfigurationRequest request = null, CancellationToken? token = null) =>
            fixture.Service.ConfigureAsync("LDPlayer", request ?? Request(), token ?? Token).GetAwaiter().GetResult();

        private static GameDetectionResult Panel(bool valid = true)
        {
            var evidence = new List<GameDetectionEvidence>();
            if (valid)
            {
                evidence.Add(new GameDetectionEvidence { TemplateId = TemplateId.ResourceSearchPanelAnchor, Found = true, MatchResult = ImageMatchResult.FoundAt(1, 1, 10, 10) });
                evidence.Add(new GameDetectionEvidence { TemplateId = TemplateId.SearchButtonEnabled, Found = true, MatchResult = ImageMatchResult.FoundAt(400, 400, 20, 20) });
            }
            return new GameDetectionResult { State = valid ? GameState.ResourceSearchPanel : GameState.Unknown,
                IsSuccessful = true, Evidence = evidence.AsReadOnly() };
        }

        private static GameDetectionResult PanelFromResourceTab()
        {
            return new GameDetectionResult
            {
                State = GameState.ResourceSearchPanel,
                IsSuccessful = true,
                Evidence = new List<GameDetectionEvidence>
                {
                    new GameDetectionEvidence { TemplateId = TemplateId.ResourceTabUnselected,
                        Found = true, MatchResult = ImageMatchResult.FoundAt(158, 648, 104, 52) },
                    new GameDetectionEvidence { TemplateId = TemplateId.SearchButtonEnabled,
                        Found = true, MatchResult = ImageMatchResult.FoundAt(933, 549, 185, 70) }
                }.AsReadOnly()
            };
        }

        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message} Expected={expected}, Actual={actual}"); }
        private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

        private sealed class Fixture
        { public FakeUi Ui; public FakeClient Client; public FakeNavigation Navigation; public FakeDetector Detector; public FakeRegistry Registry; public IResourceSearchConfigurationService Service; }

        private sealed class FakeUi
        {
            public bool ResourceSelected, FilterChecked, IgnoreResourceTap, InvalidResourceBounds,
                HideLevelValue, AmbiguousResource, LevelRequiresStableChip,
                FilterRequiresStableControl, ResourceRequiresStableIcon, HideUncheckedFilter,
                ResourceRequiresCompactStableIcon, BinaryLevelFallback,
                LevelControlsLoseDirectMatchAfterTap, LevelControlDirectMatchDisabled;
            public bool ResourceTabSelected = true;
            public int Level = 3;
        }

        private sealed class FakeMatcher : IImageMatcher
        {
            private readonly FakeUi ui;
            public FakeMatcher(FakeUi ui) { this.ui = ui; }
            public ImageMatchResult Find(byte[] screenshot, byte[] template, ImageRegion? region = null)
            {
                if (ui.ResourceRequiresCompactStableIcon && IsPng(template))
                {
                    using (var stream = new MemoryStream(template, writable: false))
                    using (var bitmap = new Bitmap(stream))
                        return bitmap.Width == 50 && bitmap.Height == 50 && region.HasValue
                            ? ImageMatchResult.FoundAt(85, 0, 50, 50)
                            : ImageMatchResult.NotFound();
                }
                if (template.Length > 1 && template[0] == 137)
                    return ui.BinaryLevelFallback && IsBinaryMask(template)
                        ? ImageMatchResult.FoundAt(10, 20, 45, 33)
                        : ImageMatchResult.NotFound();
                TemplateId id = (TemplateId)template[0];
                switch (id)
                {
                    case TemplateId.ResourceTabSelected: return Found(ui.ResourceTabSelected, 158, 648, 104, 52);
                    case TemplateId.ResourceTabUnselected: return Found(!ui.ResourceTabSelected, 158, 648, 104, 52);
                    case TemplateId.ResourceIronSelected: return Found(ui.ResourceSelected
                        && (!ui.ResourceRequiresStableIcon || region.HasValue), 10, 10, 20, 30);
                    case TemplateId.ResourceStoneSelected: return Found(ui.ResourceSelected
                        && (!ui.ResourceRequiresStableIcon || region.HasValue), 40, 10, 20, 30);
                    case TemplateId.ResourceWoodSelected: return Found(ui.ResourceSelected
                        && (!ui.ResourceRequiresStableIcon || region.HasValue), 70, 10, 20, 30);
                    case TemplateId.ResourceFoodSelected: return Found(ui.ResourceSelected
                        && (!ui.ResourceRequiresStableIcon || region.HasValue), 100, 10, 20, 30);
                    case TemplateId.ResourceIronUnselected: return ui.InvalidResourceBounds
                        ? ImageMatchResult.FoundAt(10, 10, 0, 0)
                        : Found((!ui.ResourceSelected || ui.AmbiguousResource)
                            && (!ui.ResourceRequiresStableIcon || region.HasValue), 10, 10, 20, 30);
                    case TemplateId.ResourceStoneUnselected: return ui.InvalidResourceBounds
                        ? ImageMatchResult.FoundAt(40, 10, 0, 0)
                        : Found((!ui.ResourceSelected || ui.AmbiguousResource)
                            && (!ui.ResourceRequiresStableIcon || region.HasValue), 40, 10, 20, 30);
                    case TemplateId.ResourceWoodUnselected: return ui.InvalidResourceBounds
                        ? ImageMatchResult.FoundAt(70, 10, 0, 0)
                        : Found((!ui.ResourceSelected || ui.AmbiguousResource)
                            && (!ui.ResourceRequiresStableIcon || region.HasValue), 70, 10, 20, 30);
                    case TemplateId.ResourceFoodUnselected: return ui.InvalidResourceBounds
                        ? ImageMatchResult.FoundAt(100, 10, 0, 0)
                        : Found((!ui.ResourceSelected || ui.AmbiguousResource)
                            && (!ui.ResourceRequiresStableIcon || region.HasValue), 100, 10, 20, 30);
                    case TemplateId.LevelMinusButton: return Found(!ui.LevelControlDirectMatchDisabled || region.HasValue, 100, 100, 20, 20);
                    case TemplateId.LevelPlusButton: return Found(!ui.LevelControlDirectMatchDisabled || region.HasValue, 200, 100, 20, 20);
                    case TemplateId.LevelValue7: return Found(ui.Level == 7 && !ui.HideLevelValue
                        && (!ui.LevelRequiresStableChip || region.HasValue), 150, 50, 20, 20);
                    case TemplateId.LevelValue6: return Found(ui.Level == 6 && !ui.HideLevelValue
                        && (!ui.LevelRequiresStableChip || region.HasValue), 150, 50, 20, 20);
                    case TemplateId.LevelValue5: return Found(ui.Level == 5 && !ui.HideLevelValue
                        && (!ui.LevelRequiresStableChip || region.HasValue), 150, 50, 20, 20);
                    case TemplateId.UnoccupiedFilterChecked: return Found(ui.FilterChecked
                        && (!ui.FilterRequiresStableControl || region.HasValue), 300, 100, 20, 20);
                    case TemplateId.UnoccupiedFilterUnchecked: return Found(!ui.FilterChecked
                        && !ui.HideUncheckedFilter
                        && (!ui.FilterRequiresStableControl || region.HasValue), 300, 100, 20, 20);
                    case TemplateId.SearchButtonEnabled: return ImageMatchResult.FoundAt(400, 400, 20, 20);
                    default: return ImageMatchResult.FoundAt(1, 1, 10, 10);
                }
            }
            private static ImageMatchResult Found(bool value, int x, int y, int width, int height) =>
                value ? ImageMatchResult.FoundAt(x, y, width, height) : ImageMatchResult.NotFound();

            private static bool IsBinaryMask(byte[] png)
            {
                using (var stream = new MemoryStream(png, writable: false))
                using (var bitmap = new Bitmap(stream))
                {
                    for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (pixel.R != pixel.G || pixel.G != pixel.B
                            || (pixel.R != 0 && pixel.R != 255)) return false;
                    }
                    return true;
                }
            }

            private static bool IsPng(byte[] bytes) => bytes != null && bytes.Length > 8
                && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71;
        }

        private sealed class FakeRegistry : ITemplateRegistry
        {
            public TemplateId? Missing; public bool UseImageLevelTemplate, UseImageFoodUnselectedTemplate;
            public TemplateDefinition GetDefinition(TemplateId id) => new TemplateDefinition(id, id + ".png", .8);
            public string GetPath(TemplateId id) => Path.Combine("templates", id + ".png");
            public byte[] LoadBytes(TemplateId id)
            {
                if (id == TemplateId.LevelValue7 && UseImageLevelTemplate) return CreateLevelTemplate();
                if (id == TemplateId.ResourceFoodUnselected && UseImageFoodUnselectedTemplate)
                    return CreateResourceTemplate();
                return new[] { (byte)id };
            }
            public bool Exists(TemplateId id) => Missing != id;

            private static byte[] CreateLevelTemplate()
            {
                using (var bitmap = new Bitmap(125, 33))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var stream = new MemoryStream())
                {
                    graphics.Clear(Color.DarkRed);
                    graphics.DrawString("7", SystemFonts.DefaultFont, Brushes.White, 55, 5);
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }

            private static byte[] CreateResourceTemplate()
            {
                using (var bitmap = new Bitmap(150, 150))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var stream = new MemoryStream())
                {
                    graphics.Clear(Color.DarkSlateGray);
                    graphics.FillEllipse(Brushes.Gold, 40, 40, 70, 70);
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }

        private sealed class FakeNavigation : IWorldMapNavigationService
        {
            private int active;
            public bool Success = true; public int Calls, DelayMs, MaxActive;
            public Task<NavigationResult> EnsureWorldMapAsync(string d, CancellationToken t) => OpenResourceSearchPanelAsync(d, t);
            public async Task<NavigationResult> OpenResourceSearchPanelAsync(string d, CancellationToken t)
            {
                Calls++; int now = Interlocked.Increment(ref active); MaxActive = Math.Max(MaxActive, now);
                try { if (DelayMs > 0) await Task.Delay(DelayMs, t); return new NavigationResult { Success = Success,
                    InitialState = GameState.WorldMap, FinalState = Success ? GameState.ResourceSearchPanel : GameState.WorldMap,
                    FinalEvidence = new GameDetectionEvidence[0], Transitions = new NavigationTransition[0], Message = Success ? "open" : "failed" }; }
                finally { Interlocked.Decrement(ref active); }
            }
        }

        private sealed class FakeDetector : IGameStateDetector
        {
            private int calls; public bool FailOnSecondCall, UseResourceTabEvidence;
            public Task<GameDetectionResult> DetectAsync(string d, CancellationToken t)
            { calls++; return Task.FromResult(UseResourceTabEvidence
                ? PanelFromResourceTab() : Panel(!(FailOnSecondCall && calls >= 2))); }
            public GameDetectionResult Detect(byte[] p) => Panel();
        }

        private sealed class FakeClient : ILdPlayerClient
        {
            private readonly FakeUi ui;
            public readonly List<string> Taps = new List<string>();
            public int ResourceTabTaps, ResourceTaps, MinusTaps, PlusTaps, FilterTaps, SearchTaps, ProhibitedCalls;
            public int TotalInput => Taps.Count + ProhibitedCalls;
            public FakeClient(FakeUi ui) { this.ui = ui; }
            public Task TapAsync(string d, int x, int y, CancellationToken t)
            {
                t.ThrowIfCancellationRequested(); Taps.Add(x + "," + y);
                if (x == 210 && y == 674) { ResourceTabTaps++; ui.ResourceTabSelected = true; }
                else if ((x == 20 || x == 50 || x == 80 || x == 110) && y == 25) { ResourceTaps++; if (!ui.IgnoreResourceTap) ui.ResourceSelected = true; }
                else if (x == 110) { MinusTaps++; ui.Level = Math.Max(1, ui.Level - 1); if (ui.LevelControlsLoseDirectMatchAfterTap) ui.LevelControlDirectMatchDisabled = true; }
                else if (x == 210) { PlusTaps++; ui.Level = Math.Min(7, ui.Level + 1); if (ui.LevelControlsLoseDirectMatchAfterTap) ui.LevelControlDirectMatchDisabled = true; }
                else if (x == 310) { FilterTaps++; ui.FilterChecked = !ui.FilterChecked; }
                else if (x == 408 && y == 362) { FilterTaps++; ui.FilterChecked = !ui.FilterChecked; }
                else if (x == 410) SearchTaps++;
                return Task.CompletedTask;
            }
            public Task<byte[]> CaptureScreenshotPngAsync(string d, CancellationToken t)
            {
                t.ThrowIfCancellationRequested();
                if (!ui.BinaryLevelFallback) return Task.FromResult(new byte[] { 1 });
                using (var bitmap = new Bitmap(1280, 720))
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return Task.FromResult(stream.ToArray());
                }
            }
            public Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<string>>(new[] { "LDPlayer" });
            public Task<bool> IsRunningAsync(string d, CancellationToken t) => Task.FromResult(true);
            public Task OpenAsync(string d, CancellationToken t) => Task.CompletedTask;
            public Task CloseAsync(string d, CancellationToken t) => Task.CompletedTask;
            public Task RunAppAsync(string d, string p, CancellationToken t) => Task.CompletedTask;
            public Task BackAsync(string d, CancellationToken t) { ProhibitedCalls++; return Task.CompletedTask; }
            public Task TapByPercentAsync(string d, double x, double y, CancellationToken t) { ProhibitedCalls++; return Task.CompletedTask; }
            public Task LongPressAsync(string d, int x, int y, int ms, CancellationToken t) { ProhibitedCalls++; return Task.CompletedTask; }
            public Task SwipeByPercentAsync(string d, double sx, double sy, double ex, double ey, int ms, CancellationToken t) { ProhibitedCalls++; return Task.CompletedTask; }
            public Task InputTextAsync(string d, string s, CancellationToken t) { ProhibitedCalls++; return Task.CompletedTask; }
            public Task PressKeyAsync(string d, AndroidKeyCode k, CancellationToken t) { ProhibitedCalls++; return Task.CompletedTask; }
        }

        private sealed class FakeLogger : IDiagnosticLogger
        { public void Info(string message) { } public void Error(string message, Exception exception) { } }
    }
}
