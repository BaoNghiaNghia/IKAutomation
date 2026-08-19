using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.Workflows
{
    public interface IOneShotFarmDiagnosticService
    {
        Task<string> CaptureAsync(string deviceName, OneShotFarmStep step,
            OneShotFarmOutcome outcome, CancellationToken cancellationToken);
    }
}
