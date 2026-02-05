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
    // Store chunk coordinates that need to be deleted from the GPU
    private static ConcurrentQueue<(int x, int z)> _unloadQueue = new();
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
        options.Size = new Vector2D<int>(1920, 1080);
        options.Title = "Voxel Engine - Multi-Chunk View";

        options.VSync = true;
        
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
        const int viewDistance = 40;

        while (true)
        {
            int pCX = (int)Math.Floor(CameraPosition.X / 16.0f);
            int pCZ = (int)Math.Floor(CameraPosition.Z / 16.0f);

            // 1. CREATE A "VALID" SET
            // We define exactly which chunks are allowed to exist right now
            HashSet<(int, int)> visibleCoords = new HashSet<(int, int)>();

            int x = 0, z = 0;
            int dx = 0, dz = -1;
            int sideLength = viewDistance * 2;
            int maxChunks = sideLength * sideLength;

            for (int i = 0; i < maxChunks; i++)
            {
                int currentX = pCX + x;
                int currentZ = pCZ + z;

                float dist = Vector2.Distance(new Vector2(currentX, currentZ), new Vector2(pCX, pCZ));
                if (dist <= viewDistance)
                {
                    visibleCoords.Add((currentX, currentZ));
                }

                // Spiral math
                if (x == z || (x < 0 && x == -z) || (x > 0 && x == 1 - z))
                {
                    int temp = dx; dx = -dz; dz = temp;
                }
                x += dx; z += dz;
            }

            // 2. THE TOTAL PURGE (Unload Pass)
            // If a chunk is in our world dictionary but NOT in our visible set, kill it.
            // This is much more reliable than a simple distance check while moving fast.
            foreach (var coord in voxelWorld.Chunks.Keys)
            {
                if (!visibleCoords.Contains(coord))
                {
                    if (voxelWorld.Chunks.TryRemove(coord, out _))
                    {
                        _unloadQueue.Enqueue(coord);
                    }
                }
            }

            // 3. LOAD PASS
            // Now we only load what's missing from the visible set
            // 3. LOAD PASS
            foreach (var coord in visibleCoords)
            {
                if (!voxelWorld.Chunks.ContainsKey(coord))
                {
                    var chunk = new Chunk(coord.Item1, coord.Item2, voxelWorld);

                    if (voxelWorld.Chunks.TryAdd(coord, chunk))
                    {
                        // Get neighbors once
                        var n = voxelWorld.GetNeighbors(coord.Item1, coord.Item2);

                        // OPTIMIZATION 1: Only mesh the NEW chunk if it has neighbors.
                        // If neighbors aren't loaded yet, the faces will be wrong anyway.
                        // When those neighbors eventually load, they will trigger this chunk to mesh.
                        if (n.r != null && n.l != null && n.f != null && n.b != null)
                        {
                            QueueChunkForMeshing(chunk);
                        }

                        // OPTIMIZATION 2: Only re-mesh neighbors if they AREN'T currently pending.
                        // This prevents the same neighbor from being meshed 4 times in a row 
                        // as you move past a group of new chunks.
                        if (n.r != null) QueueChunkForMeshing(n.r);
                        if (n.l != null) QueueChunkForMeshing(n.l);
                        if (n.f != null) QueueChunkForMeshing(n.f);
                        if (n.b != null) QueueChunkForMeshing(n.b);
                    }

                    // Keep it at 0 or 1 to prevent thread starvation
                    Thread.Sleep(0);
                }
            }

            Thread.Sleep(200); // Run the whole check 5 times per second
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
        // 1. Replacement Logic
        // If this chunk already exists in the render list, we MUST delete it first.
        // This happens frequently when neighbors load and trigger a re-mesh.
        lock (_renderChunks)
        {
            var existing = _renderChunks.Find(rc =>
                (int)Math.Floor(rc.WorldPosition.X / 16.0f) == chunk.ChunkX &&
                (int)Math.Floor(rc.WorldPosition.Z / 16.0f) == chunk.ChunkZ);

            if (existing != null)
            {
                Gl.DeleteVertexArray(existing.VAO);
                Gl.DeleteBuffer(existing.VBO);
                _totalVertexCount -= existing.VertexCount;
                _renderChunks.Remove(existing);
            }
        }

        // 2. Create the new RenderChunk object
        RenderChunk rc = new RenderChunk();
        rc.VertexCount = (uint)(vertices.Length / 6);
        rc.WorldPosition = new Vector3(chunk.ChunkX * Chunk.Size, 0, chunk.ChunkZ * Chunk.Size);

        // Update global counter
        _totalVertexCount += rc.VertexCount;

        // 3. GPU Buffer Allocation
        rc.VAO = Gl.GenVertexArray();
        Gl.BindVertexArray(rc.VAO);

        rc.VBO = Gl.GenBuffer();
        Gl.BindBuffer(BufferTargetARB.ArrayBuffer, rc.VBO);

        // Upload vertex data to the GPU
        fixed (void* v = vertices)
        {
            Gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
        }

        uint stride = 6 * sizeof(float);
        // Attribute 0: Position (x, y, z)
        Gl.VertexAttribPointer(0, 3, (GLEnum)VertexAttribType.Float, false, stride, (void*)0);
        Gl.EnableVertexAttribArray(0);

        // Attribute 1: Color (r, g, b)
        Gl.VertexAttribPointer(1, 3, (GLEnum)VertexAttribType.Float, false, stride, (void*)(3 * sizeof(float)));
        Gl.EnableVertexAttribArray(1);

        // 4. Finalize
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
        // Use actual window size for aspect ratio
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(70.0f * (float)Math.PI / 180.0f, (float)window.Size.X / window.Size.Y, 0.1f, 2000.0f);

        SetUniformMatrix(Shader, "uView", view);
        SetUniformMatrix(Shader, "uProjection", projection);

        _totalVertexCount = 0;

        // We can't use 'lock' easily here without stuttering, 
        // but the streamer is handling the list safely.
        var chunksToDraw = _renderChunks.ToArray();

        foreach (var rc in chunksToDraw)
        {
            // 1. DIRECTIONAL CULLING ONLY
            // We removed the Distance check because the streamer loop handles that now.
            Vector3 toChunk = Vector3.Normalize(rc.WorldPosition - CameraPosition);
            float dot = Vector3.Dot(CameraFront, toChunk);

            // Keep a wide enough FOV so chunks don't pop in at the corners
            if (dot < -0.3f) continue;

            // 2. DRAWING
            var model = Matrix4x4.CreateTranslation(rc.WorldPosition);
            SetUniformMatrix(Shader, "uModel", model);

            Gl.BindVertexArray(rc.VAO);
            Gl.DrawArrays(PrimitiveType.Triangles, 0, rc.VertexCount);

            _totalVertexCount += rc.VertexCount;
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
        // 1. GPU UNLOAD LOGIC: Total Drain
        // Process the entire queue every frame to keep up with high-speed movement
        while (_unloadQueue.TryDequeue(out var coords))
        {
            lock (_renderChunks)
            {
                // Use exact coordinate matching
                var existing = _renderChunks.Find(rc =>
                    (int)Math.Floor(rc.WorldPosition.X / 16.0f) == coords.x &&
                    (int)Math.Floor(rc.WorldPosition.Z / 16.0f) == coords.z);

                if (existing != null)
                {
                    Gl.DeleteVertexArray(existing.VAO);
                    Gl.DeleteBuffer(existing.VBO);
                    _totalVertexCount -= existing.VertexCount;
                    _renderChunks.Remove(existing);
                }
            }
        }

        // 2. GPU UPLOAD LOGIC: Adaptive Burst
        int uploadLimit = _uploadQueue.Count > 100 ? 50 : 5; // Higher base limit for faster movement
        for (int i = 0; i < uploadLimit; i++)
        {
            if (_uploadQueue.TryDequeue(out var data))
            {
                FinalizeGPUUpload(data.chunk, data.vertices);
            }
            else break;
        }

        // 3. Stats & FPS
        _timePassed += deltaTime;
        _frameCount++;
        if (_timePassed >= 1.0)
        {
            double fps = _frameCount / _timePassed;
            window.Title = $"Voxel Engine | Chunks: {_renderChunks.Count} | Verts: {_totalVertexCount:N0} | FPS: {fps:F0} | Pending: {_uploadQueue.Count}";
            _timePassed = 0;
            _frameCount = 0;
        }

        // 4. Movement Logic
        var keyboard = Input.Keyboards[0];
        float speed = 60f * (float)deltaTime; // 60 units per second

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