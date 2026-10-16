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
                CommandMenuItems.AddGroup(topLevel.Items, group);
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

/// <summary>
/// Menu items for commands, shared by the main menu and the panels' right-click menus, so both lay commands out alike:
/// commands that make things (those with a <see cref="EditorCommand.Category"/>) go in a submenu per category, each item
/// with its icon and a tooltip saying what it makes.
/// </summary>
public static class CommandMenuItems
{
    /// <summary>
    /// Adds a group of commands: those without a category as items in their order, then those with one in a submenu
    /// per category (see <see cref="AddCategories"/>).
    /// </summary>
    public static void AddGroup(ItemCollection items, IEnumerable<EditorCommand> group)
    {
        var commands = group.ToList();
        foreach (var command in commands.Where(c => c.Category is null).OrderBy(c => c.Placement?.Order ?? 0))
            items.Add(Item(command));
        AddCategories(items, CommandCategories.Group(commands));
    }

    /// <summary>
    /// Adds a submenu for each category, in order (Characters &gt; Platformer Player), with the genre modules' together in
    /// one more, <see cref="CommandCategories.Genres"/>, so however many modules there are, the menu stays short.
    /// </summary>
    public static void AddCategories(ItemCollection items, IEnumerable<IGrouping<string, EditorCommand>> categories)
    {
        MenuItem? genres = null;
        foreach (var category in categories)
        {
            var submenu = new MenuItem { Header = category.Key.Replace("_", "__"), Tag = category.Key };
            foreach (var command in category)
                submenu.Items.Add(Item(command, CommandCategories.ItemText(command), CommandCategories.Description(command)));
            if (CommandCategories.IsModule(category.Key))
            {
                if (genres is null)
                    items.Add(genres = new MenuItem { Header = CommandCategories.Genres, Tag = CommandCategories.Genres });
                genres.Items.Add(submenu);
            }
            else
            {
                items.Add(submenu);
            }
        }
    }

    /// <summary>
    /// The right-click menu <paramref name="menu"/> (a component's, in the Inspector), built from the commands placed on it
    /// (<see cref="EditorCommand.ContextPlacement"/>) that belong on it for <paramref name="target"/>, what it was opened on,
    /// which each runs on: a separator between groups, and an item that can't run on it now greyed, saying why.
    /// </summary>
    public static ContextMenu ContextMenu(IEnumerable<EditorCommand> commands, string menu, object target)
    {
        var context = new ContextMenu();
        var groups = commands.Where(c => c.ContextPlacement?.Menu == menu && c.IsOnMenuFor(target)).GroupBy(c => c.ContextPlacement!.Group).OrderBy(g => g.Key);
        foreach (var group in groups)
        {
            if (context.Items.Count > 0)
                context.Items.Add(new Separator());
            foreach (var command in group.OrderBy(c => c.ContextPlacement!.Order))
            {
                var item = Item(command, command.TextFor(target), command.CanExecute(target) ? command.TipFor(target) : command.WhyNot(target));
                item.CommandParameter = target;
                context.Items.Add(item);
            }
        }

        return context;
    }

    /// <summary>A command's menu item: its text (or <paramref name="header"/>), shortcut and icon, running it when clicked.</summary>
    public static MenuItem Item(EditorCommand command, string? header = null, string? tip = null)
    {
        var item = new MenuItem
        {
            Header = header ?? command.Text,
            // A command with choices opens a submenu of them instead of running.
            Command = command.Choices is null ? command : null,
            InputGestureText = command.GestureText,
            Icon = command.Icon is { } icon ? Icons.Create(icon) : null,
            IsCheckable = false,
            IsChecked = command.IsChecked == true,
            ToolTip = tip,
            Tag = command,
        };
        // Greyed out (an attack with nothing selected to give it to), it still says what it makes.
        ToolTipService.SetShowOnDisabled(item, true);
        return item;
    }
}
