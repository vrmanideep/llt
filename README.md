
# LoqNative (LLT)

A lightweight, native hardware control dashboard for Lenovo LOQ laptops (specifically tested on the LOQ 15AHP10). It interfaces directly with Windows WMI and Lenovo's Energy Driver to manage thermal profiles, battery limits, and GPU states without relying on heavy OEM software like Lenovo Vantage.

## Key Features
* **Thermal Management:** Toggle between Quiet, Balanced, Performance, and Custom modes. Syncs directly with the physical motherboard LED (Blue, White, Red, Purple).
* **Battery Conservation:** Instantly toggle the 80% charge limit or Rapid Charge.
* **GPU Control & Deep Sleep:** Switch MUX states (Hybrid, iGPU, dGPU). Includes a one-click NVML process killer to force the NVIDIA GPU into the D3Cold (0.0W) deep sleep state.
* **Telemetry OSD:** In-game transparent overlay (Ctrl + Alt + 0) monitoring CPU/GPU temperatures, active wattage, fan speeds, and 1% low FPS.
* **Unrestricted Startup:** Uses a custom PowerShell bridge to bypass Windows Task Scheduler battery restrictions, ensuring the dashboard boots seamlessly even when the laptop is unplugged.
* **Instant Screen Off:** Global hotkey (Alt + S) directly cuts display power without entering Modern Standby or suspending background tasks.
* **Quick Toggle:** Global hotkey (Alt + L) to instantly show or hide the hardware dashboard.

## Requirements
* Windows 11
* .NET 8.0 SDK
* Administrator privileges (required for WMI and Energy Driver communication)

## Build and Run
1. Clone the repository:
   ```bash
   git clone [https://github.com/vrmanideep/llt.git](https://github.com/vrmanideep/llt.git)



2. Navigate to the project directory and run:
    ```bash
    cd llt
    dotnet run

    ```
