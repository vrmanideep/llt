using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace LoqNative.Core
{
    public static class HwinfoEngine
    {
        public static Dictionary<string, double> GetAllLoqSensors()
        {
            var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                // Open HWiNFO's memory-mapped file
                using var mmf = MemoryMappedFile.OpenExisting("Global\\HWiNFO_SENSORS_SM2", MemoryMappedFileRights.Read);
                using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                // Read SM2 Header Offsets
                uint readingSectionOffset = accessor.ReadUInt32(24);
                uint readingElementSize = accessor.ReadUInt32(28);
                uint numReadings = accessor.ReadUInt32(32);

                for (uint i = 0; i < numReadings; i++)
                {
                    long currentOffset = readingSectionOffset + (i * readingElementSize);
                    
                    // szLabelUser is at offset 136 (length 128)
                    byte[] labelBytes = new byte[128];
                    accessor.ReadArray(currentOffset + 136, labelBytes, 0, 128);
                    string sensorName = Encoding.UTF8.GetString(labelBytes).TrimEnd('\0');

                    // If it starts with 'Loq', grab the Value (double at offset 280)
                    if (sensorName.StartsWith("Loq", StringComparison.OrdinalIgnoreCase))
                    {
                        results[sensorName] = accessor.ReadDouble(currentOffset + 280);
                    }
                }
            }
            catch 
            { 
                // Fails silently and returns an empty dictionary if HWiNFO is closed
            }
            
            return results;
        }
    }
}