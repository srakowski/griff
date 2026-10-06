using System.Numerics;
using Griff.Rendering;
using Silk.NET.OpenGL;

namespace Griff.Graphics;

/// <summary>
/// Draws a <see cref="RenderFrame"/> with OpenGL 3.3: textured, tinted quads batched by texture.
/// Must be created and used on the thread that owns the GL context.
/// </summary>
public sealed class SpriteRenderer : IDisposable
{
    private const int MaxQuads = 4096;
    private const int FloatsPerVertex = 8; // clip x, y, u, v, r, g, b, a

    private const string VertexShader = """
        #version 330 core
        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec4 aColor;
        out vec2 vUv;
        out vec4 vColor;
        void main()
        {
            vUv = aUv;
            vColor = aColor;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    private const string FragmentShader = """
        #version 330 core
        in vec2 vUv;
        in vec4 vColor;
        uniform sampler2D uTexture;
        out vec4 fragColor;
        void main()
        {
            fragColor = texture(uTexture, vUv) * vColor;
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
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly float[] _vertices = new float[MaxQuads * 4 * FloatsPerVertex];
    private int _quadCount;
    private Texture? _batchTexture;

    public SpriteRenderer(GL gl, string assetRoot, TextRasterizer? textRasterizer = null)
    {
        _gl = gl;
        _textures = new TextureCache(gl, assetRoot, textRasterizer);
        _program = CreateProgram();

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

        foreach (var sprite in frame.Sprites)
            Add(sprite, frame.ViewProjection);
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
        _textures.Dispose();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }

    private void Add(SpriteDraw sprite, Matrix4x4 viewProjection)
    {
        var world = sprite.World;
        Texture texture;
        if (sprite.Text is { } text)
        {
            if (_textures.GetText(text) is not { } textTexture)
                return;
            texture = textTexture;

            // The draw is one line high; stretch to the rasterized block's line count and aspect.
            var lines = text.Count(c => c == '\n') + 1;
            var aspect = (float)texture.Width / texture.Height;
            world = Matrix4x4.CreateScale(aspect * lines, lines, 1) * world;
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

        var tint = sprite.Tint;
        var toClip = world * viewProjection;
        var offset = _quadCount * 4 * FloatsPerVertex;
        foreach (var (position, uv) in Corners)
        {
            var clip = Vector4.Transform(new Vector4(position, 0, 1), toClip);
            _vertices[offset++] = clip.X / clip.W;
            _vertices[offset++] = clip.Y / clip.W;
            _vertices[offset++] = uv.X;
            _vertices[offset++] = uv.Y;
            _vertices[offset++] = tint.R;
            _vertices[offset++] = tint.G;
            _vertices[offset++] = tint.B;
            _vertices[offset++] = tint.A;
        }

        _quadCount++;
    }

    private unsafe void Flush()
    {
        if (_quadCount == 0 || _batchTexture is null)
            return;

        _gl.BindTexture(TextureTarget.Texture2D, _batchTexture.Handle);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, new ReadOnlySpan<float>(_vertices, 0, _quadCount * 4 * FloatsPerVertex));
        _gl.DrawElements(PrimitiveType.Triangles, (uint)(_quadCount * 6), DrawElementsType.UnsignedInt, (void*)0);

        LastDrawCalls++;
        _quadCount = 0;
        _batchTexture = null;
    }

    private uint CreateProgram()
    {
        var vertex = Compile(ShaderType.VertexShader, VertexShader);
        var fragment = Compile(ShaderType.FragmentShader, FragmentShader);
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
        _gl.Uniform1(_gl.GetUniformLocation(program, "uTexture"), 0);
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
