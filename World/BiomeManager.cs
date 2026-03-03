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
    public struct ChunkNoiseMap
    {
        public float[,] Heights;
        public BiomeType[,] Biomes;

        public ChunkNoiseMap(int size)
        {
            Heights = new float[size, size];
            Biomes = new BiomeType[size, size];
        }
    }

    public const float SEA_LEVEL = 62f;

    public static void InitializeNoise(VoxelWorld world, int seed)
    {
        // Continentalness: Smoother frequency for larger landmasses
        world.ContinentalNoise.SetSeed(seed);
        world.ContinentalNoise.SetFrequency(0.00008f);
        world.ContinentalNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        world.ContinentalNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        world.ContinentalNoise.SetFractalOctaves(9);

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
    }

    public static ChunkNoiseMap GetNoiseMap(VoxelWorld world, int chunkX, int chunkZ)
    {
        int size = 16;
        ChunkNoiseMap map = new ChunkNoiseMap(size);
        int startX = chunkX * size;
        int startZ = chunkZ * size;

        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                float wx = startX + x;
                float wz = startZ + z;

                // STEP 1: Get the height once
                float h = GetHeightAt(world, wx, wz);
                map.Heights[x, z] = h;

                // STEP 2: Get the biome using that pre-calculated height
                // This is the "Major Bit" that stops the 7-second lag.
                map.Biomes[x, z] = GetBiomeAtWithHeight(world, wx, wz, h);
            }
        }
        return map;
    }

    public static BiomeType GetBiomeAtWithHeight(VoxelWorld world, float wx, float wz, float height)
    {
        // 1. Use the height we already calculated - NO MORE RE-CALCULATING!
        if (height <= SEA_LEVEL) return BiomeType.Ocean;

        // 2. THE RUFFLE (High-frequency jitter for biome edges)
        float ruffleFreq = 40.1f;
        float ruffleAmp = 15.0f;
        float jitterX = world.ErosionNoise.GetNoise(wx * ruffleFreq, wz * ruffleFreq) * ruffleAmp;
        float jitterZ = world.ErosionNoise.GetNoise(wz * ruffleFreq, (wx + 123) * ruffleFreq) * ruffleAmp;

        // Sample climate with the ruffled coordinates
        float temp = (world.TempNoise.GetNoise(wx + jitterX, wz + jitterZ) + 1f) / 2f;
        float humidity = (world.HumidityNoise.GetNoise(wx + jitterX, wz + jitterZ) + 1f) / 2f;

        // 3. CLIMATE PRE-CHECK
        bool isDesert = temp > 0.72f && humidity < 0.35f;
        float relativeHeight = height - SEA_LEVEL;

        // 4. MOUNTAIN LOGIC
        float mountainRegionMask = world.ErosionNoise.GetNoise(wx * 0.005f, wz * 0.005f);
        float baseRuffle = world.ErosionNoise.GetNoise(wx * 0.2f, wz * 0.2f) * 10f;
        float erosionValue = Math.Abs(world.ErosionNoise.GetNoise(wx, wz));
        float ruggedness = (erosionValue * 0.9f) + (relativeHeight / 80f);

        if (!isDesert && ruggedness > 0.32f && mountainRegionMask > -0.1f && relativeHeight > (15f + baseRuffle))
        {
            return BiomeType.Mountains;
        }

        // 5. FINAL BIOME SELECTION
        if (isDesert) return BiomeType.Desert;
        if (temp < 0.32f) return BiomeType.Tundra;
        if (humidity > 0.68f) return BiomeType.Forest;

        return BiomeType.Plains;
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
        float rawContinental = world.ContinentalNoise.GetNoise(wx, wz);
        float continentalness = ApplyContinentalContrast(rawContinental);

        // OCEAN BRANCH
        if (continentalness < 0)
        {
            return SEA_LEVEL + (continentalness * 40f);
        }

        // LAND BRANCH
        float temp = (world.TempNoise.GetNoise(wx, wz) + 1f) / 2f;
        float humidity = (world.HumidityNoise.GetNoise(wx, wz) + 1f) / 2f;

        // 1. MATCHING THRESHOLDS
        bool isDesertZone = temp > 0.72f && humidity < 0.35f;

        float desertInfluence = 0f;
        if (isDesertZone)
        {
            // 2. INTERNAL BUFFER (The 30-block inset)
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
        float biomeShape = (mountainHeight * (1.0f - desertInfluence)) + (finalDuneHeight * desertInfluence);
        float totalLandHeight = (basePlains + biomeShape) * beachCurve;

        return SEA_LEVEL + totalLandHeight;
    }

    public static BiomeType GetBiomeAt(VoxelWorld world, float wx, float wz)
    {
        // Instead of doing its own math, it calls the height first
        float height = GetHeightAt(world, wx, wz);
        // Then passes it to the internal logic
        return GetBiomeAtWithHeight(world, wx, wz, height);
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