using Griff.Art;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// A color field's picker: hue and value, alpha, the Sprite Painter's palette and recent colors, each change one undo
/// step.
/// </summary>
public class ColorPickerTests
{
    // Two sprites tinted differently.
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        var scene = new Scene();
        foreach (var (name, tint) in new[] { ("Gem", Color.White), ("Ruby", Color.Red) })
        {
            var entity = new Entity(name);
            entity.AddComponent(new Sprite { Tint = tint });
            scene.Add(entity);
        }

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static PropertyField Tint(InspectorPanel inspector) =>
        inspector.Components.Single(c => c.IsNamed("Sprite")).Properties.Single(p => p.PropertyName == "Tint");

    private static string TintOf(SceneDocument document, string name) => Named(document, name).GetComponent<Sprite>()!.Tint.ToHex();

    [Fact]
    public void OpeningThePicker_ShowsTheFieldsColor_AsHueSaturationAndValue() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Ruby"));
        var tint = Tint(inspector);

        Assert.Null(tint.ColorPicker);
        tint.ToggleColorCommand.Execute(null);
        var picker = tint.ColorPicker!;

        Assert.True(tint.IsColorOpen);
        Assert.Equal((0d, 1d, 1d, 1d), (picker.Hue, picker.Saturation, picker.Value, picker.Alpha));
        Assert.Equal("#FF0000", picker.Hex);

        // Only color fields have one.
        var position = inspector.Components.Single(c => c.IsNamed("Transform")).Properties[0];
        position.IsColorOpen = true;
        Assert.False(position.IsColorOpen);
    });

    [Fact]
    public void Dragging_SetsTheColorAsItGoes_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Ruby"));
        var tint = Tint(inspector);
        tint.IsColorOpen = true;
        var picker = tint.ColorPicker!;

        // Along the hue strip, then down the square, then the alpha strip: each move sets it.
        picker.Hue = 60;
        Assert.Equal("#FFFF00FF", TintOf(document, "Ruby"));
        picker.Hue = 120;
        picker.SetSaturationValue(1, 0.5);
        Assert.Equal("#008000FF", TintOf(document, "Ruby"));
        picker.Alpha = 0.5;
        Assert.Equal("#00800080", TintOf(document, "Ruby"));
        Assert.Equal("#00800080", picker.Hex);
        picker.Commit();

        // Letting go ends the step: undo takes the whole drag back.
        document.History.Undo();
        Assert.Equal("#FF0000FF", TintOf(document, "Ruby"));
        Assert.Equal("#FF0000", picker.Hex);
    });

    [Fact]
    public void Picking_KeepsTheHueOfGrays_AndBlack() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Ruby"));
        var tint = Tint(inspector);
        tint.IsColorOpen = true;
        var picker = tint.ColorPicker!;

        picker.Hue = 200;
        picker.SetSaturationValue(0.8, 0);
        Assert.Equal("#000000FF", TintOf(document, "Ruby"));
        // Brightening black again comes back in the hue and strength it had.
        picker.Value = 1;
        Assert.Equal(200, picker.Hue, 0.01);
        Assert.Equal(0.8, picker.Saturation, 0.01);
    });

    [Fact]
    public void Swatches_AreTheSpritePaintersPalette_AndPickingOneSetsIt() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        services.GetRequiredService<EditorArt>().Palette = Palette.Handheld;
        selection.Select(Named(document, "Gem"));
        var tint = Tint(inspector);
        tint.IsColorOpen = true;
        var picker = tint.ColorPicker!;

        Assert.Same(Palette.Handheld, picker.SelectedPalette);
        Assert.Equal(["#0F380F", "#306230", "#8BAC0F", "#9BBC0F"], picker.Swatches.Select(s => s.Hex));

        picker.Swatches[2].PickCommand.Execute(null);
        Assert.Equal("#8BAC0FFF", TintOf(document, "Gem"));
        Assert.True(picker.Swatches[2].IsCurrent);
        Assert.False(picker.Swatches[0].IsCurrent);

        // Choosing another palette here is the Sprite Painter's choice too.
        picker.SelectedPalette = Palette.Earth;
        Assert.Same(Palette.Earth, services.GetRequiredService<EditorArt>().Palette);
        Assert.Equal(16, picker.Swatches.Count);
    });

    [Fact]
    public void PickedColors_GoFirstInRecent_InEveryColorField_AndNextTime() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var settings = services.GetRequiredService<ISettingsStore>();
        selection.Select(Named(document, "Gem"));
        var tint = Tint(inspector);
        tint.IsColorOpen = true;
        var picker = tint.ColorPicker!;
        Assert.False(picker.HasRecent);

        picker.Pick("#29ADFF");
        picker.Pick("#FF004D");
        picker.Pick("#29ADFF");
        Assert.Equal(["#29ADFF", "#FF004D"], picker.Recent.Select(s => s.Hex));
        Assert.Equal(["#29ADFFFF", "#FF004DFF"], settings.Settings.RecentColors);
        // Each pick was its own undo step.
        document.History.Undo();
        Assert.Equal("#FF004DFF", TintOf(document, "Gem"));

        // A drag ends up in Recent when the mouse lets go (or the picker shuts).
        picker.Hue = 120;
        tint.IsColorOpen = false;
        Assert.Equal("#00FF00FF", settings.Settings.RecentColors[0]);

        // Another entity's color field has them too.
        selection.Select(Named(document, "Ruby"));
        Tint(inspector).IsColorOpen = true;
        Assert.Equal(["#00FF00", "#29ADFF", "#FF004D"], Tint(inspector).ColorPicker!.Recent.Select(s => s.Hex));

        // Recent keeps a dozen.
        for (var i = 0; i < 20; i++)
            Tint(inspector).ColorPicker!.Pick($"#0000{i:X2}");
        Assert.Equal(ColorPicker.RecentCount, settings.Settings.RecentColors.Count);
    });

    [Fact]
    public void WithSeveralSelected_PickingSetsEveryOne_AndUndoPutsEachBack() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.SelectEntities([Named(document, "Gem"), Named(document, "Ruby")]);
        var tint = Tint(inspector);
        Assert.True(tint.IsMixed);

        tint.IsColorOpen = true;
        tint.ColorPicker!.Pick("#FFEC27");
        Assert.Equal("#FFEC27FF", TintOf(document, "Gem"));
        Assert.Equal("#FFEC27FF", TintOf(document, "Ruby"));

        document.History.Undo();
        Assert.Equal("#FFFFFFFF", TintOf(document, "Gem"));
        Assert.Equal("#FF0000FF", TintOf(document, "Ruby"));
    });

    [Fact]
    public void TypingAHex_SetsIt_AndThePickerFollowsChangesMadeElsewhere() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Gem"));
        var tint = Tint(inspector);
        tint.IsColorOpen = true;
        var picker = tint.ColorPicker!;

        picker.Hex = "#0000FF80";
        Assert.Equal("#0000FF80", TintOf(document, "Gem"));
        Assert.Equal(240, picker.Hue, 0.01);
        Assert.Equal(0.5, picker.Alpha, 0.01);

        // The field's own hex box, while the picker is open.
        tint.ColorHex = "#00FF00";
        Assert.Equal(120, picker.Hue, 0.01);
        Assert.Throws<ArgumentException>(() => picker.Pick("green"));
    });
}
