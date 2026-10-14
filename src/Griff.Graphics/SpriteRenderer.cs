using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;
using Silk.NET.OpenGL;

namespace Griff.Graphics;

/// <summary>
/// Draws a <see cref="RenderFrame"/> with OpenGL 3.3: textured, tinted quads batched by texture. In a dark scene, lights
/// are added up in a light map the size of the screen, which then darkens the world drawn before it.
/// Must be created and used on the thread that owns the GL context.
/// </summary>
public sealed class SpriteRenderer : IDisposable
{
    private const int MaxQuads = 4096;
    private const int FloatsPerVertex = 12; // clip x, y, u, v, tint r, g, b, a, flash r, g, b, amount

    private const string VertexShader = """
        #version 330 core
        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec4 aColor;
        layout(location = 3) in vec4 aFlash;
        out vec2 vUv;
        out vec4 vColor;
        out vec4 vFlash;
        void main()
        {
            vUv = aUv;
            vColor = aColor;
            vFlash = aFlash;
            gl_Position = vec4(aPosition, 0.0, 1.0);
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

    // Unit quad corners with their UVs. Image row 0 is the top, so the quad's top maps to v = 0.
    private static readonly (Vector2 Position, Vector2 Uv)[] Corners =
    [
        (new(-0.5f, 0.5f), new(0, 0)),
        (new(0.5f, 0.5f), new(1, 0)),
        (new(0.5f, -0.5f), new(1, 1)),
        (new(-0.5f, -0.5f), new(0, 1)),
    ];

    private readonly GL _gl;
    private readonly TextureCache _textures;
    private readonly uint _program;
    private readonly uint _shapeProgram;
    private readonly int _shapeMode;
    private readonly uint _postProgram;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly float[] _vertices = new float[MaxQuads * 4 * FloatsPerVertex];
    private int _quadCount;
    private Texture? _batchTexture;

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

        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_vertices.Length * sizeof(float)), ReadOnlySpan<float>.Empty, BufferUsageARB.DynamicDraw);

        _ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        var indices = new uint[MaxQuads * 6];
        for (uint q = 0, i = 0; q < MaxQuads; q++, i += 6)
        {
            var v = q * 4;
            (indices[i], indices[i + 1], indices[i + 2]) = (v, v + 1, v + 2);
            (indices[i + 3], indices[i + 4], indices[i + 5]) = (v, v + 2, v + 3);
        }

        gl.BufferData<uint>(BufferTargetARB.ElementArrayBuffer, indices, BufferUsageARB.StaticDraw);

        var stride = (uint)(FloatsPerVertex * sizeof(float));
        unsafe
        {
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));
            gl.EnableVertexAttribArray(3);
            gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));
        }

        gl.BindVertexArray(0);
    }

    /// <summary>Number of draw calls in the last frame, for diagnostics.</summary>
    public int LastDrawCalls { get; private set; }

    public void Render(RenderFrame frame, int width, int height)
    {
        _textures.RefreshChanged();
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
        _gl.DeleteBuffer(_ebo);
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
            DrawShapes(1, [(sprite.World * viewProjection, sprite.Tint, sprite.HoleRadius)]);
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
            var lines = text.Count(c => c == '\n') + 1;
            var width = (float)texture.Width / texture.Height * lines;
            var shift = sprite.Align switch { TextAlign.Left => width / 2, TextAlign.Right => -width / 2, _ => 0f };
            world = Matrix4x4.CreateScale(width, lines, 1) * Matrix4x4.CreateTranslation(shift, 0, 0) * world;
        }
        else
        {
            texture = _textures.Get(sprite.Image);
        }

        if (_batchTexture is not null && texture != _batchTexture)
            Flush();
        if (_quadCount == MaxQuads)
            Flush();
        _batchTexture = texture;
        WriteQuad(world * viewProjection, sprite.Source, sprite.Tint, sprite.Flash);
    }

    // One quad's four vertices into the batch: its corners taken to clip space, the part of the image, tint and flash.
    private void WriteQuad(Matrix4x4 toClip, Vector4 source, Color tint, Color flash)
    {
        var offset = _quadCount * 4 * FloatsPerVertex;
        foreach (var (position, uv) in Corners)
        {
            var clip = Vector4.Transform(new Vector4(position, 0, 1), toClip);
            _vertices[offset++] = clip.X / clip.W;
            _vertices[offset++] = clip.Y / clip.W;
            _vertices[offset++] = source.X + uv.X * source.Z;
            _vertices[offset++] = source.Y + uv.Y * source.W;
            _vertices[offset++] = tint.R;
            _vertices[offset++] = tint.G;
            _vertices[offset++] = tint.B;
            _vertices[offset++] = tint.A;
            _vertices[offset++] = flash.R;
            _vertices[offset++] = flash.G;
            _vertices[offset++] = flash.B;
            _vertices[offset++] = flash.A;
        }

        _quadCount++;
    }

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
        DrawShapes(0, frame.Lights.Where(l => l.Casters.Count == 0).Select(l => LightQuad(l, frame.ViewProjection)));

        // A light with shadows on its own: its shadows mark the stencil, and it's added up only where they didn't.
        foreach (var light in frame.Lights.Where(l => l.Casters.Count > 0))
        {
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
            DrawShapes(0, [LightQuad(light, frame.ViewProjection)]);
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

    private static (Matrix4x4 ToClip, Color Color, float Parameter) LightQuad(LightDraw light, Matrix4x4 viewProjection) =>
        (Matrix4x4.CreateScale(light.Radius * 2, light.Radius * 2, 1) * Matrix4x4.CreateTranslation(light.Center.X, light.Center.Y, 0) * viewProjection,
         light.Color, light.Softness);

    // Each casting side's shadow as two quads fanned from its near end (the second folds its last corner onto itself).
    private void DrawShadows(LightDraw light, Matrix4x4 viewProjection)
    {
        _gl.UseProgram(_shapeProgram);
        _gl.Uniform1(_shapeMode, 0);
        foreach (var edge in light.Casters)
        {
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
    private void WriteCorners(Matrix4x4 viewProjection, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        var offset = _quadCount * 4 * FloatsPerVertex;
        foreach (var point in (ReadOnlySpan<Vector2>)[p0, p1, p2, p3])
        {
            var clip = Vector4.Transform(new Vector4(point, 0, 1), viewProjection);
            _vertices[offset++] = clip.X / clip.W;
            _vertices[offset++] = clip.Y / clip.W;
            for (var i = 0; i < FloatsPerVertex - 2; i++)
                _vertices[offset++] = 0;
        }

        _quadCount++;
    }

    // Lights (mode 0) or holes (mode 1) with the shape program, each quad already in clip space; the sprite program is
    // back on afterwards.
    private void DrawShapes(int mode, IEnumerable<(Matrix4x4 ToClip, Color Color, float Parameter)> quads)
    {
        _gl.UseProgram(_shapeProgram);
        _gl.Uniform1(_shapeMode, mode);
        foreach (var (toClip, color, parameter) in quads)
        {
            WriteQuad(toClip, new Vector4(0, 0, 1, 1), color, new Color(parameter, 0, 0, 0));
            if (_quadCount == MaxQuads)
                DrawBatch();
        }

        DrawBatch();
        _gl.UseProgram(_program);
    }

    // Draws the quads written so far with whatever program and texture are bound.
    private unsafe void DrawBatch()
    {
        if (_quadCount == 0)
            return;
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, new ReadOnlySpan<float>(_vertices, 0, _quadCount * 4 * FloatsPerVertex));
        _gl.DrawElements(PrimitiveType.Triangles, (uint)(_quadCount * 6), DrawElementsType.UnsignedInt, (void*)0);
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
