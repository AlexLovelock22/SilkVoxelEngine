using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VoxelEngine_Silk.Net_1._0.World.Biomes;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.World;

public enum BiomeType { Ocean, Desert, Plains, Mountains, Forest, Tundra }

public static class BiomeManager
{
    public const float SEA_LEVEL = 62f;

    public static void InitializeNoise(VoxelWorld world, int seed)
    {
        // Continentalness: Smoother frequency for larger landmasses
        world.ContinentalNoise.SetSeed(seed);
        world.ContinentalNoise.SetFrequency(0.00008f);
        world.ContinentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ContinentalNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ContinentalNoise.SetFractalOctaves(7);

        // Erosion: Used for the "Ruffle" and shelf structure
        world.ErosionNoise.SetSeed(seed + 101);
        world.ErosionNoise.SetFrequency(0.0006f);
        world.ErosionNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ErosionNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ErosionNoise.SetFractalOctaves(5);

        // The Peaks: Ridged noise for the "Proud" structure
        world.RiverNoise.SetSeed(seed + 102);
        world.RiverNoise.SetFrequency(0.0008f);
        world.RiverNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.RiverNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        world.RiverNoise.SetFractalOctaves(6);

        world.TempNoise.SetSeed(seed + 202);
        world.TempNoise.SetFrequency(0.0001f);
        world.HumidityNoise.SetSeed(seed + 303);
        world.HumidityNoise.SetFrequency(0.0001f);
        ExportNoiseAreaImage(world, 0, 0, 6000);
    }
    public static void ExportNoiseAreaImage(VoxelWorld world, int centerX, int centerZ, int size)
    {
        int halfSize = size / 2;
        int startX = centerX - halfSize;
        int startZ = centerZ - halfSize;

        using (Image<L8> image = new Image<L8>(size, size)) // Greyscale is clearer for masks
        {
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    float wx = startX + x;
                    float wz = startZ + z;

                    float temp = (world.TempNoise.GetNoise(wx, wz) + 1f) / 2f;
                    float hum = (world.HumidityNoise.GetNoise(wx, wz) + 1f) / 2f;

                    // This is the specific logic that controls dune height
                    float desertInfluence = Math.Clamp((temp - 0.65f) * 2f, 0, 1) * Math.Clamp((0.45f - hum) * 2f, 0, 1);

                    image[x, z] = new L8((byte)(desertInfluence * 255));
                }
            }
            image.Save("DuneMaskDebug.png");
        }
    }

    private static float ApplyContinentalContrast(float noiseValue) => MathF.Tanh(noiseValue * 3.5f);

  

    public static byte GetBlockAt(VoxelWorld world, int y, float surfaceHeight, BiomeType biome)
    {
        if (y > surfaceHeight)
        {
            if (y <= SEA_LEVEL) return 3;
            return 0;
        }

        if (y >= surfaceHeight - 1)
        {
            // Beach Check
            if (surfaceHeight <= SEA_LEVEL + 1.8f && biome != BiomeType.Desert)
                return 6;

            return GetSurfaceBlock(biome, 0, 0);
        }

        return GetFillerBlock(biome);
    }

   public static float GetHeightAt(VoxelWorld world, float wx, float wz)
    {
        // Fix the (0,0) pillar artifact
        wx += 0.001f;
        wz += 0.001f;

        float rawContinental = world.ContinentalNoise.GetNoise(wx, wz);
        float continentalness = ApplyContinentalContrast(rawContinental);

        if (continentalness < 0)
        {
            return SEA_LEVEL + (continentalness * 40f);
        }
        else
        {
            float temp = (world.TempNoise.GetNoise(wx, wz) + 1f) / 2f;
            float humidity = (world.HumidityNoise.GetNoise(wx, wz) + 1f) / 2f;
            
            // 1. MATCHING THRESHOLDS
            // We use the exact same logic as GetBiomeAt. 
            // Any area outside this "Box" will have 0 desertInfluence.
            bool isDesertZone = temp > 0.72f && humidity < 0.35f;

            float desertInfluence = 0f;
            if (isDesertZone)
            {
                // 2. INTERNAL BUFFER (The 30-block inset)
                // We calculate how far "into" the desert we are. 
                // This creates the subtle ramp-up you wanted.
                float edgeBuffer = Math.Min((temp - 0.72f) * 40f, (0.35f - humidity) * 40f);
                desertInfluence = Math.Clamp(edgeBuffer, 0, 1);
            }

            float beachCurve = MathF.Pow(Math.Clamp(continentalness * 8.0f, 0, 1), 2.0f);
            float basePlains = 5f;

            // 3. CALCULATE STANDARD TERRAIN
            float warpScale = 45.0f;
            float warpX = world.ErosionNoise.GetNoise(wx * 1.5f, wz * 1.5f) * warpScale;
            float warpZ = world.ErosionNoise.GetNoise(wz * 1.5f, wx * 1.5f) * warpScale;
            float erosionValue = world.ErosionNoise.GetNoise(wx, wz);
            float rawPeak = (world.RiverNoise.GetNoise(wx + warpX, wz + warpZ) + 1.0f) / 2.0f;
            float proudPeaks = MathF.Pow(rawPeak, 3.5f) * 145f; 
            float shelfDetail = MathF.Abs(world.ErosionNoise.GetNoise(wx * 3f, wz * 3f)) * 8f;
            float mountainHeight = (proudPeaks + shelfDetail) * Math.Clamp(erosionValue + 0.5f, 0, 1);

            // 4. CALCULATE DESERT TERRAIN
            float finalDuneHeight = Desert.GetHeight(world, wx, wz);

            // 5. FINAL BLEND
            // If isDesertZone is false, desertInfluence is 0, and dunes are physically impossible.
            float biomeShape = (mountainHeight * (1.0f - desertInfluence)) + (finalDuneHeight * desertInfluence);

            float totalLandHeight = (basePlains + biomeShape) * beachCurve;

            return SEA_LEVEL + totalLandHeight;
        }
    }

    public static BiomeType GetBiomeAt(VoxelWorld world, float wx, float wz)
    {
        float height = GetHeightAt(world, wx, wz);
        if (height <= SEA_LEVEL) return BiomeType.Ocean;

        float temp = (world.TempNoise.GetNoise(wx, wz) + 1f) / 2f;
        float humidity = (world.HumidityNoise.GetNoise(wx, wz) + 1f) / 2f;

        // 6. SYNCED CHECK
        // This must match the 'isDesertZone' logic in GetHeightAt exactly.
        if (temp > 0.72f && humidity < 0.35f) return BiomeType.Desert;

        float relativeHeight = height - SEA_LEVEL;
        float erosionValue = world.ErosionNoise.GetNoise(wx, wz);
        float stoneScore = (relativeHeight / 45f) + (erosionValue * 0.55f);

        if (stoneScore > 0.65f) return BiomeType.Mountains;
        if (temp < 0.32f) return BiomeType.Tundra;
        if (humidity > 0.68f) return BiomeType.Forest;

        return BiomeType.Plains;
    }

    public static byte GetSurfaceBlock(BiomeType type, float wx, float wz)
    {
        return type switch
        {
            BiomeType.Ocean => 6,
            BiomeType.Desert => 6,
            BiomeType.Mountains => 5, // Stone
            BiomeType.Tundra => 8,
            BiomeType.Forest => 9,
            _ => 1
        };
    }

    public static byte GetFillerBlock(BiomeType type)
    {
        return type switch
        {
            BiomeType.Desert => 6,
            BiomeType.Mountains => 5,
            _ => 2
        };
    }

    public static bool IsLocalWater(BiomeType type, float wx, float wz) => type == BiomeType.Ocean;
}