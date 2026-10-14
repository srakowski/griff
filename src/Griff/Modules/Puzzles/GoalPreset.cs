using System.Numerics;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Modules.Puzzles;

/// <summary>
/// Creates a goal for grid crates: a flat square drawn under them with a <see cref="Goal"/> whose All Covered Message is
/// "Solved", sent once every goal in the scene has a crate on it. The first goal in a scene also gets a
/// <see cref="MoveHistory"/>, so the player can undo moves (Z) and restart (R).
/// </summary>
public sealed class GoalPreset : EntityPreset
{
    public override string Id => "Entity.CreateGoal";

    public override string Text => "Create Goa_l";

    public override int Order => 19;

    public override string EntityName(Scene scene) => "Goal";

    public override void Configure(Entity entity, Scene scene)
    {
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.6f, 0.6f);
        sprite.Tint = Color.FromBytes(0xE8, 0xC5, 0x4A, 0xCC);
        sprite.Order = -1;
        entity.AddComponent<Goal>().AllCoveredMessage = "Solved";
        if (!scene.Entities.Any(e => e.GetComponent<MoveHistory>() is not null))
            entity.AddComponent<MoveHistory>();
    }
}
