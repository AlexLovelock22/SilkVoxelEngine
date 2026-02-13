#version 330 core

in vec3 vNormal;
in vec3 vWorldPos;
in vec2 vTexCoord; 
out vec4 FragColor;

uniform sampler2D uTexture;   
uniform sampler2D uHeightmap; 
uniform vec3 uSunDir;

// Screen-space noise to "muddle" the shadow edge
float ScreenNoise(vec2 uv) {
    return fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453);
}

float GetHeight(vec2 worldXZ) {
    vec2 mapCoords = floor(worldXZ) + 512.0;
    vec2 uv = mod(mod(mapCoords, 1024.0) + 1024.0, 1024.0) / 1024.0;
    uv += (0.5 / 1024.0);
    return texture(uHeightmap, uv).r * 255.0;
}

float CalculateShadow(vec3 lightDir, vec3 worldPos, vec3 normal) {
    float dotNL = dot(normal, lightDir);
    
    // Ensure Z-faces stay bright unless occluded
    float faceShadow = clamp(smoothstep(-0.15, 0.05, dotNL), 0.45, 1.0);
    if (dotNL <= -0.15) return 0.45;

    // --- INCREASED BLUR RANGE LOGIC ---
    float noise = ScreenNoise(gl_FragCoord.xy);
    
    // Jitter grain size back to 0.08
    vec3 jitter = (noise - 0.5) * 0.08 * vec3(1.0, 0.0, 1.0); 
    
    // Bias kept at 0.1 to maintain alignment
    vec3 rayPos = worldPos + (normal * 0.1); 
    
    // Multiplier increased to 0.4 to significantly widen the blur range
    vec3 rayDir = normalize(lightDir + (jitter * 0.4)); 

    vec2 startBlock = floor(worldPos.xz);
    float baseHeight = GetHeight(worldPos.xz);

    vec3 mapPos = floor(rayPos);
    vec3 deltaDist = abs(vec3(1.0) / (rayDir + 0.00001)); 
    vec3 rayStep = sign(rayDir);
    vec3 sideDist = (rayStep * (mapPos - rayPos) + (rayStep * 0.5) + 0.5) * deltaDist;

    float totalDist = 0.0;
    float ddaShadow = 1.0;

    for (int i = 0; i < 80; i++) {
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
                totalDist = sideDist.x; sideDist.x += deltaDist.x; mapPos.x += rayStep.x;
            } else {
                totalDist = sideDist.z; sideDist.z += deltaDist.z; mapPos.z += rayStep.z;
            }
        } else {
            if (sideDist.y < sideDist.z) {
                totalDist = sideDist.y; sideDist.y += deltaDist.y; mapPos.y += rayStep.y;
            } else {
                totalDist = sideDist.z; sideDist.z += deltaDist.z; mapPos.z += rayStep.z;
            }
        }
        if (currentRayY > 255.0 || currentRayY < 0.0) break;
    }
    
    return min(faceShadow, ddaShadow);
}

void main() {
    vec4 texColor = texture(uTexture, vTexCoord);
    
    float directionalWeight = 1.0;
    if (vNormal.y < -0.9) directionalWeight = 0.5; 

    float shadow = 1.0;
    float ambient = 0.3; 
    float intensity = 0.7;

    if (uSunDir.y > 0.0) {
        shadow = CalculateShadow(normalize(uSunDir), vWorldPos, vNormal);
    } else {
        shadow = CalculateShadow(normalize(-uSunDir), vWorldPos, vNormal);
        shadow = mix(0.7, 1.0, (shadow - 0.45) / 0.55);
        ambient = 0.15;
        intensity = 0.25;
    }

    vec3 finalRGB = texColor.rgb * (ambient + (shadow * intensity)) * directionalWeight;
    FragColor = vec4(finalRGB, 1.0);
}