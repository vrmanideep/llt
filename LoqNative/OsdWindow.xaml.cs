using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LoqNative
{
    public partial class OsdWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;   // Click-through
        private const int WS_EX_TOOLWINDOW = 0x00000080;    // Hide from Alt-Tab
        private const int WS_EX_NOACTIVATE = 0x08000000;    // NEVER steal focus from games
        private const int WS_EX_TOPMOST = 0x00000008;       // Force topmost at OS level

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        public OsdWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            
            // Apply the stealth/overlay flags
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOPMOST);
            
            // Force it above the DirectX swap chain
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public void UpdateStats(string fps, string low1, uint cpuTemp, uint gpuTemp, uint cpuFan, uint gpuFan, uint cpuUsg, uint gpuUsg, double gpuW, double cpuW)
        {
            TxtBig.Text = fps; 
            TxtLow.Text = $"{low1} (1%)";
            
            TxtGpu.Text = $"GPU: {gpuTemp}°  {gpuFan,4} RPM  {gpuUsg,3}%  {gpuW,5:0.0} W"; 

            string cpuWStr = cpuW > 0 ? $"{cpuW,5:0.0} W" : " --.- W";
            TxtCpu.Text = $"CPU: {cpuTemp}°  {cpuFan,4} RPM  {cpuUsg,3}%  {cpuWStr}";
        }

        public void ShowOverlay()
        {
            this.Show();
            // Re-assert topmost position in case the game tried to draw over it
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}