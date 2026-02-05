using System;
using System.Collections.Generic;

namespace VoxelEngine_Silk.Net_1._0.World;

public class Chunk
{
    public const int Size = 16;
    public byte[,,] Blocks = new byte[Size, Size, Size];

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
        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                // 1. Convert local chunk coords to global world coords
                float worldX = (ChunkX * Size) + x;
                float worldZ = (ChunkZ * Size) + z;

                // 2. Get climate values (returns -1 to 1, we normalize to 0 to 1)
                float temp = (world.TempNoise.GetNoise(worldX, worldZ) + 1f) / 2f;
                float humidity = (world.HumidityNoise.GetNoise(worldX, worldZ) + 1f) / 2f;

                // 3. Determine Biome and get its specific settings
                var biomeType = BiomeManager.DetermineBiome(temp, humidity);
                var settings = BiomeManager.GetSettings(biomeType);

                // 4. Calculate height based on Biome parameters
                // We set the frequency of the height noise dynamically based on the biome!
                world.HeightNoise.SetFrequency(settings.Frequency);
                float noiseHeight = world.HeightNoise.GetNoise(worldX, worldZ); // -1 to 1

                // Map the noise to our biome's height and variation
                int finalHeight = (int)(settings.BaseHeight + (noiseHeight * settings.Variation));

                // 5. Fill the blocks
                for (int y = 0; y < Size; y++)
                {
                    if (y <= finalHeight)
                    {
                        Blocks[x, y, z] = 1; // For now, just "solid"
                    }
                    else
                    {
                        Blocks[x, y, z] = 0; // Air
                    }
                }
            }
        }
    }

    public float[] GetVertexData(Chunk right, Chunk left, Chunk front, Chunk back)
    {
        // Pre-size the list. With greedy meshing, we need way less space!
        List<float> vertices = new List<float>(4096);
        bool[,] processedTop = new bool[Size, Size];

        // 1. GREEDY TOP FACES (The most important for FPS)
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                int y = GetSurfaceHeight(x, z);
                if (y < 0 || processedTop[x, z]) continue;

                // Only mesh if there is air above it
                if (!IsAir(x, y + 1, z, right, left, front, back)) continue;

                // Find how far we can extend this face in the X direction
                int width = 1;
                while (x + width < Size && !processedTop[x + width, z] &&
                       GetSurfaceHeight(x + width, z) == y &&
                       IsAir(x + width, y + 1, z, right, left, front, back))
                {
                    width++;
                }

                // Find how far we can extend this face in the Z direction
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

                // Add ONE large quad for the entire area
                AddGreedyFace(vertices, x, y + 0.5f, z, width, depth, true);

                // Mark all merged blocks as processed
                for (int dz = 0; dz < depth; dz++)
                    for (int dx = 0; dx < width; dx++)
                        processedTop[x + dx, z + dz] = true;
            }
        }

        // 2. SIDES AND BOTTOM (Standard mesh for now)
        // We iterate again to catch the sides of the blocks
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int z = 0; z < Size; z++)
                {
                    if (Blocks[x, y, z] == 0) continue;

                    // We skip 'up' because we did it greedily above
                    bool down = IsAir(x, y - 1, z, right, left, front, back);
                    bool leftF = IsAir(x - 1, y, z, right, left, front, back);
                    bool rightF = IsAir(x + 1, y, z, right, left, front, back);
                    bool frontF = IsAir(x, y, z + 1, right, left, front, back);
                    bool backF = IsAir(x, y, z - 1, right, left, front, back);

                    if (down || leftF || rightF || frontF || backF)
                    {
                        AddCubeSides(vertices, x, y, z, down, leftF, rightF, frontF, backF);
                    }
                }
            }
        }

        return vertices.ToArray();
    }

    // Helper to find the top block at a coordinate
    private int GetSurfaceHeight(int x, int z)
    {
        for (int y = Size - 1; y >= 0; y--)
        {
            if (Blocks[x, y, z] != 0) return y;
        }
        return -1;
    }

    // Helper to add a large rectangular face
    private void AddGreedyFace(List<float> v, float x, float y, float z, int w, int d, bool up)
    {
        const float r = 0.5f; const float g = 0.5f; const float b = 0.5f;

        // Corners of the large rectangle
        float xMin = x - 0.5f;
        float xMax = x + w - 0.5f;
        float zMin = z - 0.5f;
        float zMax = z + d - 0.5f;

        // Triangle 1
        AddVertex(v, xMin, y, zMin, r, g, b);
        AddVertex(v, xMax, y, zMin, r, g, b);
        AddVertex(v, xMax, y, zMax, r, g, b);
        // Triangle 2
        AddVertex(v, xMax, y, zMax, r, g, b);
        AddVertex(v, xMin, y, zMax, r, g, b);
        AddVertex(v, xMin, y, zMin, r, g, b);
    }

    private void AddCubeSides(List<float> v, float x, float y, float z, bool down, bool left, bool right, bool front, bool back)
    {
        const float r = 0.45f; const float g = 0.45f; const float b = 0.45f; // Slightly darker sides

        if (down) AddFace(v, x - 0.5f, y - 0.5f, z + 0.5f, x + 0.5f, y - 0.5f, z + 0.5f, x + 0.5f, y - 0.5f, z - 0.5f, x - 0.5f, y - 0.5f, z - 0.5f, r, g, b);
        if (left) AddFace(v, x - 0.5f, y + 0.5f, z + 0.5f, x - 0.5f, y + 0.5f, z - 0.5f, x - 0.5f, y - 0.5f, z - 0.5f, x - 0.5f, y - 0.5f, z + 0.5f, r, g, b);
        if (right) AddFace(v, x + 0.5f, y + 0.5f, z - 0.5f, x + 0.5f, y + 0.5f, z + 0.5f, x + 0.5f, y - 0.5f, z + 0.5f, x + 0.5f, y - 0.5f, z - 0.5f, r, g, b);
        if (front) AddFace(v, x - 0.5f, y + 0.5f, z + 0.5f, x + 0.5f, y + 0.5f, z + 0.5f, x + 0.5f, y - 0.5f, z + 0.5f, x - 0.5f, y - 0.5f, z + 0.5f, r, g, b);
        if (back) AddFace(v, x + 0.5f, y + 0.5f, z - 0.5f, x - 0.5f, y + 0.5f, z - 0.5f, x - 0.5f, y - 0.5f, z - 0.5f, x + 0.5f, y - 0.5f, z - 0.5f, r, g, b);
    }

    private bool IsAir(int x, int y, int z, Chunk right, Chunk left, Chunk front, Chunk back)
    {
        if (y < 0 || y >= Size) return true;

        if (x >= 0 && x < Size && z >= 0 && z < Size)
            return Blocks[x, y, z] == 0;

        if (x >= Size) return right == null || right.Blocks[0, y, z] == 0;
        if (x < 0) return left == null || left.Blocks[Size - 1, y, z] == 0;
        if (z >= Size) return front == null || front.Blocks[x, y, 0] == 0;
        if (z < 0) return back == null || back.Blocks[x, y, Size - 1] == 0;

        return true;
    }

    // Optimization 2: Manual vertex feeding (ZERO temporary array allocations)
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
        // Triangle 1
        AddVertex(v, x1, y1, z1, r, g, b);
        AddVertex(v, x2, y2, z2, r, g, b);
        AddVertex(v, x3, y3, z3, r, g, b);
        // Triangle 2
        AddVertex(v, x3, y3, z3, r, g, b);
        AddVertex(v, x4, y4, z4, r, g, b);
        AddVertex(v, x1, y1, z1, r, g, b);
    }

    private void AddCube(List<float> v, float x, float y, float z,
                         bool up, bool down, bool left, bool right, bool front, bool back)
    {
        const float r = 0.5f; const float g = 0.5f; const float b = 0.5f;

        // Using 0.5f offsets so cubes are 1x1x1 and centered on their coordinate
        if (up) AddFace(v, x - 0.5f, y + 0.5f, z - 0.5f, x + 0.5f, y + 0.5f, z - 0.5f, x + 0.5f, y + 0.5f, z + 0.5f, x - 0.5f, y + 0.5f, z + 0.5f, r, g, b);
        if (down) AddFace(v, x - 0.5f, y - 0.5f, z + 0.5f, x + 0.5f, y - 0.5f, z + 0.5f, x + 0.5f, y - 0.5f, z - 0.5f, x - 0.5f, y - 0.5f, z - 0.5f, r, g, b);
        if (left) AddFace(v, x - 0.5f, y + 0.5f, z + 0.5f, x - 0.5f, y + 0.5f, z - 0.5f, x - 0.5f, y - 0.5f, z - 0.5f, x - 0.5f, y - 0.5f, z + 0.5f, r, g, b);
        if (right) AddFace(v, x + 0.5f, y + 0.5f, z - 0.5f, x + 0.5f, y + 0.5f, z + 0.5f, x + 0.5f, y - 0.5f, z + 0.5f, x + 0.5f, y - 0.5f, z - 0.5f, r, g, b);
        if (front) AddFace(v, x - 0.5f, y + 0.5f, z + 0.5f, x + 0.5f, y + 0.5f, z + 0.5f, x + 0.5f, y - 0.5f, z + 0.5f, x - 0.5f, y - 0.5f, z + 0.5f, r, g, b);
        if (back) AddFace(v, x + 0.5f, y + 0.5f, z - 0.5f, x - 0.5f, y + 0.5f, z - 0.5f, x - 0.5f, y - 0.5f, z - 0.5f, x + 0.5f, y - 0.5f, z - 0.5f, r, g, b);
    }
}