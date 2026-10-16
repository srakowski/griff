using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

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
