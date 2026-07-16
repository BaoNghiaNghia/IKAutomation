using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public interface ISelectFarmTeamService
    {
        Task<SelectFarmTeamResult> SelectAsync(string deviceName,
            TeamSelectionRequest request, CancellationToken cancellationToken);
    }
}
