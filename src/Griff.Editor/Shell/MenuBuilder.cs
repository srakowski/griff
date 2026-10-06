using System.Windows.Controls;
using Griff.Editor.Core.Commands;
using Griff.Editor.Themes;

namespace Griff.Editor.Shell;

/// <summary>Builds the main menu from registered menus and command placements.</summary>
public sealed class MenuBuilder(CommandRegistry commands, IEnumerable<MenuDefinition> menus)
{
    public void Populate(Menu menu)
    {
        var definitions = menus.ToDictionary(m => m.Id);
        var placed = commands.All.Where(c => c.Placement is not null).ToLookup(c => c.Placement!.Menu);

        foreach (var group in placed)
        {
            if (!definitions.ContainsKey(group.Key))
                throw new InvalidOperationException(
                    $"Command '{group.First().Id}' targets menu '{group.Key}', which is not registered.");
        }

        foreach (var definition in definitions.Values.OrderBy(m => m.Order))
        {
            var groups = placed[definition.Id].GroupBy(c => c.Placement!.Group).OrderBy(g => g.Key).ToList();
            if (groups.Count == 0)
                continue;

            var topLevel = new MenuItem { Header = definition.Text };
            foreach (var group in groups)
            {
                if (topLevel.Items.Count > 0)
                    topLevel.Items.Add(new Separator());

                foreach (var command in group.OrderBy(c => c.Placement!.Order))
                {
                    topLevel.Items.Add(new MenuItem
                    {
                        Header = command.Text,
                        Command = command,
                        InputGestureText = command.GestureText,
                        Icon = command.Icon is { } icon ? Icons.Create(icon) : null,
                    });
                }
            }

            menu.Items.Add(topLevel);
        }
    }
}
