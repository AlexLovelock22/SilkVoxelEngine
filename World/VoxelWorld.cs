using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace VoxelEngine_Silk.Net_1._0.World;

public class VoxelWorld
{
    public ConcurrentDictionary<(int, int), Chunk> Chunks = new();
    // Noise Generators
    public FastNoiseLite HeightNoise = new();
    public FastNoiseLite TempNoise = new();
    public FastNoiseLite HumidityNoise = new();
    public (Chunk? r, Chunk? l, Chunk? f, Chunk? b) GetNeighbors(int cx, int cz)
    {
        Chunks.TryGetValue((cx + 1, cz), out var r);
        Chunks.TryGetValue((cx - 1, cz), out var l);
        Chunks.TryGetValue((cx, cz + 1), out var f);
        Chunks.TryGetValue((cx, cz - 1), out var b);
        return (r, l, f, b);
    }

    public VoxelWorld()
    {
        // 1. HEIGHT NOISE (The physical terrain)
        HeightNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        HeightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        HeightNoise.SetFractalOctaves(5);
        HeightNoise.SetFractalLacunarity(2.0f);
        HeightNoise.SetFractalGain(0.6f);

        // 2. TEMPERATURE (Climate Size & Variation)
        TempNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        TempNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        TempNoise.SetFractalOctaves(3); // Adds jaggedness to biome borders
                                        // This low frequency ensures the main climate zones are large
        TempNoise.SetFrequency(0.003f);

        // 3. HUMIDITY (Moisture Distribution)
        HumidityNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        HumidityNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        HumidityNoise.SetFractalOctaves(3);
        // Setting this slightly higher than Temp prevents biomes from looking like circles
        HumidityNoise.SetFrequency(0.005f);
        HumidityNoise.SetSeed(1337);
    }


    public byte GetBlock(int x, int y, int z)
    {
        // 1. Identify which chunk these global coordinates belong to
        int cx = (int)Math.Floor(x / (float)Chunk.Size);
        int cz = (int)Math.Floor(z / (float)Chunk.Size);

        // 2. Find local coordinates within that chunk (0 to 15)
        int lx = x - (cx * Chunk.Size);
        int lz = z - (cz * Chunk.Size);

        if (Chunks.TryGetValue((cx, cz), out var chunk))
        {
            // FIX: Change Size (16) to Height (256)
            if (y < 0 || y >= Chunk.Height) return 0;
            return chunk.Blocks[lx, y, lz];
        }
        return 0;
    }
}