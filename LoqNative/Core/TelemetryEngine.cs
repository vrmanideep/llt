using System;
using System.Management;
using System.Runtime.InteropServices;

namespace LoqNative.Core
{
    public static class TelemetryEngine
    {
        // 1. Link your custom Assembly DLL
        [DllImport("Dependencies\\telemetry.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong GetCpuCycles();

        // 2. WMI Connection Helper
        private static ManagementObject GetGameZone()
        {
            var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
            foreach (ManagementObject obj in searcher.Get()) return obj;
            throw new Exception("WMI Class not found.");
        }

        // 3. Polling Methods
        public static (uint cpu, uint gpu, bool fansActive) GetThermals()
        {
            try
            {
                var gamezone = GetGameZone();
                uint cpuTemp = (uint)gamezone.InvokeMethod("GetCPUTemp", null);
                uint gpuTemp = (uint)gamezone.InvokeMethod("GetGPUTemp", null);
                uint fanStatus = (uint)gamezone.InvokeMethod("GetFanCoolingStatus", null);
                
                return (cpuTemp, gpuTemp, fanStatus > 0);
            }
            catch
            {
                return (0, 0, false); // Fallback if kernel is busy
            }
        }
    }
}