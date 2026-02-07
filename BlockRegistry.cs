namespace VoxelEngine_Silk.Net_1._0.World;

public enum BlockType : byte
{
    Air = 0,
    Grass = 1,
    Dirt = 2,
    Water = 3,
    Mud = 4,
    Stone = 5
}

public static class BlockRegistry
{
    // Defines where in the atlas the texture starts (0, 1, 2, 3...)
    // Assumes atlas is 1 row high, 6+ tiles wide.
    public static int GetTexture(BlockType type, string face)
    {
        return type switch
        {
            // Index 1 is the pure green top, Index 0 is the side with dirt peaks
            BlockType.Grass => face == "top" ? 1 : (face == "bottom" ? 2 : 0),
            BlockType.Dirt => 2,
            BlockType.Water => 3,
            BlockType.Mud => 4,
            BlockType.Stone => 5,
            _ => 0
        };
    }

    public static bool IsTransparent(byte id) => id == (byte)BlockType.Air || id == (byte)BlockType.Water;
}