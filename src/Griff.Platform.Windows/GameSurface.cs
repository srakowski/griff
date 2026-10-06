using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Griff.Graphics;
using Griff.Rendering;
using Silk.NET.OpenGL;

namespace Griff.Platform.Windows;

/// <summary>
/// A WPF element that draws <see cref="RenderFrame"/>s with OpenGL into a child window.
/// Call <see cref="Render"/> once per frame. Mouse input over the surface is reported through events;
/// keyboard input stays with the WPF window.
/// </summary>
public sealed class GameSurface : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;

    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;

    private IntPtr _hwnd;
    private WglContext? _context;
    private GL? _gl;
    private SpriteRenderer? _renderer;
    private string? _assetRoot;

    /// <summary>Raised with the cursor position in pixels from the top-left of the surface.</summary>
    public event Action<Point>? MouseMoved;

    /// <summary>Raised when a mouse button goes down (true) or up (false) over the surface.</summary>
    public event Action<Runtime.MouseButton, bool>? MouseButtonChanged;

    /// <summary>Folder that sprite image paths are relative to (the project's Assets folder).</summary>
    public string? AssetRoot
    {
        get => _assetRoot;
        set
        {
            if (_assetRoot == value)
                return;
            _assetRoot = value;
            _renderer?.Dispose();
            _renderer = null;
        }
    }

    /// <summary>Surface size in physical pixels.</summary>
    public (int Width, int Height) PixelSize
    {
        get
        {
            if (_hwnd == IntPtr.Zero || !GetClientRect(_hwnd, out var rect))
                return (0, 0);
            return (rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }

    public float Aspect
    {
        get
        {
            var (width, height) = PixelSize;
            return height > 0 ? (float)width / height : 1f;
        }
    }

    /// <summary>Draws <paramref name="frame"/> and presents it. Does nothing until the surface has a window.</summary>
    public void Render(RenderFrame frame)
    {
        if (_context is null || _gl is null || AssetRoot is null)
            return;

        var (width, height) = PixelSize;
        if (width == 0 || height == 0)
            return;

        _context.MakeCurrent();
        _renderer ??= new SpriteRenderer(_gl, AssetRoot, WpfText.Rasterize);
        _renderer.Render(frame, width, height);
        _context.SwapBuffers();
    }

    /// <summary>Renders <paramref name="frame"/> and returns the pixels (top-down RGBA) without presenting.</summary>
    public (byte[] Pixels, int Width, int Height)? Capture(RenderFrame frame)
    {
        if (_context is null || _gl is null || AssetRoot is null)
            return null;

        var (width, height) = PixelSize;
        if (width == 0 || height == 0)
            return null;

        _context.MakeCurrent();
        _renderer ??= new SpriteRenderer(_gl, AssetRoot, WpfText.Rasterize);
        _renderer.Render(frame, width, height);
        return (_renderer.Capture(width, height), width, height);
    }

    /// <summary>Converts a surface pixel position to normalized device coordinates (-1..1, Y up).</summary>
    public Vector2 ToNdc(Point pixel)
    {
        var (width, height) = PixelSize;
        if (width == 0 || height == 0)
            return Vector2.Zero;
        return new Vector2((float)(pixel.X / width * 2 - 1), (float)(1 - pixel.Y / height * 2));
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        _hwnd = CreateWindowEx(0, "static", "", WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        _context = new WglContext(_hwnd);
        _gl = GL.GetApi(_context.GetProcAddress);
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _context?.MakeCurrent();
        _renderer?.Dispose();
        _renderer = null;
        _gl?.Dispose();
        _gl = null;
        _context?.Dispose();
        _context = null;
        DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmMouseMove:
                MouseMoved?.Invoke(PointFrom(lParam));
                break;
            case WmLButtonDown or WmRButtonDown or WmMButtonDown:
                // Keep keyboard focus in WPF so key events keep flowing to the game.
                Window.GetWindow(this)?.Activate();
                MouseButtonChanged?.Invoke(ButtonFor(msg), true);
                break;
            case WmLButtonUp or WmRButtonUp or WmMButtonUp:
                MouseButtonChanged?.Invoke(ButtonFor(msg), false);
                break;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private static Runtime.MouseButton ButtonFor(int msg) => msg switch
    {
        WmRButtonDown or WmRButtonUp => Runtime.MouseButton.Right,
        WmMButtonDown or WmMButtonUp => Runtime.MouseButton.Middle,
        _ => Runtime.MouseButton.Left,
    };

    private static Point PointFrom(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        return new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
}
