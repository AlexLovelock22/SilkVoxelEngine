using System;
using System.Collections.Generic;

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
        const int SEA_LEVEL = 64; // Match your BiomeManager sea level

        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                float worldX = (ChunkX * Size) + x;
                float worldZ = (ChunkZ * Size) + z;

                // 1. MACRO & CLIMATE
                float macroNoise = world.HeightNoise.GetNoise(worldX * 0.005f, worldZ * 0.005f);
                float continentalOffset = macroNoise * 40f;

                float temp = (world.TempNoise.GetNoise(worldX * 0.5f, worldZ * 0.5f) + 1f) / 2f;
                float humidity = (world.HumidityNoise.GetNoise(worldX * 0.5f, worldZ * 0.5f) + 1f) / 2f;

                // 2. BASE BIOME HEIGHT
                float biomeHeight = GetWeightedHeight(world, worldX, worldZ, temp, humidity);
                float finalHeightFloat = biomeHeight + continentalOffset;

                // 3. RIVER CARVING (New!)
                // We take the absolute value of Perlin noise to get "ridged" lines
                float riverSample = MathF.Abs(world.RiverNoise.GetNoise(worldX, worldZ));
                float riverThreshold = 0.02f; // Smaller = narrower rivers

                if (riverSample < riverThreshold)
                {
                    // Calculate how deep to carve based on how close we are to the river center
                    float riverCurve = 1.0f - (riverSample / riverThreshold);
                    // Push the ground down (up to 15 blocks deep)
                    finalHeightFloat -= (riverCurve * 15f);
                }

                int finalHeight = (int)MathF.Round(finalHeightFloat);
                finalHeight = Math.Clamp(finalHeight, 0, Height - 1);

                _heightMap[x, z] = finalHeight;
                if (finalHeight > _highestPoint) _highestPoint = finalHeight;

                // 4. BLOCK FILLING (Updated for Water)
                for (int y = 0; y < Height; y++)
                {
                    if (y <= finalHeight)
                    {
                        Blocks[x, y, z] = 1; // Solid Ground (Stone/Dirt)
                    }
                    else if (y <= SEA_LEVEL)
                    {
                        Blocks[x, y, z] = 2; // WATER BLOCK
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

    public float[] GetVertexData(Chunk right, Chunk left, Chunk front, Chunk back)
    {
        // Pre-size the list higher since we have more potential vertical faces
        List<float> vertices = new List<float>(8192);
        bool[,] processedTop = new bool[Size, Size];

        // 1. GREEDY TOP FACES
        // This logic stays efficient because it only cares about the surface 'y'
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (processedTop[x, z]) continue;

                int y = GetSurfaceHeight(x, z);
                if (y < 0) continue;

                // Only mesh if there is air above it (using Height check inside IsAir)
                if (!IsAir(x, y + 1, z, right, left, front, back)) continue;

                int width = 1;
                while (x + width < Size && !processedTop[x + width, z] &&
                       GetSurfaceHeight(x + width, z) == y &&
                       IsAir(x + width, y + 1, z, right, left, front, back))
                {
                    width++;
                }

                int depth = 1;
                bool canExtendZ = true;
                while (z + depth < Size && canExtendZ)
                {
                    for (int wx = 0; wx < width; wx++)
                    {
                        if (processedTop[x + wx, z + depth] ||
                            GetSurfaceHeight(x + wx, z + depth) != y ||
                            !IsAir(x + wx, y + 1, z + depth, right, left, front, back))
                        {
                            canExtendZ = false;
                            break;
                        }
                    }
                    if (canExtendZ) depth++;
                }

                AddGreedyFace(vertices, x, y, z, width, depth, true);

                for (int dz = 0; dz < depth; dz++)
                    for (int dx = 0; dx < width; dx++)
                        processedTop[x + dx, z + dz] = true;
            }
        }

        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                int columnTop = GetSurfaceHeight(x, z);
                if (columnTop < 0) continue;

                for (int side = 0; side < 4; side++)
                {
                    for (int y = 0; y <= columnTop; y++)
                    {
                        if (Blocks[x, y, z] == 0) continue;

                        int nx = x, nz = z;
                        if (side == 0) nx--;
                        else if (side == 1) nx++;
                        else if (side == 2) nz++; else if (side == 3) nz--;

                        if (IsAir(nx, y, nz, right, left, front, back))
                        {
                            int startY = y;
                            int height = 1;

                            while (y + 1 <= columnTop &&
                                   Blocks[x, y + 1, z] != 0 &&
                                   IsAir(nx, y + 1, nz, right, left, front, back))
                            {
                                height++;
                                y++;
                            }

                            AddVerticalGreedySide(vertices, x, startY, z, height, side);
                        }
                    }
                }
            }
        }
        return vertices.ToArray();
    }

    private void AddVerticalGreedySide(List<float> v, float x, float y, float z, int h, int side)
    {
        const float r = 0.45f; const float g = 0.45f; const float b = 0.45f;

        float yMin = y;
        float yMax = y + h;

        if (side == 0)
            AddFace(v, x, yMax, z + 1, x, yMax, z, x, yMin, z, x, yMin, z + 1, r, g, b);
        else if (side == 1)
            AddFace(v, x + 1, yMax, z, x + 1, yMax, z + 1, x + 1, yMin, z + 1, x + 1, yMin, z, r, g, b);
        else if (side == 2)
            AddFace(v, x, yMax, z + 1, x + 1, yMax, z + 1, x + 1, yMin, z + 1, x, yMin, z + 1, r, g, b);
        else if (side == 3)
            AddFace(v, x + 1, yMax, z, x, yMax, z, x, yMin, z, x + 1, yMin, z, r, g, b);
    }

    private void AddGreedyFace(List<float> v, float x, float y, float z, int w, int d, bool up)
    {
        const float r = 0.5f; const float g = 0.5f; const float b = 0.5f;

        float xMin = x;
        float xMax = x + w;
        float zMin = z;
        float zMax = z + d;

        float yTop = y + 1.0f;

        AddVertex(v, xMin, yTop, zMin, r, g, b);
        AddVertex(v, xMax, yTop, zMin, r, g, b);
        AddVertex(v, xMax, yTop, zMax, r, g, b);

        AddVertex(v, xMax, yTop, zMax, r, g, b);
        AddVertex(v, xMin, yTop, zMax, r, g, b);
        AddVertex(v, xMin, yTop, zMin, r, g, b);
    }

    private int GetSurfaceHeight(int x, int z)
    {
        for (int y = Height - 1; y >= 0; y--)
        {
            if (Blocks[x, y, z] != 0) return y;
        }
        return -1;
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

    private void AddVertex(List<float> v, float x, float y, float z, float r, float g, float b)
    {
        v.Add(x); v.Add(y); v.Add(z);
        v.Add(r); v.Add(g); v.Add(b);
    }

    private void AddFace(List<float> v,
        float x1, float y1, float z1,
        float x2, float y2, float z2,
        float x3, float y3, float z3,
        float x4, float y4, float z4,
        float r, float g, float b)
    {
  
        AddVertex(v, x1, y1, z1, r, g, b);
        AddVertex(v, x2, y2, z2, r, g, b);
        AddVertex(v, x3, y3, z3, r, g, b);

        AddVertex(v, x3, y3, z3, r, g, b);
        AddVertex(v, x4, y4, z4, r, g, b);
        AddVertex(v, x1, y1, z1, r, g, b);
    }
}