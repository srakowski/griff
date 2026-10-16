using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.Entities;

/// <summary>
/// Hinges the selected entity at its middle so it turns there: to the world, or with two entities selected, to the one
/// picked first (pick the cart, then Ctrl+click the wheel). It gets a Hinge Joint, and a Body that rotates (and a box
/// collider, without a collider). Drag the hinge's knob in the Scene view to move it (to a door's end), and tick Limit or
/// Motor in the Inspector. One undo step.
/// </summary>
public sealed class AddHingeCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.AddHinge";
    public override string Text => "Add _Hinge";
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 11);

    protected override bool CanExecuteCore() => sceneDocuments.Scene is not null && selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } entity)
            throw new InvalidOperationException("Select the entity to hinge (and, to hinge it to another, pick that one first).");
        var other = selection.Entities.Count > 1 ? selection.Entities[^2] : null;

        using (document.History.BeginGroup("Add Hinge"))
        {
            if (entity.GetComponent<Collider>() is null)
                document.Execute(new AddComponentEdit(entity, new BoxCollider()));
            if (entity.GetComponent<Body>() is not { } body)
                document.Execute(new AddComponentEdit(entity, new Body { Rotates = true }));
            else if (!body.Rotates)
                document.Execute(new SetPropertyEdit(body, Property(typeof(Body), nameof(Body.Rotates)), true));
            document.Execute(new AddComponentEdit(entity, new HingeJoint { To = other?.Name, Anchor = entity.GetComponent<Collider>()!.Offset }));
        }

        document.History.Seal();
        return Task.CompletedTask;
    }

    private PropertyDescriptor Property(Type component, string name) =>
        projectSession.Components.Get(component).Properties.Single(p => p.Name == name);
}
