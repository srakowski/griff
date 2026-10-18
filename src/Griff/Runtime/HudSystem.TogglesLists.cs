using System.Numerics;
using Griff.Data;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

// Toggles lists: a row for each row of a table, each with a Toggle kept in the save data, made again when the table or the
// list changes. Made just before the layouts line them up, for every list in the scene, like the controls lists' rows.
internal sealed partial class HudSystem
{
    internal void BuildTogglesLists()
    {
        var lists = Game.Scene.AllNow<TogglesList>();
        if (lists.Count == 0)
            return;
        foreach (var list in lists)
        {
            var stamp = new TogglesListStamp(Game.Tables.Version, list.Table, list.Hide, list.LabelColumn, list.DefaultColumn, list.SaveAs,
                list.OnText, list.OffText, list.Template, list.Font, list.TextSize, list.Color, list.ButtonColor, list.RowHeight,
                list.ButtonWidth, list.RowWidth(), ToggleRowOrder(list));
            if (list.BuiltFrom == stamp && ToggleRowsStillThere(list))
                continue;

            list.BuiltFrom = stamp;
            MakeToggleRows(list);
        }
    }

    private bool ToggleRowsStillThere(TogglesList list)
    {
        foreach (var row in list.Rows)
        {
            if (row.Scene != Game.Scene || row.Parent != list.Entity || Game.IsDestroying(row))
                return false;
        }

        return true;
    }

    private void MakeToggleRows(TogglesList list)
    {
        var owner = list.Entity!;
        var table = Game.Tables.Find(list.Table);
        if (table is null && !string.IsNullOrWhiteSpace(list.Table))
            Game.Warn($"The toggles list on \"{owner.Name}\" lists the rows of table \"{list.Table.Trim()}\", but there's no table called that; it lists nothing.");
        var template = string.IsNullOrWhiteSpace(list.Template) ? null : EntityNames.Find(Game.Scene, list.Template, new NameLookup(owner));
        if (template is null && !string.IsNullOrWhiteSpace(list.Template))
            Game.Warn($"The toggles list on \"{owner.Name}\" copies \"{list.Template.Trim()}\" for its rows, but there's no entity called that; it makes plain rows.");

        // The rows made before go, all but the template if it's among them.
        foreach (var child in owner.Children.ToList())
        {
            if (template is not null && template.IsSelfOrDescendantOf(child))
                continue;
            if (child.SelfAndDescendants().Any(e => e.GetComponent<Toggle>() is not null))
            {
                child.IsEnabled = false;
                Game.Destroy(child);
            }
        }

        var picked = table is null ? [] : list.Pick(table);
        var copier = template is null ? null : new EntityTemplate(template, Game.Components ?? ComponentRegistry.CreateDefault());
        var rows = new List<Entity>(picked.Count);
        foreach (var row in picked)
            rows.Add(Game.Spawn(copier is null ? PlainToggleRow(list, row) : CopiedToggleRow(copier, list, row), owner));
        list.SetRows([.. picked.Select(r => r.Name)], rows);
    }

    // The label on the left, and a button on the right whose Toggle says On or Off.
    private static Entity PlainToggleRow(TogglesList list, TableRow source)
    {
        var (width, height, order) = (list.RowWidth(), Math.Max(0.1f, list.RowHeight), ToggleRowOrder(list));
        var on = list.DefaultOf(source);
        var row = new Entity(source.Name);
        var room = row.AddComponent<Sprite>();
        (room.Size, room.Tint, room.Order) = (new Vector2(width, height), new Color(0, 0, 0, 0), order);

        var name = new Entity("Name");
        name.Transform.Position = new Vector3(-width / 2 + 0.1f, 0, 0);
        var label = name.AddComponent<Text>();
        (label.Value, label.Size, label.Color, label.Font, label.Order, label.Align) = (list.LabelOf(source), list.TextSize, list.Color, list.Font, order + 1, TextAlign.Left);
        row.AddChild(name);

        var key = new Entity("Switch");
        key.Transform.Position = new Vector3(list.ButtonX(-width / 2, width), 0, 0);
        var size = new Vector2(list.ButtonWidth, height * 0.85f);
        var back = key.AddComponent<Sprite>();
        (back.Size, back.Tint, back.Order) = (size, list.ButtonColor, order + 2);
        var text = key.AddComponent<Text>();
        (text.Value, text.Size, text.Color, text.Font, text.Order) = (on ? list.OnText : list.OffText, list.TextSize, list.Color, list.Font, order + 3);
        key.AddComponent<Button>().Size = size;
        var toggle = key.AddComponent<Toggle>();
        (toggle.IsOn, toggle.OnText, toggle.OffText, toggle.SaveAs) = (on, list.OnText, list.OffText, list.KeyOf(source));
        row.AddChild(key);
        return row;
    }

    private static int ToggleRowOrder(TogglesList list)
    {
        var order = Views.OrderAbove(list.Entity!.Parent, 100);
        return list.Entity.GetComponent<Sprite>() is { } sprite ? Math.Max(order, sprite.Order + 10) : order;
    }

    // A copy of the template for the row: "{name}" in its words is the label, "{row}" the row's name, and its Toggles with no
    // Save As are kept under the list's, starting as the table's default column says.
    private static Entity CopiedToggleRow(EntityTemplate copier, TogglesList list, TableRow source)
    {
        var row = copier.Instantiate();
        (row.Name, row.IsEnabled) = (source.Name, true);
        var label = list.LabelOf(source);
        foreach (var entity in row.SelfAndDescendants())
        {
            foreach (var text in entity.GetComponents<Text>())
                text.Value = ForToggleRow(text.Value, label, source.Name);
            foreach (var tooltip in entity.GetComponents<Tooltip>())
                tooltip.Value = ForToggleRow(tooltip.Value, label, source.Name);
            foreach (var toggle in entity.GetComponents<Toggle>())
            {
                toggle.SaveAs = string.IsNullOrWhiteSpace(toggle.SaveAs) ? list.KeyOf(source) : ForToggleRow(toggle.SaveAs, label, source.Name);
                if (!string.IsNullOrWhiteSpace(list.DefaultColumn))
                    toggle.IsOn = list.DefaultOf(source);
            }
        }

        return row;
    }

    private static string? ForToggleRow(string? value, string label, string row) =>
        value is null || !value.Contains('{') ? value : value.Replace("{name}", label, StringComparison.OrdinalIgnoreCase).Replace("{row}", row, StringComparison.OrdinalIgnoreCase);
}
