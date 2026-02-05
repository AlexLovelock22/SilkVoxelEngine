using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using System.Drawing;
using System.Numerics;
using VoxelEngine_Silk.Net_1._0.World;

namespace VoxelEngine_Silk.Net_1._0;

// Helper class to bundle a chunk's GPU data together
class RenderChunk
{
    public uint VAO;
    public uint VBO;
    public uint VertexCount;
    public Vector3 WorldPosition; // The position in the world to draw this chunk
}

class Program
{
    private static IWindow window = null!;
    private static GL Gl = null!;
    private static IInputContext Input = null!;
    private static uint Shader;

    // We now store a list of chunks to render instead of just one
    private static List<RenderChunk> _renderChunks = new List<RenderChunk>();

    // Camera
    private static Vector3 CameraPosition = new Vector3(8, 20, 30);
    private static Vector3 CameraFront = new Vector3(0, 0, -1);
    private static Vector3 CameraUp = Vector3.UnitY;
    private static float CameraYaw = -90f;
    private static float CameraPitch = 0f;
    private static Vector2 LastMousePos;

    // Performance/Debug
    private static double _timePassed;
    private static int _frameCount;
    private static uint _totalVertexCount;
    private static VoxelWorld voxelWorld = new VoxelWorld();
    static void Main(string[] args)
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(1280, 720);
        options.Title = "Voxel Engine - Multi-Chunk View";

        window = Window.Create(options);
        window.Load += OnLoad;
        window.Update += OnUpdate;
        window.Render += OnRender;
        window.Run();
    }
    private static unsafe void OnLoad()
    {
        Gl = GL.GetApi(window);
        Gl.Enable(EnableCap.DepthTest);
        Gl.ClearColor(0.4f, 0.6f, 0.9f, 1.0f);

        // PHASE 1: Create all chunk data (No GPU stuff here)
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                // Pass 'voxelWorld' so the chunk knows where to look for neighbors
                var chunk = new Chunk(x, z, voxelWorld);
                voxelWorld.Chunks[(x, z)] = chunk;
            }
        }

        // PHASE 2: Generate Meshes and Upload to GPU
        // Now that neighbors exist, Chunk.GetVertexData() can see across borders!
        foreach (var chunk in voxelWorld.Chunks.Values)
        {
            UploadChunkToGPU(chunk);
        }

        // 2. Shader Setup (Stays largely the same)
        string vertexCode = File.ReadAllText("shader.vert");
        string fragmentCode = File.ReadAllText("shader.frag");
        uint vs = CompileShader(ShaderType.VertexShader, vertexCode);
        uint fs = CompileShader(ShaderType.FragmentShader, fragmentCode);
        Shader = Gl.CreateProgram();
        Gl.AttachShader(Shader, vs);
        Gl.AttachShader(Shader, fs);
        Gl.LinkProgram(Shader);
        Gl.DeleteShader(vs);
        Gl.DeleteShader(fs);

        // 3. Input Setup
        Input = window.CreateInput();
        foreach (var mouse in Input.Mice)
        {
            mouse.Cursor.CursorMode = CursorMode.Raw;
        }
        Input.Mice[0].MouseMove += OnMouseMove;
        LastMousePos = Input.Mice[0].Position;
    }

    private static unsafe void UploadChunkToGPU(Chunk chunk)
    {
        float[] vertices = chunk.GetVertexData();

        RenderChunk rc = new RenderChunk();
        rc.VertexCount = (uint)(vertices.Length / 6);
        rc.WorldPosition = new Vector3(chunk.ChunkX * Chunk.Size, 0, chunk.ChunkZ * Chunk.Size);
        _totalVertexCount += rc.VertexCount;

        rc.VAO = Gl.GenVertexArray();
        Gl.BindVertexArray(rc.VAO);

        rc.VBO = Gl.GenBuffer();
        Gl.BindBuffer(BufferTargetARB.ArrayBuffer, rc.VBO);
        fixed (void* v = vertices)
        {
            Gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
        }

        uint stride = 6 * sizeof(float);
        Gl.VertexAttribPointer(0, 3, (GLEnum)VertexAttribType.Float, false, stride, (void*)0);
        Gl.EnableVertexAttribArray(0);
        Gl.VertexAttribPointer(1, 3, (GLEnum)VertexAttribType.Float, false, stride, (void*)(3 * sizeof(float)));
        Gl.EnableVertexAttribArray(1);

        _renderChunks.Add(rc);
    }

    // Helper method to create a chunk, generate mesh, and upload to GPU
    

    private static void OnRender(double deltaTime)
    {
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        Gl.UseProgram(Shader);

        var view = Matrix4x4.CreateLookAt(CameraPosition, CameraPosition + CameraFront, CameraUp);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(45.0f * (float)Math.PI / 180.0f, 1280f / 720f, 0.1f, 1000.0f);

        SetUniformMatrix(Shader, "uView", view);
        SetUniformMatrix(Shader, "uProjection", projection);

        // 4. Render Loop: Iterate through all chunks
        foreach (var rc in _renderChunks)
        {
            // For each chunk, we translate the model matrix to its world position
            var model = Matrix4x4.CreateTranslation(rc.WorldPosition);
            SetUniformMatrix(Shader, "uModel", model);

            Gl.BindVertexArray(rc.VAO);
            Gl.DrawArrays(PrimitiveType.Triangles, 0, rc.VertexCount);
        }
    }

    private static unsafe void SetUniformMatrix(uint shader, string name, Matrix4x4 matrix)
    {
        int location = Gl.GetUniformLocation(shader, name);
        if (location != -1)
            Gl.UniformMatrix4(location, 1, false, (float*)&matrix);
    }

    private static uint CompileShader(ShaderType type, string code)
    {
        uint handle = Gl.CreateShader(type);
        Gl.ShaderSource(handle, code);
        Gl.CompileShader(handle);
        string infoLog = Gl.GetShaderInfoLog(handle);
        if (!string.IsNullOrWhiteSpace(infoLog))
            Console.WriteLine($"Error compiling {type}: {infoLog}");
        return handle;
    }

    private static void OnUpdate(double deltaTime)
    {
        _timePassed += deltaTime;
        _frameCount++;

        if (_timePassed >= 1.0)
        {
            double fps = _frameCount / _timePassed;
            window.Title = $"Voxel Engine | FPS: {fps:F1} | Total Vertices: {_totalVertexCount:N0}";
            _timePassed = 0;
            _frameCount = 0;
        }

        var keyboard = Input.Keyboards[0];
        float speed = 20f * (float)deltaTime; // Increased speed for the larger world

        if (keyboard.IsKeyPressed(Key.W)) CameraPosition += speed * CameraFront;
        if (keyboard.IsKeyPressed(Key.S)) CameraPosition -= speed * CameraFront;
        if (keyboard.IsKeyPressed(Key.A)) CameraPosition -= speed * Vector3.Normalize(Vector3.Cross(CameraFront, CameraUp));
        if (keyboard.IsKeyPressed(Key.D)) CameraPosition += speed * Vector3.Normalize(Vector3.Cross(CameraFront, CameraUp));
        if (keyboard.IsKeyPressed(Key.Space)) CameraPosition += speed * CameraUp;
        if (keyboard.IsKeyPressed(Key.ShiftLeft)) CameraPosition -= speed * CameraUp;

        if (keyboard.IsKeyPressed(Key.Escape)) window.Close();
    }

    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        var lookSensitivity = 0.1f;
        float xOffset = (position.X - LastMousePos.X) * lookSensitivity;
        float yOffset = (position.Y - LastMousePos.Y) * lookSensitivity;
        LastMousePos = position;

        CameraYaw += xOffset;
        CameraPitch -= yOffset;

        if (CameraPitch > 89.0f) CameraPitch = 89.0f;
        if (CameraPitch < -89.0f) CameraPitch = -89.0f;

        float yawRad = CameraYaw * (float)Math.PI / 180.0f;
        float pitchRad = CameraPitch * (float)Math.PI / 180.0f;

        Vector3 direction;
        direction.X = (float)Math.Cos(yawRad) * (float)Math.Cos(pitchRad);
        direction.Y = (float)Math.Sin(pitchRad);
        direction.Z = (float)Math.Sin(yawRad) * (float)Math.Cos(pitchRad);

        CameraFront = Vector3.Normalize(direction);
    }
}