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
/// Shows and edits the selected entities. Component sections are generated from component
/// descriptors, so any component (including user scripts) gets an inspector for free. With several
/// entities selected it shows the components they all have: a change sets it on every one of them as
/// one undo step, and values that differ show as mixed.
/// </summary>
public sealed partial class InspectorPanel : ToolPanel, IPropertyHost
{
    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly Scripts.ScriptEditors _scripts;
    private SceneDocument? _document;
    private EntityHeader? _header;
    private string? _hiddenNote;
    private IReadOnlyList<Entity> _entities = [];
    private int[] _componentCounts = [];

    public InspectorPanel(IWorkspace workspace, ISelection selection, Scripts.ScriptEditors scripts, Art.EditorArt art)
    {
        _workspace = workspace;
        _selection = selection;
        _scripts = scripts;
        // A picture saved over the one a field shows: show the new one.
        art.ImageSaved += (_, _) => Refresh();
        selection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ISelection.Entities))
                Rebuild();
        };
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

    /// <summary>Name and enabled state of the selected entities; replaced on every selection change.</summary>
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

    /// <summary>With several entities selected, says that components only some of them have are left out; otherwise null.</summary>
    public string? HiddenNote
    {
        get => _hiddenNote;
        private set
        {
            if (SetProperty(ref _hiddenNote, value))
                OnPropertyChanged(nameof(HasHiddenNote));
        }
    }

    public bool HasHiddenNote => HiddenNote is not null;

    /// <summary>Components that can be added, alphabetically.</summary>
    public IEnumerable<ComponentDescriptor> AvailableComponents =>
        _workspace.Components.All.Where(d => d.Type != typeof(Transform)).OrderBy(d => d.DisplayName);

    /// <summary>Adds the component to every selected entity, as one undo step.</summary>
    public void AddComponent(ComponentDescriptor descriptor) =>
        ExecuteOnEach($"Add {descriptor.DisplayName}", entity => new AddComponentEdit(entity, descriptor.CreateInstance()));

    /// <summary>The component catalog, checked against the primary selected entity.</summary>
    public ComponentCatalogViewModel CreateCatalog() => new(_workspace.Components, _selection.Entity);

    /// <summary>The primary selected entity, which the catalog checks components against.</summary>
    public Entity? Entity => _selection.Entity;

    /// <summary>
    /// Adds a component, and any components it requires that an entity is missing, to every selected entity, as one
    /// undo step.
    /// </summary>
    public void AddWithRequirements(ComponentDescriptor descriptor) =>
        ExecuteOnEach($"Add {descriptor.DisplayName}", entity => Griff.Editing.ComponentCatalog.AddWithRequirements(_workspace.Components, descriptor, entity));

    // One edit per selected entity, run as one undo step.
    private void ExecuteOnEach(string description, Func<Entity, IEdit?> edit)
    {
        var edits = _entities.Select(edit).OfType<IEdit>().ToList();
        if (edits.Count > 0)
            Execute(edits.Count == 1 ? edits[0] : new CompositeEdit($"{description} to {edits.Count} entities", edits));
    }

    /// <summary>Opens the script a component comes from in the script editor, at its class.</summary>
    public void EditScript(Type type) => _ = _scripts.OpenComponentAsync(type);

    /// <summary>Ends the current undo step, e.g. when a drag or typed edit finishes.</summary>
    public void Seal() => _document?.History.Seal();

    internal void Execute(IEdit edit) => _document?.Execute(edit);

    /// <summary>Renames an entity, and the fields in the scene that name it, as one undo step.</summary>
    internal void Rename(Entity entity, string name) => Execute(EntityReferences.Rename(entity, name, _workspace.Components));

    /// <summary>The project's prefabs' names, read as the selection changes: fields can name them as well as entities.</summary>
    public IReadOnlyCollection<string> PrefabNames { get; private set; } = [];

    public void SetValues(IReadOnlyList<Component> components, PropertyDescriptor property, IReadOnlyList<object?> values)
    {
        if (SetPropertiesEdit.For(components, property, values) is { } edit)
            Execute(edit);
    }

    /// <summary>Puts a painted picture on components: a sprite or tilemap also takes its frames or tiles.</summary>
    public void UseImage(IReadOnlyList<Component> components, PropertyDescriptor property, string image, Griff.Art.SheetLayout layout)
    {
        var edits = components.Select(c => ArtEdits.UseSheet(_workspace.Components, c, property.Name, image, layout)).OfType<IEdit>().ToList();
        if (edits.Count > 0)
            Execute(edits.Count == 1 ? edits[0] : new CompositeEdit($"Use {image}", edits));
        Seal();
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
        if (_entities.Where((entity, i) => entity.Components.Count != _componentCounts[i]).Any())
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
        // The primary entity first: its components set the order, and its values show for anything that isn't mixed.
        var entities = _selection.Entities.Reverse().ToList();
        _entities = entities;
        _componentCounts = [.. entities.Select(e => e.Components.Count)];
        Header = entities.Count == 0 ? null : new EntityHeader(entities, this);
        HiddenNote = null;
        if (entities.Count == 0)
            return;

        PrefabNames = _workspace.Project?.FindPrefabs().Select(System.IO.Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet() ?? [];

        foreach (var components in Shared(entities))
            Components.Add(new ComponentSection(components, this, _workspace.Components));
        if (entities.Any(e => e.Components.Count != Components.Count))
            HiddenNote = $"Only the components all {entities.Count} have are shown.";
    }

    // Each component of the first entity, with the same component on every other one: the nth of a type matches the
    // nth of that type. Components some of them lack are left out, and so are missing ones when there are several.
    private static IEnumerable<IReadOnlyList<Component>> Shared(IReadOnlyList<Entity> entities)
    {
        var seen = new Dictionary<Type, int>();
        foreach (var component in entities[0].Components)
        {
            var type = component.GetType();
            var nth = seen[type] = seen.GetValueOrDefault(type) + 1;
            var matches = entities.Skip(1).Select(e => e.Components.Where(c => c.GetType() == type).ElementAtOrDefault(nth - 1)).ToList();
            if (entities.Count == 1 || (component is not MissingComponent && matches.All(m => m is not null)))
                yield return [component, .. matches!];
        }
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

/// <summary>
/// The selected entity's name and enabled state; with several selected, how many there are (names stay one per
/// entity) and enabled when all of them are.
/// </summary>
public sealed class EntityHeader(IReadOnlyList<Entity> entities, InspectorPanel owner) : ObservableObject
{
    public EntityHeader(Entity entity, InspectorPanel owner)
        : this([entity], owner)
    {
    }

    public bool IsSeveral => entities.Count > 1;

    /// <summary>The entity's name, renamed by setting it; with several selected, "3 entities", which can't be set.</summary>
    public string Name
    {
        get => IsSeveral ? $"{entities.Count} entities" : entities[0].Name;
        set
        {
            var name = value.Trim();
            if (!IsSeveral && name.Length > 0 && name != entities[0].Name)
                owner.Rename(entities[0], name);
            OnPropertyChanged();
        }
    }

    /// <summary>The selected entities' names.</summary>
    public string Names => string.Join(", ", entities.Select(e => e.Name));

    /// <summary>Null (the check box's in-between state) when some are enabled and some aren't.</summary>
    public bool? IsEnabled
    {
        get => entities.All(e => e.IsEnabled) ? true : entities.Any(e => e.IsEnabled) ? null : false;
        set
        {
            if (value is not { } enabled)
                return;
            var edits = entities.Where(e => e.IsEnabled != enabled).Select(IEdit (e) => new SetEntityEnabledEdit(e, enabled)).ToList();
            if (edits.Count > 0)
                owner.Execute(edits.Count == 1 ? edits[0] : new CompositeEdit($"{(enabled ? "Enable" : "Disable")} {edits.Count} entities", edits));
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed class ComponentSection : ObservableObject
{
    private readonly InspectorPanel _owner;
    private readonly IReadOnlyList<Component> _components;
    private bool _isExpanded = true;

    public ComponentSection(Component component, InspectorPanel owner, ComponentRegistry registry)
        : this([component], owner, registry)
    {
    }

    /// <param name="components">The same component on each selected entity, the primary one's first.</param>
    public ComponentSection(IReadOnlyList<Component> components, InspectorPanel owner, ComponentRegistry registry)
    {
        _owner = owner;
        _components = components;
        var component = Component = components[0];
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
        Properties = descriptor.Properties.Select(p => new PropertyField(components, p, owner)).ToList();
    }

    /// <summary>The primary entity's component.</summary>
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

    // From every selected entity, as one undo step.
    private void Remove()
    {
        var edits = _components.Where(c => c.Entity is not null).Select(IEdit (c) => new RemoveComponentEdit(c)).ToList();
        if (edits.Count > 0)
            _owner.Execute(edits.Count == 1 ? edits[0] : new CompositeEdit($"Remove {Title} from {edits.Count} entities", edits));
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

    /// <summary>An image file: a preview and the path, with buttons to pick one from the project or paint one.</summary>
    Image,

    /// <summary>
    /// The name of an entity (or several, separated by commas): text, with a button to pick one from the scene's
    /// entities, and entities can be dropped on it from the Hierarchy.
    /// </summary>
    Entity,
}

/// <summary>
/// One editable property, of one component or of the same component on several selected entities. Exposes typed
/// accessors for each editor template; when the components' values differ, the field is mixed and shows
/// <see cref="Mixed"/> until a value is set, which sets it on all of them.
/// </summary>
public sealed class PropertyField : ObservableObject
{
    /// <summary>What a text field shows when the selected entities' values differ.</summary>
    public const string Mixed = "—";

    private static readonly string[] AxisNames = ["X", "Y", "Z", "W"];

    private readonly IReadOnlyList<Component> _components;
    private readonly PropertyDescriptor _property;
    private readonly IPropertyHost _owner;

    public PropertyField(Component component, PropertyDescriptor property, IPropertyHost owner)
        : this([component], property, owner)
    {
    }

    /// <param name="components">Components of one type, one per selected entity; the first one's value shows when they agree.</param>
    public PropertyField(IReadOnlyList<Component> components, PropertyDescriptor property, IPropertyHost owner)
    {
        _components = components;
        _property = property;
        _owner = owner;
        Label = InspectorPanel.Humanize(property.Name);

        var type = property.Type;
        Kind = type == typeof(bool) ? FieldKind.Bool
            : type == typeof(int) || type == typeof(float) || type == typeof(double) ? FieldKind.Number
            : type == typeof(string) && IsSoundName(property.Name) ? FieldKind.Sound
            : type == typeof(string) && IsImageName(property.Name) ? FieldKind.Image
            : property.EntityName is not null ? FieldKind.Entity
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

    /// <summary>True when the selected entities' values differ (never for a single entity).</summary>
    public bool IsMixed => _components.Skip(1).Any(c => !SameValue(_property.GetValue(c), Value));

    private static bool SameValue(object? a, object? b) =>
        a is IReadOnlyList<string> x && b is IReadOnlyList<string> y ? x.SequenceEqual(y) : Equals(a, b);

    // Sets every component's value at once, as one undo step.
    private void SetAll(object? value) => _owner.SetValues(_components, _property, [.. _components.Select(_ => value)]);

    /// <summary>Properties named like JumpSound, Sound or Music hold a sound file's asset path.</summary>
    public static bool IsSoundName(string name) => name.EndsWith("Sound", StringComparison.Ordinal) || name == "Music";

    /// <summary>Properties named Image or like FireImage hold an image file's asset path.</summary>
    public static bool IsImageName(string name) => name.EndsWith("Image", StringComparison.Ordinal);

    /// <summary>For an image field: a small preview of the picture, or null.</summary>
    public System.Windows.Media.ImageSource? ImagePreview => Kind == FieldKind.Image && !IsMixed ? Art.EditorArt.Current?.Thumbnail(Value as string) : null;

    /// <summary>For an image field: set to a file that isn't in the project.</summary>
    public bool IsImageMissing => Kind == FieldKind.Image && !IsMixed && Value is string { Length: > 0 } path &&
        Art.EditorArt.Current is { AssetsPath: not null } art && art.Resolve(path) is null;

    public string ImageTip => Value is string { Length: > 0 } && !IsImageMissing ? "Paint over this in the Sprite Painter" : "Paint a picture for this in the Sprite Painter";

    /// <summary>
    /// Opens the Sprite Painter on the field's picture (or a new one shaped for the component); each picture it saves
    /// is put on the component.
    /// </summary>
    public void PaintImage() => PaintImage(fresh: false);

    private void PaintImage(bool fresh)
    {
        if (Art.EditorArt.Current is not { } art)
            return;
        var host = _owner;
        var (components, property) = (_components, _property);
        var current = fresh || IsMixed || art.Resolve(Value as string) is null ? null : (string)Value!;
        var painter = art.OpenPainter(current, Art.EditorArt.LayoutFor(components[0]), (image, used) => host.UseImage(components, property, image, used));
        if (current is null && components[0].Entity is { } entity)
            painter.Model.Name = painter.Model.SuggestName(Art.EditorArt.Slug(entity.Name));
    }

    public IRelayCommand PaintImageCommand => _paintImage ??= new RelayCommand(PaintImage);

    /// <summary>Shows the project's images in a menu under the button that asked; picking one sets it.</summary>
    public IRelayCommand<FrameworkElement> ChooseImageCommand => _chooseImage ??= new RelayCommand<FrameworkElement>(ChooseImage);

    private IRelayCommand? _paintImage;
    private IRelayCommand<FrameworkElement>? _chooseImage;

    private void ChooseImage(FrameworkElement? anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var current = IsMixed ? null : Value as string;
        var art = Art.EditorArt.Current;
        var images = art?.Images() ?? [];
        foreach (var image in images)
        {
            var item = new MenuItem
            {
                Header = image,
                IsCheckable = true,
                IsChecked = string.Equals(image, current, StringComparison.OrdinalIgnoreCase),
                Icon = new System.Windows.Controls.Image { Source = art?.Thumbnail(image), Width = 16, Height = 16, Stretch = Stretch.Uniform },
            };
            RenderOptions.SetBitmapScalingMode(item.Icon as DependencyObject, BitmapScalingMode.NearestNeighbor);
            item.Click += (_, _) => UseChosenImage(image);
            menu.Items.Add(item);
        }

        if (images.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No images in the project yet", IsEnabled = false });
        menu.Items.Add(new Separator());
        var paint = new MenuItem { Header = "Paint a New One...", Icon = Icons.Create(Icons.Palette) };
        paint.Click += (_, _) => PaintImage(fresh: true);
        menu.Items.Add(paint);
        if (!string.IsNullOrEmpty(current) || IsMixed)
        {
            var clear = new MenuItem { Header = "None", Icon = Icons.Create(Icons.Clear) };
            clear.Click += (_, _) => TextValue = null;
            menu.Items.Add(clear);
        }

        menu.IsOpen = true;
    }

    // A picture painted in Griff brings its frames or tiles along; any other is just set.
    private void UseChosenImage(string image)
    {
        if (Art.EditorArt.Current?.LayoutOf(image) is { } layout)
            _owner.UseImage(_components, _property, image, layout);
        else
            TextValue = image;
    }

    /// <summary>For a sound field: whether it's music (made in the Sound Maker's Music mode) rather than an effect.</summary>
    public bool IsMusic => _property.Name == "Music";

    /// <summary>For a sound field: set to a file that isn't in the project.</summary>
    public bool IsSoundMissing => Kind == FieldKind.Sound && !IsMixed && Value is string { Length: > 0 } path &&
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
        var current = IsMixed ? null : Value as string;
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
        var current = IsMixed ? null : Value as string;
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
        if (!string.IsNullOrEmpty(current) || IsMixed)
        {
            var clear = new MenuItem { Header = "None", Icon = Icons.Create(Icons.Clear) };
            clear.Click += (_, _) => TextValue = null;
            menu.Items.Add(clear);
        }

        menu.IsOpen = true;
    }

    /// <summary>For an entity field: whether it names several, separated by commas, so picking one adds it (or takes it out).</summary>
    public bool NamesMany => _property.EntityName?.Many == true;

    /// <summary>
    /// For an entity field: the entities it can name, in Hierarchy order, each with how deep it is. One that names an
    /// entity inside its own (an attack's hitbox) lists what's inside the primary selected entity.
    /// </summary>
    public IReadOnlyList<EntityChoice> EntityChoices()
    {
        if (_property.EntityName is not { } entityName || _components[0].Entity is not { } owner)
            return [];
        var choices = new List<EntityChoice>();
        void Add(Entity entity, int depth)
        {
            choices.Add(new EntityChoice(entity, depth, Names(entity.Name)));
            foreach (var child in entity.Children)
                Add(child, depth + 1);
        }

        foreach (var root in entityName.Inside ? owner.Children : owner.Scene?.Roots ?? [])
            Add(root, 0);
        return choices;
    }

    /// <summary>
    /// For an entity field: the names it holds that match nothing, no entity it can name and no prefab (shown in red).
    /// Empty when they all match, or with several selected whose values differ.
    /// </summary>
    public IReadOnlyList<string> UnmatchedNames => _unmatched ??=
        Kind == FieldKind.Entity && !IsMixed ? EntityReferences.Unmatched(new(_components[0], _property), _owner.PrefabNames) : [];

    private IReadOnlyList<string>? _unmatched;

    public bool IsUnmatched => UnmatchedNames.Count > 0;

    /// <summary>Says which names match nothing, e.g. "Nothing in the scene is called "Playr"".</summary>
    public string? UnmatchedTip => IsUnmatched
        ? $"Nothing {(_property.EntityName!.Inside ? $"inside {_components[0].Entity?.Name}" : "in the scene")} is called " +
          string.Join(" or ", UnmatchedNames.Select(n => $"\"{n}\""))
        : null;

    /// <summary>True when the field names <paramref name="name"/>: on every selected entity, when there are several.</summary>
    public bool Names(string name) => _components.All(c => SplitNames(_property.GetValue(c)).Contains(name));

    /// <summary>
    /// Picks an entity by name: names it, or on a field that names several, adds it (to those that don't have it yet)
    /// or takes it out again (when they all have it).
    /// </summary>
    public void PickEntity(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return;
        if (!NamesMany)
            Value = name;
        else if (Names(name))
            SetEach(names => names.Remove(name));
        else
            SetEach(names => names.Add(name));
        _owner.Seal();
    }

    /// <summary>
    /// An entity dropped on the field from the Hierarchy: names it, or on a field that names several, adds it to those
    /// that don't have it yet.
    /// </summary>
    public void DropEntity(Entity entity)
    {
        if (NamesMany)
            SetEach(names => names.Add(entity.Name));
        else
            Value = entity.Name;
        _owner.Seal();
    }

    // Changes each component's own list of names, so several selected switches keep their other targets.
    private void SetEach(Action<List<string>> change) => _owner.SetValues(_components, _property, [.. _components.Select(c =>
    {
        var names = SplitNames(_property.GetValue(c));
        var had = names.ToList();
        change(names);
        names = [.. names.Distinct()];
        return names.SequenceEqual(had) ? _property.GetValue(c) : names.Count == 0 ? null : (object)string.Join(", ", names);
    })]);

    private static List<string> SplitNames(object? value) =>
        [.. (value as string ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Shows the entities the field can name in a menu under the button that asked; picking one names it.</summary>
    public IRelayCommand<FrameworkElement> ChooseEntityCommand => _chooseEntity ??= new RelayCommand<FrameworkElement>(ChooseEntity);

    private IRelayCommand<FrameworkElement>? _chooseEntity;

    private void ChooseEntity(FrameworkElement? anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var choices = EntityChoices();
        foreach (var choice in choices)
        {
            var item = new MenuItem
            {
                // Indented like the Hierarchy, so children show under their parents.
                Header = new TextBlock { Text = choice.Entity.Name, Margin = new Thickness(choice.Depth * 12, 0, 0, 0) },
                Tag = choice.Entity.Name,
                IsCheckable = true,
                IsChecked = choice.IsNamed,
                // Several can be ticked in turn without opening it again.
                StaysOpenOnClick = NamesMany,
            };
            var name = choice.Entity.Name;
            item.Click += (_, _) =>
            {
                PickEntity(name);
                foreach (var other in menu.Items.OfType<MenuItem>().Where(i => Equals(i.Tag, name)))
                    other.IsChecked = Names(name);
            };
            menu.Items.Add(item);
        }

        if (choices.Count == 0)
            menu.Items.Add(new MenuItem { Header = _property.EntityName?.Inside == true ? "Nothing inside this entity yet" : "No entities in the scene", IsEnabled = false });
        if (!string.IsNullOrEmpty(Value as string) || IsMixed)
        {
            menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "None", Icon = Icons.Create(Icons.Clear) };
            clear.Click += (_, _) => TextValue = null;
            menu.Items.Add(clear);
        }

        menu.IsOpen = true;
    }

    public bool IsInteger => _property.Type == typeof(int);

    public Array? EnumValues { get; }

    public IReadOnlyList<AxisField> Axes { get; } = [];

    /// <summary>The value (the first component's, when several are edited); setting it sets it on all of them.</summary>
    public object? Value
    {
        get => _property.GetValue(_components[0]);
        set => SetAll(value);
    }

    /// <summary>Null (the check box's in-between state) when mixed.</summary>
    public bool? BoolValue
    {
        get => IsMixed ? null : Value is true;
        set
        {
            if (value is null)
                return;
            Value = value.Value;
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

    /// <summary><see cref="Mixed"/> when mixed; leaving it at that changes nothing.</summary>
    public string? TextValue
    {
        get => IsMixed ? Mixed : Value as string;
        set
        {
            if (IsMixed && value == Mixed)
                return;
            Value = string.IsNullOrEmpty(value) ? null : value;
            _owner.Seal();
        }
    }

    /// <summary>A list of strings as text, one per line.</summary>
    public string LinesValue
    {
        get => IsMixed ? Mixed : Value is IReadOnlyList<string> lines ? string.Join(Environment.NewLine, lines) : "";
        set
        {
            if (IsMixed && value == Mixed)
                return;
            var lines = value.ReplaceLineEndings("\n").Split('\n');
            if (IsMixed || Value is not IReadOnlyList<string> current || !current.SequenceEqual(lines))
                Value = lines;
            _owner.Seal();
        }
    }

    /// <summary>Null (nothing picked) when mixed.</summary>
    public object? EnumValue
    {
        get => IsMixed ? null : Value;
        set
        {
            if (value is null)
                return;
            Value = value;
            _owner.Seal();
        }
    }

    public string ColorHex
    {
        get => IsMixed ? Mixed : Value is Color color ? color.ToHex() : "";
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
            if (IsMixed)
                return Brushes.Transparent;
            var color = Value is Color c ? c : Color.Black;
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.AByte, color.RByte, color.GByte, color.BByte));
            brush.Freeze();
            return brush;
        }
    }

    public void Refresh()
    {
        _unmatched = null;
        OnPropertyChanged(string.Empty);
        foreach (var axis in Axes)
            axis.Refresh();
    }

    internal float GetAxis(int index) => ToFloats(Value)[index];

    internal bool IsAxisMixed(int index) => _components.Skip(1).Any(c => ToFloats(_property.GetValue(c))[index] != GetAxis(index));

    // Each component keeps its other axes: setting X on several positions lines them up without moving them in Y.
    internal void SetAxis(int index, float value) => _owner.SetValues(_components, _property, [.. _components.Select(c =>
    {
        var values = ToFloats(_property.GetValue(c));
        values[index] = value;
        return FromFloats(_property.Type, values);
    })]);

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

/// <summary>An entity an entity field can name, how deep it is in the Hierarchy, and whether the field names it now.</summary>
public sealed record EntityChoice(Entity Entity, int Depth, bool IsNamed)
{
    /// <summary>Where it is ("Gate/Hint"), and whether it's named now: what agents see of it in a list.</summary>
    public string Label
    {
        get
        {
            var path = Entity.Name;
            for (var parent = Entity.Parent; parent is not null; parent = parent.Parent)
                path = $"{parent.Name}/{path}";
            return IsNamed ? $"{path} (named)" : path;
        }
    }
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

    /// <summary>True when the selected entities differ on this axis.</summary>
    public bool IsMixed => owner.IsAxisMixed(index);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsMixed));
    }
}

/// <summary>What a <see cref="PropertyField"/> edits through: it applies changes as undoable edits and ends undo steps.</summary>
public interface IPropertyHost
{
    /// <summary>Sets the property of each component to its value, as one undo step.</summary>
    void SetValues(IReadOnlyList<Component> components, PropertyDescriptor property, IReadOnlyList<object?> values);

    /// <summary>Sets an image field to a picture painted in the Sprite Painter, laid out as <paramref name="layout"/>.</summary>
    void UseImage(IReadOnlyList<Component> components, PropertyDescriptor property, string image, Griff.Art.SheetLayout layout)
    {
        SetValues(components, property, [.. components.Select(_ => (object?)image)]);
        Seal();
    }

    /// <summary>Ends the current undo step, e.g. when a drag or typed edit finishes.</summary>
    void Seal();

    /// <summary>The project's prefabs' names: an entity field naming one isn't flagged as matching nothing.</summary>
    IReadOnlyCollection<string> PrefabNames => [];
}
