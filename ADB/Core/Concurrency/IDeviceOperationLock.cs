using System;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Core.Concurrency
{
    public interface IDeviceOperationLock
    {
        Task<T> RunAsync<T>(
            string deviceName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken);
    }
}
