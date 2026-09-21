using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace LoqNative
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. Auto-Elevate to Administrator (and pass forward any arguments like --silent)
            if (!IsAdministrator())
            {
                var exeName = Process.GetCurrentProcess().MainModule?.FileName;
                if (exeName != null)
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo(exeName)
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        Arguments = string.Join(" ", e.Args) 
                    };
                    
                    try { Process.Start(startInfo); } catch { }
                }
                Environment.Exit(0);
                return;
            }

            // 2. Enforce Single Instance Lock
            const string appName = "LoqNative_SingleInstance_Mutex";
            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                Current.Shutdown();
                return;
            }

            // 3. Intercept the launch to handle the Silent Boot flag
            MainWindow = new MainWindow();
            
            if (!e.Args.Contains("--silent"))
            {
                MainWindow.Show();
            }
        }

        private bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}