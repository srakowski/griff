using System.Runtime.InteropServices;

namespace Griff.Platform.Windows;

/// <summary>An OpenGL context on a Win32 window, created through WGL.</summary>
public sealed class WglContext : IDisposable
{
    private const uint PfdDrawToWindow = 0x4;
    private const uint PfdSupportOpenGL = 0x20;
    private const uint PfdDoubleBuffer = 0x1;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _hdc;
    private readonly IntPtr _context;
    private readonly IntPtr _opengl32;
    private bool _disposed;

    public WglContext(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _hdc = GetDC(hwnd);

        var descriptor = new PixelFormatDescriptor
        {
            Size = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(),
            Version = 1,
            Flags = PfdDrawToWindow | PfdSupportOpenGL | PfdDoubleBuffer,
            PixelType = 0, // RGBA
            ColorBits = 32,
            AlphaBits = 8,
            DepthBits = 24,
            StencilBits = 8,
        };

        var format = ChoosePixelFormat(_hdc, ref descriptor);
        if (format == 0 || !SetPixelFormat(_hdc, format, ref descriptor))
            throw new InvalidOperationException("No suitable OpenGL pixel format.");

        _context = wglCreateContext(_hdc);
        if (_context == IntPtr.Zero)
            throw new InvalidOperationException("Couldn't create an OpenGL context. Is a graphics driver installed?");

        _opengl32 = LoadLibrary("opengl32.dll");
        MakeCurrent();

        // The UI thread paces frames; don't block it waiting for vsync.
        var swapInterval = GetProcAddress("wglSwapIntervalEXT");
        if (swapInterval != IntPtr.Zero)
            Marshal.GetDelegateForFunctionPointer<SwapIntervalDelegate>(swapInterval)(0);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SwapIntervalDelegate(int interval);

    public void MakeCurrent() => wglMakeCurrent(_hdc, _context);

    public void SwapBuffers() => SwapBuffersNative(_hdc);

    /// <summary>Resolves a GL function. Core 1.1 functions come from opengl32.dll, everything else from the driver.</summary>
    public IntPtr GetProcAddress(string name)
    {
        var address = wglGetProcAddress(name);
        if (address is 0 or 1 or 2 or 3 or -1)
            address = GetProcAddressNative(_opengl32, name);
        return address;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        wglDeleteContext(_context);
        ReleaseDC(_hwnd, _hdc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort Size;
        public ushort Version;
        public uint Flags;
        public byte PixelType;
        public byte ColorBits;
        public byte RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift, AlphaBits, AlphaShift;
        public byte AccumBits, AccumRedBits, AccumGreenBits, AccumBlueBits, AccumAlphaBits;
        public byte DepthBits;
        public byte StencilBits;
        public byte AuxBuffers;
        public byte LayerType;
        public byte Reserved;
        public uint LayerMask, VisibleMask, DamageMask;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int ChoosePixelFormat(IntPtr hdc, ref PixelFormatDescriptor descriptor);

    [DllImport("gdi32.dll")]
    private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PixelFormatDescriptor descriptor);

    [DllImport("gdi32.dll", EntryPoint = "SwapBuffers")]
    private static extern bool SwapBuffersNative(IntPtr hdc);

    [DllImport("opengl32.dll")]
    private static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll")]
    private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr context);

    [DllImport("opengl32.dll")]
    private static extern bool wglDeleteContext(IntPtr context);

    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr wglGetProcAddress(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddressNative(IntPtr module, string name);
}
