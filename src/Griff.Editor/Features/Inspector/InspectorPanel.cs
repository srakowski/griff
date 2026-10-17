using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Media;
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
    private readonly IProjectSession _projectSession;
    private readonly ISceneDocuments _sceneDocuments;
    private readonly ISelection _selection;
    private readonly Scripts.ScriptEditors _scripts;
    private readonly Core.Dialogs.IDialogService _dialogs;
    private readonly Core.Status.IStatusService _status;
    private readonly Art.EditorArt _art;
    private readonly Audio.EditorAudio _audio;
    private Core.Settings.ISettingsStore _settings;
    // For the selection: each component of a prefab copy (or of something inside one) and the prefab's own, the
    // components a copy added, and the prefabs they were found from (read through PrefabFiles, which reads a file again
    // when it changes), by file.
    private readonly Dictionary<Component, Component> _prefabParts = [];
    private readonly HashSet<Component> _addedToCopies = [];
    private readonly Dictionary<string, Entity?> _prefabs = new(StringComparer.OrdinalIgnoreCase);
    private SceneDocument? _document;
    private EntityHeader? _header;
    private string? _hiddenNote;
    private IReadOnlyList<Entity> _entities = [];
    private int[] _componentCounts = [];

    private readonly PrefabFiles _prefabFiles;
    private readonly ProblemLog? _runProblems;
    private readonly Core.Workspace.IPlaySession? _playSession;

    public InspectorPanel(ISceneDocuments sceneDocuments, IScriptSession scriptSession, IProjectSession projectSession, ISelection selection, Scripts.ScriptEditors scripts, Art.EditorArt art,
        Audio.EditorAudio audio, Core.Dialogs.IDialogService dialogs, Core.Status.IStatusService status, Core.Settings.ISettingsStore settings,
        Lazy<Core.Commands.CommandRegistry> commands, PrefabFiles prefabFiles, Core.Workspace.IPlaySession? playSession = null)
    {
        if (playSession is not null)
        {
            // What the running game finds wrong in its data is flagged on the component it was found on.
            _playSession = playSession;
            _runProblems = playSession.Problems;
            _runProblems.Reported += _ => RunProblemsChanged();
            playSession.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Core.Workspace.IPlaySession.IsPlaying))
                    RunProblemsChanged();
            };
        }

        _prefabFiles = prefabFiles;
        _commands = commands;
        _settings = settings;
        _art = art;
        _audio = audio;
        _projectSession = projectSession;
        _sceneDocuments = sceneDocuments;
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
        sceneDocuments.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ISceneDocuments.Scene))
                Track(sceneDocuments.Scene);
        };
        scriptSession.ScriptsReloaded += (_, _) => Rebuild();
        Track(sceneDocuments.Scene);
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

    /// <summary>
    /// The section showing the selected entities' <paramref name="name"/> (as it's titled, "Box Collider", or by its type,
    /// "BoxCollider"), for a command that takes a component; says what there is when there's no such one.
    /// </summary>
    public ComponentSection Section(string name)
    {
        static string Key(string text) => text.Replace(" ", "", StringComparison.Ordinal);
        if (!HasEntity)
            throw new InvalidOperationException("Nothing is selected: select the entities first, or name one with \"entity\".");
        return Components.FirstOrDefault(c => string.Equals(Key(c.Title), Key(name), StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.TypeKey, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"The selection has no {name}; it has {string.Join(", ", Components.Select(c => c.Title))}.");
    }

    /// <summary>The field "Component.Field" ("Pickup.Amount") of the selected entities, by the names the Inspector shows or the property's.</summary>
    public PropertyField Field(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0)
            throw new ArgumentException($"Name the field as \"Component.Field\" (\"Pickup.Amount\"), not \"{name}\".");
        var section = Section(name[..dot]);
        var field = name[(dot + 1)..];
        return section.Properties.FirstOrDefault(p => string.Equals(p.PropertyName, field, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Label.Replace(" ", "", StringComparison.Ordinal), field.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"{section.Title} has no field {field}.");
    }

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
        _projectSession.Components.All.Where(d => d.Type != typeof(Transform)).OrderBy(d => d.DisplayName);

    /// <summary>
    /// Adds a component, and any components it requires that an entity is missing (a Patrol brings a Body and a
    /// BoxCollider), to every selected entity, as one undo step.
    /// </summary>
    public void AddComponent(ComponentDescriptor descriptor) =>
        ExecuteOnEach($"Add {descriptor.DisplayName}", entity => Griff.Editing.ComponentCatalog.AddWithRequirements(_projectSession.Components, descriptor, entity));

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
    public ComponentCatalogViewModel CreateCatalog() => new(_projectSession.Components, _selection.Entity);

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
        if (Griff.Editing.ComponentCatalog.Satisfier(_projectSession.Components, requirement.Type) is not { } satisfier)
            return;
        var edits = components.Select(c => c.Entity).OfType<Entity>().Where(e => !requirement.IsMetBy(e))
            .Select(e => Griff.Editing.ComponentCatalog.AddWithRequirements(_projectSession.Components, satisfier, e)).ToList();
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
            .SelectMany(c => Griff.Editing.ComponentCatalog.Dependents(_projectSession.Components, c).Select(d => (Entity: c.Entity!, Dependent: d)))
            .ToList();
        if (dependents.Count == 0)
            return null;
        var names = dependents.Select(d => Humanize(_projectSession.Components.Get(d.Dependent.GetType()).DisplayName)).Distinct().ToList();
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

    // Asked for when a field's editor runs a command: the commands that open panels are made after the panels are.
    private readonly Lazy<Core.Commands.CommandRegistry> _commands;

    /// <summary>Runs one of the editor's commands, as a field's editor does (the Clip Editor's opens it).</summary>
    public void RunCommand<TCommand>() where TCommand : Core.Commands.EditorCommand
    {
        _ = _commands.Value.Get<TCommand>().ExecuteAsync();
    }

    /// <summary>Runs the command that edits fields marked <paramref name="attribute"/> more fully (see <see cref="IEditsFields"/>), if there is one.</summary>
    public void EditFully(FieldEditorAttribute attribute)
    {
        if (_commands.Value.All.OfType<IEditsFields>().FirstOrDefault(c => c.Edits.IsInstanceOfType(attribute)) is Core.Commands.EditorCommand command)
            _ = command.ExecuteAsync();
    }

    /// <summary>Renames an entity, and the fields in the scene that name it, as one undo step.</summary>
    internal void Rename(Entity entity, string name) => Execute(RenameEntityEdit.WithReferences(entity, name, _projectSession.Components));

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
                _names = _projectSession.Names;
            }

            return _names;
        }
    }

    /// <summary>The project's prefabs' names, read as the selection changes: fields can name them as well as entities.</summary>
    public IReadOnlyCollection<string> PrefabNames { get; private set; } = [];

    private readonly FontPreviews _fontPreviews = new();

    /// <summary>The font picker's preview of a font: its name drawn in it, as the game draws it.</summary>
    public ImageSource? FontPreview(string? font, string text) => _fontPreviews.Preview(_projectSession.Project?.AssetsPath, font, text);

    /// <summary>
    /// True while a prefab's tab is showing: fields naming things outside the entity they're on aren't flagged, as they
    /// name things in whatever scene the prefab is placed in.
    /// </summary>
    public bool InPrefab => _sceneDocuments.Scene?.IsPrefab == true;

    /// <summary>Whether the prefab file at <paramref name="assetPath"/> (in Assets) is there.</summary>
    internal bool PrefabExists(string assetPath) =>
        _projectSession.Project is { } project && System.IO.File.Exists(System.IO.Path.Combine(project.AssetsPath, assetPath));

    /// <summary>The prefab's own component that <paramref name="component"/>, on a placed copy or inside one, was made from.</summary>
    public Component? PrefabComponent(Component component) => _prefabParts.GetValueOrDefault(component);

    /// <summary>True for a component on a placed copy (or inside one) that its prefab doesn't have.</summary>
    public bool IsAddedToCopy(Component component) => _addedToCopies.Contains(component);

    /// <summary>A placed copy's prefab, as an entity made from its file (read again when the file changes), or null.</summary>
    internal Entity? PrefabOf(Entity copy)
    {
        if (copy.Prefab is not { } link || _projectSession.Project?.FindPrefab(link) is not { } path)
            return null;
        return _prefabs[path] = _prefabFiles.Read(path);
    }

    /// <summary>What a placed copy changes from its prefab (nothing if it isn't one, or its prefab can't be read).</summary>
    internal IReadOnlyList<PrefabOverride> OverridesOf(Entity copy) =>
        PrefabOf(copy) is { } prefab ? PrefabOverrides.Find(copy, prefab, _projectSession.Components) : [];

    /// <summary>
    /// Sets the field in each selected copy's prefab to the copy's value, and brings the prefab's other copies up to
    /// date (keeping what they changed themselves).
    /// </summary>
    public void ApplyToPrefab(IReadOnlyList<Component> components, PropertyDescriptor property)
    {
        var update = _prefabFiles.ApplyField(components, property);
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
        var edits = components.Select(c => ArtEdits.UseSheet(_projectSession.Components, c, property.Name, image, layout)).OfType<IEdit>().ToList();
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
    private bool PrefabsChanged() => _prefabs.Any(p => !ReferenceEquals(_prefabFiles.Read(p.Key), p.Value));

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

        PrefabNames = _projectSession.Project?.FindPrefabs().Select(System.IO.Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet() ?? [];
        foreach (var entity in entities)
            MatchPrefab(entity);

        foreach (var (components, lacking) in Sections(entities))
            Components.Add(new ComponentSection(components, this, _projectSession.Components, lacking));
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
        if (ComponentEdits.Copy(_projectSession.Components, component) is not { } values)
            return;
        Copied = values;
        _status.Show($"Copied {Humanize(_projectSession.Components.Get(values.Type).DisplayName)}'s values.");
    }

    /// <summary>Sets the components to the copied values (they must be of the copied type), as one undo step.</summary>
    internal void Paste(IReadOnlyList<Component> components)
    {
        if (Copied is { } values && ComponentEdits.Paste(_projectSession.Components, values, components) is { } edit)
            Execute(edit);
        Seal();
    }

    /// <summary>Sets the components back to what a new one starts with, as one undo step.</summary>
    internal void Reset(IReadOnlyList<Component> components)
    {
        if (ComponentEdits.Reset(_projectSession.Components, components) is { } edit)
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
        if (ComponentEdits.AddCopies(_projectSession.Components, source, lacking) is { } edit)
            Execute(edit);
    }

    /// <summary>The words of what the running game found wrong on <paramref name="component"/>, each once.</summary>
    internal IReadOnlyList<string> RunProblemsOf(Component component) => _runProblems is null || _playSession is not { IsPlaying: true } ? [] : [.. _runProblems.On(component).Select(p => p.Message)];

    private void RunProblemsChanged()
    {
        void Notify()
        {
            foreach (var section in Components)
                section.RefreshRunProblems();
        }

        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(Notify);
        else
            Notify();
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
    /// Opens the section of every component the selection shows, and every subheading in them (the Expand All button by
    /// the filter). As folding one does, it holds for those components on every entity, kept in the user's settings.
    /// </summary>
    public void ExpandAll() => SetAllCollapsed(false);

    /// <summary>
    /// Folds shut the section of every component the selection shows, and every subheading in them (the Collapse All
    /// button by the filter), leaving their headers. It holds for those components on every entity, kept in the user's
    /// settings.
    /// </summary>
    public void CollapseAll() => SetAllCollapsed(true);

    private void SetAllCollapsed(bool collapsed)
    {
        var list = _settings.Settings.CollapsedComponents;
        var changed = false;
        foreach (var key in Components.SelectMany(s => s.Groups.Select(g => g.Key).Prepend(s.TypeKey)))
        {
            if (list.Contains(key) == collapsed)
                continue;
            if (collapsed)
                list.Add(key);
            else
                list.Remove(key);
            changed = true;
        }

        if (!changed)
            return;
        _settings.Save();
        foreach (var section in Components)
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
