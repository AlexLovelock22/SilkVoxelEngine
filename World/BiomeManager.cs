using System;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.World;

public enum BiomeType { Desert, Plains, Forest, Tundra, Mountains, Ocean, River }

public static class BiomeManager
{
    public const float SEA_LEVEL = 62f;
    private const float BLEND_THRESHOLD = 0.05f; 

    public static void InitializeNoise(VoxelWorld world, int seed)
    {
        // 1. Global Decision Noise
        world.ContinentalNoise.SetSeed(seed);
        world.ContinentalNoise.SetFrequency(0.00035f);
        world.ContinentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ContinentalNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ContinentalNoise.SetFractalOctaves(4);
        world.ContinentalNoise.SetFractalWeightedStrength(0.4f);

        world.ErosionNoise.SetSeed(seed + 10);
        world.ErosionNoise.SetFrequency(0.01f);
        world.ErosionNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);

        world.TempNoise.SetSeed(seed + 3);
        world.TempNoise.SetFrequency(0.00008f);
        world.TempNoise.SetFractalType(FastNoiseLite.FractalType.None);

        world.HumidityNoise.SetSeed(seed + 2);
        world.HumidityNoise.SetFrequency(0.0005f);
        world.HumidityNoise.SetFractalType(FastNoiseLite.FractalType.FBm);

        world.RiverNoise.SetSeed(seed + 4);
        world.RiverNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.RiverNoise.SetFrequency(0.00022f); 
        world.RiverNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        world.RiverNoise.SetFractalOctaves(3);

        // 2. Biome Shaping
        PlainsBiome.Initialize(seed);
        ForestBiome.Initialize(seed);
        MountainsBiome.Initialize(seed);
    }

    public static float GetHeightAt(VoxelWorld world, float wx, float wz)
    {
        var (t, h, c) = GetBiomeNoiseValues(world, wx, wz);
        float baseHeight = SEA_LEVEL + 4f;

        // 1. Ocean Floor
        if (c < -0.05f) return SEA_LEVEL - 15f;

        // 2. Lowland Floor (Calculate the smooth floor first)
        float boundary = 0.50f;
        float hWeight = Math.Clamp((h - (boundary - BLEND_THRESHOLD)) / (BLEND_THRESHOLD * 2f), 0f, 1f);
        float plainsH = PlainsBiome.GetHeight(wx, wz);
        float forestH = ForestBiome.GetHeight(wx, wz);
        float lowlandH = (plainsH * (1f - hWeight)) + (forestH * hWeight);

        // 3. Mountain Override Logic
        float ridgeRaw = world.RiverNoise.GetNoise(wx, wz);
        float mask = (world.TempNoise.GetNoise(wx * 0.1f, wz * 0.1f) + 1f) / 2f;
        float targetMask = (t < 0.4f) ? 0.35f : 0.45f;

        // We use 0.45 as the start of the "Mountain Base" (The Skirt)
        float mWeight = Math.Clamp((ridgeRaw - 0.45f) / 0.15f, 0f, 1f);
        if (mask < targetMask) mWeight = 0;

        // If we have mountain influence, override the lowland terrain
        if (mWeight > 0)
        {
            float mountainH = MountainsBiome.GetHeight(wx, wz, ridgeRaw, mWeight);
            
            // LERP: Smoothly transition from lowland floor to mountain peak
            // This ensures the mountain "lifts" the ground rather than being stuck on top
            float finalOffset = (lowlandH * (1f - mWeight)) + (mountainH * mWeight);
            return baseHeight + finalOffset;
        }

        return baseHeight + lowlandH;
    }

    private static (float t, float h, float c) GetBiomeNoiseValues(VoxelWorld world, float wx, float wz)
    {
        float oceanWarp = 22.0f;
        float rx = wx + world.ErosionNoise.GetNoise(wx, wz) * oceanWarp;
        float rz = wz + world.ErosionNoise.GetNoise(wx + 500, wz + 500) * oceanWarp;

        float biomeWarp = 8.0f;
        float nbx = wx + world.ErosionNoise.GetNoise(wx, wz) * biomeWarp;
        float nbz = wz + world.ErosionNoise.GetNoise(wx + 500, wz + 500) * biomeWarp;

        float c = world.ContinentalNoise.GetNoise(rx, rz);
        float t = (world.TempNoise.GetNoise(nbx, nbz) + 1f) / 2f;
        float h = (world.HumidityNoise.GetNoise(nbx + 1000, nbz + 1000) + 1f) / 2f;
        
        return (t, h, c);
    }

    public static BiomeType GetBiomeAt(VoxelWorld world, float wx, float wz)
    {
        var (t, h, c) = GetBiomeNoiseValues(world, wx, wz);
        
        float nbx = wx + world.ErosionNoise.GetNoise(wx, wz) * 8.0f;
        float nbz = wz + world.ErosionNoise.GetNoise(wx + 500, wz + 500) * 8.0f;
        float ridge = world.RiverNoise.GetNoise(nbx, nbz);
        float mountainMask = (world.TempNoise.GetNoise(wx * 0.1f, wz * 0.1f) + 1f) / 2f;

        return DetermineBiome(t, h, c, ridge, mountainMask);
    }

    public static byte GetSurfaceBlock(BiomeType type, float wx, float wz)
    {
        return type switch
        {
            BiomeType.Mountains => 5,
            BiomeType.Forest => ForestBiome.GetForestSurfaceBlock(wx, wz),
            BiomeType.Ocean => 6,
            _ => 1
        };
    }

    public static byte GetFillerBlock(BiomeType type) => 2;

    public static bool IsLocalWater(BiomeType type, float wx, float wz)
    {
        if (type == BiomeType.Ocean || type == BiomeType.River) return true;
        if (type == BiomeType.Forest) return ForestBiome.IsGully(wx, wz);
        return false;
    }

    private static BiomeType DetermineBiome(float t, float h, float c, float ridge, float mask)
    {
        if (c < -0.05f) return BiomeType.Ocean;

        float targetMask = (t < 0.4f) ? 0.35f : 0.45f;
        
        // Mountain threshold at 0.58, but height blending starts at 0.45
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