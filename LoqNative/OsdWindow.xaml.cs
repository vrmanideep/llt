using System;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace LoqNative
{
    public partial class OsdWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020; // Win32 Ghost Mode
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        public OsdWindow()
        {
            InitializeComponent();
            
            // Pin to top-left corner
            this.Left = 20; 
            this.Top = 20;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            
            // Bypass WPF and force the window into a native unclickable state
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        }

        public void ShowOverlay()
        {
            this.Show();
        }
        // Catch the telemetry stream sent by MainWindow.xaml.cs
        public void UpdateStats(string fpsAvg, string fpsLow, uint cpuTemp, uint gpuTemp, uint cpuFan, uint gpuFan, uint cpuUsage, uint gpuUsage, double gpuWattage, double cpuWattage)
        {
            Dispatcher.Invoke(() => {
                TxtFps.Text = $"{fpsAvg} FPS";
                TxtFpsLow.Text = $"{fpsLow} (1% Low)"; 
                
                TxtGpu.Text = $"GPU: {gpuTemp}°C | {gpuWattage:0.0}W | {gpuFan} RPM";
                TxtCpu.Text = $"CPU: {cpuTemp}°C | {cpuWattage:0.0}W | {cpuFan} RPM";
            });
        }
    }
}