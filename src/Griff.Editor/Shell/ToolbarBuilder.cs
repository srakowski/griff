using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Core.Commands;
using Griff.Editor.Themes;

namespace Griff.Editor.Shell;

/// <summary>Builds the main toolbar from command toolbar placements.</summary>
public sealed class ToolbarBuilder(CommandRegistry commands)
{
    // Kept here: commands only hold on to these weakly.
    private readonly List<EventHandler> _refreshers = [];

    public void Populate(Panel toolbar)
    {
        var groups = commands.All
            .Where(c => c.Toolbar is not null)
            .GroupBy(c => c.Toolbar!.Group)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            if (toolbar.Children.Count > 0)
            {
                var separator = new Separator();
                separator.SetResourceReference(FrameworkElement.StyleProperty, "Style.Separator.Vertical");
                toolbar.Children.Add(separator);
            }

            foreach (var command in group.OrderBy(c => c.Toolbar!.Order))
            {
                var icon = command.Icon
                    ?? throw new InvalidOperationException($"Toolbar command '{command.Id}' has no icon.");

                var button = new Button
                {
                    Command = command,
                    Content = Icons.Create(icon),
                    ToolTip = command.ToolTipText,
                };
                button.SetResourceReference(FrameworkElement.StyleProperty, "Style.Button.Toolbar");
                toolbar.Children.Add(button);

                // An on/off switch (Mute) shows which way it's set: its icon follows it.
                if (command.IsChecked is not null)
                {
                    var shown = icon;
                    EventHandler refresh = (_, _) =>
                    {
                        if (command.Icon is { } now && now != shown)
                            button.Content = Icons.Create(shown = now);
                    };
                    _refreshers.Add(refresh);
                    command.CanExecuteChanged += refresh;
                }
            }
        }
    }
}
