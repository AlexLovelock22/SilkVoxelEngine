#version 330 core

in vec3 vNormal;
in vec3 vWorldPos;
out vec4 FragColor;

uniform sampler2D uHeightmap;
uniform vec3 uSunDir;

float GetHeight(vec2 worldXZ) {
    // We stick to the standard UV mapping matching your 1024 wrap
    vec2 uv = (worldXZ + 512.0) / 1024.0;
    return texture(uHeightmap, uv).r * 255.0;
}

void main() {
    // Early exit for night/sunset
    if (uSunDir.y <= 0.05) { FragColor = vec4(vec3(0.1), 1.0); return; }

    vec3 rayDir = normalize(uSunDir);
    // Tiny offset to prevent the surface from shadowing itself
    vec3 rayPos = vWorldPos + (vNormal * 0.05);
    float baseHeight = GetHeight(vWorldPos.xz);

    // DDA Setup
    vec3 mapPos = floor(rayPos);
    vec3 deltaDist = abs(vec3(1.0) / rayDir);
    vec3 rayStep = sign(rayDir);
    vec3 sideDist = (rayStep * (mapPos - rayPos) + (rayStep * 0.5) + 0.5) * deltaDist;

    float shadow = 1.0;
    float totalDist = 0.0;

    // Standard DDA loop for performance
    for (int i = 0; i < 120; i++) {
        float h = GetHeight(mapPos.xz);
        float currentRayY = vWorldPos.y + (rayDir.y * totalDist);

        // SHARP CHECK: If the height at this grid cell is above the ray, it's a shadow.
        // Adding 1.0 because the heightmap represents the floor of the block.
        if (h + 1.0 > currentRayY) {
            // Ensure we aren't hitting the block we are standing on
            if (!(h <= baseHeight + 0.1 && vNormal.y > 0.5)) {
                shadow = 0.4; // Binary shadow value (no blurring math)
                break;
            }
        }

        // Stepping logic
        if (sideDist.x < sideDist.y) {
            if (sideDist.x < sideDist.z) {
                totalDist = sideDist.x;
                sideDist.x += deltaDist.x;
                mapPos.x += rayStep.x;
            } else {
                totalDist = sideDist.z;
                sideDist.z += deltaDist.z;
                mapPos.z += rayStep.z;
            }
        } else {
            if (sideDist.y < sideDist.z) {
                totalDist = sideDist.y;
                sideDist.y += deltaDist.y;
                mapPos.y += rayStep.y;
            } else {
                totalDist = sideDist.z;
                sideDist.z += deltaDist.z;
                mapPos.z += rayStep.z;
            }
        }
        
        // Break if ray goes into space
        if (currentRayY > 255.0) break;
    }

    float diffuse = max(dot(vNormal, rayDir), 0.0);
    vec3 color = vec3(0.2, 0.5, 0.1); // Base grass color
    
    // Apply lighting and shadow
    FragColor = vec4(color * (0.15 + diffuse * shadow), 1.0);
}