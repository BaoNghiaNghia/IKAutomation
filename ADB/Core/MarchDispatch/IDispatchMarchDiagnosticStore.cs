using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.MarchDispatch
{
    public interface IDispatchMarchDiagnosticStore
    {
        Task<string> SaveAsync(string deviceName, DispatchMarchOutcome outcome,
            byte[] screenshotPng, CancellationToken cancellationToken);
    }
}
