using System;
using System.Collections.Generic;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.World;

public class Chunk
{
    public const int Size = 16;
    public byte[,,] Blocks = new byte[Size, Size, Size];

    // 1. Store the chunk's grid coordinates
    public int ChunkX { get; private set; }
    public int ChunkZ { get; private set; }
    private VoxelWorld _world;


    // 2. Accept coordinates in the constructor
    public Chunk(int chunkX, int chunkZ, VoxelWorld world)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        _world = world;
        GenerateTerrain();
    }

    private void GenerateTerrain()
    {
        // Simple terrain generation for now
        // We will replace this with Perlin Noise math in the next step
        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                // We use the Chunk coordinates to offset the Y-level or Noise
                for (int y = 0; y < Size / 2; y++)
                {
                    Blocks[x, y, z] = 1;
                }
            }
        }
    }

    public float[] GetVertexData()
    {
        List<float> vertices = new List<float>();

        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int z = 0; z < Size; z++)
                {
                    if (Blocks[x, y, z] == 0) continue;

                    bool up = IsAir(x, y + 1, z);
                    bool down = IsAir(x, y - 1, z);
                    bool left = IsAir(x - 1, y, z);
                    bool right = IsAir(x + 1, y, z);
                    bool front = IsAir(x, y, z + 1);
                    bool back = IsAir(x, y, z - 1);

                    AddCube(vertices, x, y, z, up, down, left, right, front, back);
                }
            }
        }
        return vertices.ToArray();
    }

    private bool IsAir(int x, int y, int z)
    {
        // Convert local chunk coordinates to world coordinates
        int worldX = x + (ChunkX * Size);
        int worldY = y;
        int worldZ = z + (ChunkZ * Size);

        // Ask the world manager for the block at those global coordinates
        return _world.GetBlock(worldX, worldY, worldZ) == 0;
    }

    private void AddCube(List<float> v, float x, float y, float z,
                         bool up, bool down, bool left, bool right, bool front, bool back)
    {
        // 3. We use the World Position (local + chunk offset) for the Random Seed
        // This ensures colors stay the same even if the chunk is re-meshed
        int worldX = (int)x + (ChunkX * Size);
        int worldZ = (int)z + (ChunkZ * Size);

        Random rand = new Random((int)(worldX + y * 1000 + worldZ * 10000));
        float r = (float)rand.NextDouble();
        float g = (float)rand.NextDouble();
        float b = (float)rand.NextDouble();

        // UP FACE
        if (up)
        {
            v.AddRange(new float[] {
                x-0.5f, y+0.5f, z-0.5f, r, g, b,  x+0.5f, y+0.5f, z-0.5f, r, g, b,  x+0.5f, y+0.5f, z+0.5f, r, g, b,
                x+0.5f, y+0.5f, z+0.5f, r, g, b,  x-0.5f, y+0.5f, z+0.5f, r, g, b,  x-0.5f, y+0.5f, z-0.5f, r, g, b
            });
        }

        // DOWN FACE
        if (down)
        {
            v.AddRange(new float[] {
                x-0.5f, y-0.5f, z-0.5f, r, g, b,  x+0.5f, y-0.5f, z-0.5f, r, g, b,  x+0.5f, y-0.5f, z+0.5f, r, g, b,
                x+0.5f, y-0.5f, z+0.5f, r, g, b,  x-0.5f, y-0.5f, z+0.5f, r, g, b,  x-0.5f, y-0.5f, z-0.5f, r, g, b
            });
        }

        // LEFT FACE
        if (left)
        {
            v.AddRange(new float[] {
                x-0.5f, y+0.5f, z+0.5f, r, g, b,  x-0.5f, y+0.5f, z-0.5f, r, g, b,  x-0.5f, y-0.5f, z-0.5f, r, g, b,
                x-0.5f, y-0.5f, z-0.5f, r, g, b,  x-0.5f, y-0.5f, z+0.5f, r, g, b,  x-0.5f, y+0.5f, z+0.5f, r, g, b
            });
        }

        // RIGHT FACE
        if (right)
        {
            v.AddRange(new float[] {
                x+0.5f, y+0.5f, z+0.5f, r, g, b,  x+0.5f, y+0.5f, z-0.5f, r, g, b,  x+0.5f, y-0.5f, z-0.5f, r, g, b,
                x+0.5f, y-0.5f, z-0.5f, r, g, b,  x+0.5f, y-0.5f, z+0.5f, r, g, b,  x+0.5f, y+0.5f, z+0.5f, r, g, b
            });
        }

        // FRONT FACE
        if (front)
        {
            v.AddRange(new float[] {
                x-0.5f, y-0.5f, z+0.5f, r, g, b,  x+0.5f, y-0.5f, z+0.5f, r, g, b,  x+0.5f, y+0.5f, z+0.5f, r, g, b,
                x+0.5f, y+0.5f, z+0.5f, r, g, b,  x-0.5f, y+0.5f, z+0.5f, r, g, b,  x-0.5f, y-0.5f, z+0.5f, r, g, b
            });
        }

        // BACK FACE
        if (back)
        {
            v.AddRange(new float[] {
                x-0.5f, y-0.5f, z-0.5f, r, g, b,  x+0.5f, y-0.5f, z-0.5f, r, g, b,  x+0.5f, y+0.5f, z-0.5f, r, g, b,
                x+0.5f, y+0.5f, z-0.5f, r, g, b,  x-0.5f, y+0.5f, z-0.5f, r, g, b,  x-0.5f, y-0.5f, z-0.5f, r, g, b
            });
        }
    }
}