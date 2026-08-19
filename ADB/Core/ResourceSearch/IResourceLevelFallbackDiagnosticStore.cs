using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public interface IResourceLevelFallbackDiagnosticStore
    {
        Task<string> SaveAsync(string deviceName, string fileSuffix,
            byte[] pngBytes, CancellationToken cancellationToken);
    }
}
