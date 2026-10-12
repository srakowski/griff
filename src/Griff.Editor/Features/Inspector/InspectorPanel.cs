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
/// one undo step, and values that differ show as mixed. On placed copies of prefabs, fields set differently from the
/// prefab are marked, each with Revert and Apply to Prefab. Each field's name explains it (its documentation as a
/// tooltip), and a section folded shut stays shut for that component on every entity, and next time.
/// </summary>
public sealed partial class InspectorPanel : ToolPanel, IPropertyHost
{
    private readonly IWorkspace _workspace;
    private readonly ISelection _selection;
    private readonly Scripts.ScriptEditors _scripts;
    private readonly Core.Dialogs.IDialogService _dialogs;
    private readonly Core.Status.IStatusService _status;
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
        Core.Dialogs.IDialogService dialogs, Core.Status.IStatusService status, Core.Settings.ISettingsStore settings)
    {
        _settings = settings;
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

    internal void Execute(IEdit edit) => _document?.Execute(edit);

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
        _namesRead = false;
        if (ChangesComponents(e.Edit) || PrefabsChanged() || Components.Any(c => c.IsOutOfDate))
            Rebuild();
        else
            Refresh();
    }

    // Show live values while the game runs. Components added or removed by gameplay rebuild the sections.
    private void OnRuntimeTicked(object? sender, EventArgs e)
    {
        if (_entities.Where((entity, i) => entity.Components.Count != _componentCounts[i]).Any() || PrefabsChanged())
            Rebuild();
        else
            Refresh();
    }

    private static bool ChangesComponents(IEdit edit) => edit switch
    {
        AddComponentEdit or RemoveComponentEdit or UpdatePrefabCopyEdit => true,
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

        foreach (var components in Shared(entities))
            Components.Add(new ComponentSection(components, this, _workspace.Components));
        if (entities.Any(e => e.Components.Count != Components.Count))
            HiddenNote = $"Only the components all {entities.Count} have are shown.";
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
        _overrideCount = null;
        Header?.Refresh();
        foreach (var section in Components)
            section.Refresh();
    }

    private int? _overrideCount;

    /// <summary>How many things the one selected copy changes from its prefab.</summary>
    internal int OverrideCount(Entity copy) => _overrideCount ??= OverridesOf(copy).Count;

    /// <summary>True when the component's section is folded shut (on every entity), by its type's saved name.</summary>
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
        foreach (var section in Components.Where(s => s.TypeKey == type))
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
        Properties = Used().Select(p => new PropertyField(components, p, owner)).ToList();
        SetupNote = component.SetupNote();
        IsAddedToCopy = components.Any(owner.IsAddedToCopy);
        Needs = descriptor.Requires
            .Select(r => (Link: r, Lacking: components.Count(c => c.Entity is { } e && !r.IsMetBy(e))))
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

    /// <summary>True when <paramref name="name"/> is the component's name, as shown or as its type's.</summary>
    public bool IsNamed(string name) =>
        Title.Equals(name, StringComparison.OrdinalIgnoreCase) || TypeName.Equals(name, StringComparison.OrdinalIgnoreCase);

    public bool IsMissing { get; }

    public bool CanRemove => Component is not Transform;

    /// <summary>True for components from the project's scripts, which can be opened in the script editor.</summary>
    public bool IsScript => Component is not MissingComponent && Component.GetType().Assembly != typeof(Component).Assembly;

    public IRelayCommand EditScriptCommand { get; }

    public IReadOnlyList<PropertyField> Properties { get; }

    public IRelayCommand RemoveCommand { get; }

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
        foreach (var property in Properties)
            property.Refresh();
    }

    // From every selected entity, as one undo step; asks first when something needs it.
    private void Remove() => _owner.RemoveComponents(_components, Title);
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
    /// The name of a prefab, scene, input action, collision layer or message (or several, separated by commas): text,
    /// with a button to pick one of the project's, flagged when it matches none of them.
    /// </summary>
    Name,
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
            : type == typeof(string) && IsSoundName(property.Name) ? FieldKind.Sound
            : type == typeof(string) && IsImageName(property.Name) ? FieldKind.Image
            : property.EntityName is not null ? FieldKind.Entity
            : property.Names is not null ? FieldKind.Name
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

        if (Kind == FieldKind.Vector)
        {
            Axes = Enumerable.Range(0, ToFloats(Value).Length)
                .Select(i => new AxisField(this, i, AxisNames[i]))
                .ToList();
        }
    }

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
            var tip = string.Join("\n\n", new[] { OverrideTip, Summary, Kind == FieldKind.Number ? DragTip : null }.OfType<string>());
            return tip.Length > 0 ? tip : null;
        }
    }

    private const string DragTip = "Drag the name sideways to change it (Shift: fine, Ctrl: coarse), or press Up and Down while typing.";

    /// <summary>The property's name in code ("TileSize").</summary>
    public string PropertyName => _property.Name;

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

    private IRelayCommand? _paintImage;

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
    public IReadOnlyList<string> UnmatchedNames => _unmatched ??= IsMixed ? [] : Kind switch
    {
        FieldKind.Entity => EntityReferences.Unmatched(new(_components[0], _property), _owner.PrefabNames, _owner.InPrefab),
        FieldKind.Name => _owner.Names?.Unmatched(_components[0], _property) ?? [],
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
        : $"Nothing {(_property.EntityName!.Inside ? $"inside {_components[0].Entity?.Name}" : "in the scene")} is called " +
          string.Join(" or ", UnmatchedNames.Select(n => $"\"{n}\""));

    /// <summary>True when the field names <paramref name="name"/>: on every selected entity, when there are several.</summary>
    public bool Names(string name) => _components.All(c => SplitNames(_property.GetValue(c)).Contains(name, NameComparer));

    // Entities and messages match exactly, as the game matches them; prefabs, scenes, actions and layers whatever their capitals.
    private StringComparer NameComparer =>
        _property.Names is { Kind: not NameKind.Message } ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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
                Audio.EditorAudio.Current?.Preview(name);
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

    /// <summary>True for a field with a list to pick from: images, sounds, and names of entities, prefabs, scenes, input actions, layers and messages.</summary>
    public bool HasPicker => Kind is FieldKind.Image or FieldKind.Sound or FieldKind.Entity or FieldKind.Name;

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
        _ => _property.Names?.Kind switch
        {
            NameKind.Message => "Choose a message the project sends or listens for, or type a new one",
            NameKind.Layer => "Choose a collision layer something is on, or type a new one",
            NameKind.Action => "Choose an input action: every game's own, or one from Game > Controls",
            NameKind.Prefab => "Choose a prefab from the project, or drag one here from the Assets panel",
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
                var art = Art.EditorArt.Current;
                choices = [.. (art?.Images() ?? []).Select(i => new PickerChoice(i, thumbnail: () => art?.Thumbnail(i)))];
                setup = new("Type to find an image", "No images in the project yet", false, false,
                    [new("Paint a New One...", Icons.Palette, "picker:paint", () => PaintImage(fresh: true)), .. none]);
                break;
            case FieldKind.Sound:
                choices = [.. SoundChoices().Select(s => new PickerChoice(s, icon: s.StartsWith("Music/", StringComparison.OrdinalIgnoreCase) ? Icons.Music : Icons.Audio))];
                var mode = IsMusic ? Audio.SoundMakerMode.Music : Audio.SoundMakerMode.Effect;
                setup = new(IsMusic ? "Type to find music" : "Type to find a sound", "No sounds in the project yet", false, false,
                    [new(IsMusic ? "New Music..." : "New Sound...", IsMusic ? Icons.Music : Icons.Audio, "picker:make",
                        () => Audio.EditorAudio.Current?.OpenMaker(mode, saved: saved => TextValue = saved)), .. none]);
                break;
            case FieldKind.Entity:
                var inside = _property.EntityName?.Inside == true;
                // Indented like the Hierarchy, so children show under their parents.
                choices = [.. EntityChoices().Select(c => new PickerChoice(c.Entity.Name, depth: c.Depth))];
                setup = new(inside ? "Type to find an entity inside this one" : "Type to find an entity",
                    inside ? "Nothing inside this entity yet" : "No entities in the scene", NamesMany, true, none);
                break;
            default:
                var kind = _property.Names!;
                choices = [.. (_owner.Names?.Choices(kind) ?? []).Select(c => new PickerChoice(c.Name, detail: c.Detail))];
                setup = new($"Type to find {NameNoun}", kind.Kind switch
                {
                    NameKind.Prefab => "No prefabs in the project yet",
                    NameKind.Scene => "No scenes in the project yet",
                    NameKind.Message => "No messages sent or heard yet: type one to use it",
                    _ => "None in the project yet: type one to use it",
                }, NamesMany, true, none);
                break;
        }

        bool IsPicked(string name) => Kind is FieldKind.Image or FieldKind.Sound
            ? string.Equals(name, current, StringComparison.OrdinalIgnoreCase)
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
        if (Kind == FieldKind.Name)
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

    /// <summary>True for a prefab on its own: only names of things inside a field's entity can be checked.</summary>
    bool InPrefab => false;

    /// <summary>
    /// The project's prefabs, scenes, input actions, layers and messages, which fields naming them list and are checked
    /// against; null when they can't be known (nothing is flagged then).
    /// </summary>
    KnownNames? Names => null;

    /// <summary>For a component of a placed prefab copy (or of something inside one): the prefab's own, or null.</summary>
    Component? PrefabComponent(Component component) => null;

    /// <summary>Saves the components' value of the property into their prefabs, updating the prefabs' other copies.</summary>
    void ApplyToPrefab(IReadOnlyList<Component> components, PropertyDescriptor property)
    {
    }
}
