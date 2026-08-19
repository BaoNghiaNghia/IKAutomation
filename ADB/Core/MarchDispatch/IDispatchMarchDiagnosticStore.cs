using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.MarchDispatch
{
    public interface IDispatchMarchDiagnosticStore
    {
        Task<string> SaveAsync(string deviceName, DispatchMarchOutcome outcome,
            byte[] screenshotPng, CancellationToken cancellationToken);
    }
}
