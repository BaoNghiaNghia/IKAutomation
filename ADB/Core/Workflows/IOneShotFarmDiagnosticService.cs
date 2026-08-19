using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IOneShotFarmDiagnosticService
    {
        Task<string> CaptureAsync(string deviceName, OneShotFarmStep step,
            OneShotFarmOutcome outcome, CancellationToken cancellationToken);
    }
}
