using Silk.NET.OpenGL;
using System.IO;

namespace VoxelEngine_Silk.Net_1._0.Helpers
{
    public static class ShaderManager
    {
        public static uint CreateShaderProgram(string vertexPath, string fragmentPath, GL gl)
        {
            string vertexSource = File.ReadAllText(vertexPath);
            string fragmentSource = File.ReadAllText(fragmentPath);

            uint vertexShader = gl.CreateShader(ShaderType.VertexShader);
            gl.ShaderSource(vertexShader, vertexSource);
            gl.CompileShader(vertexShader);
            CheckShaderError(vertexShader, "Vertex", vertexPath, gl);

            uint fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
            gl.ShaderSource(fragmentShader, fragmentSource);
            gl.CompileShader(fragmentShader);
            CheckShaderError(fragmentShader, "Fragment", fragmentPath, gl);

            uint program = gl.CreateProgram();
            gl.AttachShader(program, vertexShader);
            gl.AttachShader(program, fragmentShader);
            gl.LinkProgram(program);

            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
            if (status == 0)
                Console.WriteLine(gl.GetProgramInfoLog(program));

            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);

            return program;
        }

        public static void CheckShaderError(uint shader, string type, string path, GL gl)
        {
            string infoLog = gl.GetShaderInfoLog(shader);
            if (!string.IsNullOrWhiteSpace(infoLog))
                Console.WriteLine($"{type} Shader Error in {path}: {infoLog}");
        }

        public static uint CreateShaderProgramFromFile(string vertexPath, string fragmentPath, GL gl)
        {
            string vertexSource = File.ReadAllText(vertexPath);
            string fragmentSource = File.ReadAllText(fragmentPath);
            return CreateShaderProgramFromSource(vertexSource, fragmentSource, gl);
        }


        public static uint CreateShaderProgramFromSource(string vertexSource, string fragmentSource, GL gl)
        {
            uint vertexShader = gl.CreateShader(ShaderType.VertexShader);
            gl.ShaderSource(vertexShader, vertexSource);
            gl.CompileShader(vertexShader);

            // Check for errors
            string vLog = gl.GetShaderInfoLog(vertexShader);
            if (!string.IsNullOrWhiteSpace(vLog)) Console.WriteLine($"Vertex Error: {vLog}");

            uint fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
            gl.ShaderSource(fragmentShader, fragmentSource);
            gl.CompileShader(fragmentShader);

            string fLog = gl.GetShaderInfoLog(fragmentShader);
            if (!string.IsNullOrWhiteSpace(fLog)) Console.WriteLine($"Fragment Error: {fLog}");

            uint program = gl.CreateProgram();
            gl.AttachShader(program, vertexShader);
            gl.AttachShader(program, fragmentShader);
            gl.LinkProgram(program);

            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);

            return program;
        }



        public static uint CompileCrosshairShader(GL gl)
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

            return CreateShaderProgramFromSource(vertCode, fragCode, gl);
        }

        public static uint CompileSelectionShader(GL gl)
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

            uint vertex = gl.CreateShader(ShaderType.VertexShader);
            gl.ShaderSource(vertex, vertCode);
            gl.CompileShader(vertex);

            uint fragment = gl.CreateShader(ShaderType.FragmentShader);
            gl.ShaderSource(fragment, fragCode);
            gl.CompileShader(fragment);

            uint program = gl.CreateProgram();
            gl.AttachShader(program, vertex);
            gl.AttachShader(program, fragment);
            gl.LinkProgram(program);

            gl.DeleteShader(vertex);
            gl.DeleteShader(fragment);

            return ShaderManager.CreateShaderProgramFromSource(vertCode, fragCode, gl);
        }


    }
}
