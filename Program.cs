using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using System.Drawing;
using System.Numerics;
using System.Collections.Concurrent;
using VoxelEngine_Silk.Net_1._0.World;
using System.Diagnostics;


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
    private static ConcurrentQueue<(Chunk chunk, float[] vertices)> _uploadQueue = new();
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

        Stopwatch totalSw = Stopwatch.StartNew();


        // PHASE 2: Background Streaming Logic
        // This starts a permanent loop that handles the 300x300 area
        Task.Run(() => WorldStreamerLoop());

        // Shader & Input Setup
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

        Input = window.CreateInput();
        foreach (var mouse in Input.Mice) mouse.Cursor.CursorMode = CursorMode.Raw;
        Input.Mice[0].MouseMove += OnMouseMove;
        LastMousePos = Input.Mice[0].Position;

        Console.WriteLine($"[Perf] Engine Initialized in {totalSw.ElapsedMilliseconds}ms. Streaming started.");
    }

    private static void WorldStreamerLoop()
    {
        const int renderDistance = 150;

        while (true)
        {
            int pCX = (int)Math.Floor(CameraPosition.X / 16.0f);
            int pCZ = (int)Math.Floor(CameraPosition.Z / 16.0f);

            int x = 0, z = 0;
            int dx = 0, dz = -1;
            int sideLength = renderDistance * 2;
            int maxChunks = sideLength * sideLength;

            for (int i = 0; i < maxChunks; i++)
            {
                int currentX = pCX + x;
                int currentZ = pCZ + z;

                // If chunk doesn't exist, create it and notify neighbors
                if (!voxelWorld.Chunks.ContainsKey((currentX, currentZ)))
                {
                    var chunk = new Chunk(currentX, currentZ, voxelWorld);

                    if (voxelWorld.Chunks.TryAdd((currentX, currentZ), chunk))
                    {
                        // 1. Mesh the new chunk
                        QueueChunkForMeshing(chunk);

                        // 2. Mesh neighbors (if they exist) to hide the newly touching borders
                        var n = voxelWorld.GetNeighbors(currentX, currentZ);
                        if (n.r != null) QueueChunkForMeshing(n.r);
                        if (n.l != null) QueueChunkForMeshing(n.l);
                        if (n.f != null) QueueChunkForMeshing(n.f);
                        if (n.b != null) QueueChunkForMeshing(n.b);
                    }

                    if (i % 10 == 0) Thread.Sleep(1);
                }

                // Spiral math logic
                if (x == z || (x < 0 && x == -z) || (x > 0 && x == 1 - z))
                {
                    int temp = dx;
                    dx = -dz;
                    dz = temp;
                }
                x += dx;
                z += dz;
            }

            Thread.Sleep(2000);
        }
    }


    private static void QueueChunkForMeshing(Chunk chunk)
    {
        Task.Run(() =>
        {
            // Get the latest neighbor references from the world
            var n = voxelWorld.GetNeighbors(chunk.ChunkX, chunk.ChunkZ);

            // Generate the vertex data with neighbor awareness
            float[] vertices = chunk.GetVertexData(n.r, n.l, n.f, n.b);

            // Send to the main thread for GPU upload
            _uploadQueue.Enqueue((chunk, vertices));
        });
    }


    private static unsafe void FinalizeGPUUpload(Chunk chunk, float[] vertices)
    {
        // 1. Check if we already have a render object for this specific chunk coordinate
        // Vector3 comparison is used to find the chunk at the exact world position
        lock (_renderChunks) // Safety lock for list manipulation
        {
            var existing = _renderChunks.Find(rc =>
                rc.WorldPosition.X == chunk.ChunkX * Chunk.Size &&
                rc.WorldPosition.Z == chunk.ChunkZ * Chunk.Size);

            if (existing != null)
            {
                // Clean up old GPU memory to prevent a leak
                Gl.DeleteVertexArray(existing.VAO);
                Gl.DeleteBuffer(existing.VBO);
                _totalVertexCount -= existing.VertexCount;
                _renderChunks.Remove(existing);
            }
        }

        // 2. Create the new RenderChunk
        RenderChunk rc = new RenderChunk();
        rc.VertexCount = (uint)(vertices.Length / 6);
        rc.WorldPosition = new Vector3(chunk.ChunkX * Chunk.Size, 0, chunk.ChunkZ * Chunk.Size);
        _totalVertexCount += rc.VertexCount;

        // 3. GPU Buffer Allocation
        rc.VAO = Gl.GenVertexArray();
        Gl.BindVertexArray(rc.VAO);

        rc.VBO = Gl.GenBuffer();
        Gl.BindBuffer(BufferTargetARB.ArrayBuffer, rc.VBO);

        fixed (void* v = vertices)
        {
            Gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
        }

        uint stride = 6 * sizeof(float);
        // Position (Location 0)
        Gl.VertexAttribPointer(0, 3, (GLEnum)VertexAttribType.Float, false, stride, (void*)0);
        Gl.EnableVertexAttribArray(0);
        // Color (Location 1)
        Gl.VertexAttribPointer(1, 3, (GLEnum)VertexAttribType.Float, false, stride, (void*)(3 * sizeof(float)));
        Gl.EnableVertexAttribArray(1);

        lock (_renderChunks)
        {
            _renderChunks.Add(rc);
        }
    }
    
    // Helper method to handle 'await' outside of the 'unsafe' OnLoad context
    private static void StartPerformanceMonitor(Stopwatch totalSw, Stopwatch phase2Sw, int totalChunks, Func<int> getProgress)
    {
        Task.Run(async () =>
        {
            while (getProgress() < totalChunks)
            {
                await Task.Delay(100);
            }
            phase2Sw.Stop();
            Console.WriteLine($"[Perf] Phase 2 (Meshing) took: {phase2Sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"[Perf] TOTAL SYSTEM STARTUP: {totalSw.ElapsedMilliseconds}ms");
        });
    }





    // Helper method to create a chunk, generate mesh, and upload to GPU


    private static void OnRender(double deltaTime)
    {
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        Gl.UseProgram(Shader);

        var view = Matrix4x4.CreateLookAt(CameraPosition, CameraPosition + CameraFront, CameraUp);
        // Note: increased far plane to 2000 to match your large world
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(45.0f * (float)Math.PI / 180.0f, 1280f / 720f, 0.1f, 2000.0f);

        SetUniformMatrix(Shader, "uView", view);
        SetUniformMatrix(Shader, "uProjection", projection);

        int chunksDrawn = 0;
        _totalVertexCount = 0; // Reset this so the title reflects what's actually on screen

        foreach (var rc in _renderChunks)
        {
            // 1. DISTANCE CULLING
            // Skip chunks that are too far away. 
            // 1000.0f is a good balance for a 300x300 world.
            float distSq = Vector3.DistanceSquared(CameraPosition, rc.WorldPosition);
            if (distSq > 1000.0f * 1000.0f) continue;

            // 2. FRUSTUM CULLING (Directional Check)
            // Only draw if the chunk is roughly in front of the camera.
            // Dot product > 0 means the chunk is within a 180-degree field in front of us.
            Vector3 toChunk = Vector3.Normalize(rc.WorldPosition - CameraPosition);
            float dot = Vector3.Dot(CameraFront, toChunk);

            // If the dot product is low, it's behind or way to the side. 
            // We use -0.2 to allow a bit of "peripheral vision" so chunks don't pop out at the edges.
            if (dot < -0.2f && distSq > 50.0f * 50.0f) continue;

            // 3. DRAWING
            var model = Matrix4x4.CreateTranslation(rc.WorldPosition);
            SetUniformMatrix(Shader, "uModel", model);

            Gl.BindVertexArray(rc.VAO);
            Gl.DrawArrays(PrimitiveType.Triangles, 0, rc.VertexCount);

            chunksDrawn++;
            _totalVertexCount += (uint)rc.VertexCount;
        }

        // Optional: Update window title with chunks culled
        // window.Title = $"Drawn: {chunksDrawn} / Total: {_renderChunks.Count}";
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
        // GPU UPLOAD LOGIC: Adaptive Burst
        // If we have thousands of chunks waiting, upload in massive bursts
        // If we only have a few, upload slowly to maintain high FPS
        int uploadLimit = _uploadQueue.Count > 500 ? 500 : 2;

        for (int i = 0; i < uploadLimit; i++)
        {
            if (_uploadQueue.TryDequeue(out var data))
            {
                FinalizeGPUUpload(data.chunk, data.vertices);
            }
            else break; // Queue empty
        }

        // FPS Counter & Stats
        _timePassed += deltaTime;
        _frameCount++;

        if (_timePassed >= 1.0)
        {
            double fps = _frameCount / _timePassed;
            window.Title = $"Voxel Engine | FPS: {fps:F1} | Verts: {_totalVertexCount:N0} | Queue: {_uploadQueue.Count}";
            _timePassed = 0;
            _frameCount = 0;
        }

        // Movement Logic
        var keyboard = Input.Keyboards[0];
        float speed = 20f * (float)deltaTime;

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