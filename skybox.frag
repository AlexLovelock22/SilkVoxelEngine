#version 330 core
out vec4 FragColor;
in vec3 TexCoords;

uniform vec3 uSunDir;
uniform sampler2D uStarTex;

void main() {
    vec3 viewDir = normalize(TexCoords);
    float sunHeight = uSunDir.y;
    
    // Sky Colors
    vec3 skyBlue = vec3(0.3, 0.6, 0.9);
    vec3 skyDark = vec3(0.01, 0.01, 0.03);
    float sunVis = clamp(sunHeight + 0.1, 0.0, 1.0); 
    vec3 finalSky = mix(skyDark, skyBlue, sunVis);

    // Stars
    float starAlpha = clamp(1.0 - (sunVis * 4.0), 0.0, 1.0);
    if (starAlpha > 0.0) {
        vec2 starUV = vec2(atan(viewDir.z, viewDir.x) / 6.28, acos(viewDir.y) / 3.14);
        vec3 stars = texture(uStarTex, starUV * 6.0).rgb;
        finalSky += stars * starAlpha;
    }

    FragColor = vec4(finalSky, 1.0);
}