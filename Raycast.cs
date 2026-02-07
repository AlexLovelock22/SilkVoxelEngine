using System.Numerics;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0;

public struct RaycastResult
{
    public bool Hit;
    public Vector3 IntPos;   // The coordinate of the block we hit (Break target)
    public Vector3 PlacePos; // The coordinate of the face neighbor (Place target)
}

public static class Raycaster
{
    /// <summary>
    /// Traces a ray from origin in a direction to find the first non-transparent block.
    /// </summary>
    public static RaycastResult Trace(VoxelWorld world, Vector3 origin, Vector3 direction, float maxDistance)
    {
        // We step in small increments to ensure we don't skip over corners
        float step = 0.05f; 
        Vector3 currentPos = origin;
        Vector3 lastPos = origin;

        for (float distance = 0; distance < maxDistance; distance += step)
        {
            currentPos = origin + (direction * distance);

            int x = (int)MathF.Floor(currentPos.X);
            int y = (int)MathF.Floor(currentPos.Y);
            int bz = (int)MathF.Floor(currentPos.Z);

            byte block = world.GetBlock(x, y, bz);

            // Using your BlockRegistry to determine what is "solid" vs "interactable"
            // We ignore Air (0) and Water (5) so we can click through water to the riverbed
            if (!BlockRegistry.IsTransparent(block))
            {
                return new RaycastResult
                {
                    Hit = true,
                    IntPos = new Vector3(x, y, bz),
                    // The placement position is the last air/water block we were in before hitting the solid
                    PlacePos = new Vector3(
                        (int)MathF.Floor(lastPos.X),
                        (int)MathF.Floor(lastPos.Y),
                        (int)MathF.Floor(lastPos.Z)
                    )
                };
            }

            lastPos = currentPos;
        }

        return new RaycastResult { Hit = false };
    }
}