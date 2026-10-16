using System.Numerics;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Editing;

public class EditHistoryTests
{
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();
    private readonly EditHistory _history = new();
    private readonly Scene _scene = new();

    private PropertyDescriptor PositionProperty =>
        _registry.Get(typeof(Transform)).Properties.Single(p => p.Name == nameof(Transform.Position));

    [Fact]
    public void SetLocked_LocksSeveral_AndUndoesEachToItsOwnState()
    {
        var (a, b) = (new Entity("A"), new Entity("B") { IsLocked = true });

        _history.Execute(new SetEntitiesLockedEdit([a, b], locked: true));
        Assert.True(a.IsLocked && b.IsLocked);
        Assert.Equal("Lock 2 entities", _history.UndoDescription);

        _history.Undo();
        Assert.False(a.IsLocked);
        Assert.True(b.IsLocked);

        _history.Execute(new SetEntitiesLockedEdit([b], locked: false));
        Assert.Equal("Unlock B", _history.UndoDescription);
        Assert.False(b.IsLocked);
    }

    [Fact]
    public void SetHidden_HidesSeveral_AndUndoesEachToItsOwnState()
    {
        var (a, b) = (new Entity("A"), new Entity("B") { IsHidden = true });

        _history.Execute(new SetEntitiesHiddenEdit([a, b], hidden: true));
        Assert.True(a.IsHidden && b.IsHidden);
        Assert.Equal("Hide 2 entities", _history.UndoDescription);

        _history.Undo();
        Assert.False(a.IsHidden);
        Assert.True(b.IsHidden);

        _history.Execute(new SetEntitiesHiddenEdit([b], hidden: false));
        Assert.Equal("Show B", _history.UndoDescription);
        Assert.False(b.IsHidden);
    }

    [Fact]
    public void UndoRedo_RestoresStateExactly()
    {
        var entity = new Entity("Player");
        _history.Execute(new AddEntityEdit(_scene, entity));
        _history.Execute(new RenameEntityEdit(entity, "Hero"));

        _history.Undo();
        Assert.Equal("Player", entity.Name);
        _history.Undo();
        Assert.Equal(0, _scene.Count);

        _history.Redo();
        _history.Redo();
        Assert.Same(entity, _scene.Find(entity.Id));
        Assert.Equal("Hero", entity.Name);
    }

    [Fact]
    public void Execute_ClearsRedo()
    {
        var entity = new Entity("A");
        _history.Execute(new AddEntityEdit(_scene, entity));
        _history.Undo();

        _history.Execute(new AddEntityEdit(_scene, new Entity("B")));

        Assert.False(_history.CanRedo);
    }

    [Fact]
    public void PropertyEdits_MergeIntoOneStep_UntilSealed()
    {
        var entity = new Entity();
        _scene.Add(entity);

        for (var x = 1; x <= 50; x++)
            _history.Execute(new SetPropertyEdit(entity.Transform, PositionProperty, new Vector3(x, 0, 0)));
        _history.Seal();
        _history.Execute(new SetPropertyEdit(entity.Transform, PositionProperty, new Vector3(99, 0, 0)));

        _history.Undo();
        Assert.Equal(new Vector3(50, 0, 0), entity.Transform.Position);
        _history.Undo();
        Assert.Equal(Vector3.Zero, entity.Transform.Position);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public void PropertyEdits_OnDifferentComponents_DoNotMerge()
    {
        var a = new Entity("A");
        var b = new Entity("B");
        _scene.Add(a);
        _scene.Add(b);

        _history.Execute(new SetPropertyEdit(a.Transform, PositionProperty, Vector3.One));
        _history.Execute(new SetPropertyEdit(b.Transform, PositionProperty, Vector3.One));

        _history.Undo();
        Assert.Equal(Vector3.One, a.Transform.Position);
        Assert.Equal(Vector3.Zero, b.Transform.Position);
    }

    [Fact]
    public void Group_UndoesAsOneStep()
    {
        using (_history.BeginGroup("Create Player"))
        {
            var entity = new Entity("Entity");
            _history.Execute(new AddEntityEdit(_scene, entity));
            _history.Execute(new RenameEntityEdit(entity, "Player"));
        }

        Assert.Equal("Create Player", _history.UndoDescription);
        _history.Undo();
        Assert.Equal(0, _scene.Count);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public void IsDirty_FollowsHistory()
    {
        var entity = new Entity("A");
        Assert.False(_history.IsDirty);

        _history.Execute(new AddEntityEdit(_scene, entity));
        Assert.True(_history.IsDirty);

        _history.MarkSaved();
        Assert.False(_history.IsDirty);

        _history.Execute(new RenameEntityEdit(entity, "B"));
        Assert.True(_history.IsDirty);

        _history.Undo();
        Assert.False(_history.IsDirty);

        _history.Undo();
        Assert.True(_history.IsDirty);
    }

    [Fact]
    public void Edit_AfterSave_DoesNotMergeIntoSavedState()
    {
        var entity = new Entity();
        _scene.Add(entity);
        _history.Execute(new SetPropertyEdit(entity.Transform, PositionProperty, Vector3.One));
        _history.MarkSaved();

        _history.Execute(new SetPropertyEdit(entity.Transform, PositionProperty, new Vector3(2, 2, 2)));

        Assert.True(_history.IsDirty);
        _history.Undo();
        Assert.Equal(Vector3.One, entity.Transform.Position);
        Assert.False(_history.IsDirty);
    }

    [Fact]
    public void RemoveEntity_Undo_RestoresParentAndPosition()
    {
        var parent = new Entity("Parent");
        var a = new Entity("A");
        var b = new Entity("B");
        var c = new Entity("C");
        _scene.Add(parent);
        _scene.Add(a, parent);
        _scene.Add(b, parent);
        _scene.Add(c, parent);

        _history.Execute(new RemoveEntityEdit(_scene, b));
        Assert.Equal([a, c], parent.Children);

        _history.Undo();
        Assert.Equal([a, b, c], parent.Children);
    }

    [Fact]
    public void MoveEntity_Undo_RestoresParentAndPosition()
    {
        var a = new Entity("A");
        var b = new Entity("B");
        var c = new Entity("C");
        _scene.Add(a);
        _scene.Add(b);
        _scene.Add(c);

        _history.Execute(new MoveEntityEdit(_scene, a, c));
        Assert.Equal([b, c], _scene.Roots);

        _history.Undo();
        Assert.Equal([a, b, c], _scene.Roots);
        Assert.Null(a.Parent);
    }

    [Fact]
    public void RemoveComponent_Undo_RestoresPosition()
    {
        var entity = new Entity();
        var first = entity.AddComponent<Scenes.TestComponent>();
        var second = entity.AddComponent<Scenes.RenamedComponent>();

        _history.Execute(new RemoveComponentEdit(first));
        _history.Undo();

        Assert.Equal<Component>([entity.Transform, first, second], entity.Components);
    }

    [Fact]
    public void Changed_ReportsActions()
    {
        var actions = new List<EditAction>();
        _history.Changed += (_, e) => actions.Add(e.Action);

        _history.Execute(new AddEntityEdit(_scene, new Entity()));
        _history.Undo();
        _history.Redo();

        Assert.Equal([EditAction.Execute, EditAction.Undo, EditAction.Redo], actions);
    }

    [Fact]
    public void Undo_DuringGroup_Throws()
    {
        _history.Execute(new AddEntityEdit(_scene, new Entity()));

        using (_history.BeginGroup("Open"))
            Assert.Throws<InvalidOperationException>(() => _history.Undo());
    }

    [Fact]
    public void SetPositions_MovesSeveral_MergesWhileUnsealed_AndUndoesInOneStep()
    {
        var (a, b) = (new Entity("A"), new Entity("B"));
        b.Transform.Position = new Vector3(5, 0, 1);
        Transform[] both = [a.Transform, b.Transform];

        _history.Seal();
        _history.Execute(new SetPositionsEdit(both, [new Vector3(1, 0, 0), new Vector3(6, 0, 1)]));
        _history.Execute(new SetPositionsEdit(both, [new Vector3(2, 3, 0), new Vector3(7, 3, 1)]));
        Assert.Equal(new Vector3(7, 3, 1), b.Transform.Position);
        Assert.Equal("Move 2 entities", _history.UndoDescription);

        // A different set of entities is its own step.
        _history.Execute(new SetPositionsEdit([b.Transform], [Vector3.Zero]));
        _history.Undo();
        Assert.Equal(new Vector3(7, 3, 1), b.Transform.Position);

        _history.Undo();
        Assert.Equal(Vector3.Zero, a.Transform.Position);
        Assert.Equal(new Vector3(5, 0, 1), b.Transform.Position);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public void RotateEntities_TurnsAndSwingsSeveral_MergesWhileUnsealed_AndUndoesInOneStep()
    {
        var (a, b) = (new Entity("A"), new Entity("B"));
        b.Transform.Position = new Vector3(2, 0, 1);
        b.Transform.Rotation = new Vector3(0, 0, 10);
        Transform[] both = [a.Transform, b.Transform];

        _history.Seal();
        _history.Execute(new RotateEntitiesEdit(both, [Vector3.Zero, new Vector3(0, 2, 1)], [new Vector3(0, 0, 90), new Vector3(0, 0, 100)]));
        _history.Execute(new RotateEntitiesEdit(both, [Vector3.Zero, new Vector3(-2, 0, 1)], [new Vector3(0, 0, 180), new Vector3(0, 0, 190)]));
        Assert.Equal(new Vector3(-2, 0, 1), b.Transform.Position);
        Assert.Equal(new Vector3(0, 0, 190), b.Transform.Rotation);
        Assert.Equal("Rotate 2 entities", _history.UndoDescription);

        // A different set of entities is its own step.
        _history.Execute(new RotateEntitiesEdit([a.Transform], [Vector3.Zero], [new Vector3(0, 0, 45)]));
        Assert.Equal("Rotate A", _history.UndoDescription);
        _history.Undo();
        Assert.Equal(new Vector3(0, 0, 180), a.Transform.Rotation);

        _history.Undo();
        Assert.Equal(Vector3.Zero, a.Transform.Rotation);
        Assert.Equal(new Vector3(2, 0, 1), b.Transform.Position);
        Assert.Equal(new Vector3(0, 0, 10), b.Transform.Rotation);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public void ScaleEntities_ScalesAndMovesSeveral_MergesWhileUnsealed_AndUndoesInOneStep()
    {
        var (a, b) = (new Entity("A"), new Entity("B"));
        b.Transform.Position = new Vector3(2, 0, 1);
        b.Transform.Scale = new Vector3(1, 3, 1);
        Transform[] both = [a.Transform, b.Transform];

        _history.Seal();
        _history.Execute(new ScaleEntitiesEdit(both, [Vector3.Zero, new Vector3(3, 0, 1)], [new Vector3(1.5f, 1, 1), new Vector3(1.5f, 3, 1)]));
        _history.Execute(new ScaleEntitiesEdit(both, [Vector3.Zero, new Vector3(4, 0, 1)], [new Vector3(2, 1, 1), new Vector3(2, 3, 1)]));
        Assert.Equal(new Vector3(4, 0, 1), b.Transform.Position);
        Assert.Equal(new Vector3(2, 3, 1), b.Transform.Scale);
        Assert.Equal("Scale 2 entities", _history.UndoDescription);

        // A different set of entities is its own step.
        _history.Execute(new ScaleEntitiesEdit([a.Transform], [Vector3.Zero], [new Vector3(5, 1, 1)]));
        Assert.Equal("Scale A", _history.UndoDescription);
        _history.Undo();
        Assert.Equal(new Vector3(2, 1, 1), a.Transform.Scale);

        _history.Undo();
        Assert.Equal(Vector3.One, a.Transform.Scale);
        Assert.Equal(new Vector3(2, 0, 1), b.Transform.Position);
        Assert.Equal(new Vector3(1, 3, 1), b.Transform.Scale);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public void SetProperties_SetsSeveral_MergesWhileUnsealed_AndUndoesEachToItsOwnValue()
    {
        var (a, b) = (new Entity("A"), new Entity("B"));
        b.Transform.Position = new Vector3(5, 0, 1);
        Component[] both = [a.Transform, b.Transform];

        // Nothing to change: no edit. One component: a plain property edit.
        Assert.Null(SetPropertiesEdit.For(both, PositionProperty, [Vector3.Zero, new Vector3(5, 0, 1)]));
        Assert.IsType<SetPropertyEdit>(SetPropertiesEdit.For([a.Transform], PositionProperty, [Vector3.One]));

        _history.Seal();
        _history.Execute(SetPropertiesEdit.For(both, PositionProperty, [Vector3.One, Vector3.One])!);
        _history.Execute(SetPropertiesEdit.For(both, PositionProperty, [new Vector3(2), new Vector3(2)])!);
        Assert.Equal(new Vector3(2), a.Transform.Position);
        Assert.Equal(new Vector3(2), b.Transform.Position);
        Assert.Equal("Change Position on 2 entities", _history.UndoDescription);

        _history.Undo();
        Assert.Equal(Vector3.Zero, a.Transform.Position);
        Assert.Equal(new Vector3(5, 0, 1), b.Transform.Position);
        Assert.False(_history.CanUndo);

        _history.Redo();
        Assert.Equal(new Vector3(2), b.Transform.Position);
    }

    [Fact]
    public void SetPropertyGroup_SetsTogether_MergesWhileUnsealed_AndUndoesInOneStep()
    {
        var entity = new Entity("Plank");
        var sprite = entity.AddComponent<Sprite>();
        var size = _registry.Get(typeof(Sprite)).Properties.Single(p => p.Name == nameof(Sprite.Size));

        _history.Seal();
        _history.Execute(new SetPropertyGroupEdit("Resize Sprite", [(sprite, size, new Vector2(2, 1)), (entity.Transform, PositionProperty, new Vector3(0.5f, 0, 0))]));
        _history.Execute(new SetPropertyGroupEdit("Resize Sprite", [(sprite, size, new Vector2(3, 1)), (entity.Transform, PositionProperty, new Vector3(1, 0, 0))]));
        Assert.Equal(new Vector2(3, 1), sprite.Size);
        Assert.Equal(new Vector3(1, 0, 0), entity.Transform.Position);
        Assert.Equal("Resize Sprite", _history.UndoDescription);

        // Other properties are their own step.
        _history.Execute(new SetPropertyGroupEdit("Resize Sprite", [(sprite, size, new Vector2(4, 1))]));
        _history.Undo();
        Assert.Equal(new Vector2(3, 1), sprite.Size);

        _history.Undo();
        Assert.Equal(Vector2.One, sprite.Size);
        Assert.Equal(Vector3.Zero, entity.Transform.Position);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public void SetProperties_FollowsRebuiltComponents()
    {
        var (a, b) = (new Entity("A"), new Entity("B"));
        _history.Execute(new SetPropertiesEdit([a.Transform, b.Transform], PositionProperty, [Vector3.One, Vector3.One]));

        // A script reload swaps components for new ones: the edit points at those, and still undoes.
        var replacement = new Transform { Position = Vector3.One };
        _history.RemapComponents(new Dictionary<Component, Component> { [b.Transform] = replacement }, _registry);
        _history.Undo();
        Assert.Equal(Vector3.Zero, a.Transform.Position);
        Assert.Equal(Vector3.Zero, replacement.Position);
    }

    [Fact]
    public void RecordDone_KeepsAChangeAlreadyMade_ToUndo()
    {
        var entity = new Entity("A");
        var changed = false;
        _history.Changed += (_, e) => changed = e.Action == EditAction.Execute;

        // Locked outside the history (as a file changed on disk is): recorded, not applied again.
        var applied = 0;
        entity.IsLocked = true;
        _history.RecordDone(new LockEdit(entity, () => applied++));
        Assert.True(changed);
        Assert.Equal(0, applied);
        Assert.Equal("Lock A", _history.UndoDescription);

        _history.Undo();
        Assert.False(entity.IsLocked);
        _history.Redo();
        Assert.True(entity.IsLocked);
    }

    [Fact]
    public void Limit_LetsTheOldestStepsGo()
    {
        var history = new EditHistory { Limit = 2 };
        var entity = new Entity("A");
        foreach (var name in new[] { "B", "C", "D" })
            history.Execute(new RenameEntityEdit(entity, name));

        history.Undo();
        history.Undo();
        Assert.False(history.CanUndo);
        Assert.Equal("B", entity.Name);
    }

    private sealed class LockEdit(Entity entity, Action applied) : IEdit
    {
        public string Description => $"Lock {entity.Name}";

        public void Apply()
        {
            entity.IsLocked = true;
            applied();
        }

        public void Revert() => entity.IsLocked = false;
    }
}
