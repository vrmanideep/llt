using System;
using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Linq;
using System.Collections.Generic;

namespace LoqNative.Core
{
    public class AdvancedBatteryStats
    {
        public int CycleCount { get; set; }
        public uint DesignCapacityMw { get; set; }
        public uint FullChargeCapacityMw { get; set; }
        public double WearLevelPercent { get; set; }
        public TimeSpan? TimeOnBattery { get; set; }
    }

    public static class AdvancedBatteryEngine
    {
        public static AdvancedBatteryStats GetDeepBatteryHealth()
        {
            var stats = new AdvancedBatteryStats();

            try
            {
                // 1. Get deep hardware health (Cycles and Capacities)
                // Windows WMI usually stores this in the root\WMI namespace under BatteryStaticData and BatteryFullChargedCapacity
                using var staticSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryStaticData");
                foreach (var obj in staticSearcher.Get())
                {
                    using (obj)
                    {
                        stats.DesignCapacityMw = Convert.ToUInt32(obj["DesignedCapacity"]);
                    }
                }

                using var fullSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryFullChargedCapacity");
                foreach (var obj in fullSearcher.Get())
                {
                    using (obj)
                    {
                        stats.FullChargeCapacityMw = Convert.ToUInt32(obj["FullChargedCapacity"]);
                    }
                }

                using var cycleSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryCycleCount");
                foreach (var obj in cycleSearcher.Get())
                {
                    using (obj)
                    {
                        stats.CycleCount = Convert.ToInt32(obj["CycleCount"]);
                    }
                }

                // Calculate battery degradation (Wear Level)
                if (stats.DesignCapacityMw > 0 && stats.FullChargeCapacityMw > 0)
                {
                    double wear = 100.0 - ((double)stats.FullChargeCapacityMw / stats.DesignCapacityMw * 100.0);
                    stats.WearLevelPercent = Math.Max(0, wear); // Ensure it doesn't show negative if overcharged
                }
            }
            catch { /* Hardware layer busy or unsupported */ }

            return stats;
        }

        // 2. The Intelligent "Stopwatch" - Scrapes Windows Logs for the exact second you unplugged
        public static TimeSpan? GetTimeOnBattery()
        {
            try
            {
                var logs = new List<(DateTime Date, bool IsACOnline)>();

                // Query the Windows Event Log for Event ID 105 (Power Source Change)
                var query = new EventLogQuery("System", PathType.LogName, "*[System[EventID=105]]");
                using var logReader = new EventLogReader(query);
                using var propertySelector = new EventLogPropertySelector(new[] { "Event/EventData/Data[@Name='AcOnline']" });

                // Read backwards through recent events
                while (logReader.ReadEvent() is EventLogRecord record)
                {
                    var date = record.TimeCreated;
                    var isAcOnline = record.GetPropertyValues(propertySelector)[0] as bool?;

                    if (date != null && isAcOnline != null)
                    {
                        logs.Add((date.Value, isAcOnline.Value));
                    }
                }

                if (logs.Count == 0) return null;

                // Reverse to read chronologically
                logs.Reverse();

                // Find the *last* time the AC was unplugged (IsACOnline == false)
                var lastUnplugEvent = logs.TakeWhile(log => log.IsACOnline == false).LastOrDefault();

                if (lastUnplugEvent.Date != default(DateTime))
                {
                    return DateTime.Now - lastUnplugEvent.Date;
                }
            }
            catch { /* Access denied or no events found */ }

            return null;
        }
    }
}