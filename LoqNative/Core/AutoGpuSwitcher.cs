using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace LoqNative.Core;

public static class AutoGpuSwitcher
{
    private static bool _isRunning = false;
    private static int _trackedGamePid = 0;
    
    // Blacklist basic UI apps that use hardware acceleration so they don't wake the dGPU
    private static readonly HashSet<string> Blacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "explorer", "chrome", "msedge", "discord", "code", "steam", "taskmgr", "loqnative",
        "devenv", "textinputhost", "applicationframehost"
    };

    public static void StartWatching()
    {
        if (_isRunning) return;
        _isRunning = true;
        
        // Ensure we start in Eco mode on boot
        WmiEngine.SetGpuMode(LoqGpuMode.Eco);
        
        Task.Run(async () =>
        {
            while (_isRunning)
            {
                CheckProcesses();
                await Task.Delay(3000); // Scan every 3 seconds
            }
        });
    }

    private static void CheckProcesses()
    {
        try
        {
            if (_trackedGamePid == 0)
            {
                // Poll the ETW DirectX event listener for any app generating frames
                var fpsData = EtwFpsMonitor.GetRawData();
                
                // If an app is pushing frames to the screen...
                if (fpsData.Fps > 15 && fpsData.Pid > 0)
                {
                    using var proc = Process.GetProcessById(fpsData.Pid);
                    string pName = proc.ProcessName.ToLower();

                    // ...and it is NOT a basic desktop app, it must be a game or 3D render
                    if (!Blacklist.Contains(pName))
                    {
                        _trackedGamePid = fpsData.Pid;
                        
                        // Wake up the NVIDIA dGPU (Standard / Hybrid Mode)
                        WmiEngine.SetGpuMode(LoqGpuMode.Standard);
                    }
                }
            }
            else
            {
                // We are currently tracking an active game. Check if it has closed.
                bool gameStillRunning = false;
                try
                {
                    var proc = Process.GetProcessById(_trackedGamePid);
                    gameStillRunning = !proc.HasExited;
                }
                catch
                {
                    // If GetProcessById throws, the game process no longer exists
                    gameStillRunning = false;
                }

                if (!gameStillRunning)
                {
                    _trackedGamePid = 0;
                    
                    // Wait 5 seconds for cloud saves or launchers to finish syncing
                    System.Threading.Thread.Sleep(5000); 
                    
                    // Nuke lingering NVIDIA apps and power down to Eco
                    OsTelemetry.KillGpuApps(); 
                    WmiEngine.SetGpuMode(LoqGpuMode.Eco); 
                }
            }
        }
        catch { }
    }
}