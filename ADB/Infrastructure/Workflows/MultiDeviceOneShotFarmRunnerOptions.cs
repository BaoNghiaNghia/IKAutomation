using System;
using System.Configuration;

namespace IK_Auto_ADB.Infrastructure.Workflows
{
    public sealed class MultiDeviceOneShotFarmRunnerOptions
    {
        public MultiDeviceOneShotFarmRunnerOptions(int preflightTimeoutMs = 90000,
            int deviceRequeueDelayMs = 750, int maxTeamsPerDeviceCycle = 4,
            int maxDeviceIterationsPerCycle = 8,
            int maxConsecutiveNoProgressAttempts = 2)
        {
            if (preflightTimeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(preflightTimeoutMs));
            if (deviceRequeueDelayMs < 0) throw new ArgumentOutOfRangeException(nameof(deviceRequeueDelayMs));
            if (maxTeamsPerDeviceCycle < 1 || maxTeamsPerDeviceCycle > 4)
                throw new ArgumentOutOfRangeException(nameof(maxTeamsPerDeviceCycle));
            if (maxDeviceIterationsPerCycle < maxTeamsPerDeviceCycle)
                throw new ArgumentOutOfRangeException(nameof(maxDeviceIterationsPerCycle));
            if (maxConsecutiveNoProgressAttempts < 1)
                throw new ArgumentOutOfRangeException(nameof(maxConsecutiveNoProgressAttempts));
            PreflightTimeoutMs = preflightTimeoutMs;
            DeviceRequeueDelayMs = deviceRequeueDelayMs;
            MaxTeamsPerDeviceCycle = maxTeamsPerDeviceCycle;
            MaxDeviceIterationsPerCycle = maxDeviceIterationsPerCycle;
            MaxConsecutiveNoProgressAttempts = maxConsecutiveNoProgressAttempts;
        }

        public int PreflightTimeoutMs { get; }
        public int DeviceRequeueDelayMs { get; }
        public int MaxTeamsPerDeviceCycle { get; }
        public int MaxDeviceIterationsPerCycle { get; }
        public int MaxConsecutiveNoProgressAttempts { get; }
    }

    public static class AppConfigMultiDeviceOneShotFarmRunnerOptionsProvider
    {
        public static MultiDeviceOneShotFarmRunnerOptions Load() =>
            new MultiDeviceOneShotFarmRunnerOptions(
                ReadPositive("Operations.PreflightTimeoutSeconds", 90) * 1000,
                ReadNonNegative("Operations.DeviceRequeueDelayMs", 750),
                ReadPositive("Operations.MaxTeamsPerDeviceCycle", 4),
                ReadPositive("Operations.MaxDeviceIterationsPerCycle", 8),
                ReadPositive("Operations.MaxConsecutiveNoProgressAttempts", 2));

        private static int ReadPositive(string key, int fallback) =>
            int.TryParse(ConfigurationManager.AppSettings[key], out int value)
                && value > 0 ? value : fallback;

        private static int ReadNonNegative(string key, int fallback) =>
            int.TryParse(ConfigurationManager.AppSettings[key], out int value)
                && value >= 0 ? value : fallback;
    }
}
