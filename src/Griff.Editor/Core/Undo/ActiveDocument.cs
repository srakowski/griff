using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Core.Undo;

/// <summary>
/// Which document Edit &gt; Undo and Redo (Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z) go to: the <see cref="IDocumentEditor"/> last
/// worked in or changed. Clicking into a panel or window that edits a document (the Tables panel, the Chart panel, the
/// Sprite Painter) makes it the active one, as changing its document does (an agent's ui_set or ui_invoke on it too);
/// anywhere else in the editor (the Scene view, the Hierarchy, the Inspector, the Assets panel), or a change to the scene,
/// makes the open scene or prefab the active one, as it is to start with. A window that edits a document has Undo and
/// Redo's shortcuts of its own, going to its document whatever was active.
/// </summary>
public sealed class ActiveDocument : ObservableObject
{
    // The one the editor's windows on this thread report focus to (tests run editors side by side, each on its own thread).
    [ThreadStatic]
    private static WeakReference<ActiveDocument>? t_current;

    private static int s_hooked;

    private readonly IServiceProvider? _services;
    private readonly HashSet<IDocumentEditor> _watched = [];
    private IDocumentEditor _editor;
    private SceneDocument? _scene;

    public ActiveDocument(IWorkspace workspace, IEnumerable<IDocumentEditor> editors, IServiceProvider? services = null)
    {
        _services = services;
        Scene = new SceneEditor(workspace);
        _editor = Scene;
        foreach (var editor in editors)
            Watch(editor);
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Scene))
            {
                Track(workspace.Scene);
                OnPropertyChanged(string.Empty);
            }
        };
        Track(workspace.Scene);
        t_current = new WeakReference<ActiveDocument>(this);
        Hook();
    }

    /// <summary>The open scene or prefab's editor: the Scene view, the Hierarchy, the Inspector and the Clip Editor edit it.</summary>
    public IDocumentEditor Scene { get; }

    /// <summary>The editor undo and redo go to now: the scene's, unless a panel or window with a document of its own was worked in last.</summary>
    public IDocumentEditor Editor => _editor;

    /// <summary>What the active editor shows, as undoing says it ("the Spells table"), or null.</summary>
    public string? DocumentName => _editor.DocumentName;

    /// <summary>What Undo would take back now ("Set Fireball's Damage"), or null.</summary>
    public string? UndoDescription => HistoryOf(null)?.UndoDescription;

    /// <summary>What Redo would do again now, or null.</summary>
    public string? RedoDescription => HistoryOf(null)?.RedoDescription;

    /// <summary>Makes <paramref name="editor"/> the one undo and redo go to.</summary>
    public void Activate(IDocumentEditor editor)
    {
        Watch(editor);
        if (ReferenceEquals(_editor, editor))
            return;
        _editor = editor;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Whether <paramref name="editor"/>'s document (or the active one's, with none) has a change to undo now.</summary>
    public bool CanUndo(IDocumentEditor? editor = null) => HistoryOf(editor)?.CanUndo == true;

    /// <summary>Whether <paramref name="editor"/>'s document (or the active one's, with none) has a change undone to redo now.</summary>
    public bool CanRedo(IDocumentEditor? editor = null) => HistoryOf(editor)?.CanRedo == true;

    /// <summary>
    /// Undoes the last change to <paramref name="editor"/>'s document (the active one's, with none), which becomes the
    /// active one: "Set Fireball's Damage in the Spells table", or null if there was nothing to undo.
    /// </summary>
    public string? Undo(IDocumentEditor? editor = null) => Step(editor, undo: true);

    /// <summary>Redoes the last change undone to <paramref name="editor"/>'s document (the active one's, with none), as <see cref="Undo"/>.</summary>
    public string? Redo(IDocumentEditor? editor = null) => Step(editor, undo: false);

    private string? Step(IDocumentEditor? editor, bool undo)
    {
        editor ??= _editor;
        if (HistoryOf(editor) is not { } history || !(undo ? history.CanUndo : history.CanRedo))
            return null;
        Activate(editor);
        var what = undo ? history.UndoDescription : history.RedoDescription;
        var where = editor.DocumentName;
        if (undo)
            history.Undo();
        else
            history.Redo();
        OnPropertyChanged(string.Empty);
        return where is null ? what : $"{what} in {where}";
    }

    private EditHistory? HistoryOf(IDocumentEditor? editor) => (editor ?? _editor) is { CanChange: true } active ? active.History : null;

    // An editor's own changes make it the active one.
    private void Watch(IDocumentEditor editor)
    {
        if (_watched.Add(editor))
            editor.Edited += OnEdited;
    }

    private void OnEdited(object? sender, EventArgs e)
    {
        if (sender is IDocumentEditor editor)
            Activate(editor);
    }

    /// <summary>Lets go of an editor whose window has closed: the scene is active again if it was.</summary>
    public void Forget(IDocumentEditor editor)
    {
        if (_watched.Remove(editor))
            editor.Edited -= OnEdited;
        if (ReferenceEquals(_editor, editor))
            Activate(Scene);
    }

    // A change to the scene (from anywhere: the Scene view, the Inspector, an agent's entity tools) makes it the active one.
    private void Track(SceneDocument? document)
    {
        if (_scene is not null)
            _scene.Changed -= OnSceneChanged;
        _scene = document;
        if (_scene is not null)
            _scene.Changed += OnSceneChanged;
    }

    private void OnSceneChanged(object? sender, EditEventArgs e)
    {
        if (e.Action == EditAction.Execute)
            Activate(Scene);
        else
            OnPropertyChanged(string.Empty);
    }

    // ----- Where the keyboard is

    private static void Hook()
    {
        if (Interlocked.Exchange(ref s_hooked, 1) == 1)
            return;
        EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnFocus), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
    }

    private static ActiveDocument? Current => t_current is { } weak && weak.TryGetTarget(out var current) ? current : null;

    // The event goes up through each element; act on it once, where it starts.
    private static void OnFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(sender, e.NewFocus) && Current is { } documents)
            documents.Focused(e.NewFocus as DependencyObject);
    }

    /// <summary>
    /// Keyboard focus went to <paramref name="element"/>: the document editor it's in (itself, or the view model of what
    /// holds it) becomes the active one, or the scene's anywhere else in a window. A menu leaves it as it is, so Edit &gt; Undo
    /// goes where the keyboard was.
    /// </summary>
    public void Focused(DependencyObject? element)
    {
        for (var node = element; node is not null; node = Parent(node))
        {
            switch (node)
            {
                case MenuBase or MenuItem or ContextMenu:
                    return;
                case IDocumentEditor editor:
                    Activate(editor);
                    return;
                case FrameworkElement { DataContext: IDocumentEditor editor }:
                    Activate(editor);
                    return;
                case Window:
                    Activate(Scene);
                    return;
            }
        }
    }

    private static DependencyObject? Parent(DependencyObject node) =>
        (node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : null) ?? LogicalTreeHelper.GetParent(node);

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window && Current is { } documents)
            documents.Opened(window);
    }

    /// <summary>
    /// A window that edits a document of its own (the Sprite Painter, the Clip Editor) has opened: its changes make it the
    /// active one, and Undo and Redo's shortcuts in it go to its document.
    /// </summary>
    public void Opened(Window window)
    {
        if (window.DataContext is not IDocumentEditor editor || window == Application.Current?.MainWindow)
            return;
        Watch(editor);
        window.Closed += (_, _) => Forget(editor);
        if (_services?.GetService<CommandRegistry>() is not { } commands)
            return;
        foreach (var id in new[] { "Edit.Undo", "Edit.Redo" })
        {
            var command = commands.Get(id);
            foreach (var gesture in new[] { command.Gesture, command.AlternateGesture }.OfType<KeyGesture>())
            {
                if (!window.InputBindings.OfType<KeyBinding>().Any(b => b.Key == gesture.Key && b.Modifiers == gesture.Modifiers))
                    window.InputBindings.Add(new KeyBinding(command, gesture) { CommandParameter = editor });
            }
        }
    }

    // The open scene or prefab, as a document.
    private sealed class SceneEditor(IWorkspace workspace) : IDocumentEditor
    {
        public EditHistory? History => workspace.Scene?.History;

        public string? DocumentName => workspace.Scene is { } document ? $"the {document.Name} {(document.IsPrefab ? "prefab" : "scene")}" : null;

        // Its changes come through the scene's own history, which the active document follows.
        public event EventHandler? Edited
        {
            add { }
            remove { }
        }

        public override string ToString() => DocumentName ?? "No scene";
    }
}
