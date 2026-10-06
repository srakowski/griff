using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Griff.Editor.Themes.Converters;

/// <summary>
/// Converts a <see cref="TreeViewItem"/> to a left margin proportional to its depth, so the item
/// template can indent its header while the row background still spans the full width.
/// </summary>
public sealed class TreeIndentConverter : IValueConverter
{
    public double Indent { get; set; } = 16;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var depth = 0;
        var current = value as DependencyObject;
        while (current is not null and not TreeView)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is TreeViewItem)
                depth++;
        }

        return new Thickness(depth * Indent, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
