using System;
using System.Collections.Generic;

namespace VoxelEngine_Silk.Net_1._0.World;

public class Chunk
{
    public const int Size = 16;
    public const int Height = 256; // Added this
    public byte[,,] Blocks = new byte[Size, Height, Size]; // Updated array


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

        // 1. DATA MAP: 16-block resolution for massive, non-spotty biomes
        const int sampleRes = 16;
        const int mapSize = (Size / sampleRes) + 1;
        var heightSampleMap = new float[mapSize, mapSize];

        // Pre-calculate the heights at the 16x16 grid corners
        for (int mx = 0; mx < mapSize; mx++)
        {
            for (int mz = 0; mz < mapSize; mz++)
            {
                // SCALE FIX: We multiply by a 'ClimateScale' factor (e.g., 0.5f) 
                // to make biomes even larger and less spotty.
                float worldX = (ChunkX * Size) + (mx * sampleRes);
                float worldZ = (ChunkZ * Size) + (mz * sampleRes);

                // Sample climate with a very low frequency for 'Continent' feel
                float temp = (world.TempNoise.GetNoise(worldX * 0.5f, worldZ * 0.5f) + 1f) / 2f;
                float humidity = (world.HumidityNoise.GetNoise(worldX * 0.5f, worldZ * 0.5f) + 1f) / 2f;

                var biome = BiomeManager.DetermineBiome(temp, humidity);
                var settings = BiomeManager.GetSettings(biome);

                world.HeightNoise.SetFrequency(settings.Frequency);
                float noise = world.HeightNoise.GetNoise(worldX, worldZ);

                heightSampleMap[mx, mz] = settings.BaseHeight + (noise * settings.Variation);
            }
        }

        // 2. GENERATION LOOP
        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                float worldX = (ChunkX * Size) + x;
                float worldZ = (ChunkZ * Size) + z;

                float fx = (float)x / sampleRes;
                float fz = (float)z / sampleRes;
                int x0 = (int)Math.Floor(fx);
                int z0 = (int)Math.Floor(fz);
                int x1 = Math.Min(x0 + 1, mapSize - 1);
                int z1 = Math.Min(z0 + 1, mapSize - 1);

                float tx = fx - x0;
                float tz = fz - z0;

                // QUINTIC S-CURVE (The Rolling Hill Secret)
                // 6t^5 - 15t^4 + 10t^3 ensures zero-acceleration at the borders.
                float sx = tx * tx * tx * (tx * (tx * 6 - 15) + 10);
                float sz = tz * tz * tz * (tz * (tz * 6 - 15) + 10);

                // Bilinear Interpolation of pre-calculated heights
                float blendedHeight = Lerp2D(
                    heightSampleMap[x0, z0], heightSampleMap[x1, z0],
                    heightSampleMap[x0, z1], heightSampleMap[x1, z1],
                    sx, sz
                );

                // 3. THE FINAL POLISH: Fractal Layering
                // Adding a second, much smaller noise octave breaks up the "perfect" math
                // and makes the hills look like real earth, not plastic.
                float detail = world.HeightNoise.GetNoise(worldX * 5.0f, worldZ * 5.0f) * 0.3f;

                int finalHeight = (int)MathF.Round(blendedHeight + detail);
                finalHeight = Math.Clamp(finalHeight, 0, Height - 1);

                _heightMap[x, z] = finalHeight;
                if (finalHeight > _highestPoint) _highestPoint = finalHeight;

                for (int y = 0; y <= finalHeight; y++)
                {
                    Blocks[x, y, z] = 1;
                }
            }
        }
    }

    // Utility math for smooth transitions
    private float Lerp(float a, float b, float t) => a + (b - a) * t;

    private float Lerp2D(float c00, float c10, float c01, float c11, float tx, float tz)
    {
        float r1 = Lerp(c00, c10, tx);
        float r2 = Lerp(c01, c11, tx);
        return Lerp(r1, r2, tz);
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

                AddGreedyFace(vertices, x, y + 0.5f, z, width, depth, true);

                for (int dz = 0; dz < depth; dz++)
                    for (int dx = 0; dx < width; dx++)
                        processedTop[x + dx, z + dz] = true;
            }
        }

        // 2. SIDES AND BOTTOM (Optimized Vertical Loop)
        // 2. SIDES (Greedy Vertical Meshing)
        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                int columnTop = GetSurfaceHeight(x, z);
                if (columnTop < 0) continue;

                // We process each of the 4 side directions separately to find vertical strips
                // 0: Left, 1: Right, 2: Front, 3: Back
                for (int side = 0; side < 4; side++)
                {
                    for (int y = 0; y <= columnTop; y++)
                    {
                        if (Blocks[x, y, z] == 0) continue;

                        // 1. Determine which neighbor to check based on the side
                        int nx = x, nz = z;
                        if (side == 0) nx--;
                        else if (side == 1) nx++;
                        else if (side == 2) nz++; else if (side == 3) nz--;

                        // 2. Only start a greedy strip if this face is visible (IsAir)
                        if (IsAir(nx, y, nz, right, left, front, back))
                        {
                            int startY = y;
                            int height = 1;

                            // 3. Look UP to see how many identical faces we can merge vertically
                            while (y + 1 <= columnTop &&
                                   Blocks[x, y + 1, z] != 0 &&
                                   IsAir(nx, y + 1, nz, right, left, front, back))
                            {
                                height++;
                                y++; // Skip these blocks in the main 'y' loop
                            }

                            // 4. Add ONE tall quad for the entire vertical strip
                            AddVerticalGreedySide(vertices, x, startY, z, height, side);
                        }
                    }
                }

                // OPTIMIZATION: Removed the 'down' face check entirely. 
                // This stops rendering the underside of the world.
            }
        }
        return vertices.ToArray();
    }

    private void AddVerticalGreedySide(List<float> v, float x, float y, float z, int h, int side)
    {
        const float r = 0.45f; const float g = 0.45f; const float b = 0.45f;
        float yMin = y - 0.5f;
        float yMax = y + h - 0.5f;
        float xOff = x, zOff = z;

        // Adjust coordinates based on which side we are drawing
        if (side == 0) // Left (-X)
            AddFace(v, xOff - 0.5f, yMax, zOff + 0.5f, xOff - 0.5f, yMax, zOff - 0.5f, xOff - 0.5f, yMin, zOff - 0.5f, xOff - 0.5f, yMin, zOff + 0.5f, r, g, b);
        else if (side == 1) // Right (+X)
            AddFace(v, xOff + 0.5f, yMax, zOff - 0.5f, xOff + 0.5f, yMax, zOff + 0.5f, xOff + 0.5f, yMin, zOff + 0.5f, xOff + 0.5f, yMin, zOff - 0.5f, r, g, b);
        else if (side == 2) // Front (+Z)
            AddFace(v, xOff - 0.5f, yMax, zOff + 0.5f, xOff + 0.5f, yMax, zOff + 0.5f, xOff + 0.5f, yMin, zOff + 0.5f, xOff - 0.5f, yMin, zOff + 0.5f, r, g, b);
        else if (side == 3) // Back (-Z)
            AddFace(v, xOff + 0.5f, yMax, zOff - 0.5f, xOff - 0.5f, yMax, zOff - 0.5f, xOff - 0.5f, yMin, zOff - 0.5f, xOff + 0.5f, yMin, zOff - 0.5f, r, g, b);
    }

    // Helper to find the top block at a coordinate
    private int GetSurfaceHeight(int x, int z)
    {
        // Search from the very top of the 256-height world downwards
        for (int y = Height - 1; y >= 0; y--)
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
        // 1. UPDATED: Check against Height (256) instead of Size (16)
        // This prevents mountains from being culled incorrectly at the old ceiling
        if (y < 0 || y >= Height) return true;

        // 2. Local check (Internal to this chunk)
        if (x >= 0 && x < Size && z >= 0 && z < Size)
            return Blocks[x, y, z] == 0;

        // 3. Neighbor checks (Horizontal boundaries)
        // Note: We still use 'Size' here because the chunks are still 16x16 wide.
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