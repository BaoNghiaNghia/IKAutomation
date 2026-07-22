using System;
using System.Collections.Generic;
using System.Threading;

namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IContinuousFarmCheckpointStore
    {
        ContinuousFarmCheckpoint Load(string deviceName,
            CancellationToken cancellationToken);

        void Save(ContinuousFarmCheckpoint checkpoint,
            CancellationToken cancellationToken);
    }

    public sealed class ContinuousFarmCheckpoint
    {
        public int Version { get; set; } = 1;
        public DateTimeOffset SavedAt { get; set; }
        public ContinuousFarmDeviceSnapshot Device { get; set; }
        public IReadOnlyList<DateTimeOffset> TechnicalFailureTimestamps { get; set; }
            = new DateTimeOffset[0];
    }
}
