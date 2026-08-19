using System;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.Navigation
{
    public interface IWorldMapNavigationService
    {
        Task<NavigationResult> EnsureWorldMapAsync(string deviceName, CancellationToken cancellationToken);
        Task<NavigationResult> OpenResourceSearchPanelAsync(string deviceName, CancellationToken cancellationToken);
        Task<NavigationResult> TapWorldMapPointAsync(string deviceName, int x, int y,
            CancellationToken cancellationToken);
        Task<NavigationResult> RepositionToAllianceTerritoryAsync(string deviceName, CancellationToken cancellationToken);
    }

    public interface IWorldMapNavigationProgressService
    {
        Task<NavigationResult> RepositionToAllianceTerritoryAsync(
            string deviceName,
            IProgress<NavigationTransition> progress,
            CancellationToken cancellationToken);
    }

    public interface IResourceAreaMapPointNavigationService
    {
        Task<NavigationResult> OpenMapAndTapPointAsync(
            string deviceName, int x, int y, CancellationToken cancellationToken);

        Task<NavigationResult> OpenMapAndEnterCoordinatesAsync(
            string deviceName, int mapX, int mapY,
            CancellationToken cancellationToken);
    }
}
