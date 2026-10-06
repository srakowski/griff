using System.Numerics;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Editing;

public class CopyValuesTests
{
    [Fact]
    public void KeepsPlayChanges_AsOneUndoableStep()
    {
        var registry = ComponentRegistry.CreateDefault();
        var serializer = new SceneSerializer(registry);
        var edit = new Scene();
        var player = new Entity("Player");
        player.AddComponent<Body>().GravityScale = 1;
        edit.Add(player);

        // Play mode runs on a copy with the same ids; tweak it.
        var play = serializer.LoadFromString(serializer.SaveToString(edit));
        var live = play.Find(player.Id)!;
        live.Transform.Position = new Vector3(3, 4, 0);
        live.GetComponent<Body>()!.GravityScale = 0.5f;

        var history = new EditHistory();
        using (history.BeginGroup("Keep play changes"))
        {
            foreach (var change in CopyValues.Between(live, player, registry))
                history.Execute(change);
        }

        Assert.Equal(new Vector3(3, 4, 0), player.Transform.Position);
        Assert.Equal(0.5f, player.GetComponent<Body>()!.GravityScale);

        history.Undo();
        Assert.Equal(Vector3.Zero, player.Transform.Position);
        Assert.Equal(1f, player.GetComponent<Body>()!.GravityScale);
    }

    [Fact]
    public void NoDifferences_NoEdits()
    {
        var registry = ComponentRegistry.CreateDefault();
        var a = new Entity("Same");
        var b = new Entity("Same");

        Assert.Empty(CopyValues.Between(a, b, registry));
    }
}
