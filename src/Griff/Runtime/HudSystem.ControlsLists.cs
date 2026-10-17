using System.Numerics;
using System.Text.RegularExpressions;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

// Controls lists: a row for each input action, each with a Rebind button, made again when the actions or the list change.
// Made just before the layouts line them up, for every list in the scene (a closed options panel's too), so a panel's rows
// are there to pick as it opens.
internal sealed partial class HudSystem
{
    internal void BuildControlsLists()
    {
        var lists = Game.Scene.AllNow<ControlsList>();
        if (lists.Count == 0)
            return;
        var version = Game.MachineInput.BindingsVersion;
        foreach (var list in lists)
        {
            var stamp = new ControlsListStamp(version, list.Actions, list.Show, list.Hide, list.Labels, list.Slots, list.Template, list.Font,
                list.TextSize, list.Color, list.ButtonColor, list.RowHeight, list.ButtonWidth, list.RowWidth(), RowOrder(list));
            if (list.BuiltFrom == stamp && RowsStillThere(list))
                continue;

            var listed = list.Pick(Game.MachineInput.Actions);
            // Only the bindings changed (the player rebound a key): the rows show it by themselves.
            if (list.BuiltFrom is { } was && was with { Version = version } == stamp && listed.SequenceEqual(list.Listed) && RowsStillThere(list))
            {
                list.BuiltFrom = stamp;
                continue;
            }

            list.BuiltFrom = stamp;
            MakeRows(list, listed);
        }
    }

    private bool RowsStillThere(ControlsList list)
    {
        foreach (var row in list.Rows)
        {
            if (row.Scene != Game.Scene || row.Parent != list.Entity || Game.IsDestroying(row))
                return false;
        }

        return true;
    }

    private void MakeRows(ControlsList list, List<string> listed)
    {
        var owner = list.Entity!;
        var template = string.IsNullOrWhiteSpace(list.Template) ? null : EntityNames.Find(Game.Scene, list.Template, new NameLookup(owner));
        if (template is null && !string.IsNullOrWhiteSpace(list.Template))
            Game.Warn($"The controls list on \"{owner.Name}\" copies \"{list.Template.Trim()}\" for its rows, but there's no entity called that; it makes plain rows.");

        // The rows made before (or saved with the scene) go, all but the template if it's among them.
        foreach (var child in owner.Children.ToList())
        {
            if (template is not null && template.IsSelfOrDescendantOf(child))
                continue;
            if (child.SelfAndDescendants().Any(e => e.GetComponents<Button>().Any(b => b.Action == ButtonAction.Rebind)))
            {
                child.IsEnabled = false;
                Game.Destroy(child);
            }
        }

        var copier = template is null ? null : new EntityTemplate(template, Game.Components ?? ComponentRegistry.CreateDefault());
        var rows = new List<Entity>(listed.Count);
        foreach (var action in listed)
        {
            var row = copier is null ? PlainRow(list, action) : CopiedRow(copier, list, action);
            rows.Add(Game.Spawn(row, owner));
        }

        list.SetRows(listed, rows);
    }

    // The action's name on the left, and a Rebind button for each slot on the right showing its keys.
    private static Entity PlainRow(ControlsList list, string action)
    {
        var (width, height, order) = (list.RowWidth(), Math.Max(0.1f, list.RowHeight), RowOrder(list));
        var slots = Math.Max(1, list.Slots);
        var row = new Entity(action);
        var room = row.AddComponent<Sprite>();
        (room.Size, room.Tint, room.Order) = (new Vector2(width, height), new Color(0, 0, 0, 0), order);

        var name = new Entity("Name");
        name.Transform.Position = new Vector3(-width / 2 + 0.1f, 0, 0);
        AddText(name, list, list.LabelOf(action), order + 1).Align = TextAlign.Left;
        row.AddChild(name);

        for (var slot = 0; slot < slots; slot++)
        {
            var key = new Entity(slots == 1 ? "Key" : $"Key {slot + 1}");
            key.Transform.Position = new Vector3(list.ButtonX(-width / 2, width, slot, slots), 0, 0);
            var size = new Vector2(list.ButtonWidth, height * 0.85f);
            var back = key.AddComponent<Sprite>();
            (back.Size, back.Tint, back.Order) = (size, list.ButtonColor, order + 2);
            AddText(key, list, slots == 1 ? $"{{{action}:all}}" : $"{{{action}:{slot}}}", order + 3);
            var button = key.AddComponent<Button>();
            (button.Size, button.Action, button.InputAction, button.BindingSlot) = (size, ButtonAction.Rebind, action, slot);
            row.AddChild(key);
        }

        return row;
    }

    // Above what it's inside (a panel's backdrop), and its own background if it has one.
    private static int RowOrder(ControlsList list)
    {
        var order = Views.OrderAbove(list.Entity!.Parent, 100);
        return list.Entity.GetComponent<Sprite>() is { } sprite ? Math.Max(order, sprite.Order + 10) : order;
    }

    private static Text AddText(Entity entity, ControlsList list, string value, int order)
    {
        var text = entity.AddComponent<Text>();
        (text.Value, text.Size, text.Color, text.Font, text.Order) = (value, list.TextSize, list.Color, list.Font, order);
        return text;
    }

    // A copy of the template for the action: "{name}" in its words is the action's name, "{action}" (and "{action:all}")
    // its keys, and its Rebind buttons with no action rebind this one.
    private static Entity CopiedRow(EntityTemplate copier, ControlsList list, string action)
    {
        var row = copier.Instantiate();
        (row.Name, row.IsEnabled) = (action, true);
        var label = list.LabelOf(action);
        foreach (var entity in row.SelfAndDescendants())
        {
            foreach (var text in entity.GetComponents<Text>())
                text.Value = ForRow(text.Value, action, label);
            foreach (var tooltip in entity.GetComponents<Tooltip>())
                tooltip.Value = ForRow(tooltip.Value, action, label);
            foreach (var button in entity.GetComponents<Button>())
            {
                if (button.Action == ButtonAction.Rebind && string.IsNullOrWhiteSpace(button.InputAction))
                    button.InputAction = action;
            }
        }

        return row;
    }

    private static readonly Regex RowAction = new(@"\{\s*action\s*(?=[:}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? ForRow(string? value, string action, string label) =>
        value is null || !value.Contains('{') ? value : RowAction.Replace(value.Replace("{name}", label, StringComparison.OrdinalIgnoreCase), "{" + action);
}
