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

                // 4. BLOCK FILLING (The "Painting" Logic)
                for (int y = 0; y < Height; y++)
                {
                    if (y > finalHeight)
                    {
                        // Above surface: Air or Water
                        Blocks[x, y, z] = y <= (int)BiomeManager.SEA_LEVEL ? (byte)BlockType.Water : (byte)BlockType.Air;
                    }
                    else if (y == finalHeight)
                    {
                        // THE SURFACE LAYER
                        if (y <= (int)BiomeManager.SEA_LEVEL - 1)
                            Blocks[x, y, z] = (byte)BlockType.Mud; // Underwater riverbeds
                        else
                            Blocks[x, y, z] = (byte)BlockType.Grass; // Dry land
                    }
                    else if (y > finalHeight - 4)
                    {
                        // SUB-SURFACE (3 blocks of dirt under the grass)
                        Blocks[x, y, z] = (byte)BlockType.Dirt;
                    }
                    else
                    {
                        // THE DEEP FOUNDATION
                        Blocks[x, y, z] = (byte)BlockType.Stone;
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



    private void AddGreedyFace(List<float> v, int x, int y, int z, int w, int d, bool isTop, byte type)
    {
        var face = isTop ? "top" : "bottom";
        var data = GetFaceData(type, face, w, d);
        Vector3 normal = isTop ? new Vector3(0, 1, 0) : new Vector3(0, -1, 0);

        if (isTop)
        {
            AddVertex(v, new Vector3(x, y + 1, z), data.color, new Vector2(data.min.X, data.min.Y), normal);
            AddVertex(v, new Vector3(x, y + 1, z + d), data.color, new Vector2(data.min.X, data.max.Y), normal);
            AddVertex(v, new Vector3(x + w, y + 1, z + d), data.color, new Vector2(data.max.X, data.max.Y), normal);

            AddVertex(v, new Vector3(x, y + 1, z), data.color, new Vector2(data.min.X, data.min.Y), normal);
            AddVertex(v, new Vector3(x + w, y + 1, z + d), data.color, new Vector2(data.max.X, data.max.Y), normal);
            AddVertex(v, new Vector3(x + w, y + 1, z), data.color, new Vector2(data.max.X, data.min.Y), normal);
        }
        else
        {
            // Bottom faces use the same logic but with normal (0, -1, 0)
            AddVertex(v, new Vector3(x, y, z), data.color, new Vector2(data.min.X, data.min.Y), normal);
            AddVertex(v, new Vector3(x + w, y, z + d), data.color, new Vector2(data.max.X, data.max.Y), normal);
            AddVertex(v, new Vector3(x, y, z + d), data.color, new Vector2(data.min.X, data.max.Y), normal);

            AddVertex(v, new Vector3(x, y, z), data.color, new Vector2(data.min.X, data.min.Y), normal);
            AddVertex(v, new Vector3(x + w, y, z), data.color, new Vector2(data.max.X, data.min.Y), normal);
            AddVertex(v, new Vector3(x + w, y, z + d), data.color, new Vector2(data.max.X, data.max.Y), normal);
        }
    }

    // 3. Update AddVerticalGreedySide for Side normals
    private void AddVerticalGreedySide(List<float> v, int x, int y, int z, int height, int side, byte type)
    {
        var data = GetFaceData(type, "side", 1, height);
        Vector3 p1, p2, p3, p4;
        Vector3 normal;

        switch (side)
        {
            case 0: // Left (-X)
                p1 = new Vector3(x, y, z); p2 = new Vector3(x, y, z + 1);
                p3 = new Vector3(x, y + height, z + 1); p4 = new Vector3(x, y + height, z);
                normal = new Vector3(-1, 0, 0);
                break;
            case 1: // Right (+X)
                p1 = new Vector3(x + 1, y, z + 1); p2 = new Vector3(x + 1, y, z);
                p3 = new Vector3(x + 1, y + height, z); p4 = new Vector3(x + 1, y + height, z + 1);
                normal = new Vector3(1, 0, 0);
                break;
            case 2: // Front (+Z)
                p1 = new Vector3(x, y, z + 1); p2 = new Vector3(x + 1, y, z + 1);
                p3 = new Vector3(x + 1, y + height, z + 1); p4 = new Vector3(x, y + height, z + 1);
                normal = new Vector3(0, 0, 1);
                break;
            default: // Back (-Z)
                p1 = new Vector3(x + 1, y, z); p2 = new Vector3(x, y, z);
                p3 = new Vector3(x, y + height, z); p4 = new Vector3(x + 1, y + height, z);
                normal = new Vector3(0, 0, -1);
                break;
        }

        AddVertex(v, p1, data.color, new Vector2(data.min.X, data.min.Y), normal);
        AddVertex(v, p2, data.color, new Vector2(data.max.X, data.min.Y), normal);
        AddVertex(v, p3, data.color, new Vector2(data.max.X, data.max.Y), normal);

        AddVertex(v, p1, data.color, new Vector2(data.min.X, data.min.Y), normal);
        AddVertex(v, p3, data.color, new Vector2(data.max.X, data.max.Y), normal);
        AddVertex(v, p4, data.color, new Vector2(data.min.X, data.max.Y), normal);
    }

    private void AddBottomFace(List<float> v, int x, int y, int z, byte type)
    {
        var data = GetFaceData(type, "bottom", 1, 1);
        Vector3 normal = new Vector3(0, -1, 0);

        // Triangle 1
        AddVertex(v, new Vector3(x, y, z), data.color, new Vector2(data.min.X, data.min.Y), normal);
        AddVertex(v, new Vector3(x + 1, y, z + 1), data.color, new Vector2(data.max.X, data.max.Y), normal);
        AddVertex(v, new Vector3(x, y, z + 1), data.color, new Vector2(data.min.X, data.max.Y), normal);

        // Triangle 2
        AddVertex(v, new Vector3(x, y, z), data.color, new Vector2(data.min.X, data.min.Y), normal);
        AddVertex(v, new Vector3(x + 1, y, z), data.color, new Vector2(data.max.X, data.min.Y), normal);
        AddVertex(v, new Vector3(x + 1, y, z + 1), data.color, new Vector2(data.max.X, data.max.Y), normal);
    }

    private bool ShouldRenderFace(byte currentType, int nx, int ny, int nz, Chunk? r, Chunk? l, Chunk? f, Chunk? b)
    {
        byte neighborType = GetBlockId(nx, ny, nz, r, l, f, b);

        // If neighbor is Air, always render
        if (neighborType == (byte)BlockType.Air) return true;

        // If I am solid and neighbor is Water, I must render my side
        if (currentType != (byte)BlockType.Water && neighborType == (byte)BlockType.Water) return true;

        // If both are water, cull the face
        if (currentType == (byte)BlockType.Water && neighborType == (byte)BlockType.Water) return false;

        // Default: hide face if neighbor is solid
        return false;
    }

    // Inside Chunk.cs
    // 1. Update AddVertex to include Normal data
    private void AddVertex(List<float> v, Vector3 pos, Vector3 color, Vector2 uv, Vector3 normal)
    {
        // Position (0, 1, 2)
        v.Add(pos.X); v.Add(pos.Y); v.Add(pos.Z);
        // Color (3, 4, 5)
        v.Add(color.X); v.Add(color.Y); v.Add(color.Z);
        // UV (6, 7)
        v.Add(uv.X); v.Add(uv.Y);
        // Normal (8, 9, 10) - New!
        v.Add(normal.X); v.Add(normal.Y); v.Add(normal.Z);
    }

    private (Vector2 min, Vector2 max, Vector3 color) GetFaceData(byte type, string face, int w, int h)
    {
        float atlasWidthTiles = 6f; // Total blocks in your atlas
        float atlasHeightTiles = 1f;

        float uUnit = 1.0f / atlasWidthTiles;
        float vUnit = 1.0f / atlasHeightTiles;

        // Ask the registry which texture index to use for this specific face
        int texIndex = BlockRegistry.GetTexture((BlockType)type, face); // Use the registry!
        float uOffset = texIndex * uUnit;

        return (
            new Vector2(uOffset, 0),
            new Vector2(uOffset + (uUnit * w), vUnit * h),
            Vector3.One // White color (no tinting)
        );
    }

    // Inside Chunk.cs
    public (float[] opaque, float[] water) FillVertexData()
    {
        List<float> opaqueBuffer = new List<float>();
        List<float> waterBuffer = new List<float>();

        var neighbors = _world.GetNeighbors(ChunkX, ChunkZ);
        var r = neighbors.r; var l = neighbors.l; var f = neighbors.f; var b = neighbors.b;

        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                // Scanning the full column to catch layers (Air -> Water -> Mud -> Stone)
                for (int y = 0; y < Height; y++)
                {
                    byte blockType = Blocks[x, y, z];
                    if (blockType == (byte)BlockType.Air) continue;

                    // Culling checks
                    bool up = ShouldRenderFace(blockType, x, y + 1, z, r, l, f, b);
                    bool down = ShouldRenderFace(blockType, x, y - 1, z, r, l, f, b);
                    bool left = ShouldRenderFace(blockType, x - 1, y, z, r, l, f, b);
                    bool right = ShouldRenderFace(blockType, x + 1, y, z, r, l, f, b);
                    bool front = ShouldRenderFace(blockType, x, y, z + 1, r, l, f, b);
                    bool back = ShouldRenderFace(blockType, x, y, z - 1, r, l, f, b);

                    if (!up && !down && !left && !right && !front && !back) continue;

                    if (blockType == (byte)BlockType.Water)
                    {
                        // --- WATER LOGIC ---
                        // 1. Top Surface (Using your special plane with the 1/16th drop and dual-sides)
                        if (up) AddWaterPlane(waterBuffer, x, y, z, 0.0625f, blockType);

                        // 2. Water Sides (Always added to waterBuffer for transparency)
                        if (left) AddVerticalGreedySide(waterBuffer, x, y, z, 1, 0, blockType);
                        if (right) AddVerticalGreedySide(waterBuffer, x, y, z, 1, 1, blockType);
                        if (front) AddVerticalGreedySide(waterBuffer, x, y, z, 1, 2, blockType);
                        if (back) AddVerticalGreedySide(waterBuffer, x, y, z, 1, 3, blockType);

                        if (down) AddBottomFace(waterBuffer, x, y, z, blockType);
                    }
                    else
                    {
                        // --- OPAQUE LOGIC (Grass, Dirt, Stone, Mud) ---
                        if (up) AddGreedyFace(opaqueBuffer, x, y, z, 1, 1, true, blockType);
                        if (down) AddBottomFace(opaqueBuffer, x, y, z, blockType);
                        if (left) AddVerticalGreedySide(opaqueBuffer, x, y, z, 1, 0, blockType);
                        if (right) AddVerticalGreedySide(opaqueBuffer, x, y, z, 1, 1, blockType);
                        if (front) AddVerticalGreedySide(opaqueBuffer, x, y, z, 1, 2, blockType);
                        if (back) AddVerticalGreedySide(opaqueBuffer, x, y, z, 1, 3, blockType);
                    }
                }
            }
        }
        return (opaqueBuffer.ToArray(), waterBuffer.ToArray());
    }

    private byte GetBlockId(int nx, int ny, int nz, Chunk? r, Chunk? l, Chunk? f, Chunk? b)
    {
        if (ny < 0 || ny >= Height) return 0;

        // Inside this chunk
        if (nx >= 0 && nx < Size && nz >= 0 && nz < Size)
            return Blocks[nx, ny, nz];

        // Neighboring chunks
        if (nx >= Size) return r?.Blocks[0, ny, nz] ?? 0;
        if (nx < 0) return l?.Blocks[Size - 1, ny, nz] ?? 0;
        if (nz >= Size) return f?.Blocks[nx, ny, 0] ?? 0;
        if (nz < 0) return b?.Blocks[nx, ny, Size - 1] ?? 0;

        return 0;
    }

    private void AddWaterPlane(List<float> v, int x, int y, int z, float offset, byte type)
    {
        var data = GetFaceData(type, "top", 1, 1);
        float surfaceY = (y + 1) - offset;

        Vector3 normalUp = new Vector3(0, 1, 0);
        Vector3 normalDown = new Vector3(0, -1, 0);

        // --- TOP FACE (Visible from sky) ---
        AddVertex(v, new Vector3(x, surfaceY, z), data.color, new Vector2(data.min.X, data.min.Y), normalUp);
        AddVertex(v, new Vector3(x, surfaceY, z + 1), data.color, new Vector2(data.min.X, data.max.Y), normalUp);
        AddVertex(v, new Vector3(x + 1, surfaceY, z + 1), data.color, new Vector2(data.max.X, data.max.Y), normalUp);

        AddVertex(v, new Vector3(x, surfaceY, z), data.color, new Vector2(data.min.X, data.min.Y), normalUp);
        AddVertex(v, new Vector3(x + 1, surfaceY, z + 1), data.color, new Vector2(data.max.X, data.max.Y), normalUp);
        AddVertex(v, new Vector3(x + 1, surfaceY, z), data.color, new Vector2(data.max.X, data.min.Y), normalUp);

        // --- UNDERSIDE FACE (Visible from underwater) ---
        AddVertex(v, new Vector3(x, surfaceY, z), data.color, new Vector2(data.min.X, data.min.Y), normalDown);
        AddVertex(v, new Vector3(x + 1, surfaceY, z + 1), data.color, new Vector2(data.max.X, data.max.Y), normalDown);
        AddVertex(v, new Vector3(x, surfaceY, z + 1), data.color, new Vector2(data.min.X, data.max.Y), normalDown);

        AddVertex(v, new Vector3(x, surfaceY, z), data.color, new Vector2(data.min.X, data.min.Y), normalDown);
        AddVertex(v, new Vector3(x + 1, surfaceY, z), data.color, new Vector2(data.max.X, data.min.Y), normalDown);
        AddVertex(v, new Vector3(x + 1, surfaceY, z + 1), data.color, new Vector2(data.max.X, data.max.Y), normalDown);
    }
}