using IK_Auto_ADB.Core.Abstractions;
using IK_Auto_ADB.Core.Concurrency;
using IK_Auto_ADB.Core.Diagnostics;
using IK_Auto_ADB.Core.GameDetection;
using IK_Auto_ADB.Core.Navigation;
using IK_Auto_ADB.Core.TeamSelection;
using IK_Auto_ADB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Infrastructure.TeamSelection
{
    public sealed class WorldMapTeamAvailabilityService : IWorldMapTeamAvailabilityService
    {
        private readonly IWorldMapNavigationService navigation;
        private readonly ILdPlayerClient client;
        private readonly ITemplateRegistry registry;
        private readonly IImageMatcher matcher;
        private readonly IDeviceOperationLock operationLock;
        private readonly WorldMapTeamAvailabilityOptions options;
        private readonly IDiagnosticLogger logger;
        private readonly ConcurrentDictionary<string, RosterKnowledge> knownRosters =
            new ConcurrentDictionary<string, RosterKnowledge>(StringComparer.OrdinalIgnoreCase);

        public WorldMapTeamAvailabilityService(IWorldMapNavigationService navigation,
            IGameStateDetector detector, ILdPlayerClient client,
            ITemplateRegistry registry, IImageMatcher matcher,
            IDeviceOperationLock operationLock, WorldMapTeamAvailabilityOptions options,
            IDiagnosticLogger logger)
        {
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            if (detector == null) throw new ArgumentNullException(nameof(detector));
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            this.operationLock = operationLock ?? throw new ArgumentNullException(nameof(operationLock));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<WorldMapTeamAvailabilityResult> CheckAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("LDPlayer device name is required.", nameof(deviceName));

            TemplateId[] requiredTemplates =
            {
                TemplateId.WorldMapTeamReadyAnchor,
                TemplateId.WorldMapAnchor,
                TemplateId.Team1Badge,
                TemplateId.Team2Badge,
                TemplateId.Team3Badge,
                TemplateId.Team4Badge
            };
            TemplateId? missingTemplate = requiredTemplates
                .Where(template => !registry.Exists(template))
                .Select(template => (TemplateId?)template)
                .FirstOrDefault();
            if (missingTemplate.HasValue)
            {
                string path = registry.GetPath(missingTemplate.Value);
                return Failed($"Required template '{missingTemplate.Value}' was not found at '{path}'.");
            }

            string normalizedDeviceName = deviceName.Trim();

            // A numbered roster row or its ready label is a stable, focused WorldMap
            // anchor.  Do this inexpensive read first: falling through to
            // EnsureWorldMapAsync on every roster refresh can invoke the global
            // detector when WorldMapAnchor is temporarily missed, which blocks all
            // of the other devices behind expensive unrelated template checks.
            bool focusedRosterVisible = await operationLock.RunAsync(normalizedDeviceName,
                token => HasFocusedRosterEvidenceAsync(normalizedDeviceName, token),
                cancellationToken);
            if (!focusedRosterVisible)
            {
                NavigationResult navigationResult = await navigation.EnsureWorldMapAsync(
                    normalizedDeviceName, cancellationToken);
                if (!navigationResult.Success || navigationResult.FinalState != GameState.WorldMap)
                {
                    return Failed("WorldMap could not be verified before checking team availability.",
                        navigationResult.ErrorMessage ?? navigationResult.Message,
                        navigationResult.FinalState);
                }
            }

            return await operationLock.RunAsync(normalizedDeviceName,
                token => CheckCoreAsync(normalizedDeviceName, focusedRosterVisible, token), cancellationToken);
        }

        private async Task<bool> HasFocusedRosterEvidenceAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            using (CapturedFrame screenshot = await CaptureFrameAsync(deviceName, cancellationToken))
            {
                WorldMapTeamRosterLayout layout = WorldMapTeamRosterLayoutResolver.Resolve(
                    screenshot.Width, screenshot.Height, options);
                if (layout == null)
                    return false;
                ImageRegion rosterRegion = new ImageRegion(layout.Rows[0].X, layout.Rows[0].Y,
                    layout.Rows[0].Width, layout.Rows.Sum(row => row.Height));

                TemplateId[] badgeTemplates =
                {
                    TemplateId.Team1Badge,
                    TemplateId.Team2Badge,
                    TemplateId.Team3Badge,
                    TemplateId.Team4Badge
                };
                var requests = new List<ImageMatchRequest>();
                for (int index = 0; index < badgeTemplates.Length; index++)
                {
                    requests.Add(new ImageMatchRequest(registry.LoadBytes(badgeTemplates[index]),
                        layout.SearchRows[index]));
                }
                requests.Add(new ImageMatchRequest(
                    registry.LoadBytes(TemplateId.WorldMapTeamReadyAnchor),
                    rosterRegion));

                IReadOnlyList<ImageMatchResult> results = await FindManyAsync(
                    screenshot, requests, cancellationToken);
                bool numberedRowFound = results.Take(badgeTemplates.Length)
                    .Where((match, index) => IsMatchInsideRow(match, layout.Rows[index]))
                    .Any(match => match != null && match.Found);
                bool readyLabelFound = results.Count > badgeTemplates.Length
                    && IsMatchInsideRegion(results[badgeTemplates.Length], rosterRegion);
                bool confirmed = numberedRowFound || readyLabelFound;

                logger.Info($"[WorldMap Team Roster Preflight] DeviceName='{deviceName}', "
                    + $"FocusedRosterVisible={confirmed}, NumberedRowFound={numberedRowFound}, "
                    + $"ReadyLabelFound={readyLabelFound}, "
                    + $"Region=({rosterRegion.X},{rosterRegion.Y},"
                    + $"{rosterRegion.Width},{rosterRegion.Height}), "
                    + $"NextAction='{(confirmed ? "FocusedRosterScan" : "EnsureWorldMap")}'");
                return confirmed;
            }
        }

        public void ClearKnownRoster(string deviceName = null)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                knownRosters.Clear();
                return;
            }
            knownRosters.TryRemove(deviceName.Trim(), out RosterKnowledge ignored);
        }

        private async Task<WorldMapTeamAvailabilityResult> CheckCoreAsync(string deviceName,
            bool focusedWorldMapPreflightConfirmed, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TeamNumber[] teams =
            {
                TeamNumber.Team1, TeamNumber.Team2, TeamNumber.Team3, TeamNumber.Team4
            };
            var badgeMatches = new Dictionary<TeamNumber, ImageMatchResult>();
            var readyMatchesByTeam = new Dictionary<TeamNumber, ImageMatchResult>();
            var busyTeamsFresh = new HashSet<TeamNumber>();
            var lockedTeamsFresh = new HashSet<TeamNumber>();
            var rowEvidenceTeams = new HashSet<TeamNumber>();
            int verifiedFrameCount = 0;
            int focusedWorldMapFrameCount = 0;
            int focusedWorldMapAnchorFallbackCount = 0;
            bool earlyCompleted = false;
            GameDetectionResult lastState = null;
            WorldMapTeamRosterLayout lastLayout = null;
            for (int frame = 0; frame < options.ObservationFrameCount; frame++)
            {
                if (frame > 0)
                    await Task.Delay(options.ObservationIntervalMs, cancellationToken);

                using (CapturedFrame screenshot = await CaptureFrameAsync(deviceName, cancellationToken))
                {
                WorldMapTeamRosterLayout layout = WorldMapTeamRosterLayoutResolver.Resolve(
                    screenshot.Width, screenshot.Height, options);
                if (layout == null)
                    return Failed("Team roster region falls outside the captured frame.",
                        state: GameState.WorldMap);
                var badgeRequests = teams.Select((team, index) => new ImageMatchRequest(
                    registry.LoadBytes(BadgeTemplate(team)), layout.SearchRows[index])).ToArray();
                IReadOnlyList<ImageMatchResult> badgeResults = await FindManyAsync(
                    screenshot, badgeRequests, cancellationToken);
                var frameBadgeMatches = new Dictionary<TeamNumber, ImageMatchResult>();
                var frameReadyMatchesByTeam = new Dictionary<TeamNumber, ImageMatchResult>();
                var frameBusyTeams = new HashSet<TeamNumber>();
                var frameLockedTeams = new HashSet<TeamNumber>();
                var frameRowEvidenceTeams = new HashSet<TeamNumber>();
                for (int index = 0; index < teams.Length; index++)
                {
                    TeamNumber team = teams[index];
                    ImageMatchResult badgeMatch = badgeResults[index] ?? ImageMatchResult.NotFound();
                    if (IsMatchInsideRow(badgeMatch, layout.Rows[index]))
                    {
                        frameBadgeMatches[team] = badgeMatch;
                        frameRowEvidenceTeams.Add(team);
                    }
                }
                layout = WorldMapTeamRosterLayoutResolver.AlignToNumberedBadges(
                    layout, frameBadgeMatches, screenshot.Height, options);
                lastLayout = layout;

                var statusRequests = new List<ImageMatchRequest>();
                var statusSignals = new List<Tuple<TeamNumber, string>>();
                for (int index = 0; index < teams.Length; index++)
                {
                    TeamNumber team = teams[index];
                    ImageRegion rowRegion = layout.SearchRows[index];
                    AddStatusRequest(statusRequests, statusSignals, team, "Ready",
                        TemplateId.WorldMapTeamReadyAnchor, rowRegion);
                    AddStatusRequest(statusRequests, statusSignals, team, "Locked",
                        TemplateId.TeamDisabledAnchor, rowRegion);
                    // A confirmed, unlocked row without the focused Ready label is
                    // already treated as Busy below. Matching the optional Busy and
                    // Timer artwork for every row duplicates that decision and is
                    // particularly expensive because the native matcher is
                    // serialized across devices. Keep the explicit Locked check:
                    // it is the only negative signal that changes roster membership.
                }
                IReadOnlyList<ImageMatchResult> statusResults = statusRequests.Count == 0
                    ? new ImageMatchResult[0] : await FindManyAsync(
                        screenshot, statusRequests, cancellationToken);
                for (int index = 0; index < statusResults.Count; index++)
                {
                    TeamNumber requestedTeam = statusSignals[index].Item1;
                    string signal = statusSignals[index].Item2;
                    ImageMatchResult rowMatch = statusResults[index] ?? ImageMatchResult.NotFound();
                    TeamNumber team = signal == "Ready"
                        ? ResolveStatusTeam(requestedTeam, rowMatch, frameBadgeMatches, layout)
                        : requestedTeam;
                    if (IsMatchInsideRow(rowMatch, layout.Rows[(int)team - 1]))
                    {
                        if (signal == "Ready") frameReadyMatchesByTeam[team] = rowMatch;
                        else if (signal == "Locked") frameLockedTeams.Add(team);
                        else frameBusyTeams.Add(team);
                        frameRowEvidenceTeams.Add(team);
                    }
                }

                // The preflight may already have proved this is the WorldMap from a fresh
                // roster frame. Numbered rows and row-local status anchors are focused
                // WorldMap evidence, so a transiently blank follow-up roster frame must not
                // force the global detector back through every unrelated game template.
                bool focusedWorldMapEvidence = frameRowEvidenceTeams.Count > 0;
                if (!focusedWorldMapEvidence)
                {
                    if (focusedWorldMapPreflightConfirmed)
                    {
                        lastState = new GameDetectionResult
                        {
                            State = GameState.WorldMap,
                            IsSuccessful = true,
                            Evidence = new GameDetectionEvidence[0]
                        };
                    }
                    else
                    {
                        focusedWorldMapAnchorFallbackCount++;
                        if (!await HasFocusedWorldMapAnchorAsync(screenshot,
                            cancellationToken))
                            break;
                        lastState = new GameDetectionResult
                        {
                            State = GameState.WorldMap,
                            IsSuccessful = true,
                            Evidence = new GameDetectionEvidence[0]
                        };
                    }

                    // The focused map-anchor fallback proves this is still the map,
                    // but it produced no roster evidence. Repeating more roster
                    // frames cannot make a safe team decision, so return the
                    // existing cached/uncertain roster promptly.
                    verifiedFrameCount++;
                    earlyCompleted = true;
                    break;
                }
                else
                {
                    focusedWorldMapFrameCount++;
                    lastState = new GameDetectionResult
                    {
                        State = GameState.WorldMap,
                        IsSuccessful = true,
                        Evidence = new GameDetectionEvidence[0]
                    };
                }

                verifiedFrameCount++;
                foreach (KeyValuePair<TeamNumber, ImageMatchResult> item in frameBadgeMatches)
                    badgeMatches[item.Key] = item.Value;
                foreach (KeyValuePair<TeamNumber, ImageMatchResult> item in frameReadyMatchesByTeam)
                    readyMatchesByTeam[item.Key] = item.Value;
                busyTeamsFresh.UnionWith(frameBusyTeams);
                lockedTeamsFresh.UnionWith(frameLockedTeams);
                rowEvidenceTeams.UnionWith(frameRowEvidenceTeams);

                // A fresh Ready label is the information the scheduler needs for
                // the next state transition. Its row (and all preceding rows) is
                // retained as fresh roster evidence below, so waiting for all four
                // optional rows only delays other devices in the preflight queue.
                bool readyFrameEvidence = frameReadyMatchesByTeam.Count > 0;

                // Keep this complete-evidence condition for callers that opt into
                // more than one observation frame, but the normal configuration
                // uses one current frame rather than three stale duplicates.
                bool completeFrameEvidence = teams.All(team =>
                    frameReadyMatchesByTeam.ContainsKey(team)
                    || frameBusyTeams.Contains(team)
                    || frameLockedTeams.Contains(team));
                if (readyFrameEvidence || completeFrameEvidence)
                {
                    earlyCompleted = true;
                    break;
                }
                }
            }

            if (verifiedFrameCount == 0)
                return Failed("Fresh screenshots were not verified as WorldMap; "
                    + "readiness was not inferred.", lastState?.ErrorMessage,
                    lastState?.State ?? GameState.Unknown);

            var freshExisting = new HashSet<TeamNumber>(rowEvidenceTeams);
            freshExisting.ExceptWith(lockedTeamsFresh);
            // A positively identified numbered row or row-local status establishes
            // the roster size. Accounts unlock rows sequentially, so a confirmed
            // Team3 row also proves Team1 and Team2 exist. This fills only preceding
            // rows; it never remaps one row's evidence to another team identity.
            int highestConfirmedRow = freshExisting.Select(team => (int)team)
                .DefaultIfEmpty(0).Max();
            for (int number = 1; number <= highestConfirmedRow; number++)
            {
                TeamNumber precedingTeam = (TeamNumber)number;
                if (!lockedTeamsFresh.Contains(precedingTeam))
                    freshExisting.Add(precedingTeam);
            }
            TeamRosterEvidenceSource freshSource = badgeMatches.Count > 0
                ? TeamRosterEvidenceSource.FreshBadges
                : freshExisting.Count > 0
                    ? TeamRosterEvidenceSource.FreshRowEvidence
                    : TeamRosterEvidenceSource.Unknown;
            RosterKnowledge previousKnowledge;
            knownRosters.TryGetValue(deviceName, out previousKnowledge);
            int previousKnownCount = previousKnowledge?.ActiveTeams.Count ?? 0;
            if (freshExisting.Count > 0)
            {
                knownRosters.AddOrUpdate(deviceName,
                    new RosterKnowledge(freshExisting, DateTimeOffset.UtcNow, freshSource),
                    (_, known) => UpdateKnowledge(known, freshExisting,
                        lockedTeamsFresh, freshSource));
            }
            RosterKnowledge currentKnowledge;
            knownRosters.TryGetValue(deviceName, out currentKnowledge);
            int configuredOverride = options.GetKnownUnlockedTeamCount(deviceName);
            bool useCached = currentKnowledge != null
                && freshExisting.Count < currentKnowledge.ActiveTeams.Count;
            // A row-local lock is strong contradictory evidence, but a known roster
            // is only downgraded after the cache confirmation policy accepts it.
            // Until then it remains diagnostic evidence, not an immediate removal.
            var effectiveLockedTeams = useCached
                ? new HashSet<TeamNumber>() : lockedTeamsFresh;
            HashSet<TeamNumber> existing = configuredOverride > 0
                ? new HashSet<TeamNumber>(teams.Where(team => (int)team <= configuredOverride))
                : useCached || freshExisting.Count == 0
                ? new HashSet<TeamNumber>(currentKnowledge?.ActiveTeams
                    ?? Enumerable.Empty<TeamNumber>())
                : freshExisting;
            TeamRosterEvidenceSource rosterSource = configuredOverride > 0
                ? TeamRosterEvidenceSource.FreshRowEvidence
                : useCached || (freshExisting.Count == 0
                    && existing.Count > 0)
                ? TeamRosterEvidenceSource.CachedKnownCount : freshSource;
            TeamRosterClassification classification = configuredOverride > 0
                ? TeamRosterClassification.FreshConfirmed
                : rosterSource
                    == TeamRosterEvidenceSource.CachedKnownCount
                ? TeamRosterClassification.CachedConfirmed
                : freshExisting.Count > 0
                ? freshExisting.Count == 1 && badgeMatches.ContainsKey(TeamNumber.Team1)
                    ? TeamRosterClassification.ExplicitSingleTeam
                    : TeamRosterClassification.FreshConfirmed
                : TeamRosterClassification.Uncertain;

            var lockedTeams = effectiveLockedTeams.OrderBy(team => (int)team).ToList();
            var availableTeams = existing.Where(team => !effectiveLockedTeams.Contains(team))
                .OrderBy(team => (int)team).ToList();
            var readyTeams = readyMatchesByTeam.Keys.Where(team => existing.Contains(team)
                    && !effectiveLockedTeams.Contains(team)
                    && !busyTeamsFresh.Contains(team))
                .OrderBy(team => (int)team).ToList();
            // An unlocked existing row that is not freshly Ready is busy for the
            // scheduler, even when the optional busy/timer anchor is obscured.
            var busyTeams = availableTeams.Where(team => !readyTeams.Contains(team))
                .OrderBy(team => (int)team).ToList();
            var readyMatches = readyTeams.Select(team => readyMatchesByTeam[team]).ToList();
            var rowObservations = teams.Select(team => new TeamRowObservation
            {
                Team = team,
                BadgeFound = badgeMatches.ContainsKey(team),
                BadgeBounds = badgeMatches.TryGetValue(team, out ImageMatchResult badge)
                    ? new ImageRegion(badge.X, badge.Y, badge.Width, badge.Height) : default(ImageRegion),
                ReadyBounds = readyMatchesByTeam.TryGetValue(team, out ImageMatchResult readyMatch)
                    ? new ImageRegion(readyMatch.X, readyMatch.Y, readyMatch.Width, readyMatch.Height)
                    : default(ImageRegion),
                RowBounds = lastLayout?.Rows[(int)team - 1] ?? default(ImageRegion),
                Exists = availableTeams.Contains(team),
                IsVisible = rowEvidenceTeams.Contains(team),
                IsReady = readyTeams.Contains(team),
                IsBusy = busyTeams.Contains(team),
                IsLocked = lockedTeams.Contains(team),
                State = lockedTeams.Contains(team) ? TeamRowState.Locked
                    : readyTeams.Contains(team) ? TeamRowState.Ready
                    : busyTeams.Contains(team) ? TeamRowState.Busy
                    : existing.Contains(team) ? TeamRowState.Unknown : TeamRowState.Missing,
                EvidenceSource = lockedTeams.Contains(team) ? "LockedAnchor"
                    : badgeMatches.ContainsKey(team) ? "NumberedBadge"
                    : readyTeams.Contains(team) ? "ReadyLabel"
                    : busyTeamsFresh.Contains(team) ? "BusyStructure"
                    : freshExisting.Contains(team) ? "InferredPrecedingRow"
                    : existing.Contains(team) ? "CachedConfirmed" : "None",
                EvidenceStrength = badgeMatches.ContainsKey(team) || lockedTeams.Contains(team)
                    ? "Strong" : rowEvidenceTeams.Contains(team) ? "Moderate"
                    : freshExisting.Contains(team) ? "Inferred"
                    : existing.Contains(team) ? "Cached" : "None"
            }).ToArray();
            IReadOnlyDictionary<TeamNumber, TeamRowObservation> rowsByTeam =
                rowObservations.ToDictionary(row => row.Team, row => row);

            ImageMatchResult match = readyMatches.FirstOrDefault()
                ?? ImageMatchResult.NotFound();
            bool ready = readyTeams.Count > 0;
            string resultSource = configuredOverride > 0
                ? "ConfiguredOverride"
                : useCached && freshExisting.Count > 0 ? "FreshPlusCached"
                : classification.ToString();
            ImageRegion resolvedRoster = lastLayout == null
                ? options.TeamRosterRegion
                : new ImageRegion(lastLayout.Rows[0].X, lastLayout.Rows[0].Y,
                    lastLayout.Rows[0].Width,
                    lastLayout.Rows.Sum(row => row.Height));
            logger.Info($"[WorldMap Team Roster] DeviceName='{deviceName}', "
                + $"ObservationFrames={verifiedFrameCount}/{options.ObservationFrameCount}, "
                + $"FocusedWorldMapFrames={focusedWorldMapFrameCount}, "
                + $"FocusedWorldMapAnchorFallbacks={focusedWorldMapAnchorFallbackCount}, "
                + $"EarlyCompletion={earlyCompleted}, "
                + $"Ready={ready}, ReadyTeams='{string.Join(",", readyTeams)}', "
                + $"AvailableTeams='{string.Join(",", availableTeams)}', "
                + $"BusyTeams='{string.Join(",", busyTeams)}', LockedTeams='{string.Join(",", lockedTeams)}', "
                + $"Team1Exists={existing.Contains(TeamNumber.Team1)}, "
                + $"Team2Exists={existing.Contains(TeamNumber.Team2)}, "
                + $"Team3Exists={existing.Contains(TeamNumber.Team3)}, "
                + $"Team4Exists={existing.Contains(TeamNumber.Team4)}, "
                + $"Team4Locked={lockedTeams.Contains(TeamNumber.Team4)}, "
                + $"Bounds=({match.X},{match.Y},{match.Width},{match.Height}), "
                + $"ConfiguredRegion=({options.TeamRosterRegion.X},{options.TeamRosterRegion.Y},"
                + $"{options.TeamRosterRegion.Width},{options.TeamRosterRegion.Height}), "
                + $"ResolvedRegion=({resolvedRoster.X},{resolvedRoster.Y},"
                + $"{resolvedRoster.Width},{resolvedRoster.Height}), "
                + $"ResolvedRowHeight={lastLayout?.Rows[0].Height ?? options.TeamRowHeight}, FreshRosterCount={freshExisting.Count}, "
                + $"FreshConfirmedTeams='{string.Join(",", freshExisting.OrderBy(team => (int)team))}', "
                + $"CachedConfirmedTeams='{string.Join(",", previousKnowledge?.ActiveTeams.OrderBy(team => (int)team) ?? Enumerable.Empty<TeamNumber>())}', "
                + $"PreviousKnownRosterCount={previousKnownCount}, "
                + $"ConfiguredOverride={configuredOverride}, "
                + $"RosterStatus='{classification}', RosterSource='{resultSource}', "
                + $"RosterConfidence='{(classification == TeamRosterClassification.Uncertain ? "Uncertain" : currentKnowledge?.EvidenceStrength ?? "Fresh")}', "
                + $"{RowLog(rowObservations, TeamNumber.Team1)}, {RowLog(rowObservations, TeamNumber.Team2)}, "
                + $"{RowLog(rowObservations, TeamNumber.Team3)}, {RowLog(rowObservations, TeamNumber.Team4)}, Cancellation=false");
            DateTimeOffset rosterCapturedAt = DateTimeOffset.UtcNow;
            bool isFreshRoster = freshExisting.Count > 0;
            return new WorldMapTeamAvailabilityResult
            {
                RosterScanId = Guid.NewGuid(),
                RosterCapturedAt = rosterCapturedAt,
                IsFresh = isFreshRoster,
                Success = true,
                AnyReadyTeam = ready,
                AvailableTeams = availableTeams.AsReadOnly(),
                ExistingTeams = availableTeams.AsReadOnly(),
                ReadyTeams = readyTeams.AsReadOnly(),
                BusyTeams = busyTeams.AsReadOnly(),
                LockedTeams = lockedTeams.AsReadOnly(),
                RowObservations = rowObservations,
                TeamRows = rowsByTeam,
                ConfirmedRosterCount = availableTeams.Count,
                FinalState = GameState.WorldMap,
                ReadyMatch = match,
                ReadyMatches = readyMatches.AsReadOnly(),
                RosterEvidenceSource = rosterSource,
                RosterClassification = classification,
                IsRosterUncertain = rosterSource == TeamRosterEvidenceSource.Unknown,
                RosterSource = resultSource,
                RosterStatus = classification.ToString(),
                RosterConfidence = classification == TeamRosterClassification.Uncertain
                    ? "Uncertain" : currentKnowledge?.EvidenceStrength ?? "Fresh",
                Message = ready
                    ? $"Detected {availableTeams.Count} team(s); ready teams: "
                        + $"{string.Join(", ", readyTeams)}."
                    : $"Detected {availableTeams.Count} team(s); no team is ready."
            };
        }

        private void AddStatusRequest(ICollection<ImageMatchRequest> requests,
            ICollection<Tuple<TeamNumber, string>> signals, TeamNumber team,
            string signal, TemplateId template, ImageRegion region)
        {
            if (!registry.Exists(template)) return;
            requests.Add(new ImageMatchRequest(registry.LoadBytes(template), region));
            signals.Add(Tuple.Create(team, signal));
        }

        private static RosterKnowledge UpdateKnowledge(RosterKnowledge known,
            HashSet<TeamNumber> fresh, HashSet<TeamNumber> locked,
            TeamRosterEvidenceSource source)
        {
            if (fresh.Count > known.ActiveTeams.Count)
                return new RosterKnowledge(fresh, DateTimeOffset.UtcNow, source);
            if (fresh.SetEquals(known.ActiveTeams))
                return new RosterKnowledge(fresh, DateTimeOffset.UtcNow, source);
            if (fresh.Count >= known.ActiveTeams.Count) return known;

            TeamNumber[] missing = known.ActiveTeams.Where(team => !fresh.Contains(team)).ToArray();
            bool strongContradiction = missing.Length > 0
                && missing.All(locked.Contains);
            if (!strongContradiction) return known;
            int confirmations = known.PendingActiveTeams != null
                    && known.PendingActiveTeams.SetEquals(fresh)
                ? known.StrongContradictionConfirmations + 1 : 1;
            return confirmations >= 3
                ? new RosterKnowledge(fresh, DateTimeOffset.UtcNow, source)
                : new RosterKnowledge(known.ActiveTeams, known.LastConfirmedAt,
                    known.EvidenceSource, fresh, confirmations);
        }

        private async Task<CapturedFrame> CaptureFrameAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            var frameClient = client as IFrameCapturingLdPlayerClient;
            if (frameClient != null)
                return await frameClient.CaptureFrameAsync(deviceName, cancellationToken);

            byte[] png = await client.CaptureScreenshotPngAsync(deviceName, cancellationToken);
            using (var stream = new MemoryStream(png, writable: false))
            using (var source = new Bitmap(stream))
                return new CapturedFrame(new Bitmap(source), DateTimeOffset.UtcNow);
        }

        private async Task<bool> HasFocusedWorldMapAnchorAsync(CapturedFrame frame,
            CancellationToken cancellationToken)
        {
            int top = frame.Height / 2;
            ImageRegion region = new ImageRegion(0, top, frame.Width / 2,
                Math.Max(1, frame.Height - top));
            var requests = new[]
            {
                new ImageMatchRequest(registry.LoadBytes(TemplateId.WorldMapAnchor), region)
            };
            IReadOnlyList<ImageMatchResult> matches = await FindManyAsync(frame, requests,
                cancellationToken);
            return matches.Count == 1 && IsMatchInsideRegion(matches[0], region);
        }

        private async Task<IReadOnlyList<ImageMatchResult>> FindManyAsync(CapturedFrame frame,
            IReadOnlyList<ImageMatchRequest> requests, CancellationToken cancellationToken)
        {
            var asyncMatcher = matcher as IAsyncFrameImageMatcher;
            if (asyncMatcher != null)
                return await asyncMatcher.FindManyAsync(frame, requests, cancellationToken);
            var frameMatcher = matcher as IFrameImageMatcher;
            if (frameMatcher != null) return frameMatcher.FindMany(frame, requests);
            var batchMatcher = matcher as IBatchImageMatcher;
            if (batchMatcher != null) return batchMatcher.FindMany(frame.GetPngBytes(), requests);
            return requests.Select(request => matcher.Find(frame.GetPngBytes(),
                request.TemplatePng, request.SearchRegion)).ToArray();
        }

        private int EstimateRosterTop(
            IReadOnlyDictionary<TeamNumber, ImageMatchResult> badges,
            int rowHeight)
        {
            int badgeTopPadding = options.BadgeTopPadding;
            if (badges == null || badges.Count == 0)
                return options.TeamRosterRegion.Y + badgeTopPadding;

            int[] candidates = badges
                .Select(item => item.Value.Y - badgeTopPadding
                    - (((int)item.Key - 1) * rowHeight))
                .OrderBy(value => value)
                .ToArray();
            int estimated = candidates[candidates.Length / 2];
            int maximum = options.TeamRosterRegion.Y
                + options.TeamRosterRegion.Height - rowHeight;
            return Math.Max(options.TeamRosterRegion.Y,
                Math.Min(maximum, estimated));
        }

        private ImageRegion RosterRowRegion(int requestedTop, int rowHeight)
        {
            int rowTop = Math.Max(options.TeamRosterRegion.Y, requestedTop);
            int rowBottom = Math.Min(options.TeamRosterRegion.Y
                + options.TeamRosterRegion.Height, rowTop + rowHeight);
            if (rowBottom - rowTop < rowHeight)
                rowTop = Math.Max(options.TeamRosterRegion.Y, rowBottom - rowHeight);
            return new ImageRegion(options.TeamRosterRegion.X, rowTop,
                options.TeamRosterRegion.Width, Math.Max(1, rowBottom - rowTop));
        }

        private static TemplateId BadgeTemplate(TeamNumber team)
        {
            switch (team)
            {
                case TeamNumber.Team1: return TemplateId.Team1Badge;
                case TeamNumber.Team2: return TemplateId.Team2Badge;
                case TeamNumber.Team3: return TemplateId.Team3Badge;
                case TeamNumber.Team4: return TemplateId.Team4Badge;
                default: throw new ArgumentOutOfRangeException(nameof(team));
            }
        }

        private static string RowLog(IReadOnlyList<TeamRowObservation> rows, TeamNumber team)
        {
            TeamRowObservation row = rows.FirstOrDefault(value => value.Team == team);
            return row == null
                ? $"{team}Exists=false"
                : $"{team}Exists={row.Exists}, {team}Ready={row.IsReady}, {team}Locked={row.IsLocked}, {team}Evidence='{row.EvidenceSource}'";
        }

        private static WorldMapTeamAvailabilityResult Failed(string message,
            string error = null, GameState state = GameState.Unknown) =>
            new WorldMapTeamAvailabilityResult
            {
                Success = false,
                AnyReadyTeam = false,
                AvailableTeams = new TeamNumber[0],
                ExistingTeams = new TeamNumber[0],
                ReadyTeams = new TeamNumber[0],
                BusyTeams = new TeamNumber[0],
                LockedTeams = new TeamNumber[0],
                RowObservations = new TeamRowObservation[0],
                TeamRows = new Dictionary<TeamNumber, TeamRowObservation>(),
                FinalState = state,
                Message = message,
                ErrorMessage = error ?? message,
                ReadyMatch = ImageMatchResult.NotFound(),
                ReadyMatches = new ImageMatchResult[0],
                RosterEvidenceSource = TeamRosterEvidenceSource.Unknown,
                RosterClassification = TeamRosterClassification.Failed,
                IsRosterUncertain = true,
                RosterStatus = TeamRosterClassification.Failed.ToString(),
                RosterConfidence = "Failed"
            };

        private sealed class RosterKnowledge
        {
            public RosterKnowledge(IEnumerable<TeamNumber> activeTeams,
                DateTimeOffset lastConfirmedAt, TeamRosterEvidenceSource evidenceSource,
                IEnumerable<TeamNumber> pendingActiveTeams = null,
                int strongContradictionConfirmations = 0)
            {
                ActiveTeams = new HashSet<TeamNumber>(activeTeams ?? Enumerable.Empty<TeamNumber>());
                LastConfirmedAt = lastConfirmedAt;
                EvidenceSource = evidenceSource;
                PendingActiveTeams = pendingActiveTeams == null ? null
                    : new HashSet<TeamNumber>(pendingActiveTeams);
                StrongContradictionConfirmations = strongContradictionConfirmations;
            }

            public HashSet<TeamNumber> ActiveTeams { get; }
            public int HighestConfirmedTeamCount => ActiveTeams.Count;
            public DateTimeOffset LastConfirmedAt { get; }
            public TeamRosterEvidenceSource EvidenceSource { get; }
            public HashSet<TeamNumber> PendingActiveTeams { get; }
            public int StrongContradictionConfirmations { get; }
            public string EvidenceStrength => EvidenceSource == TeamRosterEvidenceSource.FreshBadges
                ? "Strong" : "RowEvidence";
        }

        private bool IsMatchInsideRow(ImageMatchResult match, ImageRegion row)
        {
            if (match == null || !match.Found || match.Width <= 0 || match.Height <= 0)
                return false;
            int tolerance = options.RowVerticalTolerance;
            return match.Y >= row.Y - tolerance
                && match.Y + match.Height <= row.Y + row.Height + tolerance;
        }

        private static bool IsMatchInsideRegion(ImageMatchResult match, ImageRegion region)
        {
            return match != null && match.Found && match.Width > 0 && match.Height > 0
                && match.X >= region.X && match.Y >= region.Y
                && match.X + match.Width <= region.X + region.Width
                && match.Y + match.Height <= region.Y + region.Height;
        }

        private static TeamNumber ResolveStatusTeam(TeamNumber requestedTeam,
            ImageMatchResult match,
            IReadOnlyDictionary<TeamNumber, ImageMatchResult> badgeMatches,
            WorldMapTeamRosterLayout layout)
        {
            if (match == null || !match.Found || layout == null
                || badgeMatches == null || badgeMatches.Count < 2
                || !badgeMatches.ContainsKey(requestedTeam))
                return requestedTeam;

            double statusCenterY = match.Y + (match.Height / 2d);
            var nearest = badgeMatches
                .Where(item => item.Value != null && item.Value.Found
                    && (int)item.Key >= 1 && (int)item.Key <= layout.Rows.Count)
                .Select(item => new
                {
                    Team = item.Key,
                    Distance = Math.Abs(statusCenterY
                        - (item.Value.Y + (item.Value.Height / 2d)))
                })
                .OrderBy(item => item.Distance)
                .ThenBy(item => (int)item.Team)
                .FirstOrDefault();
            if (nearest == null || nearest.Distance > layout.Rows[0].Height)
                return requestedTeam;
            return nearest.Team;
        }

    }
}
