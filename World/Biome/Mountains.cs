using System;

namespace VoxelEngine_Silk.Net_1._0.World;

public static class MountainsBiome
{
    private static readonly FastNoiseLite RidgeStructure = new FastNoiseLite();
    private static readonly FastNoiseLite SubRidgeNoise = new FastNoiseLite();

    public const float LIFT_START = 0.50f; 

    public static void Initialize(int seed)
    {
        RidgeStructure.SetSeed(seed + 201);
        RidgeStructure.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        RidgeStructure.SetFrequency(0.002f); 
        RidgeStructure.SetFractalType(FastNoiseLite.FractalType.Ridged);
        RidgeStructure.SetFractalOctaves(2);

        SubRidgeNoise.SetSeed(seed + 202);
        SubRidgeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        SubRidgeNoise.SetFrequency(0.005f); 
        SubRidgeNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
    }

    public static float GetHeight(float wx, float wz, float ridgeRaw)
    {
        // 1. Normalize the ridge (0.0 to 1.0)
        float ridgeAlpha = Math.Clamp((ridgeRaw - LIFT_START) / (1.0f - LIFT_START), 0f, 1f);
        
        // 2. Sharpen the Spine (Fix for "flat areas")
        // Increasing the power to 2.2 ensures the "peak" is narrow and tall.
        // This prevents the "flat mountain biome" issue.
        float ridgeLine = MathF.Pow(ridgeAlpha, 2.2f);

        // 3. Ridgeline Scaling
        // We've increased the multiplier from 115 to 145 for taller peaks.
        float verticalScale = 145f;

        // 4. Skirt Tapering (Fix for Image 1)
        // This ensures lone peaks spread their "feet" out to fill the biome.
        float skirt = MathF.Sin(ridgeAlpha * MathF.PI * 0.5f); 

        // 5. Structural Spurs (Branching ridges)
        float spurs = (RidgeStructure.GetNoise(wx, wz) + 1f) / 2f;
        
        // Combine: Main Spine is dominant, spurs create the "direction" seen in Image 1.
        float structure = (ridgeLine * 0.85f) + (spurs * 0.15f * skirt);

        float elevation = structure * verticalScale;
        
        // Keep detail noise low to maintain the clean "Image 2" ridgeline shape
        float details = SubRidgeNoise.GetNoise(wx, wz) * 5f * ridgeLine;

        return elevation + details;
    }
}