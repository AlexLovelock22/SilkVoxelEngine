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
        // Continentalness: Smoother frequency for larger landmasses
        world.ContinentalNoise.SetSeed(seed);
        world.ContinentalNoise.SetFrequency(0.00008f); 
        world.ContinentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ContinentalNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ContinentalNoise.SetFractalOctaves(7);

        // Erosion: Used for the "Ruffle" and shelf structure
        world.ErosionNoise.SetSeed(seed + 101);
        world.ErosionNoise.SetFrequency(0.0006f); 
        world.ErosionNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ErosionNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ErosionNoise.SetFractalOctaves(5);

        // The Peaks: Ridged noise for the "Proud" structure
        world.RiverNoise.SetSeed(seed + 102);
        world.RiverNoise.SetFrequency(0.0008f); 
        world.RiverNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.RiverNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        world.RiverNoise.SetFractalOctaves(6); 

        world.TempNoise.SetSeed(seed + 202);
        world.TempNoise.SetFrequency(0.0001f);
        world.HumidityNoise.SetSeed(seed + 303);
        world.HumidityNoise.SetFrequency(0.0001f);
    }

    private static float ApplyContinentalContrast(float noiseValue) => MathF.Tanh(noiseValue * 3.5f);

    public static float GetHeightAt(VoxelWorld world, float wx, float wz)
    {
        float rawContinental = world.ContinentalNoise.GetNoise(wx, wz);
        float continentalness = ApplyContinentalContrast(rawContinental);

        if (continentalness < 0)
        {
            return SEA_LEVEL + (continentalness * 40f);
        }
        else
        {
            // --- DOMAIN WARPING (The "Ruffle") ---
            // Shifting coordinates slightly to create jagged, non-conical slopes
            float warpScale = 45.0f; // Higher scale = more twisted rock formations
            float warpX = world.ErosionNoise.GetNoise(wx * 1.5f, wz * 1.5f) * warpScale;
            float warpZ = world.ErosionNoise.GetNoise(wz * 1.5f, wx * 1.5f) * warpScale;

            float erosionValue = world.ErosionNoise.GetNoise(wx, wz);
            
            // --- PROUD STRUCTURE ---
            // We use the ridged noise with the warped coordinates
            float rawPeak = (world.RiverNoise.GetNoise(wx + warpX, wz + warpZ) + 1.0f) / 2.0f;
            
            // Squashes the valleys but makes the tops extremely tall/steep
            float proudPeaks = MathF.Pow(rawPeak, 3.5f) * 145f; 

            // --- THE SHELF EFFECT (Ruffle) ---
            // Adding a "staircase" effect to the slopes using erosion
            float shelfDetail = MathF.Abs(world.ErosionNoise.GetNoise(wx * 3f, wz * 3f)) * 8f;

            // Beach Curve
            float beachCurve = MathF.Pow(Math.Clamp(continentalness * 8.0f, 0, 1), 2.0f);
            
            // Masking mountains so they only appear inland
            float mountainMask = beachCurve * Math.Clamp(erosionValue + 0.5f, 0, 1);
            
            float basePlains = 5f;
            float totalHeight = (basePlains + (proudPeaks * mountainMask) + (shelfDetail * mountainMask)) * beachCurve;

            return SEA_LEVEL + totalHeight;
        }
    }

    public static byte GetBlockAt(VoxelWorld world, int y, float surfaceHeight, BiomeType biome)
    {
        if (y > surfaceHeight)
        {
            if (y <= SEA_LEVEL) return 3; 
            return 0; 
        }

        if (y >= surfaceHeight - 1)
        {
            // Beach Check
            if (surfaceHeight <= SEA_LEVEL + 1.8f && biome != BiomeType.Desert)
                return 6;

            return GetSurfaceBlock(biome, 0, 0);
        }

        return GetFillerBlock(biome);
    }

    public static BiomeType GetBiomeAt(VoxelWorld world, float wx, float wz)
    {
        float height = GetHeightAt(world, wx, wz);
        float relativeHeight = height - SEA_LEVEL;

        if (height <= SEA_LEVEL) return BiomeType.Ocean;

        float erosionValue = world.ErosionNoise.GetNoise(wx, wz);
        
        // Stone starts appearing based on height and ruggedness
        // Lowering the divisor to 45 makes mountains appear more easily
        float stoneScore = (relativeHeight / 45f) + (erosionValue * 0.55f);

        if (stoneScore > 0.65f) return BiomeType.Mountains;

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
            BiomeType.Ocean => 6,
            BiomeType.Desert => 6,
            BiomeType.Mountains => 5, // Stone
            BiomeType.Tundra => 8,
            BiomeType.Forest => 9,
            _ => 1
        };
    }

    public static byte GetFillerBlock(BiomeType type)
    {
        return type switch
        {
            BiomeType.Desert => 6,
            BiomeType.Mountains => 5,
            _ => 2
        };
    }

    public static bool IsLocalWater(BiomeType type, float wx, float wz) => type == BiomeType.Ocean;
}