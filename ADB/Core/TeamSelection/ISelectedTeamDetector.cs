using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public interface ISelectedTeamDetector
    {
        Task<SelectedTeamConsensusResult> DetectAsync(string deviceName,
            SelectedTeamDetectionContext context, CancellationToken cancellationToken);
        SelectedTeamFrameResult DetectFrame(byte[] screenshotPng,
            SelectedTeamDetectionContext context);
    }
}
