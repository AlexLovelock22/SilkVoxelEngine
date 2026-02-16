using System;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp.Processing;

namespace VoxelEngine_Silk.Net_1._0.World;

public class Chunk
{
    public const int Size = 16;
    public const int Height = 256; // Added this
    public byte[,,] Blocks = new byte[Size, Height, Size]; // Updated array
    public bool IsDirty { get; set; } = true;

    private int[,] _heightMap = new int[Size, Size];
    private int _highestPoint = 0;

    public int ChunkX { get; private set; }
    public int ChunkZ { get; private set; }
    private VoxelWorld _world;

    public int[,] GetHeightMap() => _heightMap;



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

                // 1. Get the procedural height from BiomeManager
                float heightSample = BiomeManager.GetHeightAt(world, worldX, worldZ);
                int surfaceY = (int)Math.Clamp(heightSample, 0, Height - 1);

                if (surfaceY > _highestPoint) _highestPoint = surfaceY;
                _heightMap[x, z] = surfaceY;

                // 2. Determine the dominant biome for block selection
                // We re-calculate climate to see which biome "wins" at this specific spot
                float t = (world.TempNoise.GetNoise(worldX, worldZ) + 1f) / 2f;
                float h = (world.HumidityNoise.GetNoise(worldX, worldZ) + 1f) / 2f;

                BiomeType dominantBiome = BiomeType.Plains;
                float maxInfluence = -1.0f;

                foreach (BiomeType type in Enum.GetValues(typeof(BiomeType)))
                {
                    // We use the internal BiomeManager method to find influence
                    // Note: If CalculateInfluence is private, you may need to make it public in BiomeManager.cs
                    float influence = 1.0f - (MathF.Sqrt(MathF.Pow(t - BiomeManager.GetSettings(type).IdealTemp, 2) +
                                             MathF.Pow(h - BiomeManager.GetSettings(type).IdealHumidity, 2)) * 1.2f);

                    if (influence > maxInfluence)
                    {
                        maxInfluence = influence;
                        dominantBiome = type;
                    }
                }

                // 3. Fill the column
                for (int y = 0; y < Height; y++)
                {
                    if (y > surfaceY)
                    {
                        // Water level check (if below SEA_LEVEL and no block, place water)
                        if (y <= BiomeManager.SEA_LEVEL)
                        {
                            Blocks[x, y, z] = (byte)BlockType.Water;
                        }
                        else
                        {
                            Blocks[x, y, z] = (byte)BlockType.Air;
                        }
                    }
                    else if (y == surfaceY)
                    {
                        // Assign test blocks based on dominant biome
                        Blocks[x, y, z] = dominantBiome switch
                        {
                            BiomeType.Plains => (byte)BlockType.Grass,
                            BiomeType.Tundra => (byte)BlockType.Dirt,  // Testing requirement
                            BiomeType.Mountains => (byte)BlockType.Mud, // Testing requirement
                            BiomeType.Desert => (byte)BlockType.Stone,
                            BiomeType.Forest => (byte)BlockType.CoarseDirt,
                            _ => (byte)BlockType.Grass
                        };
                    }
                    else if (y > surfaceY - 4)
                    {
                        // Subsurface
                        Blocks[x, y, z] = (byte)BlockType.Dirt;
                    }
                    else
                    {
                        // Deep underground
                        Blocks[x, y, z] = (byte)BlockType.Stone;
                    }
                }
            }
        }
    }

    private byte SetBlockType(int y, int height)
    {
        if (y > height) return y <= (int)BiomeManager.SEA_LEVEL ? (byte)BlockType.Water : (byte)BlockType.Air;
        if (y == height) return y <= (int)BiomeManager.SEA_LEVEL - 1 ? (byte)BlockType.Mud : (byte)BlockType.Grass;
        if (y > height - 4) return (byte)BlockType.Dirt;
        return (byte)BlockType.Stone;
    }


    private void AddGreedyFace(List<float> v, int x, int y, int z, int w, int d, bool isTop, byte type, float[] ao)
    {
        var face = isTop ? "top" : "bottom";
        var data = GetFaceData(type, face, w, d);
        Vector3 normal = isTop ? new Vector3(0, 1, 0) : new Vector3(0, -1, 0);
        float yPos = isTop ? y + 1 : y;

        // Corner Positions
        Vector3 v0 = new Vector3(x, yPos, z);         // Bottom-Left
        Vector3 v1 = new Vector3(x + w, yPos, z);     // Bottom-Right
        Vector3 v2 = new Vector3(x + w, yPos, z + d); // Top-Right
        Vector3 v3 = new Vector3(x, yPos, z + d);     // Top-Left

        // For TOP faces to be CCW (visible from above):
        // Triangle 1: v0 -> v3 -> v2
        // Triangle 2: v0 -> v2 -> v1
        // (This is the reverse of what was likely happening)

        if (ao[0] + ao[2] < ao[1] + ao[3])
        {
            // Triangle 1
            AddVertex(v, v0, data.color, new Vector2(data.min.X, data.min.Y), normal, ao[0]);
            AddVertex(v, v3, data.color, new Vector2(data.min.X, data.max.Y), normal, ao[3]);
            AddVertex(v, v2, data.color, new Vector2(data.max.X, data.max.Y), normal, ao[2]);

            // Triangle 2
            AddVertex(v, v0, data.color, new Vector2(data.min.X, data.min.Y), normal, ao[0]);
            AddVertex(v, v2, data.color, new Vector2(data.max.X, data.max.Y), normal, ao[2]);
            AddVertex(v, v1, data.color, new Vector2(data.max.X, data.min.Y), normal, ao[1]);
        }
        else
        {
            // Triangle 1
            AddVertex(v, v1, data.color, new Vector2(data.max.X, data.min.Y), normal, ao[1]);
            AddVertex(v, v0, data.color, new Vector2(data.min.X, data.min.Y), normal, ao[0]);
            AddVertex(v, v3, data.color, new Vector2(data.min.X, data.max.Y), normal, ao[3]);

            // Triangle 2
            AddVertex(v, v1, data.color, new Vector2(data.max.X, data.min.Y), normal, ao[1]);
            AddVertex(v, v3, data.color, new Vector2(data.min.X, data.max.Y), normal, ao[3]);
            AddVertex(v, v2, data.color, new Vector2(data.max.X, data.max.Y), normal, ao[2]);
        }
    }

    // 3. Update AddVerticalGreedySide for Side normals
    private void AddVerticalGreedySide(List<float> v, int x, int y, int z, int height, int side, byte type, float[] ao)
    {
        var data = GetFaceData(type, "side", 1, height);
        Vector3 p1, p2, p3, p4;
        Vector3 normal;

        // Determine corner positions and normals based on side
        switch (side)
        {
            case 0: // Left (-X)
                p1 = new Vector3(x, y, z); p2 = new Vector3(x, y, z + 1);
                p3 = new Vector3(x, y + height, z + 1); p4 = new Vector3(x, y + height, z);
                normal = new Vector3(-1, 0, 0); break;
            case 1: // Right (+X)
                p1 = new Vector3(x + 1, y, z + 1); p2 = new Vector3(x + 1, y, z);
                p3 = new Vector3(x + 1, y + height, z); p4 = new Vector3(x + 1, y + height, z + 1);
                normal = new Vector3(1, 0, 0); break;
            case 2: // Front (+Z)
                p1 = new Vector3(x, y, z + 1); p2 = new Vector3(x + 1, y, z + 1);
                p3 = new Vector3(x + 1, y + height, z + 1); p4 = new Vector3(x, y + height, z + 1);
                normal = new Vector3(0, 0, 1); break;
            default: // Back (-Z)
                p1 = new Vector3(x + 1, y, z); p2 = new Vector3(x, y, z);
                p3 = new Vector3(x, y + height, z); p4 = new Vector3(x + 1, y + height, z);
                normal = new Vector3(0, 0, -1); break;
        }

        // Mapping: ao[0]: p1, ao[1]: p2, ao[2]: p3, ao[3]: p4
        // Flip diagonal if the alternative diagonal is darker to smooth the gradient
        if (ao[0] + ao[2] < ao[1] + ao[3])
        {
            // Triangle 1
            AddVertex(v, p1, data.color, new Vector2(data.min.X, data.min.Y), normal, ao[0]);
            AddVertex(v, p3, data.color, new Vector2(data.max.X, data.max.Y), normal, ao[2]);
            AddVertex(v, p4, data.color, new Vector2(data.min.X, data.max.Y), normal, ao[3]);

            // Triangle 2
            AddVertex(v, p1, data.color, new Vector2(data.min.X, data.min.Y), normal, ao[0]);
            AddVertex(v, p2, data.color, new Vector2(data.max.X, data.min.Y), normal, ao[1]);
            AddVertex(v, p3, data.color, new Vector2(data.max.X, data.max.Y), normal, ao[2]);
        }
        else
        {
            // Triangle 1
            AddVertex(v, p2, data.color, new Vector2(data.max.X, data.min.Y), normal, ao[1]);
            AddVertex(v, p3, data.color, new Vector2(data.max.X, data.max.Y), normal, ao[2]);
            AddVertex(v, p4, data.color, new Vector2(data.min.X, data.max.Y), normal, ao[3]);

            // Triangle 2
            AddVertex(v, p2, data.color, new Vector2(data.max.X, data.min.Y), normal, ao[1]);
            AddVertex(v, p4, data.color, new Vector2(data.min.X, data.max.Y), normal, ao[3]);
            AddVertex(v, p1, data.color, new Vector2(data.min.X, data.min.Y), normal, ao[0]);
        }
    }



    private void AddBottomFace(List<float> v, int x, int y, int z, byte type, float[] ao)
    {
        var data = GetFaceData(type, "bottom", 1, 1);
        Vector3 normal = new Vector3(0, -1, 0);

        Vector3 v0 = new Vector3(x, y, z);
        Vector3 v1 = new Vector3(x + 1, y, z);
        Vector3 v2 = new Vector3(x + 1, y, z + 1);
        Vector3 v3 = new Vector3(x, y, z + 1);

        if (ao[0] + ao[2] < ao[1] + ao[3])
        {
            AddVertex(v, v0, data.color, new Vector2(data.min.X, data.min.Y), normal, ao[0]);
            AddVertex(v, v2, data.color, new Vector2(data.max.X, data.max.Y), normal, ao[2]);
            AddVertex(v, v3, data.color, new Vector2(data.min.X, data.max.Y), normal, ao[3]);

            AddVertex(v, v0, data.color, new Vector2(data.min.X, data.min.Y), normal, ao[0]);
            AddVertex(v, v1, data.color, new Vector2(data.max.X, data.min.Y), normal, ao[1]);
            AddVertex(v, v2, data.color, new Vector2(data.max.X, data.max.Y), normal, ao[2]);
        }
        else
        {
            AddVertex(v, v1, data.color, new Vector2(data.max.X, data.min.Y), normal, ao[1]);
            AddVertex(v, v2, data.color, new Vector2(data.max.X, data.max.Y), normal, ao[2]);
            AddVertex(v, v3, data.color, new Vector2(data.min.X, data.max.Y), normal, ao[3]);

            AddVertex(v, v1, data.color, new Vector2(data.max.X, data.min.Y), normal, ao[1]);
            AddVertex(v, v3, data.color, new Vector2(data.min.X, data.max.Y), normal, ao[3]);
            AddVertex(v, v0, data.color, new Vector2(data.min.X, data.min.Y), normal, ao[0]);
        }
    }

    private void AddWaterPlane(List<float> v, int x, int y, int z, float offset, byte type)
    {
        var data = GetFaceData(type, "top", 1, 1);
        float surfaceY = (y + 1) - offset;
        float ao = 1f;
        Vector3 normalUp = new Vector3(0, 1, 0);
        Vector3 normalDown = new Vector3(0, -1, 0);

        // --- TOP FACE (Visible from sky) ---
        AddVertex(v, new Vector3(x, surfaceY, z), data.color, new Vector2(data.min.X, data.min.Y), normalUp, ao);
        AddVertex(v, new Vector3(x, surfaceY, z + 1), data.color, new Vector2(data.min.X, data.max.Y), normalUp, ao);
        AddVertex(v, new Vector3(x + 1, surfaceY, z + 1), data.color, new Vector2(data.max.X, data.max.Y), normalUp, ao);

        AddVertex(v, new Vector3(x, surfaceY, z), data.color, new Vector2(data.min.X, data.min.Y), normalUp, ao);
        AddVertex(v, new Vector3(x + 1, surfaceY, z + 1), data.color, new Vector2(data.max.X, data.max.Y), normalUp, ao);
        AddVertex(v, new Vector3(x + 1, surfaceY, z), data.color, new Vector2(data.max.X, data.min.Y), normalUp, ao);

        // --- UNDERSIDE FACE (Visible from underwater) ---
        AddVertex(v, new Vector3(x, surfaceY, z), data.color, new Vector2(data.min.X, data.min.Y), normalDown, ao);
        AddVertex(v, new Vector3(x + 1, surfaceY, z + 1), data.color, new Vector2(data.max.X, data.max.Y), normalDown, ao);
        AddVertex(v, new Vector3(x, surfaceY, z + 1), data.color, new Vector2(data.min.X, data.max.Y), normalDown, ao);

        AddVertex(v, new Vector3(x, surfaceY, z), data.color, new Vector2(data.min.X, data.min.Y), normalDown, ao);
        AddVertex(v, new Vector3(x + 1, surfaceY, z), data.color, new Vector2(data.max.X, data.min.Y), normalDown, ao);
        AddVertex(v, new Vector3(x + 1, surfaceY, z + 1), data.color, new Vector2(data.max.X, data.max.Y), normalDown, ao);
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
    private void AddVertex(List<float> v, Vector3 pos, Vector3 color, Vector2 uv, Vector3 normal, float ao)
    {
        // Position (0, 1, 2)
        v.Add(pos.X); v.Add(pos.Y); v.Add(pos.Z);
        // Color (3, 4, 5)
        v.Add(color.X); v.Add(color.Y); v.Add(color.Z);
        // UV (6, 7)
        v.Add(uv.X); v.Add(uv.Y);
        // Normal (8, 9, 10) - New!
        v.Add(normal.X); v.Add(normal.Y); v.Add(normal.Z);

        v.Add(ao);
    }

    private (Vector2 min, Vector2 max, Vector3 color) GetFaceData(byte type, string face, int w, int h)
    {
        float atlasWidthTiles = 8f; // Total blocks in your atlas
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

        var n = _world.GetNeighbors(ChunkX, ChunkZ);
        Chunk? r = n.r; Chunk? l = n.l; Chunk? f = n.f; Chunk? b = n.b;

        int topFaceCount = 0;
        int totalVoxels = 0;

        bool IsOpaque(int x, int y, int z)
        {
            byte id = GetBlockId(x, y, z, r, l, f, b);
            return id != 0 && id != (byte)BlockType.Water;
        }

        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                for (int y = 0; y < Height; y++)
                {
                    byte blockType = Blocks[x, y, z];
                    if (blockType == 0) continue;
                    totalVoxels++;

                    bool up = !IsOpaque(x, y + 1, z);
                    bool down = !IsOpaque(x, y - 1, z);
                    bool left = !IsOpaque(x - 1, y, z);
                    bool right = !IsOpaque(x + 1, y, z);
                    bool front = !IsOpaque(x, y, z + 1);
                    bool back = !IsOpaque(x, y, z - 1);

                    if (blockType == (byte)BlockType.Water)
                    {
                        bool waterSurface = (y + 1 >= Height) || GetBlockId(x, y + 1, z, r, l, f, b) == 0;
                        if (waterSurface)
                        {
                            AddWaterPlane(waterBuffer, x, y, z, 0.06f, blockType);
                        }
                    }
                    else
                    {
                        if (!up && !down && !left && !right && !front && !back) continue;

                        if (up)
                        {
                            AddGreedyFace(opaqueBuffer, x, y, z, 1, 1, true, blockType, CalculateFaceAO(x, y, z, "top", r, l, f, b));
                            topFaceCount++;
                        }
                        if (down) AddBottomFace(opaqueBuffer, x, y, z, blockType, CalculateFaceAO(x, y, z, "bottom", r, l, f, b));
                        if (left) AddVerticalGreedySide(opaqueBuffer, x, y, z, 1, 0, blockType, CalculateFaceAO(x, y, z, "left", r, l, f, b));
                        if (right) AddVerticalGreedySide(opaqueBuffer, x, y, z, 1, 1, blockType, CalculateFaceAO(x, y, z, "right", r, l, f, b));
                        if (front) AddVerticalGreedySide(opaqueBuffer, x, y, z, 1, 2, blockType, CalculateFaceAO(x, y, z, "front", r, l, f, b));
                        if (back) AddVerticalGreedySide(opaqueBuffer, x, y, z, 1, 3, blockType, CalculateFaceAO(x, y, z, "back", r, l, f, b));
                    }
                }
            }
        }

        if (topFaceCount == 0 && totalVoxels > 0)
        {
            Console.WriteLine($"[FVD Warning] Chunk {ChunkX},{ChunkZ} has {totalVoxels} voxels but ZERO top faces generated.");
        }

        return (opaqueBuffer.ToArray(), waterBuffer.ToArray());
    }


    public byte GetBlockId(int x, int y, int z, Chunk? r, Chunk? l, Chunk? f, Chunk? b)
    {
        // 1. Height Safety using actual array bounds
        if (y < 0 || y >= Height) return 0;

        // 2. Internal Check (0 to Size-1)
        if (x >= 0 && x < Size && z >= 0 && z < Size)
        {
            return Blocks[x, y, z];
        }

        // 3. Neighbor Checks with Bound Guarding
        // We use Math.Clamp to ensure that if AO asks for a corner (like x=16, z=16),
        // we don't crash the neighbor's array either.
        try
        {
            if (x >= Size)
                return r != null ? r.Blocks[0, y, Math.Clamp(z, 0, Size - 1)] : (byte)0;
            if (x < 0)
                return l != null ? l.Blocks[Size - 1, y, Math.Clamp(z, 0, Size - 1)] : (byte)0;
            if (z >= Size)
                return f != null ? f.Blocks[Math.Clamp(x, 0, Size - 1), y, 0] : (byte)0;
            if (z < 0)
                return b != null ? b.Blocks[Math.Clamp(x, 0, Size - 1), y, Size - 1] : (byte)0;
        }
        catch (IndexOutOfRangeException)
        {
            // If we STILL hit a bound issue, return air instead of killing the thread
            return 0;
        }

        return 0;
    }

    private float GetVertexAO(bool side1, bool side2, bool corner)
    {
        if (side1 && side2) return 0.4f; // Both sides blocked: Max dark

        int count = (side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0);
        return count switch
        {
            1 => 0.8f,
            2 => 0.6f,
            3 => 0.4f,
            _ => 1.0f  // No neighbors: Fully bright
        };
    }

    private float[] CalculateFaceAO(int x, int y, int z, string face, Chunk? r, Chunk? l, Chunk? f, Chunk? b)
    {
        float[] ao = new float[4];

        // Helper to get neighbor blocks safely
        // Note: y+1 or y-1 inside the chunk is usually safe, but check bounds if your world has a ceiling/floor
        bool GetSafe(int nx, int ny, int nz)
        {
            return GetBlockId(nx, ny, nz, r, l, f, b) != 0;
        }

        switch (face)
        {
            case "top":
                // Samples at Y + 1
                ao[0] = GetVertexAO(GetSafe(x - 1, y + 1, z), GetSafe(x, y + 1, z - 1), GetSafe(x - 1, y + 1, z - 1)); // NW
                ao[1] = GetVertexAO(GetSafe(x + 1, y + 1, z), GetSafe(x, y + 1, z - 1), GetSafe(x + 1, y + 1, z - 1)); // NE
                ao[2] = GetVertexAO(GetSafe(x + 1, y + 1, z), GetSafe(x, y + 1, z + 1), GetSafe(x + 1, y + 1, z + 1)); // SE
                ao[3] = GetVertexAO(GetSafe(x - 1, y + 1, z), GetSafe(x, y + 1, z + 1), GetSafe(x - 1, y + 1, z + 1)); // SW
                break;

            case "bottom":
                // Samples at Y - 1
                ao[0] = GetVertexAO(GetSafe(x - 1, y - 1, z), GetSafe(x, y - 1, z - 1), GetSafe(x - 1, y - 1, z - 1));
                ao[1] = GetVertexAO(GetSafe(x + 1, y - 1, z), GetSafe(x, y - 1, z - 1), GetSafe(x + 1, y - 1, z - 1));
                ao[2] = GetVertexAO(GetSafe(x + 1, y - 1, z), GetSafe(x, y - 1, z + 1), GetSafe(x + 1, y - 1, z + 1));
                ao[3] = GetVertexAO(GetSafe(x - 1, y - 1, z), GetSafe(x, y - 1, z + 1), GetSafe(x - 1, y - 1, z + 1));
                break;

            case "left": // -X side
                ao[0] = GetVertexAO(GetSafe(x - 1, y - 1, z), GetSafe(x - 1, y, z - 1), GetSafe(x - 1, y - 1, z - 1));
                ao[1] = GetVertexAO(GetSafe(x - 1, y - 1, z), GetSafe(x - 1, y, z + 1), GetSafe(x - 1, y - 1, z + 1));
                ao[2] = GetVertexAO(GetSafe(x - 1, y + 1, z), GetSafe(x - 1, y, z + 1), GetSafe(x - 1, y + 1, z + 1));
                ao[3] = GetVertexAO(GetSafe(x - 1, y + 1, z), GetSafe(x - 1, y, z - 1), GetSafe(x - 1, y + 1, z - 1));
                break;

            case "right": // +X side
                ao[0] = GetVertexAO(GetSafe(x + 1, y - 1, z), GetSafe(x + 1, y, z + 1), GetSafe(x + 1, y - 1, z + 1));
                ao[1] = GetVertexAO(GetSafe(x + 1, y - 1, z), GetSafe(x + 1, y, z - 1), GetSafe(x + 1, y - 1, z - 1));
                ao[2] = GetVertexAO(GetSafe(x + 1, y + 1, z), GetSafe(x + 1, y, z - 1), GetSafe(x + 1, y + 1, z - 1));
                ao[3] = GetVertexAO(GetSafe(x + 1, y + 1, z), GetSafe(x + 1, y, z + 1), GetSafe(x + 1, y + 1, z + 1));
                break;

            case "front": // +Z side
                ao[0] = GetVertexAO(GetSafe(x - 1, y, z + 1), GetSafe(x, y - 1, z + 1), GetSafe(x - 1, y - 1, z + 1));
                ao[1] = GetVertexAO(GetSafe(x + 1, y, z + 1), GetSafe(x, y - 1, z + 1), GetSafe(x + 1, y - 1, z + 1));
                ao[2] = GetVertexAO(GetSafe(x + 1, y, z + 1), GetSafe(x, y + 1, z + 1), GetSafe(x + 1, y + 1, z + 1));
                ao[3] = GetVertexAO(GetSafe(x - 1, y, z + 1), GetSafe(x, y + 1, z + 1), GetSafe(x - 1, y + 1, z + 1));
                break;

            case "back": // -Z side
                ao[0] = GetVertexAO(GetSafe(x + 1, y, z - 1), GetSafe(x, y - 1, z - 1), GetSafe(x + 1, y - 1, z - 1));
                ao[1] = GetVertexAO(GetSafe(x - 1, y, z - 1), GetSafe(x, y - 1, z - 1), GetSafe(x - 1, y - 1, z - 1));
                ao[2] = GetVertexAO(GetSafe(x - 1, y, z - 1), GetSafe(x, y + 1, z - 1), GetSafe(x - 1, y + 1, z - 1));
                ao[3] = GetVertexAO(GetSafe(x + 1, y, z - 1), GetSafe(x, y + 1, z - 1), GetSafe(x + 1, y + 1, z - 1));
                break;

            default:
                ao[0] = ao[1] = ao[2] = ao[3] = 1.0f;
                break;
        }

        return ao;
    }
}