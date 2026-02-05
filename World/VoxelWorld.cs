using System;
using System.Collections.Generic;

namespace VoxelEngine_Silk.Net_1._0.World;

public class VoxelWorld
{
    public Dictionary<(int, int), Chunk> Chunks = new();

    public byte GetBlock(int x, int y, int z)
    {
        // 1. Identify which chunk these global coordinates belong to
        int cx = (int)Math.Floor(x / (float)Chunk.Size);
        int cz = (int)Math.Floor(z / (float)Chunk.Size);

        // 2. Find local coordinates within that chunk (0 to 15)
        int lx = x - (cx * Chunk.Size);
        int lz = z - (cz * Chunk.Size);

        if (Chunks.TryGetValue((cx, cz), out var chunk))
        {
            // Simple bounds check for Y (since we don't have vertical chunks yet)
            if (y < 0 || y >= Chunk.Size) return 0;
            return chunk.Blocks[lx, y, lz];
        }

        return 0; // Air
    }
}