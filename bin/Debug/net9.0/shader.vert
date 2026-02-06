#version 330 core

layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aColor;
layout (location = 2) in vec2 aTexCoord; // New UV input

out vec3 ourColor;
out vec2 TexCoord; // Pass UV to fragment shader

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
    ourColor = aColor;
    TexCoord = aTexCoord; // Send UV through
}