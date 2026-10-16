using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Makes the selected entities break on hard knocks: each gets a Breakable (cracking as hits add up, splitting into pieces
/// once they reach its Strength), and a Body that rotates, so it tumbles and topples, and a box collider the size of its
/// sprite if it has no collider. Set its Strength and Min Hit in the Inspector, or give it a Table Values on a Materials
/// table. One undo step.
/// </summary>
public sealed class AddBreakableAction : EntityAction
{
    public override string Id => "Entity.AddBreakable";

    public override string Text => "Add _Breakable";

    public override int Order => 13;

    public override bool EachSelected => true;

    public override string NotApplicable => "Select what should break on hard knocks.";

    public override bool AppliesTo(Entity entity) => true;

    public override IEnumerable<IEdit> Edits(Entity entity, Scene scene, ComponentRegistry components)
    {
        if (entity.GetComponent<Collider>() is null)
        {
            var size = entity.GetComponent<Sprite>()?.Size ?? System.Numerics.Vector2.One;
            yield return new AddComponentEdit(entity, new BoxCollider { Size = System.Numerics.Vector2.Abs(size) });
        }

        if (entity.GetComponent<Body>() is not { } body)
            yield return new AddComponentEdit(entity, new Body { Rotates = true });
        else if (!body.Rotates)
            yield return new SetPropertyEdit(body, components.Get(typeof(Body)).Properties.Single(p => p.Name == nameof(Body.Rotates)), true);
        if (entity.GetComponent<Breakable>() is null)
            yield return new AddComponentEdit(entity, new Breakable());
    }
}
