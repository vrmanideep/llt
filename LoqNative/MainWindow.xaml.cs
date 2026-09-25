using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using LoqNative.Core;

namespace LoqNative
{
    public partial class MainWindow : Window
    {
        // --- WINDOWS API FOR STYLING & HOTKEYS ---
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        private const int HOTKEY_OSD_ID = 9001;
        private const uint VK_0 = 0x30; // '0' key -> Shortcut is Ctrl + Alt + 0
        private OsdWindow _osdWindow = new OsdWindow();
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_L = 0x4C; // 'L' key -> Shortcut is Ctrl + Alt + L

        // Key code for 'T' (Telemetry Report)
        private const uint VK_T = 0x54;
        private const int HOTKEY_REPORT_ID = 9004;
        private object? _lastStats;

        private DispatcherTimer? _telemetryTimer;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private LoqThermalMode _lastKnownThermalMode = (LoqThermalMode)0;
        private bool _isInitializing = false;
        private bool _isExplicitExit = false;

        // --- UPDATED STYLING COLORS (Hardware LED Sync) ---
        private readonly SolidColorBrush _colorTransparent = new SolidColorBrush(System.Windows.Media.Colors.Transparent);
        private readonly SolidColorBrush _textActive = new SolidColorBrush(System.Windows.Media.Colors.White);
        private readonly SolidColorBrush _textInactive = new SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170));
        private readonly SolidColorBrush _colorActiveAccent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 255)); 
        
        // Physical Power Button Colors
        private readonly SolidColorBrush _ledBlue = new SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 132, 255));
        private readonly SolidColorBrush _ledWhite = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
        private readonly SolidColorBrush _ledRed = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));
        private readonly SolidColorBrush _ledPurple = new SolidColorBrush(System.Windows.Media.Color.FromRgb(191, 90, 242));

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize NVAPI Telemetry Engine
            GpuTelemetryEngine.Initialize();
            
            var desktopWorkingArea = SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width - 12;
            this.Top = desktopWorkingArea.Bottom - this.Height - 12;

            // Automatically hide the app when you click away from it
            this.Deactivated += (s, e) => this.Hide();

            SetupSystemTrayIcon();
            ReadInitialHardwareState();
            
            this.IsVisibleChanged += (s, e) => 
            {
                if (this.IsVisible) {
                    _telemetryTimer?.Start();
                    _ = UpdateDashboardAsync(); 
                } else {
                    _telemetryTimer?.Stop();
                }
            };

            StartTelemetryEngine();
            EtwFpsMonitor.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW);

            // Existing Hotkeys
            RegisterHotKey(hwnd, HOTKEY_ID, MOD_ALT, VK_L); 
            RegisterHotKey(hwnd, HOTKEY_OSD_ID, MOD_CONTROL | MOD_ALT, VK_0);
            
            // New Telemetry Report Hotkey (Alt + T)
            RegisterHotKey(hwnd, HOTKEY_REPORT_ID, MOD_ALT, VK_T); 
            
            HwndSource.FromHwnd(hwnd)?.AddHook(HwndHook);
        }

        private void ShowAnimated()
        {
            this.Show();
            this.Activate();

            var slideAnim = new DoubleAnimation {
                From = 30, To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeAnim = new DoubleAnimation {
                From = 0, To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var transform = new TranslateTransform();
            this.RenderTransform = transform;

            transform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            this.BeginAnimation(Window.OpacityProperty, fadeAnim);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int keyId = wParam.ToInt32();
                if (keyId == HOTKEY_ID) {
                    // Release the sleep lock so the laptop can sleep normally again
                    SetThreadExecutionState(ES_CONTINUOUS); 

                    if (this.IsVisible) this.Hide();
                    else ShowAnimated(); 
                    handled = true;
                }
                else if (keyId == HOTKEY_OSD_ID) {
                    if (_osdWindow.IsVisible) _osdWindow.Hide();
                    else _osdWindow.Show();
                    CheckTelemetryState();
                    handled = true;
                }
                else if (keyId == HOTKEY_REPORT_ID) {
                    handled = true;
                    if (_lastStats != null) LoqNative.Core.TelemetryLogger.DumpReport(_lastStats);
                }
            }
            return IntPtr.Zero;
        }

        private void StartTelemetryEngine()
        {
            _telemetryTimer = new DispatcherTimer();
            _telemetryTimer.Interval = TimeSpan.FromSeconds(1.5); 
            _telemetryTimer.Tick += async (s, e) => await UpdateDashboardAsync();
            
            this.IsVisibleChanged += (s, e) => CheckTelemetryState();
            _osdWindow.IsVisibleChanged += (s, e) => CheckTelemetryState();
            
            CheckTelemetryState();
        }

        private void CheckTelemetryState()
        {
            if (this.IsVisible || _osdWindow.IsVisible) {
                _telemetryTimer?.Start();
                _ = UpdateDashboardAsync();
            } else {
                _telemetryTimer?.Stop();
            }
        }

        private void SetupSystemTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application; 
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "LOQ Native Control";
            
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Exit Dashboard", null, (s, e) => {
                _isExplicitExit = true;
                _notifyIcon.Dispose();
                System.Windows.Application.Current.Shutdown();
            });
            _notifyIcon.ContextMenuStrip = contextMenu;

            _notifyIcon.MouseClick += (s, e) => {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    if (this.IsVisible) this.Hide();
                    else ShowAnimated(); 
                }
            };
        }

        private void ReadInitialHardwareState()
        {
            _isInitializing = true;

            HighlightActiveThermalButton(WmiEngine.GetCurrentThermalMode());
            HighlightActiveGpuButton(WmiEngine.GetCurrentGpuMode());
            
            LoqBatteryMode currentBatteryMode = WmiEngine.GetBatteryMode();
            BatterySlider.Value = (double)currentBatteryMode;
            UpdateBatteryText(currentBatteryMode);
            
            CbStartup.IsChecked = GetStartupState();

            _isInitializing = false;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Hide();

        private void BtnThermalMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;

            try {
                LoqThermalMode selectedMode = LoqThermalMode.Balanced;

                if (btn == BtnQuiet) selectedMode = LoqThermalMode.Quiet;
                else if (btn == BtnBalanced) selectedMode = LoqThermalMode.Balanced;
                else if (btn == BtnPerformance) selectedMode = LoqThermalMode.Performance;
                else if (btn == BtnCustom) selectedMode = LoqThermalMode.Custom;

                WmiEngine.SetThermalMode(selectedMode);
                HighlightActiveThermalButton(selectedMode);
            } catch (Exception ex) {
                System.Windows.MessageBox.Show($"Mode change failed: {ex.Message}", "WMI Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void HighlightActiveThermalButton(LoqThermalMode activeMode)
        {
            if (_lastKnownThermalMode == activeMode) return;
            _lastKnownThermalMode = activeMode;

            ResetButtonRowStyle(BtnQuiet, BtnBalanced, BtnPerformance, BtnCustom);
            
            if (activeMode == LoqThermalMode.Quiet) {
                BtnQuiet.Background = _ledBlue;
                BtnQuiet.Foreground = _textActive;
            }
            else if (activeMode == LoqThermalMode.Balanced) {
                BtnBalanced.Background = _ledWhite;
                BtnBalanced.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Black);
            }
            else if (activeMode == LoqThermalMode.Performance) {
                BtnPerformance.Background = _ledRed;
                BtnPerformance.Foreground = _textActive;
            }
            else if (activeMode == LoqThermalMode.Custom) {
                BtnCustom.Background = _ledPurple;
                BtnCustom.Foreground = _textActive;
            }
        }

        private void BtnGpuMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;

            try {
                LoqGpuMode selectedMode = LoqGpuMode.Hybrid;

                if (btn == BtnHybrid) selectedMode = LoqGpuMode.Hybrid;
                else if (btn == BtnIgpu) selectedMode = LoqGpuMode.IGPUOnly;
                else if (btn == BtnAutoGpu) selectedMode = LoqGpuMode.Auto;
                else if (btn == BtnDgpu) selectedMode = LoqGpuMode.DGPU;

                WmiEngine.SetGpuMode(selectedMode);
                HighlightActiveGpuButton(selectedMode);

                if (selectedMode == LoqGpuMode.DGPU || WmiEngine.GetCurrentGpuMode() == LoqGpuMode.DGPU) {
                    System.Windows.MessageBox.Show("Display layout changes require a system reboot to alter physical MUX routing.", "Hardware Switch Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            } catch (Exception ex) {
                System.Windows.MessageBox.Show($"GPU routing execution failed: {ex.Message}", "WMI Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnForceSleep_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OsTelemetry.KillGpuApps();

                BtnForceSleep.Content = "✓ GPU Cleared to Sleep";
                BtnForceSleep.Foreground = new SolidColorBrush(System.Windows.Media.Colors.LightGreen);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                timer.Tick += (s, args) =>
                {
                    BtnForceSleep.Content = "⚡ Kill Rogue GPU Apps (Force Deep Sleep)";
                    BtnForceSleep.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 199, 89));
                    timer.Stop();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to clear GPU processes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void HighlightActiveGpuButton(LoqGpuMode activeMode)
        {
            ResetButtonRowStyle(BtnHybrid, BtnIgpu, BtnAutoGpu, BtnDgpu);
            if (activeMode == LoqGpuMode.Hybrid) ApplyActiveStyle(BtnHybrid);
            else if (activeMode == LoqGpuMode.IGPUOnly) ApplyActiveStyle(BtnIgpu);
            else if (activeMode == LoqGpuMode.Auto) ApplyActiveStyle(BtnAutoGpu);
            else if (activeMode == LoqGpuMode.DGPU) ApplyActiveStyle(BtnDgpu);
        }

        private void BatterySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            
            int targetValue = (int)Math.Round(e.NewValue);

            if (BatterySlider.Value != targetValue)
            {
                _isInitializing = true;
                BatterySlider.Value = targetValue;
                _isInitializing = false;
            }

            LoqBatteryMode targetMode = (LoqBatteryMode)targetValue;
            UpdateBatteryText(targetMode);

            try {
                WmiEngine.SetBatteryMode(targetMode);
            } catch (Exception ex) {
                System.Windows.MessageBox.Show($"Driver command failed: {ex.Message}\n\nYou MUST run this application as an Administrator to control the Energy Driver.", "Permission Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                _isInitializing = true;
                BatterySlider.Value = (double)WmiEngine.GetBatteryMode(); 
                _isInitializing = false;
            }
        }

        private void UpdateBatteryText(LoqBatteryMode mode)
        {
            BatteryLimitText.Text = mode switch {
                LoqBatteryMode.Conservation => "Power Mode: Conservation (80%)",
                LoqBatteryMode.RapidCharge => "Power Mode: Rapid Charge",
                _ => "Power Mode: Normal (100%)"
            };
        }

        private void CbStartup_Click(object sender, RoutedEventArgs e)
        {
            SetStartupState(CbStartup.IsChecked == true);
        }

        private bool GetStartupState()
        {
            try {
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "schtasks.exe";
                process.StartInfo.Arguments = "/query /tn \"LoqNative_Startup\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0;
            } catch { return false; }
        }

        private void SetStartupState(bool enable)
        {
            try {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue("LoqNative", false);
            } catch { }

            try {
                if (enable) {
                    string? exePath = Environment.ProcessPath;
                    
                    using var pCreate = new System.Diagnostics.Process();
                    pCreate.StartInfo.FileName = "schtasks.exe";
                    pCreate.StartInfo.Arguments = $"/create /tn \"LoqNative_Startup\" /tr \"\\\"{exePath}\\\" --silent\" /sc onlogon /rl highest /f";
                    pCreate.StartInfo.UseShellExecute = false;
                    pCreate.StartInfo.CreateNoWindow = true;
                    pCreate.Start();
                    pCreate.WaitForExit();

                    if (pCreate.ExitCode != 0) {
                        System.Windows.MessageBox.Show("Failed to create Scheduled Task. Ensure the app is currently running as Administrator.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    using var pPatch = new System.Diagnostics.Process();
                    pPatch.StartInfo.FileName = "powershell.exe";
                    pPatch.StartInfo.Arguments = "-NoProfile -WindowStyle Hidden -Command \"Set-ScheduledTask -TaskName 'LoqNative_Startup' -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0)\"";
                    pPatch.StartInfo.UseShellExecute = false;
                    pPatch.StartInfo.CreateNoWindow = true;
                    pPatch.Start();
                    pPatch.WaitForExit();
                } else {
                    using var pDelete = new System.Diagnostics.Process();
                    pDelete.StartInfo.FileName = "schtasks.exe";
                    pDelete.StartInfo.Arguments = "/delete /tn \"LoqNative_Startup\" /f";
                    pDelete.StartInfo.UseShellExecute = false;
                    pDelete.StartInfo.CreateNoWindow = true;
                    pDelete.Start();
                    pDelete.WaitForExit();
                }
            } catch (Exception ex) {
                System.Windows.MessageBox.Show($"Failed to configure startup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ResetButtonRowStyle(params System.Windows.Controls.Button[] buttons)
        {
            foreach (var btn in buttons) {
                btn.Background = _colorTransparent;
                btn.Foreground = _textInactive;
            }
        }

        private void ApplyActiveStyle(System.Windows.Controls.Button button)
        {
            button.Background = _colorActiveAccent;
            button.Foreground = _textActive;
        }

        private async Task UpdateDashboardAsync()
        {
            var nvGpuState = GpuTelemetryEngine.GetLiveTelemetry();

            var stats = await Task.Run(() => 
            {
                uint fallbackGpuTemp = WmiEngine.GetFeatureValue((uint)LoqTelemetryId.GpuTemperature);
                var battery = OsTelemetry.GetBatteryInfo();
                var ram = OsTelemetry.GetRamDetails();
                
                return new 
                {
                    CpuTemp = WmiEngine.GetFeatureValue((uint)LoqTelemetryId.CpuTemperature),
                    CpuFan = WmiEngine.GetFeatureValue((uint)LoqTelemetryId.CpuFanSpeed),
                    GpuFan = WmiEngine.GetFeatureValue((uint)LoqTelemetryId.GpuFanSpeed),
                    CpuUsage = OsTelemetry.GetCpuUsage(),
                    CpuClock = OsTelemetry.GetCpuClock(),
                    Ram = ram,
                    Battery = battery,
                    LiveThermalMode = WmiEngine.GetCurrentThermalMode(),
                    
                    GpuTemp = nvGpuState?.CoreTemp > 0 ? (uint)nvGpuState.CoreTemp : fallbackGpuTemp,
                    GpuUsage = nvGpuState != null ? nvGpuState.CoreUsage : OsTelemetry.GetNvidiaGpuStats(fallbackGpuTemp).Usage,
                    GpuCoreClock = nvGpuState?.CoreClockMHz ?? 0,
                    GpuVramUsage = nvGpuState?.VramUsage ?? 0,
                    GpuWattage = OsTelemetry.GetNvidiaGpuStats(fallbackGpuTemp).Wattage
                };
            });

            _lastStats = stats;

            double calcCpuW = !stats.Battery.IsCharging && stats.Battery.Wattage > 0 
                ? Math.Max(0, stats.Battery.Wattage - stats.GpuWattage - 10) 
                : 0;

            if (_osdWindow.IsVisible) {
                var fpsData = EtwFpsMonitor.GetFps();
                _osdWindow.UpdateStats(fpsData.Avg, fpsData.Low, stats.CpuTemp, stats.GpuTemp, stats.CpuFan, stats.GpuFan, stats.CpuUsage, stats.GpuUsage, stats.GpuWattage, calcCpuW);
            }

            if (!this.IsVisible) return; 

            CpuData1.Text = stats.CpuTemp > 0 ? $"{stats.CpuTemp}°C" : "--°C";
            CpuData2.Text = stats.CpuFan > 0 ? $"{stats.CpuFan} RPM" : "0 RPM";
            CpuData3.Text = $"{stats.CpuUsage}% | {(stats.CpuClock / 1000.0):0.00} GHz";

            RamData1.Text = $"{stats.Ram.UsedGB:0.0} / {stats.Ram.TotalGB:0.0}";

            GpuData1.Text = stats.GpuTemp > 0 ? $"{stats.GpuTemp}°C" : "--°C";
            GpuData2.Text = stats.GpuFan > 0 ? $"{stats.GpuFan} RPM" : "0 RPM";
            GpuData3.Text = $"{stats.GpuUsage}%";

            string battStatus = stats.Battery.IsCharging ? "Charging" : "Discharging";
            BatteryWattage.Text = stats.Battery.Wattage > 0 ? $"{battStatus}: {stats.Battery.Wattage:0.0} W" : $"{battStatus}: -- W";
            BatteryPercent.Text = $"Charge: {stats.Battery.Percent:0.0}%";

            HighlightActiveThermalButton(stats.LiveThermalMode);
        }

        protected override void OnClosed(EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);
            UnregisterHotKey(hwnd, HOTKEY_REPORT_ID);
            _notifyIcon?.Dispose(); 
            
            GpuTelemetryEngine.Shutdown();
            
            base.OnClosed(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                base.OnClosing(e);
            }
        }
    }
}