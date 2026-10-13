using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Griff.Editor.Themes;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Features.DesignSystem;

public sealed record Swatch(string Name, Brush Brush, string Hex);

public sealed record IconSample(string Name, string Glyph);

/// <summary>Living reference for the design system. Tokens are read from the loaded resources.</summary>
public partial class DesignSystemWindow : ChromeWindow
{
    public DesignSystemWindow()
    {
        InitializeComponent();

        var resources = Flatten(Application.Current.Resources)
            .Where(r => r.Key is string)
            .DistinctBy(r => (string)r.Key)
            .ToList();

        BrushList.ItemsSource = resources
            .Where(r => ((string)r.Key).StartsWith("Brush.", StringComparison.Ordinal) && r.Value is SolidColorBrush)
            .Select(r => new Swatch((string)r.Key, (SolidColorBrush)r.Value, ((SolidColorBrush)r.Value).Color.ToString(CultureInfo.InvariantCulture)))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        ColorList.ItemsSource = resources
            .Where(r => r.Value is System.Windows.Media.Color)
            .Select(r => new Swatch((string)r.Key, new SolidColorBrush((System.Windows.Media.Color)r.Value), ((System.Windows.Media.Color)r.Value).ToString(CultureInfo.InvariantCulture)))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        IconList.ItemsSource = typeof(Icons)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => new IconSample(f.Name, (string)f.GetRawConstantValue()!))
            .ToList();
    }

    private static IEnumerable<KeyValuePair<object, object>> Flatten(ResourceDictionary dictionary)
    {
        foreach (var merged in dictionary.MergedDictionaries)
        {
            foreach (var entry in Flatten(merged))
                yield return entry;
        }

        foreach (var key in dictionary.Keys)
            yield return new KeyValuePair<object, object>(key, dictionary[key]);
    }
}
