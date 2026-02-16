using System;
using System.Diagnostics;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.World;

public enum BiomeType { Desert, Plains, Forest, Tundra, Mountains }

public struct BiomeSettings
{
    public float BaseHeight;
    public float Variation;
    public float Frequency;
    public float IdealTemp;
    public float IdealHumidity;
}

public static class BiomeManager
{
    public const float SEA_LEVEL = 64f;
    private static int _logCounter = 0;

    // SCALING UP: Lowered from 0.0002f to 0.00005f for massive biomes.
    public const float CLIMATE_SCALE = 0.00005f; 

    public static BiomeSettings GetSettings(BiomeType type) => type switch
    {
        // Mountains: Highest peaks, very rugged
        BiomeType.Mountains => new BiomeSettings { 
            BaseHeight = 125, Variation = 90, Frequency = 0.015f, 
            IdealTemp = 0.50f, IdealHumidity = 0.85f 
        },
        // Forest: Higher base, medium variation
        BiomeType.Forest => new BiomeSettings { 
            BaseHeight = 78, Variation = 25, Frequency = 0.01f, 
            IdealTemp = 0.55f, IdealHumidity = 0.60f 
        },
        // Desert: Rolling dunes, very low frequency for smoothness
        BiomeType.Desert => new BiomeSettings { 
            BaseHeight = 70, Variation = 15, Frequency = 0.005f, 
            IdealTemp = 0.85f, IdealHumidity = 0.20f 
        },
        // Plains: Flat and low
        BiomeType.Plains => new BiomeSettings { 
            BaseHeight = 67, Variation = 10, Frequency = 0.006f, 
            IdealTemp = 0.50f, IdealHumidity = 0.30f 
        },
        // Tundra: Very flat, cold regions
        BiomeType.Tundra => new BiomeSettings { 
            BaseHeight = 64, Variation = 6, Frequency = 0.005f, 
            IdealTemp = 0.20f, IdealHumidity = 0.40f 
        },
        _ => new BiomeSettings { 
            BaseHeight = 64, Variation = 10, Frequency = 0.01f, 
            IdealTemp = 0.5f, IdealHumidity = 0.5f 
        }
    };

    public static float GetHeightAt(VoxelWorld world, float wx, float wz)
    {
        // Sample Climate at the massive scale
        float tRaw = world.TempNoise.GetNoise(wx * CLIMATE_SCALE, wz * CLIMATE_SCALE);
        float hRaw = world.HumidityNoise.GetNoise((wx + 5000) * CLIMATE_SCALE, (wz + 5000) * CLIMATE_SCALE);

        float temp = (tRaw + 1f) / 2f;
        float humid = (hRaw + 1f) / 2f;

        return CalculateBlendedHeight(world, wx, wz, temp, humid);
    }

    private static float CalculateBlendedHeight(VoxelWorld world, float wx, float wz, float t, float h)
    {
        float totalWeight = 0;
        float weightedHeight = 0;

        foreach (BiomeType type in Enum.GetValues(typeof(BiomeType)))
        {
            float weight = GetBiomeWeight(type, t, h);
            if (weight <= 0) continue;

            // Blending Curve
            float smoothWeight = weight * weight * (3 - 2 * weight);
            var s = GetSettings(type);
            
            // Sample height noise for the specific biome
            float noise = world.HeightNoise.GetNoise(wx * s.Frequency, wz * s.Frequency);
            float hSample = s.BaseHeight + (noise * s.Variation);

            weightedHeight += hSample * smoothWeight;
            totalWeight += smoothWeight;
        }

        float baseResult = totalWeight > 0 ? (weightedHeight / totalWeight) : SEA_LEVEL;

        // Continental Macro-Variation (very slow shifts in elevation)
        float macro = world.HeightNoise.GetNoise(wx * 0.0002f, wz * 0.0002f) * 25f;

        return baseResult + macro;
    }

    private static float GetBiomeWeight(BiomeType type, float t, float h)
    {
        // Blend determines how wide the transition area is between biomes
        float blend = 0.25f; 

        return type switch
        {
            BiomeType.Tundra    => Math.Clamp((0.35f - t) / blend, 0, 1),
            BiomeType.Mountains => Math.Clamp((h - 0.70f) / blend, 0, 1),
            BiomeType.Desert    => Math.Min(Math.Clamp((t - 0.60f) / blend, 0, 1), Math.Clamp((0.40f - h) / blend, 0, 1)),
            BiomeType.Forest    => Math.Min(Math.Clamp((h - 0.45f) / blend, 0, 1), Math.Clamp((0.75f - h) / blend, 0, 1)),
            BiomeType.Plains    => Math.Min(
                                    Math.Min(Math.Clamp((t - 0.30f) / blend, 0, 1), Math.Clamp((0.70f - t) / blend, 0, 1)),
                                    Math.Clamp((0.50f - h) / blend, 0, 1)),
            _ => 0
        };
    }

    public static BiomeType GetBiomeAt(float temp, float humid)
    {
        // Priority selection for UI/Logic
        if (temp < 0.35f) return BiomeType.Tundra;
        if (humid > 0.70f) return BiomeType.Mountains;
        if (temp > 0.60f && humid < 0.40f) return BiomeType.Desert;
        if (humid > 0.45f) return BiomeType.Forest;
        return BiomeType.Plains;
    }
}