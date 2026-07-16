using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public interface ISelectFarmTeamDiagnosticStore
    {
        Task<string> SaveAsync(string deviceName, SelectFarmTeamOutcome outcome,
            byte[] screenshotPng, CancellationToken cancellationToken);
    }
}
