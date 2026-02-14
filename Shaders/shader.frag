#version 330 core

in vec3 vNormal;
in vec3 vWorldPos;
in vec2 vTexCoord; 
out vec4 FragColor;

uniform sampler2D uTexture;    
// 1. Updated to sampler3D
uniform sampler3D uVoxelGrid; 
uniform vec3 uSunDir;

// Screen-space noise for soft-shadow jitter
float ScreenNoise(vec2 uv) {
    return fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453);
}

// 2. New 3D Voxel Sampling Function
float GetVoxel(vec3 worldPos) {
    // Map world coordinates to 0.0 - 1.0 range based on 1024x256x1024 size
    // Using mod ensures the wrapping matches the C# side exactly
    vec3 texCoords;
    texCoords.x = mod(worldPos.x, 1024.0) / 1024.0;
    texCoords.z = mod(worldPos.z, 1024.0) / 1024.0;
    texCoords.y = worldPos.y / 256.0;

    // Sample the R8 texture. Value > 0 means solid
    return texture(uVoxelGrid, texCoords).r;
}

float CalculateShadow(vec3 lightDir, vec3 worldPos, vec3 normal) {
    float dotNL = dot(normal, lightDir);
    float faceShadow = clamp(smoothstep(-0.15, 0.05, dotNL), 0.45, 1.0);
    if (dotNL <= -0.15) return 0.45;

    float noise = ScreenNoise(gl_FragCoord.xy);
    vec3 jitter = (noise - 0.5) * 0.05 * vec3(1.0, 1.0, 1.0); 
    
    // Offset ray start to prevent shadow acne (blotches)
    vec3 rayPos = worldPos + (normal * 0.1); 
    vec3 rayDir = normalize(lightDir + (jitter * 0.2)); 

    // DDA Setup
    vec3 mapPos = floor(rayPos);
    vec3 deltaDist = abs(vec3(1.0) / (rayDir + 0.00001)); 
    vec3 rayStep = sign(rayDir);
    vec3 sideDist = (rayStep * (mapPos - rayPos) + (rayStep * 0.5) + 0.5) * deltaDist;

    float totalDist = 0.0;
    float ddaShadow = 1.0;

    // 3. 3D DDA Loop
    // Instead of checking a heightmap, we step through the 3D grid
    for (int i = 0; i < 100; i++) {
        // Sample 3D grid at current step
        if (GetVoxel(mapPos + 0.5) > 0.0) {
            ddaShadow = 0.45; 
            break;
        }

        // Standard DDA step logic
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
        
        // Bounds check
        if (mapPos.y > 255.0 || mapPos.y < 0.0 || totalDist > 128.0) break;
    }
    
    return min(faceShadow, ddaShadow);
}

void main() {
    vec4 texColor = texture(uTexture, vTexCoord);
    
    // Simple directional lighting for faces
    float directionalWeight = 1.0;
    if (vNormal.y > 0.9) directionalWeight = 1.0;       // Top
    else if (vNormal.y < -0.9) directionalWeight = 0.5; // Bottom
    else directionalWeight = 0.8;                       // Sides

    float shadow = 1.0;
    float ambient = 0.35; 
    float intensity = 0.65;

    if (uSunDir.y > 0.0) {
        shadow = CalculateShadow(normalize(uSunDir), vWorldPos, vNormal);
    } else {
        // Night lighting
        shadow = CalculateShadow(normalize(-uSunDir), vWorldPos, vNormal);
        shadow = mix(0.8, 1.0, (shadow - 0.45) / 0.55);
        ambient = 0.1;
        intensity = 0.2;
    }

    // 4. Debug: Coordinate Visualization
    // If you uncomment this, the world will turn into a coordinate map
    // FragColor = vec4(mod(vWorldPos.x, 1024.0)/1024.0, vWorldPos.y/256.0, mod(vWorldPos.z, 1024.0)/1024.0, 1.0); return;

    vec3 finalRGB = texColor.rgb * (ambient + (shadow * intensity)) * directionalWeight;
    FragColor = vec4(finalRGB, texColor.a);
    if (texColor.a < 0.1) discard;
}