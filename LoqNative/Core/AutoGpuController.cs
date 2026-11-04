
using System;
using System.Management;
using LoqNative.Core;

namespace LoqNative.Core
{
    public enum AutoGpuHealth
    {
        Unknown,
        Ready,
        Disabled,
        Error
    }

    public sealed class AutoGpuStatus
    {
        public AutoGpuHealth Health { get; init; }
        public bool? NvidiaEnabled { get; init; }
        public bool OnAcPower { get; init; }
        public string DisplayMode { get; init; } = "Not verified";
        public string Message { get; init; } = "";
    }

    public static class AutoGpuController
    {
        public static AutoGpuStatus GetStatus()
        {
            var pnp = GpuPnpManager.GetDgpuStatus();
            var battery = OsTelemetry.GetBatteryInfo();

            if (!pnp.Success || !pnp.IsEnabled.HasValue)
            {
                return new AutoGpuStatus
                {
                    Health = AutoGpuHealth.Unknown,
                    OnAcPower = battery.IsCharging,
                    Message = "Unable to determine NVIDIA device state."
                };
            }

            if (!pnp.IsEnabled.Value)
            {
                return new AutoGpuStatus
                {
                    Health = AutoGpuHealth.Disabled,
                    NvidiaEnabled = false,
                    OnAcPower = battery.IsCharging,
                    Message = "NVIDIA GPU is disabled."
                };
            }

            return new AutoGpuStatus
            {
                Health = AutoGpuHealth.Ready,
                NvidiaEnabled = true,
                OnAcPower = battery.IsCharging,
                Message = "NVIDIA GPU is available. Display switching is managed by NVIDIA."
            };
        }

        public static GpuPnpResult EnableNvidia()
        {
            return GpuPnpManager.SetDgpuState(true);
        }
    }
}
