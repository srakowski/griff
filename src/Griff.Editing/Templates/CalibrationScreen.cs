using System.Numerics;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Templates;

/// <summary>
/// A calibration screen ready to use, as Entity &gt; Audio &gt; Create Calibration Screen and the Arcade template's options
/// make it: a panel (closed until something opens it) with a <see cref="Calibration"/>, a heading, the calibration's prompt,
/// a slider to fine-tune the offset by hand, and Apply, Again and Back buttons. Opening it starts the clicks.
/// </summary>
public static class CalibrationScreen
{
    /// <summary>What the panel is called, for a button to open it.</summary>
    public const string Name = "Calibrate";

    private static readonly Color Orange = Color.FromBytes(0xF0, 0x8C, 0x2E);
    private static readonly Color Light = Color.FromBytes(0xE6, 0xE8, 0xEC);
    private static readonly Color Gray = Color.FromBytes(0x7D, 0x83, 0x90);

    /// <summary>Makes <paramref name="panel"/> the screen: a backdrop in the middle of the view, its panel and its calibration.</summary>
    public static void Configure(Entity panel, CalibrationKind measures = CalibrationKind.Audio)
    {
        panel.Transform.Position = new Vector3(0, 0, 3);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(10, 6.6f);
        backdrop.Tint = Color.FromBytes(0x16, 0x19, 0x20);
        backdrop.Order = 300;
        panel.AddComponent<Panel>();
        panel.AddComponent<Calibration>().Measures = measures;
    }

    /// <summary>
    /// What goes inside it: the heading, the prompt, the offset's slider and the buttons (Apply and Again do the calibration's
    /// actions on <paramref name="panel"/>, the entity's name; Back closes it, as Esc and the gamepad's B do).
    /// </summary>
    public static IEnumerable<Entity> Children(string panel = Name, CalibrationKind measures = CalibrationKind.Audio)
    {
        // Says which offset it sets, as its calibration does.
        var heading = Label("Heading", new Vector2(0, 2.55f), "{0}", 0.75f, Orange);
        heading.GetComponent<Text>()!.Output = nameof(Calibration.Title);
        yield return heading;

        var prompt = Label("Prompt", new Vector2(0, 1.45f), "{0}", 0.34f, Light);
        prompt.GetComponent<Text>()!.Output = nameof(Calibration.Prompt);
        yield return prompt;

        // The offset in use, to fine-tune by hand: a slider bound to it, and how much it is.
        var tune = new Entity("Offset");
        tune.Transform.Position = new Vector3(-0.5f, -1.75f, 0);
        var slider = tune.AddComponent<Slider>();
        (slider.Size, slider.Min, slider.Max, slider.Step, slider.Order) = (new Vector2(5, 0.32f), -200, 500, 5, 310);
        slider.Setting = measures == CalibrationKind.Visual ? GameSetting.VisualOffset : GameSetting.AudioOffset;
        yield return tune;

        var readout = Label("Offset ms", new Vector2(2.4f, -1.75f), "{0:0} ms", 0.34f, Gray);
        var text = readout.GetComponent<Text>()!;
        (text.Output, text.Align) = (nameof(Calibration.Current), TextAlign.Left);
        yield return readout;

        yield return MakeButton("Apply", -3, ButtonAction.Do, "Apply", panel, Key.None, Color.FromBytes(0x4C, 0x9A, 0xFF));
        yield return MakeButton("Again", 0, ButtonAction.Do, "Start", panel, Key.None, Color.FromBytes(0x3A, 0x3F, 0x4B));
        yield return MakeButton("Back", 3, ButtonAction.ClosePanel, null, null, Key.Escape, Color.FromBytes(0x3A, 0x3F, 0x4B));
    }

    private static Entity Label(string name, Vector2 position, string value, float size, Color color)
    {
        var entity = ProjectTemplates.Label(name, position, value, size, color);
        entity.GetComponent<Text>()!.Order = 301;
        return entity;
    }

    private static Entity MakeButton(string label, float x, ButtonAction action, string? @do, string? targets, Key shortcut, Color color)
    {
        var entity = ProjectTemplates.MenuButton(label, new Vector2(x, -2.65f), action, null, shortcut, color, order: 310);
        var size = new Vector2(2.6f, 0.85f);
        entity.GetComponent<Sprite>()!.Size = size;
        entity.GetComponent<Text>()!.Size = 0.45f;
        var button = entity.GetComponent<Button>()!;
        (button.Size, button.Do, button.Targets) = (size, @do, targets);
        return entity;
    }
}
