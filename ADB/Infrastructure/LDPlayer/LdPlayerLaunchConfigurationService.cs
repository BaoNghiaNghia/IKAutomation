using IK_Auto_ADB.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Infrastructure.LDPlayer
{
    /// <summary>
    /// Uses LDPlayer's documented console commands for the display profile and
    /// persists the instance-local ADB setting before starting the instance.
    /// </summary>
    public sealed class LdPlayerLaunchConfigurationService : ILdPlayerLaunchConfigurationService
    {
        private const int CommandTimeoutMilliseconds = 30000;
        private const int StartTimeoutMilliseconds = 90000;
        private static readonly Regex AdbSetting = new Regex(
            "\\\"basicSettings\\.adbDebug\\\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex ResolutionWidth = new Regex(
            "\\\"advancedSettings\\.resolution\\\"\\s*:\\s*\\{[\\s\\S]*?\\\"width\\\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex ResolutionHeight = new Regex(
            "\\\"advancedSettings\\.resolution\\\"\\s*:\\s*\\{[\\s\\S]*?\\\"height\\\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex ResolutionDpi = new Regex(
            "\\\"advancedSettings\\.resolutionDpi\\\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

        public async Task<LdPlayerLaunchConfigurationResult> ConfigureAsync(
            IReadOnlyList<string> deviceNames, CancellationToken cancellationToken)
        {
            string[] requested = (deviceNames ?? new string[0]).Where(value =>
                !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (requested.Length == 0)
                throw new ArgumentException("At least one LDPlayer device is required.", nameof(deviceNames));

            string consolePath = ResolveConsolePath();
            IReadOnlyList<Instance> instances = await ListAsync(consolePath, cancellationToken);
            var results = new List<LdPlayerInstanceConfigurationResult>();
            foreach (string name in requested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Instance instance = instances.FirstOrDefault(value => string.Equals(value.Name,
                    name, StringComparison.OrdinalIgnoreCase));
                if (instance == null)
                {
                    results.Add(new LdPlayerInstanceConfigurationResult { DeviceName = name,
                        Message = "Không tìm thấy instance trong ldconsole list2." });
                    continue;
                }

                try
                {
                    InstanceProfile profile = ReadProfile(consolePath, instance.Index);
                    if (!profile.RequiresConfiguration)
                    {
                        if (instance.IsRunning)
                            await WaitForAdbAsync(consolePath, instance.Index, cancellationToken);
                        results.Add(new LdPlayerInstanceConfigurationResult { DeviceName = name,
                            Success = true, Message = instance.IsRunning
                                ? "Cấu hình 1280x720 DPI 240 và Debug ADB local đã đúng."
                                : "Cấu hình đã đúng; instance hiện đang tắt nên không khởi động lại." });
                        continue;
                    }

                    bool restartAfterConfiguration = instance.IsRunning;
                    if (restartAfterConfiguration)
                    {
                        await RunAsync(consolePath, "quit --index " + instance.Index,
                            CommandTimeoutMilliseconds, cancellationToken);
                        await WaitForStateAsync(consolePath, instance.Index, false, cancellationToken);
                    }
                    await RunAsync(consolePath, "modify --index " + instance.Index
                        + " --resolution 1280,720,240", CommandTimeoutMilliseconds,
                        cancellationToken);
                    EnableLocalAdb(consolePath, instance.Index);
                    if (restartAfterConfiguration)
                    {
                        await RunAsync(consolePath, "launch --index " + instance.Index,
                            CommandTimeoutMilliseconds, cancellationToken);
                        await WaitForStateAsync(consolePath, instance.Index, true, cancellationToken);
                        await WaitForAdbAsync(consolePath, instance.Index, cancellationToken);
                    }
                    results.Add(new LdPlayerInstanceConfigurationResult { DeviceName = name,
                        Success = true, Message = restartAfterConfiguration
                            ? "Đã sửa cấu hình và khởi động lại instance."
                            : "Đã sửa cấu hình; instance đang tắt nên không khởi động lại." });
                }
                catch (Exception exception) when (!(exception is OperationCanceledException))
                {
                    results.Add(new LdPlayerInstanceConfigurationResult { DeviceName = name,
                        Message = exception.Message });
                }
            }
            return new LdPlayerLaunchConfigurationResult { Devices = results };
        }

        private static string ResolveConsolePath()
        {
            string configured = ConfigurationManager.AppSettings["LDCONSOLE_PATH"];
            string[] candidates = { configured, @"C:\LDPlayer\LDPlayer9\ldconsole.exe",
                @"D:\LDPlayer\LDPlayer9\ldconsole.exe" };
            string path = candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)
                && File.Exists(Environment.ExpandEnvironmentVariables(value.Trim())));
            if (path == null) throw new FileNotFoundException(
                "Không tìm thấy ldconsole.exe. Hãy cấu hình LDCONSOLE_PATH.");
            return Environment.ExpandEnvironmentVariables(path.Trim());
        }

        private static void EnableLocalAdb(string consolePath, int index)
        {
            string directory = Path.Combine(Path.GetDirectoryName(consolePath), "vms", "config");
            string path = Path.Combine(directory, "leidian" + index + ".config");
            if (!File.Exists(path)) throw new FileNotFoundException(
                "Không tìm thấy cấu hình instance để bật Debug ADB.", path);
            string content = File.ReadAllText(path);
            if (AdbSetting.IsMatch(content)) content = AdbSetting.Replace(content,
                "\"basicSettings.adbDebug\": 1", 1);
            else
            {
                int lastBrace = content.LastIndexOf('}');
                if (lastBrace < 0) throw new InvalidDataException("Cấu hình LDPlayer không hợp lệ.");
                string prefix = content.Substring(0, lastBrace).TrimEnd();
                content = prefix + (prefix.EndsWith("{") ? string.Empty : ",")
                    + Environment.NewLine + "  \"basicSettings.adbDebug\": 1"
                    + Environment.NewLine + "}";
            }
            File.WriteAllText(path, content);
        }

        private static InstanceProfile ReadProfile(string consolePath, int index)
        {
            string directory = Path.Combine(Path.GetDirectoryName(consolePath), "vms", "config");
            string path = Path.Combine(directory, "leidian" + index + ".config");
            if (!File.Exists(path)) throw new FileNotFoundException(
                "Không tìm thấy cấu hình instance để kiểm tra.", path);
            string content = File.ReadAllText(path);
            return new InstanceProfile
            {
                Width = ReadNumber(ResolutionWidth, content),
                Height = ReadNumber(ResolutionHeight, content),
                Dpi = ReadNumber(ResolutionDpi, content),
                AdbDebug = ReadNumber(AdbSetting, content)
            };
        }

        private static int ReadNumber(Regex pattern, string content)
        {
            Match match = pattern.Match(content);
            int value;
            return match.Success && int.TryParse(match.Groups[1].Value, out value) ? value : -1;
        }

        private static async Task WaitForStateAsync(string consolePath, int index, bool running,
            CancellationToken cancellationToken)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(StartTimeoutMilliseconds);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Instance instance = (await ListAsync(consolePath, cancellationToken)).FirstOrDefault(
                    value => value.Index == index);
                if (instance != null && instance.IsRunning == running) return;
                await Task.Delay(500, cancellationToken);
            }
            throw new TimeoutException(running ? "LDPlayer khởi động quá thời gian chờ."
                : "LDPlayer chưa tắt sau thời gian chờ.");
        }

        private static async Task WaitForAdbAsync(string consolePath, int index,
            CancellationToken cancellationToken)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(StartTimeoutMilliseconds);
            string lastResponse = string.Empty;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    lastResponse = await RunAsync(consolePath, "adb --index " + index
                        + " --command \"shell getprop sys.boot_completed\"",
                        CommandTimeoutMilliseconds, cancellationToken);
                    if (lastResponse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Any(value => string.Equals(value.Trim(), "1", StringComparison.Ordinal))) return;
                }
                catch (InvalidOperationException exception)
                {
                    lastResponse = exception.Message;
                }
                await Task.Delay(1000, cancellationToken);
            }
            throw new TimeoutException("ADB chưa sẵn sàng sau khi khởi động: " + lastResponse.Trim());
        }

        private static async Task<IReadOnlyList<Instance>> ListAsync(string consolePath,
            CancellationToken cancellationToken)
        {
            string output = await RunAsync(consolePath, "list2", CommandTimeoutMilliseconds,
                cancellationToken);
            return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Parse).Where(value => value != null).ToArray();
        }

        private static Instance Parse(string line)
        {
            string[] values = line.Split(',');
            int index;
            int started;
            if (values.Length < 5 || !int.TryParse(values[0], out index)
                || !int.TryParse(values[4], out started) || string.IsNullOrWhiteSpace(values[1])) return null;
            return new Instance { Index = index, Name = values[1].Trim(), IsRunning = started != 0 };
        }

        private static async Task<string> RunAsync(string fileName, string arguments,
            int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            using (var process = new Process { StartInfo = new ProcessStartInfo(fileName, arguments)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                  RedirectStandardError = true } })
            {
                process.Start();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                Task outputCompleted = Task.WhenAll(stdout, stderr);
                Task completed = await Task.WhenAny(outputCompleted,
                    Task.Delay(timeoutMilliseconds, cancellationToken));
                if (completed != outputCompleted)
                {
                    if (!process.HasExited) process.Kill();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("ldconsole command timed out: " + arguments);
                }
                process.WaitForExit();
                string output = (await stdout) + (await stderr);
                if (process.ExitCode != 0) throw new InvalidOperationException(output.Trim());
                return output;
            }
        }

        private sealed class Instance { public int Index; public string Name; public bool IsRunning; }
        private sealed class InstanceProfile
        {
            public int Width;
            public int Height;
            public int Dpi;
            public int AdbDebug;
            public bool RequiresConfiguration => Width != 1280 || Height != 720 || Dpi != 240 || AdbDebug != 1;
        }
    }
}
