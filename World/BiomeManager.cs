using System;
using System.Diagnostics;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.World;

public enum BiomeType { Desert, Plains, Forest, Tundra, Mountains, Ocean, River }

public static class BiomeManager
{
    public const float SEA_LEVEL = 62f;
    public const float RIVER_THRESHOLD = 0.035f;

    // FIRM SHAPES: Lower frequency makes the "blobs" larger and more solid.
    private const float CLIMATE_FREQUENCY = 0.0006f; 

    public static void InitializeNoise(VoxelWorld world, int seed)
    {
        // VAST CONTINENTS: Extremely low frequency (0.0003f) for huge oceans
        world.ContinentalNoise.SetSeed(seed);
        world.ContinentalNoise.SetFrequency(0.0003f); 
        world.ContinentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ContinentalNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ContinentalNoise.SetFractalOctaves(3);

        // BOUNDARY RUFFLE: Higher frequency but used only for edge jitter
        world.ErosionNoise.SetSeed(seed + 10);
        world.ErosionNoise.SetFrequency(0.015f); 
        world.ErosionNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);

        world.HumidityNoise.SetSeed(seed + 2);
        world.HumidityNoise.SetFrequency(CLIMATE_FREQUENCY);
        world.HumidityNoise.SetFractalType(FastNoiseLite.FractalType.FBm);

        world.TempNoise.SetSeed(seed + 3);
        world.TempNoise.SetFrequency(CLIMATE_FREQUENCY);
        world.TempNoise.SetFractalType(FastNoiseLite.FractalType.FBm);

        // MOUNTAIN SPLINES: Lower frequency (0.0005f) for longer, massive ranges
        world.RiverNoise.SetSeed(seed + 4);
        world.RiverNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.RiverNoise.SetFrequency(0.0005f); 
        world.RiverNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        world.RiverNoise.SetFractalOctaves(3);
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
        // THE RUFFLE TECHNIQUE: 
        // We warp the coordinates only slightly (warpStrength 15) to keep the "firm" shape,
        // but use a secondary high-frequency noise for the "jitter".
        float warpStrength = 15.0f; 
        float rx = wx + world.ErosionNoise.GetNoise(wx, wz) * warpStrength;
        float rz = wz + world.ErosionNoise.GetNoise(wx + 500, wz + 500) * warpStrength;

        // Climate (using ruffled coordinates)
        float t = (world.TempNoise.GetNoise(rx, rz) + 1f) / 2f;
        float h = (world.HumidityNoise.GetNoise(rx + 1000, rz + 1000) + 1f) / 2f;
        
        // Landmass (using smooth coordinates for stable, large continents)
        float c = world.ContinentalNoise.GetNoise(wx, wz);
        
        // Mountain Ridge
        float ridge = world.RiverNoise.GetNoise(rx, rz);
        
        // A wider mask for mountains so ranges are thicker and more continuous
        float mountainMask = (world.TempNoise.GetNoise(wx * 0.1f, wz * 0.1f) + 1f) / 2f;

        return DetermineBiome(t, h, c, ridge, mountainMask);
    }

    public static BiomeType GetBiomeAt(float temp, float humid, float cont, float ridge, float currentHeight)
    {
        return DetermineBiome(temp, humid, cont, ridge, 0.5f);
    }

    private static BiomeType DetermineBiome(float t, float h, float c, float ridge, float mask)
    {
        // 1. BIG OCEANS: c < 0.0f gives roughly 50% ocean. 
        // Lowering to -0.1f makes land even more sparse/continental.
        if (c < -0.05f) return BiomeType.Ocean;

        // 2. MASSIVE MOUNTAIN RANGES
        // ridge > 0.7f creates thicker splines. 
        // mask > 0.4f ensures they only appear in large "upland" territories.
        if (ridge > 0.7f && mask > 0.45f) 
        {
            return BiomeType.Mountains;
        }

        // 3. FIRM CLIMATE SHAPES
        if (t < 0.38f) 
            return (h > 0.55f) ? BiomeType.Forest : BiomeType.Tundra;

        if (t < 0.65f)
        {
            if (h < 0.40f) return BiomeType.Plains;
            return BiomeType.Forest;
        }

        if (h < 0.38f) return BiomeType.Desert;
        return (h < 0.65f) ? BiomeType.Plains : BiomeType.Forest;
    }
}