using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// The shortcuts a big scene's frame takes (sorting by keys, sprites drawn on their own, scripts called only for what
/// they have, only flashing sprites moved on) give the same game and the same picture as going through everything.
/// </summary>
public sealed class FastPathTests
{
    private sealed class Counter : Behavior
    {
        public int Updates { get; private set; }

        protected override void Update() => Updates++;
    }

    private class LateOnly : Behavior
    {
        public int Lates { get; private set; }

        protected override void LateUpdate() => Lates++;
    }

    // Overrides nothing itself: what it has comes from LateOnly.
    private sealed class LateOnlyChild : LateOnly;

    private sealed class FixedOnly : Behavior
    {
        public int Fixeds { get; private set; }

        protected override void FixedUpdate() => Fixeds++;
    }

    private sealed class Ring : Component, IDrawable
    {
        public int? DrawOrder => null;

        public int DrawStep => DrawSteps.Shapes;

        public void Draw(Drawing drawing) => drawing.Quad(Vector2.Zero, Vector2.One, Color.Red);
    }

    [Fact]
    public void Scripts_GetTheStepsTheirTypeHas_IncludingFromABaseScript()
    {
        var scene = new Scene();
        var entity = new Entity("Scripts");
        var counter = entity.AddComponent<Counter>();
        var late = entity.AddComponent<LateOnlyChild>();
        var fixedOnly = entity.AddComponent<FixedOnly>();
        scene.Add(entity);
        var game = new Game(scene, new InputState());

        for (var i = 0; i < 3; i++)
            game.Tick(1 / 60.0);

        Assert.Equal(3, counter.Updates);
        Assert.Equal(3, late.Lates);
        Assert.Equal(3, fixedOnly.Fixeds);
    }

    [Fact]
    public void AScriptAddedToAnEntityAlreadyRunning_StartsAndUpdatesFromTheNextStep()
    {
        var scene = new Scene();
        var entity = new Entity("Host");
        entity.AddComponent<Counter>();
        scene.Add(entity);
        var game = new Game(scene, new InputState());
        game.Tick(1 / 60.0);
        game.Tick(1 / 60.0);

        var added = entity.AddComponent<Counter>();
        game.Tick(1 / 60.0);
        game.Tick(1 / 60.0);

        Assert.Equal(2, added.Updates);
    }

    [Fact]
    public void ASpriteFlashedBeforeItsAdded_FadesOnceItsInTheGame()
    {
        var game = new Game(new Scene(), new InputState());
        var entity = new Entity("Hit");
        var sprite = entity.AddComponent<Sprite>();
        sprite.Flash(Color.Red, 0.1f);
        game.Spawn(entity);

        for (var i = 0; i < 12; i++)
            game.Tick(1 / 60.0);

        Assert.Equal(Color.Transparent, sprite.FlashOverlay);
    }

    [Fact]
    public void ASpriteOnADisabledEntity_HoldsItsFlashUntilItsEnabledAgain()
    {
        var scene = new Scene();
        var entity = new Entity("Hit");
        var sprite = entity.AddComponent<Sprite>();
        scene.Add(entity);
        var game = new Game(scene, new InputState());
        sprite.Flash(Color.White, 0.2f);
        game.Tick(1 / 60.0);
        var flash = sprite.FlashOverlay;

        entity.IsEnabled = false;
        for (var i = 0; i < 30; i++)
            game.Tick(1 / 60.0);
        Assert.Equal(flash, sprite.FlashOverlay);

        entity.IsEnabled = true;
        for (var i = 0; i < 30; i++)
            game.Tick(1 / 60.0);
        Assert.Equal(Color.Transparent, sprite.FlashOverlay);
    }

    [Fact]
    public void AnEntityThatGainsADrawable_IsNoLongerDrawnAsJustItsSprite()
    {
        var scene = new Scene();
        var entity = new Entity("Thing");
        entity.AddComponent<Sprite>();
        scene.Add(entity);
        Assert.Single(SceneRenderer.Build(scene, 1).Sprites);

        var ring = entity.AddComponent(new Ring());
        Assert.Equal(2, SceneRenderer.Build(scene, 1).Sprites.Count);

        entity.RemoveComponent(ring);
        Assert.Single(SceneRenderer.Build(scene, 1).Sprites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawOrder_IsOrderThenZThenHierarchy_ForManyEntitiesInAnyOrder(bool sortByY)
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().SortByY = sortByY;
        scene.Add(camera);
        var random = new Random(7);
        float[] zs = [0, 0, -0f, 1, -1, 0.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity];
        for (var i = 0; i < 2000; i++)
        {
            var entity = new Entity($"E{i}");
            entity.Transform.Position = new Vector3(0, random.Next(-3, 4) * 0.5f, zs[random.Next(zs.Length)]);
            entity.AddComponent<Sprite>().Order = random.Next(-3, 4) * (random.Next(2) == 0 ? 1 : 100_000);
            if (random.Next(10) == 0)
                entity.AddComponent(new Ring());
            var parent = random.Next(5) == 0 && scene.Roots.Count > 1 ? scene.Roots[random.Next(1, scene.Roots.Count)] : null;
            scene.Add(entity, parent);
        }

        // What comparing them one by one gives, keeping hierarchy order among equals.
        var expected = scene.Entities.Where(e => e.GetComponent<Sprite>() is not null)
            .Select((e, index) => (Entity: e, Index: index, Order: e.GetComponent<Sprite>()!.Order, Position: e.Transform.WorldPosition))
            .OrderBy(e => e.Order).ThenBy(e => e.Position.Z).ThenByDescending(e => sortByY ? e.Position.Y : 0).ThenBy(e => e.Index)
            .Select(e => e.Entity);

        Assert.Equal(expected, SceneRenderer.DrawOrder(scene));
    }
}
