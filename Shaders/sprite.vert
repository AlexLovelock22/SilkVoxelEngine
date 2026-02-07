#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec2 aTexCoord;

out vec2 TexCoord;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main() {
    TexCoord = aTexCoord;
    // Strip translation from view so the sky objects are always "infinitely" far
    // Inside sprite.vert
    mat4 staticView = mat4(mat3(uView)); // This removes player movement from the view
    gl_Position = uProjection * staticView * uModel * vec4(aPos, 1.0);
}