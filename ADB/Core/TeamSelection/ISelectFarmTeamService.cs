using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.TeamSelection
{
    public interface ISelectFarmTeamService
    {
        Task<SelectFarmTeamResult> SelectAsync(string deviceName,
            TeamSelectionRequest request, CancellationToken cancellationToken);
    }
}
