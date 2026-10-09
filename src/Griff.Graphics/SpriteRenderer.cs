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
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly float[] _vertices = new float[MaxQuads * 4 * FloatsPerVertex];
    private int _quadCount;
    private Texture? _batchTexture;

    // The light map: a texture the size of the screen that lights add up in, and the framebuffer that draws into it.
    private uint _lightFramebuffer;
    private uint _lightTexture;
    private (int Width, int Height) _lightSize;

    public SpriteRenderer(GL gl, string assetRoot, TextRasterizer? textRasterizer = null)
    {
        _gl = gl;
        _textures = new TextureCache(gl, assetRoot, textRasterizer);
        _program = CreateProgram(FragmentShader);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uTexture"), 0);
        _shapeProgram = CreateProgram(ShapeFragmentShader);
        _shapeMode = _gl.GetUniformLocation(_shapeProgram, "uMode");

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
        _textures.Dispose();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        _gl.DeleteProgram(_shapeProgram);
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
        DrawShapes(0, frame.Lights.Select(l =>
            (Matrix4x4.CreateScale(l.Radius * 2, l.Radius * 2, 1) * Matrix4x4.CreateTranslation(l.Center.X, l.Center.Y, 0) * frame.ViewProjection,
             l.Color, l.Softness)));

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)screen);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.Zero);

        // The light map over the whole screen, the right way up (a texture drawn into starts at the bottom).
        _gl.BindTexture(TextureTarget.Texture2D, _lightTexture);
        WriteQuad(Matrix4x4.CreateScale(2, 2, 1), new Vector4(0, 1, 1, -1), Color.White, Color.Transparent);
        DrawBatch();

        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
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
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)screen);
        _lightSize = (width, height);
    }

    private void DeleteLightTarget()
    {
        if (_lightFramebuffer != 0)
            _gl.DeleteFramebuffer(_lightFramebuffer);
        if (_lightTexture != 0)
            _gl.DeleteTexture(_lightTexture);
        (_lightFramebuffer, _lightTexture, _lightSize) = (0, 0, (0, 0));
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
