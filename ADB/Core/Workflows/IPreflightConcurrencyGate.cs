using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    /// <summary>
    /// Shared, lightweight admission for roster availability checks.  This is
    /// deliberately independent from the adaptive Farm automation admission.
    /// </summary>
    public interface IPreflightConcurrencyGate
    {
        Task<IPreflightConcurrencyLease> AcquireAsync(string deviceName,
            CancellationToken cancellationToken);

        PreflightConcurrencySnapshot GetSnapshot();
    }

    public interface IPreflightConcurrencyLease : IDisposable
    {
    }

    public sealed class PreflightConcurrencySnapshot
    {
        public int Limit { get; set; }
        public int Active { get; set; }
        public int Queued { get; set; }
    }

    public sealed class PreflightConcurrencyOptions
    {
        public PreflightConcurrencyOptions(int maximumConcurrency = 20,
            int staggerMinMs = 0, int staggerMaxMs = 0)
        {
            if (maximumConcurrency < 1 || maximumConcurrency > 25)
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
            if (staggerMinMs < 0 || staggerMaxMs < staggerMinMs)
                throw new ArgumentOutOfRangeException(nameof(staggerMinMs));
            MaximumConcurrency = maximumConcurrency;
            StaggerMinMs = staggerMinMs;
            StaggerMaxMs = staggerMaxMs;
        }

        public int MaximumConcurrency { get; }
        public int StaggerMinMs { get; }
        public int StaggerMaxMs { get; }
    }
}
