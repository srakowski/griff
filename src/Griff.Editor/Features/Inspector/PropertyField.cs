using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Editor.Themes;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Transform = Griff.Scenes.Transform;

namespace Griff.Editor.Features.Inspector;

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

    /// <summary>
    /// The names an entity field holds that several entities answer to where it needs one (see
    /// <see cref="EntityReferences.Ambiguous"/>): flagged like a name that matches nothing.
    /// </summary>
    public IReadOnlyList<string> AmbiguousNames => _ambiguous ??= IsMixed || IsFaded || Kind != FieldKind.Entity ? [] : EntityReferences.Ambiguous(new(_components[0], _property));

    private IReadOnlyList<string>? _ambiguous;

    public bool IsUnmatched => UnmatchedNames.Count > 0 || AmbiguousNames.Count > 0;

    /// <summary>
    /// Says which names match nothing, e.g. "Nothing in the scene is called "Playr"" or "There's no prefab called
    /// "Bulet" in the project".
    /// </summary>
    public string? UnmatchedTip => !IsUnmatched ? null
        : UnmatchedNames.Count == 0 ? EntityReferences.AmbiguousProblem(new(_components[0], _property), AmbiguousNames)
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

    /// <summary>Why Revert to Prefab can't run on it (nothing changed from the prefab), or null when it can.</summary>
    public string? WhyNotRevert => IsOverridden ? null : $"{Label} is as its prefab has it.";

    /// <summary>Sets the field back to the prefab's value on each selected copy that changed it, as one undo step.</summary>
    internal void RevertToPrefab()
    {
        if (WhyNotRevert is { } why)
            throw new InvalidOperationException(why);
        _owner.SetValues(_components, _property, [.. _components.Select(c =>
            _owner.PrefabComponent(c) is { } prefab ? PrefabOverrides.CopyValue(_property.GetValue(prefab)) : _property.GetValue(c))]);
        _owner.Seal();
    }

    /// <summary>Why Apply to Prefab can't run on it (nothing changed, or the copies differ), or null when it can.</summary>
    public string? WhyNotApply => WhyNotRevert ?? (IsMixed ? $"The selected copies have different {Label}s: select one to apply its value." : null);

    /// <summary>
    /// Makes the copy's value the prefab's: saves it into the prefab file, and brings the prefab's other copies up to
    /// date (their own changes stay). Not with mixed values.
    /// </summary>
    internal void ApplyToPrefab()
    {
        if (WhyNotApply is { } why)
            throw new InvalidOperationException(why);
        _owner.ApplyToPrefab(_components, _property);
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
        _ambiguous = null;
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
