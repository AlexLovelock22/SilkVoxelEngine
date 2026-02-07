#version 330 core
layout (location = 0) in vec3 aPos;
out vec3 TexCoords;
uniform mat4 uView;
uniform mat4 uProjection;
void main() {
    TexCoords = aPos;
    mat4 staticView = mat4(mat3(uView)); 
    gl_Position = (uProjection * staticView * vec4(aPos, 1.0)).xyww;
}