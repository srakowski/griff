using System.Numerics;
using Griff.Agent;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>
/// What fields with editors of their own say about their values: how they're read and written, what's wrong with one,
/// what component_describe tells agents, and that a script's field marked for one gets it (or is told it can't).
/// </summary>
public sealed class FieldEditorAttributeTests
{
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    /// <summary>A script's fields marked for editors, as Griff's own are.</summary>
    public sealed class Trap : Component
    {
        [Dice]
        public string? Hurt { get; set; } = "2d6";

        [Points(Least = 3)]
        public string? Outline { get; set; } = "0 0; 1 0; 0 1";

        [Direction]
        public Vector2 Facing { get; set; } = Vector2.UnitY;

        [Offset]
        public Vector2 Mouth { get; set; }
    }

    /// <summary>A dice editor on a number: it can't have one.</summary>
    public sealed class Wrong : Component
    {
        [Dice]
        public int Hurt { get; set; }
    }

    private PropertyDescriptor Property<T>(string name) => _registry.Get(typeof(T)).Properties.Single(p => p.Name == name);

    [Fact]
    public void Dice_AreFlaggedWhenTheyDontRead()
    {
        var dice = Assert.IsType<DiceAttribute>(Property<BumpAttack>("DamageRoll").Editor);
        Assert.Null(dice.Problem(new BumpAttack(), "2d6+3"));
        Assert.Null(dice.Problem(new BumpAttack(), ""));
        Assert.Equal("\"2d\" isn't dice: write them like 2d6+3, d20 or 5", dice.Problem(new BumpAttack(), "2d"));
    }

    [Fact]
    public void Points_ReadWriteAndFlagWhatIsntAPoint()
    {
        var points = Assert.IsType<PointsAttribute>(Property<Mover>("Points").Editor);
        Assert.True(points.FromStart);
        Assert.True(Assert.IsType<PointsAttribute>(Property<GridPatrol>("Points").Editor).Cells);
        Assert.Equal(3, Assert.IsType<PointsAttribute>(Property<PolygonCollider>("Points").Editor).Least);
        Assert.Equal("4 0; 0.5 -1.25", PointsAttribute.Write([new(4, 0), new(0.5f, -1.25f)]));
        Assert.Equal([new Vector2(4, 0), new Vector2(0.5f, -1.25f)], PointsAttribute.Read("4 0; 0.5 -1.25"));
        Assert.Equal("\"4\" isn't a point: write each as two numbers, x then y, like 4 0", points.Problem(new Mover(), "4 0; 4"));
    }

    [Fact]
    public void TileNumbers_AreWrittenWithRanges()
    {
        Assert.IsType<TileNumbersAttribute>(Property<Tilemap>("BackgroundTiles").Editor);
        Assert.IsType<TileNumbersAttribute>(Property<Griff.Modules.Dungeons.DungeonGenerator>("WallTile").Editor);
        Assert.Equal("1, 2, 5-8, 20", TileNumbersAttribute.Write([20, 5, 6, 7, 8, 1, 2, 6]));
        Assert.Equal([1, 2, 5, 6, 7, 8, 20], TileNumbersAttribute.Read("1, 2, 5-8, 20"));
        Assert.Equal("\"x\" isn't a tile number or range: write them like 10-15, 20", new TileNumbersAttribute().Problem(new Tilemap(), "3, x"));
        Assert.Equal("16-19 @6, 30-31 @2.5", TileAnimationsAttribute.Write([new(16, 19, 6), new(30, 31, 2.5f)]));
        Assert.NotNull(new TileAnimationsAttribute().Problem(new Tilemap(), "16 @6"));
        Assert.Equal("16-31, 48-63", AutoTilesAttribute.Write([16, 48]));
        Assert.NotNull(new AutoTilesAttribute().Problem(new Tilemap(), "16-20"));
    }

    [Fact]
    public void PrefabCounts_ReadAndWriteAPrefabAndHowMany()
    {
        Assert.Equal(("Enemy", 4, 4), PrefabCountsAttribute.Read("Enemy 4"));
        Assert.Equal(("Health Potion", 1, 2), PrefabCountsAttribute.Read("Health Potion 2-1"));
        Assert.Equal(("Gold", 1, 1), PrefabCountsAttribute.Read("Gold"));
        Assert.Equal("Enemy 4", PrefabCountsAttribute.Write("Enemy", 4, 4));
        Assert.Equal("Potion 1-2", PrefabCountsAttribute.Write("Potion", 1, 2));
        Assert.Equal("Gold", PrefabCountsAttribute.Write("Gold", 1, 1));
    }

    [Fact]
    public void AClip_IsPickedFromTheAnimationsClips()
    {
        var choice = Assert.IsType<ChoiceOfAttribute>(Property<Animation>("Clip").Editor);
        var animation = new Animation { Clips = "Idle 0-1, Run 2-5" };
        Assert.Equal(["Idle", "Run"], choice.Choices(animation));
        Assert.Null(choice.Problem(animation, "run"));
        Assert.Equal("There's no \"Fly\" to pick: the choices are Idle, Run", choice.Problem(animation, "Fly"));
        Assert.Equal("There's no \"Fly\" to pick: there's nothing to pick yet", choice.Problem(new Animation(), "Fly"));
        Assert.StartsWith("\"5\" isn't a clip", new ClipsAttribute().Problem(animation, "Idle 0-1, 5"));
    }

    [Fact]
    public void Directions_NameTheEightWays()
    {
        Assert.IsType<DirectionAttribute>(Property<Shooter>("Direction").Editor);
        Assert.IsType<DirectionAttribute>(Property<Bouncer>("Direction").Editor);
        Assert.Equal("Up Right", DirectionAttribute.NameOf(new Vector2(2, 2)));
        Assert.Equal("Down", DirectionAttribute.NameOf(new Vector2(0, -3)));
        Assert.Null(DirectionAttribute.NameOf(new Vector2(1, 2)));
        Assert.Null(DirectionAttribute.NameOf(Vector2.Zero));
        Assert.True(Property<Shooter>("Offset").Offset!.World);
        Assert.False(Property<Light>("Offset").Offset!.World);
    }

    [Fact]
    public void TextShowingAnOutput_PreviewsHowItReads()
    {
        var format = Assert.IsType<OutputFormatAttribute>(Property<Text>("Value").Editor);
        var entity = new Entity("Hud");
        entity.AddComponent(new Health { Max = 8 });
        var text = entity.AddComponent<Text>();
        var scene = new Scene();
        scene.Add(entity);
        Assert.Null(format.Preview(text, "HP {0}"));
        text.Output = "Health";
        Assert.Equal("HP 8 / 8", format.Preview(text, "HP {0} / {1}"));
        Assert.Equal("8.0", format.Preview(text, "{0:0.0}"));
    }

    [Fact]
    public void AScriptsFields_GetTheSameEditors_AndOnesThatCantHaveThemAreRefused()
    {
        _registry.Register<Trap>();
        var trap = _registry.Get(typeof(Trap));
        Assert.IsType<DiceAttribute>(trap.Properties.Single(p => p.Name == "Hurt").Editor);
        Assert.IsType<DirectionAttribute>(trap.Properties.Single(p => p.Name == "Facing").Editor);
        Assert.NotNull(trap.Properties.Single(p => p.Name == "Mouth").Offset);

        var error = Assert.Throws<NotSupportedException>(() => _registry.Register<Wrong>());
        Assert.Equal("Wrong.Hurt is marked [Dice], which goes on a string (text), but it's Int32.", error.Message);
    }

    [Fact]
    public void ComponentDescribe_SaysWhatEachEditorTakes()
    {
        var mover = ComponentDescription.Of(_registry, _registry.Get(typeof(Mover)));
        Assert.Contains("(Points: \"x y\" pairs separated by semicolons, e.g. \"4 0; 4 3\"; the start (0 0) is always the first point and isn't listed; at least 1.", mover);
        var battler = ComponentDescription.Of(_registry, _registry.Get(typeof(Griff.Modules.Battles.Battler)));
        Assert.Contains("(Dice: how many, \"d\", their sides", battler);
        var shooter = ComponentDescription.Of(_registry, _registry.Get(typeof(Shooter)));
        Assert.Contains("(A direction, x right and y up", shooter);
        Assert.Contains("(A point offset from the entity, in world units", shooter);
        var door = ComponentDescription.Of(_registry, _registry.Get(typeof(Door)));
        Assert.Contains("(Names a flag: \"Name\" holds while it's set, \"not Name\" while it isn't.)", door);
        Assert.Contains("(Names an item.)", door);
    }
}
