#if DEBUG
using System.Runtime.InteropServices;
using System.Text;
using Point = System.Windows.Point;

namespace Griff.Editor.Diagnostics.Demo;

// Windows message boxes (the dialog service's Confirm and errors): answered by clicking their buttons with the real mouse.
internal sealed partial class DemoUi
{
    /// <summary>Waits for a message box from the editor and clicks its button labelled <paramref name="button"/> ("Yes").</summary>
    public async Task Answer(string button)
    {
        var box = await WaitFor(() => MessageBoxWindow() is { } found ? (object)found : null, "a message box", 5000);
        var handle = (IntPtr)box;
        var target = await WaitFor(() => ButtonIn(handle, button) is { } found ? (object)found : null, $"the {button} button", 2000);
        GetWindowRect((IntPtr)target, out var rect);
        await input.Click(new Point((rect.Left + rect.Right) / 2.0, (rect.Top + rect.Bottom) / 2.0));
        await WaitFor(() => MessageBoxWindow() is null ? new object() : null, "the message box to close", 3000);
        await Settle(200);
    }

    // A visible dialog-class window (#32770, which MessageBox.Show makes) belonging to this process.
    private static IntPtr? MessageBoxWindow()
    {
        IntPtr? found = null;
        var process = (uint)Environment.ProcessId;
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window) && GetWindowThreadProcessId(window, out var owner) != 0 && owner == process && ClassOf(window) == "#32770")
            {
                found = window;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static IntPtr? ButtonIn(IntPtr box, string label)
    {
        IntPtr? found = null;
        EnumChildWindows(box, (child, _) =>
        {
            if (ClassOf(child) == "Button" && TextOf(child).Replace("&", "").Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                found = child;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string ClassOf(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetClassName(window, text, text.Capacity);
        return text.ToString();
    }

    private static string TextOf(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);
}
#endif
