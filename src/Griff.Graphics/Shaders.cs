namespace Griff.Graphics;

/// <summary>
/// The shaders a <see cref="DrawList"/> is drawn with, once for every renderer that speaks GLSL: OpenGL 3.3 puts
/// <see cref="Desktop"/> before each, WebGL 2 (GLSL ES 3.00) <see cref="Web"/>; both run them at full precision, so they
/// work out the same colors. (A Direct3D renderer would have them in HLSL, doing the same.)
/// </summary>
public static class Shaders
{
    /// <summary>What goes before each shader for OpenGL 3.3.</summary>
    public const string Desktop = "#version 330 core\nprecision highp float;\n";

    /// <summary>What goes before each shader for WebGL 2.</summary>
    public const string Web = "#version 300 es\nprecision highp float;\nprecision highp int;\n";

    /// <summary>
    /// Every quad: each instance (a <see cref="DrawQuad"/>, its five attributes) is drawn as two triangles, (top right,
    /// bottom right, top left) and (bottom right, top left, bottom left): corners 0-1-2 and 0-2-3 round the quad, so a quad
    /// of any four points (a shadow) is split the same way always.
    /// </summary>
    public const string Vertex = """
        layout(location = 0) in vec4 aCorners01;
        layout(location = 1) in vec4 aCorners23;
        layout(location = 2) in vec4 aSource;
        layout(location = 3) in vec4 aColor;
        layout(location = 4) in vec4 aFlash;
        out vec2 vUv;
        out vec4 vColor;
        out vec4 vFlash;
        void main()
        {
            vec2 position;
            vec2 uv;
            if (gl_VertexID == 0) { position = aCorners01.zw; uv = vec2(1.0, 0.0); }
            else if (gl_VertexID == 1) { position = aCorners23.xy; uv = vec2(1.0, 1.0); }
            else if (gl_VertexID == 2) { position = aCorners01.xy; uv = vec2(0.0, 0.0); }
            else { position = aCorners23.zw; uv = vec2(0.0, 1.0); }
            vUv = aSource.xy + uv * aSource.zw;
            vColor = aColor;
            vFlash = aFlash;
            gl_Position = vec4(position, 0.0, 1.0);
        }
        """;

    /// <summary>Sprites and text: the texture, tinted, with the flash painted over it.</summary>
    public const string Sprite = """
        in vec2 vUv;
        in vec4 vColor;
        in vec4 vFlash;
        uniform sampler2D uTexture;
        out vec4 fragColor;
        void main()
        {
            vec4 color = texture(uTexture, vUv) * vColor;
            fragColor = vec4(mix(color.rgb, vFlash.rgb, vFlash.a), color.a);
        }
        """;

    /// <summary>
    /// Shapes with no image, drawn from the quad's own coordinates (0..1 across it). Mode 0 is a light: its color, fading
    /// out to the rim as Light.Falloff does, by the softness in the flash's first channel. Mode 1 is a hole: the color
    /// everywhere outside a circle of that radius (a fraction of the quad's half-width), for an iris.
    /// </summary>
    public const string Shape = """
        in vec2 vUv;
        in vec4 vColor;
        in vec4 vFlash;
        uniform int uMode;
        out vec4 fragColor;
        void main()
        {
            float d = length(vUv * 2.0 - 1.0);
            if (uMode == 0)
            {
                float inner = 1.0 - clamp(vFlash.x, 0.0, 1.0);
                float x = clamp((d - inner) / max(1.0 - inner, 1e-4), 0.0, 1.0);
                float amount = d >= 1.0 ? 0.0 : 1.0 - x * x * (3.0 - 2.0 * x);
                fragColor = vec4(vColor.rgb * amount, 1.0);
            }
            else
            {
                float edge = fwidth(d);
                fragColor = vec4(vColor.rgb, vColor.a * smoothstep(vFlash.x - edge, vFlash.x, d));
            }
        }
        """;

    /// <summary>
    /// Screen effects over the finished picture (see ScreenEffects; the uniforms are <see cref="LookValue"/>'s), in the
    /// order a picture goes through them: the curved glass bends where each pixel looks, big pixels sample one spot per
    /// block, the lens splits red and blue, then the colors are graded, lined, vignetted and grained, and last of all
    /// limited to a palette (so a handheld's four greens stay four). The few-color palettes lift the darks first, so a dim
    /// scene doesn't sink into a single shade.
    /// </summary>
    public const string Effects = """
        in vec2 vUv;
        uniform sampler2D uTexture;
        uniform vec2 uSize;
        uniform float uVignette;
        uniform float uScanlines;
        uniform float uCurvature;
        uniform float uPixel;
        uniform float uAberration;
        uniform float uSaturation;
        uniform float uContrast;
        uniform float uBrightness;
        uniform vec4 uTint;
        uniform float uGrain;
        uniform int uPalette;
        uniform float uTime;
        out vec4 fragColor;

        float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

        vec3 palette(vec3 color)
        {
            float l = dot(color, vec3(0.299, 0.587, 0.114));
            float lifted = pow(clamp(l, 0.0, 1.0), 0.55);
            if (uPalette == 1)
            {
                vec3 shades[4] = vec3[](vec3(0.06, 0.22, 0.06), vec3(0.19, 0.38, 0.19), vec3(0.55, 0.67, 0.06), vec3(0.61, 0.74, 0.06));
                return shades[int(clamp(floor(lifted * 4.0), 0.0, 3.0))];
            }
            if (uPalette == 2)
                return vec3(l * 1.07 + 0.05, l * 0.87 + 0.03, l * 0.64);
            if (uPalette == 3)
                return vec3(step(0.5, lifted));
            return color;
        }

        void main()
        {
            vec2 uv = vUv;
            if (uCurvature > 0.0)
            {
                vec2 c = uv * 2.0 - 1.0;
                c *= 1.0 + uCurvature * 0.18 * dot(c, c);
                uv = c * 0.5 + 0.5;
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                {
                    fragColor = vec4(0.0, 0.0, 0.0, 1.0);
                    return;
                }
            }

            float block = max(1.0, uPixel * uSize.y / 720.0);
            if (block > 1.0)
                uv = (floor(uv * uSize / block) + 0.5) * block / uSize;

            vec3 color;
            if (uAberration > 0.0)
            {
                vec2 shift = (uv - 0.5) * uAberration * 0.03;
                color = vec3(texture(uTexture, uv + shift).r, texture(uTexture, uv).g, texture(uTexture, uv - shift).b);
            }
            else
            {
                color = texture(uTexture, uv).rgb;
            }

            color *= uTint.rgb;
            color = (color - 0.5) * uContrast + 0.5 + uBrightness;
            float luma = dot(color, vec3(0.299, 0.587, 0.114));
            color = mix(vec3(luma), color, uSaturation);

            if (uScanlines > 0.0)
            {
                float line = max(2.0, uSize.y / 240.0);
                float y = uv.y * uSize.y / line;
                color *= 1.0 - uScanlines * 0.6 * pow(abs(sin(y * 3.14159)), 2.0);
            }

            if (uVignette > 0.0)
            {
                vec2 d = (vUv - 0.5) * vec2(uSize.x / uSize.y, 1.0);
                color *= 1.0 - uVignette * smoothstep(0.35, 1.0, length(d) * 1.25);
            }

            if (uGrain > 0.0)
                color += (hash(gl_FragCoord.xy + fract(uTime * 7.31) * 113.0) - 0.5) * uGrain * 0.25;

            fragColor = vec4(palette(clamp(color, 0.0, 1.0)), 1.0);
        }
        """;
}
