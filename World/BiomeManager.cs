using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.World;

public enum BiomeType { Ocean, Desert, Plains, Mountains, Forest, Tundra }

public static class BiomeManager
{
    public const float SEA_LEVEL = 62f;

    public static void InitializeNoise(VoxelWorld world, int seed)
    {
        // Continentalness: High octaves for jagged coastlines
        world.ContinentalNoise.SetSeed(seed);
        world.ContinentalNoise.SetFrequency(0.0001f);
        world.ContinentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ContinentalNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ContinentalNoise.SetFractalOctaves(9);

        // Erosion: High frequency for rugged detail
        world.ErosionNoise.SetSeed(seed + 101);
        world.ErosionNoise.SetFrequency(0.0008f);
        world.ErosionNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ErosionNoise.SetFractalOctaves(5);

        // The Peaks: Ridged noise for jagged mountains
        world.RiverNoise.SetSeed(seed + 102);
        world.RiverNoise.SetFrequency(0.0007f);
        world.RiverNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.RiverNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        world.RiverNoise.SetFractalOctaves(6);

        world.TempNoise.SetSeed(seed + 202);
        world.TempNoise.SetFrequency(0.0001f);
        world.HumidityNoise.SetSeed(seed + 303);
        world.HumidityNoise.SetFrequency(0.0001f);
    }

    private static float ApplyContinentalContrast(float noiseValue) => MathF.Tanh(noiseValue * 3.0f);

    public static float GetHeightAt(VoxelWorld world, float wx, float wz)
    {
        float rawContinental = world.ContinentalNoise.GetNoise(wx, wz);
        float continentalness = ApplyContinentalContrast(rawContinental);

        if (continentalness < 0)
        {
            // Ocean floor (Actual bed)
            return SEA_LEVEL + (continentalness * 35f);
        }
        else
        {
            // --- RUGGEDNESS: DOMAIN WARPING ---
            // Jitter the coordinates to break up the "conical" mountain look
            float warpScale = 25.0f;
            float warpX = world.ErosionNoise.GetNoise(wx * 1.2f, wz * 1.2f) * warpScale;
            float warpZ = world.ErosionNoise.GetNoise(wz * 1.2f, wx * 1.2f) * warpScale;

            float erosionValue = world.ErosionNoise.GetNoise(wx, wz);

            // Beach Curve: Forces land to stay flat at the shore (0 to 1)
            float beachCurve = MathF.Pow(Math.Clamp(continentalness * 7.0f, 0, 1), 2.5f);

            float mountainMask = beachCurve * Math.Clamp(erosionValue + 0.4f, 0, 1);
            float basePlains = 6f;

            // Warped peaks
            float rawPeak = (world.RiverNoise.GetNoise(wx + warpX, wz + warpZ) + 1.0f) / 2.0f;
            float peakNoise = MathF.Pow(rawPeak, 3.0f) * 120f;

            // Small crags for texture
            float detail = world.ErosionNoise.GetNoise(wx * 4f, wz * 4f) * 4f;

            float heightOffset = (basePlains + (peakNoise * mountainMask) + detail) * beachCurve;

            return SEA_LEVEL + heightOffset;
        }
    }

    // IMPORTANT: Use this in your Chunk Loop to fix the water level
    public static byte GetBlockAt(VoxelWorld world, int y, float surfaceHeight, BiomeType biome)
    {
        // 1. Air/Water Volume (Everything above the dirt/stone floor)
        if (y > surfaceHeight)
        {
            // If we are below Sea Level but above the floor, it is solid Water.
            if (y <= SEA_LEVEL) return 3; // Updated to Water ID 3

            return 0; // Air
        }

        // 2. Surface Layer
        if (y >= surfaceHeight - 1)
        {
            // Beach override: If near water, use sand (ID 6)
            if (surfaceHeight <= SEA_LEVEL + 2.5f && biome != BiomeType.Desert)
                return 6;

            // Note: You may need to pass worldX/worldZ if your surface blocks use noise
            return GetSurfaceBlock(biome, 0, 0);
        }

        // 3. Subsurface
        return GetFillerBlock(biome);
    }

    public static BiomeType GetBiomeAt(VoxelWorld world, float wx, float wz)
    {
        float height = GetHeightAt(world, wx, wz);
        float relativeHeight = height - SEA_LEVEL;

        if (height <= SEA_LEVEL) return BiomeType.Ocean;

        // "Fuzzy" mountain logic to avoid stone shelves
        float erosionValue = world.ErosionNoise.GetNoise(wx, wz);
        float stoneScore = (relativeHeight / 55f) + (erosionValue * 0.5f);

        if (stoneScore > 0.72f) return BiomeType.Mountains;

        float temp = (world.TempNoise.GetNoise(wx, wz) + 1f) / 2f;
        float humidity = (world.HumidityNoise.GetNoise(wx, wz) + 1f) / 2f;

        if (temp > 0.72f && humidity < 0.35f) return BiomeType.Desert;
        if (temp < 0.32f) return BiomeType.Tundra;
        if (humidity > 0.68f) return BiomeType.Forest;

        return BiomeType.Plains;
    }

    public static byte GetSurfaceBlock(BiomeType type, float wx, float wz)
    {
        return type switch
        {
            BiomeType.Ocean => 6,      // Sand
            BiomeType.Desert => 6,     // Sand
            BiomeType.Mountains => 5,  // Stone
            BiomeType.Tundra => 8,     // Snow
            BiomeType.Forest => 9,     // Moss
            _ => 1                     // Grass
        };
    }

    public static byte GetFillerBlock(BiomeType type)
    {
        return type switch
        {
            BiomeType.Desert => 6,
            BiomeType.Mountains => 5,
            _ => 2 // Dirt
        };
    }

    public static bool IsLocalWater(BiomeType type, float wx, float wz) => type == BiomeType.Ocean;
}