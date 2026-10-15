using System.Runtime.InteropServices;
using Griff.Rendering;
using Silk.NET.OpenGL;

namespace Griff.Graphics;

/// <summary>
/// An OpenGL context on a window that's never shown, drawing into a framebuffer of its own this many pixels big, with an
/// <see cref="OpenGLRenderer"/>: frames drawn and read back with no window on the screen, for benchmarks and for comparing
/// the desktop's pixels with the browser's. Windows only (WGL). Used on the thread that made it.
/// </summary>
public sealed class OffscreenGL : IDisposable
{
    private readonly IntPtr _window, _dc, _context, _opengl32;
    private readonly uint _framebuffer, _color;

    public OffscreenGL(int width, int height, string assetRoot)
    {
        (Width, Height) = (width, height);
        _window = CreateWindowEx(0, "static", "Griff", 0x80000000, 0, 0, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        _dc = GetDC(_window);
        var format = new PixelFormat { Size = (ushort)Marshal.SizeOf<PixelFormat>(), Version = 1, Flags = 0x4 | 0x20 | 0x1, ColorBits = 32, AlphaBits = 8, DepthBits = 24, StencilBits = 8 };
        SetPixelFormat(_dc, ChoosePixelFormat(_dc, ref format), ref format);
        _context = wglCreateContext(_dc);
        if (_context == IntPtr.Zero || !wglMakeCurrent(_dc, _context))
            throw new InvalidOperationException("No OpenGL context could be made.");
        _opengl32 = LoadLibrary("opengl32.dll");
        GL = GL.GetApi(name =>
        {
            var address = wglGetProcAddress(name);
            return address is 0 or 1 or 2 or 3 or -1 ? GetProcAddress(_opengl32, name) : address;
        });

        _framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _color = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _color);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)width, (uint)height);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _color);
        Renderer = new OpenGLRenderer(GL, assetRoot);
    }

    public int Width { get; }

    public int Height { get; }

    public GL GL { get; }

    public OpenGLRenderer Renderer { get; }

    /// <summary>Draws <paramref name="frame"/> into the framebuffer.</summary>
    public void Render(RenderFrame frame) => Renderer.Render(frame, Width, Height);

    /// <summary>The framebuffer's pixels, as top-down RGBA rows.</summary>
    public byte[] Capture() => Renderer.Capture(Width, Height);

    public void Dispose()
    {
        Renderer.Dispose();
        GL.DeleteRenderbuffer(_color);
        GL.DeleteFramebuffer(_framebuffer);
        wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        wglDeleteContext(_context);
        DestroyWindow(_window);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormat
    {
        public ushort Size, Version;
        public uint Flags;
        public byte PixelType, ColorBits, RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift, AlphaBits, AlphaShift;
        public byte AccumBits, AccumRedBits, AccumGreenBits, AccumBlueBits, AccumAlphaBits, DepthBits, StencilBits, AuxBuffers, LayerType, Reserved;
        public uint LayerMask, VisibleMask, DamageMask;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("gdi32.dll")]
    private static extern int ChoosePixelFormat(IntPtr dc, ref PixelFormat format);

    [DllImport("gdi32.dll")]
    private static extern bool SetPixelFormat(IntPtr dc, int format, ref PixelFormat descriptor);

    [DllImport("opengl32.dll")]
    private static extern IntPtr wglCreateContext(IntPtr dc);

    [DllImport("opengl32.dll")]
    private static extern bool wglMakeCurrent(IntPtr dc, IntPtr context);

    [DllImport("opengl32.dll")]
    private static extern bool wglDeleteContext(IntPtr context);

    [DllImport("opengl32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern IntPtr wglGetProcAddress(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
