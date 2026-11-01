using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LoqNative.Core;

public static class StartupManager
{
    private const string TaskName = "LoqNative_Startup";

    public static void Enable()
    {
        try {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            // 1. Wipe any old task to prevent duplicates
            Disable();

            // 2. Create the Scheduled Task with Highest Privileges to bypass UAC popup
            using var pCreate = new Process();
            pCreate.StartInfo.FileName = "schtasks.exe";
            pCreate.StartInfo.Arguments = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\" --silent\" /sc onlogon /rl highest /f";
            pCreate.StartInfo.UseShellExecute = false;
            pCreate.StartInfo.CreateNoWindow = true;
            pCreate.Start();
            pCreate.WaitForExit();

            // 3. Patch the task so Windows doesn't block it from starting while on battery power
            using var pPatch = new Process();
            pPatch.StartInfo.FileName = "powershell.exe";
            pPatch.StartInfo.Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Set-ScheduledTask -TaskName '{TaskName}' -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0)\"";
            pPatch.StartInfo.UseShellExecute = false;
            pPatch.StartInfo.CreateNoWindow = true;
            pPatch.Start();
            pPatch.WaitForExit();
        } catch { }
    }

    public static void Disable()
    {
        try {
            using var pDelete = new Process();
            pDelete.StartInfo.FileName = "schtasks.exe";
            pDelete.StartInfo.Arguments = $"/delete /tn \"{TaskName}\" /f";
            pDelete.StartInfo.UseShellExecute = false;
            pDelete.StartInfo.CreateNoWindow = true;
            pDelete.Start();
            pDelete.WaitForExit();
        } catch { }
    }

    public static void EnsureStartMenuShortcut()
    {
        try
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            string programsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string shortcutPath = Path.Combine(programsFolder, "LoqNative.lnk");

            if (!File.Exists(shortcutPath))
            {
                IShellLink link = (IShellLink)new ShellLink();
                link.SetDescription("LoqNative Control Panel");
                link.SetPath(exePath);
                link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? string.Empty);

                IPersistFile file = (IPersistFile)link;
                file.Save(shortcutPath, false);
            }
        }
        catch { }
    }

    // --- Native COM Interop for Shortcut Creation ---
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}