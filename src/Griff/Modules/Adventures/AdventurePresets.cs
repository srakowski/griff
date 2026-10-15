using System.Numerics;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Modules.Adventures;

/// <summary>
/// Creates a walk area: a floor 8 wide and 3 deep where a <see cref="Walker"/> walks, its corners dragged in the Scene view
/// (and a corner added by dragging the plus on a side) to fit the room. Tick its Blocks for a hole in the floor.
/// </summary>
[Category("Adventures")]
public sealed class WalkAreaPreset : EntityPreset
{
    public override string Id => "Entity.CreateWalkArea";

    public override string Text => "Create Walk Area";

    public override int Order => 29;

    public override string? Icon => "Zone";

    public override string EntityName(Scene scene) => "Walk Area";

    public override void Configure(Entity entity, Scene scene)
    {
        entity.AddComponent<WalkArea>();
        entity.Transform.Position = new Vector3(0, -2, 0);
    }
}

/// <summary>
/// Creates a hotspot: an area 1.5 wide and 2 tall (a door's) with a <see cref="Hotspot"/> whose walker stands just in front
/// of it, with a line to say on looking at it. Drag its corners in the Scene view to fit what it is, and give it a line to
/// say on using it, an action, a flag or a dialogue.
/// </summary>
[Category("Adventures")]
public sealed class HotspotPreset : EntityPreset
{
    public override string Id => "Entity.CreateHotspot";

    public override string Text => "Create Hotspot";

    public override int Order => 30;

    public override string? Icon => "Tap";

    public override string EntityName(Scene scene) => "Hotspot";

    public override void Configure(Entity entity, Scene scene)
    {
        var hotspot = entity.AddComponent<Hotspot>();
        (hotspot.Points, hotspot.StandAt, hotspot.LookLine) = ("-0.75 -1; 0.75 -1; 0.75 1; -0.75 1", new Vector2(0, -1.5f), "Nothing special about it.");
    }
}

/// <summary>
/// Creates a walker: the player's character in a point-and-click adventure, a figure that walks where the player clicks,
/// round what's in the way, and walks over to look at and use hotspots. A scene with no walk area gets one for it to walk
/// on, all in one undo step.
/// </summary>
[Category("Adventures")]
public sealed class WalkerPreset : EntityPreset
{
    public override string Id => "Entity.CreateWalker";

    public override string Text => "Create Walker";

    public override int Order => 31;

    public override string? Icon => "Walker";

    public override string EntityName(Scene scene) => "Player";

    // Beside what's selected rather than inside it (a walk area, a hotspot), so it walks on its own.
    public override Entity? InsideOf(Entity? selected)
    {
        var parent = selected;
        while (parent is not null && (parent.GetComponent<WalkArea>() is not null || parent.GetComponent<Hotspot>() is not null))
            parent = parent.Parent;
        return parent;
    }

    public override void Configure(Entity entity, Scene scene)
    {
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint, sprite.Order) = (new Vector2(0.8f, 1.6f), Color.FromBytes(0x4C, 0x8D, 0xF0), 10);
        entity.AddComponent<Walker>();
        // Its feet on the walk area made with it.
        entity.Transform.Position = new Vector3(0, -1.7f, 0);
    }

    public override IEnumerable<IEdit> EditScene(Scene scene)
    {
        if (scene.Entities.Any(e => e.GetComponent<WalkArea>() is not null))
            yield break;
        var floor = new Entity("Walk Area");
        floor.AddComponent<WalkArea>();
        floor.Transform.Position = new Vector3(0, -2, 0);
        yield return new AddEntityEdit(scene, floor);
    }
}
