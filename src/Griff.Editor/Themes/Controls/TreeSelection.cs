using System.Windows;

namespace Griff.Editor.Themes.Controls;

/// <summary>
/// Marks a tree row as selected in a tree that selects several rows (a WPF TreeView only selects one): the row draws
/// like a selected one. Set it from the item container style, bound to the row's own selected flag.
/// </summary>
public static class TreeSelection
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached(
        "IsSelected", typeof(bool), typeof(TreeSelection), new PropertyMetadata(false));

    public static bool GetIsSelected(DependencyObject element) => (bool)element.GetValue(IsSelectedProperty);

    public static void SetIsSelected(DependencyObject element, bool value) => element.SetValue(IsSelectedProperty, value);
}
