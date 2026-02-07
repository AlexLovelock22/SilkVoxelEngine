using Silk.NET.OpenGL;
using System.Numerics;

public class SelectionBox
{
    private uint _vao;
    private uint _vbo;
    private GL _gl;

    public SelectionBox(GL gl)
    {
        _gl = gl;

        float s = 1.002f; 
        float[] vertices = {
            0,0,0, s,0,0,  0,s,0, s,s,0,  0,0,s, s,0,s,  0,s,s, s,s,s, // X
            0,0,0, 0,s,0,  s,0,0, s,s,0,  0,0,s, 0,s,s,  s,0,s, s,s,s, // Y
            0,0,0, 0,0,s,  s,0,0, s,0,s,  0,s,0, 0,s,s,  s,s,0, s,s,s  // Z
        };

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        
        unsafe {
            fixed (void* data = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (uint)(vertices.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
            }
            
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        }
        _gl.EnableVertexAttribArray(0);
    }

    public void Render(uint shader, Vector3 blockPos, Matrix4x4 view, Matrix4x4 projection)
    {
        _gl.UseProgram(shader);
        
        Matrix4x4 model = Matrix4x4.CreateTranslation(blockPos - new Vector3(0.001f));
        
        SetUniform(shader, "uView", view);
        SetUniform(shader, "uProjection", projection);
        SetUniform(shader, "uModel", model);

        _gl.BindVertexArray(_vao);
        _gl.LineWidth(2.0f); 
        _gl.DrawArrays(PrimitiveType.Lines, 0, 24);
    }

    // Helper to send Matrix4x4 to the shader
    private void SetUniform(uint shader, string name, Matrix4x4 matrix)
    {
        int location = _gl.GetUniformLocation(shader, name);
        unsafe {
            _gl.UniformMatrix4(location, 1, false, (float*)&matrix);
        }
    }
}