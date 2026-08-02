using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
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
    }
}
