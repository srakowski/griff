using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.Scripts;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Big components in the Inspector: fields under subheadings that fold shut, and fields that fade while what they
/// depend on is off.
/// </summary>
public class InspectorGroupTests
{
    // Two players that run and jump, and a menu button.
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        var scene = new Scene();
        foreach (var name in new[] { "Player", "Player 2" })
        {
            var player = new Entity(name);
            player.AddComponent(new BoxCollider());
            player.AddComponent(new Body());
            player.AddComponent(new PlatformerMovement());
            scene.Add(player);
        }

        var button = new Entity("Play");
        button.AddComponent(new Button());
        scene.Add(button);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static ComponentSection Section(InspectorPanel inspector, string component) => inspector.Components.Single(c => c.IsNamed(component));

    private static PropertyField Field(InspectorPanel inspector, string component, string label) =>
        Section(inspector, component).Properties.Single(p => p.Label == label);

    [Fact]
    public void Fields_AreListedUnderTheirSubheadings() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Player"));
        var movement = Section(inspector, "Platformer Movement");

        Assert.Equal(["Jump", "Walls", "Dash", "Ladders and Platforms", "Control", "Sounds and Effects"], movement.Groups.Select(g => g.Name));
        Assert.Equal(["Dash", "Dash Speed", "Dash Time", "Dash Cooldown"], movement.Group("dash")!.Fields.Select(f => f.Label));
        // The rows: the ungrouped fields, then each heading followed by its fields.
        var rows = movement.Rows.Select(r => r is FieldGroup g ? $"# {g.Name}" : ((PropertyField)r).Label).ToList();
        Assert.Equal(["Speed", "Acceleration", "Deceleration", "Air Control", "# Jump", "Jump Height"], rows.Take(6));
        Assert.Equal(movement.Properties.Count + movement.Groups.Count, rows.Count);

        // A component with no subheadings lists just its fields.
        Assert.Empty(Section(inspector, "Body").Groups);
        Assert.Equal(Section(inspector, "Body").Properties, Section(inspector, "Body").Rows);
    });

    [Fact]
    public void FoldingASubheading_HidesItsFields_OnEveryEntity_AndNextTime() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var settings = services.GetRequiredService<ISettingsStore>();
        selection.Select(Named(document, "Player"));

        Section(inspector, "Platformer Movement").Group("Dash")!.IsExpanded = false;
        Assert.True(Field(inspector, "Platformer Movement", "Dash Speed").IsFolded);
        Assert.False(Field(inspector, "Platformer Movement", "Jump Height").IsFolded);
        Assert.Equal(["Griff.PlatformerMovement/Dash"], settings.Settings.CollapsedComponents);
        Assert.Equal("Open Dash: 4 fields", Section(inspector, "Platformer Movement").Group("Dash")!.Tip);

        selection.Select(Named(document, "Player 2"));
        Assert.False(Section(inspector, "Platformer Movement").Group("Dash")!.IsExpanded);
        Assert.True(Section(inspector, "Platformer Movement").IsExpanded);

        var next = new InspectorPanel(services.GetRequiredService<IWorkspace>(), selection, services.GetRequiredService<ScriptEditors>(),
            services.GetRequiredService<EditorArt>(), services.GetRequiredService<EditorAudio>(), services.GetRequiredService<IDialogService>(), services.GetRequiredService<IStatusService>(), settings);
        Assert.False(Section(next, "Platformer Movement").Group("Dash")!.IsExpanded);

        Section(next, "Platformer Movement").Group("Dash")!.ToggleCommand.Execute(null);
        Assert.Empty(settings.Settings.CollapsedComponents);
        Assert.False(Field(inspector, "Platformer Movement", "Dash Speed").IsFolded);
    });

    [Fact]
    public void DashFields_FadeWhileDashIsOff_AndComeBackWithIt() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Player"));
        var dashSpeed = Field(inspector, "Platformer Movement", "Dash Speed");

        Assert.True(dashSpeed.IsFaded);
        Assert.StartsWith("Only used when Dash is on.", dashSpeed.LabelTip);
        Assert.True(Section(inspector, "Platformer Movement").Group("Dash")!.IsFaded is false); // Dash itself isn't faded.
        Assert.True(Field(inspector, "Platformer Movement", "Wall Slide Speed").IsFaded);
        Assert.False(Field(inspector, "Platformer Movement", "Speed").IsFaded);

        // A faded field can still be set ahead of time.
        dashSpeed.NumberValue = 30;
        Assert.Equal(30, Named(document, "Player").GetComponent<PlatformerMovement>()!.DashSpeed);

        Field(inspector, "Platformer Movement", "Dash").BoolValue = true;
        Assert.False(Field(inspector, "Platformer Movement", "Dash Speed").IsFaded);
        Assert.Null(Field(inspector, "Platformer Movement", "Dash Speed").FadeTip);

        document.History.Undo();
        Assert.True(Field(inspector, "Platformer Movement", "Dash Speed").IsFaded);
    });

    [Fact]
    public void WithSeveralSelected_AFieldFades_OnlyWhenItsUnusedOnAllOfThem() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        Named(document, "Player").GetComponent<PlatformerMovement>()!.Dash = true;
        selection.SelectEntities([Named(document, "Player"), Named(document, "Player 2")]);

        Assert.False(Field(inspector, "Platformer Movement", "Dash Speed").IsFaded);
        Assert.True(Field(inspector, "Platformer Movement", "Wall Slide Speed").IsFaded);
    });

    [Fact]
    public void AButtonsFields_FadeUnlessItsActionUsesThem() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Play"));

        Assert.True(Field(inspector, "Button", "Scene").IsFaded);
        Assert.Equal("Only used when Action is Load Scene, New Game, Host Game or Leave Game.", Field(inspector, "Button", "Scene").FadeTip);
        // A faded field's name isn't flagged as matching nothing.
        Field(inspector, "Button", "Panel").TextValue = "Nowhere";
        Assert.False(Field(inspector, "Button", "Panel").IsUnmatched);

        Field(inspector, "Button", "Action").EnumValue = "Open Panel";
        Assert.False(Field(inspector, "Button", "Panel").IsFaded);
        Assert.True(Field(inspector, "Button", "Panel").IsUnmatched);
        Assert.True(Field(inspector, "Button", "Scene").IsFaded);
    });
}
