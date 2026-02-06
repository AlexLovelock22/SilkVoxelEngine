public enum BiomeType { Desert, Plains, Forest, Tundra, Mountains }

public struct BiomeSettings
{
    public float BaseHeight;
    public float Variation;
    public float Frequency; 
    public byte SurfaceBlock; 
}

public static class BiomeManager
{
    private const float SEA_LEVEL = 64f; 

    public static BiomeSettings GetSettings(BiomeType type) => type switch
    {
        BiomeType.Plains => new BiomeSettings
        {
            BaseHeight = SEA_LEVEL,
            Variation = 6,
            Frequency = 0.004f
        },

        BiomeType.Mountains => new BiomeSettings
        {
            BaseHeight = SEA_LEVEL + 40,
            Variation = 150,            
            Frequency = 0.006f          
        },

        BiomeType.Forest => new BiomeSettings { BaseHeight = SEA_LEVEL + 4, Variation = 15, Frequency = 0.008f },
        _ => new BiomeSettings { BaseHeight = SEA_LEVEL, Variation = 10, Frequency = 0.005f }
    };
    public static BiomeType DetermineBiome(float temp, float humidity)
    {
        if (temp < 0.3f)
        {
            return humidity > 0.5f ? BiomeType.Mountains : BiomeType.Tundra;
        }

        if (temp > 0.7f)
        {
            if (humidity < 0.35f) return BiomeType.Desert;
            if (humidity > 0.65f) return BiomeType.Forest;
            return BiomeType.Plains; 
        }
       
        if (temp >= 0.3f && temp <= 0.7f)
        {
            if (humidity > 0.6f) return BiomeType.Forest;
            if (humidity > 0.3f) return BiomeType.Plains;
            return BiomeType.Plains;
        }

        return BiomeType.Plains;
    }
}