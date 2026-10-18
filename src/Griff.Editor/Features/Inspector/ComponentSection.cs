using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Transform = Griff.Scenes.Transform;

namespace Griff.Editor.Features.Inspector;

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

    /// <summary>What the running game found wrong on this component (a prefab that won't load, a name that finds nothing), one per line; null when nothing.</summary>
    public string? RunProblems => _components.SelectMany(_owner.RunProblemsOf).Distinct().ToList() is { Count: > 0 } lines ? string.Join("\n", lines) : null;

    /// <summary>True while <see cref="RunProblems"/> says something.</summary>
    public bool HasRunProblems => RunProblems is not null;

    internal void RefreshRunProblems()
    {
        OnPropertyChanged(nameof(RunProblems));
        OnPropertyChanged(nameof(HasRunProblems));
    }

    public bool CanRemove => Component is not Transform;

    /// <summary>True for components from the project's scripts, which can be opened in the script editor.</summary>
    public bool IsScript => Component is not MissingComponent && !ModuleAssemblies.IsGriffs(Component.GetType());

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

    // What a component's menu does (Reset to Defaults, Copy and Paste Values, Move Up and Down, Add to All, Remove
    // Component) is a command each (ComponentCommands), given this section; these are what they run, each saying why it
    // can't first (null when it can), so a greyed menu item and an agent that tried are told the same.

    /// <summary>Why Add to All can't run: every selected entity has it already. Null when it can.</summary>
    public string? WhyNotAddToAll => IsPartial ? null : $"Every selected entity has {Title} already.";

    /// <summary>
    /// Adds the component to each selected entity that doesn't have it, as a copy of the one shown (its values too, and
    /// with what it needs), as one undo step.
    /// </summary>
    internal void AddToAll()
    {
        Refuse(WhyNotAddToAll);
        _owner.AddCopies(Component, Lacking);
    }

    /// <summary>Whether its values can be reset and copied: not for a component whose type is missing, or only some have.</summary>
    public bool HasValues => _descriptor is not null && !IsPartial;

    /// <summary>Why its values can't be reset, copied or pasted, or null when they can.</summary>
    public string? WhyNotValues =>
        IsPartial ? $"Only some of the selection have {Title}: select those, or Add to All first."
        : !HasValues ? $"{Title}'s type couldn't be found, so its values can't be changed."
        : null;

    /// <summary>Sets every value back to what a new one starts with (on each selected entity), as one undo step.</summary>
    internal void ResetToDefaults()
    {
        Refuse(WhyNotValues);
        _owner.Reset(_components);
    }

    /// <summary>Copies its values (the primary entity's), for Paste Values on another of its type.</summary>
    internal void CopyValues()
    {
        Refuse(WhyNotValues);
        _owner.Copy(Component);
    }

    /// <summary>True when values copied from a component of its type are waiting to be pasted.</summary>
    public bool CanPaste => WhyNotPaste is null;

    /// <summary>Why Paste Values can't run: nothing copied, or values of another type. Null when it can.</summary>
    public string? WhyNotPaste =>
        WhyNotValues ?? (_owner.Copied is not { } copied ? "Nothing's been copied yet: Copy Values on a component first."
        : copied.Type != Component.GetType() ? $"The copied values are {ComponentCatalog.Article(InspectorPanel.Humanize(copied.TypeName))}'s, not {ComponentCatalog.Article(Title)}'s."
        : null);

    /// <summary>What Paste Values says on its menu: "Paste Sprite Values" with a Sprite's copied.</summary>
    public string PasteText => _owner.Copied is { } copied ? $"Paste {InspectorPanel.Humanize(copied.TypeName)} Values" : "Paste Values";

    /// <summary>Sets the copied values on it (on each selected entity), as one undo step.</summary>
    internal void PasteValues()
    {
        Refuse(WhyNotPaste);
        _owner.Paste(_components);
    }

    /// <summary>True when it isn't the first component under the Transform (on one of the selected entities).</summary>
    public bool CanMoveUp => WhyNotMove(-1) is null;

    /// <summary>True when it isn't the last component (on one of the selected entities).</summary>
    public bool CanMoveDown => WhyNotMove(1) is null;

    /// <summary>Why it can't move up (<paramref name="offset"/> -1) or down (1) its entity's list, or null when it can.</summary>
    public string? WhyNotMove(int offset) =>
        IsPartial ? $"Only some of the selection have {Title}: select those to move it."
        : Component is Transform ? "The Transform stays first."
        : !ComponentEdits.CanMove(_components, offset) ? $"{Title} is already {(offset < 0 ? "first, under the Transform" : "last")}."
        : null;

    /// <summary>Moves it one place up (<paramref name="offset"/> -1) or down (1) its entity's list of components (each selected entity's), as one undo step.</summary>
    internal void Move(int offset)
    {
        Refuse(WhyNotMove(offset));
        _owner.Move(_components, offset);
    }

    /// <summary>Why it can't be removed (the Transform), or null when it can.</summary>
    public string? WhyNotRemove => CanRemove ? null : "The Transform stays: every entity has one.";

    /// <summary>Removes it from every selected entity that has it, as one undo step; asks first when something needs it.</summary>
    internal void Remove()
    {
        Refuse(WhyNotRemove);
        _owner.RemoveComponents(_components, Title);
    }

    private static void Refuse(string? why)
    {
        if (why is not null)
            throw new InvalidOperationException(why);
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

    public void Refresh() => Refresh(live: false);

    /// <summary>Shows the live values while the game runs, leaving text fields that haven't changed as they are.</summary>
    public void RefreshLive() => Refresh(live: true);

    private void Refresh(bool live)
    {
        RefreshRunProblems();
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(ShowsFields));
        foreach (var group in Groups)
            group.Refresh();
        foreach (var property in Properties)
        {
            if (live)
                property.RefreshLive();
            else
                property.Refresh();
        }
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

}
