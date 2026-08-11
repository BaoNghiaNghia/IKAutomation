using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency
{
    public sealed class DeviceAutomationOwnershipService : IDeviceAutomationOwnershipService
    {
        private sealed class OwnershipEntry
        {
            public DeviceAutomationOwner Owner;
            public Guid Token;
        }

        private sealed class Lease : IDeviceAutomationLease
        {
            private DeviceAutomationOwnershipService service;
            private readonly Guid token;

            public Lease(DeviceAutomationOwnershipService service, string deviceName,
                DeviceAutomationOwner owner, Guid token)
            {
                this.service = service;
                DeviceName = deviceName;
                Owner = owner;
                this.token = token;
            }

            public string DeviceName { get; }
            public DeviceAutomationOwner Owner { get; }

            public void Dispose()
            {
                DeviceAutomationOwnershipService current =
                    System.Threading.Interlocked.Exchange(ref service, null);
                current?.Release(DeviceName, Owner, token);
            }
        }

        private readonly ConcurrentDictionary<string, OwnershipEntry> owners =
            new ConcurrentDictionary<string, OwnershipEntry>(StringComparer.OrdinalIgnoreCase);
        private IDiagnosticLogger logger;

        public static DeviceAutomationOwnershipService Shared { get; } =
            new DeviceAutomationOwnershipService();

        public DeviceAutomationOwnershipService(IDiagnosticLogger logger = null)
        {
            this.logger = logger;
        }

        public void SetLogger(IDiagnosticLogger value)
        {
            if (value != null) logger = value;
        }

        public event EventHandler<DeviceAutomationOwnershipChangedEventArgs> OwnershipChanged;

        public DeviceAutomationOwner GetOwner(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return DeviceAutomationOwner.None;
            OwnershipEntry entry;
            return owners.TryGetValue(deviceName.Trim(), out entry)
                ? entry.Owner : DeviceAutomationOwner.None;
        }

        public bool TryAcquire(string deviceName, DeviceAutomationOwner requestedOwner,
            out IDeviceAutomationLease lease)
        {
            lease = null;
            if (string.IsNullOrWhiteSpace(deviceName))
                throw new ArgumentException("Device name is required.", nameof(deviceName));
            if (requestedOwner == DeviceAutomationOwner.None)
                throw new ArgumentOutOfRangeException(nameof(requestedOwner));

            string normalized = deviceName.Trim();
            var entry = new OwnershipEntry { Owner = requestedOwner, Token = Guid.NewGuid() };
            bool acquired = owners.TryAdd(normalized, entry);
            DeviceAutomationOwner currentOwner = acquired ? requestedOwner : GetOwner(normalized);
            Log(normalized, requestedOwner, currentOwner, "Acquire", acquired,
                acquired ? string.Empty : "OwnedBy" + currentOwner);
            if (!acquired) return false;

            lease = new Lease(this, normalized, requestedOwner, entry.Token);
            RaiseChanged(normalized, requestedOwner);
            return true;
        }

        private void Release(string deviceName, DeviceAutomationOwner owner, Guid token)
        {
            OwnershipEntry current;
            bool released = owners.TryGetValue(deviceName, out current)
                && current.Token == token
                && owners.TryRemove(deviceName, out current);
            Log(deviceName, owner, released ? DeviceAutomationOwner.None : GetOwner(deviceName),
                "Release", released, released ? string.Empty : "LeaseNotCurrent");
            if (released) RaiseChanged(deviceName, DeviceAutomationOwner.None);
        }

        private void RaiseChanged(string deviceName, DeviceAutomationOwner owner)
        {
            OwnershipChanged?.Invoke(this, new DeviceAutomationOwnershipChangedEventArgs
            {
                DeviceName = deviceName,
                Owner = owner
            });
        }

        private void Log(string deviceName, DeviceAutomationOwner requestedOwner,
            DeviceAutomationOwner currentOwner, string action, bool success, string reason)
        {
            string message = $"[Device Automation Lease] DeviceName='{deviceName}', "
                + $"RequestedOwner='{requestedOwner}', CurrentOwner='{currentOwner}', "
                + $"Action='{action}', Success={success}, Reason='{reason}'";
            if (logger != null) logger.Info(message); else Trace.TraceInformation(message);
        }
    }
}
