using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// The component catalog's state: every component by category, searchable, with the selected one's documentation, what
/// it needs and what it works with, checked against the entity it would be added to.
/// </summary>
public sealed class ComponentCatalogViewModel : ObservableObject
{
    private readonly ComponentRegistry _registry;
    private string _search = "";
    private CatalogItem? _selected;
    private CatalogDetails? _details;
    private Entity? _entity;

    public ComponentCatalogViewModel(ComponentRegistry registry, Entity? entity)
    {
        _registry = registry;
        _entity = entity;
        Items = Griff.Editing.ComponentCatalog.Build(registry).Select(e => new CatalogItem(e)).ToList();
        View = new ListCollectionView(Items.ToList()) { Filter = o => Matches((CatalogItem)o) };
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CatalogItem.Category)));
        NavigateCommand = new RelayCommand<Type>(Navigate);
        Selected = Items.FirstOrDefault();
    }

    /// <summary>The entity components are added to (the selected one), or null when nothing is selected.</summary>
    public Entity? Entity
    {
        get => _entity;
        set
        {
            _entity = value;
            Refresh();
        }
    }

    public IReadOnlyList<CatalogItem> Items { get; }

    /// <summary>The items grouped by category and filtered by <see cref="Search"/>.</summary>
    public ListCollectionView View { get; }

    /// <summary>Follows a link to another component in the docs or the dependency lists.</summary>
    public IRelayCommand<Type> NavigateCommand { get; }

    public string Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value))
                return;
            View.Refresh();
            OnPropertyChanged(nameof(HasResults));
            if (_search.Trim().Length > 0)
                Selected = BestMatch();
            else if (Selected is null || !Matches(Selected))
                Selected = View.Cast<CatalogItem>().FirstOrDefault();
        }
    }

    // What's typed most likely names: the component called that, then one whose name starts with it, then one whose name
    // has it, then the first listed (it matched on its description).
    private CatalogItem? BestMatch()
    {
        var typed = _search.Replace(" ", "");
        var shown = View.Cast<CatalogItem>().ToList();
        return shown.FirstOrDefault(i => i.Name.Equals(typed, StringComparison.OrdinalIgnoreCase))
            ?? shown.FirstOrDefault(i => i.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            ?? shown.FirstOrDefault(i => i.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            ?? shown.FirstOrDefault();
    }

    /// <summary>Selects the item <paramref name="by"/> rows down the list as it shows (up when negative), stopping at its ends.</summary>
    public void MoveSelection(int by)
    {
        var shown = View.Cast<CatalogItem>().ToList();
        if (shown.Count == 0)
            return;
        var at = Selected is null ? -1 : shown.IndexOf(Selected);
        Selected = shown[Math.Clamp(at < 0 ? (by > 0 ? 0 : shown.Count - 1) : at + by, 0, shown.Count - 1)];
    }

    public bool HasResults => !View.IsEmpty;

    public CatalogItem? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                Refresh();
        }
    }

    /// <summary>Checks the selected component against the entity again, e.g. after components were added to it.</summary>
    public void Refresh()
    {
        Details = Selected is null ? null : new CatalogDetails(Selected.Entry, _registry, Entity);
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(AddLabel));
        OnPropertyChanged(nameof(PlanText));
    }

    public CatalogDetails? Details
    {
        get => _details;
        private set => SetProperty(ref _details, value);
    }

    public bool CanAdd => Entity is not null && Selected is not null;

    public string AddLabel => Entity is null ? "Add" : $"Add to {Entity.Name}";

    /// <summary>What pressing Add does, spelled out: which requirements come along, or why it can't.</summary>
    public string PlanText
    {
        get
        {
            if (Selected is null)
                return "";
            if (Entity is null)
                return "Select an entity in the Hierarchy to add components to it.";
            var plan = Griff.Editing.ComponentCatalog.PlanAdd(_registry, Selected.Entry.Descriptor, Entity);
            var extra = plan.Take(plan.Count - 1).Select(d => d.DisplayName).ToList();
            var already = Entity.Components.Any(c => c.GetType() == Selected.Entry.Descriptor.Type)
                ? $" {Entity.Name} already has one; this adds another."
                : "";
            return extra.Count == 0
                ? $"Adds {Selected.Name} to {Entity.Name}.{already}"
                : $"Adds {Selected.Name} to {Entity.Name}, with {Join(extra)} that it needs.{already}";
        }
    }

    /// <summary>Selects the catalog entry for <paramref name="type"/>, or for a base type its first kind.</summary>
    public void Navigate(Type? type)
    {
        if (type is null)
            return;
        var target = Items.FirstOrDefault(i => i.Entry.Descriptor.Type == type)
            ?? (Griff.Editing.ComponentCatalog.Satisfier(_registry, type) is { } d ? Items.FirstOrDefault(i => i.Entry.Descriptor == d) : null);
        if (target is null)
            return;
        if (!Matches(target))
            Search = "";
        Selected = target;
    }

    private bool Matches(CatalogItem item)
    {
        var words = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.All(w =>
            item.Name.Contains(w, StringComparison.OrdinalIgnoreCase) ||
            item.Category.Contains(w, StringComparison.OrdinalIgnoreCase) ||
            item.Entry.Summary.PlainText.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    internal static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };
}

/// <summary>One row of the catalog's list.</summary>
public sealed class CatalogItem(CatalogEntry entry)
{
    public CatalogEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public string Category => Entry.Category;

    public bool IsScript => Entry.IsScript;

    public string Blurb => Entry.Summary.IsEmpty ? "No description yet." : Entry.Summary.FirstSentence;

    public bool HasRequirements => Entry.Requires.Count > 0;

    /// <summary>"Needs Body, Collider" for the list, so dependencies show before anything is picked.</summary>
    public string NeedsText => HasRequirements ? "Needs " + ComponentCatalogViewModel.Join(Entry.Requires.Select(r => r.Type.Name).ToList()) : "";

    public override string ToString() => Name;
}

public enum RequirementStatus
{
    /// <summary>The entity already has it.</summary>
    Met,

    /// <summary>Adding the component adds this too.</summary>
    WillAdd,

    /// <summary>No entity to check against.</summary>
    Unknown,
}

/// <summary>A component another one needs, and whether the entity has it.</summary>
public sealed record RequirementRow(string Name, string? Options, string? Note, Type Target, RequirementStatus Status, string StatusText);

/// <summary>A link to another component, with a note, e.g. in Works With or Required By.</summary>
public sealed record LinkRow(string Name, string? Note, Type Target, bool OnEntity);

/// <summary>Everything shown about the selected component.</summary>
public sealed class CatalogDetails
{
    public CatalogDetails(CatalogEntry entry, ComponentRegistry registry, Entity? entity)
    {
        Entry = entry;
        var plan = entity is null ? [] : Griff.Editing.ComponentCatalog.PlanAdd(registry, entry.Descriptor, entity);
        Requirements = entry.Requires.Select(r => Requirement(r, registry, entity, plan)).ToList();
        RequiredBy = entry.RequiredBy.Select(d => new LinkRow(d.DisplayName, null, d.Type, entity?.Components.Any(c => c.GetType() == d.Type) == true)).ToList();
        WorksWith = entry.WorksWith.Select(w => new LinkRow(w.Type.Name, w.Note, w.Type, entity is not null && w.IsMetBy(entity))).ToList();
    }

    public CatalogEntry Entry { get; }

    public string Name => Entry.Name;

    public string Category => Entry.Category;

    public bool IsScript => Entry.IsScript;

    public DocText Summary => Entry.Summary.IsEmpty ? NoDocs : Entry.Summary;

    public DocText Remarks => Entry.Remarks;

    public bool HasRemarks => !Entry.Remarks.IsEmpty;

    public IReadOnlyList<RequirementRow> Requirements { get; }

    public bool HasRequirements => Requirements.Count > 0;

    public IReadOnlyList<LinkRow> RequiredBy { get; }

    public bool HasRequiredBy => RequiredBy.Count > 0;

    public IReadOnlyList<LinkRow> WorksWith { get; }

    public bool HasWorksWith => WorksWith.Count > 0;

    public IReadOnlyList<CatalogProperty> Properties => Entry.Properties;

    public bool HasProperties => Properties.Count > 0;

    private DocText NoDocs => new([[new DocRun(IsScript
        ? "This script has no description yet. Write a /// <summary> comment above its class to describe it here."
        : "No description yet.")]]);

    private static RequirementRow Requirement(ComponentLink link, ComponentRegistry registry, Entity? entity, IReadOnlyList<ComponentDescriptor> plan)
    {
        var options = Griff.Editing.ComponentCatalog.Options(registry, link.Type);
        var optionsText = options.Count > 1 || (options.Count == 1 && options[0].Type != link.Type)
            ? "Any kind: " + string.Join(" or ", options.Select(o => o.DisplayName))
            : null;

        if (entity is null)
            return new RequirementRow(link.Type.Name, optionsText, link.Note, link.Type, RequirementStatus.Unknown, "");
        if (entity.Components.FirstOrDefault(link.Type.IsInstanceOfType) is { } existing)
            return new RequirementRow(link.Type.Name, optionsText, link.Note, link.Type, RequirementStatus.Met, $"{entity.Name} has {(existing.GetType() == link.Type ? "one" : "a " + existing.GetType().Name)}");
        var adding = plan.FirstOrDefault(p => link.Type.IsAssignableFrom(p.Type));
        return new RequirementRow(link.Type.Name, optionsText, link.Note, link.Type, RequirementStatus.WillAdd,
            adding is null ? "Missing" : adding.Type == link.Type ? "Will be added" : $"A {adding.DisplayName} will be added");
    }
}
