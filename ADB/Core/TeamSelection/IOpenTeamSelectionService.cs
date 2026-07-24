using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public interface IOpenTeamSelectionService
    {
        Task<OpenTeamSelectionResult> OpenAsync(string deviceName, CancellationToken cancellationToken);
    }
}
