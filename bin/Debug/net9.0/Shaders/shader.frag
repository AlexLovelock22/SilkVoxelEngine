#version 330 core

in vec3 vNormal;
in vec3 vWorldPos;
in vec2 vTexCoord; 
out vec4 FragColor;

uniform sampler2D uTexture;   
uniform sampler2D uHeightmap; 
uniform vec3 uSunDir;

// Noise function for the "Muddled" fuzz effect
float InterleavedGradientNoise(vec2 uv) {
    return fract(52.9829189 * fract(dot(uv, vec2(0.0605, 0.0598))));
}

float GetHeight(vec2 worldXZ) {
    vec2 mapCoords = floor(worldXZ) + 512.0;
    vec2 uv = mod(mod(mapCoords, 1024.0) + 1024.0, 1024.0) / 1024.0;
    uv += (0.5 / 1024.0);
    return texture(uHeightmap, uv).r * 255.0;
}

float CalculateShadow(vec3 lightDir, vec3 worldPos, vec3 normal) {
    float dotNL = dot(normal, lightDir);
    
    // FIX: We make the face lighting much more binary. 
    // If the sun is even slightly hitting the side (dotNL > -0.05), it stays bright.
    // This ensures Z-faces (where dotNL is ~0.0) aren't "auto-shaded."
    float faceShadow = clamp(smoothstep(-0.2, 0.0, dotNL), 0.45, 1.0);
    
    // If the face is definitely pointing away from the sun, return ambient shadow.
    if (dotNL <= -0.2) return 0.45;

    // --- AGGRESSIVE MUDDLED FUZZ ---
    float dither = InterleavedGradientNoise(gl_FragCoord.xy);
    // Increased jitter to 0.1 for a very strong blur on the triangle edges.
    vec3 rayDir = normalize(lightDir + (vec3(dither) - 0.5) * 0.1);

    vec3 rayPos = worldPos + (normal * 0.05);
    vec2 startBlock = floor(worldPos.xz);
    float baseHeight = GetHeight(worldPos.xz);

    vec3 mapPos = floor(rayPos);
    vec3 deltaDist = abs(vec3(1.0) / (rayDir + 0.00001)); 
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
                // Blur the shadow more as it gets further away
                float distanceFade = smoothstep(30.0, 0.0, totalDist);
                ddaShadow = mix(1.0, 0.45, distanceFade);
                break;
            }
        }

        // DDA Step
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
    
    // Neutral weighting for all vertical walls.
    // Only the bottom of blocks gets darkened to prevent a "floating" look.
    float directionalWeight = 1.0;
    if (vNormal.y < -0.9) directionalWeight = 0.6; 

    float shadow = 1.0;
    float ambient = 0.3; 
    float intensity = 0.7;

    if (uSunDir.y > 0.0) {
        shadow = CalculateShadow(normalize(uSunDir), vWorldPos, vNormal);
    } else {
        vec3 moonDir = normalize(-uSunDir);
        float moonShadow = CalculateShadow(moonDir, vWorldPos, vNormal);
        shadow = mix(0.7, 1.0, (moonShadow - 0.45) / 0.55);
        ambient = 0.15;
        intensity = 0.25;
    }

    vec3 finalRGB = texColor.rgb * (ambient + (shadow * intensity)) * directionalWeight;
    FragColor = vec4(finalRGB, 1.0);
}