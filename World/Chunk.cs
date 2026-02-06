using System;
using System.Collections.Generic;
using System.Numerics;

namespace VoxelEngine_Silk.Net_1._0.World;

public class Chunk
{
    public const int Size = 16;
    public const int Height = 256; // Added this
    public byte[,,] Blocks = new byte[Size, Height, Size]; // Updated array
    public bool IsDirty { get; set; } = true;

    // Handling Max Height so we're not wasing resources:
    private int[,] _heightMap = new int[Size, Size];
    private int _highestPoint = 0;

    public int ChunkX { get; private set; }
    public int ChunkZ { get; private set; }
    private VoxelWorld _world;

    public Chunk(int chunkX, int chunkZ, VoxelWorld world)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        _world = world;
        GenerateTerrain(world);
    }

    private void GenerateTerrain(VoxelWorld world)
    {
        _highestPoint = 0;

        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                float worldX = (ChunkX * Size) + x;
                float worldZ = (ChunkZ * Size) + z;

                // 1. MACRO & CLIMATE
                float macroNoise = world.HeightNoise.GetNoise(worldX * 0.005f, worldZ * 0.005f);
                float continentalOffset = macroNoise * 40f;

                // Sampling climate per-block as per your original logic
                float temp = (world.TempNoise.GetNoise(worldX * 0.5f, worldZ * 0.5f) + 1f) / 2f;
                float humidity = (world.HumidityNoise.GetNoise(worldX * 0.5f, worldZ * 0.5f) + 1f) / 2f;

                // 2. BASE BIOME HEIGHT
                float biomeHeight = GetWeightedHeight(world, worldX, worldZ, temp, humidity);
                float finalHeightFloat = biomeHeight + continentalOffset;

                // 3. RIVER CARVING
                float riverSample = MathF.Abs(world.RiverNoise.GetNoise(worldX, worldZ));
                float riverThreshold = 0.02f;

                if (riverSample < riverThreshold)
                {
                    float riverCurve = 1.0f - (riverSample / riverThreshold);
                    finalHeightFloat -= (riverCurve * 15f);
                }

                int finalHeight = (int)MathF.Round(finalHeightFloat);
                finalHeight = Math.Clamp(finalHeight, 0, Height - 1);

                _heightMap[x, z] = finalHeight;
                if (finalHeight > _highestPoint) _highestPoint = finalHeight;

                // 4. BLOCK FILLING
                for (int y = 0; y < Height; y++)
                {
                    if (y <= finalHeight)
                    {
                        Blocks[x, y, z] = 1; // Solid Ground
                    }
                    // Referencing the centralized public constant from BiomeManager
                    else if (y <= (int)BiomeManager.SEA_LEVEL)
                    {
                        Blocks[x, y, z] = 2; // Water
                    }
                    else
                    {
                        Blocks[x, y, z] = 0; // Air
                    }
                }
            }
        }
        this.IsDirty = true;
    }
    private float GetWeightedHeight(VoxelWorld world, float wx, float wz, float t, float h)
    {
        // Sample all biomes and blend them based on climate influence
        BiomeType[] types = (BiomeType[])Enum.GetValues(typeof(BiomeType));
        float totalWeight = 0;
        float weightedHeight = 0;

        foreach (var type in types)
        {
            float influence = CalculateBiomeInfluence(type, t, h);
            if (influence <= 0) continue;

            var settings = BiomeManager.GetSettings(type);
            float weight = influence * influence; // Smoother transitions

            // Sample noise at the biome's specific frequency
            float noise = world.HeightNoise.GetNoise(wx * (settings.Frequency * 100f), wz * (settings.Frequency * 100f));
            float hSample = settings.BaseHeight + (noise * settings.Variation);

            weightedHeight += hSample * weight;
            totalWeight += weight;
        }

        return (totalWeight > 0) ? (weightedHeight / totalWeight) : 64f;
    }

    private float CalculateBiomeInfluence(BiomeType type, float t, float h)
    {
        // Maps climate distance to a 0-1 weight
        float targetT = 0.5f, targetH = 0.5f;
        switch (type)
        {
            case BiomeType.Desert: targetT = 0.8f; targetH = 0.2f; break;
            case BiomeType.Forest: targetT = 0.7f; targetH = 0.7f; break;
            case BiomeType.Mountains: targetT = 0.2f; targetH = 0.5f; break;
            case BiomeType.Plains: targetT = 0.5f; targetH = 0.4f; break;
            case BiomeType.Tundra: targetT = 0.1f; targetH = 0.2f; break;
        }

        float dist = MathF.Sqrt(MathF.Pow(t - targetT, 2) + MathF.Pow(h - targetH, 2));
        return Math.Max(0, 1.0f - (dist * 2.5f)); // 2.5f defines the blend 'softness'
    }

    public int FillVertexData(List<float> buffer)
    {
        buffer.Clear();
        var neighbors = _world.GetNeighbors(ChunkX, ChunkZ);

        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                int columnHeight = _heightMap[x, z];
                int maxY = Math.Max(columnHeight, (int)BiomeManager.SEA_LEVEL);

                for (int y = 0; y <= maxY; y++)
                {
                    byte blockType = Blocks[x, y, z];
                    if (blockType == 0) continue;

                    bool up = IsAir(x, y + 1, z, neighbors.r, neighbors.l, neighbors.f, neighbors.b);
                    bool down = IsAir(x, y - 1, z, neighbors.r, neighbors.l, neighbors.f, neighbors.b);
                    bool left = IsAir(x - 1, y, z, neighbors.r, neighbors.l, neighbors.f, neighbors.b);
                    bool right = IsAir(x + 1, y, z, neighbors.r, neighbors.l, neighbors.f, neighbors.b);
                    bool front = IsAir(x, y, z + 1, neighbors.r, neighbors.l, neighbors.f, neighbors.b);
                    bool back = IsAir(x, y, z - 1, neighbors.r, neighbors.l, neighbors.f, neighbors.b);

                    if (!up && !down && !left && !right && !front && !back) continue;

                    // 1. TOP FACE (Using Greedy helper)
                    if (up) AddGreedyFace(buffer, x, y, z, 1, 1, true, blockType);

                    // 2. BOTTOM FACE (Simple Quad for now)
                    if (down) AddBottomFace(buffer, x, y, z, blockType);

                    // 3. SIDE FACES (Using Vertical Greedy helpers)
                    if (left) AddVerticalGreedySide(buffer, x, y, z, 1, 0, blockType);
                    if (right) AddVerticalGreedySide(buffer, x, y, z, 1, 1, blockType);
                    if (front) AddVerticalGreedySide(buffer, x, y, z, 1, 2, blockType);
                    if (back) AddVerticalGreedySide(buffer, x, y, z, 1, 3, blockType);
                }
            }
        }
        return buffer.Count;
    }

    private void AddGreedyFace(List<float> v, int x, int y, int z, int width, int depth, bool isTop, byte type)
    {
        Vector3 color = type == 2 ? new Vector3(0.2f, 0.4f, 0.8f) : new Vector3(0.5f, 0.5f, 0.5f);
        if (!isTop) color *= 0.3f;

        float yPos = isTop ? y + 1 : y;

        // Define the 4 corners of the greedy quad
        Vector3 p1 = new Vector3(x, yPos, z);
        Vector3 p2 = new Vector3(x + width, yPos, z);
        Vector3 p3 = new Vector3(x + width, yPos, z + depth);
        Vector3 p4 = new Vector3(x, yPos, z + depth);

        AddQuad(v, p1, p2, p3, p4, color);
    }

    private void AddVerticalGreedySide(List<float> v, int x, int y, int z, int height, int side, byte type)
    {
        Vector3 color = type == 2 ? new Vector3(0.2f, 0.35f, 0.7f) : new Vector3(0.4f, 0.4f, 0.4f);

        Vector3 p1, p2, p3, p4;

        switch (side)
        {
            case 0: // Left (-X)
                p1 = new Vector3(x, y, z); p2 = new Vector3(x, y, z + 1);
                p3 = new Vector3(x, y + height, z + 1); p4 = new Vector3(x, y + height, z);
                break;
            case 1: // Right (+X)
                p1 = new Vector3(x + 1, y, z + 1); p2 = new Vector3(x + 1, y, z);
                p3 = new Vector3(x + 1, y + height, z); p4 = new Vector3(x + 1, y + height, z + 1);
                break;
            case 2: // Front (+Z)
                p1 = new Vector3(x, y, z + 1); p2 = new Vector3(x + 1, y, z + 1);
                p3 = new Vector3(x + 1, y + height, z + 1); p4 = new Vector3(x, y + height, z + 1);
                break;
            default: // Back (-Z)
                p1 = new Vector3(x + 1, y, z); p2 = new Vector3(x, y, z);
                p3 = new Vector3(x, y + height, z); p4 = new Vector3(x + 1, y + height, z);
                break;
        }
        AddQuad(v, p1, p2, p3, p4, color);
    }

    private void AddBottomFace(List<float> v, int x, int y, int z, byte type)
    {
        Vector3 color = (type == 2 ? new Vector3(0.2f, 0.4f, 0.8f) : new Vector3(0.5f, 0.5f, 0.5f)) * 0.3f;
        AddQuad(v, new Vector3(x, y, z + 1), new Vector3(x + 1, y, z + 1), new Vector3(x + 1, y, z), new Vector3(x, y, z), color);
    }

    private bool IsAir(int x, int y, int z, Chunk right, Chunk left, Chunk front, Chunk back)
    {
        if (y < 0 || y >= Height) return true;

        if (x >= 0 && x < Size && z >= 0 && z < Size)
            return Blocks[x, y, z] == 0;

        if (x >= Size) return right == null || right.Blocks[0, y, z] == 0;
        if (x < 0) return left == null || left.Blocks[Size - 1, y, z] == 0;
        if (z >= Size) return front == null || front.Blocks[x, y, 0] == 0;
        if (z < 0) return back == null || back.Blocks[x, y, Size - 1] == 0;

        return true;
    }

    // Inside Chunk.cs
    private void AddVertex(List<float> v, Vector3 pos, Vector3 color, Vector2 uv)
    {
        // Position
        v.Add(pos.X); v.Add(pos.Y); v.Add(pos.Z);
        // Color
        v.Add(color.X); v.Add(color.Y); v.Add(color.Z);
        // UVs (New! This prepares you for texturing)
        v.Add(uv.X); v.Add(uv.Y);
    }

    private void AddQuad(List<float> v, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 c4, Vector3 color, int w = 1, int h = 1)
    {
        // Triangle 1
        AddVertex(v, c1, color, new Vector2(0, 0));
        AddVertex(v, c2, color, new Vector2(w, 0));
        AddVertex(v, c3, color, new Vector2(w, h));
        // Triangle 2
        AddVertex(v, c3, color, new Vector2(w, h));
        AddVertex(v, c4, color, new Vector2(0, h));
        AddVertex(v, c1, color, new Vector2(0, 0));
    }
}