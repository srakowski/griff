#if DEBUG
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// Keeps the demo's real input in the demo's own editor. Before each key press, typed character, click, scroll or mouse
/// nudge it checks that the window in front belongs to the editor (or to a program the demo started, such as the
/// tour's exported game); if not, it tries once to bring the editor to the front, and if that fails it stops the demo
/// there, sending nothing, so keys meant for the editor never land in someone else's window. Once it has stopped a demo
/// it refuses every input after, so nothing that catches the failure can carry on typing.
/// </summary>
internal static class DemoFocus
{
    private static readonly HashSet<int> Ours = [Environment.ProcessId];
    private static string? _refusal;

    /// <summary>Why input was refused (the demo then stops); null while input may go.</summary>
    public static string? Refusal => _refusal;

    /// <summary>Lets input go to a program the demo started itself (the tour's exported game).</summary>
    public static void Allow(int processId)
    {
        lock (Ours)
            Ours.Add(processId);
    }

    /// <summary>
    /// Throws (and keeps refusing) unless the window in front is the demo's own, after one try to bring it there. For a
    /// click (<paramref name="click"/>), the window under the cursor has to be the demo's too: another program's window
    /// on top of the editor would get it.
    /// </summary>
    public static void EnsureInFront(bool click = false)
    {
        if (_refusal is { } refused)
            throw new DemoFocusException(refused);
        if (Decide(click ? WouldBeClicked : Foreground, BringEditorToFront, IsOurs) is { } refusal)
        {
            _refusal = refusal;
            throw new DemoFocusException(refusal);
        }
    }

    /// <summary>
    /// The guard's decision: null if the window in front (<paramref name="foreground"/>: its process and title) is
    /// <paramref name="ours"/>; otherwise it calls <paramref name="bringToFront"/> once and looks again, and says why not
    /// if it still isn't.
    /// </summary>
    internal static string? Decide(Func<(int Process, string Title)> foreground, Func<bool> bringToFront, Func<int, bool> ours)
    {
        if (ours(foreground().Process))
            return null;
        bringToFront();
        var front = foreground();
        return ours(front.Process) ? null : $"the editor isn't in front ({front.Title}); no input sent";
    }

    private static bool IsOurs(int processId)
    {
        lock (Ours)
            return processId != 0 && Ours.Contains(processId);
    }

    // The window in front: its process and its title (or, with none, its program's name).
    private static (int Process, string Title) Foreground() => Describe(GetForegroundWindow());

    private static (int Process, string Title) Describe(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return (0, "no window");
        if (GetWindowThreadProcessId(window, out var process) == 0)
            return (0, "a window that has closed");
        var title = new char[256];
        if (GetWindowText(window, title, title.Length) is var length and > 0)
            return ((int)process, new string(title, 0, length));
        try
        {
            return ((int)process, Process.GetProcessById((int)process).ProcessName);
        }
        catch (ArgumentException)
        {
            return ((int)process, "a window with no title");
        }
    }

    // What a click now goes to: the window in front if it isn't the demo's, else the window under the cursor.
    private static (int Process, string Title) WouldBeClicked()
    {
        var front = Foreground();
        if (!IsOurs(front.Process) || !GetCursorPos(out var at))
            return front;
        return Describe(WindowFromPoint(at));
    }

    // Brings the editor's main window to the front without sending any input: Windows lets a program take the front
    // while it shares the input of the one that has it.
    private static bool BringEditorToFront()
    {
        if (Application.Current?.MainWindow is not { } main || new WindowInteropHelper(main).Handle is var editor && editor == IntPtr.Zero)
            return false;
        var front = GetForegroundWindow();
        var frontThread = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, out _);
        var ourThread = GetCurrentThreadId();
        var attached = frontThread != 0 && frontThread != ourThread && AttachThreadInput(ourThread, frontThread, true);
        try
        {
            BringWindowToTop(editor);
            return SetForegroundWindow(editor);
        }
        finally
        {
            if (attached)
                AttachThreadInput(ourThread, frontThread, false);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, [Out] char[] text, int count);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint to, bool join);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

/// <summary>The demo stopped because its editor wasn't in front, before sending input anywhere else.</summary>
internal sealed class DemoFocusException(string message) : Exception(message);
#endif
