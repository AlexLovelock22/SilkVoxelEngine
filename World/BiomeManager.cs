using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.World;

public enum BiomeType { Ocean, Land }

public static class BiomeManager
{
    public const float SEA_LEVEL = 62f;

    public static void InitializeNoise(VoxelWorld world, int seed)
    {
        // 1. Continentalness: Defines the land/sea footprint.
        world.ContinentalNoise.SetSeed(seed);
        world.ContinentalNoise.SetFrequency(0.00015f); 
        world.ContinentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ContinentalNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ContinentalNoise.SetFractalOctaves(10);

        // 2. Erosion Selector: The "Director" for mountains.
        world.ErosionNoise.SetSeed(seed + 101);
        world.ErosionNoise.SetFrequency(0.0003f); 
        world.ErosionNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);

        // 3. The Peaks: Ridged noise for "Personality B".
        world.RiverNoise.SetSeed(seed + 102);
        world.RiverNoise.SetFrequency(0.0007f);
        world.RiverNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.RiverNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        world.RiverNoise.SetFractalOctaves(6); 

        // 4. Micro-Terrain (New): This gives the "Smooth Land" its character.
        // We use TempNoise as a placeholder for a 4th noise slot if you have it.
        world.TempNoise.SetSeed(seed + 202);
        world.TempNoise.SetFrequency(0.002f); // Higher frequency = smaller, more frequent hills
        world.TempNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.TempNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.TempNoise.SetFractalOctaves(4); // Adds that "weathered" look
    }

    private static float ApplyContinentalContrast(float noiseValue)
    {
        return MathF.Tanh(noiseValue * 3.0f); 
    }

    public static float GetHeightAt(VoxelWorld world, float wx, float wz)
    {
        float rawContinental = world.ContinentalNoise.GetNoise(wx, wz);
        float continentalness = ApplyContinentalContrast(rawContinental);

        if (continentalness < 0)
        {
            return SEA_LEVEL + (continentalness * 30f);
        }
        else
        {
            float baseLandHeight = SEA_LEVEL + 4f;

            // --- THE DISCONTINUITY BLEND ---

            float erosionValue = world.ErosionNoise.GetNoise(wx, wz);
            float cliffAlpha = Math.Clamp(MathF.Tanh(erosionValue * 4.0f) * 0.5f + 0.5f, 0, 1);

            // PERSONALITY A: The Detailed Plains
            // We use the new TempNoise here to create actual rolling character.
            // We multiply by 8f to 12f to give it enough height to be noticeable.
            float plainsDetail = world.TempNoise.GetNoise(wx, wz);
            float plainsHeight = (plainsDetail + 1.0f) * 10f; 

            // PERSONALITY B: The Jagged Peaks
            float peaksHeight = (world.RiverNoise.GetNoise(wx, wz) + 1.0f) * 90f; 

            // Blend them together
            float combinedHeight = Lerp(plainsHeight, peaksHeight, cliffAlpha);

            return baseLandHeight + combinedHeight;
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static BiomeType GetBiomeAt(VoxelWorld world, float wx, float wz)
    {
        float rawNoise = world.ContinentalNoise.GetNoise(wx, wz);
        float continentalness = ApplyContinentalContrast(rawNoise);
        return continentalness < 0 ? BiomeType.Ocean : BiomeType.Land;
    }

    public static void ExportBiomeMap(VoxelWorld world, int size, string fileName)
    {
        using Image<Rgba32> image = new Image<Rgba32>(size, size);
        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                BiomeType type = GetBiomeAt(world, x, z);
                image[x, z] = type == BiomeType.Ocean ? new Rgba32(0, 0, 128) : new Rgba32(34, 139, 34);
            }
        }
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        image.SaveAsPng(path);
        Console.WriteLine($"Map exported to: {path}");
    }

    public static byte GetSurfaceBlock(BiomeType type, float wx, float wz) => type == BiomeType.Ocean ? (byte)255 : (byte)1;
    public static byte GetFillerBlock(BiomeType type) => 2;
    public static bool IsLocalWater(BiomeType type, float wx, float wz) => type == BiomeType.Ocean;
}