using IK_Auto_ADB.Core.Workflows;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Infrastructure.Workflows
{
    /// <summary>Streaming shared semaphore for lightweight preflight work.</summary>
    public sealed class PreflightConcurrencyGate : IPreflightConcurrencyGate
    {
        private readonly object sync = new object();
        private readonly PreflightConcurrencyOptions options;
        private readonly SemaphoreSlim semaphore;
        private readonly Func<int, CancellationToken, Task> delayAsync;
        private DateTimeOffset nextAdmissionAt = DateTimeOffset.MinValue;
        private int active;
        private int queued;

        public PreflightConcurrencyGate(PreflightConcurrencyOptions options,
            Func<int, CancellationToken, Task> delayAsync = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            semaphore = new SemaphoreSlim(options.MaximumConcurrency,
                options.MaximumConcurrency);
            this.delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
        }

        public async Task<IPreflightConcurrencyLease> AcquireAsync(string deviceName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync) queued++;
            bool entered = false;
            try
            {
                int staggerDelay = ReserveStaggerDelay(deviceName);
                if (staggerDelay > 0)
                    await delayAsync(staggerDelay, cancellationToken);
                await semaphore.WaitAsync(cancellationToken);
                entered = true;
                lock (sync)
                {
                    queued--;
                    active++;
                }
                return new Lease(this);
            }
            finally
            {
                if (!entered)
                {
                    lock (sync) queued--;
                }
            }
        }

        public PreflightConcurrencySnapshot GetSnapshot()
        {
            lock (sync)
            {
                return new PreflightConcurrencySnapshot
                {
                    Limit = options.MaximumConcurrency,
                    Active = active,
                    Queued = queued
                };
            }
        }

        private int ReserveStaggerDelay(string deviceName)
        {
            lock (sync)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                DateTimeOffset scheduled = nextAdmissionAt > now ? nextAdmissionAt : now;
                nextAdmissionAt = scheduled.AddMilliseconds(StableSpacing(deviceName));
                double delay = (scheduled - now).TotalMilliseconds;
                return delay <= 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Ceiling(delay));
            }
        }

        private int StableSpacing(string deviceName)
        {
            if (options.StaggerMaxMs <= options.StaggerMinMs)
                return options.StaggerMinMs;
            unchecked
            {
                int hash = 17;
                foreach (char value in (deviceName ?? string.Empty).ToUpperInvariant())
                    hash = hash * 31 + value;
                int range = options.StaggerMaxMs - options.StaggerMinMs + 1;
                return options.StaggerMinMs + (int)((uint)hash % (uint)range);
            }
        }

        private void Release()
        {
            lock (sync) active--;
            semaphore.Release();
        }

        private sealed class Lease : IPreflightConcurrencyLease
        {
            private PreflightConcurrencyGate owner;
            public Lease(PreflightConcurrencyGate owner) { this.owner = owner; }
            public void Dispose()
            {
                PreflightConcurrencyGate current = Interlocked.Exchange(ref owner, null);
                if (current != null) current.Release();
            }
        }
    }
}
