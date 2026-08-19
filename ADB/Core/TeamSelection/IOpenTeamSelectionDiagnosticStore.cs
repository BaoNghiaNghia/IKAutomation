using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.TeamSelection
{
    public interface IOpenTeamSelectionDiagnosticStore
    {
        Task<string> SaveAsync(string deviceName, OpenTeamSelectionOutcome outcome,
            byte[] screenshotPng, CancellationToken cancellationToken);
    }
}
