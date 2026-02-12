#version 330 core

in vec3 vNormal;
in vec3 vWorldPos;
in vec2 vTexCoord; 
out vec4 FragColor;

uniform sampler2D uTexture;   
uniform sampler2D uHeightmap; 
uniform vec3 uSunDir;

float GetHeight(vec2 worldXZ) {
    vec2 uv = (worldXZ + 512.0) / 1024.0;
    return texture(uHeightmap, uv).r * 255.0;
}

float CalculateShadow(vec3 lightDir, vec3 worldPos, vec3 normal) {
    // 1. BACKFACE CHECK: 
    // If the face is pointing away from the light, it's 100% in shadow.
    // This fixes the 'missing shadow' on side faces.
    if (dot(normal, lightDir) < 0.0) {
        return 0.45;
    }

    vec3 rayDir = normalize(lightDir);
    vec3 rayPos = worldPos + (normal * 0.05);
    vec2 startBlock = floor(worldPos.xz);
    float baseHeight = GetHeight(worldPos.xz);

    vec3 mapPos = floor(rayPos);
    vec3 deltaDist = abs(vec3(1.0) / rayDir);
    vec3 rayStep = sign(rayDir);
    vec3 sideDist = (rayStep * (mapPos - rayPos) + (rayStep * 0.5) + 0.5) * deltaDist;

    float totalDist = 0.0;

    for (int i = 0; i < 100; i++) {
        float h = GetHeight(mapPos.xz);
        float currentRayY = worldPos.y + (rayDir.y * totalDist);

        if (h + 1.0 > currentRayY) {
            bool isStartBlock = (floor(mapPos.xz) == startBlock);
            if (!isStartBlock && !(h <= baseHeight + 0.1 && normal.y > 0.5)) {
                return 0.45; 
            }
        }

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
        if (currentRayY > 255.0 || currentRayY < 0.0) break;
    }
    return 1.0;
}

void main() {
    vec4 texColor = texture(uTexture, vTexCoord);
    float shadow = 1.0;
    float ambient = 0.2;
    float intensity = 0.8;

    if (uSunDir.y > 0.05) {
        // Sun Shadowing
        shadow = CalculateShadow(uSunDir, vWorldPos, vNormal);
    } else {
        // Moon Shadowing
        vec3 moonDir = normalize(-uSunDir);
        float moonShadow = CalculateShadow(moonDir, vWorldPos, vNormal);
        shadow = (moonShadow < 1.0) ? 0.7 : 1.0; 
        ambient = 0.1;
        intensity = 0.3;
    }

    // Combine everything into a flat, shaded look
    vec3 finalRGB = texColor.rgb * (ambient + shadow * intensity);
    
    FragColor = vec4(finalRGB, 1.0);
}