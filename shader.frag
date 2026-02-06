#version 330 core

out vec4 FragColor;
in vec3 ourColor;
in vec2 TexCoord; // Received from vertex shader

uniform sampler2D uTexture; // The actual texture atlas

void main()
{
    // Combine texture color with vertex color
    FragColor = texture(uTexture, TexCoord) * vec4(ourColor, 1.0f);
}