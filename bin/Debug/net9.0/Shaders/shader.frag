#version 330 core

in vec3 vNormal;
in vec3 vWorldPos;
out vec4 FragColor;

uniform sampler2D uHeightmap;
uniform vec3 uSunDir;

// Exact heightmap lookup matching the C# 1024-unit world
float GetHeight(vec2 worldXZ) {
    vec2 uv = (worldXZ + 512.0) / 1024.0;
    return texture(uHeightmap, uv).r * 255.0;
}

void main() {
    if (uSunDir.y <= 0.05) { FragColor = vec4(vec3(0.1), 1.0); return; }

    vec3 rayDir = normalize(uSunDir);
    // Start at surface, biased by normal to prevent self-collision
    vec3 rayPos = vWorldPos + (vNormal * 0.01);
    float baseHeight = GetHeight(vWorldPos.xz);

    // DDA SETUP
    vec3 mapPos = floor(rayPos);
    vec3 deltaDist = abs(vec3(length(rayDir)) / rayDir);
    vec3 rayStep = sign(rayDir);
    vec3 sideDist = (rayStep * (mapPos - rayPos) + (rayStep * 0.5) + 0.5) * deltaDist;

    float shadow = 1.0;

    // Traverse the grid block-by-block
    for (int i = 0; i < 128; i++) {
        // Sample the heightmap at the current block center
        float h = GetHeight(mapPos.xz);

        // If the current block height is greater than our ray's current altitude
        if (h + 1.0 > rayPos.y) {
            // Ignore the block we started on
            if (!(h <= baseHeight + 0.1 && vNormal.y > 0.5)) {
                shadow = 0.4;
                break;
            }
        }

        // Jump to the next grid boundary
        if (sideDist.x < sideDist.y) {
            if (sideDist.x < sideDist.z) {
                sideDist.x += deltaDist.x;
                mapPos.x += rayStep.x;
            } else {
                sideDist.z += deltaDist.z;
                mapPos.z += rayStep.z;
            }
        } else {
            if (sideDist.y < sideDist.z) {
                sideDist.y += deltaDist.y;
                mapPos.y += rayStep.y;
            } else {
                sideDist.z += deltaDist.z;
                mapPos.z += rayStep.z;
            }
        }

        // Update ray altitude based on the jump
        // (Simplified for heightmap-based DDA)
        rayPos += rayDir * 0.5; 

        if (rayPos.y > 255.0 || rayPos.y < 0.0) break;
    }

    float diffuse = max(dot(vNormal, rayDir), 0.0);
    vec3 grassColor = vec3(0.2, 0.5, 0.1);
    FragColor = vec4(grassColor * (0.15 + diffuse * shadow), 1.0);
}