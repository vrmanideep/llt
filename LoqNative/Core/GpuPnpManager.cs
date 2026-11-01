using System;
using System.Diagnostics;
using System.Management;
using System.Linq;
using System.Collections.Generic;

namespace LoqNative.Core
{
    public static class GpuPnpManager
    {
        private static string _nvidiaDeviceId = string.Empty;

        // Add the executables you want to trigger the dGPU wakeup here
        private static readonly HashSet<string> _gameTriggers = new(StringComparer.OrdinalIgnoreCase)
        {
            "f125", "forzahorizon6", "cyberpunk2077"
        };

        private static string GetNvidiaDeviceId()
        {
            if (!string.IsNullOrEmpty(_nvidiaDeviceId)) return _nvidiaDeviceId;

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass = 'Display' AND Manufacturer LIKE '%NVIDIA%'");
                foreach (var obj in searcher.Get())
                {
                    _nvidiaDeviceId = obj["PNPDeviceID"]?.ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(_nvidiaDeviceId)) break;
                }
            }
            catch { }

            return _nvidiaDeviceId;
        }

        public static void SetDgpuState(bool enable)
        {
            string deviceId = GetNvidiaDeviceId();
            if (string.IsNullOrEmpty(deviceId)) return;

            string action = enable ? "/enable-device" : "/disable-device";
            
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = "pnputil.exe";
                process.StartInfo.Arguments = $"{action} \"{deviceId}\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();
            }
            catch { }
        }

        public static bool IsGameRunning()
        {
            try
            {
                var runningProcesses = Process.GetProcesses();
                foreach (var proc in runningProcesses)
                {
                    if (_gameTriggers.Contains(proc.ProcessName))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }
    }
}