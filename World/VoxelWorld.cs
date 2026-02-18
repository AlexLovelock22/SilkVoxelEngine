using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using VoxelEngine_Silk.Net_1._0.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;

namespace VoxelEngine_Silk.Net_1._0.World;

public class VoxelWorld
{
    public ConcurrentDictionary<(int, int), Chunk> Chunks = new();
    public FastNoiseLite ContinentalNoise = new();
    public FastNoiseLite ErosionNoise = new();
    public FastNoiseLite HeightNoise = new();
    public FastNoiseLite TempNoise = new();
    public FastNoiseLite HumidityNoise = new();
    public FastNoiseLite RiverNoise = new();
    private bool _heightmapNeedsUpdate = false;

    public const int WorldMapSize = 1024;
    private byte[] _globalHeightmapData = new byte[WorldMapSize * WorldMapSize];

    public byte[] GetHeightmapData() => _globalHeightmapData;

    public VoxelWorld(int seed = 13308)
    {
        BiomeManager.InitializeNoise(this, seed);
    }

    public (Chunk? r, Chunk? l, Chunk? f, Chunk? b) GetNeighbors(int cx, int cz)
    {
        Chunks.TryGetValue((cx + 1, cz), out var r);
        Chunks.TryGetValue((cx - 1, cz), out var l);
        Chunks.TryGetValue((cx, cz + 1), out var f);
        Chunks.TryGetValue((cx, cz - 1), out var b);
        return (r, l, f, b);
    }

    public byte GetBlock(int x, int y, int z)
    {
        int cx = (int)Math.Floor(x / (float)Chunk.Size);
        int cz = (int)Math.Floor(z / (float)Chunk.Size);

        int lx = x - (cx * Chunk.Size);
        int lz = z - (cz * Chunk.Size);

        if (Chunks.TryGetValue((cx, cz), out var chunk))
        {
            if (y < 0 || y >= Chunk.Height) return (byte)BlockType.Air;
            return chunk.Blocks[lx, y, lz];
        }

        return (byte)BlockType.Air;
    }

    public void SetBlock(int x, int y, int z, byte type, Silk.NET.OpenGL.GL gl, uint voxelTex3D)
    {
        int cx = (int)Math.Floor(x / 16.0);
        int cz = (int)Math.Floor(z / 16.0);

        if (Chunks.TryGetValue((cx, cz), out var chunk))
        {
            int lx = x - (cx * 16);
            int lz = z - (cz * 16);

            if (y >= 0 && y < 256)
            {
                chunk.Blocks[lx, y, lz] = type;
                int texX = ((x % 1024) + 1024) % 1024;
                int texZ = ((z % 1024) + 1024) % 1024;
                MeshManager.UpdateVoxelIn3DTexture(gl, voxelTex3D, texX, y, texZ, type);

                chunk.IsDirty = true;
                UpdateNeighborIfEdge(x, y, z, lx, lz, cx, cz);
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

    public void ExportBiomeMap(int size)
    {
        using (Image<Rgba32> image = new Image<Rgba32>(size, size))
        {
            for (int x = 0; x < size; x++)
            {
                for (int z = 0; z < size; z++)
                {
                    float worldX = x - (size / 2);
                    float worldZ = z - (size / 2);

                    // FIX: Use the exact same 3-arg method as Chunk.cs
                    BiomeType type = BiomeManager.GetBiomeAt(this, worldX, worldZ);

                    image[x, z] = type switch
                    {
                        BiomeType.Ocean => new Rgba32(0, 0, 128),
                        BiomeType.River => new Rgba32(0, 191, 255),
                        BiomeType.Mountains => new Rgba32(105, 105, 105),
                        BiomeType.Forest => new Rgba32(34, 139, 34),
                        BiomeType.Desert => new Rgba32(237, 201, 175),
                        BiomeType.Tundra => new Rgba32(200, 245, 255),
                        BiomeType.Plains => new Rgba32(124, 252, 0),
                        _ => new Rgba32(0, 0, 0)
                    };
                }
            }
            image.Save("WorldBiomeMap.png");
        }
    }
}