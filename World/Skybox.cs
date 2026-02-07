using System.Numerics;
using Silk.NET.OpenGL;
public unsafe class Skybox
{
    private uint _vao, _vbo;
    private GL _gl;
    private static float[] _cubeVertices = {
        -1f,  1f, -1f, -1f, -1f, -1f,  1f, -1f, -1f,  1f, -1f, -1f,  1f,  1f, -1f, -1f,  1f, -1f,
        -1f, -1f,  1f, -1f, -1f, -1f, -1f,  1f, -1f, -1f,  1f, -1f, -1f,  1f,  1f, -1f, -1f,  1f,
         1f, -1f, -1f,  1f, -1f,  1f,  1f,  1f,  1f,  1f,  1f,  1f,  1f,  1f, -1f,  1f, -1f, -1f,
        -1f, -1f,  1f, -1f,  1f,  1f,  1f,  1f,  1f,  1f,  1f,  1f,  1f, -1f,  1f, -1f, -1f,  1f,
        -1f,  1f, -1f,  1f,  1f, -1f,  1f,  1f,  1f,  1f,  1f,  1f, -1f,  1f,  1f, -1f,  1f, -1f,
        -1f, -1f, -1f, -1f, -1f,  1f,  1f, -1f, -1f,  1f, -1f, -1f, -1f, -1f,  1f,  1f, -1f,  1f
    };

    public Skybox(GL gl) {
        _gl = gl;
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        fixed (float* v = _cubeVertices)
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_cubeVertices.Length * sizeof(float)), v, GLEnum.StaticDraw);
        _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, 3 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);
    }

    public void Render(uint shader, Matrix4x4 view, Matrix4x4 projection, Vector3 sunDir) {
        _gl.DepthFunc(GLEnum.Lequal); // Skybox passes depth at 1.0
        _gl.UseProgram(shader);
        
        int viewLoc = _gl.GetUniformLocation(shader, "uView");
        int projLoc = _gl.GetUniformLocation(shader, "uProjection");
        int sunLoc = _gl.GetUniformLocation(shader, "uSunDir");

        _gl.UniformMatrix4(viewLoc, 1, false, (float*)&view);
        _gl.UniformMatrix4(projLoc, 1, false, (float*)&projection);
        _gl.Uniform3(sunLoc, sunDir.X, sunDir.Y, sunDir.Z);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(GLEnum.Triangles, 0, 36);
        _gl.DepthFunc(GLEnum.Less);
    }
}