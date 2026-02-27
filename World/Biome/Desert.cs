using System;

namespace VoxelEngine_Silk.Net_1._0.World.Biomes;

public static class Desert
{
    public static float GetHeight(VoxelWorld world, float wx, float wz)
    {
        // 1. DUNE SHAPE NOISE (The high-frequency "Amount" of dunes)
        float frequency = 0.415f;
        float raw1 = world.HeightNoise.GetNoise(wx * frequency, wz * frequency);
        float raw2 = world.HeightNoise.GetNoise(wx * frequency * 3f, wz * frequency * 3f) * 0.2f;
        
        // Normalization & Smoothing
        float combined = (raw1 + raw2 + 1.25f) / 2.5f; 
        combined = Math.Clamp(combined, 0, 1);
        float smoothDunes = combined * combined * (3.0f - 2.0f * combined);

        // 2. PEAK VARIATION NOISE (The "Scale" of the dunes)
        // We use a MUCH lower frequency here so height changes over hundreds of blocks.
        // 0.005f means a height change every 200 blocks.
        float variationFreq = 0.005f;
        float heightVariation = (world.HeightNoise.GetNoise(wx * variationFreq, wz * variationFreq) + 1.0f) * 0.5f;

        // 3. DEFINE THE RANGE
        // Instead of a flat 30f, we lerp between a 'Min Height' and a 'Max Height'
        float minPeak = 10f;
        float maxPeak = 45f;
        float currentMaxHeight = minPeak + (heightVariation * (maxPeak - minPeak));

        // 4. COMBINE
        return smoothDunes * currentMaxHeight;
    }
}