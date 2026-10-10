using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Panels;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Inspector;

public partial class InspectorView : UserControl
{
    private ComponentCatalogWindow? _catalog;

    public InspectorView()
    {
        InitializeComponent();
    }

    private InspectorPanel Panel => (InspectorPanel)DataContext;

    // A finished drag or typed number closes the undo step, so the next change is separate.
    private void OnEditCompleted(object sender, RoutedEventArgs e) => Panel.Seal();

    private void OnAddComponentClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = AddComponentButton,
            Placement = PlacementMode.Bottom,
            MinWidth = AddComponentButton.ActualWidth,
        };

        foreach (var descriptor in Panel.AvailableComponents)
        {
            var item = new MenuItem { Header = descriptor.DisplayName };
            item.Click += (_, _) => Panel.AddComponent(descriptor);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var browse = new MenuItem { Header = "Browse Catalog..." };
        browse.Click += (_, _) => OpenCatalog();
        menu.Items.Add(browse);

        menu.IsOpen = true;
    }

    private void OnCatalogClicked(object sender, RoutedEventArgs e) => OpenCatalog();

    // Not modal, so the editor stays usable alongside it; it follows the selection and the entity's components.
    private void OpenCatalog()
    {
        if (_catalog is not null)
        {
            _catalog.Activate();
            return;
        }

        var panel = Panel;
        var model = panel.CreateCatalog();
        void Follow(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(InspectorPanel.Header) or nameof(InspectorPanel.Components))
                model.Entity = panel.Entity;
        }

        void ComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => model.Refresh();

        _catalog = new ComponentCatalogWindow(model, panel.AddWithRequirements) { Owner = Window.GetWindow(this) };
        panel.PropertyChanged += Follow;
        panel.Components.CollectionChanged += ComponentsChanged;
        _catalog.Closed += (_, _) =>
        {
            panel.PropertyChanged -= Follow;
            panel.Components.CollectionChanged -= ComponentsChanged;
            _catalog = null;
        };
        _catalog.Show();
    }
}

/// <summary>Picks the editor template for a property by its <see cref="FieldKind"/>.</summary>
public sealed class FieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Bool { get; set; }
    public DataTemplate? Number { get; set; }
    public DataTemplate? Text { get; set; }
    public DataTemplate? Enum { get; set; }
    public DataTemplate? Vector { get; set; }
    public DataTemplate? Color { get; set; }
    public DataTemplate? Lines { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        (item as PropertyField)?.Kind switch
        {
            FieldKind.Bool => Bool,
            FieldKind.Number => Number,
            FieldKind.Text => Text,
            FieldKind.Enum => Enum,
            FieldKind.Vector => Vector,
            FieldKind.Color => Color,
            FieldKind.Lines => Lines,
            _ => null,
        };
}

public sealed class InspectorModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddPanel<InspectorPanel, InspectorView>();
}
