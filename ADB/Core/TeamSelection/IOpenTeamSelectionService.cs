using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.TeamSelection
{
    public interface IOpenTeamSelectionService
    {
        Task<OpenTeamSelectionResult> OpenAsync(string deviceName, CancellationToken cancellationToken);
    }
}
