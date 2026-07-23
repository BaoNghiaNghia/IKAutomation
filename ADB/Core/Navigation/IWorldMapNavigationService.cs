using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Navigation
{
    public interface IWorldMapNavigationService
    {
        Task<NavigationResult> EnsureWorldMapAsync(string deviceName, CancellationToken cancellationToken);
        Task<NavigationResult> OpenResourceSearchPanelAsync(string deviceName, CancellationToken cancellationToken);
        Task<NavigationResult> RepositionToAllianceTerritoryAsync(string deviceName, CancellationToken cancellationToken);
    }
}
