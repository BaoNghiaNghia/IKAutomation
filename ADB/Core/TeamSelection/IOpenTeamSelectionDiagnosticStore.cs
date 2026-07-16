using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public interface IOpenTeamSelectionDiagnosticStore
    {
        Task<string> SaveAsync(string deviceName, OpenTeamSelectionOutcome outcome,
            byte[] screenshotPng, CancellationToken cancellationToken);
    }
}
