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
        private static PhysicalGPU _gpu;
        private static bool _isInitialized = false;

        public static void Initialize()
        {
            try
            {
                NVIDIA.Initialize();
                _gpu = PhysicalGPU.GetPhysicalGPUs().FirstOrDefault();
                if (_gpu != null) _isInitialized = true;
            }
            catch { _isInitialized = false; }
        }

        public static GpuState GetLiveTelemetry()
        {
            if (!_isInitialized || _gpu == null) return null;

            var state = new GpuState();

            try
            {
                // 1. Temperature (Safely pulling the first available sensor)
                var thermalSensors = _gpu.ThermalInformation.ThermalSensors.ToArray();
                if (thermalSensors.Length > 0)
                {
                    state.CoreTemp = thermalSensors[0].CurrentTemperature;
                }

                // 2. Usage Percentages
                var usage = _gpu.UsageInformation;
                state.CoreUsage = (uint)usage.GPU.Percentage;
                
                // Calculate VRAM usage based on Total minus Available
                var memInfo = _gpu.MemoryInformation;
                double totalVram = memInfo.DedicatedVideoMemoryInkB;
                double availVram = memInfo.CurrentAvailableDedicatedVideoMemoryInkB;
                state.VramUsage = (uint)(((totalVram - availVram) / totalVram) * 100);

                // 3. Current Clocks (Converting kHz to MHz)
                var clocks = _gpu.CurrentClockFrequencies;
                
                var coreClock = clocks.FirstOrDefault(c => c.Key == NvAPIWrapper.Native.GPU.PublicClockDomain.Graphics);
                if (coreClock.Value != null) state.CoreClockMHz = (int)(coreClock.Value.FrequencyInkHz / 1000);

                var memClock = clocks.FirstOrDefault(c => c.Key == NvAPIWrapper.Native.GPU.PublicClockDomain.Memory);
                if (memClock.Value != null) state.VramClockMHz = (int)(memClock.Value.FrequencyInkHz / 1000);
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