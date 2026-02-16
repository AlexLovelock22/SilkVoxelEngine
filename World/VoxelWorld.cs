using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using VoxelEngine_Silk.Net_1._0.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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

    public VoxelWorld()
    {
        int seed = 1337; // Use a fixed seed for testing

        // Terrain Height: Standard frequency for local hills/valleys
        HeightNoise.SetSeed(seed);
        HeightNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        HeightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        HeightNoise.SetFractalOctaves(5);
        HeightNoise.SetFrequency(0.01f);

        // Temperature: Continental scale (1 cycle every ~500 blocks)
        TempNoise.SetSeed(seed + 1);
        TempNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        TempNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        TempNoise.SetFractalOctaves(6);
        TempNoise.SetFrequency(0.001f);

        // Humidity: Continental scale (1 cycle every ~500 blocks)
        HumidityNoise.SetSeed(seed + 2);
        HumidityNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        HumidityNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        HumidityNoise.SetFractalOctaves(3);
        HumidityNoise.SetFrequency(0.001f);
    }

    /// <summary>
    /// Generates a 2D image of the biome distribution centered at 0,0.
    /// This allows you to visualize the "jigsaw" look.
    /// </summary>
    public void ExportNoiseDebug(string path, int width, int height, float startX, float startZ)
    {
        using var image = new Image<L8>(width, height);

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float wx = startX + x;
                float wz = startZ + z;

                // We sample raw coordinates because frequency is set in the constructor
                float noiseValue = TempNoise.GetNoise(wx, wz);
                byte byteValue = (byte)(Math.Clamp((noiseValue + 1f) / 2f, 0f, 1f) * 255);
                image[x, z] = new L8(byteValue);
            }
        }
        image.Save(path);
    }

    public void ExportBiomeMap(int imageSize)
    {
        using Image<Rgb24> image = new Image<Rgb24>(imageSize, imageSize);

        for (int z = 0; z < imageSize; z++)
        {
            for (int x = 0; x < imageSize; x++)
            {
                float worldX = x - (imageSize / 2);
                float worldZ = z - (imageSize / 2);

                // Using the new unified sampling logic
                float t = (TempNoise.GetNoise(worldX, worldZ) + 1f) / 2f;
                float h = (HumidityNoise.GetNoise(worldX + 1000, worldZ + 1000) + 1f) / 2f;

                image[x, z] = GetBiomeColor(t, h);
            }
        }
        image.Save("BiomePreview_Organic.png");
    }


    private Rgb24 GetBiomeColor(float t, float h)
    {
        float minDistance = float.MaxValue;
        Rgb24 bestColor = Color.Magenta;

        foreach (BiomeType type in Enum.GetValues(typeof(BiomeType)))
        {
            var settings = BiomeManager.GetSettings(type);
            float dist = MathF.Sqrt(MathF.Pow(t - settings.IdealTemp, 2) + MathF.Pow(h - settings.IdealHumidity, 2));

            if (dist < minDistance)
            {
                minDistance = dist;
                bestColor = type switch
                {
                    BiomeType.Desert => Color.Khaki,
                    BiomeType.Forest => Color.ForestGreen,
                    BiomeType.Mountains => Color.SlateGray,
                    BiomeType.Plains => Color.LightGreen,
                    BiomeType.Tundra => Color.AliceBlue,
                    _ => Color.Gray
                };
            }
        }
        return bestColor;
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
}