#version 330 core
out vec4 FragColor;
in vec3 TexCoords;

uniform vec3 uSunDir;
uniform float uTime;

// 1. HELPER: Standard 3D Rotation
vec3 rotate(vec3 v, vec3 axis, float angle) {
    float s = sin(angle);
    float c = cos(angle);
    float oc = 1.0 - c;
    mat3 m = mat3(
        oc * axis.x * axis.x + c,           oc * axis.x * axis.y - axis.z * s,  oc * axis.z * axis.x + axis.y * s,
        oc * axis.x * axis.y + axis.z * s,  oc * axis.y * axis.y + c,           oc * axis.y * axis.z - axis.x * s,
        oc * axis.z * axis.x - axis.y * s,  oc * axis.y * axis.z + axis.x * s,  oc * axis.z * axis.z + c
    );
    return m * v;
}

// 2. HELPER: Noise & Hash
float hash(vec3 p) {
    p  = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i + vec3(0, 0, 0)), hash(i + vec3(1, 0, 0)), f.x),
                   mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x),
                   mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

// 3. STAR LAYER FUNCTION
vec3 drawStarLayer(vec3 dir, float starPresence, vec3 skyColor, float density, float sizeBase) {
    vec3 starPos = dir * 250.0; 
    vec3 starId = floor(starPos);
    float n = hash(starId); 
    
    if (n > density) { 
        vec3 localPos = fract(starPos) - 0.5;
        float dist = length(localPos);
        float starRadius = sizeBase + (n * 0.15); 
        float starPoint = smoothstep(starRadius, starRadius - 0.15, dist);
        
        float twinkleSeed = fract(n * 1234.5678); 
        float phase = twinkleSeed * 6.2831;
        float speed = 1.5 + fract(twinkleSeed * 45.67) * 2.5;
        float pulse = sin(uTime * speed + phase); 
        
        float twinkleEffect = 1.0;
        if (fract(twinkleSeed * 8.9) > 0.5) {
            twinkleEffect = mix(0.2, 1.0, pulse * 0.5 + 0.5);
        }
        
        float intensity = pow(n, 15.0) * 1.6;
        float currentBrightness = intensity * mix(1.0, twinkleEffect, starPresence);
        currentBrightness *= starPresence * starPoint;

        float whiteLock = pow(starPresence, 8.0); 
        vec3 ghostColor = skyColor * 1.4; 
        vec3 starWhite = vec3(0.85); 
        
        return mix(ghostColor, starWhite, whiteLock) * currentBrightness;
    }
    return vec3(0.0);
}

void main() {
    vec3 viewDir = normalize(TexCoords);
    
    // --- 1. COORDINATED LIGHTING LOGIC ---
    float sunHeight = uSunDir.y;
    vec3 uMoonDir = -uSunDir; 
    
    // Fast sky transition
    float sunVis = clamp((sunHeight + 0.05) * 4.0, 0.0, 1.0);
    
    // Slow star/moon transition
    float nightPresence = smoothstep(0.4, -0.2, sunHeight);
    float starPresence = nightPresence * nightPresence;

    // --- 2. REFINED DYNAMIC SKY BASE ---
    vec3 midnightBlue = vec3(0.01, 0.018, 0.05); 
    vec3 skyBlueBase  = vec3(0.3, 0.6, 0.9); // Your original favorite blue
    
    // Create very tight variations around the base blue
    vec3 deepBlue  = vec3(0.28, 0.58, 0.88); 
    vec3 lightBlue = vec3(0.35, 0.65, 0.95);
    
    // Large, slow-moving variation
    float dayVariation = noise(viewDir * 1.2 + uTime * 0.03);
    vec3 dynamicDayBlue = mix(deepBlue, lightBlue, dayVariation);

    // Final sky background
    vec3 skyBase = mix(midnightBlue, dynamicDayBlue, sunVis);

    // --- 3. HORIZON WRAP ---
    float horizonMask = pow(1.0 - abs(viewDir.y), 4.0); 
    float sunsetIntensity = smoothstep(0.5, 0.0, abs(sunHeight - 0.1));
    float sunDot = max(dot(viewDir, uSunDir), 0.0);
    vec3 horizonGlow = mix(vec3(0.8, 0.1, 0.05), vec3(1.0, 0.35, 0.1), sunsetIntensity);
    skyBase += horizonGlow * horizonMask * sunsetIntensity * mix(0.5, 1.5, pow(sunDot, 2.0)) * 0.5;

    // --- 4. THE SUN (Sharp Pixel Glow) ---
    float sunHalo = pow(sunDot, 12.0) * 0.12; 
    float sunRim = pow(sunDot, 400.0) * 0.3; 
    vec3 sunColor = mix(vec3(1.0, 0.6, 0.3), vec3(1.0, 1.0, 0.9), sunVis);
    skyBase += sunColor * (sunHalo + sunRim) * clamp(sunHeight + 0.1, 0.0, 1.0);

    // --- 5. THE MOON GLOW ---
    float moonDot = max(dot(viewDir, uMoonDir), 0.0);
    float moonGlowAmount = pow(moonDot, 20.0) * 0.15;
    vec3 moonColor = vec3(0.6, 0.8, 1.0); 
    skyBase += moonColor * moonGlowAmount * nightPresence;

    // --- 6. NEBULA & DITHERING ---
    // Added a "Day Haze" that uses the actual sky color to stay subtle
    float hazePresence = smoothstep(0.3, 0.7, sunVis);
    if (hazePresence > 0.0) {
        float hazeNoise = noise(viewDir * 4.0 - uTime * 0.05);
        skyBase += vec3(0.04) * smoothstep(0.5, 0.9, hazeNoise) * hazePresence;
    }

    float nebulaPresence = smoothstep(0.15, 0.0, sunVis); 
    if (nebulaPresence > 0.0) {
        float skyVariation = noise(viewDir * 2.5 + uTime * 0.003);
        skyVariation = smoothstep(0.1, 0.9, skyVariation);
        vec3 nebula = mix(vec3(0.0), vec3(0.05, 0.01, 0.06), skyVariation);
        float tealMask = noise(viewDir * 2.2 - uTime * 0.002);
        nebula = mix(nebula, vec3(0.01, 0.04, 0.05), tealMask * 0.55);
        skyBase += nebula * nebulaPresence;
    }
    
    skyBase += hash(viewDir + uTime) * 0.004;

    // --- 7. STARS (Slow Natural Fade) ---
    vec3 stars = vec3(0.0);
    if (starPresence > 0.001) { 
        vec3 primaryAxis = normalize(vec3(0.0, 1.0, 0.5));
        vec3 secondaryAxis = normalize(vec3(0.3, 0.8, -0.2));
        vec3 dir1 = rotate(viewDir, primaryAxis, uTime * 0.008);
        vec3 dir2 = rotate(viewDir, secondaryAxis, uTime * 0.009);
        
        stars += drawStarLayer(dir1, starPresence, skyBase, 0.998, 0.45);
        stars += drawStarLayer(dir2, starPresence, skyBase, 0.9994, 0.55);
    }

    FragColor = vec4(max(skyBase, stars), 1.0);
}