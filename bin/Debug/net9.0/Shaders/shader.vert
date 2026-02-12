#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aColor;
layout (location = 2) in vec2 aTexCoord;
layout (location = 3) in vec3 aNormal;

out vec3 vColor;
out vec2 vTexCoord;
out vec3 vNormal;
out vec3 vWorldPos; // Required for shadow math

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    // Calculate the absolute world position of this vertex
    vec4 worldPos = uModel * vec4(aPos, 1.0);
    vWorldPos = worldPos.xyz;
    
    vColor = aColor;
    vTexCoord = aTexCoord;
    vNormal = aNormal;
    
    gl_Position = uProjection * uView * worldPos;
}