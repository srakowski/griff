using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Every field editor's template drawn, headless. A template's StaticResources are found where it was read and then in the
/// app's resources, and a test has no app; so the templates (FieldEditors/*.xaml) are read again here inside a dictionary
/// that has the editor's theme, as the app's resources do. An editor of each kind is made for a field of a component that
/// has one, and drawn from its template: a template naming a resource that isn't there, or with a binding that throws,
/// fails here rather than in the running editor.
/// </summary>
public sealed partial class FieldEditorTemplateTests
{
    [Fact]
    public void EveryFieldEditor_DrawsFromItsTemplate_WithTheThemeLoaded() => EditorHost.Run(services =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var registry = services.GetRequiredService<Griff.Editor.Core.Workspace.Workspace>().Components;
        var host = services.GetRequiredService<InspectorPanel>();
        var templates = Templates();
        var root = new Border { Width = 320 };
        root.Resources.MergedDictionaries.Add(templates);
        var panel = new StackPanel();
        root.Child = panel;

        // A field of some component marked for each editor: the first one the registry has.
        var drawn = new List<Type>();
        foreach (var (attribute, editorType) in FieldEditor.Kinds)
        {
            Assert.True(templates.Contains(new DataTemplateKey(editorType)), $"{editorType.Name} has no DataTemplate in FieldEditors/*.xaml or a genre's Templates.xaml.");
            var found = registry.All.Concat(Scripted.All).SelectMany(d => d.Properties.Select(p => (Descriptor: d, Property: p)))
                .FirstOrDefault(x => x.Property.Editor?.GetType() == attribute);
            if (found.Descriptor is null)
                continue;
            var component = found.Descriptor.CreateInstance();
            new Entity("Sample").AddComponent(component);
            var field = new PropertyField(component, found.Property, host);
            Assert.IsType(editorType, field.Editor);
            panel.Children.Add(new ContentControl { Content = field.Editor });
            drawn.Add(editorType);
        }

        root.Measure(new Size(320, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();

        // Every editor drew its own template (not its type's name, as one with no template would).
        foreach (ContentControl shown in panel.Children)
        {
            var presenter = Descendants(shown).OfType<ContentPresenter>().First();
            Assert.True(VisualTreeHelper.GetChildrenCount(presenter) > 0, $"{shown.Content.GetType().Name} drew nothing.");
            Assert.IsNotType<TextBlock>(VisualTreeHelper.GetChild(presenter, 0));
            Assert.True(shown.ActualHeight > 0, $"{shown.Content.GetType().Name} has no height.");
        }

        Assert.Equal(FieldEditor.Kinds.Values.Select(t => t.Name).Order(), drawn.Select(t => t.Name).Order());
    });

    // The field editors' templates, every FieldEditors/*.xaml, and the Inspector's own (FieldTemplates.xaml, whose
    // FieldSelector picks a field's template by its kind), each read as the app reads it: in a dictionary of its own, finding
    // its StaticResources in itself, in what it merges, and in the theme (the app's resources).
    internal static ResourceDictionary Templates()
    {
        var inspector = Path.Combine(RepositoryRoot(), "src", "Griff.Editor", "Features", "Inspector");
        var folder = Path.Combine(inspector, "FieldEditors");
        var all = new ResourceDictionary();
        foreach (var file in Directory.GetFiles(folder, "*.xaml").Order(StringComparer.Ordinal).Append(Path.Combine(inspector, "FieldTemplates.xaml")))
        {
            var text = File.ReadAllText(file);
            // The Inspector's own merges the editors' files, which are read on their own here.
            var merged = Path.GetDirectoryName(file) != folder ? []
                : MergedSource().Matches(MergedDictionaries().Match(text).Value).Select(m => Inside(File.ReadAllText(Path.Combine(folder, m.Groups[1].Value))));
            var xaml = $"""
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                    xmlns:inspector="clr-namespace:Griff.Editor.Features.Inspector;assembly=Griff.Editor"
                                    xmlns:controls="clr-namespace:Griff.Editor.Themes.Controls;assembly=Griff.Editor"
                                    xmlns:theme="clr-namespace:Griff.Editor.Themes;assembly=Griff.Editor"
                                    xmlns:converters="clr-namespace:Griff.Editor.Themes.Converters;assembly=Griff.Editor">
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="pack://application:,,,/Griff.Editor;component/Themes/Theme.xaml" />
                    </ResourceDictionary.MergedDictionaries>
                {string.Concat(merged)}
                {Inside(text)}
                </ResourceDictionary>
                """;
            all.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
        }

        // Each genre's templates (Genres/<Genre>/Templates.xaml, merged by GenreTemplates), read as they are, with the theme.
        foreach (var file in Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", "Griff.Editor", "Genres"), "Templates.xaml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var text = ClrNamespace().Replace(File.ReadAllText(file), "clr-namespace:$1;assembly=Griff.Editor\"");
            var root = text.IndexOf('>', text.IndexOf("<ResourceDictionary", StringComparison.Ordinal)) + 1;
            text = text[..root] + """

                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="pack://application:,,,/Griff.Editor;component/Themes/Theme.xaml" />
                    </ResourceDictionary.MergedDictionaries>
                """ + text[root..];
            all.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(text));
        }

        return all;
    }

    // A script marked for the editors none of Griff's components use (a list's rows, names and values), as a game's might be.
    private static readonly Lazy<Griff.Scenes.Serialization.ComponentRegistry> ScriptedRegistry = new(() =>
    {
        var registry = Griff.Scenes.Serialization.ComponentRegistry.CreateDefault();
        var result = new Griff.Scripting.ScriptHost(registry).Reload([new Griff.Scripting.ScriptSource("Scripts/Marked.cs", """
            using System.Collections.Generic;
            public class Marked : Behavior
            {
                [ListRows(CellKind.Color)]
                public IReadOnlyList<string> Colors { get; set; } = ["#F2607E"];

                [Pairs(Values = CellKind.Number)]
                public IReadOnlyList<string> Prices { get; set; } = ["Sword: 30"];
            }
            """)], []);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));
        return registry;
    });

    private static Griff.Scenes.Serialization.ComponentRegistry Scripted => ScriptedRegistry.Value;

    [GeneratedRegex(@"clr-namespace:([A-Za-z0-9.]+)""")]
    private static partial Regex ClrNamespace();

    [GeneratedRegex(@"<ResourceDictionary\s+Source=""([^""]+)""")]
    private static partial Regex MergedSource();

    // What's inside a dictionary file's root element, its own merged dictionaries left out.
    private static string Inside(string file)
    {
        var start = file.IndexOf('>', file.IndexOf("<ResourceDictionary", StringComparison.Ordinal)) + 1;
        var end = file.LastIndexOf("</ResourceDictionary>", StringComparison.Ordinal);
        return MergedDictionaries().Replace(file[start..end], "");
    }

    [GeneratedRegex(@"<ResourceDictionary\.MergedDictionaries>.*?</ResourceDictionary\.MergedDictionaries>", RegexOptions.Singleline)]
    private static partial Regex MergedDictionaries();

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Griff.Editor")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException("Can't find the repository from " + AppContext.BaseDirectory);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child))
                yield return deeper;
        }
    }
}
