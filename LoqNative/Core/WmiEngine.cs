using System;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LoqNative.Core
{
    public enum LoqThermalMode : int { Quiet = 1, Balanced = 2, Performance = 3, Custom = 255 }
    public enum LoqGpuMode : int { Hybrid = 0, IGPUOnly = 1, Auto = 2, DGPU = 3 }
    public enum LoqBatteryMode : int { Conservation = 0, Normal = 1, RapidCharge = 2 }
    public enum LoqTelemetryId : uint { CpuTemperature = 0x05040000, GpuTemperature = 0x05050000, CpuFanSpeed = 0x04030001, GpuFanSpeed = 0x04030002 }

    public static class WmiEngine
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        public static LoqThermalMode GetCurrentThermalMode()
        {
            try {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results) {
                    using (obj) {
                        using ManagementBaseObject outParams = obj.InvokeMethod("GetSmartFanMode", null, null);
                        if (outParams?["Data"] != null) return (LoqThermalMode)Convert.ToInt32(outParams["Data"]);
                    }
                }
            } catch { }
            return LoqThermalMode.Balanced; 
        }

        public static void SetThermalMode(LoqThermalMode mode)
        {
            try {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results) {
                    using (obj) {
                        using ManagementBaseObject inParams = obj.GetMethodParameters("SetSmartFanMode");
                        inParams["Data"] = (int)mode; 
                        obj.InvokeMethod("SetSmartFanMode", inParams, null);
                        return; 
                    }
                }
            } catch { }
        }

        public static LoqGpuMode GetCurrentGpuMode()
        {
            try {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results) {
                    using (obj) {
                        using ManagementBaseObject gSyncOut = obj.InvokeMethod("GetGSyncStatus", null, null);
                        if (Convert.ToInt32(gSyncOut["Data"]) == 1) return LoqGpuMode.DGPU;
                        
                        using ManagementBaseObject iGpuOut = obj.InvokeMethod("GetIGPUModeStatus", null, null);
                        int status = Convert.ToInt32(iGpuOut["Data"]);
                        if (status == 1) return LoqGpuMode.IGPUOnly;
                        if (status == 2) return LoqGpuMode.Auto;
                    }
                }
            } catch { }

            try {
                uint igpuStatus = GetFeatureValue(0x00010000); 
                if (igpuStatus == 1) return LoqGpuMode.IGPUOnly;
                if (igpuStatus == 2) return LoqGpuMode.Auto;
            } catch { }

            return LoqGpuMode.Hybrid;
        }

        public static void SetGpuMode(LoqGpuMode mode)
        {
            int gSyncValue = mode == LoqGpuMode.DGPU ? 1 : 0;
            uint igpuValue = mode switch { LoqGpuMode.IGPUOnly => 1, LoqGpuMode.Auto => 2, _ => 0 };

            try {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results) {
                    using (obj) {
                        using ManagementBaseObject inParams = obj.GetMethodParameters("SetGSyncStatus");
                        inParams["Data"] = gSyncValue;
                        obj.InvokeMethod("SetGSyncStatus", inParams, null);
                    }
                }
            } catch { }

            bool igpuSet = false;
            try {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results) {
                    using (obj) {
                        using ManagementBaseObject inParams = obj.GetMethodParameters("SetIGPUModeStatus");
                        inParams["Data"] = (int)igpuValue;
                        obj.InvokeMethod("SetIGPUModeStatus", inParams, null);
                        igpuSet = true;
                    }
                }
            } catch { }

            if (!igpuSet) {
                try {
                    using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_OTHER_METHOD");
                    using var results = searcher.Get();
                    foreach (ManagementObject obj in results) {
                        using (obj) {
                            using ManagementBaseObject inParams = obj.GetMethodParameters("SetFeatureValue");
                            inParams["IDs"] = (uint)0x00010000; 
                            inParams["value"] = igpuValue;
                            obj.InvokeMethod("SetFeatureValue", inParams, null);
                        }
                    }
                } catch (Exception ex) { throw new Exception("GPU routing failed. Error: " + ex.Message); }
            }
        }

        private const uint IOCTL_BATTERY_CHARGE_MODE = 0x831020F8;

        public static LoqBatteryMode GetBatteryMode()
        {
            byte[] inBuffer = BitConverter.GetBytes(0xFFu);
            byte[] outBuffer = new byte[4];

            try {
                using (var handle = CreateFile(@"\\.\EnergyDrv", 0x80000000 | 0x40000000, 0, IntPtr.Zero, 3, 0x80, IntPtr.Zero))
                {
                    if (!handle.IsInvalid)
                    {
                        bool success = DeviceIoControl(handle, IOCTL_BATTERY_CHARGE_MODE, inBuffer, 4, outBuffer, 4, out _, IntPtr.Zero);
                        if (success)
                        {
                            uint state = BitConverter.ToUInt32(outBuffer, 0);
                            if ((state & 0x20) != 0) return LoqBatteryMode.Conservation;
                            if ((state & 0x04) != 0) return LoqBatteryMode.RapidCharge;
                            return LoqBatteryMode.Normal;
                        }
                    }
                }
            } catch { }

            try {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Lenovo\VantageService\AddinData\IdeaNotebookAddin");
                if (key != null) {
                    string val = key.GetValue("BatteryChargeMode") as string ?? "";
                    if (val == "Storage") return LoqBatteryMode.Conservation;
                    if (val == "Quick") return LoqBatteryMode.RapidCharge;
                }
            } catch { }
            
            return LoqBatteryMode.Normal;
        }

        public static void SetBatteryMode(LoqBatteryMode mode)
        {
            uint[] commands = mode switch {
                LoqBatteryMode.Conservation => new uint[] { 0x08, 0x03 },
                LoqBatteryMode.RapidCharge  => new uint[] { 0x05, 0x07 },
                _                           => new uint[] { 0x05, 0x08 } 
            };

            using (var handle = CreateFile(@"\\.\EnergyDrv", 0x80000000 | 0x40000000, 0, IntPtr.Zero, 3, 0x80, IntPtr.Zero))
            {
                if (handle.IsInvalid) 
                    throw new Exception("Could not open \\\\.\\EnergyDrv. Ensure the app is running as Administrator.");

                foreach (uint cmd in commands)
                {
                    byte[] inBuffer = BitConverter.GetBytes(cmd);
                    byte[] outBuffer = new byte[4]; 

                    bool success = DeviceIoControl(handle, IOCTL_BATTERY_CHARGE_MODE, inBuffer, 4, outBuffer, 4, out _, IntPtr.Zero);
                    
                    if (!success)
                        throw new Exception($"Energy Driver rejected command {cmd}. Error: {Marshal.GetLastWin32Error()}");
                }
            }

            try {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Lenovo\VantageService\AddinData\IdeaNotebookAddin");
                string regString = mode switch {
                    LoqBatteryMode.Conservation => "Storage",
                    LoqBatteryMode.RapidCharge => "Quick",
                    _ => "Normal"
                };
                key?.SetValue("BatteryChargeMode", regString);
            } catch { }
        }
        
        public static uint GetFeatureValue(uint id)
        {
            try {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_OTHER_METHOD");
                using var results = searcher.Get();
                foreach (ManagementObject obj in results) {
                    using (obj) {
                        using ManagementBaseObject inParams = obj.GetMethodParameters("GetFeatureValue");
                        inParams["IDs"] = id;
                        using ManagementBaseObject outParams = obj.InvokeMethod("GetFeatureValue", inParams, null);
                        return Convert.ToUInt32(outParams["Value"]);
                    }
                }
            } catch { }
            return 0;
        }
    }

    public static class OsTelemetry
    {
        public static uint GetCpuUsage()
        {
            try {
                using var searcher = new ManagementObjectSearcher("select LoadPercentage from Win32_Processor");
                using var results = searcher.Get();
                foreach (var obj in results) { using(obj) return Convert.ToUInt32(obj["LoadPercentage"]); }
            } catch { } return 0;
        }

        public static uint GetCpuClock()
        {
            try {
                using var searcher = new ManagementObjectSearcher("select CurrentClockSpeed from Win32_Processor");
                using var results = searcher.Get();
                foreach (var obj in results) { using(obj) return Convert.ToUInt32(obj["CurrentClockSpeed"]); }
            } catch { } return 0;
        }

        public static (double UsedGB, double TotalGB) GetRamDetails()
        {
            try {
                using var searcher = new ManagementObjectSearcher("select TotalVisibleMemorySize, FreePhysicalMemory from Win32_OperatingSystem");
                using var results = searcher.Get();
                foreach (var obj in results) {
                    using(obj) {
                        ulong totalKB = Convert.ToUInt64(obj["TotalVisibleMemorySize"]);
                        ulong freeKB = Convert.ToUInt64(obj["FreePhysicalMemory"]);
                        return ((totalKB - freeKB) / 1048576.0, totalKB / 1048576.0);
                    }
                }
            } catch { } return (0, 0);
        }

        // --- BULLETPROOF NVML POLLER ---
        public static (uint Usage, double Wattage, double VramGB, uint CoreMhz, uint MemMhz) GetNvidiaGpuStats(uint currentGpuTemp)
        {
            if (currentGpuTemp == 0) return (0, 0, 0, 0, 0);

            try {
                if (NvmlNative.nvmlInit_v2() != 0) return (0, 0, 0, 0, 0);
                try {
                    if (NvmlNative.nvmlDeviceGetHandleByIndex_v2(0, out IntPtr device) != 0) return (0, 0, 0, 0, 0);

                    uint usage = 0, coreMhz = 0, memMhz = 0;
                    double wattage = 0, vramGB = 0;

                    try { if (NvmlNative.nvmlDeviceGetUtilizationRates(device, out var util) == 0) usage = util.gpu; } catch {}
                    try { if (NvmlNative.nvmlDeviceGetPowerUsage(device, out uint powerMw) == 0) wattage = powerMw / 1000.0; } catch {}
                    try { if (NvmlNative.nvmlDeviceGetMemoryInfo(device, out var mem) == 0) vramGB = mem.used / 1073741824.0; } catch {}
                    try { NvmlNative.nvmlDeviceGetClockInfo(device, 0, out coreMhz); } catch {}
                    try { NvmlNative.nvmlDeviceGetClockInfo(device, 2, out memMhz); } catch {}

                    return (usage, wattage, vramGB, coreMhz, memMhz);
                } 
                finally {
                    NvmlNative.nvmlShutdown();
                }
            } catch { }
            return (0, 0, 0, 0, 0);
        }

        public static (double Percent, double Wattage, bool IsCharging) GetBatteryInfo()
        {
            double percent = 0, wattage = 0;
            bool isCharging = false;
            try {
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                percent = powerStatus.BatteryLifePercent * 100.0;
                isCharging = powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                
                using var wmiSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryStatus");
                using var results = wmiSearcher.Get();
                foreach (var obj in results) {
                    using(obj) {
                        int chargeRate = Convert.ToInt32(obj["ChargeRate"]);
                        int dischargeRate = Convert.ToInt32(obj["DischargeRate"]);
                        if (isCharging && chargeRate > 0) wattage = chargeRate / 1000.0;
                        else if (!isCharging && dischargeRate > 0) wattage = dischargeRate / 1000.0;
                    }
                }
            } catch { }
            return (percent, wattage, isCharging);
        }

        // --- FORCE GPU DEEP SLEEP ENGINE ---
        public static void KillGpuApps()
        {
            try
            {
                if (NvmlNative.nvmlInit_v2() != 0) return;
                try
                {
                    if (NvmlNative.nvmlDeviceGetHandleByIndex_v2(0, out IntPtr device) != 0) return;

                    uint count = 0;
                    NvmlNative.nvmlDeviceGetGraphicsRunningProcesses(device, ref count, null);
                    if (count == 0) return;

                    var processes = new NvmlNative.nvmlProcessInfo_t[count];
                    if (NvmlNative.nvmlDeviceGetGraphicsRunningProcesses(device, ref count, processes) == 0)
                    {
                        int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;

                        var whitelist = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "dwm", "csrss", "winlogon", "services", "lsass", "smss", "wininit",
                            "svchost", "fontdrvhost", "nvcontainer", "nvdisplay.container", "nvsettings",
                            "nvspcaps64", "nvsphelper64", "nvwmi64", "nvcplui", "explorer", "taskhostw",
                            "sihost", "runtimebroker", "shellexperiencehost", "searchhost",
                            "startmenuexperiencehost", "textinputhost", "applicationframehost",
                            "systemsettings", "dllhost", "conhost", "audiodg", "ctfloader"
                        };

                        for (int i = 0; i < count; i++)
                        {
                            int pid = (int)processes[i].pid;
                            if (pid == currentPid || pid == 0) continue;

                            try
                            {
                                using var proc = System.Diagnostics.Process.GetProcessById(pid);
                                if (proc.SessionId == 0) continue;
                                if (whitelist.Contains(proc.ProcessName)) continue;

                                proc.Kill();
                            }
                            catch { }
                        }
                    }
                }
                finally
                {
                    NvmlNative.nvmlShutdown();
                }
            }
            catch { }
        }
    }

    // --- UPGRADED KERNEL FPS ENGINE (Filtered 1% Lows) ---
    public static class EtwFpsMonitor
    {
        private static Microsoft.Diagnostics.Tracing.Session.TraceEventSession? _session;
        private static System.Collections.Concurrent.ConcurrentDictionary<int, int> _frameCount = new();
        private static System.Collections.Concurrent.ConcurrentBag<double> _frameTimes = new();
        private static System.Collections.Concurrent.ConcurrentDictionary<int, long> _lastFrameTimestamp = new();
        
        private static int _currentFps = 0;
        private static int _current1PercentLow = 0;
        private static int _activeProcessId = 0;
        private static System.Timers.Timer? _timer;

        public static void Start()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try {
                    string sessionName = "LoqNativeFpsSession";
                    if (Microsoft.Diagnostics.Tracing.Session.TraceEventSession.GetActiveSessionNames().Contains(sessionName)) {
                        var oldSession = new Microsoft.Diagnostics.Tracing.Session.TraceEventSession(sessionName);
                        oldSession.Stop();
                    }

                    _session = new Microsoft.Diagnostics.Tracing.Session.TraceEventSession(sessionName);
                    var dxgkrnl = Guid.Parse("802ec45a-1e99-4b83-9920-87c98277ba9d");
                    _session.EnableProvider(dxgkrnl, Microsoft.Diagnostics.Tracing.TraceEventLevel.Informational, 0x1); 

                    _session.Source.Dynamic.All += (data) =>
                    {
                        if (data.EventName.Contains("Present")) 
                        {
                            int pid = data.ProcessID;
                            _frameCount.AddOrUpdate(pid, 1, (id, count) => count + 1);

                            if (pid == _activeProcessId) 
                            {
                                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                                if (_lastFrameTimestamp.TryGetValue(pid, out long last)) {
                                    double ms = (now - last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                                    if (ms > 0) _frameTimes.Add(ms);
                                }
                                _lastFrameTimestamp[pid] = now;
                            }
                        }
                    };

                    _timer = new System.Timers.Timer(1000);
                    _timer.Elapsed += (s, e) =>
                    {
                        int maxFps = 0, maxPid = 0;
                        foreach (var kvp in _frameCount) {
                            if (kvp.Value > maxFps) { maxFps = kvp.Value; maxPid = kvp.Key; }
                        }
                        
                        _currentFps = maxFps;
                        _activeProcessId = maxPid;

                        var times = _frameTimes.ToArray();
                        _frameTimes.Clear();
                        
                        if (times.Length > 0 && maxFps > 0) {
                            var fpsList = System.Linq.Enumerable.ToArray(
                                System.Linq.Enumerable.OrderBy(
                                    System.Linq.Enumerable.Where(
                                        System.Linq.Enumerable.Select(times, t => 1000.0 / t), 
                                        f => f < 1000 && f >= 1), 
                                    f => f));

                            if (fpsList.Length > 0) {
                                int index = (int)(fpsList.Length * 0.01);
                                if (index >= fpsList.Length) index = fpsList.Length - 1;
                                _current1PercentLow = (int)Math.Round(fpsList[index]);
                            } else {
                                _current1PercentLow = 0;
                            }
                        } else {
                            _current1PercentLow = 0;
                        }

                        _frameCount.Clear();
                    };
                    _timer.Start();
                    _session.Source.Process(); 
                } catch { } 
            });
        }

        public static void Stop()
        {
            _session?.Stop();
            _timer?.Stop();
        }

        public static (string Avg, string Low) GetFps() => 
            (_currentFps > 0 ? _currentFps.ToString() : "--", _currentFps > 0 ? _current1PercentLow.ToString() : "--");
    }

    public static class NvmlNative
    {
        private const string NvmlLib = @"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll";

        [DllImport(NvmlLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlInit_v2();

        [DllImport(NvmlLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlShutdown();

        [DllImport(NvmlLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport(NvmlLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power); 

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlUtilization_t {
            public uint gpu;
            public uint memory;
        }
        [DllImport(NvmlLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out nvmlUtilization_t utilization);

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlMemory_t {
            public ulong total;
            public ulong free;
            public ulong used;
        }
        [DllImport(NvmlLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out nvmlMemory_t memory);

        [DllImport(NvmlLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetClockInfo(IntPtr device, uint type, out uint clock);

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlProcessInfo_t
        {
            public uint pid;
            public ulong usedGpuMemory;
        }

        [DllImport(NvmlLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetGraphicsRunningProcesses(IntPtr device, ref uint infoCount, [Out] nvmlProcessInfo_t[]? infos);
    }
}