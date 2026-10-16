using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Core.Commands;
using Griff.Editor.Shell;

namespace Griff.Editor.Features.Inspector;

public partial class InspectorView : UserControl
{
    private ComponentCatalogWindow? _catalog;

    private readonly CommandRegistry _commands;

    public InspectorView(CommandRegistry commands)
    {
        _commands = commands;
        InitializeComponent();
        // The buttons on a component's header run its menu's commands, given the component.
        Resources["Command.AddToAll"] = commands.Get<AddComponentToAllCommand>();
        Resources["Command.RemoveComponent"] = commands.Get<RemoveComponentCommand>();
    }

    // ----- Menus: a component's (right-clicking its header) and a changed field's (its mark, or right-clicking its name),
    // each made of the commands placed on it, given the component or field.

    private void OnHeaderMenu(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ComponentSection section } header)
            return;
        Open(CommandMenuItems.ContextMenu(_commands.All, ComponentMenuCommand.Menu, section), header, PlacementMode.MousePoint);
        e.Handled = true;
    }

    private void OnOverrideMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PropertyField { IsOverridden: true } field } mark)
            return;
        Open(CommandMenuItems.ContextMenu(_commands.All, PrefabFieldCommand.Menu, field), mark, PlacementMode.Bottom);
        e.Handled = true;
    }

    private static void Open(ContextMenu menu, FrameworkElement anchor, PlacementMode placement)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    private InspectorPanel Panel => (InspectorPanel)DataContext;

    // A finished drag or typed number closes the undo step, so the next change is separate.
    private void OnEditCompleted(object sender, RoutedEventArgs e) => Panel.Seal();

    // ----- The Add Component list: its state (what's typed, what's picked) is the panel's Picker.

    private bool _wasOpen;

    // A click on the button while the list is open closes it (the list shuts as the mouse goes down outside it), rather
    // than opening it again.
    private void OnAddComponentPressed(object sender, MouseButtonEventArgs e) => _wasOpen = Panel.IsAddOpen;

    private void OnAddComponentClicked(object sender, RoutedEventArgs e)
    {
        if (!_wasOpen)
            Panel.IsAddOpen = true;
        _wasOpen = false;
    }

    private void OnAddPopupOpened(object? sender, EventArgs e)
    {
        AddSearch.Focus();
        Keyboard.Focus(AddSearch);
        if (Panel.Picker?.Selected is { } selected)
            AddList.ScrollIntoView(selected);
    }

    // Typing filters; Up and Down move through the list, Enter adds the picked one and Escape closes it.
    private void OnAddSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Up or Key.PageDown or Key.PageUp:
                Panel.Picker?.MoveSelection(e.Key switch { Key.Down => 1, Key.Up => -1, Key.PageDown => 8, _ => -8 });
                break;
            case Key.Enter:
                Panel.AddPicked();
                break;
            case Key.Escape:
                Panel.IsAddOpen = false;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnAddListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AddList.SelectedItem is { } selected)
            AddList.ScrollIntoView(selected);
    }

    // A click on a component adds it.
    private void OnAddListClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(AddList, source) is ListBoxItem { DataContext: CatalogItem item })
        {
            Panel.Picker!.Selected = item;
            Panel.AddPicked();
            e.Handled = true;
        }
    }

    // Opens the catalog on what the list had found.
    private void OnAddBrowseClicked(object sender, RoutedEventArgs e)
    {
        var search = Panel.Picker?.Search;
        var picked = Panel.Picker?.Selected?.Entry.Descriptor.Type;
        Panel.IsAddOpen = false;
        OpenCatalog();
        if (_catalog?.Model is { } model)
        {
            if (!string.IsNullOrWhiteSpace(search))
                model.Search = search;
            model.Navigate(picked);
        }
    }

    private void OnCatalogClicked(object sender, RoutedEventArgs e) => OpenCatalog();

    // ----- Dropping on fields: a sound from the Assets panel onto a sound field, a prefab or scene onto a field naming
    // one, any asset onto a text field, and an entity from the Hierarchy onto a field that names entities.

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (DropTarget(e) is not null)
        {
            e.Effects = e.Data.GetDataPresent(typeof(Hierarchy.EntityNode)) ? DragDropEffects.Link : DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (DropTarget(e) is not { } field)
            return;
        if (e.Data.GetData(typeof(Hierarchy.EntityNode)) is Hierarchy.EntityNode node)
            field.DropEntity(node.Entity);
        else if (e.Data.GetData(Assets.AssetNode.DataFormat) is string path)
            field.DropAsset(path);
        e.Handled = true;
    }

    private static PropertyField? DropTarget(DragEventArgs e)
    {
        var path = e.Data.GetData(Assets.AssetNode.DataFormat) as string;
        var isEntity = e.Data.GetDataPresent(typeof(Hierarchy.EntityNode));
        if (path is null && !isEntity)
            return null;
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is FrameworkElement { DataContext: PropertyField field })
            {
                var isSound = path is not null && Audio.EditorAudio.Extensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());
                return field.Kind switch
                {
                    FieldKind.Entity when isEntity => field,
                    FieldKind.Sound when isSound => field,
                    _ when path is not null && field.TakesAsset(path) => field,
                    _ => null,
                };
            }

            if (element is not Visual)
                return null;
        }

        return null;
    }

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

        _catalog = new ComponentCatalogWindow(model, panel.AddComponent) { Owner = Window.GetWindow(this) };
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

    /// <summary>For an enum with many choices (<see cref="PropertyField.IsLongEnum"/>): its choice, with a list to search.</summary>
    public DataTemplate? LongEnum { get; set; }
    public DataTemplate? Vector { get; set; }
    public DataTemplate? Color { get; set; }
    public DataTemplate? Lines { get; set; }
    public DataTemplate? Sound { get; set; }
    public DataTemplate? Image { get; set; }
    public DataTemplate? Entity { get; set; }

    /// <summary>For <see cref="FieldKind.Name"/>, <see cref="FieldKind.Action"/> and <see cref="FieldKind.Output"/>: a prefab, scene, input action, layer, message, action or output.</summary>
    public DataTemplate? Named { get; set; }

    /// <summary>For <see cref="FieldKind.Custom"/>: the field's own editor, shown by its template.</summary>
    public DataTemplate? Custom { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        (item as PropertyField)?.Kind switch
        {
            FieldKind.Custom => Custom,
            FieldKind.Bool => Bool,
            FieldKind.Number => Number,
            FieldKind.Text => Text,
            FieldKind.Enum when ((PropertyField)item).IsLongEnum => LongEnum,
            FieldKind.Enum => Enum,
            FieldKind.Vector => Vector,
            FieldKind.Color => Color,
            FieldKind.Lines => Lines,
            FieldKind.Sound => Sound,
            FieldKind.Image => Image,
            FieldKind.Entity => Entity,
            FieldKind.Name or FieldKind.Action or FieldKind.Output => Named,
            _ => null,
        };
}
