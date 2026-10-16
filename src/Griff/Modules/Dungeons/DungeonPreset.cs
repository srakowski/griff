using System.Numerics;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Modules.Dungeons;

/// <summary>
/// Creates a dungeon of rooms and corridors cut from solid wall, made afresh each game. It's a <see cref="Tilemap"/> with a
/// <see cref="DungeonGenerator"/> 48 cells across and 32 down, centered on the scene's middle, showing one made from a new
/// seed (Fill Walls shows its walls without a tileset). The game makes its own each time it starts (its Seed is 0); set a Seed to keep one, and
/// use Generate Dungeon to see it. It puts the entity called "Player" in the first room.
/// </summary>
[Category("Dungeons")]
public sealed class DungeonPreset : EntityPreset
{
    public override string Id => "Entity.CreateDungeon";

    public override string Text => "Create D_ungeon";

    public override int Order => 21;

    public override string? Icon => "Key";

    public override string EntityName(Scene scene) => "Dungeon";

    public override void Configure(Entity entity, Scene scene)
    {
        var map = entity.AddComponent<Tilemap>();
        map.Tint = Color.FromBytes(0x48, 0x4B, 0x55);
        var dungeon = entity.AddComponent<DungeonGenerator>();
        dungeon.FillWalls = true;
        entity.Transform.Position = new Vector3(-dungeon.Width / 2f, dungeon.Height / 2f, 0);
        map.Tiles = DungeonGen.Generate(dungeon, Random.Shared.Next(1, int.MaxValue)).Tiles;
    }
}

/// <summary>
/// Makes the selected dungeon now, to see what it looks like: its tilemap filled with rooms and corridors, its Players
/// put in the first room and its Exit in the furthest, as one undo step. With a Seed, it's the first floor the game will
/// make; with Seed 0, a new one each time (the game still makes its own). Spawns only appear in the game.
/// </summary>
public sealed class GenerateDungeonAction : EntityAction
{
    public override string Id => "Entity.GenerateDungeon";

    public override string Text => "_Generate Dungeon";

    public override int Order => 9;

    public override string? Icon => "Shuffle";

    public override string NotApplicable => "Select a dungeon (an entity with a Dungeon Generator) to generate it.";

    public override bool AppliesTo(Entity entity) => entity.GetComponent<DungeonGenerator>() is not null && entity.GetComponent<Tilemap>() is not null;

    public override IEnumerable<IEdit> Edits(Entity entity, Scene scene, ComponentRegistry components)
    {
        var dungeon = entity.GetComponent<DungeonGenerator>()!;
        var map = entity.GetComponent<Tilemap>()!;
        var layout = DungeonGen.Generate(dungeon, dungeon.Seed != 0 ? dungeon.Seed : Random.Shared.Next(1, int.MaxValue));
        var moves = new List<(Entity Entity, (int X, int Y) Cell)>();
        var players = (dungeon.Players ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(name => EntityNames.Every(scene, name)).Distinct().ToList();
        for (var i = 0; i < players.Count; i++)
            moves.Add((players[i], layout.Players[Math.Min(i, layout.Players.Count - 1)]));
        if (!string.IsNullOrWhiteSpace(dungeon.Exit) && scene.FindNamed(dungeon.Exit.Trim()) is { } exit)
            moves.Add((exit, layout.Exit));

        yield return new SetPropertyEdit(map, components.Get(typeof(Tilemap)).Properties.Single(p => p.Name == nameof(Tilemap.Tiles)), layout.Tiles);
        if (moves.Count == 0)
            yield break;
        var world = entity.Transform.WorldMatrix;
        yield return new SetPositionsEdit(moves.Select(m => m.Entity.Transform).ToList(), moves.Select(m =>
        {
            var at = Vector2.Transform(map.CellCenter(m.Cell.X, m.Cell.Y), world);
            var local = new Vector3(at, m.Entity.Transform.WorldPosition.Z);
            if (m.Entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var toParent))
                local = Vector3.Transform(local, toParent);
            return local with { Z = m.Entity.Transform.Position.Z };
        }).ToList());
    }
}
