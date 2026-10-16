using System.Numerics;
using Griff.Networking;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>A game's own component that draws its entity's tilemap itself.</summary>
public sealed class OwnTiles : Component, IDrawsTilemap;

/// <summary>A game's own component that starts the dialogue beside it.</summary>
public sealed class Lever : Component, IDialogueStarter
{
    public bool Pulled { get; set; }

    bool IDialogueStarter.StartsDialogue => Pulled;
}

/// <summary>A game's own component that keeps its entity from the players it says.</summary>
public sealed class Hush : Component, INetSecret
{
    public bool Hidden { get; set; } = true;

    bool INetSecret.HiddenFrom(int player, string team) => Hidden;
}

/// <summary>The extension points that serve any component, used by components of a game's own as Griff's genres use them.</summary>
public sealed class GeneralExtensionPointTests
{
    [Fact]
    public void ATilemapDrawsNothingWhereAnotherComponentDrawsItsTiles()
    {
        int Draws(bool own)
        {
            var scene = new Scene();
            var level = new Entity("Level");
            level.AddComponent<Tilemap>().Tiles = ["000", "000"];
            if (own)
                level.AddComponent<OwnTiles>();
            scene.Add(level);
            return SceneRenderer.Build(scene, 1).Sprites.Count;
        }

        Assert.True(Draws(own: false) > 0);
        Assert.Equal(0, Draws(own: true));
    }

    [Fact]
    public void ADialogueNeedsNoColliderWhenAnyComponentStartsIt()
    {
        var entity = new Entity("Door");
        var dialogue = entity.AddComponent<Dialogue>();
        var lever = entity.AddComponent<Lever>();
        Assert.False(dialogue.NeedsNoCollider);
        lever.Pulled = true;
        Assert.True(dialogue.NeedsNoCollider);
    }

    [Fact]
    public void AnyComponentKeepsItsEntityFromPlayers()
    {
        static Scene Level()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            foreach (var name in new[] { "Open", "Secret" })
            {
                var entity = new Entity(name);
                entity.AddComponent<Sprite>();
                if (name == "Secret")
                    entity.AddComponent<Hush>();
                scene.Add(entity);
            }

            return scene;
        }

        var world = new NetWorld(Level, setUp: game =>
        {
            var registry = ComponentRegistry.CreateDefault();
            registry.Register<Hush>();
            game.Components = registry;
        });
        world.Run(0.5);
        Assert.Contains(world.Host.Scene.Entities, e => e.Name == "Secret");
        Assert.Contains(world.Client.Scene.Entities, e => e.Name == "Open");
        Assert.DoesNotContain(world.Client.Scene.Entities, e => e.Name == "Secret");

        world.Host.Scene.Entities.Single(e => e.Name == "Secret").GetComponent<Hush>()!.Hidden = false;
        world.Run(0.5);
        Assert.Contains(world.Client.Scene.Entities, e => e.Name == "Secret");
    }

    [Theory]
    [InlineData("""{ "type": "Griff.Counter", "countGoals": true, "format": "{0} / {2}" }""", "Goals Covered")]
    [InlineData("""{ "type": "Griff.Counter", "countMoves": true }""", "Moves")]
    [InlineData("""{ "type": "Griff.Counter", "countScore": true }""", "Score")]
    [InlineData("""{ "type": "Griff.Counter", "countFloor": true }""", "Floor")]
    [InlineData("""{ "type": "Griff.Counter", "countGem": 3 }""", "Cleared:3")]
    [InlineData("""{ "type": "Griff.Counter", "countKind": 2 }""", "Cleared:2")]
    public void ACounterSavedWithAGenreSettingCountsTheOutputInstead(string saved, string output)
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = serializer.LoadFromString($$"""
            { "formatVersion": 1, "entities": [ { "id": "{{Guid.NewGuid()}}", "name": "Hud", "components": [ { "type": "Transform" }, {{saved}} ] } ] }
            """, []);

        var counter = scene.Entities.Single().GetComponent<Counter>()!;
        Assert.Equal(output, counter.Output);
        Assert.Equal("*", counter.Track);
        var again = serializer.SaveToString(scene);
        foreach (var gone in new[] { "countGoals", "countMoves", "countScore", "countFloor", "countKind", "countGem" })
            Assert.DoesNotContain(gone, again);
    }

    [Fact]
    public void ACountersTrackOfAnyEntityListsTheOutputsOfWhatTheSceneHas()
    {
        var scene = new Scene();
        var board = new Entity("Board");
        board.AddComponent<Tilemap>();
        board.AddComponent<Griff.Modules.Match3.MatchBoard>();
        scene.Add(board);
        var hud = new Entity("Hud");
        var counter = hud.AddComponent<Counter>();
        (counter.Track, counter.Output) = ("*", "Score");
        scene.Add(hud);
        var registry = ComponentRegistry.CreateDefault();
        var output = registry.Get(typeof(Counter)).Properties.Single(p => p.Name == nameof(Counter.Output));
        var track = registry.Get(typeof(Counter)).Properties.Single(p => p.Name == nameof(Counter.Track));

        Assert.Contains(Griff.Editing.OutputNames.Choices(counter, output), c => c.Name == "Score");
        Assert.Empty(Griff.Editing.OutputNames.Unmatched(counter, output));
        Assert.Empty(Griff.Editing.EntityReferences.Names("*", track.EntityName!));
    }
}
