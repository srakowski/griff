using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Griff.Editor.Themes.Converters;

/// <summary>True → Visible, false → Collapsed; reversed when <see cref="Invert"/> is set.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static BoolToVisibilityConverter Default { get; } = new();

    public static BoolToVisibilityConverter Inverted { get; } = new() { Invert = true };

    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
