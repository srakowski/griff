using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Core.Panels;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Shell;

/// <summary>Creates registered tool panels and places them as tabs in their zones.</summary>
public sealed class PanelHost(IServiceProvider services, IEnumerable<PanelDescriptor> descriptors)
{
    public void Populate(IReadOnlyDictionary<PanelZone, TabControl> zones)
    {
        var panels = descriptors
            .Select(d => (Panel: (ToolPanel)services.GetRequiredService(d.PanelType), d.ViewType))
            .OrderBy(p => p.Panel.Order);

        foreach (var (panel, viewType) in panels)
        {
            var view = (FrameworkElement)services.GetRequiredService(viewType);
            view.DataContext = panel;
            zones[panel.Zone].Items.Add(new TabItem { Header = CreateHeader(panel), Content = view });
        }

        foreach (var zone in zones.Values.Where(z => z.Items.Count > 0))
            zone.SelectedIndex = 0;
    }

    private static object CreateHeader(ToolPanel panel)
    {
        if (panel.Icon is null)
            return panel.Title;

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Icons.Create(panel.Icon, 12));
        header.Children.Add(new TextBlock { Text = panel.Title, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return header;
    }
}
