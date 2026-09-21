using System;
using System.Net;
using System.Text;
using System.Management;
using System.Security.Principal;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LoqHardwareControl
{
    class Program
    {
        // Link your Assembly Telemetry DLL
        [DllImport("telemetry.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong GetCpuCycles();

        // Ensure the server has permission to bind to Port 5050
        static bool IsAdministrator() {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static void RelaunchAsAdmin() {
            var exeName = Process.GetCurrentProcess().MainModule.FileName;
            try { Process.Start(new ProcessStartInfo(exeName) { UseShellExecute = true, Verb = "runas" }); } catch { }
            Environment.Exit(0);
        }

        static void Main()
        {
            if (!IsAdministrator()) RelaunchAsAdmin();

            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:5050/");
            
            try { 
                listener.Start(); 
                Console.WriteLine("======================================");
                Console.WriteLine(" LOQ C# ENGINE ONLINE (PORT 5050)");
                Console.WriteLine(" Waiting for Flutter commands...");
                Console.WriteLine("======================================");
            }
            catch (Exception ex) { 
                Console.WriteLine("Network Port Error: " + ex.Message); 
                return; 
            }

            while (true)
            {
                var context = listener.GetContext();
                var request = context.Request;
                var response = context.Response;
                
                string path = request.Url.AbsolutePath.ToLower();
                string resJson = "{\"status\":\"ok\"}";

                try
                {
                    // 1. THERMAL MODES (1: Quiet, 2: Balance, 3: Performance)
                    if (path == "/api/thermals")
                    {
                        uint mode = uint.Parse(request.QueryString["mode"]);
                        SetWmi("SetSmartFanMode", mode);
                        Console.WriteLine("[KERNEL] Thermal Mode switched to: " + mode);
                    }
                    // 2. BATTERY CONSERVATION (1: 80% Cap, 0: 100% Cap)
                    else if (path == "/api/battery")
                    {
                        uint state = uint.Parse(request.QueryString["state"]);
                        SetWmi("SetPowerChargeMode", state); 
                        Console.WriteLine("[KERNEL] Battery Conservation switched to: " + state);
                    }
                    // 3. RAW TELEMETRY PING
                    else if (path == "/api/telemetry")
                    {
                        ulong cycles = 0;
                        try { cycles = GetCpuCycles(); } catch { } // Pulls from your ASM file
                        
                        // We format the string safely for C# 5.0
                        resJson = string.Format("{{\"cycles\": {0}}}", cycles);
                    }
                    else
                    {
                        resJson = "{\"error\":\"Endpoint not found\"}";
                        response.StatusCode = 404;
                    }
                }
                catch (Exception ex)
                {
                    resJson = string.Format("{{\"error\":\"{0}\"}}", ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\""));
                    Console.WriteLine("[ERROR] " + ex.Message);
                    response.StatusCode = 500;
                }

                // Fire the response back to Flutter
                response.ContentType = "application/json";
                response.AppendHeader("Access-Control-Allow-Origin", "*");
                byte[] buffer = Encoding.UTF8.GetBytes(resJson);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
        }

        // --- The Windows Kernel WMI Hook ---
        static void SetWmi(string method, uint value)
        {
            var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
            foreach (ManagementObject obj in searcher.Get()) {
                obj.InvokeMethod(method, new object[] { value });
                return;
            }
            throw new Exception("LENOVO_GAMEZONE_DATA WMI Class not found on this motherboard.");
        }
    }
}