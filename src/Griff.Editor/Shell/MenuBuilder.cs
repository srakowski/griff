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
                        // A command with choices opens a submenu of them instead of running.
                        Command = command.Choices is null ? command : null,
                        InputGestureText = command.GestureText,
                        Icon = command.Icon is { } icon ? Icons.Create(icon) : null,
                        IsCheckable = false,
                        IsChecked = command.IsChecked == true,
                        Tag = command,
                    });
                }
            }

            // On/off settings show their tick as the menu opens, and commands with choices list what they can pick now.
            topLevel.SubmenuOpened += (_, e) =>
            {
                if (e.OriginalSource != topLevel)
                    return;
                foreach (var item in topLevel.Items.OfType<MenuItem>())
                {
                    if (item.Tag is EditorCommand { IsChecked: { } on })
                        item.IsChecked = on;
                    if (item.Tag is EditorCommand { Choices: { } choices } chooser)
                        FillChoices(item, chooser, choices);
                }
            };
            menu.Items.Add(topLevel);
        }
    }

    private static void FillChoices(MenuItem item, EditorCommand command, IReadOnlyList<string> choices)
    {
        item.Items.Clear();
        item.IsEnabled = choices.Count > 0 && command.CanExecute();
        foreach (var choice in choices)
        {
            var entry = new MenuItem { Header = choice.Replace("_", "__"), IsChecked = choice == command.Chosen };
            // Exceptions surface through Application.DispatcherUnhandledException, as a command's do.
            entry.Click += async (_, _) => await command.ChooseAsync(choice);
            item.Items.Add(entry);
        }
    }
}
