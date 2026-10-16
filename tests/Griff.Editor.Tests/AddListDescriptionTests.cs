using System.IO;
using Griff.Editor.Core.Workspace;
using Griff.Editing;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Add Component and Add Entity lists describe a row only when asked: a tooltip on the row itself would cover the
/// names under it, so the description is behind an info mark on the row and, for the selected row, in a line under the list.
/// </summary>
public sealed class AddListDescriptionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheAddLists_HaveNoTooltipOnTheRowItself()
    {
        // A row's own tooltip would cover the names under it; the description is behind an info glyph and, for the
        // selected row, in a fixed line under the list.
        var src = Path.Combine(FindRoot(), "src", "Griff.Editor", "Features");
        foreach (var file in new[] { "Inspector/InspectorView.xaml", "Hierarchy/HierarchyView.xaml" })
        {
            var xaml = System.Xml.Linq.XDocument.Load(Path.Combine(src, file));
            var list = xaml.Descendants().Single(e => e.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value == "AddList");
            var rowStyle = list.Descendants().Single(e => e.Name.LocalName == "Style");
            Assert.DoesNotContain(rowStyle.Descendants(), e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "ToolTip");
            Assert.Contains(list.Descendants(), e => (string?)e.Attribute("Tag") == "info" && e.Attribute("ToolTip") is not null);
        }
    }

    [Fact]
    public void TheAddComponentList_ShowsTheSelectedComponentsDescription() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var entity = new Entity("Slime");
        workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, entity));
        services.GetRequiredService<ISelection>().Select(entity);
        var inspector = services.GetRequiredService<Griff.Editor.Features.Inspector.InspectorPanel>();
        inspector.IsAddOpen = true;
        var picker = inspector.Picker!;
        picker.Search = "patrol";
        Assert.Equal(picker.Selected!.Blurb, picker.SelectedBlurb);
        Assert.NotEmpty(picker.SelectedBlurb);
    });

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root wasn't found.");
    }
}
