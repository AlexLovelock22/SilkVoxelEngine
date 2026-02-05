using System.Collections.Generic;

namespace VoxelEngine_Silk.Net_1._0.World;

public class Chunk
{
    public const int Size = 16;
    public byte[,,] Blocks = new byte[Size, Size, Size];

    public Chunk()
    {
        // Simple terrain generation: 
        // Fill the bottom half (y < 8) with blocks
        for (int x = 0; x < Size; x++)
            for (int z = 0; z < Size; z++)
                for (int y = 0; y < Size / 2; y++)
                    Blocks[x, y, z] = 1;
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

                    // Check each of the 6 directions
                    bool up = IsAir(x, y + 1, z);
                    bool down = IsAir(x, y - 1, z);
                    bool left = IsAir(x - 1, y, z);
                    bool right = IsAir(x + 1, y, z);
                    bool front = IsAir(x, y, z + 1);
                    bool back = IsAir(x, y, z - 1);

                    // Only add the cube if at least one face is visible
                    AddCube(vertices, x, y, z, up, down, left, right, front, back);
                }
            }
        }
        return vertices.ToArray();
    }

    // Helper to check if a neighbor is Air or outside the chunk
    private bool IsAir(int x, int y, int z)
    {
        if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size)
            return true; // Treat boundaries as Air so we see the outside faces

        return Blocks[x, y, z] == 0;
    }

    private void AddCube(List<float> v, float x, float y, float z,
                     bool up, bool down, bool left, bool right, bool front, bool back)
    {
        // Seeded random so the colors don't flicker when the mesh regenerates
        Random rand = new Random((int)(x + y * Size + z * Size * Size));
        float r = (float)rand.NextDouble();
        float g = (float)rand.NextDouble();
        float b = (float)rand.NextDouble();

        // UP FACE (Top)
        if (up)
        {
            v.AddRange(new float[] {
            x-0.5f, y+0.5f, z-0.5f, r, g, b,  x+0.5f, y+0.5f, z-0.5f, r, g, b,  x+0.5f, y+0.5f, z+0.5f, r, g, b,
            x+0.5f, y+0.5f, z+0.5f, r, g, b,  x-0.5f, y+0.5f, z+0.5f, r, g, b,  x-0.5f, y+0.5f, z-0.5f, r, g, b
        });
        }

        // DOWN FACE (Bottom)
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