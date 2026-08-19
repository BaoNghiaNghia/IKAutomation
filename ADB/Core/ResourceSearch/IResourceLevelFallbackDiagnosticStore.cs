using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.ResourceSearch
{
    public interface IResourceLevelFallbackDiagnosticStore
    {
        Task<string> SaveAsync(string deviceName, string fileSuffix,
            byte[] pngBytes, CancellationToken cancellationToken);
    }
}
