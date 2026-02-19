using System;

namespace VoxelEngine_Silk.Net_1._0.World;

public static class ForestBiome
{
    private static readonly FastNoiseLite MoundNoise = new FastNoiseLite();
    private static readonly FastNoiseLite GullyNoise = new FastNoiseLite();
    private static readonly FastNoiseLite BlotchNoise = new FastNoiseLite();

    // INCREASED THRESHOLD: 
    // 0.985f ensures that only the very tip of the ridge is used, 
    // tightening the width to roughly 1-2 blocks.
    private const float GULLY_THRESHOLD = 0.985f; 

    public static void Initialize(int seed)
    {
        // Smooth, rolling forest floor
        MoundNoise.SetSeed(seed + 101);
        MoundNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        MoundNoise.SetFrequency(0.006f); 
        MoundNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        MoundNoise.SetFractalOctaves(3);

        // GULLY SETTINGS
        GullyNoise.SetSeed(seed + 102);
        GullyNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        
        // Frequency controls how far apart they are. 
        // 0.004f keeps them rare as requested previously.
        GullyNoise.SetFrequency(0.004f); 
        GullyNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        GullyNoise.SetFractalOctaves(1);

        // Blotch noise for Grass (1) vs Moss (9) jigsaw pattern
        BlotchNoise.SetSeed(seed + 103);
        BlotchNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        BlotchNoise.SetFrequency(0.05f); 
    }

    public static float GetHeight(float wx, float wz)
    {
        float mounds = (MoundNoise.GetNoise(wx, wz) + 1f) / 2f;
        float height = mounds * 4.0f; 

        // If it's a gully, we drop the height by about 1.5 blocks to make room for water
        if (IsGully(wx, wz))
        {
            height -= 1.5f;
        }

        return height;
    }

    public static bool IsGully(float wx, float wz)
    {
        float gV = GullyNoise.GetNoise(wx, wz);
        
        // Ridged noise peaks at 1.0. 
        // By checking for values > 0.985, we catch only the very center of the ridge.
        return gV > GULLY_THRESHOLD; 
    }

    public static byte GetForestSurfaceBlock(float wx, float wz)
    {
        float noise = BlotchNoise.GetNoise(wx, wz);
        // Jigsaw mix: Moss (9) if positive, Grass (1) if negative
        return noise > 0f ? (byte)9 : (byte)1;
    }
}