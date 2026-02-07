using System.Numerics;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0;

public struct RaycastResult
{
    public bool Hit;
    public Vector3 IntPos;   // The block we are looking at
    public Vector3 PlacePos; // The empty space for placement
}

public static class Raycaster
{
    public static RaycastResult Trace(VoxelWorld world, Vector3 origin, Vector3 direction, float maxDistance)
    {
        // 1. Determine direction of movement (sign) and current integer block
        int x = (int)MathF.Floor(origin.X);
        int y = (int)MathF.Floor(origin.Y);
        int z = (int)MathF.Floor(origin.Z);

        int stepX = direction.X > 0 ? 1 : -1;
        int stepY = direction.Y > 0 ? 1 : -1;
        int stepZ = direction.Z > 0 ? 1 : -1;

        // 2. Calculate distance to the next grid boundary
        // We use MathF.Max to avoid division by zero
        Vector3 tMax = new Vector3(
            (stepX > 0 ? (x + 1) - origin.X : origin.X - x) / MathF.Max(MathF.Abs(direction.X), 0.0001f),
            (stepY > 0 ? (y + 1) - origin.Y : origin.Y - y) / MathF.Max(MathF.Abs(direction.Y), 0.0001f),
            (stepZ > 0 ? (z + 1) - origin.Z : origin.Z - z) / MathF.Max(MathF.Abs(direction.Z), 0.0001f)
        );

        // 3. How far we travel along the ray to cross exactly one voxel width
        Vector3 tDelta = new Vector3(
            1.0f / MathF.Max(MathF.Abs(direction.X), 0.0001f),
            1.0f / MathF.Max(MathF.Abs(direction.Y), 0.0001f),
            1.0f / MathF.Max(MathF.Abs(direction.Z), 0.0001f)
        );

        Vector3 lastPos = new Vector3(x, y, z);
        float totalDist = 0;

        // 4. Traversal Loop
        while (totalDist < maxDistance)
        {
            byte block = world.GetBlock(x, y, z);

            if (!BlockRegistry.IsTransparent(block))
            {
                return new RaycastResult
                {
                    Hit = true,
                    IntPos = new Vector3(x, y, z),
                    PlacePos = lastPos // The voxel we were in BEFORE hitting this one
                };
            }

            lastPos = new Vector3(x, y, z);

            // Move to the next closest grid boundary
            if (tMax.X < tMax.Y)
            {
                if (tMax.X < tMax.Z)
                {
                    totalDist = tMax.X;
                    tMax.X += tDelta.X;
                    x += stepX;
                }
                else
                {
                    totalDist = tMax.Z;
                    tMax.Z += tDelta.Z;
                    z += stepZ;
                }
            }
            else
            {
                if (tMax.Y < tMax.Z)
                {
                    totalDist = tMax.Y;
                    tMax.Y += tDelta.Y;
                    y += stepY;
                }
                else
                {
                    totalDist = tMax.Z;
                    tMax.Z += tDelta.Z;
                    z += stepZ;
                }
            }
        }

        return new RaycastResult { Hit = false };
    }
}