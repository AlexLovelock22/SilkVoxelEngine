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
    float dotNL = dot(normal, lightDir);
    
    // MINECRAFT MIDDAY SOFTNESS:
    // We use a wide window (-0.4 to 0.4). 
    // This ensures that when the sun is near the top (11:15am - 12:45pm), 
    // side faces (where dotNL is near 0) stay in a "mid-shade" state (around 0.7-0.8).
    float faceShadow = clamp(smoothstep(-0.4, 0.4, dotNL), 0.45, 1.0);
    
    // If the face is definitely pointing away, it's fully in shadow.
    if (dotNL <= -0.4) return 0.45;

    vec3 rayDir = normalize(lightDir);
    vec3 rayPos = worldPos + (normal * 0.05);
    vec2 startBlock = floor(worldPos.xz);
    float baseHeight = GetHeight(worldPos.xz);

    vec3 mapPos = floor(rayPos);
    vec3 deltaDist = abs(vec3(1.0) / rayDir);
    vec3 rayStep = sign(rayDir);
    vec3 sideDist = (rayStep * (mapPos - rayPos) + (rayStep * 0.5) + 0.5) * deltaDist;

    float totalDist = 0.0;
    float ddaShadow = 1.0;

    for (int i = 0; i < 100; i++) {
        float h = GetHeight(mapPos.xz);
        float currentRayY = worldPos.y + (rayDir.y * totalDist);

        if (h + 1.0 > currentRayY) {
            bool isStartBlock = (floor(mapPos.xz) == startBlock);
            if (!isStartBlock && !(h <= baseHeight + 0.1 && normal.y > 0.5)) {
                ddaShadow = 0.45; 
                break;
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
    
    return min(faceShadow, ddaShadow);
}

void main() {
    vec4 texColor = texture(uTexture, vTexCoord);
    
    // DIRECTIONAL BIAS (Permanent axis-based shading)
    float directionalWeight = 1.0;
    if (abs(vNormal.y) > 0.9) directionalWeight = 1.0;       // Top (Brightest)
    else if (abs(vNormal.z) > 0.9) directionalWeight = 0.8;  // North/South (Shaded)
    else if (abs(vNormal.x) > 0.9) directionalWeight = 0.9;  // East/West (Semi-Bright)

    float shadow = 1.0;
    float ambient = 0.25; 
    float intensity = 0.75;

    if (uSunDir.y > 0.0) {
        shadow = CalculateShadow(normalize(uSunDir), vWorldPos, vNormal);
    } else {
        vec3 moonDir = normalize(-uSunDir);
        float moonShadow = CalculateShadow(moonDir, vWorldPos, vNormal);
        shadow = mix(0.7, 1.0, (moonShadow - 0.45) / 0.55);
        ambient = 0.15;
        intensity = 0.25;
    }

    // Multiply texture by the combined light factors
    vec3 finalRGB = texColor.rgb * (ambient + (shadow * intensity)) * directionalWeight;
    
    FragColor = vec4(finalRGB, 1.0);
}