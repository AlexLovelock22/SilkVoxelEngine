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
        // 1. Height Noise (The actual verticality)
        HeightNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);

        // 2. Temperature (Low frequency = big blobs of heat/cold)
        TempNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        TempNoise.SetFrequency(0.005f);

        // 3. Humidity (Big blobs of wet/dry)
        HumidityNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        HumidityNoise.SetFrequency(0.005f);
        HumidityNoise.SetSeed(1337); // Different seed from Temp
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