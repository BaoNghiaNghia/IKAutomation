using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency
{
    public sealed class DeviceOperationLock : IDeviceOperationLock
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> deviceLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private readonly AsyncLocal<HashSet<string>> heldDevices = new AsyncLocal<HashSet<string>>();

        public static DeviceOperationLock Shared { get; } = new DeviceOperationLock();

        public async Task<T> RunAsync<T>(string deviceName,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("LDPlayer device name is required.", nameof(deviceName));
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            cancellationToken.ThrowIfCancellationRequested();
            string key = deviceName.Trim();
            HashSet<string> inherited = heldDevices.Value;
            if (inherited != null && inherited.Contains(key))
                return await operation(cancellationToken);

            SemaphoreSlim gate = deviceLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            HashSet<string> previous = heldDevices.Value;
            var current = previous == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase);
            current.Add(key);
            heldDevices.Value = current;
            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                heldDevices.Value = previous;
                gate.Release();
            }
        }
    }
}
