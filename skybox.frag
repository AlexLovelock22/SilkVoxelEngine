#version 330 core
out vec4 FragColor;
in vec3 TexCoords;

uniform vec3 uSunDir;
uniform float uTime;

float hash(vec3 p) {
    p  = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

void main() {
    vec3 viewDir = normalize(TexCoords);
    float sunHeight = uSunDir.y;
    
    vec3 skyBlue = vec3(0.3, 0.6, 0.9);
    vec3 skyDark = vec3(0.01, 0.01, 0.03);
    float sunVis = clamp(sunHeight + 0.1, 0.0, 1.0); 
    vec3 skyColor = mix(skyDark, skyBlue, sunVis);

    vec3 finalStars = vec3(0.0);
    float starPresence = smoothstep(0.8, -0.2, sunVis);
    
    if (starPresence > 0.0) {
        vec3 starPos = viewDir * 250.0; 
        float n = hash(floor(starPos)); 
        
        if (n > 0.995) { // Slightly stricter threshold for fewer, cleaner stars
            float starSeed = n * 9123.456;
            
            // 1. MODERATED INTENSITY
            // Dropped from 2.5 to 1.6 for a more realistic glow
            float intensity = pow(n, 15.0) * 1.6;
            
            // 2. REFINED SPEED
            // Lowered slightly (0.6 - 1.8 range) to keep it from being "nervous"
            float speed = 0.6 + fract(starSeed * 0.2) * 1.2;
            float pulse = sin(uTime * speed + starSeed); 
            
            // 3. TWINKLE PROBABILITY REDUCTION
            // Changed from 0.75 to 0.85 (Only ~15% of stars now twinkle)
            float twinkleEffect = 1.0;
            if (fract(starSeed * 0.1) > 0.85) {
                twinkleEffect = smoothstep(-1.0, 1.0, pulse);
            }
            
            // 4. APPLY GRADUAL FADE
            float currentBrightness = intensity * mix(1.0, twinkleEffect, starPresence);
            currentBrightness *= starPresence;

            // 5. COLOR LOCK
            float whiteLock = pow(starPresence, 8.0); 
            vec3 ghostColor = skyColor * 1.4; 
            vec3 starWhite = vec3(0.85); // Soft white rather than piercing 1.0
            
            vec3 finalStarColor = mix(ghostColor, starWhite, whiteLock);
            finalStars = finalStarColor * currentBrightness;
        }
    }

    vec3 composite = max(skyColor, finalStars);
    FragColor = vec4(composite, 1.0);
}