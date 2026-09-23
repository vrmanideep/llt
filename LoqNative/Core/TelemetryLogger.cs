using System;
using System.IO;
using System.Text.Json;

namespace LoqNative.Core
{
    public static class TelemetryLogger
    {
        public static void DumpReport(object stats)
        {
            try
            {
                // Saves directly to your build directory
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HardwareReport.json");
                
                var options = new JsonSerializerOptions { 
                    WriteIndented = true // Ensures the JSON is pretty-printed with indents and line breaks
                };
                
                string jsonString = JsonSerializer.Serialize(stats, options);
                File.WriteAllText(filePath, jsonString);
                
                // Automatically launches the JSON file in your default viewer
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to dump report: {ex.Message}");
            }
        }
    }
}