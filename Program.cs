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
using VoxelEngine_Silk.Net_1._0.Helpers;

namespace VoxelEngine_Silk.Net_1._0;

class Program
{
    private static ConcurrentQueue<(Chunk chunk, (float[] opaque, float[] water) meshData, byte[] volumeData)> _uploadQueue = new();
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
    private static uint _spriteShader;
    private static QuadMesh _quadMesh = null!;
    private static WorldManager _worldManager = null!;
    private static uint _globalVoxelTexture;

    static void Main(string[] args)
    {
        var options = WindowOptions.Default;
        options.WindowState = WindowState.Maximized;
        options.WindowBorder = WindowBorder.Resizable;
        options.Size = new Vector2D<int>(1920, 1080);
        options.Title = "Voxel Engine - Multi-Chunk View";
        options.VSync = true;

        window = Window.Create(options);

        window.Load += OnLoad;
        window.Update += OnUpdate;
        window.Render += OnRender;

        // Use the helper for resizing
        window.FramebufferResize += UpdateViewport;

        window.Run();
    }

    private static unsafe void OnLoad()
    {
        Gl = GL.GetApi(window);

        // 1. Basic GL States
        Gl.Enable(GLEnum.DepthTest);
        Gl.Enable(GLEnum.CullFace);

        // 2. Setup Alpha Blending
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);

        Gl.ClearColor(0.4f, 0.6f, 0.9f, 1.0f);

        // 3. Texture and Shaders
        _textureAtlas = TextureManager.LoadTextureAtlas(Path.Combine("Textures", "terrain.png"), Gl);

        // Initialize Skybox mesh and textures
        _skybox = new Skybox(Gl);
        _sunTexture = TextureManager.LoadTexture(Path.Combine("Textures", "sun.png"), Gl);
        _moonTexture = TextureManager.LoadTexture(Path.Combine("Textures", "moon.png"), Gl);

        string vSource = Path.Combine("Shaders", "skybox.vert");
        string fSource = Path.Combine("Shaders", "skybox.frag");
        //voxelWorld.ExportBiomeMap(2048);
        _quadMesh = new QuadMesh(Gl);

        _spriteShader = ShaderManager.CreateShaderProgram(
            Path.Combine("Shaders", "sprite.vert"),
            Path.Combine("Shaders", "sprite.frag"),
            Gl
        );

        _skyboxShader = ShaderManager.CreateShaderProgramFromFile(vSource, fSource, Gl);

        _selectionBox = new SelectionBox(Gl);
        _selectionShader = ShaderManager.CompileSelectionShader(Gl);

        _crosshair = new Crosshair(Gl);
        _crosshairShader = ShaderManager.CompileCrosshairShader(Gl);
        File.Delete("BiomePreview_Organic.png");
        File.Delete("temp_map.png");

        // voxelWorld.ExportBiomeMap(2048);
        //  voxelWorld.ExportNoiseDebug("temp_map.png", 1024, 1024, 0, 0);
        string vertexCode = Path.Combine("Shaders", "shader.vert");
        string fragmentCode = Path.Combine("Shaders", "shader.frag");
        Shader = ShaderManager.CreateShaderProgramFromFile(vertexCode, fragmentCode, Gl);


        _timeManager.OnLoad();

        // 4. Input and Background Tasks
        Stopwatch totalSw = Stopwatch.StartNew();
        InitShadowSystem();
        _worldManager = new WorldManager(voxelWorld, _unloadQueue, (chunk) =>
        {
            Task.Run(() =>
            {
                var meshData = chunk.FillVertexData();
                byte[] volumeData = PrecomputeVolumeData(chunk);

                _uploadQueue.Enqueue((chunk, meshData, volumeData));
            });
        });

        // Start the streaming thread via the manager helper
        _worldManager.StartStreaming(() => player.GetEyePosition());

        Input = window.CreateInput();
        foreach (var mouse in Input.Mice) mouse.Cursor.CursorMode = CursorMode.Raw;
        Input.Mice[0].MouseMove += OnMouseMove;
        LastMousePos = Input.Mice[0].Position;

        UpdateViewport(window.FramebufferSize);

        Console.WriteLine($"[Perf] Engine Initialized in {totalSw.ElapsedMilliseconds}ms. Streaming started.");
    }


    // Inside your main Program or initialization class
    private static void InitShadowSystem()
    {
        // Replaces CreateGlobalHeightmap with the 3D version
        _globalVoxelTexture = MeshManager.CreateVoxel3DTexture(Gl);
    }



    private static void OnRender(double deltaTime)
    {
        Vector3 sunDir = _timeManager.SunDirection;
        Vector3 eyePos = player.GetEyePosition();

        Gl.DepthMask(true);
        Gl.ClearColor(0, 0, 0, 1.0f);
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var view = Matrix4x4.CreateLookAt(eyePos, eyePos + player.CameraFront, player.CameraUp);
        float aspect = (float)window.Size.X / (float)window.Size.Y;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(70f * (float)Math.PI / 180f, aspect, 0.1f, 2000.0f);
        _frustum.Update(view * projection);

        // --- PASS 1: SKYBOX ---
        Gl.UseProgram(_skyboxShader);
        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.DepthTest);
        Gl.DepthMask(false);

        int timeLoc = Gl.GetUniformLocation(_skyboxShader, "uTime");
        if (timeLoc != -1)
        {
            float shaderTime = (float)((double)_timeManager.TotalTicks / TimeManager.TicksPerSecond);
            Gl.Uniform1(timeLoc, shaderTime);
        }
        _skybox.Render(_skyboxShader, view, projection, sunDir);
        Gl.Flush();

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

        // Unit 0: Terrain Atlas
        TextureManager.Bind(Gl, _textureAtlas, 0);
        int texLoc = Gl.GetUniformLocation(Shader, "uTexture");
        if (texLoc != -1) Gl.Uniform1(texLoc, 0);

        // --- Unit 1: 3D Voxel Texture (Replaces Global Heightmap) ---
        Gl.ActiveTexture(TextureUnit.Texture1);
        Gl.BindTexture(TextureTarget.Texture3D, _globalVoxelTexture);
        // Updated uniform name to match 3D logic
        int voxelLoc = Gl.GetUniformLocation(Shader, "uVoxelGrid");
        if (voxelLoc != -1) Gl.Uniform1(voxelLoc, 1);

        Gl.Uniform3(Gl.GetUniformLocation(Shader, "uSunDir"), sunDir.X, sunDir.Y, sunDir.Z);

        MeshManager.SetUniformMatrix(Gl, Shader, "uView", view);
        MeshManager.SetUniformMatrix(Gl, Shader, "uProjection", projection);

        RenderChunk[] chunksToDraw;
        lock (_renderChunks) { chunksToDraw = _renderChunks.ToArray(); }

        foreach (var rc in chunksToDraw)
        {
            if (rc.OpaqueVertexCount == 0) continue;
            if (!_frustum.IsBoxVisible(rc.WorldPosition, rc.WorldPosition + new Vector3(16, 256, 16))) continue;

            int worldPosLoc = Gl.GetUniformLocation(Shader, "uChunkWorldPos");
            if (worldPosLoc != -1) Gl.Uniform3(worldPosLoc, rc.WorldPosition.X, rc.WorldPosition.Y, rc.WorldPosition.Z);

            MeshManager.SetUniformMatrix(Gl, Shader, "uModel", Matrix4x4.CreateTranslation(rc.WorldPosition));
            MeshManager.RenderChunkMesh(Gl, rc.OpaqueVAO, rc.OpaqueVertexCount);
        }

        // --- PASS 3: WATER ---
        Gl.Enable(GLEnum.Blend);
        Gl.DepthMask(false);
        foreach (var rc in chunksToDraw)
        {
            if (rc.WaterVertexCount == 0) continue;

            int worldPosLoc = Gl.GetUniformLocation(Shader, "uChunkWorldPos");
            if (worldPosLoc != -1) Gl.Uniform3(worldPosLoc, rc.WorldPosition.X, rc.WorldPosition.Y, rc.WorldPosition.Z);

            MeshManager.SetUniformMatrix(Gl, Shader, "uModel", Matrix4x4.CreateTranslation(rc.WorldPosition));
            MeshManager.RenderChunkMesh(Gl, rc.WaterVAO, rc.WaterVertexCount);
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

        // Note: DebugDrawHeightmap is now omitted or would need logic to visualize a 3D slice
        // DebugDrawVoxelSlice(); 

        // --- PASS 5: UI ---
        Gl.Disable(EnableCap.DepthTest);
        _crosshair.Render(_crosshairShader);

        Gl.Enable(EnableCap.DepthTest);
        Gl.DepthMask(true);
    }

    private static byte[] PrecomputeVolumeData(Chunk chunk)
    {
        byte[] data = new byte[16 * 256 * 16];
        for (int z = 0; z < 16; z++)
        {
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    if (chunk.Blocks[x, y, z] > 0)
                    {
                        // OpenGL Data Layout: X then Y then Z
                        int index = x + (y * 16) + (z * 16 * 256);
                        data[index] = 255;
                    }
                }
            }
        }
        return data;
    }

    // 2. Updated OnUpdate to handle unloading properly
    private static void OnUpdate(double deltaTime)
    {
        var keyboard = Input.Keyboards[0];

        // Clean up GPU meshes for unloaded chunks
        _worldManager.ProcessUnloadQueue(Gl, _renderChunks);

        // NEW: We also need to clear the 3D texture area for unloaded chunks 
        // so old shadows don't stay floating in the air!
        while (_unloadQueue.TryDequeue(out var coords))
        {
            MeshManager.ClearChunkInVoxelTexture(Gl, _globalVoxelTexture, coords.x, coords.z);
        }

        _timeManager.Update(deltaTime);

        // Increase this slightly (e.g., 2 or 3) if generation is too slow
        MeshManager.ProcessUploadQueue(Gl, _globalVoxelTexture, _renderChunks, _uploadQueue, voxelWorld);

        player.Update(deltaTime, keyboard, voxelWorld);
        player.HandleInteraction(Input, voxelWorld, (float)deltaTime, Gl, _globalVoxelTexture);
        UpdatePerformanceCounters(deltaTime);
        _worldManager.UpdateDirtyChunks();
    }

    private static void DebugDrawVoxelSlice()
    {
        Gl.Disable(GLEnum.DepthTest);
        Gl.Disable(GLEnum.CullFace);
        Gl.Disable(GLEnum.Blend);
        Gl.DepthMask(false);

        Gl.UseProgram(_spriteShader);

        Gl.ActiveTexture(TextureUnit.Texture0);
        // Note: To debug a 3D texture as a 2D sprite, your sprite shader 
        // would need to be updated to handle sampler3D and a 'Z' (height) coordinate.
        // For now, we bind the 3D texture.
        Gl.BindTexture(TextureTarget.Texture3D, _globalVoxelTexture);

        Gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        Gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);

        int texLoc = Gl.GetUniformLocation(_spriteShader, "uTexture");
        if (texLoc != -1) Gl.Uniform1(texLoc, 0);

        var projection = Matrix4x4.CreateOrthographicOffCenter(0, window.Size.X, window.Size.Y, 0, -1, 1);

        float size = 256f;
        Matrix4x4 model = Matrix4x4.CreateScale(size, size, 1.0f) * Matrix4x4.CreateTranslation(20 + size / 2f, 20 + size / 2f, 0);

        MeshManager.SetUniformMatrix(Gl, _spriteShader, "uProjection", projection);
        MeshManager.SetUniformMatrix(Gl, _spriteShader, "uView", Matrix4x4.Identity);
        MeshManager.SetUniformMatrix(Gl, _spriteShader, "uModel", model);

        // This will only work if your sprite shader is modified for sampler3D!
        _quadMesh.Render();

        Gl.Enable(GLEnum.DepthTest);
        Gl.DepthMask(true);
    }


    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        Vector2 mouseOffset = new Vector2(position.X - LastMousePos.X, position.Y - LastMousePos.Y);
        LastMousePos = position;

        player.Rotate(mouseOffset);
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

        MeshManager.SetUniformMatrix(Gl, _spriteShader, "uModel", sunModel);
        MeshManager.SetUniformMatrix(Gl, _spriteShader, "uView", view);
        MeshManager.SetUniformMatrix(Gl, _spriteShader, "uProjection", projection);

        TextureManager.Bind(Gl, _sunTexture, 0);

        _quadMesh.Render();

        // --- DRAW MOON ---
        Vector3 moonDir = -sunDir;
        Matrix4x4 moonRotation = Matrix4x4.CreateLookAt(Vector3.Zero, moonDir, Vector3.UnitZ);
        Matrix4x4.Invert(moonRotation, out moonRotation);

        Matrix4x4 moonModel = Matrix4x4.CreateScale(20f) * moonRotation * Matrix4x4.CreateTranslation(moonDir * 100f);

        MeshManager.SetUniformMatrix(Gl, _spriteShader, "uModel", moonModel);
        TextureManager.Bind(Gl, _moonTexture, 0);
        _quadMesh.Render();
    }

    private static void UpdateViewport(Vector2D<int> size)
    {
        Gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);
    }

    private static double _titleUpdateTimer;
    private static double _fpsTimer;
    private static double _lastFps;

    private static void UpdatePerformanceCounters(double deltaTime)
    {
        _timePassed += deltaTime;
        _titleUpdateTimer += deltaTime;
        _fpsTimer += deltaTime;
        _frameCount++;

        // 1. Calculate FPS once per second (for stability)
        if (_fpsTimer >= 1.0)
        {
            _lastFps = _frameCount / _fpsTimer;
            _frameCount = 0;
            _fpsTimer = 0;
        }

        // 2. Update Title 60 times per second (~0.016s)
        if (_titleUpdateTimer >= (1.0 / 60.0))
        {
            int chunkCount = _renderChunks.Count;
            Vector3 pos = player.GetEyePosition();

            // Get the real-time biome matching the ruffled ground
            BiomeType type = BiomeManager.GetBiomeAt(voxelWorld, pos.X, pos.Z);
            string currentBiome = type.ToString();

            // Sample raw values (using the +1000 offset defined in BiomeManager)
            float tRaw = voxelWorld.TempNoise.GetNoise(pos.X, pos.Z);
            float hRaw = voxelWorld.HumidityNoise.GetNoise(pos.X + 1000, pos.Z + 1000);
            float t = (tRaw + 1f) / 2f;
            float h = (hRaw + 1f) / 2f;

            // 3. Update Window Title
            window.Title = $"Voxel Engine | FPS: {_lastFps:F0} | " +
                           $"Pos: ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}) | " +
                           $"Biome: {currentBiome} | T: {t:F2} H: {h:F2} | " +
                           $"Chunks: {chunkCount}";

            _titleUpdateTimer = 0;
        }
    }

}
