using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IOneShotFarmWorkflow
    {
        Task<OneShotFarmResult> RunAsync(string deviceName, OneShotFarmRequest request,
            CancellationToken cancellationToken);

        Task<OneShotFarmResult> RunAsync(string deviceName, OneShotFarmRequest request,
            IProgress<OneShotFarmProgress> progress, CancellationToken cancellationToken);
    }
}
