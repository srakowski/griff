using Griff.Editor.Features.Inspector;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The architecture test that keeps fields from landing as boxes to type code in (see <see cref="FieldCoverage"/>): every
/// field of every component, Griff's and the templates' scripts' alike, that takes text or a list of it (names, lists,
/// paths, a little language) has a picker or an editor of its own in the Inspector, or a [PlainText("why")] saying plain
/// text is right for it; and a whole number named like a tile is picked off the tileset. When one doesn't, the failure says
/// which and what to mark it.
/// </summary>
public sealed class FieldCoverageTests
{
    [Fact]
    public void EveryFieldTakingTextOrAList_HasAnEditor_OrSaysWhyPlainTextIsRight() => EditorHost.Run(services =>
    {
        var registry = services.GetRequiredService<Griff.Editor.Core.Workspace.Workspace>().Components;
        var host = services.GetRequiredService<InspectorPanel>();
        var gaps = registry.All.SelectMany(d => FieldCoverage.Gaps(d, host)).ToList();

        // The templates' scripts, compiled as a project of each compiles them, through the same projection.
        foreach (var template in ProjectTemplates.All)
        {
            var scripts = template.Scripts();
            if (scripts.Count == 0)
                continue;
            var compiled = ComponentRegistry.CreateDefault();
            var result = new ScriptHost(compiled).Reload([.. scripts.Select(s => new ScriptSource(s.Key, s.Value))], []);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            gaps.AddRange(result.ComponentTypes.Select(t => compiled.All.Single(d => d.Type == t))
                .SelectMany(d => FieldCoverage.Gaps(d, host)).Select(g => g with { Field = $"{g.Field} (the {template.Name} template's script)" }));
        }

        Assert.True(gaps.Count == 0, FieldCoverage.Message(gaps));
    });

    [Fact]
    public void AScriptFieldTypedFromMemory_IsCaught_SayingWhatToMarkIt() => EditorHost.Run(services =>
    {
        var host = services.GetRequiredService<InspectorPanel>();
        var registry = ComponentRegistry.CreateDefault();
        var result = new ScriptHost(registry).Reload([new ScriptSource("Scripts/Shop.cs", """
            using System.Collections.Generic;
            public class Shop : Behavior
            {
                public string? WinLevel { get; set; }
                public string? Greeting { get; set; } = "Welcome!";
                public IReadOnlyList<string> Stock { get; set; } = ["Sword: 30", "Shield: 20"];
                public int DoorTile { get; set; }

                [PlainText("words the shopkeeper says")]
                public string? Farewell { get; set; } = "Come again";

                [Names(NameKind.Item)]
                public string? Special { get; set; }

                public string? NextScene { get; set; }
                public float Speed { get; set; }
            }
            """)], []);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));

        var gaps = FieldCoverage.Gaps(registry.Find("Shop")!, host).ToList();
        Assert.Equal(["Shop.WinLevel", "Shop.Greeting", "Shop.Stock", "Shop.DoorTile"], gaps.Select(g => g.Field));
        Assert.Contains("[Names(NameKind.Scene)]", gaps[0].Advice);
        Assert.Contains("[Pairs]", gaps[2].Advice);
        Assert.Contains("[TileNumbers]", gaps[3].Advice);

        var message = FieldCoverage.Message(gaps);
        Assert.StartsWith("4 fields with no editor in the Inspector:", message);
        Assert.Contains("[PlainText(\"why\")]", message);
        Assert.Contains("editor.md", message);
    });

    [Fact]
    public void PlainText_MustSayWhy_AndFitWhatItsOn()
    {
        var registry = ComponentRegistry.CreateDefault();
        var empty = new ScriptHost(registry).Reload([new ScriptSource("Scripts/Sign.cs", """
            public class Sign : Behavior
            {
                [PlainText("")]
                public string? Words { get; set; }
            }
            """)], []);
        Assert.Contains(empty.Diagnostics.Concat<object>(empty.Warnings), d => d.ToString()!.Contains("without saying why", StringComparison.Ordinal));

        var both = new ScriptHost(ComponentRegistry.CreateDefault()).Reload([new ScriptSource("Scripts/Sign.cs", """
            public class Sign : Behavior
            {
                [PlainText("its own words"), Names(NameKind.Item)]
                public string? Words { get; set; }
            }
            """)], []);
        Assert.Contains(both.Diagnostics.Concat<object>(both.Warnings), d => d.ToString()!.Contains("one or the other", StringComparison.Ordinal));
    }
}
