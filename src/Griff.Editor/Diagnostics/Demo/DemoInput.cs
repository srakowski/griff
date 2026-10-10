#if DEBUG
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// Real mouse and keyboard input for the guided demo: the cursor glides to where it's going and clicks, keys are
/// pressed and typed through SendInput, so the editor reacts exactly as it would to a person.
/// </summary>
internal sealed class DemoInput(DemoOverlay overlay, double speed)
{
    private const int InputMouse = 0, InputKeyboard = 1;
    private const uint Move = 0x0001, LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010, Wheel = 0x0800;
    private const uint KeyRelease = 0x0002, Unicode = 0x0004, Extended = 0x0001;

    /// <summary>Extra speed for sped-up stretches of the script.</summary>
    public double Boost { get; set; } = 1;

    /// <summary>Above 1, everything happens faster: test runs, sped-up stretches, and the viewer's chosen speed.</summary>
    public double Speed => speed * Boost * (DemoControl.Active?.Factor ?? 1);

    private static Task Gate() => DemoControl.Active?.Gate() ?? Task.CompletedTask;

    /// <summary>Lets go of every key and button the demo might be holding (after a jump or exit mid-action).</summary>
    public void ReleaseAll()
    {
        foreach (var key in new[] { Key.LeftCtrl, Key.LeftAlt, Key.LeftShift, Key.Left, Key.Right, Key.Up, Key.Down, Key.Space })
            Send(VirtualKey(key, down: false));
        Mouse(LeftUp);
        Mouse(RightUp);
    }

    public Point Cursor => GetCursorPos(out var p) ? new Point(p.X, p.Y) : default;

    public async Task Pause(double ms)
    {
        await Gate();
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, ms / Speed)));
    }

    /// <summary>Glides the cursor to <paramref name="target"/> (screen pixels) on an eased curve.</summary>
    public async Task MoveTo(Point target, double ms = 650)
    {
        var start = Cursor;
        var distance = (target - start).Length;
        if (distance < 1)
            return;

        // Longer trips take a little longer, the way a hand moves.
        var duration = Math.Clamp(ms * (0.55 + distance / 2200), 120, 1400) / Speed;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // A slight arc looks less robotic than a ruler-straight line.
        var normal = new Vector(-(target.Y - start.Y), target.X - start.X);
        if (normal.Length > 0)
            normal.Normalize();
        var bow = Math.Min(60, distance * 0.08);

        while (clock.Elapsed.TotalMilliseconds < duration)
        {
            await Gate();
            var t = clock.Elapsed.TotalMilliseconds / duration;
            var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            var along = start + (target - start) * eased + normal * (Math.Sin(eased * Math.PI) * bow);
            SetCursorPos((int)Math.Round(along.X), (int)Math.Round(along.Y));
            await Task.Delay(8);
        }

        SetCursorPos((int)Math.Round(target.X), (int)Math.Round(target.Y));
        await Task.Delay(15);
    }

    public async Task Click(Point at, bool right = false, int clicks = 1, bool glide = true)
    {
        if (glide)
            await MoveTo(at);
        else
            SetCursorPos((int)Math.Round(at.X), (int)Math.Round(at.Y));
        await Pause(90);
        await Gate();
        for (var i = 0; i < clicks; i++)
        {
            overlay.Ripple(at);
            Mouse(right ? RightDown : LeftDown);
            await Task.Delay(45);
            Mouse(right ? RightUp : LeftUp);
            await Task.Delay(clicks > 1 ? 60 : 30);
        }

        await Pause(160);
    }

    /// <summary>Presses at <paramref name="from"/> and drags through <paramref name="path"/>, releasing at the end.</summary>
    public async Task Drag(Point from, IReadOnlyList<Point> path, double msPerSegment = 350)
    {
        await MoveTo(from);
        await Pause(80);
        overlay.Ripple(from);
        Mouse(LeftDown);
        await Task.Delay(60);
        foreach (var point in path)
            await MoveTo(point, msPerSegment);
        await Task.Delay(60);
        Mouse(LeftUp);
        await Pause(150);
    }

    /// <summary>
    /// Moves the mouse <paramref name="dx"/>, <paramref name="dy"/> pixels (screen pixels, down is positive) over
    /// <paramref name="ms"/>, as relative movement the way a hand moves it: for turning a first-person game that holds the mouse.
    /// </summary>
    public async Task Nudge(double dx, double dy, double ms)
    {
        var duration = Math.Max(1, ms / Speed);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (sentX, sentY) = (0, 0);
        while (true)
        {
            await Gate();
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration);
            var eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            var (x, y) = ((int)Math.Round(dx * eased), (int)Math.Round(dy * eased));
            if (x != sentX || y != sentY)
            {
                Send(new INPUT { type = InputMouse, u = new InputUnion { mi = new MOUSEINPUT { dx = x - sentX, dy = y - sentY, dwFlags = Move } } });
                (sentX, sentY) = (x, y);
            }

            if (t >= 1)
                break;
            await Task.Delay(8);
        }
    }

    public async Task Scroll(int notches)
    {
        for (var i = 0; i < Math.Abs(notches); i++)
        {
            Send(new INPUT { type = InputMouse, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = Wheel, mouseData = (uint)(Math.Sign(notches) * 120) } } });
            await Pause(70);
        }
    }

    /// <summary>Types <paramref name="text"/> one character at a time.</summary>
    public async Task Type(string text, double msPerChar = 45)
    {
        foreach (var c in text)
        {
            if (c == '\n')
            {
                await Press(Key.Enter, showBadge: false);
                continue;
            }

            await Gate();
            Send(Char(c, down: true), Char(c, down: false));
            // A little unevenness reads as typing rather than pasting.
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, msPerChar * (0.6 + Random.Shared.NextDouble() * 0.8) / Speed)));
        }
    }

    /// <summary>Presses a key, with modifiers held, and shows it on screen like a demo recording would.</summary>
    public async Task Press(Key key, ModifierKeys modifiers = ModifierKeys.None, bool showBadge = true)
    {
        await Gate();
        if (showBadge)
            overlay.ShowKeys(Describe(key, modifiers));

        var held = Modifiers(modifiers).ToList();
        foreach (var m in held)
            Send(VirtualKey(m, down: true));
        await Task.Delay(30);
        Send(VirtualKey(key, down: true));
        await Task.Delay(50);
        Send(VirtualKey(key, down: false));
        await Task.Delay(30);
        foreach (var m in Enumerable.Reverse(held))
            Send(VirtualKey(m, down: false));
        await Pause(120);
    }

    public void KeyDown(Key key) => Send(VirtualKey(key, down: true));

    public void KeyUp(Key key) => Send(VirtualKey(key, down: false));

    public static string Describe(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key switch
        {
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            _ => key.ToString(),
        });
        return string.Join(" + ", parts);
    }

    private static IEnumerable<Key> Modifiers(ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Control)) yield return Key.LeftCtrl;
        if (modifiers.HasFlag(ModifierKeys.Alt)) yield return Key.LeftAlt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) yield return Key.LeftShift;
    }

    private static void Mouse(uint flags) =>
        Send(new INPUT { type = InputMouse, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } } });

    private static INPUT Char(char c, bool down) => new()
    {
        type = InputKeyboard,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = Unicode | (down ? 0 : KeyRelease) } },
    };

    private static INPUT VirtualKey(Key key, bool down)
    {
        var vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
        var extended = key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Delete or Key.Home or Key.End
            or Key.PageUp or Key.PageDown or Key.Insert or Key.RightCtrl or Key.RightAlt;
        return new INPUT
        {
            type = InputKeyboard,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKey(vk, 0),
                    dwFlags = (down ? 0 : KeyRelease) | (extended ? Extended : 0),
                },
            },
        };
    }

    private static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
#endif
