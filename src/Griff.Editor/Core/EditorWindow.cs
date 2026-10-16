using System.Windows;
using System.Windows.Controls;

namespace Griff.Editor.Core;

/// <summary>
/// The editor's main window, for what needs it: the owner of the windows and dialogs it opens, the document tabs agents
/// take pictures of, closing the editor once the questions are answered. The window registers itself as it's made
/// (<see cref="Attach"/>), so services take this rather than asking the application for its MainWindow and casting it.
/// </summary>
public sealed class EditorWindow
{
    private Window? _main;
    private Action? _closeConfirmed;

    /// <summary>The main window, or null before it's made (the start of the editor, a test with no window).</summary>
    public Window? Main => _main;

    /// <summary>The tabs the Scene, the Game and the scripts are in, or null before the window is made.</summary>
    public TabControl? DocumentTabs { get; private set; }

    /// <summary>The Scene view's tab.</summary>
    public TabItem? SceneTab { get; private set; }

    /// <summary>The Game view's tab.</summary>
    public TabItem? GameTab { get; private set; }

    /// <summary>The windows open besides the main one: the editors, New Project, the Controls.</summary>
    public IReadOnlyList<Window> OtherWindows =>
        Application.Current is { } app ? [.. app.Windows.OfType<Window>().Where(w => w.IsVisible && w != _main)] : [];

    /// <summary>Called by the main window as it's made. <paramref name="closeConfirmed"/> closes it without asking again.</summary>
    public void Attach(Window main, TabControl documentTabs, TabItem sceneTab, TabItem gameTab, Action closeConfirmed)
    {
        (_main, DocumentTabs, SceneTab, GameTab, _closeConfirmed) = (main, documentTabs, sceneTab, gameTab, closeConfirmed);
    }

    /// <summary>Closes the editor once its questions are answered (unsaved changes were dealt with): the window closes without asking again.</summary>
    public void CloseConfirmed()
    {
        if (_closeConfirmed is { } close)
            close();
        else
            _main?.Close();
    }

    /// <summary>Whether <paramref name="window"/> is the main window.</summary>
    public bool IsMain(Window window) => window == _main;
}
