using System;

namespace VoxelEngine_Silk.Net_1._0.World;

public static class MountainsBiome
{
    private static readonly FastNoiseLite RidgeStructure = new FastNoiseLite();
    private static readonly FastNoiseLite SubRidgeNoise = new FastNoiseLite();

    public static void Initialize(int seed)
    {
        // Controls the "Sub-Ridges" (the branching spurs)
        RidgeStructure.SetSeed(seed + 201);
        RidgeStructure.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        RidgeStructure.SetFrequency(0.005f); 
        RidgeStructure.SetFractalType(FastNoiseLite.FractalType.Ridged);
        RidgeStructure.SetFractalOctaves(3);

        // Fine Crags
        SubRidgeNoise.SetSeed(seed + 202);
        SubRidgeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        SubRidgeNoise.SetFrequency(0.018f);
        SubRidgeNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
    }

    public static float GetHeight(float wx, float wz, float ridgeRaw, float mWeight)
    {
        // 1. The Main Peak & Ridge (Linear Trend)
        // We normalize the ridge spline to a 0-1 range.
        float mainRidge = Math.Clamp((ridgeRaw - 0.45f) / 0.55f, 0f, 1f);
        
        // Linear gradient: The higher the ridgeRaw, the closer to the "Main Peak"
        float linearBase = mainRidge * 120f; 

        // 2. Sub-Ridgelines (The "Skirt")
        // These are only visible when we are already on a mountain slope
        float spurs = (RidgeStructure.GetNoise(wx, wz) + 1f) / 2f;
        float subRidges = spurs * 30f * mainRidge;

        // 3. Erosion/Valleys
        // We use the sub-ridges to "cut" into the mountain
        float shape = linearBase + subRidges;

        // 4. Craggy Details
        float crags = SubRidgeNoise.GetNoise(wx, wz) * 10f * mainRidge;

        return shape + crags;
    }
}