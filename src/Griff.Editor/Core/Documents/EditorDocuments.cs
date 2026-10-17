using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Undo;
using Griff.Editor.Core.Workspace;

namespace Griff.Editor.Core.Documents;

/// <summary>
/// An editor open in a tab of its own in the document area, beside the Scene, the Game, prefabs and scripts: a table, the
/// Sprite Painter on a picture, the Sound Maker on a sound, the Clip Editor. Its <see cref="Model"/> is what the tab
/// works on (agents reach it by the tab's <see cref="Name"/> with ui_roots and ui_inspect); one with unsaved changes
/// (<see cref="IsDirty"/>, a dot on its tab) asks whether to save them before it closes, as a scene does.
/// </summary>
public sealed class EditorDocument : ObservableObject
{
    private readonly Func<FrameworkElement> _view;
    private string _title = "";
    private string? _key;
    private bool _isDirty;

    /// <param name="kind">What edits it ("Sprite Painter", "Table").</param>
    /// <param name="model">What its tab works on: the view's DataContext, and what agents reach.</param>
    /// <param name="view">Makes the view its tab shows, once, when its tab is made.</param>
    public EditorDocument(string kind, object model, Func<FrameworkElement> view, string? icon = null)
    {
        Kind = kind;
        Model = model;
        Icon = icon;
        _view = view;
    }

    /// <summary>What edits it: "Sprite Painter", "Sound Maker", "Table", "Clip Editor".</summary>
    public string Kind { get; }

    /// <summary>What its tab works on (the Sprite Painter's view model, a table's).</summary>
    public object Model { get; }

    /// <summary>Glyph from <see cref="Themes.Icons"/> for its tab.</summary>
    public string? Icon { get; }

    /// <summary>What its tab says: what it has open ("slime.png", "Spells"), or its kind when that's all there is.</summary>
    public string Title
    {
        get => _title.Length > 0 ? _title : Kind;
        set
        {
            if (SetProperty(ref _title, value ?? ""))
                OnPropertyChanged(nameof(Name));
        }
    }

    /// <summary>The tab's name, as agents find it: "Sprite Painter: slime.png", or its kind when that's its title.</summary>
    public string Name => Title == Kind ? Kind : $"{Kind}: {Title}";

    /// <summary>What it has open, for one tab per thing (an asset's path); null for one that's new, or follows the selection.</summary>
    public string? Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    /// <summary>Whether it has changes that aren't saved: its tab shows a dot, and closing it asks.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    /// <summary>Saves it, returning false when it couldn't be (it needs a name first, say); null for one with nothing to save.</summary>
    public Func<bool>? Save { get; init; }

    /// <summary>What its tab's tooltip says, or null.</summary>
    public string? ToolTip { get; init; }

    /// <summary>Raised once it has closed, to let go of what it held.</summary>
    public event EventHandler? Closed;

    /// <summary>The view its tab shows, once its tab is made.</summary>
    public FrameworkElement? View { get; private set; }

    /// <summary>Its view, made the first time it's asked for.</summary>
    public FrameworkElement CreateView() => View ??= _view();

    internal void OnClosed() => Closed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// The editors open in tabs of their own in the document area (<see cref="EditorDocument"/>): a table opened in a tab, the
/// Sprite Painter and Sound Maker (one tab per picture or sound), the Clip Editor, the Dialogue Editor and the Move List.
/// The window's document tabs show them; <see cref="Show"/> opens one (or brings its tab forward) and <see cref="Close"/>
/// closes one, asking about unsaved changes as a scene's tab does. They close with the project, after asking.
/// </summary>
public sealed class EditorDocuments : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly ActiveDocument _active;
    private EditorDocument? _showing;

    public EditorDocuments(IDialogService dialogs, IProjectSession projectSession, ActiveDocument active)
    {
        _dialogs = dialogs;
        _active = active;
        // Closing the project (or the editor) asks about each one's unsaved changes first; once it's closed, they go.
        projectSession.AddCloseGuard(() => Task.FromResult(ConfirmCloseAll()));
        projectSession.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IProjectSession.Project))
                CloseAll(ask: false);
        };
    }

    /// <summary>The open ones, in the order their tabs were opened.</summary>
    public ObservableCollection<EditorDocument> Open { get; } = [];

    /// <summary>The one whose tab is in front, or null when the Scene, the Game, a script or a panel is.</summary>
    public EditorDocument? Showing
    {
        get => _showing;
        set => SetProperty(ref _showing, value);
    }

    /// <summary>The names of the open ones (their tabs' names), for agents.</summary>
    public IReadOnlyList<string> Names => [.. Open.Select(d => d.Name)];

    /// <summary>Raised when one should come to the front.</summary>
    public event EventHandler<EditorDocument>? ShowRequested;

    /// <summary>Opens a document in a tab of its own (or brings its tab forward, if it's open) and returns it.</summary>
    public EditorDocument Show(EditorDocument document)
    {
        if (!Open.Contains(document))
        {
            Open.Add(document);
            // Its edits are the ones undo goes to, as clicking into it would make them.
            if (document.Model is IDocumentEditor editor)
                _active.Activate(editor);
        }

        ShowRequested?.Invoke(this, document);
        return document;
    }

    /// <summary>The open one of <paramref name="kind"/> that has <paramref name="key"/> open (an asset's path), if any.</summary>
    public EditorDocument? Find(string kind, string? key) =>
        Open.FirstOrDefault(d => d.Kind == kind && string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The open one whose model is <paramref name="model"/>, if any.</summary>
    public EditorDocument? Of(object model) => Open.FirstOrDefault(d => ReferenceEquals(d.Model, model));

    /// <summary>
    /// Closes a document's tab. With unsaved changes it asks first whether to save them (unless <paramref name="ask"/> is
    /// false, which throws them away); false if it stays open (cancelled, or the save didn't happen).
    /// </summary>
    public bool Close(EditorDocument document, bool ask = true)
    {
        if (!Open.Contains(document))
            return true;
        if (ask && document.IsDirty)
        {
            switch (_dialogs.AskSaveChanges(document.Name))
            {
                case SaveChangesChoice.Cancel:
                    return false;
                case SaveChangesChoice.Save when document.Save?.Invoke() == false:
                    return false;
            }
        }

        Open.Remove(document);
        if (ReferenceEquals(Showing, document))
            Showing = null;
        if (document.Model is IDocumentEditor editor)
            _active.Forget(editor);
        document.OnClosed();
        return true;
    }

    /// <summary>Closes the document with that name (its tab's, or what it has open: "Sprite Painter: slime.png", "slime.png"), asking about unsaved changes.</summary>
    public bool Close(string name)
    {
        var wanted = name.Trim();
        var document = Open.FirstOrDefault(d => d.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            ?? Open.FirstOrDefault(d => d.Title.Equals(wanted, StringComparison.OrdinalIgnoreCase) || d.Kind.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No document called \"{name}\" is open" + (Open.Count > 0 ? $" (open: {string.Join(", ", Names)})." : "."), nameof(name));
        return Close(document);
    }

    /// <summary>Closes every one (asking about unsaved changes, unless <paramref name="ask"/> is false); false if one stays open.</summary>
    public bool CloseAll(bool ask = true)
    {
        foreach (var document in Open.ToList())
        {
            if (!Close(document, ask))
                return false;
        }

        return true;
    }

    // Before the project closes: each one with unsaved changes is saved or let go, or the closing is called off.
    private bool ConfirmCloseAll()
    {
        foreach (var document in Open.Where(d => d.IsDirty).ToList())
        {
            switch (_dialogs.AskSaveChanges(document.Name))
            {
                case SaveChangesChoice.Cancel:
                    return false;
                case SaveChangesChoice.Save when document.Save?.Invoke() == false:
                    return false;
            }
        }

        return true;
    }
}
