using System;
using System.Linq;
using NvAPIWrapper;
using NvAPIWrapper.GPU;

namespace LoqNative.Core
{
    public class GpuState
    {
        public int CoreTemp { get; set; }
        public uint CoreUsage { get; set; }
        public uint VramUsage { get; set; }
        public int CoreClockMHz { get; set; }
        public int VramClockMHz { get; set; }
    }

    public static class GpuTelemetryEngine
    {
        private static PhysicalGPU? _gpu;
        private static bool _isInitialized = false;

        public static void Initialize()
        {
            try
            {
                NVIDIA.Initialize();
                var gpus = PhysicalGPU.GetPhysicalGPUs();
                if (gpus.Length > 0)
                {
                    _gpu = gpus[0];
                    _isInitialized = true;
                }
            }
            catch { _isInitialized = false; }
        }

        public static GpuState? GetLiveTelemetry()
        {
            if (!_isInitialized || _gpu == null) return null;

            var state = new GpuState();

            try
            {
                // 1. Temperature
                var thermalSensors = _gpu.ThermalInformation.ThermalSensors.ToArray();
                if (thermalSensors.Length > 0)
                {
                    state.CoreTemp = thermalSensors[0].CurrentTemperature;
                }

                // 2. Usage Percentages
                var usage = _gpu.UsageInformation;
                state.CoreUsage = (uint)usage.GPU.Percentage;
                
                var memInfo = _gpu.MemoryInformation;
                double totalVram = memInfo.DedicatedVideoMemoryInkB;
                double availVram = memInfo.CurrentAvailableDedicatedVideoMemoryInkB;
                if (totalVram > 0) 
                {
                    state.VramUsage = (uint)(((totalVram - availVram) / totalVram) * 100);
                }

                // 3. Current Clocks (String Parsing to bypass library versioning errors)
                string? clockDump = _gpu.CurrentClockFrequencies?.ToString();
                
                if (!string.IsNullOrEmpty(clockDump))
                {
                    // Splits "[CurrentClock] 3D Graphics = 5,10,000 kHz - Memory = 90,01,000 kHz"
                    string[] clockParts = clockDump.Split('-');
                    foreach (var part in clockParts)
                    {
                        if (part.Contains("3D Graphics"))
                        {
                            // Extracts only digits (stripping out commas and 'kHz')
                            string numericVal = new string(part.Where(char.IsDigit).ToArray());
                            if (int.TryParse(numericVal, out int khz)) 
                            {
                                state.CoreClockMHz = khz / 1000;
                            }
                        }
                        else if (part.Contains("Memory"))
                        {
                            string numericVal = new string(part.Where(char.IsDigit).ToArray());
                            if (int.TryParse(numericVal, out int khz)) 
                            {
                                state.VramClockMHz = khz / 1000;
                            }
                        }
                    }
                }
            }
            catch 
            {
                // Silently swallow transient hardware polling errors
            }

            return state;
        }

        public static void Shutdown()
        {
            if (_isInitialized) NVIDIA.Unload();
        }
    }
}