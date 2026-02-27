using System;
using System.IO;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.World.Debug;

public static class DebugExporter
{
    /// <summary>
    /// Exports a text-based representation of noise maps to the console or a file.
    /// This helps you see if the "Main Land" is flat because of the noise itself.
    /// </summary>
    public static void ExportNoiseArea(VoxelWorld world, int startX, int startZ, int size)
    {
        Console.WriteLine($"--- NOISE DEBUG REPORT ({startX}, {startZ}) size: {size} ---");
        
        for (int z = 0; z < size; z += size / 20) // 20x20 grid sample
        {
            string line = "";
            for (int x = 0; x < size; x += size / 20)
            {
                float wx = startX + x;
                float wz = startZ + z;

                // Sample the critical values
                float temp = (world.TempNoise.GetNoise(wx, wz) + 1f) / 2f;
                float humidity = (world.HumidityNoise.GetNoise(wx, wz) + 1f) / 2f;
                float desertInfluence = Math.Clamp((temp - 0.65f) * 2f, 0, 1) * Math.Clamp((0.45f - humidity) * 2f, 0, 1);
                
                // Represent desert influence as a character
                if (desertInfluence > 0.8f) line += "D "; // Deep Desert
                else if (desertInfluence > 0.1f) line += "s "; // Scrubland/Transition
                else line += ". "; // Other Biome
            }
            Console.WriteLine(line);
        }
    }
}