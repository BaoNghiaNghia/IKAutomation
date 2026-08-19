using ADB_Tool_Automation_Post_FB.Core.ResourceSearch;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.TeamSelection
{
    public interface IResourceAwareOpenTeamSelectionService : IOpenTeamSelectionService
    {
        Task<OpenTeamSelectionResult> OpenAsync(string deviceName,
            ResourceType resourceType, CancellationToken cancellationToken);
    }
}
