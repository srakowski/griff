using System.ComponentModel;
using Griff.Editor.Core.Documents;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Undo;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Data;

/// <summary>
/// Tables open in tabs of their own in the document area, beside the scene: each is a spreadsheet like the Tables panel's
/// (the same view and the same edits, saved straight away and undone with Edit &gt; Undo), for one table, with all the
/// room of the document area. Data &gt; Open Table in Tab opens one; with <see cref="InTabs"/> on (Data &gt; Open Tables in
/// Tabs), Data &gt; Open Table, New Table and double-clicking a table in the Assets panel do too, instead of showing it in
/// the Tables panel. The Data menu's table commands act on the table worked in last (<see cref="Current"/>): a table's tab
/// or the Tables panel.
/// </summary>
public sealed class TableDocuments
{
    public const string Kind = "Table";

    private readonly EditorDocuments _documents;
    private readonly TablesPanel _panel;
    private readonly ActiveDocument _active;
    private readonly TableStore _store;
    private readonly IProjectSession _projectSession;
    private readonly IStatusService _status;
    private readonly IClipboard _clipboard;
    private readonly Func<TablesPanelView> _views;
    private ISettingsStore _settings;

    public TableDocuments(EditorDocuments documents, TablesPanel panel, ActiveDocument active, TableStore store, IProjectSession projectSession,
        IStatusService status, IClipboard clipboard, Func<TablesPanelView> views, ISettingsStore settings)
    {
        _documents = documents;
        _panel = panel;
        _active = active;
        _store = store;
        _projectSession = projectSession;
        _status = status;
        _clipboard = clipboard;
        _views = views;
        _settings = settings;
    }

    /// <summary>
    /// Whether opening a table (Data &gt; Open Table, New Table, double-clicking one in the Assets panel) opens it in a tab
    /// of its own rather than in the Tables panel. Off to start with; kept between sessions.
    /// </summary>
    public bool InTabs
    {
        get => _settings.Settings.TablesInTabs;
        set
        {
            _settings.Settings.TablesInTabs = value;
            _settings.Save();
        }
    }

    /// <summary>
    /// The table editor the Data menu's table commands act on: the table's tab worked in last, or the Tables panel (worked
    /// in last, or with no table tab worked in since).
    /// </summary>
    public TablesPanel Current => _active.Editor is TablesPanel tables && (tables == _panel || _documents.Of(tables) is not null) ? tables : _panel;

    /// <summary>The table editors open in tabs.</summary>
    public IEnumerable<TablesPanel> Editors => _documents.Open.Where(d => d.Kind == Kind).Select(d => (TablesPanel)d.Model);

    /// <summary>
    /// Opens a table (by its name, or its path in Assets) in a tab of its own, or brings its tab forward if it's open there.
    /// Throws <see cref="ArgumentException"/> if there's no such table.
    /// </summary>
    public TablesPanel Show(string table)
    {
        var entry = _store.Entry(table) ?? throw new ArgumentException($"There's no table called \"{table}\".", nameof(table));
        if (_documents.Find(Kind, entry.AssetPath) is { } open)
        {
            _documents.Show(open);
            return (TablesPanel)open.Model;
        }

        var model = new TablesPanel(_store, _projectSession, _status, _clipboard);
        model.Open(entry.Name);
        var document = new EditorDocument(Kind, model, () =>
        {
            var view = _views();
            view.DataContext = model;
            return view;
        }, Icons.Table)
        {
            Title = entry.Name,
            Key = entry.AssetPath,
            ToolTip = $"The {entry.Name} table ({entry.AssetPath}): changes are saved as they're made",
        };

        // Picking another table in its tab shows that one there instead; a table that's gone takes its tab with it.
        void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(TablesPanel.TableName) or nameof(TablesPanel.Table) or ""))
                return;
            var showing = document.Title;
            if (model.TableName is not { } name || (!string.Equals(name, showing, StringComparison.OrdinalIgnoreCase) && _store.Entry(showing) is null))
            {
                _documents.Close(document, ask: false);
                return;
            }

            document.Title = name;
            document.Key = _store.Entry(name)?.AssetPath;
        }

        model.PropertyChanged += OnChanged;
        document.Closed += (_, _) =>
        {
            model.PropertyChanged -= OnChanged;
            ((IDisposable)model).Dispose();
        };
        _documents.Show(document);
        return model;
    }

    /// <summary>
    /// Opens a table where <see cref="InTabs"/> says: in a tab of its own, or in the Tables panel (shown).
    /// False if there's no such table.
    /// </summary>
    internal bool Open(string table, Core.Panels.PanelLayout layout)
    {
        if (_store.Entry(table) is null)
            return _panel.Open(table);
        if (InTabs)
        {
            Show(table);
            return true;
        }

        _panel.Open(table);
        layout.Show(_panel.Id);
        return true;
    }

    /// <summary>Opening tables follows the Tables panel from now on, kept in memory only: demos start the same way every time.</summary>
    internal void StartOver() => _settings = new MemorySettingsStore();
}
