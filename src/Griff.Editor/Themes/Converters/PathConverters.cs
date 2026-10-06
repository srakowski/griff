using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace Griff.Editor.Themes.Converters;

public static class PathConverters
{
    /// <summary>"C:\Games\Space Shooter" → "Space Shooter".</summary>
    public static IValueConverter FileName { get; } = new FileNameConverter();

    private sealed class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string path ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
