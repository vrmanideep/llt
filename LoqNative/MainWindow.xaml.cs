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
using System.Collections.Generic;

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
        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        private const int HOTKEY_OSD_ID = 9001;
        private const uint VK_0 = 0x30; 
        private OsdWindow _osdWindow = new OsdWindow();
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_L = 0x4C; 
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MONITORPOWER = 0xF170;
        private const int MONITOR_TURN_OFF = 2;
        
        private const uint VK_S = 0x53;
        private const int HOTKEY_SLEEP_ID = 9003;

        private const uint VK_T = 0x54;
        private const int HOTKEY_REPORT_ID = 9004;

        private bool? _wasCharging = null;
        private LoqGpuMode _targetGpuMode = LoqGpuMode.Standard;
        private bool _isDgpuEnabled = false;
        private object? _lastStats;

        private DispatcherTimer? _telemetryTimer;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private LoqThermalMode _lastKnownThermalMode = (LoqThermalMode)0;
        private bool _isInitializing = false;
        private bool _isExplicitExit = false;


        private readonly SolidColorBrush _colorTransparent = new SolidColorBrush(System.Windows.Media.Colors.Transparent);
        private readonly SolidColorBrush _textActive = new SolidColorBrush(System.Windows.Media.Colors.White);
        private readonly SolidColorBrush _textInactive = new SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170));
        private readonly SolidColorBrush _colorActiveAccent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 255)); 
        
        private readonly SolidColorBrush _ledBlue = new SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 132, 255));
        private readonly SolidColorBrush _ledWhite = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
        private readonly SolidColorBrush _ledRed = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));
        private readonly SolidColorBrush _ledPurple = new SolidColorBrush(System.Windows.Media.Color.FromRgb(191, 90, 242));

        public MainWindow()
        {
            InitializeComponent();
            // Listen for Windows shutdown to prevent boot delays
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;
            
            var desktopWorkingArea = SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width - 12;
            this.Top = desktopWorkingArea.Bottom - this.Height - 12;

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

        //private bool _isMaxFanOn = false;
        private void BtnMaxFan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string reportPath =
                    LoqNative.Diagnostics.Telemetry
                        .Wmi3TelemetryDiagnostic.Run();

                System.Windows.MessageBox.Show(
                    $"WMI3 diagnostic completed.\n\nReport:\n{reportPath}",
                    "LoqNative - Telemetry",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.ToString(),
                    "Telemetry diagnostic failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW);

            RegisterHotKey(hwnd, HOTKEY_ID, MOD_ALT, VK_L); 
            RegisterHotKey(hwnd, HOTKEY_OSD_ID, MOD_CONTROL | MOD_ALT, VK_0);
            RegisterHotKey(hwnd, HOTKEY_SLEEP_ID, MOD_ALT, VK_S);
            RegisterHotKey(hwnd, HOTKEY_REPORT_ID, MOD_ALT, VK_T); 
            
            HwndSource.FromHwnd(hwnd)?.AddHook(HwndHook);
        }

        private void ShowAnimated()
        {
            this.Show();
            this.Activate();

            var slideAnim = new DoubleAnimation {
                From = 30, To = 0, Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeAnim = new DoubleAnimation {
                From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var transform = new TranslateTransform();
            this.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            this.BeginAnimation(Window.OpacityProperty, fadeAnim);
        }

        private const int WM_QUERYENDSESSION = 0x0011;

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int keyId = wParam.ToInt32();
                if (keyId == HOTKEY_ID) {
                    SetThreadExecutionState(ES_CONTINUOUS); 
                    if (this.IsVisible) this.Hide();
                    else ShowAnimated(); 
                    handled = true;
                }
                else if (keyId == HOTKEY_OSD_ID) {
                    if (_osdWindow.IsVisible) _osdWindow.Hide();
                    else _osdWindow.ShowOverlay();
                    CheckTelemetryState();
                    handled = true;
                }
                else if (keyId == HOTKEY_REPORT_ID) {
                    handled = true;
                    if (_lastStats != null) LoqNative.Core.TelemetryLogger.DumpReport(_lastStats);
                }
                else if (keyId == HOTKEY_SLEEP_ID) {
                    handled = true;
                    LockWorkStation();
                    SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)MONITOR_TURN_OFF);
                }
            }
            else if (msg == WM_QUERYENDSESSION)
            {
                // Block Windows shutdown synchronously until the dGPU is re-enabled
                if (!_isDgpuEnabled)
                {
                    try { LoqNative.Core.GpuPnpManager.SetDgpuState(true); } catch { }
                }
                handled = true;
                return (IntPtr)1; // Signal Windows it is now safe to shut down
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
                if (e.Button == System.Windows.Forms.MouseButtons.Left) {
                    if (this.IsVisible) this.Hide();
                    else ShowAnimated(); 
                }
            };
        }

        private void ReadInitialHardwareState()
        {
            // Read the firmware-reported GPU mode independently.
            _targetGpuMode = WmiEngine.GetCurrentGpuMode();

            // Read NVIDIA's Windows PnP state separately.
            // An enabled NVIDIA device does not mean the laptop is in Hybrid mode.
            var gpuStatus = GpuPnpManager.GetDgpuStatus();

            if (gpuStatus.Success && gpuStatus.IsEnabled.HasValue)
            {
                _isDgpuEnabled = gpuStatus.IsEnabled.Value;
            }

            HighlightActiveGpuButton(_targetGpuMode);
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
                
                // Point to the modular PowerControl class
                LoqNative.Hardware.PowerControl.SetThermalMode(selectedMode);
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
            
            if (activeMode == LoqThermalMode.Quiet) { BtnQuiet.Background = _ledBlue; BtnQuiet.Foreground = _textActive; }
            else if (activeMode == LoqThermalMode.Balanced) { BtnBalanced.Background = _ledWhite; BtnBalanced.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Black); }
            else if (activeMode == LoqThermalMode.Performance) { BtnPerformance.Background = _ledRed; BtnPerformance.Foreground = _textActive; }
            else if (activeMode == LoqThermalMode.Custom) { BtnCustom.Background = _ledPurple; BtnCustom.Foreground = _textActive; }
        }

        
        private async void BtnGpuMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            btn.IsEnabled = false;

            try
            {
                LoqGpuMode selectedMode = LoqGpuMode.Standard;
                bool runDriverBounce = false;

                if (btn == BtnHybrid)
                {
                    selectedMode = LoqGpuMode.Standard;
                    await Task.Run(() => {
                        LoqNative.Core.GpuPnpManager.SetDgpuState(true);
                        LoqNative.Core.WmiEngine.SetGpuMode(selectedMode);
                    });
                    runDriverBounce = true;
                }
                else if (btn == BtnIgpu)
                {
                    selectedMode = LoqGpuMode.Eco;
                    await Task.Run(() => {
                        // 1. Kill rogue apps, 2. Disable device, 3. Set WMI
                        LoqNative.Core.OsTelemetry.KillGpuApps();
                        System.Threading.Thread.Sleep(800);
                        LoqNative.Core.GpuPnpManager.SetDgpuState(false);
                        LoqNative.Core.WmiEngine.SetGpuMode(selectedMode);
                    });
                    runDriverBounce = true;
                }
                else if (btn == BtnAutoGpu)
                {
                    selectedMode = LoqGpuMode.Optimized;
                    await Task.Run(() => LoqNative.Core.AutoGpuController.EnableNvidia());
                }
                else if (btn == BtnDgpu)
                {
                    selectedMode = LoqGpuMode.Ultimate;
                    await Task.Run(() => {
                        LoqNative.Core.GpuPnpManager.SetDgpuState(true);
                        LoqNative.Core.WmiEngine.SetGpuMode(selectedMode);
                    });
                }
                else { return; }

                // Instantly bounce the graphics driver to re-hook the brightness slider
                if (runDriverBounce)
                {
                    try {
                        using var restartProc = new System.Diagnostics.Process();
                        restartProc.StartInfo.FileName = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "restart64.exe");
                        restartProc.StartInfo.UseShellExecute = true;
                        restartProc.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                        restartProc.Start();
                    } catch { }
                }

                _targetGpuMode = selectedMode;
                _isDgpuEnabled = (selectedMode != LoqGpuMode.Eco);
                HighlightActiveGpuButton(selectedMode);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"GPU routing failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }


        private void BtnForceSleep_Click(object sender, RoutedEventArgs e)
        {
            try {
                OsTelemetry.KillGpuApps();
                BtnForceSleep.Content = "✓ GPU Cleared to Sleep";
                BtnForceSleep.Foreground = new SolidColorBrush(System.Windows.Media.Colors.LightGreen);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                timer.Tick += (s, args) => {
                    BtnForceSleep.Content = "⚡ Kill Rogue GPU Apps (Force Deep Sleep)";
                    BtnForceSleep.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 199, 89));
                    timer.Stop();
                };
                timer.Start();
            }
            catch (Exception ex) {
                System.Windows.MessageBox.Show($"Failed to clear GPU processes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void HighlightActiveGpuButton(LoqGpuMode activeMode)
        {
            ResetButtonRowStyle(BtnHybrid, BtnIgpu, BtnAutoGpu, BtnDgpu);
            if (activeMode == LoqGpuMode.Standard) ApplyActiveStyle(BtnHybrid);
            else if (activeMode == LoqGpuMode.Eco) ApplyActiveStyle(BtnIgpu);
            else if (activeMode == LoqGpuMode.Optimized) ApplyActiveStyle(BtnAutoGpu);
            else if (activeMode == LoqGpuMode.Ultimate) ApplyActiveStyle(BtnDgpu);
        }
        private void BatterySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            int targetValue = (int)Math.Round(e.NewValue);
            if (BatterySlider.Value != targetValue) {
                _isInitializing = true;
                BatterySlider.Value = targetValue;
                _isInitializing = false;
            }
            LoqBatteryMode targetMode = (LoqBatteryMode)targetValue;
            UpdateBatteryText(targetMode);
            try {
                    // Point to the low-level EnergyDrv controller
                    LoqNative.Core.WmiEngine.SetBatteryMode((LoqNative.Core.LoqBatteryMode)targetValue);
                } catch (Exception ex) {
                System.Windows.MessageBox.Show($"Driver command failed: {ex.Message}\n\nYou MUST run this application as Administrator.", "Permission Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                _isInitializing = true;
                // Safely reset the slider if the command fails
                BatterySlider.Value = 0; 
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

        private void CbStartup_Click(object sender, RoutedEventArgs e) => SetStartupState(CbStartup.IsChecked == true);

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
                        System.Windows.MessageBox.Show("Failed to create Scheduled Task.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            // Synchronously re-enable the NVIDIA device before the OS powers down
            // This clears the disabled flag in the registry to prevent boot delays
            if (!_isDgpuEnabled)
            {
                try
                {
                    LoqNative.Core.GpuPnpManager.SetDgpuState(true);
                }
                catch { }
            }
        }

        
        private async Task UpdateDashboardAsync()
        {
            var stats = await Task.Run(async () =>
            {
                bool cpuTempOk = WmiEngine.TryGetFeatureValue(
                    (uint)LoqTelemetryId.CpuTemperature, out uint cpuTemp);

                bool gpuTempOk = WmiEngine.TryGetFeatureValue(
                    (uint)LoqTelemetryId.GpuTemperature, out uint gpuTemp);

                bool cpuFanOk = WmiEngine.TryGetFeatureValue(
                    (uint)LoqTelemetryId.CpuFanSpeed, out uint cpuFan);

                bool gpuFanOk = WmiEngine.TryGetFeatureValue(
                    (uint)LoqTelemetryId.GpuFanSpeed, out uint gpuFan);

                var battery = OsTelemetry.GetBatteryInfo();

                if (_wasCharging == null)
                {
                    _wasCharging = battery.IsCharging;
                }
                else if (battery.IsCharging != _wasCharging)
                {
                    _wasCharging = battery.IsCharging;
                }

                

                var nvGpu = OsTelemetry.GetNvidiaGpuStats();
                uint amdUsage = OsTelemetry.GetAmdIgpuUsage();
                var ram = OsTelemetry.GetRamDetails();

                return new
                {
                    CpuTemp = cpuTemp,
                    CpuTempOk = cpuTempOk,
                    CpuFan = cpuFan,
                    CpuFanOk = cpuFanOk,

                    IgpuUsage = amdUsage,
                    IgpuTemp = cpuTemp,

                    DgpuTemp = gpuTemp,
                    DgpuTempOk = gpuTempOk,
                    DgpuUsage = nvGpu.Usage,
                    DgpuWattage = nvGpu.Wattage,
                    GpuFan = gpuFan,
                    GpuFanOk = gpuFanOk,

                    CpuUsage = OsTelemetry.GetCpuUsage(),
                    CpuClock = OsTelemetry.GetLiveCpuClockMhz(),

                    Ram = ram,
                    Battery = battery,
                    LiveThermalMode = WmiEngine.GetCurrentThermalMode()
                };
            });

            _lastStats = stats;

            double calcCpuW = (stats.CpuUsage / 100.0) * 45.0 + 5.0;

            double totalEstimatedWallPower = 0;

            if (stats.Battery.Wattage > 0)
            {
                double baseSystemPower = 15.0;

                totalEstimatedWallPower = stats.Battery.IsCharging
                    ? stats.Battery.Wattage + baseSystemPower + calcCpuW + stats.DgpuWattage
                    : stats.Battery.Wattage;
            }

            if (_osdWindow.IsVisible)
            {
                var fpsData = EtwFpsMonitor.GetFps();

                _osdWindow.UpdateStats(
                    fpsData.Avg,
                    fpsData.Low,
                    stats.CpuTemp,
                    stats.DgpuTemp,
                    stats.CpuFan,
                    stats.GpuFan,
                    stats.CpuUsage,
                    stats.DgpuUsage,
                    stats.DgpuWattage,
                    calcCpuW);
            }

            if (!this.IsVisible) return;

            bool isDgpuAwake = _isDgpuEnabled;
            bool isIgpuAwake = true;

            if (isDgpuAwake && isIgpuAwake)
            {
                IgpuColumn.Width = new GridLength(1, GridUnitType.Star);
                DgpuColumn.Width = new GridLength(1, GridUnitType.Star);
                GpuSplitterColumn.Width = GridLength.Auto;

                IgpuPanel.Visibility = Visibility.Visible;
                DgpuPanel.Visibility = Visibility.Visible;
                GpuSplitter.Visibility = Visibility.Visible;
            }
            else if (isDgpuAwake)
            {
                IgpuColumn.Width = new GridLength(0);
                DgpuColumn.Width = new GridLength(1, GridUnitType.Star);
                GpuSplitterColumn.Width = new GridLength(0);

                IgpuPanel.Visibility = Visibility.Collapsed;
                DgpuPanel.Visibility = Visibility.Visible;
                GpuSplitter.Visibility = Visibility.Collapsed;
            }
            else
            {
                IgpuColumn.Width = new GridLength(1, GridUnitType.Star);
                DgpuColumn.Width = new GridLength(0);
                GpuSplitterColumn.Width = new GridLength(0);

                IgpuPanel.Visibility = Visibility.Visible;
                DgpuPanel.Visibility = Visibility.Collapsed;
                GpuSplitter.Visibility = Visibility.Collapsed;
            }

            IgpuData1.Text = stats.CpuTempOk
                ? $"{stats.IgpuTemp}°C"
                : "--°C";

            IgpuData2.Text = stats.CpuFanOk
                ? $"{stats.CpuFan} RPM"
                : "-- RPM";

            IgpuData3.Text = $"{stats.IgpuUsage}%";

            DgpuData1.Text = stats.DgpuTempOk
                ? $"{stats.DgpuTemp}°C"
                : "--°C";

            DgpuData2.Text = stats.GpuFanOk
                ? $"{stats.GpuFan} RPM"
                : "-- RPM";

            DgpuData3.Text =
                $"{stats.DgpuUsage}% | {stats.DgpuWattage:0.0} W";

            CpuData1.Text = stats.CpuTempOk
                ? $"{stats.CpuTemp}°C"
                : "--°C";

            CpuData2.Text = stats.CpuFanOk
                ? $"{stats.CpuFan} RPM"
                : "-- RPM";

            CpuData3.Text =
                $"{stats.CpuUsage}% | {(stats.CpuClock / 1000.0):0.00} GHz";

            RamData1.Text =
                $"{stats.Ram.UsedGB:0.0} / {stats.Ram.TotalGB:0.0}";

            string battStatus =
                stats.Battery.IsCharging ? "Charging" : "Discharging";

            BatteryWattage.Text = totalEstimatedWallPower > 0
                ? $"Power: {totalEstimatedWallPower:0.0} W"
                : $"{battStatus}: -- W";

            BatteryPercent.Text =
                $"Charge: {stats.Battery.Percent:0.0}%";

            HighlightActiveThermalButton(stats.LiveThermalMode);
        }


        protected override void OnClosed(EventArgs e)
        {
            SystemEvents.SessionEnding -= SystemEvents_SessionEnding; // Remove hook
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);
            UnregisterHotKey(hwnd, HOTKEY_REPORT_ID);
            UnregisterHotKey(hwnd, HOTKEY_SLEEP_ID);
            _notifyIcon?.Dispose(); 
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

        private void TestDgpuDetection()
        {
            var result = GpuPnpManager.GetDgpuStatus();

            System.Windows.MessageBox.Show(
                $"Success: {result.Success}\n" +
                $"Enabled: {result.IsEnabled}\n" +
                $"Device ID: {result.DeviceId}\n" +
                $"Message: {result.Message}",
                "dGPU Detection"
            );
        }
    }
}