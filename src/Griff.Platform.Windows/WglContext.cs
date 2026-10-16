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
        // opengl32.dll is loaded before the pixel format is chosen: GDI may otherwise choose before the driver is loaded, and a
        // driver installed as the system's software OpenGL (Mesa on a machine with no GPU) then can't make a context on the
        // window the first time in a process.
        _opengl32 = LoadLibrary("opengl32.dll");
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
            throw new InvalidOperationException($"No suitable OpenGL pixel format (chose {format}, error {Marshal.GetLastPInvokeError()}).");

        // A driver still starting up can refuse the first context; it gets a few tries.
        for (var attempt = 1; (_context = wglCreateContext(_hdc)) == IntPtr.Zero && attempt < 3; attempt++)
            Thread.Sleep(100);
        if (_context == IntPtr.Zero)
            throw new InvalidOperationException($"Couldn't create an OpenGL context. Is a graphics driver installed? ({Describe(_hdc, format)})");

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

    /// <summary>What a pixel format is, for an error message: its bits and whether it's GDI's generic (software 1.1) one.</summary>
    private static string Describe(IntPtr hdc, int format)
    {
        var error = Marshal.GetLastPInvokeError();
        var described = new PixelFormatDescriptor();
        if (DescribePixelFormat(hdc, format, (uint)Marshal.SizeOf<PixelFormatDescriptor>(), ref described) == 0)
            return $"pixel format {format}, error {error}";
        const uint GenericFormat = 0x40, GenericAccelerated = 0x1000;
        var kind = (described.Flags & GenericFormat) == 0 ? "a driver's" : (described.Flags & GenericAccelerated) != 0 ? "generic, accelerated" : "GDI's generic software";
        return $"pixel format {format}: {kind}, {described.ColorBits}-bit color, {described.DepthBits}-bit depth, flags 0x{described.Flags:x}, error {error}";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        wglDeleteContext(_context);
        _ = ReleaseDC(_hwnd, _hdc);
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
    private static extern int DescribePixelFormat(IntPtr hdc, int format, uint size, ref PixelFormatDescriptor descriptor);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PixelFormatDescriptor descriptor);

    [DllImport("gdi32.dll", EntryPoint = "SwapBuffers")]
    private static extern bool SwapBuffersNative(IntPtr hdc);

    [DllImport("opengl32.dll", SetLastError = true)]
    private static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll")]
    private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr context);

    [DllImport("opengl32.dll")]
    private static extern bool wglDeleteContext(IntPtr context);

    [DllImport("opengl32.dll", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr wglGetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr GetProcAddressNative(IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string name);
}
