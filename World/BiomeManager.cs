public enum BiomeType { Desert, Plains, Forest, Tundra, Mountains }

public struct BiomeSettings
{
    public float BaseHeight;
    public float Variation; // Amplitude
    public float Frequency; // Frequency
    public byte SurfaceBlock; // For later when we add textures
}

public static class BiomeManager
{
    public static BiomeSettings GetSettings(BiomeType type) => type switch
    {
        BiomeType.Desert => new BiomeSettings { BaseHeight = 30, Variation = 1, Frequency = 0.01f },
        BiomeType.Plains => new BiomeSettings { BaseHeight = 50, Variation = 3, Frequency = 0.02f },
        BiomeType.Forest => new BiomeSettings { BaseHeight = 70, Variation = 6, Frequency = 0.03f },
        BiomeType.Mountains => new BiomeSettings { BaseHeight = 120, Variation = 60, Frequency = 0.035f },
        _ => new BiomeSettings { BaseHeight = 5, Variation = 5, Frequency = 0.02f }
    };

    public static BiomeType DetermineBiome(float temp, float humidity)
    {
        // Simple logic:
        if (temp > 0.6f)
        {
            return humidity < 0.3f ? BiomeType.Desert : BiomeType.Forest;
        }
        if (temp < 0.3f) return BiomeType.Tundra;

        return humidity > 0.5f ? BiomeType.Forest : BiomeType.Plains;
    }
}