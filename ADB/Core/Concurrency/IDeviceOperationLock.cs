using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Concurrency
{
    public interface IDeviceOperationLock
    {
        Task<T> RunAsync<T>(
            string deviceName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken);
    }
}
