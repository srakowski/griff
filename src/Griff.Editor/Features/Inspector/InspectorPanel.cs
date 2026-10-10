using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Transform = Griff.Scenes.Transform;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Shows and edits the selected entity. Component sections are generated from component
/// descriptors, so any component (including user scripts) gets an inspector for free.
/// </summary>
public sealed partial class InspectorPanel : ToolPanel, IPropertyHost
{
    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly Scripts.ScriptEditors _scripts;
    private SceneDocument? _document;
    private EntityHeader? _header;

    public InspectorPanel(IWorkspace workspace, ISelection selection, Scripts.ScriptEditors scripts)
    {
        _workspace = workspace;
        _selection = selection;
        _scripts = scripts;
        selection.PropertyChanged += (_, _) => Rebuild();
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Scene))
                Track(workspace.Scene);
        };
        workspace.ScriptsReloaded += (_, _) => Rebuild();
        Track(workspace.Scene);
    }

    public override string Id => "Inspector";
    public override string Title => "Inspector";
    public override string? Icon => Icons.Inspector;
    public override PanelZone Zone => PanelZone.Right;

    /// <summary>Name and enabled state of the selected entity; replaced on every selection change.</summary>
    public EntityHeader? Header
    {
        get => _header;
        private set
        {
            if (SetProperty(ref _header, value))
                OnPropertyChanged(nameof(HasEntity));
        }
    }

    public bool HasEntity => Header is not null;

    public ObservableCollection<ComponentSection> Components { get; } = [];

    /// <summary>Components that can be added, alphabetically.</summary>
    public IEnumerable<ComponentDescriptor> AvailableComponents =>
        _workspace.Components.All.Where(d => d.Type != typeof(Transform)).OrderBy(d => d.DisplayName);

    public void AddComponent(ComponentDescriptor descriptor)
    {
        if (_document is not null && _selection.Entity is { } entity)
            _document.Execute(new AddComponentEdit(entity, descriptor.CreateInstance()));
    }

    /// <summary>The component catalog, checked against the selected entity.</summary>
    public ComponentCatalogViewModel CreateCatalog() => new(_workspace.Components, _selection.Entity);

    /// <summary>The selected entity, which components are added to.</summary>
    public Entity? Entity => _selection.Entity;

    /// <summary>Adds a component and any components it requires that the entity is missing, as one undo step.</summary>
    public void AddWithRequirements(ComponentDescriptor descriptor)
    {
        if (_document is not null && _selection.Entity is { } entity)
            _document.Execute(Griff.Editing.ComponentCatalog.AddWithRequirements(_workspace.Components, descriptor, entity));
    }

    /// <summary>Opens the script a component comes from in the script editor, at its class.</summary>
    public void EditScript(Type type) => _ = _scripts.OpenComponentAsync(type);

    /// <summary>Ends the current undo step, e.g. when a drag or typed edit finishes.</summary>
    public void Seal() => _document?.History.Seal();

    internal void Execute(IEdit edit) => _document?.Execute(edit);

    public void SetValue(Component component, PropertyDescriptor property, object? value)
    {
        if (!Equals(property.GetValue(component), value))
            Execute(new SetPropertyEdit(component, property, value));
    }

    private void Track(SceneDocument? document)
    {
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
            _document.RuntimeTicked -= OnRuntimeTicked;
        }

        _document = document;
        if (_document is not null)
        {
            _document.Changed += OnDocumentChanged;
            _document.RuntimeTicked += OnRuntimeTicked;
        }

        Rebuild();
    }

    private void OnDocumentChanged(object? sender, EditEventArgs e)
    {
        if (ChangesComponents(e.Edit))
            Rebuild();
        else
            Refresh();
    }

    // Show live values while the game runs. Components added or removed by gameplay rebuild the sections.
    private void OnRuntimeTicked(object? sender, EventArgs e)
    {
        if (_selection.Entity is { } entity && entity.Components.Count != Components.Count)
            Rebuild();
        else
            Refresh();
    }

    private static bool ChangesComponents(IEdit edit) => edit switch
    {
        AddComponentEdit or RemoveComponentEdit => true,
        CompositeEdit composite => composite.Edits.Any(ChangesComponents),
        _ => false,
    };

    private void Rebuild()
    {
        Components.Clear();
        var entity = _selection.Entity;
        Header = entity is null ? null : new EntityHeader(entity, this);
        if (entity is null)
            return;

        foreach (var component in entity.Components)
            Components.Add(new ComponentSection(component, this, _workspace.Components));
    }

    private void Refresh()
    {
        Header?.Refresh();
        foreach (var section in Components)
            section.Refresh();
    }

    internal static string Humanize(string name) => PascalCaseBoundary().Replace(name, " ");

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex PascalCaseBoundary();
}

public sealed class EntityHeader(Entity entity, InspectorPanel owner) : ObservableObject
{
    public string Name
    {
        get => entity.Name;
        set
        {
            var name = value.Trim();
            if (name.Length > 0 && name != entity.Name)
                owner.Execute(new RenameEntityEdit(entity, name));
            OnPropertyChanged();
        }
    }

    public bool IsEnabled
    {
        get => entity.IsEnabled;
        set
        {
            if (value != entity.IsEnabled)
                owner.Execute(new SetEntityEnabledEdit(entity, value));
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed class ComponentSection : ObservableObject
{
    private readonly InspectorPanel _owner;
    private bool _isExpanded = true;

    public ComponentSection(Component component, InspectorPanel owner, ComponentRegistry registry)
    {
        _owner = owner;
        Component = component;
        RemoveCommand = new RelayCommand(Remove, () => CanRemove);
        EditScriptCommand = new RelayCommand(() => owner.EditScript(component.GetType()));

        if (component is MissingComponent missing)
        {
            Title = missing.TypeName;
            IsMissing = true;
            Properties = [];
            return;
        }

        var descriptor = registry.Get(component.GetType());
        Title = descriptor.DisplayName;
        Properties = descriptor.Properties.Select(p => new PropertyField(component, p, owner)).ToList();
    }

    public Component Component { get; }

    public string Title { get; }

    public bool IsMissing { get; }

    public bool CanRemove => Component is not Transform;

    /// <summary>True for components from the project's scripts, which can be opened in the script editor.</summary>
    public bool IsScript => Component is not MissingComponent && Component.GetType().Assembly != typeof(Component).Assembly;

    public IRelayCommand EditScriptCommand { get; }

    public IReadOnlyList<PropertyField> Properties { get; }

    public IRelayCommand RemoveCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public void Refresh()
    {
        foreach (var property in Properties)
            property.Refresh();
    }

    private void Remove()
    {
        if (Component.Entity is not null)
            _owner.Execute(new RemoveComponentEdit(Component));
    }
}

public enum FieldKind
{
    Bool,
    Number,
    Text,
    Enum,
    Vector,
    Color,
    Lines,

    /// <summary>A sound or music file: text, with buttons to play it, pick one from the project, or make one.</summary>
    Sound,
}

/// <summary>One editable property. Exposes typed accessors for each editor template.</summary>
public sealed class PropertyField : ObservableObject
{
    private static readonly string[] AxisNames = ["X", "Y", "Z", "W"];

    private readonly Component _component;
    private readonly PropertyDescriptor _property;
    private readonly IPropertyHost _owner;

    public PropertyField(Component component, PropertyDescriptor property, IPropertyHost owner)
    {
        _component = component;
        _property = property;
        _owner = owner;
        Label = InspectorPanel.Humanize(property.Name);

        var type = property.Type;
        Kind = type == typeof(bool) ? FieldKind.Bool
            : type == typeof(int) || type == typeof(float) || type == typeof(double) ? FieldKind.Number
            : type == typeof(string) && IsSoundName(property.Name) ? FieldKind.Sound
            : type == typeof(string) ? FieldKind.Text
            : type.IsEnum ? FieldKind.Enum
            : type == typeof(Color) ? FieldKind.Color
            : type == typeof(IReadOnlyList<string>) ? FieldKind.Lines
            : FieldKind.Vector;

        if (Kind == FieldKind.Enum)
            EnumValues = Enum.GetValues(type);

        if (Kind == FieldKind.Vector)
        {
            Axes = Enumerable.Range(0, ToFloats(Value).Length)
                .Select(i => new AxisField(this, i, AxisNames[i]))
                .ToList();
        }
    }

    public string Label { get; }

    public FieldKind Kind { get; }

    /// <summary>Properties named like JumpSound, Sound or Music hold a sound file's asset path.</summary>
    public static bool IsSoundName(string name) => name.EndsWith("Sound", StringComparison.Ordinal) || name == "Music";

    /// <summary>For a sound field: whether it's music (made in the Sound Maker's Music mode) rather than an effect.</summary>
    public bool IsMusic => _property.Name == "Music";

    /// <summary>For a sound field: set to a file that isn't in the project.</summary>
    public bool IsSoundMissing => Kind == FieldKind.Sound && Value is string { Length: > 0 } path &&
        Audio.EditorAudio.Current is { AssetsPath: not null } audio && audio.Resolve(path) is null;

    public string SoundTip => Value is string { Length: > 0 } path && Audio.EditorAudio.Current?.CanEdit(path) == true
        ? "Edit this sound in the Sound Maker"
        : IsMusic ? "Make music for this in the Sound Maker" : "Make a sound for this in the Sound Maker";

    /// <summary>Plays the field's sound, or stops it if it's playing.</summary>
    public void PreviewSound() => Audio.EditorAudio.Current?.TogglePreview(Value as string);

    /// <summary>
    /// Opens the Sound Maker on the field's sound if it was made there, or on a new one; whatever it saves is set here.
    /// </summary>
    public void MakeSound()
    {
        if (Audio.EditorAudio.Current is not { } audio)
            return;
        var current = Value as string;
        var mode = IsMusic ? Audio.SoundMakerMode.Music : Audio.SoundMakerMode.Effect;
        audio.OpenMaker(mode, audio.CanEdit(current) ? current : null, saved => TextValue = saved);
    }

    /// <summary>The project's sounds to choose from.</summary>
    public IReadOnlyList<string> SoundChoices() => Audio.EditorAudio.Current?.Sounds() ?? [];

    public IRelayCommand PreviewSoundCommand => _previewSound ??= new RelayCommand(PreviewSound);

    public IRelayCommand MakeSoundCommand => _makeSound ??= new RelayCommand(MakeSound);

    /// <summary>Shows the project's sounds in a menu under the button that asked; picking one sets it.</summary>
    public IRelayCommand<FrameworkElement> ChooseSoundCommand => _chooseSound ??= new RelayCommand<FrameworkElement>(ChooseSound);

    private IRelayCommand? _previewSound, _makeSound;
    private IRelayCommand<FrameworkElement>? _chooseSound;

    private void ChooseSound(FrameworkElement? anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var current = Value as string;
        var sounds = SoundChoices();
        foreach (var sound in sounds)
        {
            var item = new MenuItem
            {
                Header = sound,
                IsCheckable = true,
                IsChecked = string.Equals(sound, current, StringComparison.OrdinalIgnoreCase),
                Icon = Icons.Create(sound.StartsWith("Music/", StringComparison.OrdinalIgnoreCase) ? Icons.Music : Icons.Audio),
            };
            item.Click += (_, _) =>
            {
                TextValue = sound;
                Audio.EditorAudio.Current?.Preview(sound);
            };
            menu.Items.Add(item);
        }

        if (sounds.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No sounds in the project yet", IsEnabled = false });
        menu.Items.Add(new Separator());
        var make = new MenuItem { Header = IsMusic ? "New Music..." : "New Sound...", Icon = Icons.Create(IsMusic ? Icons.Music : Icons.Audio) };
        make.Click += (_, _) => Audio.EditorAudio.Current?.OpenMaker(IsMusic ? Audio.SoundMakerMode.Music : Audio.SoundMakerMode.Effect, saved: saved => TextValue = saved);
        menu.Items.Add(make);
        if (!string.IsNullOrEmpty(current))
        {
            var clear = new MenuItem { Header = "None", Icon = Icons.Create(Icons.Clear) };
            clear.Click += (_, _) => TextValue = null;
            menu.Items.Add(clear);
        }

        menu.IsOpen = true;
    }

    public bool IsInteger => _property.Type == typeof(int);

    public Array? EnumValues { get; }

    public IReadOnlyList<AxisField> Axes { get; } = [];

    public object? Value
    {
        get => _property.GetValue(_component);
        set => _owner.SetValue(_component, _property, value);
    }

    public bool BoolValue
    {
        get => Value is true;
        set
        {
            Value = value;
            _owner.Seal();
        }
    }

    public double NumberValue
    {
        get => Convert.ToDouble(Value);
        // Boxed separately: as one conditional, int and float would both widen to double.
        set => Value = _property.Type == typeof(int) ? (object)(int)Math.Round(value)
            : _property.Type == typeof(float) ? (object)(float)value
            : value;
    }

    public string? TextValue
    {
        get => Value as string;
        set
        {
            Value = string.IsNullOrEmpty(value) ? null : value;
            _owner.Seal();
        }
    }

    /// <summary>A list of strings as text, one per line.</summary>
    public string LinesValue
    {
        get => Value is IReadOnlyList<string> lines ? string.Join(Environment.NewLine, lines) : "";
        set
        {
            var lines = value.ReplaceLineEndings("\n").Split('\n');
            if (Value is not IReadOnlyList<string> current || !current.SequenceEqual(lines))
                Value = lines;
            _owner.Seal();
        }
    }

    public object? EnumValue
    {
        get => Value;
        set
        {
            Value = value;
            _owner.Seal();
        }
    }

    public string ColorHex
    {
        get => Value is Color color ? color.ToHex() : "";
        set
        {
            if (Color.TryParse(value, out var color))
            {
                Value = color;
                _owner.Seal();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ColorBrush));
        }
    }

    public Brush ColorBrush
    {
        get
        {
            var color = Value is Color c ? c : Color.Black;
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.AByte, color.RByte, color.GByte, color.BByte));
            brush.Freeze();
            return brush;
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
        foreach (var axis in Axes)
            axis.Refresh();
    }

    internal float GetAxis(int index) => ToFloats(Value)[index];

    internal void SetAxis(int index, float value)
    {
        var values = ToFloats(Value);
        values[index] = value;
        Value = FromFloats(_property.Type, values);
    }

    private static float[] ToFloats(object? value) => value switch
    {
        Vector2 v => [v.X, v.Y],
        Vector3 v => [v.X, v.Y, v.Z],
        Vector4 v => [v.X, v.Y, v.Z, v.W],
        Quaternion q => [q.X, q.Y, q.Z, q.W],
        _ => [],
    };

    private static object FromFloats(Type type, float[] v) =>
        type == typeof(Vector2) ? new Vector2(v[0], v[1])
        : type == typeof(Vector3) ? new Vector3(v[0], v[1], v[2])
        : type == typeof(Vector4) ? new Vector4(v[0], v[1], v[2], v[3])
        : new Quaternion(v[0], v[1], v[2], v[3]);
}

/// <summary>One component (X, Y, Z or W) of a vector property.</summary>
public sealed class AxisField(PropertyField owner, int index, string label) : ObservableObject
{
    public string Label { get; } = label;

    public double Value
    {
        get => owner.GetAxis(index);
        set => owner.SetAxis(index, (float)value);
    }

    public void Refresh() => OnPropertyChanged(nameof(Value));
}

/// <summary>What a <see cref="PropertyField"/> edits through: it applies changes as undoable edits and ends undo steps.</summary>
public interface IPropertyHost
{
    void SetValue(Component component, PropertyDescriptor property, object? value);

    /// <summary>Ends the current undo step, e.g. when a drag or typed edit finishes.</summary>
    void Seal();
}
