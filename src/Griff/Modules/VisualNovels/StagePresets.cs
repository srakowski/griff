using System.Numerics;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// Creates a visual novel's stage that plays straight away: a stage filling the camera's view with a park and a room to
/// show (and a night sky in plain colors), Ava and Ben on it (off stage until the script brings them on), each with three
/// expressions, a dialogue box along the bottom whose lines type themselves out, and a short script that sets the scene,
/// brings them on, changes their faces, offers a choice and moves to the room. Click, tap or press Space or Enter to read
/// on. Pictures go in Assets/Stage, to repaint. All in one undo step.
/// </summary>
[Category("Visual Novels")]
public sealed class StagePreset : EntityPreset
{
    /// <summary>The script a new stage starts with: each kind of direction, an expression on a line, and a choice.</summary>
    public static IReadOnlyList<string> Script { get; } =
    [
        "[background Park]",
        "It's a bright morning in the park.",
        "[show Ava left happy] Ava: What a lovely day for a walk!",
        "[show Ben right slide] Ben (surprised): Ava? I didn't expect to see you here!",
        "Ava (happy): Ben! Come and sit with me.",
        "[move Ben center] Ben (happy): Don't mind if I do.",
        "Ava: Shall we stay out here, or go somewhere warmer?",
        "> Stay in the sun -> Park",
        "> Go home for tea -> Home",
        "# Park",
        "Ben (happy): The sun it is.",
        "-> End",
        "# Home",
        "[background Room fade] [hide Ben] [move Ava center] Ava (happy): Home at last. The kettle's on!",
        "[show Ben right] Ben: You read my mind.",
    ];

    public override string Id => "Entity.CreateStage";

    public override string Text => "Create Stage";

    public override int Order => 32;

    public override string? Icon => "Image";

    public override string EntityName(Scene scene) => StageNames.Free(scene, "Stage");

    // On its own: a stage isn't part of anything else.
    public override Entity? InsideOf(Entity? selected) => null;

    public override IReadOnlyDictionary<string, byte[]> Files() => StageArt.Files();

    public override void Configure(Entity entity, Scene scene)
    {
        var stage = entity.AddComponent<Stage>();
        stage.Backgrounds = [$"Park: {StageArt.Park}", $"Room: {StageArt.Room}", "Night: #0B1026 #2B3A67"];
        stage.Background = "Park";
        // As much as the camera sees, where it looks.
        if (scene.Entities.Select(e => e.GetComponent<Camera>()).OfType<Camera>().FirstOrDefault() is { } camera)
        {
            var at = camera.Entity!.Transform.Position;
            entity.Transform.Position = new Vector3(at.X, at.Y, 0);
            stage.Size = new Vector2(camera.Size * 2 * 16 / 9, camera.Size * 2);
        }

        var dialogue = entity.AddComponent<Dialogue>();
        dialogue.Start = DialogueStart.SceneStart;
        dialogue.PauseGame = false;
        dialogue.Box = StageNames.Box(scene) ?? "Dialogue Box";
        dialogue.Lines = Script;
    }

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        yield return Person("Ava", StageArt.AvaExpressions, "Left");
        yield return Person("Ben", StageArt.BenExpressions, "Right");
    }

    // Someone off stage until the script brings them on, standing at their position, looking neutral.
    private static Entity Person(string name, IReadOnlyList<string> expressions, string position)
    {
        var entity = new Entity(name);
        entity.AddComponent<Speaker>().Name = name;
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size) = (StageArt.Face(name, "Neutral"), new Vector2(3.2f, 5.76f));
        var character = entity.AddComponent<Character>();
        character.Expressions = [.. expressions.Select(e => $"{e}: {StageArt.Face(name, e)}")];
        character.Expression = "Neutral";
        character.Position = position;
        entity.Transform.Position = new Vector3(position == "Left" ? -4.5f : 4.5f, 0, 0);
        entity.IsEnabled = false;
        return entity;
    }

    public override IEnumerable<IEdit> EditScene(Scene scene)
    {
        if (StageNames.Box(scene) is not null)
            yield break;
        var (box, tag, line) = StageNames.MakeBox();
        yield return new AddEntityEdit(scene, box);
        yield return new AddEntityEdit(scene, tag, box);
        yield return new AddEntityEdit(scene, line, box);
    }
}

/// <summary>
/// Creates a character on a stage: inside the stage picked (or the one a character picked is on, or the scene's first),
/// standing at a free position, with a speaker named after them and three expressions to start from (pictures in
/// Assets/Stage, to repaint). Its lines start with its name ("Character: Hello!") and its stage directions name it too
/// ("[show Character left happy]").
/// </summary>
[Category("Visual Novels")]
public sealed class CharacterPreset : EntityPreset
{
    private static readonly string[] Expressions = ["Neutral", "Happy", "Sad"];

    public override string Id => "Entity.CreateCharacter";

    public override string Text => "Create Character";

    public override int Order => 33;

    public override string? Icon => "Person";

    public override string EntityName(Scene scene) => StageNames.Free(scene, "Character");

    public override Entity? InsideOf(Entity? selected) =>
        (selected is null ? null : Stage.StageOf(selected)?.Entity) ?? _scene?.Entities.FirstOrDefault(e => e.GetComponent<Stage>() is not null) ?? selected;

    // The scene it's being made in (Configure is told it before InsideOf is asked), for the stage to go on.
    private Scene? _scene;

    public override IReadOnlyDictionary<string, byte[]> Files() =>
        Expressions.ToDictionary(e => StageArt.Face("Character", e), e => StageArt.Generic(e).Png());

    public override void Configure(Entity entity, Scene scene)
    {
        _scene = scene;
        entity.AddComponent<Speaker>().Name = entity.Name;
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size) = (StageArt.Face("Character", "Neutral"), new Vector2(3.2f, 5.76f));
        var character = entity.AddComponent<Character>();
        character.Expressions = [.. Expressions.Select(e => $"{e}: {StageArt.Face("Character", e)}")];
        character.Expression = "Neutral";

        var stage = scene.Entities.Select(e => e.GetComponent<Stage>()).OfType<Stage>().FirstOrDefault();
        var spots = StageLines.Spots(stage?.Positions ?? Stage.DefaultPositions);
        var taken = stage?.Cast.Select(c => c.Position).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var spot = spots.FirstOrDefault(s => !taken.Contains(s.Name));
        if (spot.Name is null && spots.Count > 0)
            spot = spots[0];
        character.Position = spot.Name;
        entity.Transform.Position = new Vector3(spot.At.X, spot.At.Y, 0);
    }
}

/// <summary>Names and pieces the stage presets share.</summary>
internal static class StageNames
{
    /// <summary><paramref name="name"/>, or with a number after it if the scene has one already ("Stage 2").</summary>
    public static string Free(Scene scene, string name)
    {
        var taken = scene.Entities.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(name))
            return name;
        for (var i = 2; ; i++)
        {
            if (!taken.Contains($"{name} {i}"))
                return $"{name} {i}";
        }
    }

    /// <summary>The scene's dialogue box (one with a Dialogue Box component), or null.</summary>
    public static string? Box(Scene scene) => scene.Entities.FirstOrDefault(e => e.GetComponent<DialogueBox>() is not null)?.Name;

    /// <summary>
    /// A dialogue box for a visual novel: a wide panel pinned along the bottom of the screen whose lines type themselves
    /// out and wrap, with a name tag for whoever is talking (characters stand on the stage, so no portrait).
    /// </summary>
    public static (Entity Box, Entity NameTag, Entity Line) MakeBox()
    {
        var box = new Entity("Dialogue Box");
        var anchor = box.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.Bottom, new Vector2(0, 1.3f));
        var panel = box.AddComponent<Sprite>();
        (panel.Size, panel.Tint, panel.Order) = (new Vector2(15, 2.4f), Color.FromBytes(0x16, 0x19, 0x20) with { A = 0.88f }, 200);
        var layout = box.AddComponent<DialogueBox>();
        (layout.Portrait, layout.WrapWidth) = (null, 13.5f);

        // The lines start at the left, under the name tag.
        var line = new Entity("Line");
        line.Transform.Position = new Vector3(-6.9f, 0.1f, 0);
        var text = line.AddComponent<Text>();
        (text.Value, text.Size, text.Order, text.Align) = ("...", 0.45f, 201, TextAlign.Left);
        line.AddComponent<Typewriter>();

        var tag = new Entity("Name Tag");
        tag.Transform.Position = new Vector3(-5.7f, 1.55f, 0);
        var backdrop = tag.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(3.2f, 0.7f), Color.FromBytes(0x16, 0x19, 0x20) with { A = 0.92f }, 200);
        var name = tag.AddComponent<Text>();
        (name.Value, name.Size, name.Color, name.Order) = ("Name", 0.4f, Color.FromBytes(0xE3, 0xB3, 0x41), 201);
        return (box, tag, line);
    }
}
