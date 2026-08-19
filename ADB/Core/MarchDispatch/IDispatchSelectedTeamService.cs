using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public interface IDispatchSelectedTeamService
    {
        Task<DispatchMarchResult> DispatchAsync(string deviceName,
            DispatchMarchRequest request, CancellationToken cancellationToken);
    }
}
