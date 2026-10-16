using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Modules.PhysicsGames;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.Entities;

/// <summary>
/// Makes the selected entities break on hard knocks: each gets a Breakable (cracking as hits add up, splitting into pieces
/// once they reach its Strength), and a Body that rotates, so it tumbles and topples, and a box collider the size of its
/// sprite if it has no collider. Set its Strength and Min Hit in the Inspector, or give it a Table Values on a Materials
/// table. One undo step.
/// </summary>
public sealed class AddBreakableCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.AddBreakable";
    public override string Text => "Add _Breakable";
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 13);

    protected override bool CanExecuteCore() => sceneDocuments.Scene is not null && selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is not { } document || selection.Entities.Count == 0)
            throw new InvalidOperationException("Select what should break on hard knocks.");

        using (document.History.BeginGroup("Add Breakable"))
        {
            foreach (var entity in selection.Entities.ToList())
            {
                if (entity.GetComponent<Collider>() is null)
                {
                    var size = entity.GetComponent<Sprite>()?.Size ?? System.Numerics.Vector2.One;
                    document.Execute(new AddComponentEdit(entity, new BoxCollider { Size = System.Numerics.Vector2.Abs(size) }));
                }

                if (entity.GetComponent<Body>() is not { } body)
                    document.Execute(new AddComponentEdit(entity, new Body { Rotates = true }));
                else if (!body.Rotates)
                    document.Execute(new SetPropertyEdit(body, Property(typeof(Body), nameof(Body.Rotates)), true));
                if (entity.GetComponent<Breakable>() is null)
                    document.Execute(new AddComponentEdit(entity, new Breakable()));
            }
        }

        document.History.Seal();
        return Task.CompletedTask;
    }

    private PropertyDescriptor Property(Type component, string name) =>
        projectSession.Components.Get(component).Properties.Single(p => p.Name == name);
}
