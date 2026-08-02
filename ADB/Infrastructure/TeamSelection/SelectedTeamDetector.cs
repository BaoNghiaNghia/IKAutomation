using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.TeamSelection;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection
{
    // A selected border is only an evidence signal: a winner also needs a clear margin.
    public sealed class SelectedTeamDetector : ISelectedTeamDetector
    {
        private readonly ILdPlayerClient client; private readonly ITemplateRegistry registry; private readonly IImageMatcher matcher;
        public SelectedTeamDetector(ILdPlayerClient client, ITemplateRegistry registry, IImageMatcher matcher)
        { this.client=client; this.registry=registry; this.matcher=matcher; }
        public SelectedTeamFrameResult DetectFrame(byte[] frame, SelectedTeamDetectionContext context)
        {
            if(frame==null||context==null||context.TeamRegions==null) return Fail("MissingFrameOrGeometry");
            var rows=context.TeamRegions.OrderBy(x=>(int)x.Key).ToArray();
            if(rows.Length==0||rows.Any(x=>!Valid(x.Value,context))||Overlaps(rows)) return Fail("InvalidRowGeometry");
            var scores=new Dictionary<TeamNumber,double>();
            foreach(var row in rows){ var m=matcher.Find(frame,registry.LoadBytes(TemplateId.TeamSelectedBorderAnchor),row.Value); scores[row.Key]=m!=null&&m.Found?Math.Max(0,Math.Min(1,m.Confidence??.75)):0; }
            var ranked=scores.OrderByDescending(x=>x.Value).ToArray(); var win=ranked[0]; double runner=ranked.Length>1?ranked[1].Value:0, margin=win.Value-runner;
            bool ambiguous=ranked.Count(x=>x.Value>=context.MinimumScore)>1;
            bool confident=!ambiguous&&win.Value>=context.MinimumScore&&margin>=context.WinningMargin;
            return new SelectedTeamFrameResult{Team=confident?win.Key:(TeamNumber?)null,IsConfident=confident,IsAmbiguous=ambiguous,WinningScore=win.Value,RunnerUpScore=runner,WinningMargin=margin,Rows=rows.ToDictionary(x=>x.Key,x=>x.Value),RowScores=scores,FailureReason=confident?null:(ambiguous?"MultipleStrongRows":"InsufficientSelectionEvidence")};
        }
        public async Task<SelectedTeamConsensusResult> DetectAsync(string device, SelectedTeamDetectionContext c, CancellationToken token)
        { var frames=new List<SelectedTeamFrameResult>(); for(int i=0;i<c.ConsensusFrames;i++){token.ThrowIfCancellationRequested(); frames.Add(DetectFrame(await client.CaptureScreenshotPngAsync(device,token),c)); if(i+1<c.ConsensusFrames) await Task.Delay(c.FrameIntervalMs,token);} var strong=frames.Where(x=>x.IsConfident).ToArray(); var groups=strong.GroupBy(x=>x.Team).ToArray(); if(groups.Length!=1||groups[0].Count()<c.RequiredMatchingFrames)return new SelectedTeamConsensusResult{FramesObserved=frames.Count,MatchingFrames=groups.Length==1?groups[0].Count():0,FailureReason=groups.Length>1?"ConflictingFrames":"InsufficientConsensus"}; var r=groups[0].First(); return new SelectedTeamConsensusResult{Team=r.Team,IsConfident=true,WinningScore=r.WinningScore,RunnerUpScore=r.RunnerUpScore,WinningMargin=r.WinningMargin,Rows=r.Rows,RowScores=r.RowScores,FramesObserved=frames.Count,MatchingFrames=groups[0].Count()}; }
        private static SelectedTeamFrameResult Fail(string r)=>new SelectedTeamFrameResult{FailureReason=r};
        private static bool Valid(ImageRegion r,SelectedTeamDetectionContext c)=>r.X>=0&&r.Y>=0&&r.Width>0&&r.Height>0&&r.X+r.Width<=c.ExpectedWidth&&r.Y+r.Height<=c.ExpectedHeight;
        private static bool Overlaps(KeyValuePair<TeamNumber,ImageRegion>[] r)=>r.Any(a=>r.Any(b=>a.Key!=b.Key&&a.Value.X<b.Value.X+b.Value.Width&&b.Value.X<a.Value.X+a.Value.Width&&a.Value.Y<b.Value.Y+b.Value.Height&&b.Value.Y<a.Value.Y+a.Value.Height));
    }
}
