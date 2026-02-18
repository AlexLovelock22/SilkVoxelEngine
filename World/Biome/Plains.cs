using System;

namespace VoxelEngine_Silk.Net_1._0.World;

public static class PlainsBiome
{
    private static readonly FastNoiseLite TerrainHeightNoise = new FastNoiseLite();

    public static void Initialize(int seed)
    {
        TerrainHeightNoise.SetSeed(seed + 5);
        TerrainHeightNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        TerrainHeightNoise.SetFrequency(0.005f); 
        TerrainHeightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        TerrainHeightNoise.SetFractalOctaves(3);
    }

    public static float GetHeight(float wx, float wz)
    {
        float noise = (TerrainHeightNoise.GetNoise(wx, wz) + 1f) / 2f;
        float hillAmplitude = 8.0f; 
        return noise * hillAmplitude;
    }
}