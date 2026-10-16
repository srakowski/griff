using System.IO;
using Griff.Editor.Features.Inspector;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Editor.Tests;

/// <summary>
/// The Inspector's shape: its view model and the types it's made of are a file each, named after the type; its view
/// models build no menus (the view builds them from the commands placed on them); and it reads placed copies' prefabs
/// through the cache PrefabFiles keeps rather than reading prefab files itself.
/// </summary>
public sealed class InspectorStructureTests
{
    private static readonly string Folder = Path.Combine(RepoRoot(), "src", "Griff.Editor", "Features", "Inspector");

    private static IEnumerable<(string File, TypeDeclarationSyntax Type)> TopLevelTypes(string path) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .Where(t => t.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax)
            .OfType<TypeDeclarationSyntax>()
            .Select(t => (Path.GetFileName(path), t));

    [Fact]
    public void TheInspectorsTypes_AreAFileEach()
    {
        Type[] types = [typeof(InspectorPanel), typeof(EntityHeader), typeof(ComponentSection), typeof(FieldGroup), typeof(NeedRow),
            typeof(PropertyField), typeof(EntityChoice), typeof(EnumChoice), typeof(AxisField), typeof(IPropertyHost), typeof(InspectorModule)];
        foreach (var type in types)
        {
            var file = Path.Combine(Folder, type.Name + ".cs");
            Assert.True(File.Exists(file), $"{type.Name} has no file of its own.");
            Assert.Equal([type.Name], TopLevelTypes(file).Select(t => t.Type.Identifier.Text));
        }

        Assert.True(File.Exists(Path.Combine(Folder, nameof(FieldKind) + ".cs")));
    }

    [Fact]
    public void TheInspectorsViewModels_BuildNoMenus_AndReadNoPrefabFiles()
    {
        var views = typeof(App).Assembly.GetTypes().Where(t => typeof(System.Windows.UIElement).IsAssignableFrom(t)).Select(t => t.Name).ToHashSet();
        var found = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*.cs", SearchOption.AllDirectories))
        {
            foreach (var (file, type) in TopLevelTypes(path).Where(t => !views.Contains(t.Type.Identifier.Text)))
            {
                foreach (var node in type.DescendantNodes())
                {
                    if (node is ObjectCreationExpressionSyntax { Type: var made } && made.ToString() is "ContextMenu" or "MenuItem")
                        found.Add($"{file}: {type.Identifier.Text} makes a {made}");
                    if (node is IdentifierNameSyntax { Identifier.Text: "LoadPrefabFromString" })
                        found.Add($"{file}: {type.Identifier.Text} reads a prefab file itself");
                }
            }
        }

        Assert.True(found.Count == 0, "The view builds menus from commands (CommandMenuItems.ContextMenu), and prefabs are read through PrefabFiles:\n" + string.Join("\n", found));
    }

    private static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "griff.slnx")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("The tests run from inside the Griff repository.");
    }
}
