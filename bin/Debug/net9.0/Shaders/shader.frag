#version 330 core

in vec3 vNormal;
in vec3 vWorldPos;
in vec2 vTexCoord; 
out vec4 FragColor;

uniform sampler2D uTexture;   
uniform sampler2D uHeightmap; 
uniform vec3 uSunDir;

float ScreenNoise(vec2 uv) {
    return fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453123);
}

float GetHeight(vec2 worldXZ) {
    vec2 mapCoords = floor(worldXZ) + 512.0;
    vec2 uv = mod(mod(mapCoords, 1024.0) + 1024.0, 1024.0) / 1024.0;
    uv += (0.5 / 1024.0);
    return texture(uHeightmap, uv).r * 255.0;
}

// Optimized Trace: Fewer steps, faster escape
float TraceShadowRay(vec3 rayDir, vec3 rayPos, vec3 worldPos, vec3 normal, float startBaseH, vec2 startB, float noise) {
    vec3 mapPos = floor(rayPos);
    vec3 deltaDist = abs(vec3(1.0) / (rayDir + 0.00001));
    vec3 rayStep = sign(rayDir);
    vec3 sideDist = (rayStep * (mapPos - rayPos) + (rayStep * 0.5) + 0.5) * deltaDist;

    float totalDist = 0.01 + (noise * 0.1); 
    
    // OPTIMIZATION: Reduced steps from 64 to 40. 
    // Most shadows in a heightmap world are caught quickly.
    for (int i = 0; i < 40; i++) {
        float h = GetHeight(mapPos.xz);
        float currentRayY = rayPos.y + (rayDir.y * totalDist);

        if (h + 1.0 > currentRayY) {
            bool isStartBlock = (floor(mapPos.xz) == startB);
            bool isFloor = (h <= startBaseH + 0.1 && normal.y > 0.5);
            
            if (!isStartBlock && !isFloor) {
                float depth = (h + 1.0) - currentRayY;
                return clamp(depth * 2.0, 0.0, 1.0); 
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
        // Shorter max distance for performance
        if (currentRayY > 255.0 || currentRayY < 0.0 || totalDist > 30.0) break;
    }
    return 0.0;
}

float CalculateShadow(vec3 lightDir, vec3 worldPos, vec3 normal) {
    float dotNL = dot(normal, lightDir);
    float faceShadow = clamp(smoothstep(-0.4, 0.4, dotNL), 0.45, 1.0);
    if (dotNL <= -0.4) return 0.45;

    float noise = ScreenNoise(gl_FragCoord.xy);
    vec2 startBlock = floor(worldPos.xz);
    float baseH = GetHeight(worldPos.xz);
    
    // OPTIMIZATION: Early Exit / Branching
    // First, trace ONE single ray. If it hits nothing, 
    // there's a 99% chance we are in full sun. Skip the other 3 samples.
    float firstHit = TraceShadowRay(lightDir, worldPos + (normal * 0.05), worldPos, normal, baseH, startBlock, noise);
    
    // If the main ray is clear, return early (HUGE FPS BOOST)
    if (firstHit == 0.0) return faceShadow;

    // Basis for jitter
    vec3 lightUp = abs(lightDir.y) > 0.9 ? vec3(0, 0, 1) : vec3(0, 1, 0);
    vec3 tangent = normalize(cross(lightDir, lightUp));
    vec3 bitangent = cross(lightDir, tangent);

    float shadowAccum = firstHit;
    // OPTIMIZATION: Reduced sample count from 6 to 4. 
    // Combined with noise, 4 is almost indistinguishable from 6 but 33% faster.
    const int samples = 4; 
    float spread = 0.05; 

    for(int i = 1; i < samples; i++) {
        float angle = (float(i) * (6.2831 / float(samples))) + (noise * 6.2831);
        vec2 offset = vec2(cos(angle), sin(angle)) * spread;
        vec3 jitterDir = normalize(lightDir + tangent * offset.x + bitangent * offset.y);
        shadowAccum += TraceShadowRay(jitterDir, worldPos + (normal * 0.05), worldPos, normal, baseH, startBlock, noise);
    }

    float shadowFactor = 1.0 - (shadowAccum / float(samples));
    return min(faceShadow, mix(0.45, 1.0, shadowFactor));
}

void main() {
    vec4 texColor = texture(uTexture, vTexCoord);
    
    float directionalWeight = 1.0;
    if (abs(vNormal.y) > 0.9) directionalWeight = 1.0;
    else if (abs(vNormal.z) > 0.9) directionalWeight = 0.8;
    else if (abs(vNormal.x) > 0.9) directionalWeight = 0.9;

    float shadow = 1.0;
    float ambient = 0.25; 
    float intensity = 0.75;

    if (uSunDir.y > 0.0) {
        shadow = CalculateShadow(normalize(uSunDir), vWorldPos, vNormal);
    } else {
        vec3 moonDir = normalize(-uSunDir);
        shadow = mix(0.7, 1.0, (CalculateShadow(moonDir, vWorldPos, vNormal) - 0.45) / 0.55);
        ambient = 0.15;
        intensity = 0.25;
    }

    vec3 finalRGB = texColor.rgb * (ambient + (shadow * intensity)) * directionalWeight;
    FragColor = vec4(finalRGB, 1.0);
}