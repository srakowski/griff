using System.Numerics;
using Griff.Scenes;

namespace Griff.Editing.Presets;

/// <summary>What the menu controls (a button, a toggle, a choice) are made of.</summary>
internal static class MenuControl
{
    /// <summary>A background sprite, a label and a Button of the same size; the sprite's draw order is set when it's arranged.</summary>
    public static Griff.Scenes.Text Make(Entity entity, Vector2 size, string label)
    {
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Color.FromBytes(0x3A, 0x3F, 0x4B);
        var text = entity.AddComponent<Griff.Scenes.Text>();
        text.Value = label;
        text.Size = 0.5f;
        entity.AddComponent<Griff.Scenes.Button>().Size = size;
        return text;
    }

    /// <summary>Puts the control's sprite above what it's inside.</summary>
    public static void Arrange(Entity entity, Entity? inside) => entity.GetComponent<Sprite>()!.Order = Runtime.Views.OrderAbove(inside, 100);
}

/// <summary>Creates a clickable button: a background sprite, a label and a Button set to do nothing until configured.</summary>
[Category("UI")]
public sealed class ButtonPreset : EntityPreset
{
    public override string Id => "Entity.CreateButton";
    public override string Text => "Create _Button";
    public override int Order => 0;
    public override string? Icon => "Tap";
    public override string EntityName(Scene scene) => "Button";
    public override void Configure(Entity entity, Scene scene) => MenuControl.Make(entity, new Vector2(3, 1), "Button");
    public override void Arrange(Entity entity, IReadOnlyList<Entity> children, Entity? inside) => MenuControl.Arrange(entity, inside);
}

/// <summary>Creates a slider for an options menu; set its Setting to a volume and it needs no code.</summary>
[Category("UI")]
public sealed class SliderPreset : EntityPreset
{
    public override string Id => "Entity.CreateSlider";
    public override string Text => "Create Sl_ider";
    public override int Order => 2;
    public override string? Icon => "Sliders";
    public override string EntityName(Scene scene) => "Slider";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Slider>();

    public override void Arrange(Entity entity, IReadOnlyList<Entity> children, Entity? inside) =>
        entity.GetComponent<Slider>()!.Order = Runtime.Views.OrderAbove(inside, 100);
}

/// <summary>Creates an on/off switch: a button whose label says which way it's set.</summary>
[Category("UI")]
public sealed class TogglePreset : EntityPreset
{
    public override string Id => "Entity.CreateToggle";
    public override string Text => "Create T_oggle";
    public override int Order => 1;
    public override string? Icon => "Checkbox";
    public override string EntityName(Scene scene) => "Toggle";

    public override void Configure(Entity entity, Scene scene)
    {
        MenuControl.Make(entity, new Vector2(4, 1), "Option: On");
        var toggle = entity.AddComponent<Toggle>();
        toggle.OnText = "Option: On";
        toggle.OffText = "Option: Off";
    }

    public override void Arrange(Entity entity, IReadOnlyList<Entity> children, Entity? inside) => MenuControl.Arrange(entity, inside);
}

/// <summary>Creates a button that picks one of several options, changed with left and right: "Difficulty: Normal".</summary>
[Category("UI")]
public sealed class ChoicePreset : EntityPreset
{
    public override string Id => "Entity.CreateChoice";
    public override string Text => "Create C_hoice";
    public override int Order => 3;
    public override string? Icon => "LeftRight";
    public override string EntityName(Scene scene) => "Choice";

    public override void Configure(Entity entity, Scene scene)
    {
        MenuControl.Make(entity, new Vector2(4, 1), "< Normal >");
        entity.AddComponent<Choice>().Format = "< {0} >";
    }

    public override void Arrange(Entity entity, IReadOnlyList<Entity> children, Entity? inside) => MenuControl.Arrange(entity, inside);
}

/// <summary>Creates a bar for a HUD; set its Track to show an entity's health.</summary>
[Category("UI")]
public sealed class BarPreset : EntityPreset
{
    public override string Id => "Entity.CreateBar";
    public override string Text => "Create B_ar";
    public override int Order => 4;
    public override string? Icon => "Battery";
    public override string EntityName(Scene scene) => "Bar";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Bar>();
}

/// <summary>
/// Creates a chart for a HUD or a panel: a line graph of a series a script feeds, or that samples an output of its Track
/// entity with no code.
/// </summary>
[Category("UI")]
public sealed class ChartPreset : EntityPreset
{
    public override string Id => "Entity.CreateChart";
    public override string Text => "Create Char_t";
    public override int Order => 4;
    public override string? Icon => "Chart";
    public override string EntityName(Scene scene) => "Chart";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Chart>();
}

/// <summary>
/// Creates a row of three stars, so many filled: how well a level went. Set its Saved to a level's best stars in the save
/// data (on a level select menu), or its Track and Output to a Level Goal's Stars (on the panel a won level opens).
/// </summary>
[Category("UI")]
public sealed class StarsPreset : EntityPreset
{
    public override string Id => "Entity.CreateStars";
    public override string Text => "Create Sta_rs";
    public override int Order => 4;
    public override string? Icon => "Star";
    public override string EntityName(Scene scene) => "Stars";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Stars>().Value = 2;
}

/// <summary>
/// Creates a panel: a backdrop pinned to the middle of the screen with a <see cref="Panel"/>, hidden while the game runs
/// until it's opened. Select it and create buttons to put them on it; set its Open On to "Pause" for a pause menu.
/// </summary>
[Category("UI")]
public sealed class PanelPreset : EntityPreset
{
    public override string Id => "Entity.CreatePanel";
    public override string Text => "Create Pa_nel";
    public override int Order => 5;
    public override string? Icon => "Window";
    public override string EntityName(Scene scene) => "Panel";

    public override void Configure(Entity entity, Scene scene)
    {
        entity.Transform.Position = new Vector3(0, 0, 3);
        entity.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var backdrop = entity.AddComponent<Sprite>();
        backdrop.Size = new Vector2(7, 5);
        backdrop.Tint = Color.FromBytes(0x16, 0x19, 0x20) with { A = 0.92f };
        backdrop.Order = 300;
        entity.AddComponent<Panel>();
    }
}

/// <summary>
/// Creates the box a <see cref="Dialogue"/> talks in: a panel pinned to the bottom of the screen whose text types itself
/// out, with a name tag and a portrait above it for whoever is talking (see <see cref="Speaker"/>). It's hidden while the
/// game runs until someone talks.
/// </summary>
[Category("Story")]
public sealed class DialogueBoxPreset : EntityPreset
{
    public override string Id => "Entity.CreateDialogueBox";
    public override string Text => "Create _Dialogue Box";
    public override int Order => 0;
    public override string? Icon => "Speech";
    public override string EntityName(Scene scene) => "Dialogue Box";

    public override void Configure(Entity entity, Scene scene)
    {
        var anchor = entity.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 1.4f);
        var panel = entity.AddComponent<Sprite>();
        panel.Size = new Vector2(14, 2.2f);
        panel.Tint = Color.FromBytes(0x16, 0x19, 0x20) with { A = 0.9f };
        panel.Order = 200;
        var text = entity.AddComponent<Griff.Scenes.Text>();
        text.Value = "...";
        text.Size = 0.5f;
        text.Order = 201;
        entity.AddComponent<Typewriter>();
        entity.AddComponent<DialogueBox>();
    }

    // A portrait standing on the box's top-left corner, and the speaker's name in a tag beside it.
    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        var portrait = new Entity("Portrait");
        portrait.Transform.Position = new Vector3(-5.8f, 2.15f, 0);
        var picture = portrait.AddComponent<Sprite>();
        (picture.Size, picture.Order) = (new Vector2(2, 2), 201);
        yield return portrait;

        var tag = new Entity("Name Tag");
        tag.Transform.Position = new Vector3(-3.1f, 1.45f, 0);
        var backdrop = tag.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(3.2f, 0.7f), Color.FromBytes(0x16, 0x19, 0x20) with { A = 0.9f }, 200);
        var name = tag.AddComponent<Griff.Scenes.Text>();
        (name.Value, name.Size, name.Color, name.Order) = ("Name", 0.4f, Color.FromBytes(0xE3, 0xB3, 0x41), 201);
        yield return tag;
    }
}

/// <summary>
/// Creates an arrow for a HUD that points the way to something off the screen: an amber arrow with an
/// <see cref="EdgePointer"/>, a Button that glides the camera there when it's clicked (Do: Glide), and how far off it is
/// written beside it. Set its Target to what it points at.
/// </summary>
[Category("UI")]
public sealed class EdgePointerPreset : EntityPreset
{
    /// <summary>The arrow's picture, pointing right, put in the project when it has none there.</summary>
    public const string Arrow = "Textures/edge-arrow.png";

    public override string Id => "Entity.CreateEdgePointer";
    public override string Text => "Create _Edge Pointer";
    public override int Order => 6;
    public override string? Icon => "MoveRight";
    public override string EntityName(Scene scene) => "Edge Pointer";
    public override IReadOnlyDictionary<string, byte[]> Files() => new Dictionary<string, byte[]> { [Arrow] = ArrowArt().Png() };

    public override void Configure(Entity entity, Scene scene)
    {
        entity.AddComponent<EdgePointer>();
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (Arrow, new Vector2(1, 1), 250);
        var button = entity.AddComponent<Griff.Scenes.Button>();
        (button.Size, button.Action, button.Do, button.Navigable) = (new Vector2(1, 1), ButtonAction.Do, "Glide", false);
    }

    // How far off the target is, behind the arrow (so on the screen's side of it), kept upright.
    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        var label = new Entity("Distance");
        label.Transform.Position = new Vector3(-1.1f, 0, 0);
        var text = label.AddComponent<Griff.Scenes.Text>();
        (text.Value, text.Output, text.Size, text.Order) = ("{0:0}", "Distance", 0.4f, 251);
        yield return label;
    }

    // An amber arrowhead on a short shaft, pointing right, outlined dark so it reads on any ground.
    private static Templates.TemplateArt.Canvas ArrowArt()
    {
        var c = new Templates.TemplateArt.Canvas(16, 16, seed: 47);
        var amber = Templates.TemplateArt.Rgb(0xE3, 0xB3, 0x41);
        c.Rect(2, 6, 7, 4, amber);
        Templates.TemplateArt.Polygon(c, [(8, 1.5f), (15, 8), (8, 14.5f)], amber);
        c.Rect(2, 6, 7, 1, amber.Times(1.2f));
        c.Outline(Templates.TemplateArt.Rgb(0x2A, 0x1E, 0x0C));
        return c;
    }
}
