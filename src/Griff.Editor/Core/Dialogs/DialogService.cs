using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Griff.Editor.Core.Dialogs;

public sealed class DialogService(EditorWindow editor) : IDialogService, IAnswerableDialogs
{
    private const string AppName = "Griff";

    // The active window, so dialogs opened from another dialog stack on top of it.
    private Window? Owner =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? editor.Main;

    public SaveChangesChoice AskSaveChanges(string documentName)
    {
        var result = Ask($"Save changes to {documentName}?", AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => SaveChangesChoice.Save,
            MessageBoxResult.No => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };
    }

    public string? ShowOpenFileDialog(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? ShowSaveFileDialog(string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = defaultFileName };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? ShowFolderDialog(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog();
        if (Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public string? Prompt(string title, string label, string initial, Func<string, string?> validate)
    {
        var window = new PromptWindow(title, label, initial, validate) { Owner = Owner };
        return window.ShowDialog() == true ? window.Value : null;
    }

    public bool Confirm(string title, string message) =>
        Ask(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string title, string message)
    {
        if (Owner is { } owner)
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // The questions showing (the newest last), each a message box with its caption and the window it's over.
    private readonly List<(string Caption, IntPtr Owner)> _asking = [];

    private MessageBoxResult Ask(string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = Owner;
        _asking.Add((caption, owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle));
        try
        {
            return owner is not null
                ? MessageBox.Show(owner, message, caption, buttons, image)
                : MessageBox.Show(message, caption, buttons, image);
        }
        finally
        {
            _asking.RemoveAt(_asking.Count - 1);
        }
    }

    public bool AnswerShowing(MessageBoxResult answer)
    {
        if (_asking.Count == 0)
            return false;

        // The message box is a window of this thread's (the UI thread's, where agents' calls run too): the newest
        // question's, over the window it was asked over, with its caption.
        var (caption, owner) = _asking[^1];
        var found = IntPtr.Zero;
        var name = new char[64];
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (!IsWindowVisible(window) || (owner != IntPtr.Zero && GetWindow(window, GwOwner) != owner))
                return true;
            var length = GetClassName(window, name, name.Length);
            if (new string(name, 0, Math.Max(0, length)) != DialogClass)
                return true;
            length = GetWindowText(window, name, name.Length);
            if (!caption.StartsWith(new string(name, 0, Math.Max(0, length)), StringComparison.Ordinal))
                return true;
            found = window;
            return false;
        }, IntPtr.Zero);

        // As if its button were pressed (the button's id is the answer's number).
        return found != IntPtr.Zero && PostMessage(found, WmCommand, (IntPtr)(int)answer, IntPtr.Zero);
    }

    private const string DialogClass = "#32770";
    private const uint WmCommand = 0x0111;
    private const uint GwOwner = 4;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint relation);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, [Out] char[] name, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, [Out] char[] text, int max);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
