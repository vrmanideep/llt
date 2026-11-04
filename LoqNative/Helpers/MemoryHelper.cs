using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LoqNative.Helpers
{
    public static class MemoryHelper
    {
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, int minSize, int maxSize);

        public static void Trim()
        {
            // Force .NET to clean up dead objects
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            // Force Windows to flush the working set RAM to disk
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
        }
    }
}