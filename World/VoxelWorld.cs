using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace VoxelEngine_Silk.Net_1._0.World;

public class VoxelWorld
{
    public ConcurrentDictionary<(int, int), Chunk> Chunks = new();
    public FastNoiseLite HeightNoise = new();
    public FastNoiseLite TempNoise = new();
    public FastNoiseLite HumidityNoise = new();
    public FastNoiseLite RiverNoise = new();
    private bool _heightmapNeedsUpdate = false;

    public const int WorldMapSize = 1024;
    private byte[] _globalHeightmapData = new byte[WorldMapSize * WorldMapSize];

    public byte[] GetHeightmapData() => _globalHeightmapData;

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
        // General Terrain Shape
        HeightNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        HeightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        HeightNoise.SetFractalOctaves(5);
        HeightNoise.SetFractalLacunarity(2.0f);
        HeightNoise.SetFractalGain(0.6f);

        // Climate logic for Biome blending
        TempNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        TempNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        TempNoise.SetFractalOctaves(3);
        TempNoise.SetFrequency(0.003f);

        HumidityNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        HumidityNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        HumidityNoise.SetFractalOctaves(3);
        HumidityNoise.SetFrequency(0.005f);
        HumidityNoise.SetSeed(1337);

        // River logic
        RiverNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        RiverNoise.SetFrequency(0.002f);
    }

    /// <summary>
    /// Gets a block at world coordinates. Used by Physics and Mesh Gen.
    /// </summary>
    public byte GetBlock(int x, int y, int z)
    {
        int cx = (int)Math.Floor(x / (float)Chunk.Size);
        int cz = (int)Math.Floor(z / (float)Chunk.Size);

        int lx = x - (cx * Chunk.Size);
        int lz = z - (cz * Chunk.Size);

        if (Chunks.TryGetValue((cx, cz), out var chunk))
        {
            // If out of vertical bounds, return Air from the Registry
            if (y < 0 || y >= Chunk.Height) return (byte)BlockType.Air;

            return chunk.Blocks[lx, y, lz];
        }

        // If chunk isn't loaded, return Air
        return (byte)BlockType.Air;
    }

    // Inside VoxelWorld.cs
    // Inside VoxelWorld.cs
    public void SetBlock(int x, int y, int z, byte type)
    {
        int cx = (int)Math.Floor(x / (float)Chunk.Size);
        int cz = (int)Math.Floor(z / (float)Chunk.Size);

        if (Chunks.TryGetValue((cx, cz), out var chunk))
        {
            int lx = x - (cx * Chunk.Size);
            int lz = z - (cz * Chunk.Size);

            if (y >= 0 && y < Chunk.Height)
            {
                // 1. Update the 3D block data
                chunk.Blocks[lx, y, lz] = type;

                // 2. Update the internal HeightMap array immediately
                int[,] heightData = chunk.GetHeightMap();
                if (type != 0) // Block Placed (Assuming 0 is Air)
                {
                    if (y > heightData[lx, lz])
                    {
                        heightData[lx, lz] = y;
                    }
                }
                else // Block Mined
                {
                    // If we broke the block that was the highest, scan down to find the new surface
                    if (y == heightData[lx, lz])
                    {
                        int searchY = y;
                        while (searchY > 0 && chunk.Blocks[lx, searchY, lz] == 0)
                        {
                            searchY--;
                        }
                        heightData[lx, lz] = searchY;
                    }
                }

                // 3. Mark for re-meshing and neighbor updates
                chunk.IsDirty = true;
                UpdateNeighborIfEdge(x, y, z, lx, lz, cx, cz);

                // 4. Update the global CPU collage
                StitchChunkToHeightmap(chunk);
            }
        }
    }

    private void UpdateNeighborIfEdge(int x, int y, int z, int lx, int lz, int cx, int cz)
    {
        if (lx == 0) MarkDirty(cx - 1, cz);
        if (lx == Chunk.Size - 1) MarkDirty(cx + 1, cz);
        if (lz == 0) MarkDirty(cx, cz - 1);
        if (lz == Chunk.Size - 1) MarkDirty(cx, cz + 1);
    }

    private void MarkDirty(int cx, int cz)
    {
        if (Chunks.TryGetValue((cx, cz), out var neighbor)) neighbor.IsDirty = true;
    }


    public void StitchChunkToHeightmap(Chunk chunk)
    {
        int[,] localMap = chunk.GetHeightMap();

        // Ensure we don't stitch half-baked chunks
        if (localMap[8, 8] == 0 && localMap[0, 0] == 0) return;

        int worldStartX = chunk.ChunkX * Chunk.Size;
        int worldStartZ = chunk.ChunkZ * Chunk.Size;

        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                // This 1024-unit wrapping is what prevents the ray from "snapping" 
                // at world boundaries, as long as the shader mirrors it.
                int mX = (((worldStartX + x) + 512) % 1024 + 1024) % 1024;
                int mZ = (((worldStartZ + z) + 512) % 1024 + 1024) % 1024;

                _globalHeightmapData[mZ * 1024 + mX] = (byte)localMap[x, z];
            }
        }

        _heightmapNeedsUpdate = true;
    }
}