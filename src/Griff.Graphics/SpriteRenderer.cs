using System.Numerics;
using System.Runtime.InteropServices;
using Griff.Rendering;
using Griff.Scenes;
using Silk.NET.OpenGL;

namespace Griff.Graphics;

/// <summary>
/// Draws a <see cref="RenderFrame"/> with OpenGL 3.3: textured, tinted quads batched by texture, each quad one instance
/// of a four-corner strip (its corners worked out here, in clip space). In a dark scene, lights are added up in a light
/// map the size of the screen, which then darkens the world drawn before it.
/// Must be created and used on the thread that owns the GL context.
/// </summary>
public sealed class SpriteRenderer : IDisposable
{
    // Quads a draw call takes at most: a big scene's sprites go in a few of these.
    private const int MaxQuads = 16384;

    // One quad, as an instance: its corners in clip space (top left, top right, bottom right, bottom left), the part of
    // the image on it (u, v, width, height), its tint and its flash (color, and how much).
    [StructLayout(LayoutKind.Sequential)]
    private struct Quad
    {
        public Vector4 Corners01;
        public Vector4 Corners23;
        public Vector4 Source;
        public Vector4 Tint;
        public Vector4 Flash;
    }

    // Each instance is drawn as two triangles, (top right, bottom right, top left) and (bottom right, top left, bottom
    // left): corners 0-1-2 and 0-2-3 round the quad, so a quad of any four points (a shadow) is split the same way always.
    private const string VertexShader = """
        #version 330 core
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

    private const string FragmentShader = """
        #version 330 core
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

    // Shapes with no image, drawn from the quad's own coordinates (UVs 0..1 across it). Mode 0 is a light: its color,
    // fading out to the rim as Light.Falloff does, by the softness in the flash slot's first channel. Mode 1 is a hole:
    // the color everywhere outside a circle of that radius (a fraction of the quad's half-width), for an iris.
    private const string ShapeFragmentShader = """
        #version 330 core
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

    // Screen effects over the finished frame (see ScreenEffects), in the order a picture goes through them: the curved
    // glass bends where each pixel looks, big pixels sample one spot per block, the lens splits red and blue, then the
    // colors are graded, lined, vignetted and grained, and last of all limited to a palette (so a handheld's four greens
    // stay four). The few-color palettes lift the darks first, so a dim scene doesn't sink into a single shade.
    private const string PostFragmentShader = """
        #version 330 core
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

    private readonly GL _gl;
    private readonly TextureCache _textures;
    private readonly uint _program;
    private readonly uint _shapeProgram;
    private readonly int _shapeMode;
    private readonly uint _postProgram;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly Quad[] _quads = new Quad[MaxQuads];
    private int _quadCount;
    private Texture? _batchTexture;

    // The last image drawn and its texture: a run of sprites of one image finds it without looking it up.
    private string? _lastImage;
    private Texture? _lastTexture;

    // The light map: a texture the size of the screen that lights add up in, and the framebuffer that draws into it.
    private uint _lightFramebuffer;
    private uint _lightTexture;
    private uint _lightStencil;
    private (int Width, int Height) _lightSize;

    // With screen effects, the frame is drawn into this texture first, then onto the screen through the effects.
    private uint _sceneFramebuffer;
    private uint _sceneTexture;
    private (int Width, int Height) _sceneSize;

    public SpriteRenderer(GL gl, string assetRoot, TextRasterizer? textRasterizer = null)
    {
        _gl = gl;
        _textures = new TextureCache(gl, assetRoot, textRasterizer);
        _program = CreateProgram(FragmentShader);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uTexture"), 0);
        _shapeProgram = CreateProgram(ShapeFragmentShader);
        _shapeMode = _gl.GetUniformLocation(_shapeProgram, "uMode");
        _postProgram = CreateProgram(PostFragmentShader);
        _gl.Uniform1(_gl.GetUniformLocation(_postProgram, "uTexture"), 0);
        _gl.UseProgram(_program);

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        // Every attribute is the instance's: a quad's four corners all read the same one.
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            var stride = (uint)sizeof(Quad);
            for (uint attribute = 0; attribute < 5; attribute++)
            {
                gl.EnableVertexAttribArray(attribute);
                gl.VertexAttribPointer(attribute, 4, VertexAttribPointerType.Float, false, stride, (void*)(attribute * 4 * sizeof(float)));
                gl.VertexAttribDivisor(attribute, 1);
            }
        }

        gl.BindVertexArray(0);
    }

    /// <summary>Number of draw calls in the last frame, for diagnostics.</summary>
    public int LastDrawCalls { get; private set; }

    public void Render(RenderFrame frame, int width, int height)
    {
        _textures.RefreshChanged();
        (_lastImage, _lastTexture) = (null, null);
        LastDrawCalls = 0;

        var screen = 0;
        if (frame.Effects is not null)
        {
            _gl.GetInteger(GetPName.DrawFramebufferBinding, out screen);
            EnsureTarget(ref _sceneFramebuffer, ref _sceneTexture, ref _sceneSize, width, height);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFramebuffer);
        }

        _gl.Viewport(0, 0, (uint)Math.Max(width, 1), (uint)Math.Max(height, 1));
        var background = frame.Background;
        _gl.ClearColor(background.R, background.G, background.B, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);

        // In the dark, everything up to LitCount is darkened and lit before the rest (the HUD) goes over it.
        var sprites = frame.Sprites;
        for (var i = 0; i < sprites.Count; i++)
        {
            if (i == frame.LitCount && frame.IsDark)
                ApplyLighting(frame, width, height);
            Add(sprites[i], frame.ViewProjection);
        }

        if (frame.IsDark && frame.LitCount >= sprites.Count)
            ApplyLighting(frame, width, height);
        Flush();
        if (frame.Effects is not null)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)screen);
            ApplyEffects(frame.Look, width, height);
        }

        _textures.EndFrame();

        _gl.BindVertexArray(0);
    }

    /// <summary>Reads the current back buffer as top-down RGBA rows (for screenshots and tests).</summary>
    public byte[] Capture(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        _gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels.AsSpan());

        // GL returns bottom-up rows.
        var stride = width * 4;
        var flipped = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
            Array.Copy(pixels, (height - 1 - y) * stride, flipped, y * stride, stride);
        return flipped;
    }

    public void Dispose()
    {
        DeleteLightTarget();
        if (_sceneFramebuffer != 0)
            _gl.DeleteFramebuffer(_sceneFramebuffer);
        if (_sceneTexture != 0)
            _gl.DeleteTexture(_sceneTexture);
        _textures.Dispose();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        _gl.DeleteProgram(_shapeProgram);
        _gl.DeleteProgram(_postProgram);
    }

    private void Add(SpriteDraw sprite, Matrix4x4 viewProjection)
    {
        if (sprite.HoleRadius > 0)
        {
            Flush();
            BeginShapes(1);
            WriteShape(sprite.World * viewProjection, sprite.Tint, sprite.HoleRadius);
            EndShapes();
            return;
        }

        var world = sprite.World;
        Texture texture;
        if (sprite.Text is { } text)
        {
            if (_textures.GetText(text, sprite.Font, sprite.Align) is not { } textTexture)
                return;
            texture = textTexture;

            // The draw is one line high; stretch to the rasterized block's line count and aspect,
            // then slide it so its left or right edge is at the origin for aligned text.
            var lines = text.AsSpan().Count('\n') + 1;
            var width = (float)texture.Width / texture.Height * lines;
            var shift = sprite.Align switch { TextAlign.Left => width / 2, TextAlign.Right => -width / 2, _ => 0f };
            world = Matrix4x4.CreateScale(width, lines, 1) * Matrix4x4.CreateTranslation(shift, 0, 0) * world;
        }
        else if (_lastTexture is not null && string.Equals(sprite.Image, _lastImage, StringComparison.Ordinal))
        {
            texture = _lastTexture;
        }
        else
        {
            texture = _textures.Get(sprite.Image);
            (_lastImage, _lastTexture) = (sprite.Image, texture);
        }

        if (_batchTexture is not null && texture != _batchTexture)
            Flush();
        if (_quadCount == MaxQuads)
            Flush();
        _batchTexture = texture;
        WriteQuad(world * viewProjection, sprite.Source, sprite.Tint, sprite.Flash);
    }

    // One quad into the batch: the unit quad's corners (-0.5..0.5) taken to clip space, the part of the image (row 0 is
    // its top, so the quad's top is v = 0), tint and flash.
    private void WriteQuad(in Matrix4x4 toClip, Vector4 source, Color tint, Color flash)
    {
        ref var quad = ref _quads[_quadCount++];
        quad.Corners01 = Pair(ToClip(-0.5f, 0.5f, toClip), ToClip(0.5f, 0.5f, toClip));
        quad.Corners23 = Pair(ToClip(0.5f, -0.5f, toClip), ToClip(-0.5f, -0.5f, toClip));
        quad.Source = source;
        quad.Tint = new Vector4(tint.R, tint.G, tint.B, tint.A);
        quad.Flash = new Vector4(flash.R, flash.G, flash.B, flash.A);
    }

    private static Vector2 ToClip(float x, float y, in Matrix4x4 toClip)
    {
        var clip = Vector4.Transform(new Vector4(x, y, 0, 1), toClip);
        return new Vector2(clip.X / clip.W, clip.Y / clip.W);
    }

    private static Vector4 Pair(Vector2 a, Vector2 b) => new(a.X, a.Y, b.X, b.Y);

    // Adds the lights up in the light map, starting from the ambient light, then multiplies what's been drawn by it.
    private void ApplyLighting(RenderFrame frame, int width, int height)
    {
        Flush();
        EnsureLightTarget(width, height);
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out var screen);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _lightFramebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(frame.Ambient.R, frame.Ambient.G, frame.Ambient.B, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        // By index, here and below: going through the lists as such would make garbage every frame.
        var lights = frame.Lights;
        BeginShapes(0);
        for (var i = 0; i < lights.Count; i++)
        {
            if (lights[i].Casters.Count == 0)
                WriteLight(lights[i], frame.ViewProjection);
        }

        EndShapes();

        // A light with shadows on its own: its shadows mark the stencil, and it's added up only where they didn't.
        for (var i = 0; i < lights.Count; i++)
        {
            var light = lights[i];
            if (light.Casters.Count == 0)
                continue;
            _gl.Enable(EnableCap.StencilTest);
            _gl.ClearStencil(0);
            _gl.Clear(ClearBufferMask.StencilBufferBit);
            _gl.ColorMask(false, false, false, false);
            _gl.StencilFunc(StencilFunction.Always, 1, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            DrawShadows(light, frame.ViewProjection);
            _gl.ColorMask(true, true, true, true);
            _gl.StencilFunc(StencilFunction.Equal, 0, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            BeginShapes(0);
            WriteLight(light, frame.ViewProjection);
            EndShapes();
            _gl.Disable(EnableCap.StencilTest);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)screen);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.Zero);

        // The light map over the whole screen, the right way up (a texture drawn into starts at the bottom).
        _gl.BindTexture(TextureTarget.Texture2D, _lightTexture);
        WriteQuad(Matrix4x4.CreateScale(2, 2, 1), new Vector4(0, 1, 1, -1), Color.White, Color.Transparent);
        DrawBatch();

        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    // The frame drawn so far, onto the screen through the effects.
    private void ApplyEffects(ScreenLook effects, int width, int height)
    {
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Disable(EnableCap.Blend);
        _gl.UseProgram(_postProgram);
        void Set(string name, float value) => _gl.Uniform1(_gl.GetUniformLocation(_postProgram, name), value);
        _gl.Uniform2(_gl.GetUniformLocation(_postProgram, "uSize"), (float)width, height);
        Set("uVignette", Math.Clamp(effects.Vignette, 0, 1));
        Set("uScanlines", Math.Clamp(effects.Scanlines, 0, 1));
        Set("uCurvature", Math.Clamp(effects.Curvature, 0, 1));
        Set("uPixel", Math.Clamp(effects.PixelSize, 1, 64));
        Set("uAberration", Math.Clamp(effects.Aberration, 0, 1));
        Set("uSaturation", Math.Max(effects.Saturation, 0));
        Set("uContrast", Math.Max(effects.Contrast, 0));
        Set("uBrightness", Math.Clamp(effects.Brightness, -1, 1));
        _gl.Uniform4(_gl.GetUniformLocation(_postProgram, "uTint"), effects.Tint.R, effects.Tint.G, effects.Tint.B, 1f);
        Set("uGrain", Math.Clamp(effects.Grain, 0, 1));
        _gl.Uniform1(_gl.GetUniformLocation(_postProgram, "uPalette"), (int)effects.Palette);
        Set("uTime", (float)(_clock.Elapsed.TotalSeconds % 1000));
        _gl.BindTexture(TextureTarget.Texture2D, _sceneTexture);
        WriteQuad(Matrix4x4.CreateScale(2, 2, 1), new Vector4(0, 1, 1, -1), Color.White, Color.Transparent);
        DrawBatch();
        _gl.UseProgram(_program);
        _gl.Enable(EnableCap.Blend);
    }

    private void WriteLight(in LightDraw light, in Matrix4x4 viewProjection) =>
        WriteShape(Matrix4x4.CreateScale(light.Radius * 2, light.Radius * 2, 1) * Matrix4x4.CreateTranslation(light.Center.X, light.Center.Y, 0) * viewProjection,
            light.Color, light.Softness);

    // Each casting side's shadow as two quads fanned from its near end (the second folds its last corner onto itself).
    private void DrawShadows(LightDraw light, Matrix4x4 viewProjection)
    {
        _gl.UseProgram(_shapeProgram);
        _gl.Uniform1(_shapeMode, 0);
        var casters = light.Casters;
        for (var i = 0; i < casters.Count; i++)
        {
            var edge = casters[i];
            var (a, b, farB, farMiddle, farA) = Shadows.Shadow(edge, light.Center, light.Radius * 2);
            WriteCorners(viewProjection, a, b, farB, farMiddle);
            WriteCorners(viewProjection, a, farMiddle, farA, farA);
            if (_quadCount >= MaxQuads - 1)
                DrawBatch();
        }

        DrawBatch();
        _gl.UseProgram(_program);
    }

    // A quad from four world points, in order round it, with no image.
    private void WriteCorners(Matrix4x4 viewProjection, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) =>
        _quads[_quadCount++] = new Quad
        {
            Corners01 = Pair(ToClip(p0.X, p0.Y, viewProjection), ToClip(p1.X, p1.Y, viewProjection)),
            Corners23 = Pair(ToClip(p2.X, p2.Y, viewProjection), ToClip(p3.X, p3.Y, viewProjection)),
        };

    // Lights (mode 0) or holes (mode 1) with the shape program: BeginShapes, a WriteShape for each quad (already in clip
    // space), then EndShapes, which puts the sprite program back on.
    private void BeginShapes(int mode)
    {
        _gl.UseProgram(_shapeProgram);
        _gl.Uniform1(_shapeMode, mode);
    }

    private void WriteShape(in Matrix4x4 toClip, Color color, float parameter)
    {
        WriteQuad(toClip, new Vector4(0, 0, 1, 1), color, new Color(parameter, 0, 0, 0));
        if (_quadCount == MaxQuads)
            DrawBatch();
    }

    private void EndShapes()
    {
        DrawBatch();
        _gl.UseProgram(_program);
    }

    // Draws the quads written so far with whatever program and texture are bound. Each batch goes into fresh storage
    // (the last is left to the draws still reading it), so the GPU never has to finish one before the next is written.
    private void DrawBatch()
    {
        if (_quadCount == 0)
            return;
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, new ReadOnlySpan<Quad>(_quads, 0, _quadCount), BufferUsageARB.StreamDraw);
        _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)_quadCount);
        LastDrawCalls++;
        _quadCount = 0;
    }

    private void Flush()
    {
        if (_quadCount == 0 || _batchTexture is null)
            return;

        _gl.BindTexture(TextureTarget.Texture2D, _batchTexture.Handle);
        DrawBatch();
        _batchTexture = null;
    }

    private unsafe void EnsureLightTarget(int width, int height)
    {
        if (_lightFramebuffer != 0 && _lightSize == (width, height))
            return;
        DeleteLightTarget();

        _lightTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _lightTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        _gl.GetInteger(GetPName.DrawFramebufferBinding, out var screen);
        _lightFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _lightFramebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _lightTexture, 0);

        // Shadows are marked in a stencil buffer beside it.
        _lightStencil = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _lightStencil);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _lightStencil);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)screen);
        _lightSize = (width, height);
    }

    // A plain color target the size of the screen (no stencil): for the frame before screen effects.
    private unsafe void EnsureTarget(ref uint framebuffer, ref uint texture, ref (int Width, int Height) size, int width, int height)
    {
        if (framebuffer != 0 && size == (width, height))
            return;
        if (framebuffer != 0)
            _gl.DeleteFramebuffer(framebuffer);
        if (texture != 0)
            _gl.DeleteTexture(texture);

        texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        _gl.GetInteger(GetPName.DrawFramebufferBinding, out var screen);
        framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)screen);
        size = (width, height);
    }

    private void DeleteLightTarget()
    {
        if (_lightFramebuffer != 0)
            _gl.DeleteFramebuffer(_lightFramebuffer);
        if (_lightTexture != 0)
            _gl.DeleteTexture(_lightTexture);
        if (_lightStencil != 0)
            _gl.DeleteRenderbuffer(_lightStencil);
        (_lightFramebuffer, _lightTexture, _lightStencil, _lightSize) = (0, 0, 0, (0, 0));
    }

    private uint CreateProgram(string fragmentSource)
    {
        var vertex = Compile(ShaderType.VertexShader, VertexShader);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, fragment);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0)
            throw new InvalidOperationException($"Sprite shader failed to link: {_gl.GetProgramInfoLog(program)}");

        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
        _gl.UseProgram(program);
        return program;
    }

    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled == 0)
            throw new InvalidOperationException($"{type} failed to compile: {_gl.GetShaderInfoLog(shader)}");
        return shader;
    }
}
