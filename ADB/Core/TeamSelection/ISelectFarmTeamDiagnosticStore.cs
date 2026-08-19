using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.TeamSelection
{
    public interface ISelectFarmTeamDiagnosticStore
    {
        Task<string> SaveAsync(string deviceName, SelectFarmTeamOutcome outcome,
            byte[] screenshotPng, CancellationToken cancellationToken);
    }
}
