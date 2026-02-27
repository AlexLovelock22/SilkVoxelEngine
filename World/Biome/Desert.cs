using System;

namespace VoxelEngine_Silk.Net_1._0.World.Biomes;

public static class Desert
{
   public static float GetHeight(VoxelWorld world, float wx, float wz)
{
    float frequency = 0.615f;
    
    float raw1 = world.HeightNoise.GetNoise(wx * frequency, wz * frequency);
    float raw2 = world.HeightNoise.GetNoise(wx * frequency * 3f, wz * frequency * 3f) * 0.2f;
    
    // TUNING ZONE:
    // Increase 2.5f to make the slopes longer/straighter.
    // Decrease 2.5f to make the peaks and valleys rounder/fatter.
    float combined = (raw1 + raw2 + 1.25f) / 2.5f; 
    combined = Math.Clamp(combined, 0, 1);

    // This is the "Magic" smoothing. 
    // You can run this twice (combined = t * t * (3 - 2 * t) again) for extreme smoothness.
    float smoothDunes = combined * combined * (3.0f - 2.0f * combined);

    return smoothDunes * 30f;
}
}