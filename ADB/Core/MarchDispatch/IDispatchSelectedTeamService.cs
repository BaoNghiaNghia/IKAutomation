using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.MarchDispatch
{
    public interface IDispatchSelectedTeamService
    {
        Task<DispatchMarchResult> DispatchAsync(string deviceName,
            DispatchMarchRequest request, CancellationToken cancellationToken);
    }
}
