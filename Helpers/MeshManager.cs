using Silk.NET.OpenGL;
using System.Collections.Concurrent;
using System.Numerics;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0.Helpers;

public static class MeshManager
{
    public static unsafe void UploadToVAO(ref uint vao, ref uint vbo, float[] data, out uint vertexCount, GL gl)
    {
        // Updated: 11 floats per vertex (3 pos + 3 col + 2 uv + 3 normal)
        vertexCount = (uint)(data.Length / 11);
        if (vertexCount == 0) return;

        if (vao == 0) vao = gl.GenVertexArray();
        if (vbo == 0) vbo = gl.GenBuffer();

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        fixed (float* d = data)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), d, BufferUsageARB.StaticDraw);

        int stride = 11 * sizeof(float);

        // Position (Location 0)
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)0);
        gl.EnableVertexAttribArray(0);

        // Color (Location 1)
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);

        // UV (Location 2)
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(2);

        // Normal (Location 3) - New!
        gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)(8 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
    }


    // In MeshManager.cs
    public static unsafe void FinalizeGPUUpload(
        GL gl,
        List<RenderChunk> renderChunks,
        Chunk chunk,
        (float[] opaque, float[] water) meshData)
    {
        // 1. Search the list passed into the method instead of a global variable
        RenderChunk? rc = renderChunks.FirstOrDefault(c =>
            c.WorldPosition == new Vector3(chunk.ChunkX * 16, 0, chunk.ChunkZ * 16));

        if (rc == null)
        {
            rc = new RenderChunk { WorldPosition = new Vector3(chunk.ChunkX * 16, 0, chunk.ChunkZ * 16) };
            renderChunks.Add(rc);
        }

        // 2. Upload the data layers
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
    public static void ProcessUploadQueue(GL gl, uint shadowTex, VoxelWorld world, List<RenderChunk> renderChunks, ConcurrentQueue<(Chunk chunk, (float[] opaque, float[] water) meshData)> queue)
    {
        // Limit uploads per frame to prevent stuttering
        int uploadLimit = queue.Count > 100 ? 50 : 10;

        for (int i = 0; i < uploadLimit; i++)
        {
            if (queue.TryDequeue(out var data))
            {
                // 1. Upload the new 3D geometry
                FinalizeGPUUpload(gl, renderChunks, data.chunk, data.meshData);

                // 2. Upload the new 2D heightmap data to the GPU texture
                // This is what prints your console logs and updates the shadows
                UpdateHeightmapRegion(gl, shadowTex, data.chunk);

                // 3. Clear the dirty flag as the GPU is now in sync
                data.chunk.IsDirty = false;
            }
            else break;
        }
    }

    // Helper to update the specific 16x16 area of the global heightmap
    public static unsafe void UpdateHeightmapRegion(GL gl, uint tex, Chunk chunk)
    {
        gl.BindTexture(TextureTarget.Texture2D, tex);

        int worldStartX = chunk.ChunkX * Chunk.Size;
        int worldStartZ = chunk.ChunkZ * Chunk.Size;

        // Mapping logic
        int mapX = ((worldStartX + 512) % 1024 + 1024) % 1024;
        int mapZ = ((worldStartZ + 512) % 1024 + 1024) % 1024;

        int[,] localMap = chunk.GetHeightMap();
        byte[] slice = new byte[Chunk.Size * Chunk.Size];

        // Tracking for logging
        int minH = 255;
        int maxH = 0;
        long totalH = 0;

        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                byte val = (byte)localMap[x, z];
                slice[z * Chunk.Size + x] = val;

                // Stats for logging
                if (val < minH) minH = val;
                if (val > maxH) maxH = val;
                totalH += val;
            }
        }

        // --- ENHANCED LOGGING ---
        // This will print the average height of the chunk. 
        // If you mine a block, you should see 'Avg' or 'Max' decrease in the console.
        float avg = totalH / (float)(Chunk.Size * Chunk.Size);
        Console.WriteLine($"[Heightmap Update] Chunk({chunk.ChunkX},{chunk.ChunkZ}) " +
                          $"at Tex({mapX},{mapZ}) | Min:{minH} Max:{maxH} Avg:{avg:F2}");

        fixed (byte* p = slice)
        {
            gl.TexSubImage2D(TextureTarget.Texture2D, 0, mapX, mapZ,
                             (uint)Chunk.Size, (uint)Chunk.Size,
                             PixelFormat.Red, PixelType.UnsignedByte, p);
        }
    }

    public static unsafe uint CreateGlobalHeightmap(GL gl)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);

        // Initialize 1024x1024 R8 texture (1 byte per pixel)
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, 1024, 1024, 0, PixelFormat.Red, PixelType.UnsignedByte, null);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);

        // Repeat is critical for the "Infinite Wrapping" logic
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);

        return tex;
    }






}

