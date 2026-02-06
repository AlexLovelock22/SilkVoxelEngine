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

class RenderChunk
{
    public uint VAO;
    public uint VBO;
    public uint VertexCount;
    public Vector3 WorldPosition;
}

class Program
{
    private static ConcurrentQueue<(Chunk chunk, float[] vertices)> _uploadQueue = new();
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

        Task.Run(() => WorldStreamerLoop());

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

    private static void QueueChunkForMeshing(Chunk chunk)
    {
        Task.Run(() =>
        {
            var n = voxelWorld.GetNeighbors(chunk.ChunkX, chunk.ChunkZ);
            float[] vertices = chunk.GetVertexData(n.r, n.l, n.f, n.b);
            _uploadQueue.Enqueue((chunk, vertices));
        });
    }


    private static unsafe void FinalizeGPUUpload(Chunk chunk, float[] vertices)
    {
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

        lock (_renderChunks)
        {
            _renderChunks.Add(rc);
        }
    }

    private static void OnRender(double deltaTime)
    {
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        Gl.UseProgram(Shader);

        Vector3 eyePos = player.GetEyePosition();

        var view = Matrix4x4.CreateLookAt(eyePos, eyePos + player.CameraFront, player.CameraUp);

        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            70.0f * (float)Math.PI / 180.0f,
            (float)window.Size.X / window.Size.Y,
            0.1f,
            2000.0f
        );

        SetUniformMatrix(Shader, "uView", view);
        SetUniformMatrix(Shader, "uProjection", projection);

        RenderChunk[] chunksToDraw;
        lock (_renderChunks)
        {
            chunksToDraw = _renderChunks.ToArray();
        }

        foreach (var rc in chunksToDraw)
        {
            Vector3 toChunk = Vector3.Normalize(rc.WorldPosition - eyePos);
            float dot = Vector3.Dot(player.CameraFront, toChunk);

            if (dot < -0.3f) continue;

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
                    Gl.DeleteVertexArray(existing.VAO);
                    Gl.DeleteBuffer(existing.VBO);
                    _renderChunks.Remove(existing);
                }
            }
        }

        int uploadLimit = _uploadQueue.Count > 100 ? 50 : 5;
        for (int i = 0; i < uploadLimit; i++)
        {
            if (_uploadQueue.TryDequeue(out var data))
                FinalizeGPUUpload(data.chunk, data.vertices);
            else break;
        }

        player.Update(deltaTime, keyboard, voxelWorld);

        _timePassed += deltaTime;
        _frameCount++;
        if (_timePassed >= 1.0)
        {
            double fps = _frameCount / _timePassed;


            float minX = player.Position.X - player.Radius;
            float maxX = player.Position.X + player.Radius;
            float minZ = player.Position.Z - player.Radius;
            float maxZ = player.Position.Z + player.Radius;

            window.Title = $"FPS: {fps:F0} | " +
                           $"Feet: ({player.Position.X:F2}, {player.Position.Y:F2}, {player.Position.Z:F2}) | " +
                           $"Cam: ({CameraPosition.X:F2}, {CameraPosition.Y:F2}, {CameraPosition.Z:F2}) | " +
                           $"Box X: [{minX:F2} to {maxX:F2}] Z: [{minZ:F2} to {maxZ:F2}]";

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
