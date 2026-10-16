using System.Numerics;
using Griff.Fonts;
using Griff.Hosting;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace Griff.Graphics;

/// <summary>
/// The desktop's renderer: a frame's <see cref="DrawList"/> drawn with OpenGL 3.3. Each run of quads is one instanced
/// draw of a four-corner strip (an instance a <see cref="DrawQuad"/>), in batches of up to 16,384 that each go into fresh
/// buffer storage, so the GPU never waits for the last batch to be read. The light map and the picture screen effects are
/// drawn from are framebuffers the size of the screen, the light map with a stencil buffer its shadows mark.
/// Must be created and used on the thread that owns the GL context.
/// </summary>
public sealed class OpenGLRenderer : QuadRenderer
{
    // Quads a draw call takes at most: a big scene's sprites go in a few of these.
    private const int MaxQuads = 16384;

    private readonly GL _gl;
    private readonly uint _program;
    private readonly uint _shapeProgram;
    private readonly int _shapeMode;
    private readonly uint _effectsProgram;
    private readonly EffectsUniforms _effects;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly DrawQuad[] _whole = new DrawQuad[1];

    // Textures: white, the missing image's checker, each image's (0 until it's loaded, or if it can't be) and each glyph page's.
    private readonly uint _white;
    private readonly uint _missing;
    private readonly List<uint> _images = [0];
    private readonly List<uint> _glyphPages = [];

    // The light map: a texture the size of the screen that lights add up in, and the framebuffer (with a stencil buffer
    // for shadows) that draws into it.
    private Target _light;

    // With screen effects, the frame is drawn into this picture first, then onto the screen through the effects.
    private Target _picture;

    public OpenGLRenderer(GL gl, IAssetSource? assets)
        : base(assets, installedFonts: true)
    {
        _gl = gl;
        _program = CreateProgram(Shaders.Sprite);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uTexture"), 0);
        _shapeProgram = CreateProgram(Shaders.Shape);
        _shapeMode = _gl.GetUniformLocation(_shapeProgram, "uMode");
        _effectsProgram = CreateProgram(Shaders.Effects);
        _gl.Uniform1(_gl.GetUniformLocation(_effectsProgram, "uTexture"), 0);
        _effects = new EffectsUniforms(gl, _effectsProgram);
        _gl.UseProgram(_program);

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        // Every attribute is the instance's: a quad's four corners all read the same one.
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            var stride = (uint)sizeof(DrawQuad);
            for (uint attribute = 0; attribute < 5; attribute++)
            {
                gl.EnableVertexAttribArray(attribute);
                gl.VertexAttribPointer(attribute, 4, VertexAttribPointerType.Float, false, stride, (void*)(attribute * 4 * sizeof(float)));
                gl.VertexAttribDivisor(attribute, 1);
            }
        }

        gl.BindVertexArray(0);
        _white = Upload([255, 255, 255, 255], 1, 1, smooth: false);
        _missing = Upload([255, 0, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 0, 255, 255], 2, 2, smooth: false);
    }

    public override byte[] Capture(int width, int height)
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

    protected override void Draw(DrawList list)
    {
        LastDrawCalls = 0;
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out var screen);
        var (width, height) = (list.Width, list.Height);
        var values = list.Values;
        var quads = list.Quads;
        var target = (uint)screen;

        // Clip space covers the whole target; a view only clips drawing to its part.
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        foreach (ref readonly var command in list.Commands)
        {
            var at = command.Value;
            switch (command.Op)
            {
                case DrawOp.Target:
                    if ((DrawTarget)at == DrawTarget.Picture)
                    {
                        EnsureTarget(ref _picture, width, height, stencil: false);
                        target = _picture.Framebuffer;
                    }
                    else
                    {
                        target = (uint)screen;
                    }

                    _gl.BindFramebuffer(FramebufferTarget.Framebuffer, target);
                    break;
                case DrawOp.View:
                    Clip(new PixelArea((int)values[at], (int)values[at + 1], (int)values[at + 2], (int)values[at + 3]), height);
                    break;
                case DrawOp.Clear:
                    _gl.ClearColor(values[at], values[at + 1], values[at + 2], 1f);
                    _gl.Clear(ClearBufferMask.ColorBufferBit);
                    break;
                case DrawOp.Sprites:
                    _gl.UseProgram(_program);
                    _gl.BindTexture(TextureTarget.Texture2D, TextureOf(command.Texture));
                    DrawQuads(quads, command.First, command.Count);
                    break;
                case DrawOp.Holes:
                    UseShape(1);
                    DrawQuads(quads, command.First, command.Count);
                    break;
                case DrawOp.BeginLight:
                    EnsureTarget(ref _light, width, height, stencil: true);
                    _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _light.Framebuffer);
                    _gl.ClearColor(values[at], values[at + 1], values[at + 2], 1f);
                    _gl.Clear(ClearBufferMask.ColorBufferBit);
                    _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
                    break;
                case DrawOp.Lights:
                    UseShape(0);
                    DrawQuads(quads, command.First, command.Count);
                    break;
                case DrawOp.Shadows:
                    // The shadows mark the stencil, and the light after is added up only where they didn't.
                    _gl.Enable(EnableCap.StencilTest);
                    _gl.ClearStencil(0);
                    _gl.Clear(ClearBufferMask.StencilBufferBit);
                    _gl.ColorMask(false, false, false, false);
                    _gl.StencilFunc(StencilFunction.Always, 1, 0xFF);
                    _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
                    UseShape(0);
                    DrawQuads(quads, command.First, command.Count);
                    _gl.ColorMask(true, true, true, true);
                    _gl.StencilFunc(StencilFunction.Equal, 0, 0xFF);
                    _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
                    break;
                case DrawOp.ShadowedLights:
                    UseShape(0);
                    DrawQuads(quads, command.First, command.Count);
                    _gl.Disable(EnableCap.StencilTest);
                    break;
                case DrawOp.EndLight:
                    // The light map over the view (drawing is clipped to it), multiplying what's there.
                    _gl.BindFramebuffer(FramebufferTarget.Framebuffer, target);
                    _gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.Zero);
                    _gl.UseProgram(_program);
                    _gl.BindTexture(TextureTarget.Texture2D, _light.Texture);
                    DrawWhole();
                    _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    break;
                case DrawOp.Effects:
                    _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)screen);
                    Clip(new PixelArea(0, 0, width, height), height);
                    _gl.Disable(EnableCap.Blend);
                    _gl.UseProgram(_effectsProgram);
                    _effects.Set(_gl, values.Slice(at, (int)LookValue.Count), width, height);
                    _gl.BindTexture(TextureTarget.Texture2D, _picture.Texture);
                    DrawWhole();
                    _gl.Enable(EnableCap.Blend);
                    break;
            }
        }

        _gl.Disable(EnableCap.ScissorTest);
        _gl.UseProgram(_program);
        _gl.BindVertexArray(0);
    }

    protected override void LoadImage(int texture, string path)
    {
        while (_images.Count <= texture)
            _images.Add(0);
        if (_images[texture] != 0)
            _gl.DeleteTexture(_images[texture]);
        _images[texture] = 0;
        if (!File.Exists(path))
            return;
        try
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            _images[texture] = Upload(image.Data, image.Width, image.Height, smooth: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Mid-save or not an image: the checker, until the file changes again.
        }
    }

    protected override void LoadImage(int texture, byte[] encoded)
    {
        while (_images.Count <= texture)
            _images.Add(0);
        if (_images[texture] != 0)
            _gl.DeleteTexture(_images[texture]);
        _images[texture] = 0;
        if (encoded.Length == 0)
            return;
        try
        {
            var image = ImageResult.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
            _images[texture] = Upload(image.Data, image.Width, image.Height, smooth: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Not an image: the checker.
        }
    }

    protected override void LoadGlyphRows(int texture, int top, int rows, Span<byte> rgba)
    {
        var page = DrawList.GlyphPage(texture);
        while (_glyphPages.Count <= page)
            _glyphPages.Add(Upload(null, GlyphPage.Size, GlyphPage.Size, smooth: true));
        _gl.BindTexture(TextureTarget.Texture2D, _glyphPages[page]);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, top, GlyphPage.Size, (uint)rows, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
    }

    protected override void Dispose(bool disposing)
    {
        DeleteTarget(ref _light);
        DeleteTarget(ref _picture);
        foreach (var image in _images)
        {
            if (image != 0)
                _gl.DeleteTexture(image);
        }

        foreach (var page in _glyphPages)
            _gl.DeleteTexture(page);
        _gl.DeleteTexture(_white);
        _gl.DeleteTexture(_missing);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        _gl.DeleteProgram(_shapeProgram);
        _gl.DeleteProgram(_effectsProgram);
        base.Dispose(disposing);
    }

    private uint TextureOf(int texture)
    {
        if (texture > 0)
            return texture < _images.Count && _images[texture] != 0 ? _images[texture] : _missing;
        if (texture < 0)
        {
            var page = DrawList.GlyphPage(texture);
            return page < _glyphPages.Count ? _glyphPages[page] : _white;
        }

        return _white;
    }

    // Drawing goes only into the view (which GL counts from the bottom left).
    private void Clip(PixelArea view, int height) =>
        _gl.Scissor(view.Left, height - view.Top - view.Height, (uint)Math.Max(view.Width, 0), (uint)Math.Max(view.Height, 0));

    private void UseShape(int mode)
    {
        _gl.UseProgram(_shapeProgram);
        _gl.Uniform1(_shapeMode, mode);
    }

    // Draws quads with whatever program and texture are bound, a batch at a time. Each batch goes into fresh storage (the
    // last is left to the draws still reading it), so the GPU never has to finish one before the next is written.
    private void DrawQuads(ReadOnlySpan<DrawQuad> quads, int first, int count)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        for (var done = 0; done < count; done += MaxQuads)
        {
            var batch = Math.Min(MaxQuads, count - done);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, quads.Slice(first + done, batch), BufferUsageARB.StreamDraw);
            _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)batch);
            LastDrawCalls++;
        }
    }

    // A quad over the whole target, showing a target's texture the right way up (one drawn into starts at the bottom).
    private void DrawWhole()
    {
        _whole[0] = new DrawQuad
        {
            Corners01 = new Vector4(-1, 1, 1, 1),
            Corners23 = new Vector4(1, -1, -1, -1),
            Source = new Vector4(0, 1, 1, -1),

            Tint = Vector4.One,
        };
        DrawQuads(_whole, 0, 1);
    }

    private unsafe void EnsureTarget(ref Target target, int width, int height, bool stencil)
    {
        if (target.Framebuffer != 0 && target.Size == (width, height))
            return;
        DeleteTarget(ref target);

        target.Texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, target.Texture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        _gl.GetInteger(GetPName.DrawFramebufferBinding, out var bound);
        target.Framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.Framebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.Texture, 0);
        if (stencil)
        {
            target.Stencil = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, target.Stencil);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, target.Stencil);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)bound);
        target.Size = (width, height);
    }

    private void DeleteTarget(ref Target target)
    {
        if (target.Framebuffer != 0)
            _gl.DeleteFramebuffer(target.Framebuffer);
        if (target.Texture != 0)
            _gl.DeleteTexture(target.Texture);
        if (target.Stencil != 0)
            _gl.DeleteRenderbuffer(target.Stencil);
        target = default;
    }

    private unsafe uint Upload(byte[]? rgba, int width, int height, bool smooth)
    {
        var handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        if (rgba is null)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        else
            _gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
        var filter = smooth ? (int)TextureMinFilter.Linear : (int)TextureMinFilter.Nearest;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return handle;
    }

    private uint CreateProgram(string fragmentSource)
    {
        var vertex = Compile(ShaderType.VertexShader, Shaders.Vertex);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, fragment);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0)
            throw new InvalidOperationException($"A shader failed to link: {_gl.GetProgramInfoLog(program)}");

        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
        _gl.UseProgram(program);
        return program;
    }

    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, Shaders.Desktop + source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled == 0)
            throw new InvalidOperationException($"{type} failed to compile: {_gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    private struct Target
    {
        public uint Framebuffer;
        public uint Texture;
        public uint Stencil;
        public (int Width, int Height) Size;
    }

    // Where the effects shader's settings go, looked up once.
    private sealed class EffectsUniforms(GL gl, uint program)
    {
        private readonly int _size = gl.GetUniformLocation(program, "uSize");
        private readonly int _vignette = gl.GetUniformLocation(program, "uVignette");
        private readonly int _scanlines = gl.GetUniformLocation(program, "uScanlines");
        private readonly int _curvature = gl.GetUniformLocation(program, "uCurvature");
        private readonly int _pixel = gl.GetUniformLocation(program, "uPixel");
        private readonly int _aberration = gl.GetUniformLocation(program, "uAberration");
        private readonly int _saturation = gl.GetUniformLocation(program, "uSaturation");
        private readonly int _contrast = gl.GetUniformLocation(program, "uContrast");
        private readonly int _brightness = gl.GetUniformLocation(program, "uBrightness");
        private readonly int _tint = gl.GetUniformLocation(program, "uTint");
        private readonly int _grain = gl.GetUniformLocation(program, "uGrain");
        private readonly int _palette = gl.GetUniformLocation(program, "uPalette");
        private readonly int _time = gl.GetUniformLocation(program, "uTime");

        public void Set(GL gl, ReadOnlySpan<float> look, int width, int height)
        {
            gl.Uniform2(_size, (float)width, height);
            gl.Uniform1(_vignette, look[(int)LookValue.Vignette]);
            gl.Uniform1(_scanlines, look[(int)LookValue.Scanlines]);
            gl.Uniform1(_curvature, look[(int)LookValue.Curvature]);
            gl.Uniform1(_pixel, look[(int)LookValue.PixelSize]);
            gl.Uniform1(_aberration, look[(int)LookValue.Aberration]);
            gl.Uniform1(_saturation, look[(int)LookValue.Saturation]);
            gl.Uniform1(_contrast, look[(int)LookValue.Contrast]);
            gl.Uniform1(_brightness, look[(int)LookValue.Brightness]);
            gl.Uniform4(_tint, look[(int)LookValue.TintRed], look[(int)LookValue.TintGreen], look[(int)LookValue.TintBlue], 1f);
            gl.Uniform1(_grain, look[(int)LookValue.Grain]);
            gl.Uniform1(_palette, (int)look[(int)LookValue.Palette]);
            gl.Uniform1(_time, look[(int)LookValue.Time]);
        }
    }
}
