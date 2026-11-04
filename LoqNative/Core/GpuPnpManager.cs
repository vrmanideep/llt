
using System;
using System.Diagnostics;
using System.Management;
using System.Threading;

namespace LoqNative.Core
{
    public sealed class GpuPnpResult
    {
        public bool Success { get; init; }
        public bool? IsEnabled { get; init; }
        public string Message { get; init; } = "";
        public string CommandOutput { get; init; } = "";
        public int? ExitCode { get; init; }
        public string DeviceId { get; init; } = "";
    }

    public static class GpuPnpManager
    {
        private sealed class DeviceInfo
        {
            public string DeviceId { get; init; } = "";
            public string Name { get; init; } = "";
            public uint ErrorCode { get; init; }

            public bool? IsEnabled => ErrorCode switch
            {
                0 => true,
                22 => false,
                _ => null
            };
        }

        private static DeviceInfo? FindNvidiaDevice()
        {
            const string query =
                "SELECT PNPDeviceID, Name, ConfigManagerErrorCode " +
                "FROM Win32_PnPEntity " +
                "WHERE PNPClass = 'Display' " +
                "AND Manufacturer LIKE '%NVIDIA%'";

            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2", query);

            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    string id = obj["PNPDeviceID"]?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    uint errorCode = Convert.ToUInt32(
                        obj["ConfigManagerErrorCode"] ?? uint.MaxValue);

                    return new DeviceInfo
                    {
                        DeviceId = id,
                        Name = obj["Name"]?.ToString() ?? "NVIDIA GPU",
                        ErrorCode = errorCode
                    };
                }
            }

            return null;
        }

        public static GpuPnpResult GetDgpuStatus()
        {
            try
            {
                var device = FindNvidiaDevice();

                if (device == null)
                {
                    return new GpuPnpResult
                    {
                        Message = "NVIDIA display device was not found."
                    };
                }

                return new GpuPnpResult
                {
                    Success = device.IsEnabled.HasValue,
                    IsEnabled = device.IsEnabled,
                    DeviceId = device.DeviceId,
                    Message = device.IsEnabled switch
                    {
                        true => "NVIDIA GPU is enabled.",
                        false => "NVIDIA GPU is disabled.",
                        null => $"NVIDIA device state is unknown. ConfigManagerErrorCode: {device.ErrorCode}."
                    }
                };
            }
            catch (Exception ex)
            {
                return new GpuPnpResult
                {
                    Message = $"Could not read NVIDIA device status: {ex.Message}"
                };
            }
        }

        public static GpuPnpResult SetDgpuState(bool enable)
        {
            string deviceId;

            try
            {
                var device = FindNvidiaDevice();

                if (device == null)
                {
                    return new GpuPnpResult
                    {
                        Message = "NVIDIA display device was not found."
                    };
                }

                deviceId = device.DeviceId;

                // Avoid running pnputil if the requested state is already active.
                if (device.IsEnabled == enable)
                {
                    return new GpuPnpResult
                    {
                        Success = true,
                        IsEnabled = enable,
                        DeviceId = deviceId,
                        Message = enable
                            ? "NVIDIA GPU is already enabled."
                            : "NVIDIA GPU is already disabled."
                    };
                }
            }
            catch (Exception ex)
            {
                return new GpuPnpResult
                {
                    Message = $"Could not identify NVIDIA GPU: {ex.Message}"
                };
            }

            string action = enable ? "/enable-device" : "/disable-device";

            try
            {
                using var process = new Process();

                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                process.StartInfo.ArgumentList.Add(action);
                process.StartInfo.ArgumentList.Add(deviceId);

                if (!process.Start())
                {
                    return new GpuPnpResult
                    {
                        DeviceId = deviceId,
                        Message = "Could not start pnputil.exe."
                    };
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(15000))
                {
                    process.Kill(true);
                    process.WaitForExit();

                    return new GpuPnpResult
                    {
                        DeviceId = deviceId,
                        Message = "pnputil.exe timed out."
                    };
                }

                string output = stdoutTask.GetAwaiter().GetResult();
                string error = stderrTask.GetAwaiter().GetResult();
                string combinedOutput = output + Environment.NewLine + error;

                int exitCode = process.ExitCode;

                if (exitCode != 0)
                {
                    return new GpuPnpResult
                    {
                        DeviceId = deviceId,
                        ExitCode = exitCode,
                        CommandOutput = combinedOutput,
                        Message = $"pnputil.exe failed with exit code {exitCode}."
                    };
                }

                // Verify the actual Windows device state.
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    Thread.Sleep(500);

                    var current = FindNvidiaDevice();

                    if (current?.IsEnabled == enable)
                    {
                        return new GpuPnpResult
                        {
                            Success = true,
                            IsEnabled = enable,
                            DeviceId = deviceId,
                            ExitCode = exitCode,
                            CommandOutput = combinedOutput,
                            Message = enable
                                ? "NVIDIA GPU successfully enabled and verified."
                                : "NVIDIA GPU successfully disabled and verified."
                        };
                    }
                }

                var finalStatus = GetDgpuStatus();

                return new GpuPnpResult
                {
                    Success = false,
                    IsEnabled = finalStatus.IsEnabled,
                    DeviceId = deviceId,
                    ExitCode = exitCode,
                    CommandOutput = combinedOutput,
                    Message = "pnputil completed, but the requested device state could not be verified. " +
                              finalStatus.Message
                };
            }
            catch (Exception ex)
            {
                return new GpuPnpResult
                {
                    DeviceId = deviceId,
                    Message = $"GPU device operation failed: {ex.Message}"
                };
            }
        }
    }
}
