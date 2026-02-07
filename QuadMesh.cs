using Silk.NET.OpenGL;
using System;

namespace VoxelEngine_Silk.Net_1._0;

public class QuadMesh
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;

    // Added 'unsafe' keyword here to allow pointers
    public unsafe QuadMesh(GL gl)
    {
        _gl = gl;

        float[] vertices = 
        {
            // Position (X, Y, Z)    // UV (U, V)
            -0.5f,  0.5f, 0.0f,      0.0f, 1.0f,
            -0.5f, -0.5f, 0.0f,      0.0f, 0.0f,
             0.5f, -0.5f, 0.0f,      1.0f, 0.0f,

            -0.5f,  0.5f, 0.0f,      0.0f, 1.0f,
             0.5f, -0.5f, 0.0f,      1.0f, 0.0f,
             0.5f,  0.5f, 0.0f,      1.0f, 1.0f  
        };

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        fixed (float* v = vertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
        }

        // Location 0: Position (3 floats)
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);

        // Location 1: UVs (2 floats, starting after 3 floats of position)
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);

        _gl.BindVertexArray(0);
    }

    public void Render()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }
}