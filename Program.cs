using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using System.Drawing;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;
using VoxelEngine_Silk.Net_1._0.Game;
using VoxelEngine_Silk.Net_1._0.World;
using System.Diagnostics;

namespace VoxelEngine_Silk.Net_1._0;

class RenderChunk
{
    // Pass 1: Solid
    public uint OpaqueVAO;
    public uint OpaqueVBO;
    public uint OpaqueVertexCount;

    // Pass 2: Water
    public uint WaterVAO;
    public uint WaterVBO;
    public uint WaterVertexCount;

    public Vector3 WorldPosition;
}

class Program
{
    private static ConcurrentQueue<(Chunk chunk, (float[] opaque, float[] water) meshData)> _uploadQueue = new();
    private static ConcurrentQueue<(int x, int z)> _unloadQueue = new();
    private static IWindow window = null!;
    private static GL Gl = null!;
    private static IInputContext Input = null!;
    private static uint Shader;
    private static List<RenderChunk> _renderChunks = new List<RenderChunk>();
    private static Player player = new Player(new Vector3(8, 100, 8));
    private static Vector3 CameraPosition => player.GetEyePosition();
    private static Vector2 LastMousePos;
    private static double _timePassed;
    private static int _frameCount;
    private static uint _totalVertexCount;
    private static Frustum _frustum = new Frustum();
    private static uint _textureAtlas;
    private static VoxelWorld voxelWorld = new VoxelWorld();

    // Selection Box:
    private static SelectionBox _selectionBox = null!;
    private static uint _selectionShader;

    // Crosshair
    private static Crosshair _crosshair = null!;
    private static uint _crosshairShader;

    // Time
    private static TimeManager _timeManager = new TimeManager();

    // Skybox
    private static Skybox _skybox = null!;
    private static uint _skyboxShader;
    private static uint _sunTexture;
    private static uint _moonTexture;
    private static uint _starTexture;
    private static uint _spriteShader;
    private static QuadMesh _quadMesh = null!;


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

        // 1. Basic GL States
        Gl.Enable(GLEnum.DepthTest);
        Gl.Enable(GLEnum.CullFace); // Recommended: Don't render the inside of blocks

        // 2. Setup Alpha Blending
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);

        Gl.ClearColor(0.4f, 0.6f, 0.9f, 1.0f);

        // 3. Texture and Shaders
        LoadTextureAtlas("terrain.png");

        // Initialize Skybox mesh
        _skybox = new Skybox(Gl);

        // Read files from root

        // In OnLoad, after initializing OpenGL:
        _sunTexture = LoadTexture("sun.png");
        _moonTexture = LoadTexture("moon.png");
        _starTexture = LoadTexture("stars.png");
        string vSource = File.ReadAllText("skybox.vert");
        string fSource = File.ReadAllText("skybox.frag");

        _spriteShader = CreateShaderProgram(File.ReadAllText("sprite.vert"), File.ReadAllText("sprite.frag"));
        _quadMesh = new QuadMesh(Gl); // A simple class that generates a 2x2 plane (-1 to 1)

        // Compile
        _skyboxShader = CreateShaderProgram(vSource, fSource);

        _selectionBox = new SelectionBox(Gl);
        _selectionShader = CompileSelectionShader();

        _crosshair = new Crosshair(Gl);
        _crosshairShader = CompileCrosshairShader();

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

        // 4. Input and Background Tasks
        Stopwatch totalSw = Stopwatch.StartNew();
        Task.Run(() => WorldStreamerLoop());

        Input = window.CreateInput();
        foreach (var mouse in Input.Mice) mouse.Cursor.CursorMode = CursorMode.Raw;
        Input.Mice[0].MouseMove += OnMouseMove;
        LastMousePos = Input.Mice[0].Position;

        Gl.Disable(EnableCap.DepthTest); // Ensure it draws over the world
        _crosshair.Render(_crosshairShader);
        Gl.Enable(EnableCap.DepthTest);

        Console.WriteLine($"[Perf] Engine Initialized in {totalSw.ElapsedMilliseconds}ms. Streaming started.");
    }

    private static uint CompileSelectionShader()
    {
        string vertCode = @"
        #version 330 core
        layout (location = 0) in vec3 aPos;
        uniform mat4 uView;
        uniform mat4 uProjection;
        uniform mat4 uModel;
        void main() {
            gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
        }";

        string fragCode = @"
        #version 330 core
        out vec4 FragColor;
        void main() {
            FragColor = vec4(0.0, 0.0, 0.0, 1.0); // Solid Black
        }";

        uint vertex = Gl.CreateShader(ShaderType.VertexShader);
        Gl.ShaderSource(vertex, vertCode);
        Gl.CompileShader(vertex);

        uint fragment = Gl.CreateShader(ShaderType.FragmentShader);
        Gl.ShaderSource(fragment, fragCode);
        Gl.CompileShader(fragment);

        uint program = Gl.CreateProgram();
        Gl.AttachShader(program, vertex);
        Gl.AttachShader(program, fragment);
        Gl.LinkProgram(program);

        // Clean up individual shaders after linking
        Gl.DeleteShader(vertex);
        Gl.DeleteShader(fragment);

        return program;
    }

    private static void WorldStreamerLoop()
    {
        const int viewDistance = 40;

        while (true)
        {
            int pCX = (int)Math.Floor(CameraPosition.X / 16.0f);
            int pCZ = (int)Math.Floor(CameraPosition.Z / 16.0f);

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

                if (x == z || (x < 0 && x == -z) || (x > 0 && x == 1 - z))
                {
                    int temp = dx; dx = -dz; dz = temp;
                }
                x += dx; z += dz;
            }

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

            foreach (var coord in visibleCoords)
            {
                if (!voxelWorld.Chunks.ContainsKey(coord))
                {
                    var chunk = new Chunk(coord.Item1, coord.Item2, voxelWorld);

                    if (voxelWorld.Chunks.TryAdd(coord, chunk))
                    {
                        var n = voxelWorld.GetNeighbors(coord.Item1, coord.Item2);
                        if (n.r != null && n.l != null && n.f != null && n.b != null)
                        {
                            QueueChunkForMeshing(chunk);
                        }


                        if (n.r != null) QueueChunkForMeshing(n.r);
                        if (n.l != null) QueueChunkForMeshing(n.l);
                        if (n.f != null) QueueChunkForMeshing(n.f);
                        if (n.b != null) QueueChunkForMeshing(n.b);
                    }

                    Thread.Sleep(0);
                }
            }

            Thread.Sleep(200);
        }
    }

    // Add this near your other static variables in Program.cs
    private static ConcurrentBag<List<float>> _meshBuffers = new();

    private static List<float> GetBufferFromPool()
    {
        if (_meshBuffers.TryTake(out var list)) return list;
        return new List<float>(16 * 16 * 128); // Pre-size to avoid internal resizing
    }


    private static void QueueChunkForMeshing(Chunk chunk)
    {
        Task.Run(() =>
        {
            // 1. Call the new version of FillVertexData that returns (float[] opaque, float[] water)
            var meshData = chunk.FillVertexData();

            // 2. Enqueue the tuple. This matches the new ConcurrentQueue definition.
            // It passes (chunk, (opaqueArray, waterArray))
            _uploadQueue.Enqueue((chunk, meshData));

            chunk.IsDirty = false;
        });
    }


    private static unsafe void FinalizeGPUUpload(Chunk chunk, (float[] opaque, float[] water) meshData)
    {
        // Find or create the RenderChunk
        RenderChunk? rc = _renderChunks.FirstOrDefault(c => c.WorldPosition == new Vector3(chunk.ChunkX * 16, 0, chunk.ChunkZ * 16));
        if (rc == null)
        {
            rc = new RenderChunk { WorldPosition = new Vector3(chunk.ChunkX * 16, 0, chunk.ChunkZ * 16) };
            _renderChunks.Add(rc);
        }

        // Upload Opaque Data
        UploadToVAO(ref rc.OpaqueVAO, ref rc.OpaqueVBO, meshData.opaque, out rc.OpaqueVertexCount);

        // Upload Water Data
        UploadToVAO(ref rc.WaterVAO, ref rc.WaterVBO, meshData.water, out rc.WaterVertexCount);
    }


    private static unsafe void UploadToVAO(ref uint vao, ref uint vbo, float[] data, out uint vertexCount)
    {
        // Updated: 11 floats per vertex (3 pos + 3 col + 2 uv + 3 normal)
        vertexCount = (uint)(data.Length / 11);
        if (vertexCount == 0) return;

        if (vao == 0) vao = Gl.GenVertexArray();
        if (vbo == 0) vbo = Gl.GenBuffer();

        Gl.BindVertexArray(vao);
        Gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        fixed (float* d = data)
            Gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), d, BufferUsageARB.StaticDraw);

        int stride = 11 * sizeof(float);

        // Position (Location 0)
        Gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)0);
        Gl.EnableVertexAttribArray(0);

        // Color (Location 1)
        Gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)(3 * sizeof(float)));
        Gl.EnableVertexAttribArray(1);

        // UV (Location 2)
        Gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)(6 * sizeof(float)));
        Gl.EnableVertexAttribArray(2);

        // Normal (Location 3) - New!
        Gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)(8 * sizeof(float)));
        Gl.EnableVertexAttribArray(3);
    }

    // Used For Blocks
    private static unsafe void LoadTextureAtlas(string path)
    {
        _textureAtlas = Gl.GenTexture();
        Gl.BindTexture(GLEnum.Texture2D, _textureAtlas);

        using (var img = Image.Load<Rgba32>(path))
        {
            // LOGGING: Check your file size in the console
            Console.WriteLine($"[Texture Load] Path: {path} | Size: {img.Width}x{img.Height}");

            img.Mutate(x => x.Flip(FlipMode.Vertical));
            var pixels = new byte[4 * img.Width * img.Height];
            img.CopyPixelDataTo(pixels);

            // Fix for 16px wide textures (CS7014 fix: ensure no brackets here)
            Gl.PixelStore(GLEnum.UnpackAlignment, 1);

            fixed (void* p = pixels)
            {
                Gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba, (uint)img.Width, (uint)img.Height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
            }
        }

        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    // Used for Skybox
    private static unsafe uint LoadTexture(string path)
    {
        uint handle = Gl.GenTexture();
        Gl.BindTexture(GLEnum.Texture2D, handle);

        using (var image = Image.Load<Rgba32>(path))
        {
            // OpenGL expects the first pixel to be bottom-left
            image.Mutate(x => x.Flip(FlipMode.Vertical));

            byte[] pixels = new byte[4 * image.Width * image.Height];
            image.CopyPixelDataTo(pixels);

            // This ensures OpenGL handles the byte alignment of your pixel data correctly
            Gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            fixed (byte* ptr = pixels)
            {
                Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba, (uint)image.Width, (uint)image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }

            // Generate mipmaps so the shader has data at all "levels"
            Gl.GenerateMipmap(GLEnum.Texture2D);
        }

        // Standard Pixel Art Settings
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.NearestMipmapLinear); // Use mipmaps but keep pixels sharp
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.Repeat);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.Repeat);

        return handle;
    }

    private static void OnRender(double deltaTime)
    {
        Vector3 sunDir = _timeManager.SunDirection;
        Vector3 eyePos = player.GetEyePosition();

        Gl.DepthMask(true);
        Gl.ClearColor(0, 0, 0, 1.0f);
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var view = Matrix4x4.CreateLookAt(eyePos, eyePos + player.CameraFront, player.CameraUp);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(70f * (float)Math.PI / 180f, (float)window.Size.X / window.Size.Y, 0.1f, 2000.0f);
        _frustum.Update(view * projection);

        // --- PASS 1: SKYBOX ---
        Gl.UseProgram(_skyboxShader);
        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.DepthTest);
        Gl.DepthMask(false);

        int timeLoc = Gl.GetUniformLocation(_skyboxShader, "uTime");
        if (timeLoc != -1)
        {
            // Use TotalTicks / TicksPerSecond for a smooth, increasing float
            float shaderTime = (float)((double)_timeManager.TotalTicks / TimeManager.TicksPerSecond);
            Gl.Uniform1(timeLoc, shaderTime);
        }
        _skybox.Render(_skyboxShader, view, projection, sunDir);
        Gl.Flush(); // Ensure skybox commands are processed

        // --- PASS 1.5: SUN/MOON ---
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        RenderSunMoon(view, projection, sunDir);

        // --- PASS 2: WORLD (OPAQUE) ---
        Gl.Disable(GLEnum.Blend);
        Gl.Enable(GLEnum.DepthTest);
        Gl.Enable(GLEnum.CullFace);
        Gl.DepthMask(true);

        Gl.UseProgram(Shader);
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, _textureAtlas);
        Gl.Uniform1(Gl.GetUniformLocation(Shader, "uTexture"), 0);
        Gl.Uniform3(Gl.GetUniformLocation(Shader, "uSunDir"), sunDir.X, sunDir.Y, sunDir.Z);
        SetUniformMatrix(Shader, "uView", view);
        SetUniformMatrix(Shader, "uProjection", projection);

        RenderChunk[] chunksToDraw;
        lock (_renderChunks) { chunksToDraw = _renderChunks.ToArray(); }

        foreach (var rc in chunksToDraw)
        {
            if (rc.OpaqueVertexCount == 0) continue;
            if (!_frustum.IsBoxVisible(rc.WorldPosition, rc.WorldPosition + new Vector3(16, 256, 16))) continue;
            SetUniformMatrix(Shader, "uModel", Matrix4x4.CreateTranslation(rc.WorldPosition));
            Gl.BindVertexArray(rc.OpaqueVAO);
            Gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)rc.OpaqueVertexCount);
        }

        // --- PASS 3: WATER ---
        Gl.Enable(GLEnum.Blend);
        Gl.DepthMask(false);
        foreach (var rc in chunksToDraw)
        {
            if (rc.WaterVertexCount == 0) continue;
            SetUniformMatrix(Shader, "uModel", Matrix4x4.CreateTranslation(rc.WorldPosition));
            Gl.BindVertexArray(rc.WaterVAO);
            Gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)rc.WaterVertexCount);
        }

        // --- PASS 4: SELECTION OUTLINE ---
        var result = Raycaster.Trace(voxelWorld, player.GetEyePosition(), player.CameraFront, 5.0f);
        if (result.Hit)
        {
            Gl.Disable(GLEnum.Blend);
            Gl.Enable(GLEnum.DepthTest);
            Gl.DepthMask(false);

            Gl.Enable(EnableCap.PolygonOffsetLine);
            Gl.PolygonOffset(-1.0f, -1.0f);
            _selectionBox.Render(_selectionShader, result.IntPos, view, projection);
            Gl.Disable(EnableCap.PolygonOffsetLine);
        }

        // --- PASS 5: UI ---
        Gl.Disable(EnableCap.DepthTest);
        _crosshair.Render(_crosshairShader);

        Gl.Enable(EnableCap.DepthTest);
        Gl.DepthMask(true);
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
        var keyboard = Input.Keyboards[0];

        while (_unloadQueue.TryDequeue(out var coords))
        {
            lock (_renderChunks)
            {
                var existing = _renderChunks.Find(rc =>
                    (int)Math.Floor(rc.WorldPosition.X / 16.0f) == coords.x &&
                    (int)Math.Floor(rc.WorldPosition.Z / 16.0f) == coords.z);

                if (existing != null)
                {
                    // Delete BOTH sets of GPU objects
                    if (existing.OpaqueVAO != 0) Gl.DeleteVertexArray(existing.OpaqueVAO);
                    if (existing.OpaqueVBO != 0) Gl.DeleteBuffer(existing.OpaqueVBO);
                    if (existing.WaterVAO != 0) Gl.DeleteVertexArray(existing.WaterVAO);
                    if (existing.WaterVBO != 0) Gl.DeleteBuffer(existing.WaterVBO);

                    _renderChunks.Remove(existing);
                }
            }
        }

        _timeManager.Update(deltaTime);

        int uploadLimit = _uploadQueue.Count > 100 ? 50 : 5;
        for (int i = 0; i < uploadLimit; i++)
        {
            // 'data.meshData' now contains the tuple of (opaque, water)
            if (_uploadQueue.TryDequeue(out var data))
                FinalizeGPUUpload(data.chunk, data.meshData);
            else break;
        }

        player.Update(deltaTime, keyboard, voxelWorld);
        player.HandleInteraction(Input, voxelWorld, (float)deltaTime);

        foreach (var chunk in voxelWorld.Chunks.Values)
        {
            if (chunk.IsDirty)
            {
                chunk.IsDirty = false; // Reset the flag so we don't queue it 1000 times

                // Push the chunk back into your generation thread
                Task.Run(() =>
                {
                    var meshData = chunk.FillVertexData();
                    // This 'uploadQueue' is what your Program.cs uses to call FinalizeGPUUpload
                    _uploadQueue.Enqueue((chunk, meshData));
                });
            }
        }

        _timePassed += deltaTime;
        _frameCount++;
        if (_timePassed >= 1.0)
        {
            double fps = _frameCount / _timePassed;
            window.Title = $"FPS: {fps:F0} | Position: {player.Position.X:F1}, {player.Position.Y:F1}, {player.Position.Z:F1}";
            _timePassed = 0;
            _frameCount = 0;
        }

        if (keyboard.IsKeyPressed(Key.Escape)) window.Close();
    }

    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        Vector2 mouseOffset = new Vector2(position.X - LastMousePos.X, position.Y - LastMousePos.Y);
        LastMousePos = position;

        player.Rotate(mouseOffset);
    }

    private static uint CompileCrosshairShader()
    {
        string vertCode = @"
    #version 330 core
    layout (location = 0) in vec2 aPos;
    void main() {
        gl_Position = vec4(aPos, 0.0, 1.0);
    }";

        string fragCode = @"
    #version 330 core
    out vec4 FragColor;
    void main() {
        FragColor = vec4(1.0, 1.0, 1.0, 0.8); 
    }";

        return CreateShaderProgram(vertCode, fragCode);
    }

    private static uint CreateShaderProgram(string vertexSource, string fragmentSource)
    {
        uint vertexShader = Gl.CreateShader(ShaderType.VertexShader);
        Gl.ShaderSource(vertexShader, vertexSource);
        Gl.CompileShader(vertexShader);

        // Check for compilation errors
        string infoLog = Gl.GetShaderInfoLog(vertexShader);
        if (!string.IsNullOrWhiteSpace(infoLog))
            Console.WriteLine($"Vertex Shader Error: {infoLog}");

        uint fragmentShader = Gl.CreateShader(ShaderType.FragmentShader);
        Gl.ShaderSource(fragmentShader, fragmentSource);
        Gl.CompileShader(fragmentShader);

        // Check for compilation errors
        infoLog = Gl.GetShaderInfoLog(fragmentShader);
        if (!string.IsNullOrWhiteSpace(infoLog))
            Console.WriteLine($"Fragment Shader Error: {infoLog}");

        uint program = Gl.CreateProgram();
        Gl.AttachShader(program, vertexShader);
        Gl.AttachShader(program, fragmentShader);
        Gl.LinkProgram(program);

        // Check for linking errors
        Gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            Console.WriteLine($"Shader Link Error: {Gl.GetProgramInfoLog(program)}");

        // Clean up
        Gl.DeleteShader(vertexShader);
        Gl.DeleteShader(fragmentShader);

        return program;
    }


    private static void RenderSunMoon(Matrix4x4 view, Matrix4x4 projection, Vector3 sunDir)
    {
        Gl.UseProgram(_spriteShader);
        Gl.Disable(GLEnum.CullFace);

        // 1. Calculate the base orbit angle
        float angle = MathF.Atan2(sunDir.Y, sunDir.X);

        // 2. Create a rotation that points the 'face' of the quad 
        // directly at the world origin (0,0,0) based on sunDir.
        // We use CreateLookAt from Zero to the sunDir, which creates
        // a matrix that 'looks' at the sun. We then invert it to make
        // the sun 'look' at us.
        Matrix4x4 sunRotation = Matrix4x4.CreateLookAt(Vector3.Zero, sunDir, Vector3.UnitZ);
        Matrix4x4.Invert(sunRotation, out sunRotation);

        // --- DRAW SUN ---
        float sunScale = 25f;
        Matrix4x4 sunModel = Matrix4x4.CreateScale(sunScale) * sunRotation * Matrix4x4.CreateTranslation(sunDir * 100f);

        SetUniformMatrix(_spriteShader, "uModel", sunModel);
        SetUniformMatrix(_spriteShader, "uView", view);
        SetUniformMatrix(_spriteShader, "uProjection", projection);

        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, _sunTexture);
        _quadMesh.Render();

        // --- DRAW MOON ---
        Vector3 moonDir = -sunDir;
        Matrix4x4 moonRotation = Matrix4x4.CreateLookAt(Vector3.Zero, moonDir, Vector3.UnitZ);
        Matrix4x4.Invert(moonRotation, out moonRotation);

        Matrix4x4 moonModel = Matrix4x4.CreateScale(20f) * moonRotation * Matrix4x4.CreateTranslation(moonDir * 100f);

        SetUniformMatrix(_spriteShader, "uModel", moonModel);
        Gl.BindTexture(GLEnum.Texture2D, _moonTexture);
        _quadMesh.Render();
    }
}
