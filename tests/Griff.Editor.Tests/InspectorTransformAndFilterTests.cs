using System.Numerics;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.Scripts;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Transforms in 2D (X and Y, one rotation angle) with Z behind a 3D switch, and the filters at the top of the Inspector
/// and the Hierarchy.
/// </summary>
public class InspectorTransformAndFilterTests
{
    // A player that runs and jumps, a level holding a slime and a coin, and a slime on its own.
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var player = new Entity("Player");
        player.AddComponent(new BoxCollider());
        player.AddComponent(new Body());
        player.AddComponent(new PlatformerMovement());
        player.AddComponent(new Health());
        scene.Add(player);

        var level = new Entity("Level");
        scene.Add(level);
        scene.Add(new Entity("Slime"), level);
        scene.Add(new Entity("Coin"), level);
        scene.Add(new Entity("Big Slime"));

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static ComponentSection Section(InspectorPanel inspector, string component) => inspector.Components.Single(c => c.IsNamed(component));

    private static PropertyField Field(InspectorPanel inspector, string component, string label) =>
        Section(inspector, component).Properties.Single(p => p.Label == label);

    private static string[] Shown(PropertyField field) => [.. field.Axes.Where(a => a.IsShown).Select(a => a.ShownLabel ?? "angle")];

    // ----- Transforms

    [Fact]
    public void Transforms_ShowIn2D_XAndY_AndOneRotationAngle() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Player"));

        Assert.True(Section(inspector, "Transform").IsTransform);
        Assert.False(Section(inspector, "Transform").Is3D);
        Assert.Equal(["X", "Y"], Shown(Field(inspector, "Transform", "Position")));
        Assert.Equal(["angle"], Shown(Field(inspector, "Transform", "Rotation")));
        Assert.Equal(["X", "Y"], Shown(Field(inspector, "Transform", "Scale")));
        Assert.Null(Section(inspector, "Transform").HiddenAxesNote);
        // Other vectors keep every axis.
        Assert.False(Section(inspector, "Body").IsTransform);

        // The one angle is the rotation round Z, and sets just that.
        Field(inspector, "Transform", "Rotation").Axes[2].Value = 45;
        Assert.Equal(new Vector3(0, 0, 45), Named(document, "Player").Transform.Rotation);
    });

    [Fact]
    public void The3DSwitch_ShowsZ_OnEveryEntity_AndNextTime() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var settings = services.GetRequiredService<ISettingsStore>();
        selection.Select(Named(document, "Player"));

        Section(inspector, "Transform").Is3D = true;
        Assert.True(settings.Settings.Transform3D);
        Assert.Equal(["X", "Y", "Z"], Shown(Field(inspector, "Transform", "Position")));
        Assert.Equal(["X", "Y", "Z"], Shown(Field(inspector, "Transform", "Rotation")));

        selection.Select(Named(document, "Coin"));
        Assert.True(Section(inspector, "Transform").Is3D);

        var next = new InspectorPanel(services.GetRequiredService<IWorkspace>(), selection, services.GetRequiredService<ScriptEditors>(),
            services.GetRequiredService<EditorArt>(), services.GetRequiredService<EditorAudio>(), services.GetRequiredService<IDialogService>(), services.GetRequiredService<IStatusService>(), settings);
        Assert.True(next.Transform3D);
        Assert.Equal(["X", "Y", "Z"], Shown(Field(next, "Transform", "Scale")));

        next.Transform3D = false;
        Assert.Equal(["X", "Y"], Shown(Field(inspector, "Transform", "Scale")));
    });

    [Fact]
    public void In2D_ValuesOnlyZShows_AreNamed_AndStillCount() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var slime = Named(document, "Slime");
        slime.Transform.Position = new Vector3(1, 2, 3);
        slime.Transform.Scale = new Vector3(2, 2, 0.5f);
        selection.Select(slime);

        Assert.Equal("Also set in 3D: Position Z 3, Scale Z 0.5", Section(inspector, "Transform").HiddenAxesNote);

        // Setting X in 2D keeps the Z.
        Field(inspector, "Transform", "Position").Axes[0].Value = 5;
        Assert.Equal(new Vector3(5, 2, 3), slime.Transform.Position);

        Section(inspector, "Transform").Is3D = true;
        Assert.Null(Section(inspector, "Transform").HiddenAxesNote);
    });

    // ----- The Inspector's filter

    [Fact]
    public void TheFieldFilter_ShowsOnlyFieldsWhoseNamesMatch_AndComponentsWithThem() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Player"));
        var movement = Section(inspector, "Platformer Movement");
        Assert.All(movement.Properties, p => Assert.False(p.IsFilteredOut));

        inspector.Filter = "speed";
        Assert.True(inspector.IsFiltering);
        Assert.False(Field(inspector, "Platformer Movement", "Speed").IsFilteredOut);
        Assert.False(Field(inspector, "Platformer Movement", "Dash Speed").IsFilteredOut);
        Assert.True(Field(inspector, "Platformer Movement", "Jump Height").IsFilteredOut);
        Assert.True(Section(inspector, "Health").IsFilteredOut);
        Assert.True(Section(inspector, "Transform").IsFilteredOut);
        Assert.False(movement.IsFilteredOut);
        // A subheading with nothing that matches goes too.
        Assert.True(movement.Group("Sounds and Effects")!.IsFilteredOut);
        Assert.False(movement.Group("Dash")!.IsFilteredOut);
        Assert.InRange(inspector.FilterMatches, 3, movement.Properties.Count);
        Assert.Null(inspector.FilterNote);

        // Names as in code match too.
        inspector.Filter = "InvincibleTime";
        Assert.False(Section(inspector, "Health").IsFilteredOut);
    });

    [Fact]
    public void TheFieldFilter_ShowsAllOfAComponentOrSubheadingWhoseNameMatches() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Player"));

        inspector.Filter = "health";
        Assert.All(Section(inspector, "Health").Properties, p => Assert.False(p.IsFilteredOut));

        inspector.Filter = "dash";
        Assert.All(Section(inspector, "Platformer Movement").Group("Dash")!.Fields, f => Assert.False(f.IsFilteredOut));

        inspector.Filter = "zzz";
        Assert.Equal("No fields match \"zzz\"", inspector.FilterNote);
        Assert.All(inspector.Components, c => Assert.True(c.IsFilteredOut));

        inspector.Filter = "";
        Assert.All(inspector.Components, c => Assert.False(c.IsFilteredOut));
        Assert.Null(inspector.FilterNote);
    });

    [Fact]
    public void TheFieldFilter_ShowsMatchesInFoldedSections_AndStaysAcrossSelections() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Player"));
        var movement = Section(inspector, "Platformer Movement");
        movement.Group("Dash")!.IsExpanded = false;
        movement.IsExpanded = false;
        Assert.False(movement.ShowsFields);
        Assert.True(Field(inspector, "Platformer Movement", "Dash Speed").IsHiddenRow);

        inspector.Filter = "dash speed";
        Assert.True(movement.ShowsFields);
        Assert.False(Field(inspector, "Platformer Movement", "Dash Speed").IsHiddenRow);
        // Folding stays as it was, for when the filter's emptied.
        Assert.False(movement.IsExpanded);

        selection.Select(Named(document, "Big Slime"));
        Assert.Equal("dash speed", inspector.Filter);
        Assert.True(Section(inspector, "Transform").IsFilteredOut);

        selection.Select(Named(document, "Player"));
        Assert.False(Field(inspector, "Platformer Movement", "Dash Speed").IsFilteredOut);

        inspector.Filter = "";
        Assert.False(Section(inspector, "Platformer Movement").ShowsFields);
    });

    // ----- The Hierarchy's filter

    private static EntityNode? Find(IEnumerable<EntityNode> nodes, string name)
    {
        foreach (var node in nodes)
        {
            if (node.Name == name)
                return node;
            if (Find(node.Children, name) is { } found)
                return found;
        }

        return null;
    }

    [Fact]
    public void TheEntityFilter_ShowsMatches_WithWhatTheyreInside_Opened() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var panel = services.GetRequiredService<HierarchyPanel>();
        EntityNode Node(string name) => Find(panel.Roots, name)!;
        Assert.False(Node("Level").IsExpanded);

        panel.Filter = "slime";
        Assert.Equal(["Slime", "Big Slime"], panel.FilterMatches.Select(e => e.Name));
        Assert.True(Node("Player").IsFilteredOut);
        Assert.True(Node("Coin").IsFilteredOut);
        Assert.False(Node("Level").IsFilteredOut);
        Assert.True(Node("Level").IsFilterContext);
        Assert.True(Node("Level").IsExpanded);
        Assert.False(Node("Slime").IsFilterContext);

        panel.Filter = "nothing";
        Assert.Equal("No entities match \"nothing\"", panel.FilterNote);

        // Emptying it puts the tree back as it was.
        panel.Filter = "";
        Assert.False(Node("Level").IsExpanded);
        Assert.False(Node("Player").IsFilteredOut);
        Assert.Null(panel.FilterNote);
    });

    [Fact]
    public void TheEntityFilter_StaysAsTheSceneChanges_AndShiftClicksSkipWhatItHides() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var panel = services.GetRequiredService<HierarchyPanel>();
        EntityNode Node(string name) => Find(panel.Roots, name)!;

        panel.Filter = "slime";
        document.Execute(new Griff.Editing.AddEntityEdit(document.Scene, new Entity("Slime 2")));
        Assert.False(Node("Slime 2").IsFilteredOut);
        Assert.True(Node("Player").IsFilteredOut);

        // From Slime to Big Slime, past the coin the filter hides.
        panel.Click(Node("Slime"), control: false, shift: false);
        panel.Click(Node("Big Slime"), control: false, shift: true);
        Assert.Equal(["Slime", "Big Slime"], selection.Entities.Select(e => e.Name));
    });
}
