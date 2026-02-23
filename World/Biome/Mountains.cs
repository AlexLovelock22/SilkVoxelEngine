using System;

namespace VoxelEngine_Silk.Net_1._0.World;

public static class MountainsBiome
{
    private static readonly FastNoiseLite MassifNoise = new FastNoiseLite();
    private static readonly FastNoiseLite CragNoise = new FastNoiseLite();

    public static void Initialize(int seed)
    {
        // One massive, slow shape for the mountain body
        MassifNoise.SetSeed(seed + 201);
        MassifNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        MassifNoise.SetFrequency(0.001f); 
        MassifNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        MassifNoise.SetFractalOctaves(3);

        // Sharp details only for the very peak
        CragNoise.SetSeed(seed + 202);
        CragNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        CragNoise.SetFrequency(0.01f);
        CragNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        CragNoise.SetFractalOctaves(2);
    }

    public static float GetHeight(float wx, float wz, float ridgeValue)
    {
        // 1. The Body: A broad, smooth uplift
        float massif = (MassifNoise.GetNoise(wx, wz) + 1f) / 2f;
        
        // 2. The Spine: Pull height toward the center of the biome spline
        // This ensures the mountain is highest at its "heart"
        float spine = MathF.Pow(ridgeValue, 3.0f);

        // 3. The Details: Only visible at the top
        float crags = CragNoise.GetNoise(wx, wz) * 10f * spine;

        // Base 15 + Up to 80 blocks of height
        return (massif * 30f) + (spine * 50f) + crags;
    }
}