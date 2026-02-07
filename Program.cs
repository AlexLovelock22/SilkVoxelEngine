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

        Console.WriteLine($"[Perf] Engine Initialized in {totalSw.ElapsedMilliseconds}ms. Streaming started.");
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
        vertexCount = (uint)(data.Length / 8); // 3 pos + 3 col + 2 uv = 8 floats per vertex
        if (vertexCount == 0) return;

        if (vao == 0) vao = Gl.GenVertexArray();
        if (vbo == 0) vbo = Gl.GenBuffer();

        Gl.BindVertexArray(vao);
        Gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        fixed (float* d = data)
            Gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), d, BufferUsageARB.StaticDraw);

        // Pos
        Gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0);
        Gl.EnableVertexAttribArray(0);
        // Color
        Gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float)));
        Gl.EnableVertexAttribArray(1);
        // UV
        Gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(6 * sizeof(float)));
        Gl.EnableVertexAttribArray(2);
    }

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

    private static void OnRender(double deltaTime)
    {
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        Gl.UseProgram(Shader);

        // Bind texture once
        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, _textureAtlas);
        Gl.Uniform1(Gl.GetUniformLocation(Shader, "uTexture"), 0);

        // Setup matrices
        Vector3 eyePos = player.GetEyePosition();
        var view = Matrix4x4.CreateLookAt(eyePos, eyePos + player.CameraFront, player.CameraUp);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(70f * (float)Math.PI / 180f, (float)window.Size.X / window.Size.Y, 0.1f, 2000.0f);

        _frustum.Update(view * projection);
        SetUniformMatrix(Shader, "uView", view);
        SetUniformMatrix(Shader, "uProjection", projection);

        RenderChunk[] chunksToDraw;
        lock (_renderChunks) { chunksToDraw = _renderChunks.ToArray(); }

        // --- PASS 1: SOLID GEOMETRY ---
        Gl.Disable(GLEnum.Blend);
        Gl.DepthMask(true); // Solids MUST write to depth buffer

        foreach (var rc in chunksToDraw)
        {
            if (rc.OpaqueVertexCount == 0) continue;
            if (!_frustum.IsBoxVisible(rc.WorldPosition, rc.WorldPosition + new Vector3(16, 256, 16))) continue;

            SetUniformMatrix(Shader, "uModel", Matrix4x4.CreateTranslation(rc.WorldPosition));
            Gl.BindVertexArray(rc.OpaqueVAO);
            Gl.DrawArrays(PrimitiveType.Triangles, 0, rc.OpaqueVertexCount);
        }

        // --- PASS 2: WATER GEOMETRY ---
        Gl.Enable(GLEnum.Blend);
        Gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        Gl.DepthMask(false); // Water should NOT block things behind it in the depth buffer

        foreach (var rc in chunksToDraw)
        {
            if (rc.WaterVertexCount == 0) continue;
            if (!_frustum.IsBoxVisible(rc.WorldPosition, rc.WorldPosition + new Vector3(16, 256, 16))) continue;

            SetUniformMatrix(Shader, "uModel", Matrix4x4.CreateTranslation(rc.WorldPosition));
            Gl.BindVertexArray(rc.WaterVAO);
            Gl.DrawArrays(PrimitiveType.Triangles, 0, rc.WaterVertexCount);
        }

        // Reset state for safety
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
}
