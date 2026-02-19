using System;

namespace VoxelEngine_Silk.Net_1._0.World;

public static class ForestBiome
{
    private static readonly FastNoiseLite MoundNoise = new FastNoiseLite();
    private static readonly FastNoiseLite GullyNoise = new FastNoiseLite();

    public static void Initialize(int seed)
    {
        // Smooth, rolling forest floor
        MoundNoise.SetSeed(seed + 101);
        MoundNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        MoundNoise.SetFrequency(0.006f);
        MoundNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        MoundNoise.SetFractalOctaves(3);

        // Gullies: Lower frequency (0.012) and single octave for clean, rare lines
        GullyNoise.SetSeed(seed + 102);
        GullyNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        GullyNoise.SetFrequency(0.012f);
        GullyNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        GullyNoise.SetFractalOctaves(1);
    }

    public static float GetHeight(float wx, float wz)
    {
        float mounds = (MoundNoise.GetNoise(wx, wz) + 1f) / 2f;
        float height = mounds * 4.0f;

        if (IsGully(wx, wz))
        {
            // We drop the terrain 1 block to make a "trench"
            height -= 1.0f;
        }

        return height;
    }

    /// <summary>
    /// Returns true if the coordinates fall on a sharp ridge in the gully noise.
    /// </summary>
    public static bool IsGully(float wx, float wz)
    {
        float gV = GullyNoise.GetNoise(wx, wz);
        // 0.94 is a very high threshold, making gullies quite rare and thin.
        return gV > 0.94f;
    }
}