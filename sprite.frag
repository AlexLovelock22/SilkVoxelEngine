#version 330 core
out vec4 FragColor;
in vec2 TexCoord;

uniform sampler2D uTexture;

void main() {
    vec4 col = texture(uTexture, TexCoord);
    if(col.a < 0.1) discard; // Minecraft-style transparency
    FragColor = col;
}