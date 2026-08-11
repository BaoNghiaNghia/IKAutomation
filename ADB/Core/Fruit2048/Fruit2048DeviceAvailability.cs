using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Tool_Automation_Post_FB.Core.Fruit2048
{
    public sealed class Fruit2048DeviceAvailability
    {
        public string DeviceName { get; set; }
        public bool Connected { get; set; }
        public DeviceAutomationOwner Owner { get; set; }
        public bool IsFruitRunning { get; set; }
        public bool IsSelectable => Connected && Owner == DeviceAutomationOwner.None && !IsFruitRunning;
        public string DisplayStatus => !Connected ? "Mất kết nối"
            : Owner == DeviceAutomationOwner.Farm ? "Đang Farm"
            : Owner == DeviceAutomationOwner.Fruit2048 || IsFruitRunning
                ? "Đang chơi Fruit 2048" : "Sẵn sàng";

        public static IReadOnlyList<string> SelectAllAvailable(
            IEnumerable<Fruit2048DeviceAvailability> devices)
        {
            return (devices ?? Enumerable.Empty<Fruit2048DeviceAvailability>())
                .Where(device => device.IsSelectable)
                .Select(device => device.DeviceName)
                .ToArray();
        }
    }
}
