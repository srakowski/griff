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
public sealed class GameSurface : HwndHost, Griff.Hosting.IGameView
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;

    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmRButtonDblClk = 0x0206;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMButtonDblClk = 0x0209;
    private const int WmMouseLeave = 0x02A3;
    private const int WmMouseWheel = 0x020A;
    private const int WmNcHitTest = 0x0084;
    private const int WmSetCursor = 0x0020;
    private const int WmPointerUpdate = 0x0245;
    private const int WmPointerDown = 0x0246;
    private const int WmPointerUp = 0x0247;
    private const int WmPointerCaptureChanged = 0x024C;
    private const int PtTouch = 2;
    private const int HtClient = 1;
    private const uint TmeLeave = 0x00000002;

    private IntPtr _hwnd;
    private WglContext? _context;
    private GL? _gl;
    private OpenGLRenderer? _renderer;
    private Griff.Projects.IAssetSource? _assets;
    private bool _trackingLeave;

    /// <summary>Raised with the cursor position in pixels from the top-left of the surface.</summary>
    public event Action<Point>? MouseMoved;

    /// <summary>Raised when the cursor leaves the surface.</summary>
    public event Action? MouseLeft;

    /// <summary>Raised when a mouse button goes down (true) or up (false) over the surface.</summary>
    public event Action<Input.MouseButton, bool>? MouseButtonChanged;

    /// <summary>Raised when the mouse wheel turns over the surface, in notches (positive is away from the user).</summary>
    public event Action<float>? MouseWheelTurned;

    /// <summary>
    /// Raised as a finger on a touch screen goes down, moves (true) or is lifted (false), with which finger it is and where,
    /// in pixels from the top-left of the surface. The first finger also works the mouse, as Windows makes it.
    /// </summary>
    public event Action<int, Point, bool>? TouchChanged;

    /// <summary>The pointer shown over the surface (see <c>IInput.Cursor</c>): the arrow unless the game asks for another.</summary>
    public Griff.Input.MouseCursor Pointer
    {
        get => _pointer;
        set
        {
            if (_pointer == value)
                return;
            _pointer = value;
            // Shown straight away if the mouse is over the surface, rather than when it next moves.
            if (_hwnd != IntPtr.Zero && GetCursorPos(out var at) && WindowFromPoint(at) == _hwnd)
                SetCursor(PointerHandle(value));
        }
    }

    private Griff.Input.MouseCursor _pointer;

    /// <summary>
    /// The game's files its images and fonts are drawn from: the project's Assets, watched in the editor, fixed in an
    /// exported game. Nothing is drawn until it's set.
    /// </summary>
    public Griff.Projects.IAssetSource? Assets
    {
        get => _assets;
        set
        {
            if (_assets == value)
                return;
            _assets = value;
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
        if (_context is null || _gl is null || Assets is null)
            return;

        var (width, height) = PixelSize;
        if (width == 0 || height == 0)
            return;

        _context.MakeCurrent();
        _renderer ??= new OpenGLRenderer(_gl, Assets);
        _renderer.Render(frame, width, height);
        _context.SwapBuffers();
    }

    /// <summary>Renders <paramref name="frame"/> and returns the pixels (top-down RGBA) without presenting.</summary>
    public (byte[] Pixels, int Width, int Height)? Capture(RenderFrame frame)
    {
        if (_context is null || _gl is null || Assets is null)
            return null;

        var (width, height) = PixelSize;
        if (width == 0 || height == 0)
            return null;

        _context.MakeCurrent();
        _renderer ??= new OpenGLRenderer(_gl, Assets);
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

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(0, "static", "", WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

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
            case WmNcHitTest:
                // A "static" window lets the mouse fall through to the window behind it; the game wants it.
                handled = true;
                return new IntPtr(HtClient);
            case WmSetCursor when (lParam.ToInt64() & 0xFFFF) == HtClient:
                SetCursor(PointerHandle(_pointer));
                handled = true;
                return new IntPtr(1);
            case WmMouseMove:
                // Ask Windows to tell us when the cursor leaves, once per visit.
                if (!_trackingLeave)
                {
                    var track = new TrackMouseEventInfo { Size = (uint)Marshal.SizeOf<TrackMouseEventInfo>(), Flags = TmeLeave, Window = hwnd };
                    _trackingLeave = TrackMouseEvent(ref track);
                }

                MouseMoved?.Invoke(PointFrom(lParam));
                break;
            case WmMouseLeave:
                _trackingLeave = false;
                MouseLeft?.Invoke();
                break;
            // The "static" class asks for double clicks, so a quick second click comes as one: it's a press all the same.
            case WmLButtonDown or WmRButtonDown or WmMButtonDown or WmLButtonDblClk or WmRButtonDblClk or WmMButtonDblClk:
                // Keep keyboard focus in WPF so key events keep flowing to the game.
                Window.GetWindow(this)?.Activate();
                MouseButtonChanged?.Invoke(ButtonFor(msg), true);
                break;
            case WmLButtonUp or WmRButtonUp or WmMButtonUp:
                MouseButtonChanged?.Invoke(ButtonFor(msg), false);
                break;
            // Fingers on a touch screen: each one, so several can be down at once. Left to Windows too (not handled), so the
            // first one still works the mouse.
            case WmPointerDown or WmPointerUpdate or WmPointerUp or WmPointerCaptureChanged:
                var id = (int)(wParam.ToInt64() & 0xFFFF);
                if (GetPointerType((uint)id, out var type) && type == PtTouch)
                {
                    var screen = PointFrom(lParam);
                    var client = new NativePoint { X = (int)screen.X, Y = (int)screen.Y };
                    ScreenToClient(hwnd, ref client);
                    TouchChanged?.Invoke(id, new Point(client.X, client.Y), msg is WmPointerDown or WmPointerUpdate);
                }

                break;
            case WmMouseWheel:
                MouseWheelTurned?.Invoke((short)((wParam.ToInt64() >> 16) & 0xFFFF) / 120f);
                // Handled here, so it isn't passed up to the window and counted twice.
                handled = true;
                return IntPtr.Zero;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private static Input.MouseButton ButtonFor(int msg) => msg switch
    {
        WmRButtonDown or WmRButtonUp or WmRButtonDblClk => Input.MouseButton.Right,
        WmMButtonDown or WmMButtonUp or WmMButtonDblClk => Input.MouseButton.Middle,
        _ => Input.MouseButton.Left,
    };

    // The system's pointer for each kind (none for Hidden), loaded once.
    private static IntPtr PointerHandle(Griff.Input.MouseCursor pointer) => pointer switch
    {
        Griff.Input.MouseCursor.Hidden => IntPtr.Zero,
        Griff.Input.MouseCursor.Hand => LoadCursor(IntPtr.Zero, new IntPtr(32649)),
        Griff.Input.MouseCursor.Cross => LoadCursor(IntPtr.Zero, new IntPtr(32515)),
        Griff.Input.MouseCursor.Look => LoadCursor(IntPtr.Zero, new IntPtr(32651)),
        _ => LoadCursor(IntPtr.Zero, new IntPtr(32512)),
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

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr Window;
        public uint HoverTime;
    }

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TrackMouseEventInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetPointerType(uint pointerId, out int pointerType);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);
}
