using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Editor.Features.Scripts;

/// <summary>
/// The component panel beside a script: what the component it declares is (its description, what it needs), each
/// property with its kind, starting value and description, and the live values on an entity in the scene that uses it,
/// editable right there. Clicking a property jumps to it in the code, and the property the caret is in lights up.
/// </summary>
public sealed class ComponentPanel : ObservableObject, IPropertyHost
{
    private readonly ScriptDocument _script;
    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly DispatcherTimer _outlineTimer;
    private SceneDocument? _scene;
    private ComponentDescriptor? _descriptor;
    private CatalogEntry? _entry;
    private IReadOnlyList<ScriptClass> _outline = [];
    private IReadOnlyList<Entity> _instances = [];
    private Entity? _instance;
    private string? _activeProperty;
    private string _className = "";
    private bool _pinned;

    public ComponentPanel(ScriptDocument script, IWorkspace workspace, ISelection selection)
    {
        _script = script;
        _workspace = workspace;
        _selection = selection;
        JumpCommand = new RelayCommand<ConfigRow>(row => row?.Jump());
        SelectInstanceCommand = new RelayCommand(() =>
        {
            if (Instance is { } entity)
                _selection.Select(entity);
        }, () => Instance is not null);
        AddToSelectedCommand = new RelayCommand(AddToSelected, () => CanAddToSelected);

        _outlineTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
        {
            _outlineTimer!.Stop();
            ReadOutline();
        }, Dispatcher.CurrentDispatcher);
        script.Text.TextChanged += (_, _) =>
        {
            _outlineTimer.Stop();
            _outlineTimer.Start();
        };
        workspace.ScriptsReloaded += (_, _) => Rebuild();
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IWorkspace.Scene) or nameof(IWorkspace.IsPlaying))
                Track(workspace.Scene);
        };
        selection.PropertyChanged += (_, _) => FollowSelection();
        script.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ScriptDocument.IsDirty) or nameof(ScriptDocument.ErrorCount))
            {
                OnPropertyChanged(nameof(Notice));
                OnPropertyChanged(nameof(HasNotice));
            }
        };
        Track(workspace.Scene);
        ReadOutline();
    }

    public IRelayCommand<ConfigRow> JumpCommand { get; }

    public IRelayCommand SelectInstanceCommand { get; }

    public IRelayCommand AddToSelectedCommand { get; }

    /// <summary>The class the script declares (the component, if it is one).</summary>
    public string ClassName
    {
        get => _className;
        private set => SetProperty(ref _className, value);
    }

    /// <summary>True once the class has compiled into a component Griff knows.</summary>
    public bool IsComponent => _descriptor is not null;

    public string Title => _descriptor?.DisplayName ?? ClassName;

    public string Category => _entry?.Category ?? "Scripts";

    public DocText Summary => _entry is { Summary.IsEmpty: false } entry
        ? entry.Summary
        : new DocText([[new DocRun("No description yet. A /// <summary> comment above the class describes it here, in the catalog and in tooltips.")]]);

    public bool HasSummary => _entry is { Summary.IsEmpty: false };

    public string Requires => _descriptor is { Requires.Count: > 0 } d ? string.Join(", ", d.Requires.Select(r => r.Type.Name)) : "";

    public bool HasRequires => Requires.Length > 0;

    public string WorksWith => _descriptor is { WorksWith.Count: > 0 } d ? string.Join(", ", d.WorksWith.Select(w => w.Type.Name)) : "";

    public bool HasWorksWith => WorksWith.Length > 0;

    public ObservableCollection<ConfigRow> Rows { get; } = [];

    /// <summary>Entities in the open scene that have this component.</summary>
    public IReadOnlyList<Entity> Instances
    {
        get => _instances;
        private set
        {
            SetProperty(ref _instances, value);
            OnPropertyChanged(nameof(HasInstances));
            OnPropertyChanged(nameof(InstancesText));
        }
    }

    public bool HasInstances => Instances.Count > 0;

    /// <summary>The entity whose values the panel shows and edits.</summary>
    public Entity? Instance
    {
        get => _instance;
        set
        {
            if (!SetProperty(ref _instance, value))
                return;
            // Picked by hand: stays until the entity goes away, rather than following the Hierarchy.
            _pinned = value is not null && value != _selection.Entity;
            BindValues();
            SelectInstanceCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(InstanceLabel));
        }
    }

    public string InstanceLabel => Instance is null ? "" : _workspace.IsPlaying ? "Live values" : "Values";

    public string InstancesText => !IsComponent
        ? ""
        : Instances.Count switch
        {
            0 => $"Nothing in {_scene?.Name ?? "the scene"} uses {Title} yet.",
            1 => $"Used by 1 entity in {_scene?.Name}.",
            var n => $"Used by {n} entities in {_scene?.Name}.",
        };

    public bool CanAddToSelected => IsComponent && _selection.Entity is { } entity && !_workspace.IsPlaying &&
                                    entity.Components.All(c => c.GetType() != _descriptor!.Type);

    public string AddToSelectedText => _selection.Entity is { } entity ? $"Add to {entity.Name}" : "Select an entity to add it to";

    /// <summary>Why the panel may not match the code yet, e.g. unsaved changes or a script that doesn't compile.</summary>
    public string? Notice
    {
        get
        {
            if (!IsComponent)
            {
                return _outline.Count == 0
                    ? "This script doesn't declare a class."
                    : _script.IsDirty || _script.ErrorCount > 0
                        ? $"{ClassName} isn't compiled yet. Save it (with no errors) to see it as a component."
                        : $"{ClassName} isn't a component. Components derive from Behavior.";
            }

            if (Rows.Any(r => r.IsNew))
                return "Save to compile the new properties.";
            if (_script.IsDirty)
                return "Unsaved changes: the panel shows the last saved version.";
            return null;
        }
    }

    public bool HasNotice => Notice is not null;

    /// <summary>The property the caret is in; its row lights up.</summary>
    public string? ActiveProperty
    {
        get => _activeProperty;
        set
        {
            if (!SetProperty(ref _activeProperty, value))
                return;
            foreach (var row in Rows)
                row.IsActive = row.Name == value;
        }
    }

    // ----- IPropertyHost -----

    public void SetValue(Component component, PropertyDescriptor property, object? value)
    {
        if (_scene is not null && !Equals(property.GetValue(component), value))
            _scene.Execute(new SetPropertyEdit(component, property, value));
    }

    public void Seal() => _scene?.History.Seal();

    // ----- Keeping up -----

    private void ReadOutline()
    {
        _outline = ScriptLanguage.Outline(_script.Text.Text);
        var name = _outline.FirstOrDefault(c => Find(c.Name) is not null)?.Name ?? _outline.FirstOrDefault()?.Name ?? _script.Name;
        if (name != ClassName)
        {
            ClassName = name;
            Rebuild();
            return;
        }

        UpdateDeclarations();
    }

    private ComponentDescriptor? Find(string name) =>
        _workspace.Components.All.FirstOrDefault(d => d.Type.Name == name && d.Type.Assembly != typeof(Component).Assembly);

    /// <summary>Starts over from the compiled component: its documentation and properties.</summary>
    public void Rebuild()
    {
        _descriptor = Find(ClassName);
        _entry = _descriptor is null ? null : ComponentCatalog.Build(_workspace.Components).FirstOrDefault(e => e.Descriptor == _descriptor);
        Rows.Clear();
        if (_entry is not null)
        {
            foreach (var property in _entry.Properties)
            {
                var descriptor = _descriptor!.Properties.First(p => p.Name == property.Name);
                Rows.Add(new ConfigRow(this, property.Name, property.Kind, property.Default, property.Doc, descriptor));
            }
        }

        UpdateDeclarations();
        FindInstances();
        foreach (var row in Rows)
            row.IsActive = row.Name == ActiveProperty;
        OnPropertyChanged(string.Empty);
        AddToSelectedCommand.NotifyCanExecuteChanged();
    }

    // Where each property is declared in the current text; properties typed but not compiled yet show as new.
    private void UpdateDeclarations()
    {
        var declared = _outline.FirstOrDefault(c => c.Name == ClassName)?.Properties ?? [];
        foreach (var row in Rows.Where(r => r.IsNew).ToList())
        {
            if (declared.All(d => d.Name != row.Name))
                Rows.Remove(row);
        }

        foreach (var member in declared)
        {
            var row = Rows.FirstOrDefault(r => r.Name == member.Name);
            if (row is null && IsComponent)
            {
                row = new ConfigRow(this, member.Name, "", "", DocText.Empty, null);
                Rows.Add(row);
            }

            row?.Declare(member.Start, member.Length);
        }

        foreach (var row in Rows.Where(r => declared.All(d => d.Name != r.Name)))
            row.Declare(null, 0);
        OnPropertyChanged(nameof(Notice));
        OnPropertyChanged(nameof(HasNotice));
    }

    private void Track(SceneDocument? scene)
    {
        if (_scene is not null)
        {
            _scene.Changed -= OnSceneChanged;
            _scene.RuntimeTicked -= OnRuntimeTicked;
        }

        _scene = scene;
        if (_scene is not null)
        {
            _scene.Changed += OnSceneChanged;
            _scene.RuntimeTicked += OnRuntimeTicked;
        }

        _pinned = false;
        FindInstances();
        OnPropertyChanged(nameof(InstanceLabel));
        AddToSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnSceneChanged(object? sender, EditEventArgs e)
    {
        var count = Instances.Count;
        FindInstances();
        if (Instances.Count == count)
            RefreshValues();
    }

    private void OnRuntimeTicked(object? sender, EventArgs e) => RefreshValues();

    private void FollowSelection()
    {
        if (!_pinned && _selection.Entity is { } entity && Instances.Contains(entity))
        {
            _instance = entity;
            OnPropertyChanged(nameof(Instance));
            BindValues();
        }

        OnPropertyChanged(nameof(CanAddToSelected));
        OnPropertyChanged(nameof(AddToSelectedText));
        AddToSelectedCommand.NotifyCanExecuteChanged();
    }

    private void FindInstances()
    {
        var type = _descriptor?.Type;
        var found = type is null || _scene is null ? [] : _scene.Scene.Entities.Where(e => e.Components.Any(c => c.GetType() == type)).ToList();
        if (!found.SequenceEqual(Instances))
            Instances = found;
        var keep = _instance is not null && found.Contains(_instance) ? _instance : null;
        var pick = keep ?? (_selection.Entity is { } selected && found.Contains(selected) ? selected : found.FirstOrDefault());
        if (keep is null)
            _pinned = false;
        if (pick != _instance)
        {
            _instance = pick;
            OnPropertyChanged(nameof(Instance));
            OnPropertyChanged(nameof(InstanceLabel));
            SelectInstanceCommand.NotifyCanExecuteChanged();
        }

        BindValues();
    }

    private void BindValues()
    {
        var component = _instance?.Components.FirstOrDefault(c => c.GetType() == _descriptor?.Type);
        foreach (var row in Rows)
            row.Bind(component);
    }

    private void RefreshValues()
    {
        foreach (var row in Rows)
            row.Field?.Refresh();
    }

    private void AddToSelected()
    {
        if (_descriptor is null || _selection.Entity is not { } entity || _workspace.Scene is not { } scene)
            return;
        scene.Execute(ComponentCatalog.AddWithRequirements(_workspace.Components, _descriptor, entity));
        _pinned = false;
        FindInstances();
        FollowSelection();
    }

    internal void Jump(ConfigRow row)
    {
        if (row.DeclarationStart is { } start)
            _script.Navigate(start, row.DeclarationLength);
    }
}

/// <summary>One property in the component panel: what it is, where it's declared, and its value on the shown entity.</summary>
public sealed class ConfigRow : ObservableObject
{
    private readonly ComponentPanel _panel;
    private readonly PropertyDescriptor? _descriptor;
    private PropertyField? _field;
    private bool _isActive;
    private int? _declarationStart;

    internal ConfigRow(ComponentPanel panel, string name, string kind, string initial, DocText doc, PropertyDescriptor? descriptor)
    {
        _panel = panel;
        _descriptor = descriptor;
        Name = name;
        Label = InspectorPanel.Humanize(name);
        Kind = kind;
        Initial = initial;
        Doc = doc;
    }

    public string Name { get; }

    public string Label { get; }

    public string Kind { get; }

    public string Initial { get; }

    /// <summary>"number, starts at 5".</summary>
    public string Details => IsNew ? "new: save to compile" : Initial.Length > 0 ? $"{Kind}, starts at {Initial}" : Kind;

    public DocText Doc { get; }

    public bool HasDoc => !Doc.IsEmpty;

    /// <summary>Declared in the code but not compiled yet.</summary>
    public bool IsNew => _descriptor is null;

    /// <summary>The live editor for the shown entity's value, or null when no entity is shown.</summary>
    public PropertyField? Field
    {
        get => _field;
        private set
        {
            SetProperty(ref _field, value);
            OnPropertyChanged(nameof(HasField));
        }
    }

    public bool HasField => Field is not null;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public int? DeclarationStart => _declarationStart;

    public int DeclarationLength { get; private set; }

    public bool CanJump => _declarationStart is not null;

    internal void Declare(int? start, int length)
    {
        _declarationStart = start;
        DeclarationLength = length;
        OnPropertyChanged(nameof(CanJump));
    }

    internal void Bind(Component? component) =>
        Field = component is null || _descriptor is null ? null : new PropertyField(component, _descriptor, _panel);

    public void Jump() => _panel.Jump(this);
}
