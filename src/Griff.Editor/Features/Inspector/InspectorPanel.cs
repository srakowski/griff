using System.Collections.ObjectModel;
using System.Globalization;
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
/// entities selected it edits the components they all have: a change sets it on every one of them as
/// one undo step, and values that differ show as mixed. Components only some of them have show greyed, "on 2 of 3",
/// with Add to All. Right-clicking a component's header resets it, copies and pastes its values, or moves it up or
/// down. On placed copies of prefabs, fields set differently from the
/// prefab are marked, each with Revert and Apply to Prefab. Each field's name explains it (its documentation as a
/// tooltip), and a section folded shut stays shut for that component on every entity, and next time. A filter at the
/// top narrows the fields to those whose names have what's typed; Transforms show in 2D (X and Y, one rotation angle)
/// unless 3D is switched on; and color fields open a picker from their swatch.
/// </summary>
public sealed partial class InspectorPanel : ToolPanel, IPropertyHost
{
    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly Scripts.ScriptEditors _scripts;
    private readonly Core.Dialogs.IDialogService _dialogs;
    private readonly Core.Status.IStatusService _status;
    private readonly Art.EditorArt _art;
    private readonly Audio.EditorAudio _audio;
    private Core.Settings.ISettingsStore _settings;
    // For the selection: each component of a prefab copy (or of something inside one) and the prefab's own, the
    // components a copy added, and the prefab files read for them, with when each was written.
    private readonly Dictionary<Component, Component> _prefabParts = [];
    private readonly HashSet<Component> _addedToCopies = [];
    private readonly Dictionary<string, (DateTime Written, Entity? Prefab)> _prefabs = new(StringComparer.OrdinalIgnoreCase);
    private SceneDocument? _document;
    private EntityHeader? _header;
    private string? _hiddenNote;
    private IReadOnlyList<Entity> _entities = [];
    private int[] _componentCounts = [];

    public InspectorPanel(IWorkspace workspace, ISelection selection, Scripts.ScriptEditors scripts, Art.EditorArt art,
        Audio.EditorAudio audio, Core.Dialogs.IDialogService dialogs, Core.Status.IStatusService status, Core.Settings.ISettingsStore settings,
        IServiceProvider? services = null)
    {
        _services = services;
        _settings = settings;
        _art = art;
        _audio = audio;
        _workspace = workspace;
        _selection = selection;
        _scripts = scripts;
        _dialogs = dialogs;
        _status = status;
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

    /// <summary>With several entities selected, says that components whose type couldn't be found are left out; otherwise null.</summary>
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

    /// <summary>
    /// Adds a component, and any components it requires that an entity is missing (a Patrol brings a Body and a
    /// BoxCollider), to every selected entity, as one undo step.
    /// </summary>
    public void AddComponent(ComponentDescriptor descriptor) =>
        ExecuteOnEach($"Add {descriptor.DisplayName}", entity => Griff.Editing.ComponentCatalog.AddWithRequirements(_workspace.Components, descriptor, entity));

    /// <summary>
    /// Adds the component named <paramref name="component"/> ("Patrol" or "Platformer Movement"), with what it needs, to
    /// every selected entity, as Add Component does.
    /// </summary>
    public void Add(string component)
    {
        var name = component.Replace(" ", "");
        AddComponent(AvailableComponents.FirstOrDefault(d => d.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) || d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"There's no component called {component}."));
    }

    /// <summary>The component catalog, checked against the primary selected entity.</summary>
    public ComponentCatalogViewModel CreateCatalog() => new(_workspace.Components, _selection.Entity);

    /// <summary>The primary selected entity, which the catalog checks components against.</summary>
    public Entity? Entity => _selection.Entity;

    private ComponentCatalogViewModel? _picker;

    /// <summary>
    /// The Add Component list while it's open (null when it's shut): every component by category, filtered by what's
    /// typed in its Search, with the Selected one's PlanText saying what adding it brings along. Open it with
    /// <see cref="IsAddOpen"/> and add the selected one with <see cref="AddPicked"/>.
    /// </summary>
    public ComponentCatalogViewModel? Picker
    {
        get => _picker;
        private set => SetProperty(ref _picker, value);
    }

    /// <summary>Whether the Add Component list is open. Opening it starts it afresh, with nothing typed.</summary>
    public bool IsAddOpen
    {
        get => Picker is not null;
        set
        {
            if (value == IsAddOpen)
                return;
            Picker = value && HasEntity ? CreateCatalog() : null;
            OnPropertyChanged();
        }
    }

    /// <summary>Adds the component picked in the Add Component list (with what it needs) and closes the list.</summary>
    public void AddPicked()
    {
        if (Picker?.Selected is not { } picked)
            return;
        IsAddOpen = false;
        AddComponent(picked.Entry.Descriptor);
    }

    /// <summary>
    /// Adds what a component needs (<paramref name="requirement"/>, e.g. its Body) to each of the selected entities
    /// that doesn't have one, as one undo step.
    /// </summary>
    internal void AddRequirement(IReadOnlyList<Component> components, ComponentLink requirement)
    {
        if (Griff.Editing.ComponentCatalog.Satisfier(_workspace.Components, requirement.Type) is not { } satisfier)
            return;
        var edits = components.Select(c => c.Entity).OfType<Entity>().Where(e => !requirement.IsMetBy(e))
            .Select(e => Griff.Editing.ComponentCatalog.AddWithRequirements(_workspace.Components, satisfier, e)).ToList();
        if (edits.Count > 0)
            Execute(edits.Count == 1 ? edits[0] : new CompositeEdit($"Add {satisfier.DisplayName} to {edits.Count} entities", edits));
    }

    /// <summary>
    /// Removes the components from their entities, as one undo step. When others there need them (a PlatformerMovement
    /// its Body), asks first, saying which.
    /// </summary>
    internal void RemoveComponents(IReadOnlyList<Component> components, string title)
    {
        var placed = components.Where(c => c.Entity is not null).ToList();
        if (placed.Count == 0)
            return;
        if (DependentsNote(placed) is { } note && !_dialogs.Confirm($"Remove {title}?", $"{note} Remove it anyway?"))
            return;
        var edits = placed.Select(IEdit (c) => new RemoveComponentEdit(c)).ToList();
        Execute(edits.Count == 1 ? edits[0] : new CompositeEdit($"Remove {title} from {edits.Count} entities", edits));
    }

    /// <summary>
    /// "Platformer Movement needs it on Player, and won't work without it." for components that others on their
    /// entities need, or null when nothing does.
    /// </summary>
    internal string? DependentsNote(IReadOnlyList<Component> components)
    {
        var dependents = components
            .SelectMany(c => Griff.Editing.ComponentCatalog.Dependents(_workspace.Components, c).Select(d => (Entity: c.Entity!, Dependent: d)))
            .ToList();
        if (dependents.Count == 0)
            return null;
        var names = dependents.Select(d => Humanize(_workspace.Components.Get(d.Dependent.GetType()).DisplayName)).Distinct().ToList();
        var entities = dependents.Select(d => d.Entity).Distinct().ToList();
        var where = entities.Count == 1 ? entities[0].Name : $"{entities.Count} entities";
        return $"{ComponentCatalogViewModel.Join(names)} {(names.Count == 1 ? "needs" : "need")} it on {where}, and won't work without it.";
    }

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

    Art.EditorArt IPropertyHost.Art => _art;

    Audio.EditorAudio IPropertyHost.Audio => _audio;

    internal void Execute(IEdit edit) => _document?.Execute(edit);

    private readonly IServiceProvider? _services;

    /// <summary>Runs one of the editor's commands by its id, as a field's editor does ("Art.Clips" opens the Clip Editor).</summary>
    public void RunCommand(string id)
    {
        if (_services?.GetService(typeof(Core.Commands.CommandRegistry)) is Core.Commands.CommandRegistry commands)
            _ = commands.Get(id).ExecuteAsync();
    }

    /// <summary>Renames an entity, and the fields in the scene that name it, as one undo step.</summary>
    internal void Rename(Entity entity, string name) => Execute(EntityReferences.Rename(entity, name, _workspace.Components));

    private KnownNames? _names;
    private bool _namesRead;

    /// <summary>
    /// The project's prefabs, scenes, input actions, layers and messages, read when a field first asks after the
    /// selection or the scene changes.
    /// </summary>
    public KnownNames? Names
    {
        get
        {
            if (!_namesRead)
            {
                _namesRead = true;
                _names = _workspace.Names;
            }

            return _names;
        }
    }

    /// <summary>The project's prefabs' names, read as the selection changes: fields can name them as well as entities.</summary>
    public IReadOnlyCollection<string> PrefabNames { get; private set; } = [];

    private readonly FontPreviews _fontPreviews = new();

    /// <summary>The font picker's preview of a font: its name drawn in it, as the game draws it.</summary>
    public ImageSource? FontPreview(string? font, string text) => _fontPreviews.Preview(_workspace.Project?.AssetsPath, font, text);

    /// <summary>
    /// True while a prefab's tab is showing: fields naming things outside the entity they're on aren't flagged, as they
    /// name things in whatever scene the prefab is placed in.
    /// </summary>
    public bool InPrefab => _workspace.Scene?.IsPrefab == true;

    /// <summary>Whether the prefab file at <paramref name="assetPath"/> (in Assets) is there.</summary>
    internal bool PrefabExists(string assetPath) =>
        _workspace.Project is { } project && System.IO.File.Exists(System.IO.Path.Combine(project.AssetsPath, assetPath));

    /// <summary>The prefab's own component that <paramref name="component"/>, on a placed copy or inside one, was made from.</summary>
    public Component? PrefabComponent(Component component) => _prefabParts.GetValueOrDefault(component);

    /// <summary>True for a component on a placed copy (or inside one) that its prefab doesn't have.</summary>
    public bool IsAddedToCopy(Component component) => _addedToCopies.Contains(component);

    /// <summary>A placed copy's prefab, as an entity made from its file (read again when the file changes), or null.</summary>
    internal Entity? PrefabOf(Entity copy)
    {
        if (copy.Prefab is not { } link || _workspace.Project?.FindPrefab(link) is not { } path)
            return null;
        var written = System.IO.File.GetLastWriteTimeUtc(path);
        if (_prefabs.TryGetValue(path, out var known) && known.Written == written)
            return known.Prefab;

        Entity? prefab;
        try
        {
            prefab = _workspace.Serializer.LoadPrefabFromString(System.IO.File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
        {
            prefab = null;
        }

        _prefabs[path] = (written, prefab);
        return prefab;
    }

    /// <summary>What a placed copy changes from its prefab (nothing if it isn't one, or its prefab can't be read).</summary>
    internal IReadOnlyList<PrefabOverride> OverridesOf(Entity copy) =>
        PrefabOf(copy) is { } prefab ? PrefabOverrides.Find(copy, prefab, _workspace.Components) : [];

    /// <summary>
    /// Sets the field in each selected copy's prefab to the copy's value, and brings the prefab's other copies up to
    /// date (keeping what they changed themselves).
    /// </summary>
    public void ApplyToPrefab(IReadOnlyList<Component> components, PropertyDescriptor property)
    {
        var update = PrefabFiles.ApplyField(_workspace, components, property);
        if (update.Problems.Count > 0)
            _dialogs.ShowError("Couldn't apply it to the prefab", string.Join("\n", update.Problems));
        else
            _status.Show($"Applied {Humanize(property.Name)} to the prefab. {update.Summary}".Trim());
        Rebuild();
    }

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
            _document.RuntimeRefreshed -= OnRuntimeRefreshed;
        }

        _document = document;
        if (_document is not null)
        {
            _document.Changed += OnDocumentChanged;
            _document.RuntimeRefreshed += OnRuntimeRefreshed;
        }

        Rebuild();
    }

    private void OnDocumentChanged(object? sender, EditEventArgs e)
    {
        _namesRead = false;
        if (ChangesComponents(e.Edit) || PrefabsChanged() || Components.Any(c => c.IsOutOfDate))
            Rebuild();
        else
            Refresh();
    }

    // Show live values while the game runs. Components added or removed by gameplay rebuild the sections.
    private void OnRuntimeRefreshed(object? sender, EventArgs e)
    {
        if (_entities.Where((entity, i) => entity.Components.Count != _componentCounts[i]).Any() || PrefabsChanged())
            Rebuild();
        else
            Refresh();
    }

    private static bool ChangesComponents(IEdit edit) => edit switch
    {
        AddComponentEdit or RemoveComponentEdit or MoveComponentEdit or UpdatePrefabCopyEdit => true,
        CompositeEdit composite => composite.Edits.Any(ChangesComponents),
        _ => false,
    };

    // A prefab file the selection's copies come from was saved since it was read.
    private bool PrefabsChanged() =>
        _prefabs.Any(p => System.IO.File.Exists(p.Key) ? System.IO.File.GetLastWriteTimeUtc(p.Key) != p.Value.Written : p.Value.Prefab is not null);

    private void Rebuild()
    {
        _namesRead = false;
        Components.Clear();
        _prefabParts.Clear();
        _addedToCopies.Clear();
        _prefabs.Clear();
        _overrideCount = null;
        // The primary entity first: its components set the order, and its values show for anything that isn't mixed.
        var entities = _selection.Entities.Reverse().ToList();
        _entities = entities;
        _componentCounts = [.. entities.Select(e => e.Components.Count)];
        Header = entities.Count == 0 ? null : new EntityHeader(entities, this);
        HiddenNote = null;
        if (Picker is not null)
        {
            if (entities.Count == 0)
                IsAddOpen = false;
            else
                Picker.Entity = _selection.Entity;
        }

        if (entities.Count == 0)
            return;

        PrefabNames = _workspace.Project?.FindPrefabs().Select(System.IO.Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet() ?? [];
        foreach (var entity in entities)
            MatchPrefab(entity);

        foreach (var (components, lacking) in Sections(entities))
            Components.Add(new ComponentSection(components, this, _workspace.Components, lacking));
        if (entities.Count > 1 && entities.Any(e => e.Components.Any(c => c is MissingComponent)))
            HiddenNote = "Components whose type couldn't be found are left out.";
        ApplyFilter();
    }

    // ----- Filtering fields

    private string _filter = "";

    /// <summary>
    /// What's typed in the filter at the top: only fields whose names have it show (all of a component's, or a
    /// subheading's, when its name has it), folded or not, and components with none are left out. It stays as the
    /// selection changes; empty shows everything.
    /// </summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? ""))
                ApplyFilter();
        }
    }

    /// <summary>True while the filter has something typed in it.</summary>
    public bool IsFiltering => Filter.Trim().Length > 0;

    /// <summary>How many fields the filter lets through, across the components shown.</summary>
    public int FilterMatches => Components.Sum(c => c.Properties.Count(p => !p.IsFilteredOut));

    /// <summary>"No fields match "spd"" when the filter hides everything; otherwise null.</summary>
    public string? FilterNote => IsFiltering && Components.All(c => c.IsFilteredOut) ? $"No fields match \"{Filter.Trim()}\"" : null;

    public bool HasFilterNote => FilterNote is not null;

    private void ApplyFilter()
    {
        var text = Filter.Trim();
        foreach (var section in Components)
            section.ApplyFilter(text);
        OnPropertyChanged(nameof(IsFiltering));
        OnPropertyChanged(nameof(FilterMatches));
        OnPropertyChanged(nameof(FilterNote));
        OnPropertyChanged(nameof(HasFilterNote));
    }

    // ----- Transforms in 2D or 3D

    /// <summary>
    /// Whether Transforms show in 3D: Position, Rotation and Scale in X, Y and Z. Off (the start), they show in 2D: X and
    /// Y, and one rotation angle. Kept in the user's settings.
    /// </summary>
    public bool Transform3D
    {
        get => _settings.Settings.Transform3D;
        set
        {
            if (value == Transform3D)
                return;
            _settings.Settings.Transform3D = value;
            _settings.Save();
            OnPropertyChanged();
            foreach (var section in Components.Where(c => c.IsTransform))
                section.Refresh();
        }
    }

    // ----- Colors

    /// <summary>The palette color fields' pickers offer: the Sprite Painter's, which choosing another in a picker changes too.</summary>
    public Griff.Art.Palette Palette
    {
        get => _art.Palette;
        set => _art.Palette = value;
    }

    /// <summary>Colors picked lately in color fields' pickers, most recent first, kept in the user's settings.</summary>
    public IReadOnlyList<Color> RecentColors =>
        [.. _settings.Settings.RecentColors.Select(h => Color.TryParse(h, out var c) ? c : (Color?)null).OfType<Color>()];

    /// <summary>Puts a color first in <see cref="RecentColors"/>.</summary>
    public void RememberColor(Color color)
    {
        var list = _settings.Settings.RecentColors;
        var hex = color.ToHex();
        list.RemoveAll(h => string.Equals(h, hex, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, hex);
        if (list.Count > ColorPicker.RecentCount)
            list.RemoveRange(ColorPicker.RecentCount, list.Count - ColorPicker.RecentCount);
        _settings.Save();
    }

    // Pairs each of the entity's components with its prefab's, if it's a copy or inside one.
    private void MatchPrefab(Entity entity)
    {
        if (PrefabOverrides.CopyFor(entity) is not { } copy || PrefabOf(copy) is not { } prefab ||
            PrefabOverrides.Counterpart(prefab, copy, entity) is not { } part)
            return;
        foreach (var component in entity.Components)
        {
            if (PrefabOverrides.Counterpart(part, component) is { } prefabComponent)
                _prefabParts[component] = prefabComponent;
            else
                _addedToCopies.Add(component);
        }
    }

    // Each component of the first entity, with the same component on every other one (the nth of a type matches the nth
    // of that type), then those only others have, in the order they come up. A component some of them lack comes with
    // the entities lacking it. Missing ones are left out when there are several.
    private static IEnumerable<(IReadOnlyList<Component> Components, IReadOnlyList<Entity> Lacking)> Sections(List<Entity> entities)
    {
        if (entities.Count == 1)
        {
            foreach (var component in entities[0].Components)
                yield return ([component], []);
            yield break;
        }

        var keys = new List<(Type Type, int Nth)>();
        foreach (var entity in entities)
        {
            var seen = new Dictionary<Type, int>();
            foreach (var component in entity.Components.Where(c => c is not MissingComponent))
            {
                var type = component.GetType();
                var key = (type, seen[type] = seen.GetValueOrDefault(type) + 1);
                if (!keys.Contains(key))
                    keys.Add(key);
            }
        }

        foreach (var (type, nth) in keys)
        {
            var matches = entities.Select(e => e.Components.Where(c => c.GetType() == type).ElementAtOrDefault(nth - 1)).ToList();
            yield return ([.. matches.OfType<Component>()], [.. entities.Where((_, i) => matches[i] is null)]);
        }
    }

    private ComponentValues? _copied;

    /// <summary>The values Copy Values took from a component, which Paste Values sets on another of its type; null before.</summary>
    public ComponentValues? Copied
    {
        get => _copied;
        private set
        {
            if (SetProperty(ref _copied, value))
                OnPropertyChanged(nameof(CopiedType));
        }
    }

    /// <summary>The type of component whose values were copied, "Health", or null.</summary>
    public string? CopiedType => Copied?.TypeName;

    /// <summary>Copies the component's values for Paste Values.</summary>
    internal void Copy(Component component)
    {
        if (ComponentEdits.Copy(_workspace.Components, component) is not { } values)
            return;
        Copied = values;
        _status.Show($"Copied {Humanize(_workspace.Components.Get(values.Type).DisplayName)}'s values.");
    }

    /// <summary>Sets the components to the copied values (they must be of the copied type), as one undo step.</summary>
    internal void Paste(IReadOnlyList<Component> components)
    {
        if (Copied is { } values && ComponentEdits.Paste(_workspace.Components, values, components) is { } edit)
            Execute(edit);
        Seal();
    }

    /// <summary>Sets the components back to what a new one starts with, as one undo step.</summary>
    internal void Reset(IReadOnlyList<Component> components)
    {
        if (ComponentEdits.Reset(_workspace.Components, components) is { } edit)
            Execute(edit);
        Seal();
    }

    /// <summary>Moves each component along its entity's list (-1 is up), as one undo step.</summary>
    internal void Move(IReadOnlyList<Component> components, int offset)
    {
        if (ComponentEdits.Move(components, offset) is { } edit)
            Execute(edit);
    }

    /// <summary>Adds a copy of <paramref name="source"/>, its values too, to each entity in <paramref name="lacking"/>, as one undo step.</summary>
    internal void AddCopies(Component source, IReadOnlyList<Entity> lacking)
    {
        if (ComponentEdits.AddCopies(_workspace.Components, source, lacking) is { } edit)
            Execute(edit);
    }

    private void Refresh()
    {
        _overrideCount = null;
        Header?.Refresh();
        foreach (var section in Components)
            section.Refresh();
    }

    private int? _overrideCount;

    /// <summary>How many things the one selected copy changes from its prefab.</summary>
    internal int OverrideCount(Entity copy) => _overrideCount ??= OverridesOf(copy).Count;

    /// <summary>
    /// True when the component's section is folded shut (on every entity), by its type's saved name; or one of its
    /// subheadings, by "Type/Group".
    /// </summary>
    internal bool IsCollapsed(string type) => _settings.Settings.CollapsedComponents.Contains(type);

    /// <summary>Folds the component's sections shut or opens them, on every entity, kept in the user's settings.</summary>
    internal void SetCollapsed(string type, bool collapsed)
    {
        var list = _settings.Settings.CollapsedComponents;
        if (list.Contains(type) == collapsed)
            return;
        if (collapsed)
            list.Add(type);
        else
            list.Remove(type);
        _settings.Save();
        foreach (var section in Components.Where(s => s.TypeKey == type.Split('/')[0]))
            section.Refresh();
    }

    /// <summary>
    /// Opens every section and keeps which are folded in memory from now on: demos and snapshots start the same way every
    /// time, leaving the user's own alone.
    /// </summary>
    internal void StartOver()
    {
        _settings = new Core.Settings.MemorySettingsStore();
        Rebuild();
    }

    /// <summary>"PlatformerMovement" reads "Platformer Movement".</summary>
    public static string Humanize(string name) => PascalCaseBoundary().Replace(name, " ");

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

    /// <summary>
    /// "Copy of the Coin prefab, with 2 changes" when the selection is placed copies of one prefab (saving it updates
    /// them, keeping their changes), "Inside a copy of the Coin prefab" for something in one, null otherwise.
    /// </summary>
    public string? PrefabNote
    {
        get
        {
            if (entities[0].Prefab is not { } prefab)
            {
                return !IsSeveral && PrefabOverrides.CopyFor(entities[0]) is { Prefab: { } outer }
                    ? $"Inside a copy of the {PrefabCopies.NameOf(outer)} prefab"
                    : null;
            }

            if (entities.Any(e => !string.Equals(e.Prefab, prefab, StringComparison.OrdinalIgnoreCase)))
                return null;
            var name = PrefabCopies.NameOf(prefab);
            if (!owner.PrefabExists(prefab))
                return $"{(IsSeveral ? "Copies" : "Copy")} of {prefab}, which isn't in the project";
            if (IsSeveral)
                return $"Copies of the {name} prefab";
            var changes = owner.OverrideCount(entities[0]);
            return $"Copy of the {name} prefab{changes switch { 0 => "", 1 => ", with 1 change", _ => $", with {changes} changes" }}";
        }
    }

    public bool HasPrefabNote => PrefabNote is not null;

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

    public ComponentSection(Component component, InspectorPanel owner, ComponentRegistry registry)
        : this([component], owner, registry)
    {
    }

    /// <param name="components">The same component on each selected entity that has it, the primary one's first.</param>
    /// <param name="lacking">The selected entities that don't have it: the section then only shows its name, greyed.</param>
    public ComponentSection(IReadOnlyList<Component> components, InspectorPanel owner, ComponentRegistry registry, IReadOnlyList<Entity>? lacking = null)
    {
        _owner = owner;
        _components = components;
        Lacking = lacking ?? [];
        var component = Component = components[0];
        RemoveCommand = new RelayCommand(Remove, () => CanRemove);
        EditScriptCommand = new RelayCommand(() => owner.EditScript(component.GetType()));
        HeaderMenuCommand = new RelayCommand<FrameworkElement>(HeaderMenu);

        if (component is MissingComponent missing)
        {
            Title = TypeName = TypeKey = missing.TypeName;
            IsMissing = true;
            Properties = [];
            return;
        }

        var descriptor = registry.Get(component.GetType());
        TypeName = descriptor.DisplayName;
        TypeKey = descriptor.Name;
        Title = InspectorPanel.Humanize(descriptor.DisplayName);
        var summary = Griff.Editing.DocComments.Summary(descriptor.Type);
        Tip = summary.IsEmpty ? null : summary.PlainText;
        _descriptor = descriptor;
        if (IsPartial)
        {
            Properties = [];
            var count = components.Count + Lacking.Count;
            PartialNote = $"on {components.Count} of {count}";
            PartialTip = $"{Join(components.Select(c => c.Entity?.Name ?? ""))} {(components.Count == 1 ? "has" : "have")} it; " +
                $"{Join(Lacking.Select(e => e.Name))} {(Lacking.Count == 1 ? "doesn't" : "don't")}. Add to All gives {(Lacking.Count == 1 ? "it" : "them")} " +
                $"{(components.Count == 1 ? "a copy" : $"a copy of {components[0].Entity?.Name}'s")}.";
            RemoveTip = $"Remove it from the {components.Count} that have it";
            return;
        }

        Properties = Used().Select(p => new PropertyField(components, p, owner)).ToList();
        (Groups, Rows) = Arrange(Properties);
        SetupNote = component.SetupNote();
        IsAddedToCopy = components.Any(owner.IsAddedToCopy);
        Needs = descriptor.Requires
            .Select(r => (Link: r, Lacking: components.Count(c => c.Entity is { } e && !r.IsMetBy(e, c))))
            .Where(r => r.Lacking > 0)
            .Select(r => new NeedRow(r.Link, r.Lacking, components, owner, registry))
            .ToList();
        var removeNote = owner.DependentsNote(components);
        RemoveTip = removeNote is null ? "Remove component" : $"Remove component. {removeNote}";
    }

    /// <summary>
    /// What the component needs on its entity and doesn't have (a PlatformerMovement without a Body), each with a button
    /// that adds it. Until then it won't work.
    /// </summary>
    public IReadOnlyList<NeedRow> Needs { get; } = [];

    public bool HasNeeds => Needs.Count > 0;

    /// <summary>The remove button's tooltip, which says what needs the component (and stops working without it).</summary>
    public string RemoveTip { get; } = "Remove component";

    private readonly ComponentDescriptor? _descriptor;

    // The properties every one of the components uses as it's set up now (a tilemap layer leaves out its map's settings).
    private IEnumerable<PropertyDescriptor> Used() =>
        _descriptor?.Properties.Where(p => _components.All(c => c.Uses(p.Name))) ?? [];

    /// <summary>
    /// True when what the components use or say about how they're set up has changed since the section was made (a
    /// tilemap became a layer, or stopped being one), so the Inspector makes it again.
    /// </summary>
    public bool IsOutOfDate =>
        !Used().Select(p => p.Name).SequenceEqual(Properties.Select(p => p.PropertyName)) || Component.SetupNote() != SetupNote;

    /// <summary>A line on how the component is set up, shown above its fields (see <see cref="Component.SetupNote"/>).</summary>
    public string? SetupNote { get; }

    public bool HasSetupNote => SetupNote is not null;

    /// <summary>On a placed copy of a prefab (or inside one), added there: the prefab doesn't have it.</summary>
    public bool IsAddedToCopy { get; }

    /// <summary>The primary entity's component.</summary>
    public Component Component { get; }

    /// <summary>The component's name as the Inspector shows it, "Platformer Movement".</summary>
    public string Title { get; }

    /// <summary>The component's type's name, "PlatformerMovement".</summary>
    public string TypeName { get; }

    /// <summary>The name its type is saved under, which says whether it's folded shut.</summary>
    public string TypeKey { get; }

    /// <summary>What the component does (its documentation), shown over its name.</summary>
    public string? Tip { get; }

    /// <summary>Its type's name ("DamageOnTouch"): agents find it by that as well as by its <see cref="Title"/>.</summary>
    public override string ToString() => TypeName;

    /// <summary>True when <paramref name="name"/> is the component's name, as shown or as its type's.</summary>
    public bool IsNamed(string name) =>
        Title.Equals(name, StringComparison.OrdinalIgnoreCase) || TypeName.Equals(name, StringComparison.OrdinalIgnoreCase);

    public bool IsMissing { get; }

    public bool CanRemove => Component is not Transform;

    /// <summary>True for components from the project's scripts, which can be opened in the script editor.</summary>
    public bool IsScript => Component is not MissingComponent && Component.GetType().Assembly != typeof(Component).Assembly;

    public IRelayCommand EditScriptCommand { get; }

    public IReadOnlyList<PropertyField> Properties { get; }

    /// <summary>The component's subheadings ([Group]), in order, each with its fields; empty when it has none.</summary>
    public IReadOnlyList<FieldGroup> Groups { get; } = [];

    /// <summary>What the section lists: the fields with no group first, then each group's heading followed by its fields.</summary>
    public IReadOnlyList<object> Rows { get; } = [];

    /// <summary>The subheading called <paramref name="name"/>, or null.</summary>
    public FieldGroup? Group(string name) => Groups.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // Groups gather their fields wherever they're declared (a script's base class may add to one), in the order each
    // group first comes up; ungrouped fields go first.
    private (IReadOnlyList<FieldGroup>, IReadOnlyList<object>) Arrange(IReadOnlyList<PropertyField> fields)
    {
        if (fields.All(f => f.GroupName is null))
            return ([], [.. fields]);
        var groups = fields.Where(f => f.GroupName is not null).GroupBy(f => f.GroupName!)
            .Select(g => new FieldGroup(g.Key, [.. g], this, _owner)).ToList();
        var rows = new List<object>(fields.Where(f => f.GroupName is null));
        foreach (var group in groups)
        {
            rows.Add(group);
            rows.AddRange(group.Fields);
        }

        return (groups, rows);
    }

    public IRelayCommand RemoveCommand { get; }

    /// <summary>
    /// With several entities selected, the ones that don't have this component (empty when they all do). The section
    /// then shows only its name, greyed, saying how many have it, with Add to All.
    /// </summary>
    public IReadOnlyList<Entity> Lacking { get; }

    /// <summary>True when only some of the selected entities have the component.</summary>
    public bool IsPartial => Lacking.Count > 0;

    /// <summary>"on 2 of 3" when only some of the selected entities have it; otherwise null.</summary>
    public string? PartialNote { get; }

    /// <summary>Which of the selected entities have it and which don't.</summary>
    public string? PartialTip { get; }

    /// <summary>
    /// Adds the component to each selected entity that doesn't have it, as a copy of the one shown (its values too, and
    /// with what it needs), as one undo step.
    /// </summary>
    public void AddToAll()
    {
        if (!IsPartial)
            throw new InvalidOperationException($"Every selected entity has {Title} already.");
        _owner.AddCopies(Component, Lacking);
    }

    public IRelayCommand AddToAllCommand => _addToAll ??= new RelayCommand(AddToAll, () => IsPartial);

    private IRelayCommand? _addToAll;

    /// <summary>Whether its values can be reset and copied: not for a component whose type is missing, or only some have.</summary>
    public bool HasValues => _descriptor is not null && !IsPartial;

    /// <summary>Sets every value back to what a new one starts with (on each selected entity), as one undo step.</summary>
    public void ResetToDefaults()
    {
        CheckValues();
        _owner.Reset(_components);
    }

    /// <summary>Copies its values (the primary entity's), for Paste Values on another of its type.</summary>
    public void CopyValues()
    {
        CheckValues();
        _owner.Copy(Component);
    }

    /// <summary>True when values copied from a component of its type are waiting to be pasted.</summary>
    public bool CanPaste => HasValues && _owner.Copied?.Type == Component.GetType();

    /// <summary>Sets the copied values on it (on each selected entity), as one undo step.</summary>
    public void PasteValues()
    {
        CheckValues();
        if (_owner.Copied is not { } copied)
            throw new InvalidOperationException("Nothing's been copied yet: Copy Values on a component first.");
        if (!CanPaste)
            throw new InvalidOperationException($"The copied values are {Griff.Editing.ComponentCatalog.Article(InspectorPanel.Humanize(copied.TypeName))}'s, not {Griff.Editing.ComponentCatalog.Article(Title)}'s.");
        _owner.Paste(_components);
    }

    /// <summary>True when it isn't the first component under the Transform (on one of the selected entities).</summary>
    public bool CanMoveUp => !IsPartial && Griff.Editing.ComponentEdits.CanMove(_components, -1);

    /// <summary>True when it isn't the last component (on one of the selected entities).</summary>
    public bool CanMoveDown => !IsPartial && Griff.Editing.ComponentEdits.CanMove(_components, 1);

    /// <summary>Moves it one place up its entity's list of components (each selected entity's), as one undo step.</summary>
    public void MoveUp()
    {
        CheckMove(CanMoveUp, "first, under the Transform");
        _owner.Move(_components, -1);
    }

    /// <summary>Moves it one place down its entity's list of components (each selected entity's), as one undo step.</summary>
    public void MoveDown()
    {
        CheckMove(CanMoveDown, "last");
        _owner.Move(_components, 1);
    }

    // Says why its values can't be reset, copied or pasted.
    private void CheckValues()
    {
        if (IsPartial)
            throw new InvalidOperationException($"Only some of the selection have {Title}: select those, or Add to All first.");
        if (!HasValues)
            throw new InvalidOperationException($"{Title}'s type couldn't be found, so its values can't be changed.");
    }

    // Says why it can't move that way.
    private void CheckMove(bool can, string already)
    {
        if (IsPartial)
            throw new InvalidOperationException($"Only some of the selection have {Title}: select those to move it.");
        if (Component is Transform)
            throw new InvalidOperationException("The Transform stays first.");
        if (!can)
            throw new InvalidOperationException($"{Title} is already {already}.");
    }

    /// <summary>
    /// Shows the component's menu (right-clicking its header): Reset to Defaults, Copy and Paste Values, Move Up and Move
    /// Down, or Add to All when only some of the selection have it, and Remove Component.
    /// </summary>
    public IRelayCommand<FrameworkElement> HeaderMenuCommand { get; }

    private void HeaderMenu(FrameworkElement? anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.MousePoint };
        void Add(string header, string tag, Action action, bool enabled = true, string? icon = null, string? tip = null)
        {
            var item = new MenuItem { Header = header, Tag = $"component:{tag}", IsEnabled = enabled, ToolTip = tip };
            ToolTipService.SetShowOnDisabled(item, true);
            if (icon is not null)
                item.Icon = Icons.Create(icon);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        if (IsPartial)
        {
            Add("Add to All", "addall", AddToAll, icon: Icons.Add, tip: PartialTip);
        }
        else
        {
            Add("Reset to Defaults", "reset", ResetToDefaults, HasValues, Icons.Undo, "Sets every value back to what a new one starts with");
            menu.Items.Add(new Separator());
            Add("Copy Values", "copy", CopyValues, HasValues, Icons.Copy);
            var copied = _owner.Copied;
            Add(copied is null ? "Paste Values" : $"Paste {InspectorPanel.Humanize(copied.TypeName)} Values", "paste", PasteValues, CanPaste,
                tip: copied is null ? "Copy Values on a component first"
                    : CanPaste ? null
                    : $"The copied values are {Griff.Editing.ComponentCatalog.Article(InspectorPanel.Humanize(copied.TypeName))}'s");
            menu.Items.Add(new Separator());
            Add("Move Up", "up", MoveUp, CanMoveUp);
            Add("Move Down", "down", MoveDown, CanMoveDown);
        }

        if (CanRemove)
        {
            menu.Items.Add(new Separator());
            Add("Remove Component", "remove", Remove, icon: Icons.Delete, tip: RemoveTip);
        }

        menu.IsOpen = true;
    }

    // "Red Gem and Blue Gem", or for more than three, "4 of them".
    private static string Join(IEnumerable<string> names) =>
        names.ToList() is var list && list.Count > 3 ? $"{list.Count} of them" : ComponentCatalogViewModel.Join(list);

    /// <summary>
    /// Open, showing its fields. Folding one shut folds that component shut on every entity, until it's opened again, and
    /// it's remembered next time.
    /// </summary>
    public bool IsExpanded
    {
        get => !_owner.IsCollapsed(TypeKey);
        set => _owner.SetCollapsed(TypeKey, !value);
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(ShowsFields));
        foreach (var group in Groups)
            group.Refresh();
        foreach (var property in Properties)
            property.Refresh();
        if (IsTransform)
        {
            OnPropertyChanged(nameof(Is3D));
            OnPropertyChanged(nameof(HiddenAxesNote));
            OnPropertyChanged(nameof(HasHiddenAxesNote));
        }
    }

    // ----- Transforms

    /// <summary>True for the Transform, which shows in 2D or 3D.</summary>
    public bool IsTransform => Component is Transform && !IsPartial;

    /// <summary>
    /// Whether Transforms show in 3D (X, Y and Z) rather than 2D (X and Y, one rotation angle): the switch on the
    /// Transform's header, the same for every entity, and kept next time.
    /// </summary>
    public bool Is3D
    {
        get => _owner.Transform3D;
        set => _owner.Transform3D = value;
    }

    /// <summary>
    /// In 2D, says which values 3D would show that aren't at rest ("Also set in 3D: Position Z 2"), as they still count
    /// (a position's Z orders what's drawn); otherwise null.
    /// </summary>
    public string? HiddenAxesNote => IsTransform && Properties.SelectMany(p => p.HiddenAxisValues()).ToList() is { Count: > 0 } hidden
        ? $"Also set in 3D: {string.Join(", ", hidden)}"
        : null;

    public bool HasHiddenAxesNote => HiddenAxesNote is not null;

    // ----- Filtering

    private bool _filteredOut;

    /// <summary>True while the Inspector's filter leaves the component out: neither it nor any of its fields match.</summary>
    public bool IsFilteredOut
    {
        get => _filteredOut;
        private set => SetProperty(ref _filteredOut, value);
    }

    /// <summary>Whether its fields show: while it's open, or while a filter shows what matches in it.</summary>
    public bool ShowsFields => IsExpanded || _owner.IsFiltering;

    /// <summary>
    /// Narrows its fields to those whose names have <paramref name="text"/> (all of them when the component's name has
    /// it, or a subheading's when that has it); empty shows them all.
    /// </summary>
    internal void ApplyFilter(string text)
    {
        var all = text.Length == 0 || Title.Contains(text, StringComparison.OrdinalIgnoreCase) || TypeName.Contains(text, StringComparison.OrdinalIgnoreCase);
        foreach (var field in Properties)
            field.IsFilteredOut = !all && !field.Matches(text) && field.Group?.Name.Contains(text, StringComparison.OrdinalIgnoreCase) != true;
        foreach (var group in Groups)
            group.Refresh();
        IsFilteredOut = !all && Properties.All(p => p.IsFilteredOut);
        OnPropertyChanged(nameof(ShowsFields));
    }

    // From every selected entity, as one undo step; asks first when something needs it.
    private void Remove() => _owner.RemoveComponents(_components, Title);
}

/// <summary>
/// A subheading in a component's section (from <see cref="GroupAttribute"/>), "Dash" or "Wall", over the fields that
/// go under it. Folding it shut hides them, for that component on every entity, and it's remembered next time.
/// </summary>
public sealed class FieldGroup : ObservableObject
{
    private readonly ComponentSection _section;
    private readonly InspectorPanel _owner;

    public FieldGroup(string name, IReadOnlyList<PropertyField> fields, ComponentSection section, InspectorPanel owner)
    {
        Name = name;
        Fields = fields;
        _section = section;
        _owner = owner;
        foreach (var field in fields)
            field.Group = this;
    }

    public string Name { get; }

    public IReadOnlyList<PropertyField> Fields { get; }

    // Kept with the components' folded sections, under "Type/Group", which no type's name can be.
    private string Key => $"{_section.TypeKey}/{Name}";

    /// <summary>Open, showing its fields.</summary>
    public bool IsExpanded
    {
        get => !_owner.IsCollapsed(Key);
        set => _owner.SetCollapsed(Key, !value);
    }

    /// <summary>True when every field under it is faded, as a dash's are with Dash off (see <see cref="PropertyField.IsFaded"/>).</summary>
    public bool IsFaded => Fields.All(f => f.IsFaded);

    /// <summary>True while the Inspector's filter hides every field under it, which hides the subheading too.</summary>
    public bool IsFilteredOut => Fields.All(f => f.IsFilteredOut);

    /// <summary>Says what folding does, and when it's shut, how many fields it hides.</summary>
    public string Tip => IsExpanded ? $"Fold {Name} shut" : $"Open {Name}: {Fields.Count} field{(Fields.Count == 1 ? "" : "s")}";

    /// <summary>Folds it shut, or opens it: clicking its name.</summary>
    public IRelayCommand ToggleCommand => _toggle ??= new RelayCommand(() => IsExpanded = !IsExpanded);

    private IRelayCommand? _toggle;

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// Something a component needs on its entity and doesn't have: "Needs a Body (what it pushes), which Player doesn't
/// have", with a button that adds one (with what that needs in turn).
/// </summary>
public sealed class NeedRow
{
    public NeedRow(ComponentLink requirement, int lacking, IReadOnlyList<Component> components, InspectorPanel owner, ComponentRegistry registry)
    {
        Requirement = requirement;
        var name = requirement.Type.Name;
        var note = requirement.Note is null ? "" : $" ({requirement.Note})";
        Message = components.Count == 1
            ? $"Needs {Griff.Editing.ComponentCatalog.Article(name)}{note}, which {components[0].Entity?.Name} doesn't have."
            : $"Needs {Griff.Editing.ComponentCatalog.Article(name)}{note}, which {lacking} of the {components.Count} don't have.";
        var satisfier = Griff.Editing.ComponentCatalog.Satisfier(registry, requirement.Type);
        AddLabel = satisfier is null ? "" : $"Add {InspectorPanel.Humanize(satisfier.DisplayName)}";
        var options = Griff.Editing.ComponentCatalog.Options(registry, requirement.Type);
        Tip = satisfier is null ? null
            : options.Count > 1 ? $"Any kind of {name} works ({string.Join(" or ", options.Select(o => InspectorPanel.Humanize(o.DisplayName)))}); this adds {Griff.Editing.ComponentCatalog.Article(InspectorPanel.Humanize(satisfier.DisplayName))}."
            : $"Adds {Griff.Editing.ComponentCatalog.Article(InspectorPanel.Humanize(satisfier.DisplayName))}{(components.Count == 1 ? "" : " to each that doesn't have one")}.";
        AddCommand = new RelayCommand(() => owner.AddRequirement(components, requirement), () => satisfier is not null);
    }

    /// <summary>The component it needs (a base type, like Collider, takes any kind).</summary>
    public ComponentLink Requirement { get; }

    /// <summary>The type it needs, by name: what a demo or agent asks for it by.</summary>
    public string Name => Requirement.Type.Name;

    public string Message { get; }

    /// <summary>"Add Body", or for a kind of thing, the kind it adds ("Add Box Collider").</summary>
    public string AddLabel { get; }

    public string? Tip { get; }

    public IRelayCommand AddCommand { get; }
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

    /// <summary>
    /// The name of a prefab, scene, input action, collision layer, message or font (or several, separated by commas):
    /// text, with a button to pick one of the project's, flagged when it matches none of them. A font's list previews each
    /// in its own face, the ones safe to ship first and those only installed on this PC apart.
    /// </summary>
    Name,

    /// <summary>
    /// The name of an action (a trigger's Do): text, with a button to pick one of what the entities it's done on can do,
    /// flagged when none of them can.
    /// </summary>
    Action,

    /// <summary>
    /// The name of an output (a counter's, bar's, text's or wire's Output): text, with a button to pick one of what the
    /// entity it reads knows, flagged when it knows nothing called that.
    /// </summary>
    Output,

    /// <summary>
    /// A field with an editor of its own (see <see cref="FieldEditorAttribute"/>): dice, points, tile numbers, clips and the
    /// like, edited through <see cref="PropertyField.Editor"/>.
    /// </summary>
    Custom,
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
        var summary = Griff.Editing.DocComments.Summary(property);
        Summary = summary.IsEmpty ? null : summary.PlainText;

        var type = property.Type;
        Kind = type == typeof(bool) ? FieldKind.Bool
            : type == typeof(int) || type == typeof(float) || type == typeof(double) ? FieldKind.Number
            : type == typeof(string) && (IsSoundName(property.Name) || property.File?.Kind is AssetFileKind.Sound or AssetFileKind.Music) ? FieldKind.Sound
            : type == typeof(string) && (IsImageName(property.Name) || property.File?.Kind == AssetFileKind.Image) ? FieldKind.Image
            : property.EntityName is not null ? FieldKind.Entity
            : property.Names is not null ? FieldKind.Name
            : property.ActionName is not null ? FieldKind.Action
            : property.OutputName is not null ? FieldKind.Output
            : type == typeof(string) ? FieldKind.Text
            : type.IsEnum ? FieldKind.Enum
            : type == typeof(Color) ? FieldKind.Color
            : type == typeof(IReadOnlyList<string>) ? FieldKind.Lines
            : FieldKind.Vector;

        if (Kind == FieldKind.Enum)
        {
            EnumValues = Enum.GetValues(type);
            EnumChoices = [.. EnumValues.Cast<Enum>().Select(v => new EnumChoice(v))];
        }

        if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion))
        {
            Axes = Enumerable.Range(0, ToFloats(Value).Length)
                .Select(i => new AxisField(this, i, AxisNames[i]))
                .ToList();
        }

        // A field marked for an editor of its own gets it, when the editor has one for it; a list of strings without one gets
        // rows (or names and values), unless it's marked as plain text.
        if ((property.Editor ?? GenericFields.For(property, Value)) is { } attribute && FieldEditor.Create(this, attribute) is { } editor)
        {
            _attribute = attribute;
            Editor = editor;
            Kind = FieldKind.Custom;
        }
    }

    // What its editor reads the value as: the attribute it's marked with, or the rows a list without one gets.
    private readonly FieldEditorAttribute? _attribute;

    /// <summary>
    /// For a field with an editor of its own (<see cref="FieldKind.Custom"/>): that editor, e.g. a dice builder or a list of
    /// points; null otherwise.
    /// </summary>
    public FieldEditor? Editor { get; }

    /// <summary>True when its editor takes the section's whole width, under its name (see <see cref="FieldEditor.Stacks"/>).</summary>
    public bool Stacks => Editor?.Stacks == true;

    /// <summary>The components it edits, one per selected entity, the primary one's first.</summary>
    internal IReadOnlyList<Component> Components => _components;

    /// <summary>What it edits through.</summary>
    internal IPropertyHost Host => _owner;

    /// <summary>The property it edits.</summary>
    internal PropertyDescriptor Property => _property;

    /// <summary>
    /// What's wrong with the value, for a field whose editor reads it (dice that aren't dice, a clip that isn't one of the
    /// animation's), shown in yellow; null when it reads, or the selected entities' values differ.
    /// </summary>
    public string? ValueProblem => IsMixed ? null : (_attribute ?? _property.Editor)?.Problem(_components[0], Value);

    /// <summary>True while <see cref="ValueProblem"/> says something.</summary>
    public bool HasValueProblem => ValueProblem is not null;

    public string Label { get; }

    /// <summary>What the property does, from its documentation (units included), or null if it has none.</summary>
    public string? Summary { get; }

    /// <summary>
    /// The tooltip over the field's name: on a field changed from its prefab, what the prefab has; what the field does;
    /// and for a number, how to drag it.
    /// </summary>
    public string? LabelTip
    {
        get
        {
            var tip = string.Join("\n\n", new[] { OverrideTip, IsFromTable ? TableSource : null, FadeTip, Summary, Kind == FieldKind.Number ? DragTip : null }.OfType<string>());
            return tip.Length > 0 ? tip : null;
        }
    }

    /// <summary>The subheading it's listed under ([Group]), or null.</summary>
    public string? GroupName => _property.Group;

    /// <summary>The subheading it's listed under, set by it.</summary>
    public FieldGroup? Group { get; internal set; }

    /// <summary>True while its subheading is folded shut, which hides it.</summary>
    public bool IsFolded => Group is { IsExpanded: false };

    /// <summary>
    /// True while what it depends on is off ([ShownWhen]: a dash's speed with Dash off), on every selected entity, so it
    /// isn't used: it's drawn faded, and can still be set ahead of time.
    /// </summary>
    public bool IsFaded => _components.All(c => !_property.IsShown(c));

    /// <summary>
    /// For a field a <see cref="TableValues"/> on the entity sets from a table's row as the game runs: where from, e.g.
    /// "Set from the Enemies table (Slime's Health) when the game runs: change it there, in the Tables panel."; otherwise "".
    /// </summary>
    public string TableSource => _tableSource ??= FindTableSource() ?? "";

    private string? _tableSource;

    /// <summary>True when a Table Values on the entity sets this field from a table (see <see cref="TableSource"/>).</summary>
    public bool IsFromTable => TableSource.Length > 0;

    private string? FindTableSource()
    {
        var component = _components[0];
        if (component is TableValues || component.Entity is not { } entity)
            return null;
        foreach (var values in entity.GetComponents<TableValues>())
        {
            var columns = _owner.Names?.ColumnsOf(values.Table);
            if (values.Targets(columns).FirstOrDefault(t => t.Component == component && t.Property.Name == _property.Name) is { } target)
                return $"Set from the {values.Table} table ({values.RowName}'s {target.Column}) when the game runs: change it there, in the Tables panel.";
        }

        return null;
    }

    /// <summary>For a faded field: "Only used when Dash is on."; otherwise null.</summary>
    public string? FadeTip => IsFaded ? $"Only used when {_property.ShownWhenText(InspectorPanel.Humanize, InspectorPanel.Humanize)}." : null;

    private const string DragTip = "Drag the name sideways to change it (Shift: fine, Ctrl: coarse), or press Up and Down while typing.";

    /// <summary>The property's name in code ("TileSize").</summary>
    public string PropertyName => _property.Name;

    /// <summary>Its name in code ("AmountRoll"): agents find it by that as well as by its <see cref="Label"/>.</summary>
    public override string ToString() => PropertyName;

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
    public System.Windows.Media.ImageSource? ImagePreview => Kind == FieldKind.Image && !IsMixed ? _owner.Art.Thumbnail(Value as string) : null;

    /// <summary>For an image field: set to a file that isn't in the project.</summary>
    public bool IsImageMissing => Kind == FieldKind.Image && !IsMixed && Value is string { Length: > 0 } path &&
        _owner.Art is { AssetsPath: not null } art && art.Resolve(path) is null;

    public string ImageTip => Value is string { Length: > 0 } && !IsImageMissing ? "Paint over this in the Sprite Painter" : "Paint a picture for this in the Sprite Painter";

    /// <summary>
    /// Opens the Sprite Painter on the field's picture (or a new one shaped for the component); each picture it saves
    /// is put on the component.
    /// </summary>
    public void PaintImage() => PaintImage(fresh: false);

    private void PaintImage(bool fresh)
    {
        var art = _owner.Art;
        var host = _owner;
        var (components, property) = (_components, _property);
        var current = fresh || IsMixed || art.Resolve(Value as string) is null ? null : (string)Value!;
        var painter = art.OpenPainter(current, Art.EditorArt.LayoutFor(components[0]), (image, used) => host.UseImage(components, property, image, used));
        if (current is null && components[0].Entity is { } entity)
            painter.Model.Name = painter.Model.SuggestName(Art.EditorArt.Slug(entity.Name));
    }

    public IRelayCommand PaintImageCommand => _paintImage ??= new RelayCommand(PaintImage);

    private IRelayCommand? _paintImage;

    // A picture painted in Griff brings its frames or tiles along; any other is just set.
    private void UseChosenImage(string image)
    {
        if (_owner.Art.LayoutOf(image) is { } layout)
            _owner.UseImage(_components, _property, image, layout);
        else
            TextValue = image;
    }

    /// <summary>For a sound field: whether it's music (made in the Sound Maker's Music mode) rather than an effect.</summary>
    public bool IsMusic => _property.Name == "Music" || _property.File?.Kind == AssetFileKind.Music;

    /// <summary>For a sound field: set to a file that isn't in the project.</summary>
    public bool IsSoundMissing => Kind == FieldKind.Sound && !IsMixed && Value is string { Length: > 0 } path &&
        _owner.Audio is { AssetsPath: not null } audio && audio.Resolve(path) is null;

    public string SoundTip => Value is string { Length: > 0 } path && _owner.Audio.CanEdit(path)
        ? "Edit this sound in the Sound Maker"
        : IsMusic ? "Make music for this in the Sound Maker" : "Make a sound for this in the Sound Maker";

    /// <summary>Plays the field's sound, or stops it if it's playing.</summary>
    public void PreviewSound() => _owner.Audio.TogglePreview(Value as string);

    /// <summary>
    /// Opens the Sound Maker on the field's sound if it was made there, or on a new one; whatever it saves is set here.
    /// </summary>
    public void MakeSound()
    {
        var audio = _owner.Audio;
        var current = IsMixed ? null : Value as string;
        var mode = IsMusic ? Audio.SoundMakerMode.Music : Audio.SoundMakerMode.Effect;
        audio.OpenMaker(mode, audio.CanEdit(current) ? current : null, saved => TextValue = saved);
    }

    /// <summary>The project's sounds to choose from.</summary>
    public IReadOnlyList<string> SoundChoices() => _owner.Audio.Sounds();

    public IRelayCommand PreviewSoundCommand => _previewSound ??= new RelayCommand(PreviewSound);

    public IRelayCommand MakeSoundCommand => _makeSound ??= new RelayCommand(MakeSound);

    private IRelayCommand? _previewSound, _makeSound;

    /// <summary>
    /// For a field naming entities, layers or the like: whether it names several, separated by commas, so picking one
    /// adds it (or takes it out).
    /// </summary>
    public bool NamesMany => _property.EntityName?.Many == true || _property.Names?.Many == true;

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
    /// The names the field holds that match nothing (shown in yellow): for an entity field, no entity it can name and no
    /// prefab; for one naming a prefab, scene, input action, layer or message, none the project has (see
    /// <see cref="KnownNames"/>). Empty when they all match, or with several selected whose values differ.
    /// </summary>
    public IReadOnlyList<string> UnmatchedNames => _unmatched ??= IsMixed || IsFaded ? [] : Kind switch
    {
        FieldKind.Entity => EntityReferences.Unmatched(new(_components[0], _property), _owner.PrefabNames, _owner.InPrefab),
        FieldKind.Name => _owner.Names?.Unmatched(_components[0], _property) ?? [],
        FieldKind.Action => ActionNames.Unmatched(_components[0], _property),
        FieldKind.Output => OutputNames.Unmatched(_components[0], _property),
        _ => [],
    };

    private IReadOnlyList<string>? _unmatched;

    public bool IsUnmatched => UnmatchedNames.Count > 0;

    /// <summary>
    /// Says which names match nothing, e.g. "Nothing in the scene is called "Playr"" or "There's no prefab called
    /// "Bulet" in the project".
    /// </summary>
    public string? UnmatchedTip => !IsUnmatched ? null
        : Kind == FieldKind.Name ? KnownNames.Problem(_property.Names!, UnmatchedNames)
        : Kind == FieldKind.Action ? ActionNames.Problem(UnmatchedNames)
        : Kind == FieldKind.Output ? OutputNames.Problem(UnmatchedNames)
        : $"Nothing {(_property.EntityName!.Inside ? $"inside {_components[0].Entity?.Name}" : "in the scene")} is called " +
          string.Join(" or ", UnmatchedNames.Select(n => $"\"{n}\""));

    /// <summary>True when the field names <paramref name="name"/>: on every selected entity, when there are several (an empty font names the built-in one).</summary>
    public bool Names(string name) => IsFont ? NamesFont(name) : _components.All(c => SplitNames(_property.GetValue(c)).Contains(name, NameComparer));

    // Entities and messages match exactly, as the game matches them; prefabs, scenes, actions and layers whatever their capitals.
    private StringComparer NameComparer =>
        _property.Names is { Kind: not NameKind.Message } || _property.ActionName is not null || _property.OutputName is not null ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Picks something from the field's list by name, as clicking it there does: an image or sound by its path; on a
    /// field naming entities, prefabs, layers or the like, names it, or on one naming several, adds it (to those that
    /// don't have it yet) or takes it out again (when they all have it).
    /// </summary>
    public void Pick(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return;
        switch (Kind)
        {
            case FieldKind.Image:
                UseChosenImage(name);
                return;
            case FieldKind.Sound:
                TextValue = name;
                _owner.Audio.Preview(name);
                return;
            case FieldKind.Enum:
                EnumValue = name;
                return;
        }

        if (IsFont)
        {
            PickFont(name);
            return;
        }

        if (!NamesMany)
            Value = name;
        else if (Names(name))
            SetEach(names => names.RemoveAll(n => NameComparer.Equals(n, name)));
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
        names = [.. names.Distinct(NameComparer)];
        return names.SequenceEqual(had) ? _property.GetValue(c) : names.Count == 0 ? null : (object)string.Join(", ", names);
    })]);

    private static List<string> SplitNames(object? value) =>
        [.. (value as string ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>True for a field naming a font (a Text's Font, or a script's string called ...Font).</summary>
    public bool IsFont => Kind == FieldKind.Name && _property.Names?.Kind == NameKind.Font;

    // A font picked: the built-in font empties the field; one of Griff's free fonts is copied into Assets/Fonts with its
    // license first (unless it's there), then named by its path, so the game ships it.
    private void PickFont(string name)
    {
        if (Griff.Fonts.BuiltInFonts.Find(name) == Griff.Fonts.BuiltInFonts.Default)
        {
            Value = null;
        }
        else if (Griff.Fonts.FreeFonts.Find(name) is { } free && _owner.Art.AssetsPath is { } assets)
        {
            Value = free.AddTo(assets);
        }
        else
        {
            Value = name;
        }

        _owner.Seal();
    }

    // True when every selected component's font is the one called name: an empty font is the built-in one.
    private bool NamesFont(string name) => _components.All(c =>
    {
        var font = (_property.GetValue(c) as string)?.Trim();
        return string.IsNullOrEmpty(font)
            ? Griff.Fonts.BuiltInFonts.Find(name) == Griff.Fonts.BuiltInFonts.Default
            : string.Equals(font.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase);
    });

    /// <summary>True for a field with a list to pick from: images, sounds, and names of entities, prefabs, scenes, input actions, layers and messages.</summary>
    public bool HasPicker => Kind is FieldKind.Image or FieldKind.Sound or FieldKind.Entity or FieldKind.Name or FieldKind.Action or FieldKind.Output || IsLongEnum;

    /// <summary>Enums with more choices than this (a key, of a hundred) are picked from a list to search rather than dropped down.</summary>
    public const int LongEnum = 12;

    /// <summary>
    /// For an enum field with more than <see cref="LongEnum"/> choices (a key to press): picked from a list searched by typing
    /// (its picker, as a name's), rather than a drop-down to scroll.
    /// </summary>
    public bool IsLongEnum => Kind == FieldKind.Enum && EnumChoices.Count > LongEnum;

    /// <summary>For an enum field: its choice as it reads ("Page Up"), or <see cref="Mixed"/> when the selected entities' differ.</summary>
    public string EnumLabel => IsMixed ? Mixed : Value is Enum value ? InspectorPanel.Humanize(value.ToString()) : "";

    /// <summary>For a field naming a prefab, scene, input action, layer or message: "a prefab", "a message".</summary>
    public string? NameNoun => _property.Names is { } kind ? KnownNames.Noun(kind) : null;

    /// <summary>The tooltip on the button that opens the field's list.</summary>
    public string PickerTip => Kind switch
    {
        FieldKind.Image => "Choose an image from the project",
        FieldKind.Sound => IsMusic ? "Choose music from the project" : "Choose a sound from the project",
        FieldKind.Entity => _property.EntityName?.Inside == true
            ? "Choose an entity inside this one, or drag one here from the Hierarchy"
            : "Choose an entity from the scene, or drag one here from the Hierarchy",
        FieldKind.Action => "Choose something it can do: what the components of what it's done on can do",
        FieldKind.Output => "Choose what it reads: what the components of the entity it reads know",
        FieldKind.Enum => "Choose one: type a few letters to find it",
        _ => _property.Names?.Kind switch
        {
            NameKind.Message => "Choose a message the project sends or listens for, or type a new one",
            NameKind.Layer => "Choose a collision layer something is on, or type a new one",
            NameKind.Action => "Choose an input action: every game's own, or one from Game > Controls",
            NameKind.Prefab => "Choose a prefab from the project, or drag one here from the Assets panel",
            NameKind.Table => "Choose a table from the project, or drag one here from the Assets panel",
            NameKind.Row => "Choose a row of the table",
            NameKind.Font => "Choose a font, each shown in its own face: the ones safe to ship first (Griff's free fonts are copied into Assets), or drag a font file here from the Assets panel",
            NameKind.Item => "Choose an item something gives (a pickup's Item), or type a new one",
            NameKind.Flag => "Choose a flag something sets (a trigger's Set Flag, a dialogue's [set]), or \"not\" one for while it isn't set",
            NameKind.Kind when _property.Names!.From is { } from => $"Choose {KnownNames.Noun(_property.Names!)} from {from}, or type a new one",
            NameKind.Kind => $"Choose {KnownNames.Noun(_property.Names!)} something is, or type a new one",
            NameKind.Saved => "Choose a name something keeps a value under in the save data (a Save As, a script's SaveData.Set), or type a new one",
            NameKind.Column => "Choose a column of the table",
            _ => "Choose a scene from the project, or drag one here from the Assets panel",
        },
    };

    private FieldPicker? _picker;
    private long _pickerClosedAt;
    private IRelayCommand? _togglePicker;

    /// <summary>
    /// The field's list while it's open (null when it's shut): what it can pick, narrowed by what's typed in its Search.
    /// Open it with <see cref="IsPickerOpen"/>, and pick with its Pick("Name") or PickSelected.
    /// </summary>
    public FieldPicker? Picker
    {
        get => _picker;
        private set => SetProperty(ref _picker, value);
    }

    /// <summary>Whether the field's list is open. Opening it lists what's there now, with nothing typed.</summary>
    public bool IsPickerOpen
    {
        get => Picker is not null;
        set
        {
            if (value == IsPickerOpen || (value && !HasPicker))
                return;
            if (!value)
                _pickerClosedAt = Environment.TickCount64;
            Picker = value ? CreatePicker() : null;
            OnPropertyChanged();
        }
    }

    /// <summary>Opens the field's list, or shuts it.</summary>
    public IRelayCommand TogglePickerCommand => _togglePicker ??= new RelayCommand(() =>
        // A click on the button while the list is open shuts it (as the mouse goes down outside it), rather than
        // opening it again.
        IsPickerOpen = !IsPickerOpen && Environment.TickCount64 - _pickerClosedAt > 250);

    private FieldPicker CreatePicker()
    {
        var current = IsMixed ? null : Value as string;
        PickerAction[] none = !string.IsNullOrEmpty(current) || IsMixed ? [new("None", Icons.Clear, "picker:none", () => TextValue = null)] : [];
        IReadOnlyList<PickerChoice> choices;
        PickerSetup setup;
        switch (Kind)
        {
            case FieldKind.Image:
                var art = _owner.Art;
                choices = [.. art.Images().Select(i => new PickerChoice(i, thumbnail: () => art.Thumbnail(i)))];
                setup = new("Type to find an image", "No images in the project yet", false, false,
                    [new("Paint a New One...", Icons.Palette, "picker:paint", () => PaintImage(fresh: true)), .. none]);
                break;
            case FieldKind.Sound:
                choices = [.. SoundChoices().Select(s => new PickerChoice(s, icon: s.StartsWith("Music/", StringComparison.OrdinalIgnoreCase) ? Icons.Music : Icons.Audio))];
                var mode = IsMusic ? Audio.SoundMakerMode.Music : Audio.SoundMakerMode.Effect;
                setup = new(IsMusic ? "Type to find music" : "Type to find a sound", "No sounds in the project yet", false, false,
                    [new(IsMusic ? "New Music..." : "New Sound...", IsMusic ? Icons.Music : Icons.Audio, "picker:make",
                        () => _owner.Audio.OpenMaker(mode, saved: saved => TextValue = saved)), .. none]);
                break;
            case FieldKind.Entity:
                var inside = _property.EntityName?.Inside == true;
                // Indented like the Hierarchy, so children show under their parents.
                choices = [.. EntityChoices().Select(c => new PickerChoice(c.Entity.Name, depth: c.Depth))];
                setup = new(inside ? "Type to find an entity inside this one" : "Type to find an entity",
                    inside ? "Nothing inside this entity yet" : "No entities in the scene", NamesMany, true, none);
                break;
            case FieldKind.Action:
                choices = [.. ActionNames.Choices(_components[0], _property).Select(c => new PickerChoice(c.Name, detail: c.Detail))];
                setup = new("Type to find something it can do", "What it's done on can't do anything yet: give it a component that can",
                    false, true, none);
                break;
            case FieldKind.Output:
                choices = [.. OutputNames.Choices(_components[0], _property).Select(c => new PickerChoice(c.Name, detail: c.Detail))];
                setup = new("Type to find something it reads", "What it reads knows nothing yet: give it a component that does",
                    false, true, none);
                break;
            case FieldKind.Enum:
                choices = [.. EnumChoices.Select(c => new PickerChoice(c.Value.ToString(), label: c.Label, detail: c.Tip))];
                setup = new("Type to find a choice", "There's nothing to choose", false, false, []);
                break;
            default:
                var kind = _property.Names!;
                var owner = _owner;
                choices = [.. (_owner.Names?.Choices(kind, _components[0]) ?? []).Select(c => new PickerChoice(c.Name, label: c.Label, detail: c.Detail,
                    group: c.Group, preview: IsFont ? () => owner.FontPreview(c.Name, c.Text) : null))];
                setup = new($"Type to find {NameNoun}", kind.Kind switch
                {
                    NameKind.Font => "Fonts can't be listed now: type a font file in Assets or an installed font's name",
                    NameKind.Prefab => "No prefabs in the project yet",
                    NameKind.Scene => "No scenes in the project yet",
                    NameKind.Table => "No tables in the project yet: Data > New Table makes one",
                    NameKind.Row => "Pick a table first, or give it rows in the Tables panel",
                    NameKind.Message => "No messages sent or heard yet: type one to use it",
                    NameKind.Item => "Nothing gives an item yet: give a pickup an Item, or type one",
                    NameKind.Flag => "Nothing sets a flag yet: a trigger's Set Flag or a dialogue's [set] does, or type one",
                    NameKind.Kind when kind.From is { } from => $"There's no {KnownNames.Noun(kind)[(KnownNames.Noun(kind).IndexOf(' ') + 1)..]} in {from} yet: type one to use it",
                    NameKind.Kind => $"Nothing says it's {KnownNames.Noun(kind)} yet: type one to use it",
                    NameKind.Saved => "Nothing keeps a value in the save data yet: give a slider, toggle or choice a Save As, or type one",
                    NameKind.Column => "Pick a table first, or give it columns in the Tables panel",
                    _ => "None in the project yet: type one to use it",
                }, NamesMany, true, none);
                break;
        }

        bool IsPicked(string name) => Kind is FieldKind.Image or FieldKind.Sound
            ? string.Equals(name, current, StringComparison.OrdinalIgnoreCase)
            : Kind == FieldKind.Enum ? !IsMixed && Value?.ToString() == name
            : Names(name);
        return new FieldPicker(setup, choices, Pick, IsPicked, () => IsPickerOpen = false);
    }

    /// <summary>
    /// True when a file from the Assets panel can be dropped on the field: any on a text field, a prefab on a field
    /// naming prefabs, a scene on one naming scenes.
    /// </summary>
    public bool TakesAsset(string path) => Kind switch
    {
        FieldKind.Text => true,
        FieldKind.Name => _property.Names!.Kind switch
        {
            NameKind.Prefab => path.EndsWith(Griff.Projects.Project.PrefabExtension, StringComparison.OrdinalIgnoreCase),
            NameKind.Scene => path.EndsWith(Griff.Projects.Project.SceneExtension, StringComparison.OrdinalIgnoreCase),
            NameKind.Table => path.EndsWith(Griff.Data.TableFile.Extension, StringComparison.OrdinalIgnoreCase),
            NameKind.Font => Griff.Fonts.ProjectFonts.IsFontFile(path),
            _ => false,
        },
        _ => false,
    };

    /// <summary>
    /// A file dropped on the field from the Assets panel: a prefab or scene is named (by name, as the field's list
    /// names it); any other file is set as its path.
    /// </summary>
    public void DropAsset(string path)
    {
        if (IsFont)
            Pick(path.Replace('\\', '/'));
        else if (Kind == FieldKind.Name)
            Pick(System.IO.Path.GetFileNameWithoutExtension(path));
        else
            TextValue = path;
    }

    public bool IsInteger => _property.Type == typeof(int);

    public Array? EnumValues { get; }

    /// <summary>For an enum field: its values to pick from, each named as it reads ("Ping Pong") and explained.</summary>
    public IReadOnlyList<EnumChoice> EnumChoices { get; } = [];

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
        get => Convert.ToDouble(Value, CultureInfo.InvariantCulture);
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

    /// <summary>
    /// Null (nothing picked) when mixed. Takes a value, or a choice's name as it reads or as in code ("Ping Pong" or
    /// "PingPong"), as agents give it.
    /// </summary>
    public object? EnumValue
    {
        get => IsMixed ? null : Value;
        set
        {
            if (value is string name)
            {
                value = EnumChoices.FirstOrDefault(c => c.Label.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    c.Value.ToString().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))?.Value
                    ?? throw new ArgumentException($"There's no {name} choice; the choices are {string.Join(", ", EnumChoices.Select(c => c.Label))}.");
            }

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

    public Brush ColorBrush => IsMixed ? Brushes.Transparent : ColorPicker.BrushOf(Value is Color c ? c : Color.Black);

    private ColorPicker? _colorPicker;
    private long _colorClosedAt;
    private IRelayCommand? _toggleColor;

    /// <summary>
    /// The color field's picker while it's open (null when it's shut): hue and value, alpha, the Sprite Painter's palette
    /// and recent colors. Open it with <see cref="IsColorOpen"/>.
    /// </summary>
    public ColorPicker? ColorPicker
    {
        get => _colorPicker;
        private set => SetProperty(ref _colorPicker, value);
    }

    /// <summary>Whether the color field's picker is open. Shutting it ends the undo step its drags made.</summary>
    public bool IsColorOpen
    {
        get => ColorPicker is not null;
        set
        {
            if (value == IsColorOpen || (value && Kind != FieldKind.Color))
                return;
            if (!value)
            {
                _colorClosedAt = Environment.TickCount64;
                ColorPicker!.Commit();
            }

            ColorPicker = value
                ? new ColorPicker(() => Value is Color c ? c : Color.Black, color => Value = color, color =>
                {
                    _owner.Seal();
                    _owner.RememberColor(color);
                }, () => _owner.RecentColors, () => _owner.Palette, palette => _owner.Palette = palette)
                : null;
            OnPropertyChanged();
        }
    }

    /// <summary>Opens the color picker (clicking the swatch), or shuts it.</summary>
    public IRelayCommand ToggleColorCommand => _toggleColor ??= new RelayCommand(() =>
        // A click on the swatch while the picker is open shuts it (as the mouse goes down outside it).
        IsColorOpen = !IsColorOpen && Environment.TickCount64 - _colorClosedAt > 250);

    /// <summary>
    /// True when a selected copy of a prefab (or something inside one) has this field set differently from its prefab.
    /// Saving the prefab keeps it.
    /// </summary>
    public bool IsOverridden => _components.Any(c => _owner.PrefabComponent(c) is { } prefab && PrefabOverrides.Differs(c, prefab, _property));

    /// <summary>For a changed field: what the prefab has, and how to put it back or make it the prefab's.</summary>
    public string? OverrideTip => _components
        .Select(c => _owner.PrefabComponent(c) is { } prefab && PrefabOverrides.Differs(c, prefab, _property) ? prefab : null)
        .FirstOrDefault(p => p is not null) is { } changed
        ? $"Changed from the prefab, which has {Describe(_property.GetValue(changed))}. Click for Revert and Apply to Prefab."
        : null;

    /// <summary>Sets the field back to the prefab's value on each selected copy that changed it, as one undo step.</summary>
    public void RevertToPrefab()
    {
        if (!IsOverridden)
            return;
        _owner.SetValues(_components, _property, [.. _components.Select(c =>
            _owner.PrefabComponent(c) is { } prefab ? PrefabOverrides.CopyValue(_property.GetValue(prefab)) : _property.GetValue(c))]);
        _owner.Seal();
    }

    /// <summary>
    /// Makes the copy's value the prefab's: saves it into the prefab file, and brings the prefab's other copies up to
    /// date (their own changes stay). Not with mixed values.
    /// </summary>
    public void ApplyToPrefab()
    {
        if (IsOverridden && !IsMixed)
            _owner.ApplyToPrefab(_components, _property);
    }

    /// <summary>Shows Revert and Apply to Prefab in a menu under the mark that asked.</summary>
    public IRelayCommand<FrameworkElement> OverrideMenuCommand => _overrideMenu ??= new RelayCommand<FrameworkElement>(OverrideMenu);

    private IRelayCommand<FrameworkElement>? _overrideMenu;

    private void OverrideMenu(FrameworkElement? anchor)
    {
        if (!IsOverridden)
            return;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var revert = new MenuItem { Header = "Revert to Prefab", Tag = "override:revert", Icon = Icons.Create(Icons.Undo) };
        revert.Click += (_, _) => RevertToPrefab();
        menu.Items.Add(revert);
        var apply = new MenuItem
        {
            Header = "Apply to Prefab",
            Tag = "override:apply",
            Icon = Icons.Create(Icons.Package),
            IsEnabled = !IsMixed,
            ToolTip = "Saves this value into the prefab: every copy that hasn't changed it takes it too",
        };
        apply.Click += (_, _) => ApplyToPrefab();
        menu.Items.Add(apply);
        menu.IsOpen = true;
    }

    private static string Describe(object? value) => value switch
    {
        null or "" => "nothing",
        string text => $"\"{text}\"",
        Color color => color.ToHex(),
        float f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        double d => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        IReadOnlyList<string> lines => lines.Count == 1 ? "1 line" : $"{lines.Count} lines",
        Vector2 or Vector3 or Vector4 or Quaternion => string.Join(", ", ToFloats(value).Select(f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))),
        bool b => b ? "it on" : "it off",
        _ => value.ToString() ?? "",
    };

    public void Refresh()
    {
        _unmatched = null;
        OnPropertyChanged(string.Empty);
        foreach (var axis in Axes)
            axis.Refresh();
        ColorPicker?.Sync();
        Editor?.Refresh();
    }

    // ----- Filtering

    private bool _filteredOut;

    /// <summary>True while the Inspector's filter is hiding it: neither its name nor its component's or subheading's has what's typed.</summary>
    public bool IsFilteredOut
    {
        get => _filteredOut;
        internal set
        {
            if (SetProperty(ref _filteredOut, value))
                OnPropertyChanged(nameof(IsHiddenRow));
        }
    }

    /// <summary>True when its name, as shown or in code, has <paramref name="text"/> in it.</summary>
    public bool Matches(string text) =>
        Label.Contains(text, StringComparison.OrdinalIgnoreCase) || PropertyName.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when its row is hidden: filtered out, or folded under its subheading (a filter shows what matches, folded or
    /// not).
    /// </summary>
    public bool IsHiddenRow => IsFilteredOut || (IsFolded && !_owner.IsFiltering);

    // ----- Vectors

    /// <summary>
    /// True for a Transform's Position, Rotation or Scale shown in 2D: X and Y, and one rotation angle (Z), with the
    /// rest behind the Inspector's 3D switch.
    /// </summary>
    public bool Is2D => !_owner.Transform3D && _components[0] is Transform && Kind == FieldKind.Vector;

    /// <summary>Whether a vector's axis shows: in 2D, a position's or scale's X and Y, and a rotation's Z.</summary>
    internal bool IsAxisShown(int index) => !Is2D || (PropertyName == nameof(Transform.Rotation) ? index == 2 : index < 2);

    /// <summary>
    /// The axes a 2D Transform hides that aren't at their resting value on a selected entity, e.g. "Position Z 2":
    /// they still count, and show in 3D.
    /// </summary>
    internal IEnumerable<string> HiddenAxisValues()
    {
        if (!Is2D)
            yield break;
        var rest = PropertyName == nameof(Transform.Scale) ? 1f : 0f;
        for (var i = 0; i < Axes.Count; i++)
        {
            if (IsAxisShown(i))
                continue;
            var values = _components.Select(c => ToFloats(_property.GetValue(c))[i]).Where(v => v != rest).Distinct().ToList();
            if (values.Count > 0)
                yield return $"{Label} {AxisNames[i]} {(values.Count == 1 ? values[0].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "mixed")}";
        }
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

    // A rotation (Quaternion) shows as the angles in degrees about X, Y and Z it turns by, as a Transform's Rotation does,
    // rather than its four numbers, which nobody types.
    private static float[] ToFloats(object? value) => value switch
    {
        Vector2 v => [v.X, v.Y],
        Vector3 v => [v.X, v.Y, v.Z],
        Vector4 v => [v.X, v.Y, v.Z, v.W],
        Quaternion q when Transform.EulerDegrees(q) is var angles => [Round(angles.X), Round(angles.Y), Round(angles.Z)],
        _ => [],
    };

    // Angles back from a quaternion on thousandths, so 90 doesn't read 89.99999.
    private static float Round(float degrees) => MathF.Round(degrees, 3);

    private static object FromFloats(Type type, float[] v) =>
        type == typeof(Vector2) ? new Vector2(v[0], v[1])
        : type == typeof(Vector3) ? new Vector3(v[0], v[1], v[2])
        : type == typeof(Vector4) ? new Vector4(v[0], v[1], v[2], v[3])
        : Transform.FromEulerDegrees(new Vector3(v[0], v[1], v[2]));
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

/// <summary>One value of an enum field: as it reads in the list ("Ping Pong"), and what it means (its documentation).</summary>
public sealed class EnumChoice(Enum value)
{
    public Enum Value { get; } = value;

    public string Label { get; } = InspectorPanel.Humanize(value.ToString());

    public string? Tip { get; } = Griff.Editing.DocComments.Summary(value) is { IsEmpty: false } doc ? doc.PlainText : null;

    public override string ToString() => Label;
}

/// <summary>One component (X, Y, Z or W) of a vector property.</summary>
public sealed class AxisField(PropertyField owner, int index, string label) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>Whether it shows: a 2D Transform hides Z, and a rotation's X and Y (see <see cref="PropertyField.Is2D"/>).</summary>
    public bool IsShown => owner.IsAxisShown(index);

    /// <summary>The letter on its box; none for a 2D rotation's one angle, which the field's name drags.</summary>
    public string? ShownLabel => owner.Is2D && owner.PropertyName == nameof(Transform.Rotation) ? null : Label;

    public double Value
    {
        get => owner.GetAxis(index);
        set => owner.SetAxis(index, (float)value);
    }

    /// <summary>True when the selected entities differ on this axis.</summary>
    public bool IsMixed => owner.IsAxisMixed(index);

    public void Refresh() => OnPropertyChanged(string.Empty);
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

    /// <summary>The editor's pictures, for image fields: previews, the project's images and the Sprite Painter.</summary>
    Griff.Editor.Features.Art.EditorArt Art { get; }

    /// <summary>The editor's sounds, for sound fields: previews, the project's sounds and the Sound Maker.</summary>
    Griff.Editor.Features.Audio.EditorAudio Audio { get; }

    /// <summary>The project's prefabs' names: an entity field naming one isn't flagged as matching nothing.</summary>
    IReadOnlyCollection<string> PrefabNames => [];

    /// <summary>True for a prefab on its own: only names of things inside a field's entity can be checked.</summary>
    bool InPrefab => false;

    /// <summary>
    /// The project's prefabs, scenes, input actions, layers and messages, which fields naming them list and are checked
    /// against; null when they can't be known (nothing is flagged then).
    /// </summary>
    KnownNames? Names => null;

    /// <summary>For a component of a placed prefab copy (or of something inside one): the prefab's own, or null.</summary>
    Component? PrefabComponent(Component component) => null;

    /// <summary>
    /// For the font picker: <paramref name="text"/> drawn in <paramref name="font"/> (as a Font names it), white with
    /// coverage as alpha; null when it can't be drawn.
    /// </summary>
    System.Windows.Media.ImageSource? FontPreview(string? font, string text) => null;

    /// <summary>Saves the components' value of the property into their prefabs, updating the prefabs' other copies.</summary>
    void ApplyToPrefab(IReadOnlyList<Component> components, PropertyDescriptor property)
    {
    }

    /// <summary>Colors picked lately in color fields' pickers, most recent first.</summary>
    IReadOnlyList<Color> RecentColors => [];

    /// <summary>Puts a color picked in a color field's picker first in <see cref="RecentColors"/>.</summary>
    void RememberColor(Color color)
    {
    }

    /// <summary>The palette color pickers offer: the Sprite Painter's, which choosing another in a picker changes too.</summary>
    Griff.Art.Palette Palette
    {
        get => Griff.Art.Palette.Jam;
        set { }
    }

    /// <summary>Whether Transforms show in 3D (X, Y and Z) rather than 2D (X and Y, and one rotation angle).</summary>
    bool Transform3D => true;

    /// <summary>Runs one of the editor's commands by its id, as a field's editor does to open what edits it more fully ("Art.Clips").</summary>
    void RunCommand(string id)
    {
    }

    /// <summary>True while a filter narrows the fields shown: folded ones that match show anyway.</summary>
    bool IsFiltering => false;
}
