using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Griff.Editor.Themes.Controls;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Browses every component with its documentation and dependencies, and adds the chosen one (with whatever it needs).
/// The slower, explained alternative to the Inspector's Add Component list.
/// </summary>
public partial class ComponentCatalogWindow : ChromeWindow
{
    private readonly Action<ComponentDescriptor> _add;

    /// <param name="model">What to show.</param>
    /// <param name="add">Adds the chosen component (and what it needs) to the selected entity.</param>
    public ComponentCatalogWindow(ComponentCatalogViewModel model, Action<ComponentDescriptor> add)
    {
        InitializeComponent();
        _add = add;
        DataContext = model;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ComponentCatalogViewModel.Selected))
            {
                DetailsScroller.ScrollToTop();
                if (model.Selected is { } selected)
                    ComponentList.ScrollIntoView(selected);
            }
        };
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            if (model.Selected is { } selected)
                ComponentList.ScrollIntoView(selected);
        };
    }

    public ComponentCatalogViewModel Model => (ComponentCatalogViewModel)DataContext;

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (!Model.CanAdd || Model.Selected is not { } selected)
            return;
        _add(selected.Entry.Descriptor);
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ComponentList, source) is ListBoxItem)
            OnAdd(sender, e);
    }

    // Down from the search box moves into the list, so the keyboard alone can browse.
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Up) || Model.View.IsEmpty)
            return;
        var index = Math.Max(0, ComponentList.SelectedIndex);
        if (ComponentList.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item)
            item.Focus();
        else
            ComponentList.Focus();
        e.Handled = true;
    }

    // Typing in the list goes back to the search box.
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is >= Key.A and <= Key.Z && Keyboard.Modifiers == ModifierKeys.None)
            SearchBox.Focus();
    }
}

/// <summary>"JumpHeight" → "Jump Height", as the Inspector labels fields.</summary>
public sealed class HumanizeConverter : IValueConverter
{
    public static HumanizeConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name ? InspectorPanel.Humanize(name) : value ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
