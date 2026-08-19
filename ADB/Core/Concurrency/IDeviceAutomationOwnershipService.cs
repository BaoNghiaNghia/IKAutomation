using System;

namespace IK_Auto_ADB.Core.Concurrency
{
    public enum DeviceAutomationOwner
    {
        None,
        Farm,
        Fruit2048
    }

    public sealed class DeviceAutomationOwnershipChangedEventArgs : EventArgs
    {
        public string DeviceName { get; set; }
        public DeviceAutomationOwner Owner { get; set; }
    }

    public interface IDeviceAutomationLease : IDisposable
    {
        string DeviceName { get; }
        DeviceAutomationOwner Owner { get; }
    }

    public interface IDeviceAutomationOwnershipService
    {
        event EventHandler<DeviceAutomationOwnershipChangedEventArgs> OwnershipChanged;

        DeviceAutomationOwner GetOwner(string deviceName);

        bool TryAcquire(string deviceName, DeviceAutomationOwner requestedOwner,
            out IDeviceAutomationLease lease);
    }
}
