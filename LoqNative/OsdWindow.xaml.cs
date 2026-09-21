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

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        public OsdWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        }

        public void UpdateStats(string fps, string low1, uint cpuTemp, uint gpuTemp, uint cpuFan, uint gpuFan, uint cpuUsg, uint gpuUsg, double gpuW, double cpuW)
        {
            TxtBig.Text = fps; 
            TxtLow.Text = $"{low1} (1%)";
            
            // The ,4 and ,3 keep the spacing perfectly rigid so the UI doesn't bounce around
            TxtGpu.Text = $"GPU: {gpuTemp}°  {gpuFan,4} RPM  {gpuUsg,3}%  {gpuW,5:0.0} W"; 

            string cpuWStr = cpuW > 0 ? $"{cpuW,5:0.0} W" : " --.- W";
            TxtCpu.Text = $"CPU: {cpuTemp}°  {cpuFan,4} RPM  {cpuUsg,3}%  {cpuWStr}";
        }
    }
}