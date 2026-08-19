using IK_Auto_ADB.Core.Vision;
using System;
using System.Collections.Generic;

namespace IK_Auto_ADB.Core.TeamSelection
{
    public class SelectedTeamFrameResult
    {
        public TeamNumber? Team { get; set; }
        public bool IsConfident { get; set; }
        public bool IsAmbiguous { get; set; }
        public double WinningScore { get; set; }
        public double RunnerUpScore { get; set; }
        public double WinningMargin { get; set; }
        public IReadOnlyDictionary<TeamNumber, ImageRegion> Rows { get; set; }
        public IReadOnlyDictionary<TeamNumber, double> RowScores { get; set; }
        public string FailureReason { get; set; }
        public IReadOnlyDictionary<TeamNumber, SelectedTeamRowScore> RowDetails { get; set; }
    }

    public sealed class SelectedTeamRowScore
    {
        public TeamNumber Team { get; set; } public ImageRegion RowBounds { get; set; }
        public double TemplateConfidence { get; set; } public double LeftBorderScore { get; set; }
        public double RightBorderScore { get; set; } public double TopBorderScore { get; set; }
        public double BottomBorderScore { get; set; } public int BorderEdgesFound { get; set; }
        public double ContrastScore { get; set; } public double CombinedScore { get; set; }
        public double BorderEvidenceScore { get; set; }
        public double BorderContinuityScore { get; set; }
        public double TemplatePathScore { get; set; }
        public double BorderPathScore { get; set; }
        public double EffectiveScore { get; set; }
        public bool BorderOnlyQualified { get; set; }
        public bool CandidateQualified { get; set; }
        public int TopBorderOffset { get; set; }
        public int BottomBorderOffset { get; set; }
        public int LeftBorderOffset { get; set; }
        public int RightBorderOffset { get; set; }
        public bool GeometryValid { get; set; } public string FailureReason { get; set; }
    }

    public sealed class SelectedTeamConsensusResult : SelectedTeamFrameResult
    {
        public int FramesObserved { get; set; }
        public int MatchingFrames { get; set; }
    }

    public sealed class SelectedTeamDetectionContext
    {
        public IReadOnlyDictionary<TeamNumber, ImageRegion> TeamRegions { get; set; }
        public int ExpectedWidth { get; set; } = 1280;
        public int ExpectedHeight { get; set; } = 720;
        public int ConsensusFrames { get; set; } = 3;
        public int RequiredMatchingFrames { get; set; } = 2;
        public int FrameIntervalMs { get; set; } = 250;
        public int TimeoutMs { get; set; } = 3000;
        public double MinimumScore { get; set; } = .70;
        public double WinningMargin { get; set; } = .12;
        public TeamNumber? ExpectedTeam { get; set; }
        // Post-tap verification must use badge positions from the frame being scored.
        // The tap layout can span adjacent rows and is not a reliable selected-border ROI.
        public bool ResolveRowsFromFreshBadges { get; set; }
        public ImageRegion? TeamBadgeSearchRegion { get; set; }
    }
}
