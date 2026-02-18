using System;
using System.Diagnostics;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.World;

public enum BiomeType { Desert, Plains, Forest, Tundra, Mountains, Ocean, River }

public static class BiomeManager
{
    public const float SEA_LEVEL = 62f;
    public const float RIVER_THRESHOLD = 0.035f;
    private const float CLIMATE_FREQUENCY = 0.001f; 

    public static void InitializeNoise(VoxelWorld world, int seed)
    {
        world.ContinentalNoise.SetSeed(seed);
        world.ContinentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ContinentalNoise.SetFrequency(0.0007f); 
        world.ContinentalNoise.SetFractalType(FastNoiseLite.FractalType.FBm);

        world.ErosionNoise.SetSeed(seed + 10);
        world.ErosionNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ErosionNoise.SetFrequency(0.009f); 
        world.ErosionNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ErosionNoise.SetFractalOctaves(3); 

        world.HumidityNoise.SetSeed(seed + 2);
        world.HumidityNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.HumidityNoise.SetFrequency(CLIMATE_FREQUENCY);
        world.HumidityNoise.SetFractalType(FastNoiseLite.FractalType.FBm);

        world.TempNoise.SetSeed(seed + 3);
        world.TempNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.TempNoise.SetFrequency(CLIMATE_FREQUENCY);
        world.TempNoise.SetFractalType(FastNoiseLite.FractalType.FBm);

        world.RiverNoise.SetSeed(seed + 4);
        world.RiverNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.RiverNoise.SetFrequency(0.0015f);
    }

    public static float GetHeightAt(VoxelWorld world, float wx, float wz) => 64f;

    public static byte GetSurfaceBlock(BiomeType type) => type switch
    {
        BiomeType.Desert => 6, BiomeType.Tundra => 8, BiomeType.Mountains => 5,
        BiomeType.Forest => 7, BiomeType.River => 4, BiomeType.Ocean => 6, _ => 1
    };

    public static byte GetFillerBlock(BiomeType type) => 2;

    public static BiomeType GetBiomeAt(VoxelWorld world, float wx, float wz)
    {
        float warpStrength = 40.0f; 
        float rx = wx + world.ErosionNoise.GetNoise(wx, wz) * warpStrength;
        float rz = wz + world.ErosionNoise.GetNoise(wx + 500, wz + 500) * warpStrength;

        float t = (world.TempNoise.GetNoise(rx, rz) + 1f) / 2f;
        float h = (world.HumidityNoise.GetNoise(rx + 1000, rz + 1000) + 1f) / 2f;
        
        float c = world.ContinentalNoise.GetNoise(wx, wz);
        float r = world.RiverNoise.GetNoise(wx, wz);

        return DetermineBiome(t, h, c, r);
    }

    public static BiomeType GetBiomeAt(float temp, float humid, float cont, float riverNoise, float currentHeight)
    {
        return DetermineBiome(temp, humid, cont, riverNoise);
    }

    private static BiomeType DetermineBiome(float t, float h, float c, float r)
    {
        if (c < -0.1f) return BiomeType.Ocean;
        if (Math.Abs(r) < RIVER_THRESHOLD) return BiomeType.River;

        // --- NEW LOGIC TO DECOUPLE DESERT & MOUNTAINS ---

        // 1. COLD (Tundra & Cold Forest)
        if (t < 0.35f) 
        {
            return (h > 0.50f) ? BiomeType.Forest : BiomeType.Tundra;
        }

        // 2. TEMPERATE (Mountains & Plains & Forest)
        if (t < 0.60f)
        {
            // Mountains are now very dry ONLY. 
            // If it's slightly dry, it becomes Plains first.
            if (h < 0.25f) return BiomeType.Mountains; 
            if (h < 0.60f) return BiomeType.Plains;
            return BiomeType.Forest;
        }

        // 3. HOT (Desert & Plains & Tropical Forest)
        // We raised the desert humidity floor so it doesn't bleed into the Mountain's dry zone
        if (h < 0.40f) 
        {
            // If it's hot AND very dry, check if it's "Hot enough" for desert
            // Otherwise, stay Plains to act as a buffer.
            return (t > 0.75f) ? BiomeType.Desert : BiomeType.Plains;
        }

        if (h < 0.70f) return BiomeType.Plains;
        return BiomeType.Forest;
    }
}