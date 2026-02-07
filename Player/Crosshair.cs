using Silk.NET.OpenGL;

public class Crosshair
{
    private uint _vao;
    private uint _vbo;
    private GL _gl;

    public Crosshair(GL gl)
    {
        _gl = gl;

        // Two lines forming a '+' centered at 0,0
        // Scale is 0.02 (2% of screen size)
        float s = 0.02f;
        float[] vertices = {
            -s, 0,  s, 0, // Horizontal line
             0,-s,  0, s  // Vertical line
        };

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        
        unsafe {
            fixed (void* d = vertices)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (uint)(vertices.Length * sizeof(float)), d, BufferUsageARB.StaticDraw);
            
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        }
        _gl.EnableVertexAttribArray(0);
    }

    public void Render(uint shader)
    {
        _gl.UseProgram(shader);
        _gl.BindVertexArray(_vao);
        _gl.LineWidth(2.0f);
        _gl.DrawArrays(PrimitiveType.Lines, 0, 4);
    }
}