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

    public static unsafe void FinalizeGPUUpload(GL gl, List<RenderChunk> renderChunks, Chunk chunk, (float[] opaque, float[] water) meshData, VoxelWorld world)
    {
        // CRITICAL CHECK: If the chunk was removed from the world while it was in the upload queue, 
        // discard the mesh now so it doesn't become a "Ghost Chunk".
        if (!world.Chunks.ContainsKey((chunk.ChunkX, chunk.ChunkZ)))
        {
            Console.WriteLine($"[MainThread]: Discarded Upload for ({chunk.ChunkX}, {chunk.ChunkZ}) - Chunk no longer in world.");
            return;
        }

        lock (renderChunks)
        {
            RenderChunk? rc = renderChunks.FirstOrDefault(c =>
                (int)Math.Floor(c.WorldPosition.X / 16.0f) == chunk.ChunkX &&
                (int)Math.Floor(c.WorldPosition.Z / 16.0f) == chunk.ChunkZ);

            if (rc == null)
            {
                rc = new RenderChunk { WorldPosition = new Vector3(chunk.ChunkX * 16, 0, chunk.ChunkZ * 16) };
                renderChunks.Add(rc);
                Console.WriteLine($"[MainThread]: Created New Mesh ({chunk.ChunkX}, {chunk.ChunkZ}) | Total Meshes: {renderChunks.Count}");
            }

            UploadToVAO(ref rc.OpaqueVAO, ref rc.OpaqueVBO, meshData.opaque, out rc.OpaqueVertexCount, gl);
            UploadToVAO(ref rc.WaterVAO, ref rc.WaterVBO, meshData.water, out rc.WaterVertexCount, gl);
        }
    }

    public static void DeleteChunkMesh(GL gl, RenderChunk rc)
    {
        if (rc.OpaqueVAO != 0) gl.DeleteVertexArray(rc.OpaqueVAO);
        if (rc.OpaqueVBO != 0) gl.DeleteBuffer(rc.OpaqueVBO);
        if (rc.WaterVAO != 0) gl.DeleteVertexArray(rc.WaterVAO);
        if (rc.WaterVBO != 0) gl.DeleteBuffer(rc.WaterVBO);
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

    public static void ProcessUploadQueue(GL gl, uint voxelTex3D, List<RenderChunk> renderChunks, ConcurrentQueue<(Chunk chunk, (float[] opaque, float[] water) meshData, byte[] volumeData)> queue, VoxelWorld world)
    {
        int maxUploadsPerFrame = (queue.Count > 50) ? 8 : (queue.Count > 10 ? 3 : 1);

        for (int i = 0; i < maxUploadsPerFrame; i++)
        {
            if (queue.TryDequeue(out var data))
            {
                // Verify chunk still exists before doing GPU work
                if (!world.Chunks.ContainsKey((data.chunk.ChunkX, data.chunk.ChunkZ))) continue;

                FinalizeGPUUpload(gl, renderChunks, data.chunk, data.meshData, world);

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

