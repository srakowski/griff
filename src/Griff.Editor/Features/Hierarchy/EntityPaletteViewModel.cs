using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Commands;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Hierarchy;

/// <summary>
/// The Add Entity palette's state: everything the Entity menu makes, by category, with what was made lately in Recent at
/// the top (while nothing is typed), filtered by what's typed in <see cref="Search"/> (names, categories and
/// descriptions), and the <see cref="Selected"/> one's <see cref="PlanText"/> saying what it makes.
/// </summary>
public sealed class EntityPaletteViewModel : ObservableObject
{
    /// <summary>The group at the top listing what was made lately.</summary>
    public const string RecentGroup = "Recent";

    private string _search = "";
    private EntityPaletteItem? _selected;

    public EntityPaletteViewModel(CommandRegistry commands)
    {
        Items =
        [
            .. commands.Recent.Select(c => new EntityPaletteItem(c, RecentGroup)),
            .. commands.Categorized.SelectMany(g => g.Select(c => new EntityPaletteItem(c, g.Key))),
        ];
        View = new ListCollectionView(Items.ToList()) { Filter = o => Matches((EntityPaletteItem)o) };
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EntityPaletteItem.Group)));
        Selected = View.Cast<EntityPaletteItem>().FirstOrDefault();
    }

    /// <summary>Every item: the recent ones first (again in their own categories below).</summary>
    public IReadOnlyList<EntityPaletteItem> Items { get; }

    /// <summary>The items grouped (Recent, then each category) and filtered by <see cref="Search"/>.</summary>
    public ListCollectionView View { get; }

    /// <summary>What's typed: every word must be in an item's name, category or description.</summary>
    public string Search
    {
        get => _search;
        set
        {
            value ??= "";
            if (!SetProperty(ref _search, value))
                return;
            View.Refresh();
            OnPropertyChanged(nameof(HasResults));
            Selected = _search.Trim().Length > 0 ? BestMatch() : View.Cast<EntityPaletteItem>().FirstOrDefault();
        }
    }

    public bool HasResults => !View.IsEmpty;

    /// <summary>The item Enter makes.</summary>
    public EntityPaletteItem? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                OnPropertyChanged(nameof(PlanText));
        }
    }

    /// <summary>What Enter makes, in a sentence or two, or why it can't yet.</summary>
    public string PlanText => Selected is not { } item ? ""
        : item.CanCreate ? item.Description
        : $"{item.Description} Select an entity first.";

    /// <summary>Selects the item <paramref name="by"/> rows down the list as it shows (up when negative), stopping at its ends.</summary>
    public void MoveSelection(int by)
    {
        var shown = View.Cast<EntityPaletteItem>().ToList();
        if (shown.Count == 0)
            return;
        var at = Selected is null ? -1 : shown.IndexOf(Selected);
        Selected = shown[Math.Clamp(at < 0 ? (by > 0 ? 0 : shown.Count - 1) : at + by, 0, shown.Count - 1)];
    }

    // What's typed most likely names: the thing called that, then one whose name starts with it, then one whose name has
    // it, then the first listed (it matched on its category or description).
    private EntityPaletteItem? BestMatch()
    {
        var typed = _search.Trim();
        var shown = View.Cast<EntityPaletteItem>().ToList();
        return shown.FirstOrDefault(i => i.Name.Equals(typed, StringComparison.OrdinalIgnoreCase))
            ?? shown.FirstOrDefault(i => i.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            ?? shown.FirstOrDefault(i => i.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            ?? shown.FirstOrDefault();
    }

    private bool Matches(EntityPaletteItem item)
    {
        var words = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return true;
        // While searching, each thing shows once, in its own category.
        return !item.IsRecent && words.All(w =>
            item.Name.Contains(w, StringComparison.OrdinalIgnoreCase) ||
            item.Category.Contains(w, StringComparison.OrdinalIgnoreCase) ||
            item.Description.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>One row of the Add Entity palette: a command that makes something, with its icon and what it makes.</summary>
public sealed class EntityPaletteItem(EditorCommand command, string group)
{
    public EditorCommand Command { get; } = command;

    /// <summary>The command's id, which is also the agents' tool for it (Entity.CreateSprite is Entity_CreateSprite).</summary>
    public string Id => Command.Id;

    /// <summary>What it's called in its category, e.g. "Grid Enemy" (its text without "Create ").</summary>
    public string Name { get; } = CommandCategories.ItemText(command).Replace("_", "");

    public string Category { get; } = command.Category ?? "";

    /// <summary>The category it's listed under here, or <see cref="EntityPaletteViewModel.RecentGroup"/>.</summary>
    public string Group { get; } = group;

    public bool IsRecent => Group == EntityPaletteViewModel.RecentGroup;

    /// <summary>One line saying what it makes: the first sentence of its /// summary, as agents' tool says.</summary>
    public string Description { get; } = CommandCategories.Description(command);

    public string Icon => Command.Icon ?? Icons.Add;

    /// <summary>Whether it can be made now (an attack needs an entity selected to give it to).</summary>
    public bool CanCreate => Command.CanExecute();

    public override string ToString() => Name;
}
