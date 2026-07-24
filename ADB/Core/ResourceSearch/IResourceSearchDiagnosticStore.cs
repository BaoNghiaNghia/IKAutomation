using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public interface IResourceSearchDiagnosticStore
    {
        Task<string> SaveResultAsync(string deviceName, ResourceSearchOutcome outcome,
            byte[] pngBytes, CancellationToken cancellationToken);

        Task SaveObservationAsync(string deviceName, DateTimeOffset burstTimestamp,
            int frameIndex, byte[] pngBytes, CancellationToken cancellationToken);
    }
}
