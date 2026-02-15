#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aColor;
layout (location = 2) in vec2 aTexCoord;
layout (location = 3) in vec3 aNormal;
layout (location = 4) in float aAO; // New attribute

out vec3 vColor;
out vec2 vTexCoord;
out vec3 vNormal;
out vec3 vWorldPos;
out float vAO; // Passing to fragment

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    vec4 worldPos = uModel * vec4(aPos, 1.0);
    vWorldPos = worldPos.xyz;
    
    vColor = aColor;
    vTexCoord = aTexCoord;
    vNormal = aNormal;
    vAO = aAO; 
    
    gl_Position = uProjection * uView * worldPos;
}