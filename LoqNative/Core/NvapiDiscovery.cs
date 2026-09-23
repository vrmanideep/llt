using System;
using System.Linq;
using System.Reflection;
using NvAPIWrapper;
using NvAPIWrapper.GPU;

namespace LoqNative.Core
{
    public static class NvapiDiscovery
    {
        public static void RunDiscovery()
        {
            try
            {
                NVIDIA.Initialize();
                Console.WriteLine("\n--- NVAPI Initialized Successfully ---\n");

                var gpus = PhysicalGPU.GetPhysicalGPUs();
                if (gpus.Length == 0)
                {
                    Console.WriteLine("No NVIDIA GPU detected.");
                    return;
                }

                var gpu = gpus.First();
                Console.WriteLine($"Found GPU Target: {gpu.FullName}\n");

                Console.WriteLine("=== DUMPING ALL AVAILABLE PROPERTIES & SENSORS ===");
                DumpProperties(gpu, "GPU");

                NVIDIA.Unload();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NVAPI Error: {ex.Message}");
            }
        }

        private static void DumpProperties(object obj, string prefix, int depth = 0)
        {
            // Limit depth to prevent infinite loops on circular references
            if (depth > 2 || obj == null) return; 

            var type = obj.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(obj);
                    
                    if (value == null)
                    {
                        Console.WriteLine($"{prefix}.{prop.Name} = NULL");
                        continue;
                    }

                    // If it is a basic type, print it directly
                    if (type.IsPrimitive || value is string || value is Enum || value.GetType().IsValueType)
                    {
                        Console.WriteLine($"{prefix}.{prop.Name} = {value}");
                    }
                    // If it is an array or collection, count the items
                    else if (value is System.Collections.IEnumerable enumerable)
                    {
                        int count = 0;
                        foreach (var item in enumerable) count++;
                        Console.WriteLine($"{prefix}.{prop.Name} = [Collection: {count} items]");
                    }
                    // If it is a complex nested object (like ThermalInformation), dig one level deeper
                    else
                    {
                        Console.WriteLine($"\n>>> Found Sub-Module: {prefix}.{prop.Name} ({value.GetType().Name})");
                        DumpProperties(value, $"{prefix}.{prop.Name}", depth + 1);
                    }
                }
                catch (Exception)
                {
                    // Skip properties that throw exceptions (e.g., locked by BIOS)
                    Console.WriteLine($"{prefix}.{prop.Name} = [LOCKED/UNAVAILABLE]");
                }
            }
        }
    }
}