using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.TeamSelection
{
    public interface ISelectedTeamDetector
    {
        Task<SelectedTeamConsensusResult> DetectAsync(string deviceName,
            SelectedTeamDetectionContext context, CancellationToken cancellationToken);
        SelectedTeamFrameResult DetectFrame(byte[] screenshotPng,
            SelectedTeamDetectionContext context);
    }
}
