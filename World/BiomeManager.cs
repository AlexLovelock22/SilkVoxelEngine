using System;
using System.Diagnostics;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.World;

public enum BiomeType { Desert, Plains, Forest, Tundra, Mountains, Ocean, River }

public static class BiomeManager
{
    public const float SEA_LEVEL = 62f;
    public const float RIVER_THRESHOLD = 0.035f;

    private const float TEMP_FREQUENCY = 0.00008f;    
    private const float HUMIDITY_FREQUENCY = 0.0005f; 

    // Define the noise object here to fix the CS1061 error
    private static readonly FastNoiseLite TerrainHeightNoise = new FastNoiseLite();

    public static void InitializeNoise(VoxelWorld world, int seed)
    {
        // CONTINENTAL
        world.ContinentalNoise.SetSeed(seed);
        world.ContinentalNoise.SetFrequency(0.00035f); 
        world.ContinentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ContinentalNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ContinentalNoise.SetFractalOctaves(4);
        world.ContinentalNoise.SetFractalWeightedStrength(0.4f);

        // COASTLINE RUFFLE
        world.ErosionNoise.SetSeed(seed + 10);
        world.ErosionNoise.SetFrequency(0.01f); 
        world.ErosionNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);

        // TEMPERATURE
        world.TempNoise.SetSeed(seed + 3);
        world.TempNoise.SetFrequency(TEMP_FREQUENCY);
        world.TempNoise.SetFractalType(FastNoiseLite.FractalType.None);

        // HUMIDITY
        world.HumidityNoise.SetSeed(seed + 2);
        world.HumidityNoise.SetFrequency(HUMIDITY_FREQUENCY);
        world.HumidityNoise.SetFractalType(FastNoiseLite.FractalType.FBm);

        // MOUNTAINS
        world.RiverNoise.SetSeed(seed + 4);
        world.RiverNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.RiverNoise.SetFrequency(0.00022f); 
        world.RiverNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        world.RiverNoise.SetFractalOctaves(3);

        // PLAINS SHAPING (Local object initialized here)
        TerrainHeightNoise.SetSeed(seed + 5);
        TerrainHeightNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        TerrainHeightNoise.SetFrequency(0.005f); 
        TerrainHeightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        TerrainHeightNoise.SetFractalOctaves(3);
    }

    public static float GetHeightAt(VoxelWorld world, float wx, float wz)
    {
        BiomeType type = GetBiomeAt(world, wx, wz);
        float baseHeight = SEA_LEVEL + 4f; 

        return type switch
        {
            BiomeType.Plains => baseHeight + CalculatePlainsHeight(wx, wz),
            BiomeType.Ocean  => SEA_LEVEL - 15f,
            _                => baseHeight
        };
    }

    private static float CalculatePlainsHeight(float wx, float wz)
    {
        // Sample the local static noise object
        float noise = (TerrainHeightNoise.GetNoise(wx, wz) + 1f) / 2f;
        float hillAmplitude = 8.0f; 
        
        return noise * hillAmplitude;
    }

    public static byte GetSurfaceBlock(BiomeType type) => type switch
    {
        BiomeType.Desert => 6, BiomeType.Tundra => 8, BiomeType.Mountains => 5,
        BiomeType.Forest => 7, BiomeType.River => 4, BiomeType.Ocean => 6, _ => 1
    };

    public static byte GetFillerBlock(BiomeType type) => 2;

    public static BiomeType GetBiomeAt(VoxelWorld world, float wx, float wz)
    {
        float oceanWarp = 22.0f; 
        float rx = wx + world.ErosionNoise.GetNoise(wx, wz) * oceanWarp;
        float rz = wz + world.ErosionNoise.GetNoise(wx + 500, wz + 500) * oceanWarp;

        float biomeWarp = 8.0f;
        float bx = wx + world.ErosionNoise.GetNoise(wx, wz) * biomeWarp;
        float bz = wz + world.ErosionNoise.GetNoise(wx + 500, wz + 500) * biomeWarp;

        float c = world.ContinentalNoise.GetNoise(rx, rz);
        float t = (world.TempNoise.GetNoise(bx, bz) + 1f) / 2f;
        float h = (world.HumidityNoise.GetNoise(bx + 1000, bz + 1000) + 1f) / 2f;
        float ridge = world.RiverNoise.GetNoise(bx, bz);
        float mountainMask = (world.TempNoise.GetNoise(wx * 0.1f, wz * 0.1f) + 1f) / 2f;

        return DetermineBiome(t, h, c, ridge, mountainMask);
    }

    public static BiomeType GetBiomeAt(float temp, float humid, float cont, float ridge, float currentHeight)
    {
        return DetermineBiome(temp, humid, cont, ridge, 0.5f);
    }

    private static BiomeType DetermineBiome(float t, float h, float c, float ridge, float mask)
    {
        if (c < -0.05f) return BiomeType.Ocean;

        float targetMask = (t < 0.4f) ? 0.35f : 0.45f;
        if (ridge > 0.58f && mask > targetMask) return BiomeType.Mountains;

        if (t < 0.42f) return (h < 0.55f) ? BiomeType.Tundra : BiomeType.Forest;

        if (t > 0.58f)
        {
            if (h < 0.35f) return BiomeType.Desert;
            if (h < 0.65f) return BiomeType.Plains;
            return BiomeType.Forest;
        }

        if (h < 0.50f) return BiomeType.Plains;
        return BiomeType.Forest;
    }
}