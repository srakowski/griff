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
        // opengl32.dll is loaded before the pixel format is chosen: GDI may otherwise choose before the driver is loaded, and a
        // driver installed as the system's software OpenGL (Mesa on a machine with no GPU) then can't make a context the first
        // time in a process. A driver still starting up can refuse one too, so it gets a few tries, each on a new window (a
        // window's pixel format is set once).
        _opengl32 = LoadLibrary("opengl32.dll");
        var problem = "";
        for (var attempt = 1; ; attempt++)
        {
            _window = CreateWindowEx(0, "static", "Griff", 0x80000000, 0, 0, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _dc = GetDC(_window);
            var format = new PixelFormat { Size = (ushort)Marshal.SizeOf<PixelFormat>(), Version = 1, Flags = 0x4 | 0x20 | 0x1, ColorBits = 32, AlphaBits = 8, DepthBits = 24, StencilBits = 8 };
            var chosen = ChoosePixelFormat(_dc, ref format);
            if (chosen == 0 || !SetPixelFormat(_dc, chosen, ref format))
                problem = $"no pixel format (chose {chosen}, error {Marshal.GetLastPInvokeError()})";
            else if ((_context = wglCreateContext(_dc)) == IntPtr.Zero)
                problem = $"no context ({Describe(_dc, chosen)})";
            else if (!wglMakeCurrent(_dc, _context))
                problem = $"the context couldn't be made current ({Describe(_dc, chosen)})";
            else
                break;

            if (_context != IntPtr.Zero)
                wglDeleteContext(_context);
            _context = IntPtr.Zero;
            _ = ReleaseDC(_window, _dc);
            DestroyWindow(_window);
            if (attempt == 3)
                throw new InvalidOperationException($"No OpenGL context could be made: {problem}.");
            Thread.Sleep(100);
        }

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

    // What a pixel format is, for an error message: its bits and whether it's GDI's generic (software 1.1) one.
    private static string Describe(IntPtr dc, int format)
    {
        var error = Marshal.GetLastPInvokeError();
        var described = new PixelFormat();
        if (DescribePixelFormat(dc, format, (uint)Marshal.SizeOf<PixelFormat>(), ref described) == 0)
            return $"pixel format {format}, error {error}";
        const uint GenericFormat = 0x40, GenericAccelerated = 0x1000;
        var kind = (described.Flags & GenericFormat) == 0 ? "a driver's" : (described.Flags & GenericAccelerated) != 0 ? "generic, accelerated" : "GDI's generic software";
        return $"pixel format {format}: {kind}, {described.ColorBits}-bit color, {described.DepthBits}-bit depth, flags 0x{described.Flags:x}, error {error}";
    }

    public void Dispose()
    {
        Renderer.Dispose();
        GL.DeleteRenderbuffer(_color);
        GL.DeleteFramebuffer(_framebuffer);
        wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        wglDeleteContext(_context);
        _ = ReleaseDC(_window, _dc);
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

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern int DescribePixelFormat(IntPtr dc, int format, uint size, ref PixelFormat descriptor);

    [DllImport("gdi32.dll")]
    private static extern int ChoosePixelFormat(IntPtr dc, ref PixelFormat format);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetPixelFormat(IntPtr dc, int format, ref PixelFormat descriptor);

    [DllImport("opengl32.dll", SetLastError = true)]
    private static extern IntPtr wglCreateContext(IntPtr dc);

    [DllImport("opengl32.dll", SetLastError = true)]
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
