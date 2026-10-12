using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// A field's list (its <see cref="FieldPicker"/>): the search box has the keyboard, so typing narrows it, Up and Down
/// move through it, Enter picks and Escape shuts it.
/// </summary>
public partial class FieldPickerView : UserControl
{
    public FieldPickerView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                Dispatcher.BeginInvoke(() => Keyboard.Focus(Search), System.Windows.Threading.DispatcherPriority.Input);
        };
        DataContextChanged += (_, _) =>
        {
            if (Picker?.Selected is { } selected)
                List.ScrollIntoView(selected);
        };
    }

    private FieldPicker? Picker => DataContext as FieldPicker;

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Up or Key.PageDown or Key.PageUp:
                Picker?.MoveSelection(e.Key switch { Key.Down => 1, Key.Up => -1, Key.PageDown => 8, _ => -8 });
                break;
            case Key.Enter:
                Picker?.PickSelected();
                break;
            case Key.Escape:
                Picker?.Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is { } selected)
            List.ScrollIntoView(selected);
    }

    // A click on a choice picks it.
    private void OnListClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(List, source) is ListBoxItem { DataContext: PickerChoice choice })
        {
            Picker?.Pick(choice.Name);
            e.Handled = true;
        }
    }

    private void OnUseTypedClicked(object sender, RoutedEventArgs e) => Picker?.UseTyped();

    private void OnActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PickerAction action })
            Picker?.Do(action.Tag);
    }
}

/// <summary>
/// The popup a field's list opens in, under the field: open while its <see cref="PropertyField.IsPickerOpen"/> is, and
/// shut by a click anywhere else.
/// </summary>
public sealed class FieldPickerPopup : Popup
{
    public FieldPickerPopup()
    {
        AllowsTransparency = true;
        StaysOpen = false;
        Placement = PlacementMode.Bottom;
        PopupAnimation = PopupAnimation.None;
        VerticalOffset = 2;
        var view = new FieldPickerView { MinWidth = 240, MaxWidth = 420 };
        // The popup's own data is the field; its list is the field's Picker.
        view.SetBinding(DataContextProperty, new Binding($"{nameof(DataContext)}.{nameof(PropertyField.Picker)}") { Source = this });
        view.SetBinding(WidthProperty, new Binding($"{nameof(PlacementTarget)}.{nameof(ActualWidth)}") { Source = this });
        Child = view;
        SetBinding(IsOpenProperty, new Binding(nameof(PropertyField.IsPickerOpen)) { Mode = BindingMode.TwoWay });
    }
}
