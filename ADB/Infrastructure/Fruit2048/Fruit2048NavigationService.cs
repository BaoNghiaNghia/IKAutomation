using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    /// <summary>Focused City → Festival → Fruit 2048 board navigation; it owns no gameplay state.</summary>
    public sealed class Fruit2048NavigationService : IFruit2048NavigationService
    {
        private static readonly int[] Waits = { 500, 1000, 1600 };
        private readonly ILdPlayerClient player;
        private readonly IFrameCapturingLdPlayerClient frames;
        private readonly IFrameImageMatcher matcher;
        private readonly Fruit2048TemplateCatalog templates;
        private readonly Fruit2048ScreenProfile profile;
        private readonly IDiagnosticLogger logger;

        public Fruit2048NavigationService(ILdPlayerClient player, IFrameCapturingLdPlayerClient frames,
            IFrameImageMatcher matcher, Fruit2048TemplateCatalog templates,
            Fruit2048ScreenProfile profile, IDiagnosticLogger logger)
        {
            this.player = player; this.frames = frames; this.matcher = matcher;
            this.templates = templates; this.profile = profile; this.logger = logger;
        }

        public async Task<Fruit2048NavigationResult> EnsureFruit2048ScreenAsync(string deviceName,
            IProgress<string> status, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                Observation observed = await ObserveAsync(deviceName, cancellationToken);
                if (observed.State == Fruit2048NavigationState.BoardReady) return Ready();
                if (observed.State == Fruit2048NavigationState.CaptureUnavailable)
                    return Failed(observed.Error);
                if (observed.State == Fruit2048NavigationState.FruitFestivalTabVisible)
                {
                    status?.Report("Đang mở Lễ Hội Trái Cây");
                    if (!await TapAsync(deviceName, observed, "TapFruit2048Tab", attempt, cancellationToken)) continue;
                    if (await WaitForAsync(deviceName, Fruit2048NavigationState.BoardReady, attempt, cancellationToken)) return Ready();
                    continue;
                }
                if (observed.State == Fruit2048NavigationState.CityFestivalEntryVisible)
                {
                    status?.Report("Đang mở sự kiện");
                    if (!await TapAsync(deviceName, observed, "TapEventEntry", attempt, cancellationToken)) continue;
                    if (!await WaitForAsync(deviceName, Fruit2048NavigationState.FruitFestivalTabVisible, attempt, cancellationToken)) continue;
                    Observation tab = await ObserveAsync(deviceName, cancellationToken);
                    if (tab.State != Fruit2048NavigationState.FruitFestivalTabVisible) continue;
                    status?.Report("Đang mở Lễ Hội Trái Cây");
                    if (!await TapAsync(deviceName, tab, "TapFruit2048Tab", attempt, cancellationToken)) continue;
                    if (await WaitForAsync(deviceName, Fruit2048NavigationState.BoardReady, attempt, cancellationToken)) return Ready();
                }
            }
            status?.Report("Không thể mở Lễ Hội Trái Cây");
            return Failed("Không thể mở Lễ Hội Trái Cây.");
        }

        private async Task<bool> WaitForAsync(string deviceName, Fruit2048NavigationState expected,
            int attempt, CancellationToken cancellationToken)
        {
            foreach (int wait in Waits)
            {
                await Task.Delay(wait, cancellationToken);
                Observation observation = await ObserveAsync(deviceName, cancellationToken);
                if (observation.State == expected) { Log(deviceName, attempt, observation, "Wait", 0, "Ready", wait); return true; }
                if (observation.State == Fruit2048NavigationState.CaptureUnavailable) return false;
            }
            return false;
        }

        private async Task<Observation> ObserveAsync(string deviceName, CancellationToken cancellationToken)
        {
            try
            {
                using (CapturedFrame frame = await frames.CaptureFrameAsync(deviceName, cancellationToken))
                {
                    byte[] board, tab, city;
                    if (!templates.TryGet(Fruit2048TemplateCatalog.NavigationBoardAnchor, out board)
                        || !templates.TryGet(Fruit2048TemplateCatalog.Fruit2048Tab, out tab)
                        || !templates.TryGet(Fruit2048TemplateCatalog.CityFestivalEntry, out city))
                        return new Observation { State = Fruit2048NavigationState.CaptureUnavailable, Error = "Thiếu template điều hướng Fruit2048." };
                    ImageMatchResult match = matcher.Find(frame, board, profile.Scale(profile.BoardRegion, frame.Width, frame.Height));
                    if (match.Found) return new Observation { State = Fruit2048NavigationState.BoardReady, Match = match, Anchor = Fruit2048TemplateCatalog.NavigationBoardAnchor };
                    match = matcher.Find(frame, tab, profile.Scale(profile.FruitFestivalTabRegion, frame.Width, frame.Height));
                    if (match.Found) return new Observation { State = Fruit2048NavigationState.FruitFestivalTabVisible, Match = match, Anchor = Fruit2048TemplateCatalog.Fruit2048Tab };
                    match = matcher.Find(frame, city, profile.Scale(profile.CityFestivalEntryRegion, frame.Width, frame.Height));
                    return match.Found
                        ? new Observation { State = Fruit2048NavigationState.CityFestivalEntryVisible, Match = match, Anchor = Fruit2048TemplateCatalog.CityFestivalEntry }
                        : new Observation { State = Fruit2048NavigationState.Unknown };
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { return new Observation { State = Fruit2048NavigationState.CaptureUnavailable, Error = exception.Message }; }
        }

        private async Task<bool> TapAsync(string deviceName, Observation observation, string action, int attempt, CancellationToken cancellationToken)
        {
            if (observation.Match == null || !observation.Match.Found) return false;
            await player.TapAsync(deviceName, observation.Match.CenterX, observation.Match.CenterY, cancellationToken);
            Log(deviceName, attempt, observation, action, observation.Match.CenterX, "Tapped", 0);
            return true;
        }
        private static Fruit2048NavigationResult Ready() => new Fruit2048NavigationResult { Success = true, State = Fruit2048NavigationState.BoardReady };
        private static Fruit2048NavigationResult Failed(string error) => new Fruit2048NavigationResult { Success = false, State = Fruit2048NavigationState.Unknown, Error = error };
        private void Log(string device, int attempt, Observation observation, string action, int tapX, string outcome, int wait) => logger?.Info(
            $"[Fruit2048 Navigation] DeviceName='{device}', Attempt={attempt}, DetectedState='{observation.State}', Anchor='{observation.Anchor}', Score='{observation.Match?.Confidence:F3}', MatchedBounds='{Bounds(observation.Match)}', Action='{action}', TapX={tapX}, TapY={observation.Match?.CenterY ?? 0}, WaitMs={wait}, Outcome='{outcome}'");
        private static string Bounds(ImageMatchResult match) => match == null || !match.Found ? string.Empty
            : match.X + "," + match.Y + "," + match.Width + "," + match.Height;
        private sealed class Observation { public Fruit2048NavigationState State; public ImageMatchResult Match; public string Anchor; public string Error; }
    }
}
