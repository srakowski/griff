using System.Collections;
using System.Resources;
using System.Windows;

namespace Griff.Editor.Core.Modules;

/// <summary>
/// Every genre's templates (Genres/&lt;Genre&gt;/Templates.xaml): its field editors' and its Clip Editor overlay's, found by
/// their place in the editor rather than named, merged where they're drawn (the Inspector's field templates, the Clip
/// Editor's window). So a genre's views come with its folder, and the Inspector and Clip Editor name none of them.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1010", Justification = "A ResourceDictionary, as XAML merges it.")]
public sealed class GenreTemplates : ResourceDictionary
{
    // The templates' places among the editor's compiled XAML: "genres/fighting/templates.baml".
    private static readonly Lazy<IReadOnlyList<Uri>> Found = new(() =>
    {
        var assembly = typeof(GenreTemplates).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources");
        if (stream is null)
            return [];
        using var reader = new ResourceReader(stream);
        return [.. reader.Cast<DictionaryEntry>().Select(e => (string)e.Key)
            .Where(k => k.StartsWith("genres/", StringComparison.Ordinal) && k.EndsWith("/templates.baml", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(k => new Uri($"pack://application:,,,/{assembly.GetName().Name};component/{k[..^".baml".Length]}.xaml"))];
    });

    /// <summary>Where each genre's templates are, "pack://application:,,,/Griff.Editor;component/genres/fighting/templates.xaml".</summary>
    public static IReadOnlyList<Uri> Sources => Found.Value;

    public GenreTemplates()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        foreach (var source in Sources)
            MergedDictionaries.Add(new ResourceDictionary { Source = source });
    }
}
