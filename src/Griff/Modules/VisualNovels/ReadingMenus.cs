using System.Numerics;
using Griff.Editing;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// Creates a visual novel's menus for reading, with no code: a row of buttons at the top of the screen (Log, Skip, Auto,
/// Quick Save, Quick Load, Save, Load and Settings), save and load screens of six slots each (a screenshot, when it was
/// saved and the line being said, and an X to delete it), a log of the lines said (opened with H, the gamepad's left
/// shoulder, a button or the mouse wheel), and settings for how quickly Auto reads on, whether Skip goes through lines
/// not read yet, and the volume. All in one undo step.
/// </summary>
[Category("Visual Novels")]
public sealed class ReadingMenusPreset : EntityPreset
{
    public override string Id => "Entity.CreateReadingMenus";

    public override string Text => "Create Reading Menus";

    public override int Order => 34;

    public override string? Icon => "Save";

    public override string EntityName(Scene scene) => StageNames.Free(scene, ReadingMenus.Name);

    // On its own: menus pin themselves to the screen.
    public override Entity? InsideOf(Entity? selected) => null;

    // The new entity (Configure is told it before the scene is changed), for its parts to go in.
    private Entity? _root;

    public override void Configure(Entity entity, Scene scene) => _root = entity;

    public override IEnumerable<IEdit> EditScene(Scene scene)
    {
        if (_root is not { } root)
            yield break;
        foreach (var (part, parent) in ReadingMenus.Parts())
            yield return new AddEntityEdit(scene, part, parent ?? root);
    }
}

/// <summary>The pieces of a visual novel's reading menus (see <see cref="ReadingMenusPreset"/>), for presets and templates.</summary>
public static class ReadingMenus
{
    /// <summary>The name of the entity they go in.</summary>
    public const string Name = "Reading Menus";

    /// <summary>The panels' names, which the buttons open.</summary>
    public const string SaveScreen = "Save Screen", LoadScreen = "Load Screen", Log = "Log Screen", Settings = "Reading Settings";

    private static readonly Color Back = new(0.06f, 0.07f, 0.09f, 0.96f);
    private static readonly Color Card = Color.FromBytes(0x25, 0x29, 0x33);
    private static readonly Color Pale = Color.FromBytes(0xE6, 0xE8, 0xEC);
    private static readonly Color Gold = Color.FromBytes(0xE3, 0xB3, 0x41);
    private static readonly Color Blue = Color.FromBytes(0x4C, 0x9A, 0xFF);

    /// <summary>
    /// Every entity of the menus, each with the one it goes inside (null for the entity they all go in), parents before
    /// their children: add them in order.
    /// </summary>
    public static IReadOnlyList<(Entity Entity, Entity? Parent)> Parts()
    {
        var parts = new List<(Entity, Entity?)>();
        QuickMenu(parts);
        SlotScreen(parts, SaveScreen, "Save", ButtonAction.SaveGame);
        SlotScreen(parts, LoadScreen, "Load", ButtonAction.LoadGame);
        LogScreen(parts);
        SettingsScreen(parts);
        return parts;
    }

    /// <summary>Adds the menus to <paramref name="scene"/>, inside a new entity called <see cref="Name"/>, and returns it.</summary>
    public static Entity AddTo(Scene scene)
    {
        var root = new Entity(Name);
        scene.Add(root);
        foreach (var (part, parent) in Parts())
            scene.Add(part, parent ?? root);
        return root;
    }

    // A row of small buttons along the top of the screen.
    private static void QuickMenu(List<(Entity, Entity?)> parts)
    {
        var menu = new Entity("Quick Menu");
        // Where its anchor puts it on a 16:9 screen of a camera at the middle, so it's in place in the Scene view too.
        menu.Transform.Position = new Vector3(2.39f, 4.55f, 0);
        var anchor = menu.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.TopRight, new Vector2(-6.5f, -0.45f));
        parts.Add((menu, null));

        (string Label, Action<Button, Entity> Set)[] buttons =
        [
            ("Log", (b, _) => (b.Action, b.Panel) = (ButtonAction.OpenPanel, Log)),
            ("Skip", (_, e) => Toggled(e, GameSetting.Skip, "Skip >>", "Skip")),
            ("Auto", (_, e) => Toggled(e, GameSetting.Auto, "Auto >", "Auto")),
            ("Q.Save", (b, _) => b.Action = ButtonAction.QuickSave),
            ("Q.Load", (b, _) => (b.Action, b.Fade) = (ButtonAction.QuickLoad, 0.3f)),
            ("Save", (b, _) => (b.Action, b.Panel) = (ButtonAction.OpenPanel, SaveScreen)),
            ("Load", (b, _) => (b.Action, b.Panel) = (ButtonAction.OpenPanel, LoadScreen)),
            ("Settings", (b, _) => (b.Action, b.Panel) = (ButtonAction.OpenPanel, Settings)),
        ];
        for (var i = 0; i < buttons.Length; i++)
        {
            var (label, set) = buttons[i];
            var button = SmallButton(label, new Vector2((i - (buttons.Length - 1) / 2f) * 1.6f, 0), new Vector2(1.5f, 0.5f), 250);
            // A row on the screen during play: the arrow keys are for the talk's choices, not these.
            button.GetComponent<Button>()!.Navigable = false;
            set(button.GetComponent<Button>()!, button);
            parts.Add((button, menu));
        }

        static void Toggled(Entity entity, GameSetting setting, string on, string off)
        {
            var toggle = entity.AddComponent<Toggle>();
            (toggle.Setting, toggle.IsOn, toggle.OnText, toggle.OffText) = (setting, false, on, off);
        }
    }

    // A save or load screen: six slots, each with an X to delete what's in it, and a way back.
    private static void SlotScreen(List<(Entity, Entity?)> parts, string name, string heading, ButtonAction action)
    {
        var screen = Screen(parts, name, heading);
        for (var i = 0; i < 6; i++)
        {
            var at = new Vector2((i % 3 - 1) * 5.2f, i < 3 ? 1.5f : -1.9f);
            var card = new Entity($"Slot {i + 1}");
            card.Transform.Position = new Vector3(at, 0);
            var back = card.AddComponent<Sprite>();
            (back.Size, back.Tint, back.Order) = (new Vector2(4.6f, 3.2f), Card, 305);
            var button = card.AddComponent<Button>();
            (button.Size, button.Action, button.Fade) = (new Vector2(4.6f, 3.2f), action, action == ButtonAction.LoadGame ? 0.3f : 0);
            var slot = card.AddComponent<SaveSlot>();
            (slot.Slot, slot.PictureSize, slot.PictureOffset, slot.TextOffset) = ($"{i + 1}", new Vector2(4, 2.25f), new Vector2(0, 0.4f), new Vector2(0, -0.95f));
            parts.Add((card, screen));

            // In the corner, over the card: Delete Save with no slot of its own deletes the card's.
            var delete = SmallButton("x", new Vector2(2.05f, 1.35f), new Vector2(0.45f, 0.45f), 330);
            delete.Name = "Delete";
            delete.GetComponent<Button>()!.Action = ButtonAction.DeleteSave;
            delete.GetComponent<Sprite>()!.Tint = Color.FromBytes(0x8A, 0x2E, 0x2E);
            parts.Add((delete, card));
        }

        parts.Add((BackButton(new Vector2(0, -4.2f)), screen));
    }

    private static void LogScreen(List<(Entity, Entity?)> parts)
    {
        var screen = Screen(parts, Log, "Log");
        screen.GetComponent<Panel>()!.OpenOn = "Log";
        var lines = new Entity("Lines");
        lines.Transform.Position = new Vector3(-0.3f, -0.1f, 0);
        var log = lines.AddComponent<Backlog>();
        (log.Size, log.Order) = (new Vector2(14, 6.6f), 310);
        parts.Add((lines, screen));
        var hint = Label("Hint", new Vector2(0, -3.7f), "Wheel, arrows or drag to scroll", 0.28f, Pale with { A = 0.5f });
        parts.Add((hint, screen));
        parts.Add((BackButton(new Vector2(0, -4.3f)), screen));
    }

    private static void SettingsScreen(List<(Entity, Entity?)> parts)
    {
        var screen = Screen(parts, Settings, "Settings");
        parts.Add((Label("Auto Speed Label", new Vector2(0, 2.3f), "Auto speed", 0.4f, Pale), screen));
        parts.Add((SettingSlider("Auto Speed", new Vector2(0, 1.6f), GameSetting.AutoSpeed), screen));

        var skip = SmallButton("Skip Unread", new Vector2(0, 0.2f), new Vector2(7, 0.9f), 310);
        skip.GetComponent<Text>()!.Size = 0.4f;
        var toggle = skip.AddComponent<Toggle>();
        (toggle.Setting, toggle.IsOn, toggle.OnText, toggle.OffText) = (GameSetting.SkipUnread, false, "Skip: all lines", "Skip: lines read before");
        parts.Add((skip, screen));

        parts.Add((Label("Volume Label", new Vector2(0, -1.1f), "Volume", 0.4f, Pale), screen));
        parts.Add((SettingSlider("Volume", new Vector2(0, -1.8f), GameSetting.Volume), screen));
        parts.Add((BackButton(new Vector2(0, -4.2f)), screen));
    }

    // A panel filling the screen with a heading, closed with its Back button, Esc or the gamepad's B.
    private static Entity Screen(List<(Entity, Entity?)> parts, string name, string heading)
    {
        var screen = new Entity(name);
        // Hidden until it opens, so it doesn't cover the stage in the Scene view (show it there to lay it out).
        screen.IsEnabled = false;
        screen.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        screen.AddComponent<Panel>();
        var back = screen.AddComponent<Sprite>();
        (back.Size, back.Tint, back.Order) = (new Vector2(18, 10.2f), Back, 300);
        parts.Add((screen, null));
        parts.Add((Label("Heading", new Vector2(0, 4.2f), heading, 0.7f, Gold), screen));
        return screen;
    }

    private static Entity SettingSlider(string name, Vector2 at, GameSetting setting)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        var slider = entity.AddComponent<Slider>();
        (slider.Setting, slider.Size, slider.Order, slider.Fill) = (setting, new Vector2(6, 0.4f), 310, Blue);
        return entity;
    }

    private static Entity BackButton(Vector2 at)
    {
        var back = SmallButton("Back", at, new Vector2(2.6f, 0.7f), 310);
        back.GetComponent<Button>()!.Action = ButtonAction.ClosePanel;
        back.GetComponent<Text>()!.Size = 0.4f;
        return back;
    }

    private static Entity SmallButton(string label, Vector2 at, Vector2 size, int order)
    {
        var entity = new Entity(label);
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint, sprite.Order) = (size, Color.FromBytes(0x3A, 0x3F, 0x4B) with { A = 0.92f }, order);
        var text = entity.AddComponent<Text>();
        (text.Value, text.Size, text.Color, text.Order) = (label, Math.Min(0.3f, size.Y * 0.6f), Pale, order + 1);
        entity.AddComponent<Button>().Size = size;
        return entity;
    }

    private static Entity Label(string name, Vector2 at, string value, float size, Color color)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        var text = entity.AddComponent<Text>();
        (text.Value, text.Size, text.Color, text.Order) = (value, size, color, 310);
        return entity;
    }
}
