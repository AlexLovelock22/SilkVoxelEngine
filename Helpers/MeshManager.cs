using Silk.NET.OpenGL;
using System.Collections.Concurrent;
using System.Numerics;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.Helpers;

public static class MeshManager
{
    public static unsafe void UploadToVAO(ref uint vao, ref uint vbo, float[] data, out uint vertexCount, GL gl)
    {
        vertexCount = (uint)(data.Length / 12);
        if (vertexCount == 0) return;

        if (vao == 0) vao = gl.GenVertexArray();
        if (vbo == 0) vbo = gl.GenBuffer();

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        fixed (float* d = data)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), d, BufferUsageARB.StaticDraw);

        int stride = 12 * sizeof(float);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)(8 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, (uint)stride, (void*)(11 * sizeof(float)));
        gl.EnableVertexAttribArray(4);
    }

    public static unsafe void FinalizeGPUUpload(GL gl, List<RenderChunk> renderChunks, Chunk chunk, (float[] opaque, float[] water) meshData)
    {
        RenderChunk? rc = renderChunks.FirstOrDefault(c => c.WorldPosition == new Vector3(chunk.ChunkX * 16, 0, chunk.ChunkZ * 16));
        if (rc == null)
        {
            rc = new RenderChunk { WorldPosition = new Vector3(chunk.ChunkX * 16, 0, chunk.ChunkZ * 16) };
            renderChunks.Add(rc);
        }
        UploadToVAO(ref rc.OpaqueVAO, ref rc.OpaqueVBO, meshData.opaque, out rc.OpaqueVertexCount, gl);
        UploadToVAO(ref rc.WaterVAO, ref rc.WaterVBO, meshData.water, out rc.WaterVertexCount, gl);
    }

    public static void DeleteChunkMesh(GL gl, RenderChunk rc)
    {
        if (rc.OpaqueVAO != 0) gl.DeleteVertexArray(rc.OpaqueVAO);
        if (rc.OpaqueVBO != 0) gl.DeleteBuffer(rc.OpaqueVBO);
        if (rc.WaterVAO != 0) gl.DeleteVertexArray(rc.WaterVAO);
        if (rc.WaterVBO != 0) gl.DeleteBuffer(rc.WaterVBO);
    }


    /// <summary>
    /// Updates exactly one voxel in the GPU's 3D grid. Extremely fast for SetBlock calls.
    /// </summary>


    public static void DeleteMesh(GL gl, ref uint vao, ref uint vbo)
    {
        if (vao != 0) gl.DeleteVertexArray(vao);
        if (vbo != 0) gl.DeleteBuffer(vbo);
        vao = 0;
        vbo = 0;
    }

    public static unsafe void SetUniformMatrix(GL gl, uint shader, string name, Matrix4x4 matrix)
    {
        int location = gl.GetUniformLocation(shader, name);
        if (location != -1)
        {
            // Using (float*)&matrix takes the memory address of the matrix 
            // and treats it as a pointer to a series of floats for OpenGL.
            gl.UniformMatrix4(location, 1, false, (float*)&matrix);
        }
    }

    public static void RenderChunkMesh(GL gl, uint vao, uint vertexCount)
    {
        if (vertexCount == 0 || vao == 0) return;

        gl.BindVertexArray(vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);
    }


    // Modified ProcessUploadQueue in MeshManager.cs
    // Inside MeshManager.cs
    // Inside MeshManager.cs


    // // Helper to update the specific 16x16 area of the global heightmap
    // public static unsafe void UpdateHeightmapRegion(GL gl, uint tex, Chunk chunk)
    // {
    //     gl.BindTexture(TextureTarget.Texture2D, tex);

    //     int worldStartX = chunk.ChunkX * Chunk.Size;
    //     int worldStartZ = chunk.ChunkZ * Chunk.Size;

    //     // Mapping logic
    //     int mapX = ((worldStartX + 512) % 1024 + 1024) % 1024;
    //     int mapZ = ((worldStartZ + 512) % 1024 + 1024) % 1024;

    //     int[,] localMap = chunk.GetHeightMap();
    //     byte[] slice = new byte[Chunk.Size * Chunk.Size];

    //     // Tracking for logging
    //     int minH = 255;
    //     int maxH = 0;
    //     long totalH = 0;

    //     for (int z = 0; z < Chunk.Size; z++)
    //     {
    //         for (int x = 0; x < Chunk.Size; x++)
    //         {
    //             byte val = (byte)localMap[x, z];
    //             slice[z * Chunk.Size + x] = val;

    //             // Stats for logging
    //             if (val < minH) minH = val;
    //             if (val > maxH) maxH = val;
    //             totalH += val;
    //         }
    //     }

    //     // --- ENHANCED LOGGING ---
    //     // This will print the average height of the chunk. 
    //     // If you mine a block, you should see 'Avg' or 'Max' decrease in the console.
    //     float avg = totalH / (float)(Chunk.Size * Chunk.Size);
    //     Console.WriteLine($"[Heightmap Update] Chunk({chunk.ChunkX},{chunk.ChunkZ}) " +
    //                       $"at Tex({mapX},{mapZ}) | Min:{minH} Max:{maxH} Avg:{avg:F2}");

    //     fixed (byte* p = slice)
    //     {
    //         gl.TexSubImage2D(TextureTarget.Texture2D, 0, mapX, mapZ,
    //                          (uint)Chunk.Size, (uint)Chunk.Size,
    //                          PixelFormat.Red, PixelType.UnsignedByte, p);
    //     }
    // }

    /// <summary>
    /// Initializes a 1024x256x1024 R8 texture on the GPU to store block occupancy.
    /// </summary>
    // Inside MeshManager.cs
    // --- Inside MeshManager.cs ---

    public static unsafe uint CreateVoxel3DTexture(GL gl)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture3D, tex);

        // Initialize a 1024x256x1024 R8 texture
        gl.TexImage3D(TextureTarget.Texture3D, 0, InternalFormat.R8, 1024, 256, 1024, 0, PixelFormat.Red, PixelType.UnsignedByte, null);

        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);

        // CRITICAL: Repeat allows the "ring buffer" effect. ClampR prevents sky-bleeding.
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);

        Console.WriteLine($"[MeshManager] 3D Voxel Texture Initialized (ID: {tex})");
        return tex;
    }

    public static unsafe void UpdateVoxelIn3DTexture(GL gl, uint tex3D, int x, int y, int z, byte type)
    {
        // Bounds check logging
        if (x < 0 || x >= 1024 || y < 0 || y >= 256 || z < 0 || z >= 1024)
        {
            Console.WriteLine($"[GPU ERROR] Out of Bounds: ({x}, {y}, {z})");
            return;
        }

        byte val = (type > 0) ? (byte)255 : (byte)0;

        gl.BindTexture(TextureTarget.Texture3D, tex3D);
        gl.TexSubImage3D(TextureTarget.Texture3D, 0, x, y, z, 1, 1, 1, PixelFormat.Red, PixelType.UnsignedByte, &val);

        // Log occasionally to avoid spam, but enough to verify activity
        if (type > 0) Console.WriteLine($"[GPU Sync] Voxel at ({x},{y},{z}) set to SOLID.");
    }

    public static unsafe void UploadChunkToVoxelTexture(GL gl, uint tex3D, Chunk chunk)
    {
        // Skip chunks that are entirely air to save transfer time
        // You would need a 'IsEmpty' flag in your Chunk class for this to be perfect

        int texX = ((chunk.ChunkX * 16 % 1024) + 1024) % 1024;
        int texZ = ((chunk.ChunkZ * 16 % 1024) + 1024) % 1024;

        byte[] data = new byte[16 * 256 * 16];

        // Cache-friendly loop order: Y is usually the tallest/outermost in memory
        for (int z = 0; z < 16; z++)
        {
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    byte blockType = chunk.Blocks[x, y, z];
                    if (blockType == 0) continue; // byte array defaults to 0 anyway

                    int index = x + (y * 16) + (z * 16 * 256);
                    data[index] = 255;
                }
            }
        }

        gl.BindTexture(TextureTarget.Texture3D, tex3D);
        fixed (byte* p = data)
        {
            gl.TexSubImage3D(TextureTarget.Texture3D, 0, texX, 0, texZ, 16, 256, 16, PixelFormat.Red, PixelType.UnsignedByte, p);
        }
    }

    public static unsafe void ClearChunkInVoxelTexture(GL gl, uint tex3D, int chunkX, int chunkZ)
    {
        int texX = ((chunkX * 16 % 1024) + 1024) % 1024;
        int texZ = ((chunkZ * 16 % 1024) + 1024) % 1024;

        // Zero out a 16x256x16 area
        byte[] empty = new byte[16 * 256 * 16];
        gl.BindTexture(TextureTarget.Texture3D, tex3D);
        fixed (byte* p = empty)
        {
            gl.TexSubImage3D(TextureTarget.Texture3D, 0, texX, 0, texZ, 16, 256, 16, PixelFormat.Red, PixelType.UnsignedByte, p);
        }
    }

    public static void ProcessUploadQueue(GL gl, uint voxelTex3D, List<RenderChunk> renderChunks, ConcurrentQueue<(Chunk chunk, (float[] opaque, float[] water) meshData, byte[] volumeData)> queue)
    {
        // If the player is moving fast, the queue grows. We scale the upload speed.
        // This prevents the "render lag" you described.
        int maxUploadsPerFrame = 1;
        if (queue.Count > 10) maxUploadsPerFrame = 3;
        if (queue.Count > 50) maxUploadsPerFrame = 8;

        for (int i = 0; i < maxUploadsPerFrame; i++)
        {
            if (queue.TryDequeue(out var data))
            {
                // Update Mesh
                FinalizeGPUUpload(gl, renderChunks, data.chunk, data.meshData);

                // Update Shadow Volume
                int texX = ((data.chunk.ChunkX * 16 % 1024) + 1024) % 1024;
                int texZ = ((data.chunk.ChunkZ * 16 % 1024) + 1024) % 1024;

                gl.BindTexture(TextureTarget.Texture3D, voxelTex3D);
                unsafe
                {
                    fixed (byte* p = data.volumeData)
                    {
                        gl.TexSubImage3D(TextureTarget.Texture3D, 0, texX, 0, texZ, 16, 256, 16, PixelFormat.Red, PixelType.UnsignedByte, p);
                    }
                }
                data.chunk.IsDirty = false;
            }
            else break;
        }
    }








}

