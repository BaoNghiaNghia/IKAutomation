using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Abstractions
{
    /// <summary>Applies the required LDPlayer instance settings before automation starts.</summary>
    public interface ILdPlayerLaunchConfigurationService
    {
        Task<LdPlayerLaunchConfigurationResult> ConfigureAsync(
            IReadOnlyList<string> deviceNames, CancellationToken cancellationToken);
    }

    public sealed class LdPlayerLaunchConfigurationResult
    {
        public IReadOnlyList<LdPlayerInstanceConfigurationResult> Devices { get; set; }
            = new LdPlayerInstanceConfigurationResult[0];

        public bool Success => Devices != null && Devices.Count > 0
            && System.Linq.Enumerable.All(Devices, item => item.Success);
    }

    public sealed class LdPlayerInstanceConfigurationResult
    {
        public string DeviceName { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
