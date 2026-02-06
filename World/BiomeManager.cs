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
    private const float SEA_LEVEL = 64f; // Shared baseline for all biomes

    public static BiomeSettings GetSettings(BiomeType type) => type switch
    {
        BiomeType.Plains => new BiomeSettings
        {
            BaseHeight = SEA_LEVEL,
            Variation = 6,
            Frequency = 0.004f
        },

        // MOUNTAINS: 
        // 1. Higher BaseHeight (starts higher up)
        // 2. Massive Variation (allows for huge peaks)
        // 3. Frequency: Keep this low enough so the peaks are wide and grand
        BiomeType.Mountains => new BiomeSettings
        {
            BaseHeight = SEA_LEVEL + 40, // Significant jump from sea level
            Variation = 150,             // Enough room to hit the height limit
            Frequency = 0.006f           // Lowered slightly to make them "fatter"
        },

        BiomeType.Forest => new BiomeSettings { BaseHeight = SEA_LEVEL + 4, Variation = 15, Frequency = 0.008f },
        _ => new BiomeSettings { BaseHeight = SEA_LEVEL, Variation = 10, Frequency = 0.005f }
    };
    public static BiomeType DetermineBiome(float temp, float humidity)
    {
        // 1. THE EXTREMES (Mountains and Tundra)
        if (temp < 0.3f)
        {
            // Even in the cold, humidity decides if it's a jagged mountain or a flat tundra
            return humidity > 0.5f ? BiomeType.Mountains : BiomeType.Tundra;
        }

        // 2. THE HOT ZONE (Desert and Tropical Forest)
        if (temp > 0.7f)
        {
            if (humidity < 0.35f) return BiomeType.Desert;
            if (humidity > 0.65f) return BiomeType.Forest; // Lush Jungle feel
            return BiomeType.Plains; // Savannah feel
        }

        // 3. THE TEMPERATE ZONE (The Most Common Area)
        // We split this more finely to ensure Forest and Plains intersperse
        if (temp >= 0.3f && temp <= 0.7f)
        {
            // If it's wet, it's a Forest
            if (humidity > 0.6f) return BiomeType.Forest;

            // If it's mid-humidity, it's Plains
            if (humidity > 0.3f) return BiomeType.Plains;

            // If it's dry but not "Hot", it's a dry grassland/shrubland (Plains)
            return BiomeType.Plains;
        }

        return BiomeType.Plains;
    }
}