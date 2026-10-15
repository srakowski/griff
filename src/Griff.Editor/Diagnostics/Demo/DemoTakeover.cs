#if DEBUG
using System.Runtime.InteropServices;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// Notices the viewer reaching for the mouse: any real (not injected by the demo) movement or click pauses the demo,
/// so the cursor is theirs, say to use the control bar. A low-level mouse hook tells the two apart by the injected flag.
/// </summary>
internal sealed class DemoTakeover : IDisposable
{
    private const int MouseHook = 14, MouseMove = 0x0200, LeftDown = 0x0201, RightDown = 0x0204, Injected = 0x01;

    private readonly DemoControl _control;
    private readonly HookProc _proc; // kept alive for as long as the hook is set
    private readonly IntPtr _hook;
    private (int X, int Y)? _anchor;
    private DateTime _ignoreUntil;

    public DemoTakeover(DemoControl control, bool enabled = true)
    {
        _control = control;
        _proc = OnMouse;
        _hook = enabled ? SetWindowsHookEx(MouseHook, _proc, GetModuleHandle(null), 0) : IntPtr.Zero;
        // A resume (from the bar or a hotkey) gives the hand a moment to let go before movement counts again.
        control.PauseChanged += (_, paused) =>
        {
            if (!paused)
            {
                _ignoreUntil = DateTime.Now.AddSeconds(1.2);
                _anchor = null;
            }
        };
    }

    /// <summary>Raised when the viewer takes the mouse and the demo pauses for them.</summary>
    public event EventHandler? TookOver;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !_control.Paused && DateTime.Now >= _ignoreUntil)
        {
            var info = Marshal.PtrToStructure<MouseInfo>(data);
            if ((info.Flags & Injected) == 0)
            {
                var msg = message.ToInt32();
                // A nudge of the desk isn't taking over: it takes a deliberate move or a click.
                _anchor ??= (info.X, info.Y);
                var moved = Math.Abs(info.X - _anchor.Value.X) + Math.Abs(info.Y - _anchor.Value.Y) > 40;
                if (msg is LeftDown or RightDown || (msg == MouseMove && moved))
                {
                    _control.Paused = true;
                    TookOver?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInfo
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hook, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
#endif
