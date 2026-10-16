using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.Scripts;
using Griff.Editor.Themes.Controls;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Fields that explain themselves and change quickly: tooltips from their documentation, names as they read, number steps
/// that grow with the value, and sections that stay folded.
/// </summary>
public class InspectorFieldTests
{
    // A player that runs and jumps, and a lift that goes back and forth.
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var player = new Entity("Player");
        player.AddComponent(new BoxCollider());
        player.AddComponent(new Body());
        player.AddComponent(new PlatformerMovement());
        scene.Add(player);
        var lift = new Entity("Lift");
        lift.AddComponent(new Mover());
        scene.Add(lift);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<Workspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static ComponentSection Section(InspectorPanel inspector, string component) => inspector.Components.Single(c => c.IsNamed(component));

    private static PropertyField Field(InspectorPanel inspector, string component, string label) =>
        Section(inspector, component).Properties.Single(p => p.Label == label);

    [Fact]
    public void ComponentNames_ReadAsWords() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Player"));

        Assert.Equal(["Transform", "Box Collider", "Body", "Platformer Movement"], inspector.Components.Select(c => c.Title));
        var movement = Section(inspector, "PlatformerMovement");
        Assert.Same(movement, Section(inspector, "Platformer Movement"));
        Assert.Equal("PlatformerMovement", movement.TypeName);
        Assert.StartsWith("A platformer's player with no code", movement.Tip);
        Assert.Equal("Platformer Movement", InspectorPanel.Humanize("PlatformerMovement"));
    });

    [Fact]
    public void FieldNames_ExplainTheField() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Player"));

        var acceleration = Field(inspector, "PlatformerMovement", "Acceleration");
        Assert.Equal("How quickly it gets up to speed, in units per second per second. 0 is at once.", acceleration.Summary);
        // A number says how to drag it, after what it is.
        Assert.StartsWith(acceleration.Summary + "\n\nDrag the name sideways", acceleration.LabelTip);

        var jump = Field(inspector, "PlatformerMovement", "Wall Slide");
        Assert.Equal(jump.Summary, jump.LabelTip);
        Assert.Contains("world units", Field(inspector, "Transform", "Position").Summary);
    });

    [Fact]
    public void Choices_ReadAsWords_AndSayWhatTheyDo() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Lift"));

        var mode = Field(inspector, "Mover", "Mode");
        Assert.Equal(["Ping Pong", "Loop", "Once"], mode.EnumChoices.Select(c => c.Label));
        Assert.StartsWith("To the last point, then back the same way", mode.EnumChoices[0].Tip);

        // Picking one sets the value it stands for.
        mode.EnumValue = mode.EnumChoices.Single(c => c.Label == "Once").Value;
        Assert.Equal(MoverMode.Once, Named(document, "Lift").GetComponent<Mover>()!.Mode);

        // Agents name a choice as it reads, or as in code.
        mode.EnumValue = "ping pong";
        Assert.Equal(MoverMode.PingPong, Named(document, "Lift").GetComponent<Mover>()!.Mode);
        mode.EnumValue = "Loop";
        Assert.Equal(MoverMode.Loop, Named(document, "Lift").GetComponent<Mover>()!.Mode);
        Assert.Throws<ArgumentException>(() => mode.EnumValue = "Sideways");
    });

    [Fact]
    public void FoldedSections_StayFolded_OnOtherEntities_AndNextTime() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var settings = services.GetRequiredService<ISettingsStore>();
        selection.Select(Named(document, "Player"));

        Section(inspector, "Transform").IsExpanded = false;
        Assert.Equal(["Transform"], settings.Settings.CollapsedComponents);

        // Another entity's Transform is folded too, and its other sections aren't.
        selection.Select(Named(document, "Lift"));
        Assert.False(Section(inspector, "Transform").IsExpanded);
        Assert.True(Section(inspector, "Mover").IsExpanded);

        // The next time the editor starts, it's still folded.
        var next = new InspectorPanel(services.GetRequiredService<Workspace>(), services.GetRequiredService<Workspace>(), services.GetRequiredService<Workspace>(), selection, services.GetRequiredService<ScriptEditors>(),
            services.GetRequiredService<EditorArt>(), services.GetRequiredService<EditorAudio>(), services.GetRequiredService<IDialogService>(), services.GetRequiredService<IStatusService>(), settings, services.GetRequiredService<Lazy<CommandRegistry>>(), services.GetRequiredService<PrefabFiles>());
        Assert.False(Section(next, "Transform").IsExpanded);

        Section(next, "Transform").IsExpanded = true;
        Assert.Empty(settings.Settings.CollapsedComponents);
        Assert.True(Section(inspector, "Transform").IsExpanded);
    });

    [Fact]
    public void StartingOver_OpensEverything_AndLeavesTheUsersSettingsAlone() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var settings = services.GetRequiredService<ISettingsStore>();
        selection.Select(Named(document, "Player"));
        Section(inspector, "Body").IsExpanded = false;

        inspector.StartOver();
        Assert.True(Section(inspector, "Body").IsExpanded);
        Section(inspector, "Transform").IsExpanded = false;

        Assert.Equal(["Body"], settings.Settings.CollapsedComponents);
    });

    [Fact]
    public void AName_DragsTheOneNumberBesideIt() => EditorHost.Run(_ =>
    {
        // A field's row: its name, and beside it (a level further in) its number.
        var name = new System.Windows.Controls.TextBlock { Text = "Acceleration" };
        var number = new NumberField();
        var row = new System.Windows.Controls.Grid();
        var label = new System.Windows.Controls.DockPanel();
        label.Children.Add(name);
        row.Children.Add(label);
        var editor = new System.Windows.Controls.Border { Child = number };
        row.Children.Add(editor);
        Assert.Same(number, NumberField.FieldNear(name));

        // A vector's axes are several numbers: its name drags none of them.
        var axes = new System.Windows.Controls.StackPanel();
        editor.Child = axes;
        axes.Children.Add(number);
        axes.Children.Add(new NumberField());
        Assert.Null(NumberField.FieldNear(name));
    });

    [Theory]
    [InlineData(70, 0.35)]   // Acceleration 70 reaches 90 in under 60 pixels
    [InlineData(0, 0.05)]
    [InlineData(0.5, 0.05)]
    [InlineData(-400, 2)]
    public void DragSteps_GrowWithTheValue(double value, double perPixel) =>
        Assert.Equal(perPixel, NumberField.DragStep(value, 0.05), 6);

    [Fact]
    public void DragSteps_MoveWholeNumbersEveryFewPixels() => Assert.Equal(0.2, NumberField.DragStep(3, 0.05, isInteger: true), 6);

    [Theory]
    [InlineData(70, false, 1)]
    [InlineData(7, false, 0.1)]
    [InlineData(0.5, false, 0.01)]
    [InlineData(0, false, 0.1)]
    [InlineData(250, false, 10)]
    [InlineData(3, true, 1)]
    [InlineData(250, true, 10)]
    public void Nudges_AreATenthOfTheValuesSize(double value, bool isInteger, double step) =>
        Assert.Equal(step, NumberField.NudgeStep(value, isInteger), 6);
}
