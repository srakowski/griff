using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;

namespace Griff.Editor.Tests;

// In the Scene view a layout's children are in their places as it's edited, not only once the game runs.
public class LayoutEditingTests
{
    [Fact]
    public void ChildrenAddedHiddenOrUndone_TakeTheirPlacesAsTheSceneIsEdited()
    {
        var scene = new Scene();
        var board = new Entity("Board");
        board.AddComponent<Layout>().Spacing = 0.5f;
        scene.Add(board);
        var document = new SceneDocument(scene, "Test.scene");
        var rows = Enumerable.Range(0, 2).Select(i =>
        {
            var row = new Entity($"Row {i}");
            row.AddComponent<Sprite>().Size = new Vector2(2, 1);
            return row;
        }).ToList();

        foreach (var row in rows)
            document.Execute(new AddEntityEdit(scene, row, board));
        Assert.Equal(new Vector3(-2, 1.5f, 0), rows[0].Transform.Position);
        Assert.Equal(new Vector3(-2, 0, 0), rows[1].Transform.Position);

        document.Execute(new SetEntityEnabledEdit(rows[0], false));
        Assert.Equal(new Vector3(-2, 1.5f, 0), rows[1].Transform.Position);
        document.History.Undo();
        Assert.Equal(new Vector3(-2, 0, 0), rows[1].Transform.Position);
    }
}
