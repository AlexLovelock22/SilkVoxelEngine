#version 330 core

in vec3 vNormal;
in vec3 vWorldPos;
in vec2 vTexCoord; // Standard texture coordinates from your vertex shader
out vec4 FragColor;

uniform sampler2D uTexture;   // Your block textures (Grass, Stone, etc.)
uniform sampler2D uHeightmap; // Your 1024x1024 heightmap
uniform vec3 uSunDir;

// Accurate lookup using the 512-offset and 1024-wrap
float GetHeight(vec2 worldXZ) {
    vec2 uv = (worldXZ + 512.0) / 1024.0;
    return texture(uHeightmap, uv).r * 255.0;
}

void main() {
    // Early exit for night
    if (uSunDir.y <= 0.05) { 
        vec4 nightColor = texture(uTexture, vTexCoord) * 0.15;
        FragColor = vec4(nightColor.rgb, 1.0);
        return; 
    }

    vec3 rayDir = normalize(uSunDir);
    // Offset to prevent surface acne
    vec3 rayPos = vWorldPos + (vNormal * 0.05);
    float baseHeight = GetHeight(vWorldPos.xz);

    // DDA SETUP
    vec3 mapPos = floor(rayPos);
    vec3 deltaDist = abs(vec3(1.0) / rayDir);
    vec3 rayStep = sign(rayDir);
    vec3 sideDist = (rayStep * (mapPos - rayPos) + (rayStep * 0.5) + 0.5) * deltaDist;

    float shadow = 1.0;
    float totalDist = 0.0;

    // The DDA Loop (Optimized for sharp shadows)
    for (int i = 0; i < 120; i++) {
        float h = GetHeight(mapPos.xz);
        float currentRayY = vWorldPos.y + (rayDir.y * totalDist);

        if (h + 1.0 > currentRayY) {
            // Ignore the block the ray is currently on
            if (!(h <= baseHeight + 0.1 && vNormal.y > 0.5)) {
                shadow = 0.45; // Fixed shadow darkness
                break;
            }
        }

        // Stepping to next grid boundary
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
        if (currentRayY > 255.0) break;
    }

    // Combine Everything
    vec4 texColor = texture(uTexture, vTexCoord);
    float diffuse = max(dot(vNormal, rayDir), 0.0);
    
    // Final shading calculation: Texture * (Ambient + (Diffuse * BinaryShadow))
    vec3 finalRGB = texColor.rgb * (0.2 + diffuse * shadow);
    FragColor = vec4(finalRGB, 1.0);
}