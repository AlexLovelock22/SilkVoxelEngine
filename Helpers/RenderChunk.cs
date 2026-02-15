using System.Numerics;

namespace VoxelEngine_Silk.Net_1._0.Helpers;

public class RenderChunk
{
    // Pass 1: Solid
    public uint OpaqueVAO;
    public uint OpaqueVBO;
    public uint OpaqueVertexCount;

    // Pass 2: Water
    public uint WaterVAO;
    public uint WaterVBO;
    public uint WaterVertexCount;

    public Vector3 WorldPosition;

    public uint AO;
}