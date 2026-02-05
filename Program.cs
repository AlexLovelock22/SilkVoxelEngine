using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using System.Drawing;
using System.Numerics;
using VoxelEngine_Silk.Net_1._0.World; // Ensure this matches your folder structure


namespace VoxelEngine_Silk.Net_1._0;

class Program
{
    // The main Window and OpenGL API handle
    private static IWindow window = null!;
    private static GL Gl = null!;

    // Graphics handles
    private static uint VBO;
    private static uint VAO;
    private static uint Shader;
    private static IInputContext Input = null!;

    // We store the count of vertices so OpenGL knows how many points to draw
    private static uint vertexCount;

    //Camera
    private static Vector3 CameraPosition = new Vector3(8, 10, 30);
    private static Vector3 CameraFront = new Vector3(0, 0, -1);
    private static Vector3 CameraUp = Vector3.UnitY;
    private static float CameraYaw = -90f;
    private static float CameraPitch = 0f;
    private static Vector2 LastMousePos;

    //Debug Labels
    private static double _timePassed;
    private static int _frameCount;
    static void Main(string[] args)
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(1280, 720);
        options.Title = "Voxel Engine - Single Chunk Render";

        window = Window.Create(options);

        window.Load += OnLoad;
        window.Update += OnUpdate;
        window.Render += OnRender;
        window.Run();
    }

    private static unsafe void OnLoad()
    {
        Gl = GL.GetApi(window);

        // 1. Setup Global OpenGL State
        Gl.Enable(EnableCap.DepthTest); // Ensures blocks in front hide blocks behind
        Gl.ClearColor(0.4f, 0.6f, 0.9f, 1.0f); // Sky Blue background

        // 2. Initialize our Chunk and generate Mesh data
        Chunk chunk = new Chunk();
        float[] vertices = chunk.GetVertexData();
        vertexCount = (uint)(vertices.Length / 6);

        // 3. Create and Bind the Vertex Array Object (VAO)
        VAO = Gl.GenVertexArray();
        Gl.BindVertexArray(VAO);

        // 4. Create and Bind the Vertex Buffer Object (VBO)
        VBO = Gl.GenBuffer();
        Gl.BindBuffer(BufferTargetARB.ArrayBuffer, VBO);

        // Upload the chunk's vertex data to the GPU
        fixed (void* v = vertices)
        {
            Gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
        }

        // 5. Tell the GPU how to interpret the data (X, Y, Z coordinates)
        uint stride = 6 * sizeof(float);

        // Attribute 0: Position (X, Y, Z)
        Gl.VertexAttribPointer(0, 3, (GLEnum)VertexAttribType.Float, false, stride, (void*)0);
        Gl.EnableVertexAttribArray(0);

        // Attribute 1: Color (R, G, B)
        // It starts after the first 3 floats (3 * sizeof(float))
        Gl.VertexAttribPointer(1, 3, (GLEnum)VertexAttribType.Float, false, stride, (void*)(3 * sizeof(float)));
        Gl.EnableVertexAttribArray(1);

        // 6. Shader Setup
        string vertexCode = File.ReadAllText("shader.vert");
        string fragmentCode = File.ReadAllText("shader.frag");

        uint vs = CompileShader(ShaderType.VertexShader, vertexCode);
        uint fs = CompileShader(ShaderType.FragmentShader, fragmentCode);

        Shader = Gl.CreateProgram();
        Gl.AttachShader(Shader, vs);
        Gl.AttachShader(Shader, fs);
        Gl.LinkProgram(Shader);

        // Cleanup individual shaders now that they are linked
        Gl.DeleteShader(vs);
        Gl.DeleteShader(fs);

        // 7. Setup Input
        Input = window.CreateInput();

        // Loop through all mice and set their cursor mode
        foreach (var mouse in Input.Mice)
        {
            mouse.Cursor.CursorMode = CursorMode.Raw; // This locks the mouse to the window
        }

        // Subscribe to the mouse move event
        Input.Mice[0].MouseMove += OnMouseMove;
        LastMousePos = Input.Mice[0].Position;

    }

    private static void OnRender(double deltaTime)
    {
        // Clear the screen and the depth buffer
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Gl.UseProgram(Shader);

        // --- CAMERA AND MATRICES ---

        // Model Matrix: The chunk stays at world origin (0,0,0)
        var model = Matrix4x4.Identity;

        // View Matrix: Move the camera back 30 units and center it on the 16x16 chunk
        // Note: We move the WORLD by -8, -8 to center the camera on the middle of the chunk
        var view = Matrix4x4.CreateLookAt(CameraPosition, CameraPosition + CameraFront, CameraUp);

        // Projection Matrix: 45 degree vertical Field of View
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(45.0f * (float)Math.PI / 180.0f, 1280f / 720f, 0.1f, 100.0f);

        // Upload matrices to the shader
        SetUniformMatrix(Shader, "uModel", model);
        SetUniformMatrix(Shader, "uView", view);
        SetUniformMatrix(Shader, "uProjection", projection);

        // 8. Draw the entire chunk
        Gl.BindVertexArray(VAO);
        Gl.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);
    }

    private static unsafe void SetUniformMatrix(uint shader, string name, Matrix4x4 matrix)
    {
        int location = Gl.GetUniformLocation(shader, name);
        if (location != -1)
        {
            Gl.UniformMatrix4(location, 1, false, (float*)&matrix);
        }
    }

    private static uint CompileShader(ShaderType type, string code)
    {
        uint handle = Gl.CreateShader(type);
        Gl.ShaderSource(handle, code);
        Gl.CompileShader(handle);

        // Check for compilation errors
        string infoLog = Gl.GetShaderInfoLog(handle);
        if (!string.IsNullOrWhiteSpace(infoLog))
        {
            Console.WriteLine($"Error compiling {type}: {infoLog}");
        }

        return handle;
    }

    private static void OnUpdate(double deltaTime)
    {
        _timePassed += deltaTime;
        _frameCount++;

        if (_timePassed >= 1.0) // Every 1 second
        {
            // Calculate the stats
            double fps = _frameCount / _timePassed;

            // Update the Title
            // Note: vertexCount was calculated during OnLoad
            window.Title = $"Voxel Engine | FPS: {fps:F1} | Vertices: {vertexCount:N0}";

            // Reset counters
            _timePassed = 0;
            _frameCount = 0;
        }

        var keyboard = Input.Keyboards[0];
        float speed = 10f * (float)deltaTime;

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

        // Convert degrees to radians manually: (Degrees * PI / 180)
        float yawRad = CameraYaw * (float)Math.PI / 180.0f;
        float pitchRad = CameraPitch * (float)Math.PI / 180.0f;

        Vector3 direction;
        direction.X = (float)Math.Cos(yawRad) * (float)Math.Cos(pitchRad);
        direction.Y = (float)Math.Sin(pitchRad);
        direction.Z = (float)Math.Sin(yawRad) * (float)Math.Cos(pitchRad);

        CameraFront = Vector3.Normalize(direction);
    }
}