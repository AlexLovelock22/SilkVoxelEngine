#version 330 core
out vec4 FragColor;

in vec3 ourColor;
in vec2 TexCoord;
in vec3 vNormal;

uniform sampler2D uTexture;
uniform vec3 uSunDir; 

void main()
{
    vec4 texColor = texture(uTexture, TexCoord);
    if(texColor.a < 0.1) discard;

    vec3 norm = normalize(vNormal);
    vec3 lightDir = normalize(uSunDir);

    // 1. Sunlight Visibility
    // We make the fade-out smoother so sunset lasts longer
    float sunVisibility = clamp(uSunDir.y + 0.2, 0.0, 1.0);
    
    // 2. Diffuse (Direct Light)
    // We use max(dot, 0.0) so faces pointing away from the sun don't get 'negative' light
    float diff = max(dot(norm, lightDir), 0.0);

    // 3. Dynamic Ambient (The "Too Dark" Fix)
    // Day Ambient: 0.4 (Bright shadows)
    // Night Ambient: 0.15 (Dark but visible)
    float ambient = mix(0.35, 0.7, sunVisibility);
    
    // 4. Combine
    // Sunlight only affects the 'diff' part. Ambient is always there.
    float intensity = ambient + (diff * sunVisibility * 0.7);

    // 5. Top Face Boost
    // This mimics "Sky Light" (light coming from the blue sky above)
    if(norm.y > 0.5) intensity += 0.15 * sunVisibility;

    FragColor = texColor * vec4(ourColor * intensity, 1.0);
}