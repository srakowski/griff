using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Rendering;

/// <summary>
/// A <see cref="SortGroup"/>: a thing made of several sprites (a pallet with a stack of decks on it) sorts as one at its
/// root's feet, its parts in their own Order and hierarchy order, among characters and a tilemap's rows sorting by Y; in
/// the game's frame (built in chunks or not) as in <see cref="Views.DrawOrder"/>.
/// </summary>
public sealed class SortGroupTests
{
    // A ¾ scene sorting by Y: a pallet (its feet at y -3, between the map's rows 2 and 3) with four decks stacked upward
    // on it, each 0.4 above the one below, and a wall layer of six rows sorting by Y.
    private static (Scene Scene, Entity Pallet, Entity[] Decks, Tilemap Walls) PalletScene()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().SortByY = true;
        camera.Transform.Position = new Vector3(0.5f, -3, 0);
        scene.Add(camera);

        var walls = new Tilemap { Tiles = ["1", "1", "1", "1", "1", "1"], SortByY = true };
        var level = new Entity("Walls");
        level.AddComponent(walls);
        scene.Add(level);

        var pallet = new Entity("Pallet");
        pallet.Transform.Position = new Vector3(0.5f, -2.5f, 0);
        pallet.AddComponent<Sprite>().Size = Vector2.One;
        pallet.AddComponent<SortGroup>();
        scene.Add(pallet);
        var decks = new Entity[4];
        for (var i = 0; i < decks.Length; i++)
        {
            decks[i] = new Entity($"Deck {i + 1}");
            decks[i].Transform.Position = new Vector3(0, 0.4f * (i + 1), 0);
            decks[i].AddComponent<Sprite>().Size = new Vector2(0.8f, 0.4f);
            scene.Add(decks[i], pallet);
        }

        return (scene, pallet, decks, walls);
    }

    private static Entity AddCharacter(Scene scene, float y)
    {
        var character = new Entity("Worker");
        character.Transform.Position = new Vector3(0.6f, y, 0);
        character.AddComponent<Sprite>().Size = new Vector2(0.5f);
        scene.Add(character);
        return character;
    }

    // The frame's draws back to front, one name per entity drawn in a row (a wall row by its number).
    private static List<string> Frame(Scene scene, Tilemap walls, bool chunked)
    {
        var frame = chunked ? SceneRenderer.Build(scene, 1, 0, 4) : SceneRenderer.Build(scene, 1);
        var names = new List<string>();
        foreach (var draw in frame.Sprites)
        {
            var name = draw.Entity == walls.Entity ? $"Row {walls.CellAt(new Vector2(draw.World.M41, draw.World.M42)).Y}" : draw.Entity.Name;
            if (names.Count == 0 || names[^1] != name)
                names.Add(name);
        }

        return names;
    }

    private static readonly string[] s_stack = ["Pallet", "Deck 1", "Deck 2", "Deck 3", "Deck 4"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStackOnAPallet_DrawsBottomToTop_AsOneAmongTheWallRows(bool chunked)
    {
        var (scene, _, _, walls) = PalletScene();

        Assert.Equal(["Row 0", "Row 1", "Row 2", .. s_stack, "Row 3", "Row 4", "Row 5"], Frame(scene, walls, chunked));
        Assert.Equal(s_stack, Views.DrawOrder(scene).Where(e => e != walls.Entity).Select(e => e.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACharacterWalkingUpPastIt_IsInFrontBelowItsFeet_AndBehindAboveThem(bool chunked)
    {
        var (scene, pallet, _, walls) = PalletScene();
        var worker = AddCharacter(scene, -5);
        // The pallet's feet are at -3, the worker's 0.25 under where it stands; the top deck's feet are at -1.6.
        foreach (var y in new[] { -5f, -4f, -3.1f, -2.9f, -2.2f, -1f, 0f })
        {
            worker.Transform.Position = worker.Transform.Position with { Y = y };
            var frame = Frame(scene, walls, chunked);
            var at = frame.IndexOf("Worker");
            var stack = frame.IndexOf("Pallet");
            Assert.Equal(s_stack, frame.Skip(stack).Take(5));
            Assert.True(y - 0.25f < -3 ? at > stack : at < stack, $"At {y}: {string.Join(", ", frame)}");

            var order = Views.DrawOrder(scene).ToList();
            Assert.Equal(y - 0.25f < -3, order.IndexOf(worker) > order.IndexOf(pallet));
        }

        // Among the wall rows too (row 2 sorts at -2.5, row 3 at -3.5): at -2.9 (feet -3.15) it's in front of the stack
        // and behind row 3.
        worker.Transform.Position = worker.Transform.Position with { Y = -2.9f };
        Assert.Equal(["Row 0", "Row 1", "Row 2", .. s_stack, "Worker", "Row 3", "Row 4", "Row 5"], Frame(scene, walls, chunked));
        // At -2.6 (feet -2.85), behind the stack and in front of row 2.
        worker.Transform.Position = worker.Transform.Position with { Y = -2.6f };
        Assert.Equal(["Row 0", "Row 1", "Row 2", "Worker", .. s_stack, "Row 3", "Row 4", "Row 5"], Frame(scene, walls, chunked));
    }

    [Fact]
    public void WithoutASortGroup_EachDeckSortsAtItsOwnFeet()
    {
        var (scene, pallet, _, walls) = PalletScene();
        pallet.RemoveComponent(pallet.GetComponent<SortGroup>()!);

        // Higher up, the top decks sort behind the pallet and among the rows above it.
        var frame = Frame(scene, walls, chunked: false);
        Assert.True(frame.IndexOf("Deck 4") < frame.IndexOf("Pallet"));
        Assert.True(frame.IndexOf("Deck 4") < frame.IndexOf("Row 2"));
    }

    [Fact]
    public void InsideAGroup_PartsDrawByTheirOwnOrder_AndTheGroupSortsAtItsRoots()
    {
        var (scene, pallet, decks, walls) = PalletScene();
        decks[0].GetComponent<Sprite>()!.Order = 5;
        var worker = AddCharacter(scene, -2.2f);

        // Deck 1 over the other decks, but the group stays at the pallet's Order (0), behind the worker.
        Assert.Equal(["Worker", "Pallet", "Deck 2", "Deck 3", "Deck 4", "Deck 1"],
            Frame(scene, walls, chunked: false).Where(n => !n.StartsWith("Row ", StringComparison.Ordinal)));
        Assert.Equal([worker, pallet, decks[1], decks[2], decks[3], decks[0]], Views.DrawOrder(scene).SkipWhile(e => e != worker));
    }

    [Fact]
    public void AGroupWhoseRootDrawsNothing_SortsAtTheRootsPosition_RaisedByGroundOffset()
    {
        var (scene, pallet, decks, walls) = PalletScene();
        pallet.RemoveComponent(pallet.GetComponent<Sprite>()!);
        var group = pallet.GetComponent<SortGroup>()!;
        var worker = AddCharacter(scene, -2.4f);

        // The root stands at -2.5; the worker's feet at -2.65 are below it.
        Assert.Equal([.. decks, worker], Views.DrawOrder(scene).Where(e => e != walls.Entity));
        Assert.Equal(["Deck 1", "Deck 2", "Deck 3", "Deck 4", "Worker"], Frame(scene, walls, chunked: true).Where(n => !n.StartsWith("Row ", StringComparison.Ordinal)));

        // Lowered to -3.5, the group's ground is below the worker's feet.
        group.GroundOffset = -1;
        Assert.Equal(["Worker", "Deck 1", "Deck 2", "Deck 3", "Deck 4"], Frame(scene, walls, chunked: true).Where(n => !n.StartsWith("Row ", StringComparison.Ordinal)));
    }

    [Fact]
    public void ManyGroupsInABigScene_DrawTheSameBuiltInChunks()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().SortByY = true;
        scene.Add(camera);
        var random = new Random(3);
        for (var i = 0; i < 3000; i++)
        {
            var entity = new Entity($"E{i}");
            entity.Transform.Position = new Vector3(random.Next(-4, 5), random.Next(-4, 5) * 0.5f, 0);
            entity.AddComponent<Sprite>().Order = random.Next(8) == 0 ? 1 : 0;
            scene.Add(entity);
            if (random.Next(20) == 0)
            {
                entity.AddComponent<SortGroup>();
                for (var p = 0; p < 3; p++)
                {
                    var part = new Entity($"E{i}.{p}");
                    part.Transform.Position = new Vector3(0, p * 0.4f, 0);
                    part.AddComponent<Sprite>().Order = random.Next(-1, 2);
                    scene.Add(part, entity);
                }
            }
        }

        var one = SceneRenderer.Build(scene, 1).Sprites.Select(d => d.Entity).ToList();
        Assert.Equal(one, SceneRenderer.Build(scene, 1, 0, 8).Sprites.Select(d => d.Entity));
        Assert.Equal(one, SceneRenderer.Build(scene, 1, 0, 1).Sprites.Select(d => d.Entity));
        Assert.Equal(Views.DrawOrder(scene).Where(one.Contains), one);
    }
}
