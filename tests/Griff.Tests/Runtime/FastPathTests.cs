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

    // Draws a few quads (or none) as text would, at an order of its own, noting when it's called.
    private sealed class Marks : Component, IDrawable
    {
        public int? Order { get; set; }

        public int Quads { get; set; } = 1;

        public List<Entity>? Calls { get; set; }

        public int? DrawOrder => Order;

        public int DrawStep => DrawSteps.Text;

        public void Draw(Drawing drawing)
        {
            Calls?.Add(drawing.Entity);
            for (var i = 0; i < Quads; i++)
                drawing.Quad(new Vector2(i, 0), Vector2.One, Color.White);
        }
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

        Assert.Equal(expected, Views.DrawOrder(scene));
    }

    // A crowd on one layer with a few things over and under it, here and there in the hierarchy: in order but for a few
    // stretches, which are merged; with many, they're sorted by key. Either way the order is what comparing them gives.
    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    [InlineData(false, 60)]
    [InlineData(true, 60)]
    public void DrawOrder_OfACrowdWithAFewThingsOverAndUnderIt_IsOrderThenZThenHierarchy(bool sortByY, int others)
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().SortByY = sortByY;
        scene.Add(camera);
        var random = new Random(11);
        float[] zs = [0, -0f, 1, -1, float.NaN, float.PositiveInfinity];
        var odd = Enumerable.Range(0, others).Select(_ => random.Next(3000)).ToHashSet();
        for (var i = 0; i < 3000; i++)
        {
            var entity = new Entity($"E{i}");
            var sprite = entity.AddComponent<Sprite>();
            // The crowd: one layer, Z 0 or -0 (the same), at a few heights.
            entity.Transform.Position = new Vector3(0, random.Next(-1, 2) * (odd.Contains(i) ? 1 : 0), i % 2 == 0 ? 0 : -0f);
            if (odd.Contains(i))
            {
                entity.Transform.Position = entity.Transform.Position with { Z = zs[random.Next(zs.Length)] };
                sprite.Order = random.Next(-2, 3) * 10;
            }

            scene.Add(entity, i > 0 && random.Next(50) == 0 ? scene.Roots[^1] : null);
        }

        var expected = scene.Entities.Where(e => e.GetComponent<Sprite>() is not null)
            .Select((e, index) => (Entity: e, Index: index, e.GetComponent<Sprite>()!.Order, Position: e.Transform.WorldPosition))
            .OrderBy(e => e.Order).ThenBy(e => e.Position.Z).ThenByDescending(e => sortByY ? e.Position.Y : 0).ThenBy(e => e.Index)
            .Select(e => e.Entity);

        Assert.Equal(expected, Views.DrawOrder(scene));
    }

    // Each entity is drawn as it's found and its draws then put back to front together: the frame is each entity's draws
    // (its drawables' in step order) in draw order, whether its entities were nearly in order or all over the place.
    [Theory]
    [InlineData(4)]
    [InlineData(400)]
    public void AFrame_IsEachEntitysDrawsTogether_InDrawOrder(int others)
    {
        var scene = new Scene();
        var random = new Random(5);
        var odd = Enumerable.Range(0, others).Select(_ => random.Next(1500)).ToHashSet();
        for (var i = 0; i < 1500; i++)
        {
            var entity = new Entity($"E{i}");
            entity.Transform.Position = new Vector3(random.Next(-5, 6), random.Next(-3, 4), 0);
            var order = odd.Contains(i) ? random.Next(-3, 4) : 0;
            switch (random.Next(4))
            {
                case 0:
                    entity.AddComponent<Sprite>().Order = order;
                    break;
                case 1:
                    // Marks first, drawn after the sprite all the same (by step); the sprite's order is the entity's.
                    entity.AddComponent(new Marks { Order = 99, Quads = random.Next(4) });
                    entity.AddComponent<Sprite>().Order = order;
                    break;
                case 2:
                    // No sprite: its own order, and some draw nothing this frame.
                    entity.AddComponent(new Marks { Order = order, Quads = random.Next(3) });
                    break;
                default:
                    // No order at all: it isn't drawn.
                    entity.AddComponent(new Marks { Order = null, Quads = 2 });
                    break;
            }

            scene.Add(entity, i > 0 && random.Next(20) == 0 ? scene.Roots[^1] : null);
        }

        var frame = SceneRenderer.Build(scene, 16 / 9f);
        var view = new Box(Vector2.Zero, new Vector2(Views.DefaultCameraSize * 16 / 9f, Views.DefaultCameraSize));
        var expected = Views.DrawOrder(scene)
            .SelectMany(e => e.Components.OfType<IDrawable>().OrderBy(d => d.DrawStep))
            .SelectMany(d => SceneRenderer.DrawsOf(d, view, Views.DefaultCameraSize / 180));

        Assert.Equal(expected, frame.Sprites);
    }

    // What an entity draws is asked for in hierarchy order, whatever order it's drawn in: the order a script's drawables
    // are called in doesn't change as things move between layers.
    [Fact]
    public void Drawables_AreCalledInHierarchyOrder_AndDrawnBackToFront()
    {
        var scene = new Scene();
        var calls = new List<Entity>();
        var entities = new List<Entity>();
        for (var i = 0; i < 5; i++)
        {
            var entity = new Entity($"E{i}");
            entity.AddComponent(new Marks { Order = 10 - i, Calls = calls });
            scene.Add(entity, i == 3 ? entities[1] : null);
            entities.Add(entity);
        }

        var frame = SceneRenderer.Build(scene, 1);

        // E3 is inside E1, so it comes before E2 in the hierarchy; the higher order is drawn on top.
        Assert.Equal([entities[0], entities[1], entities[3], entities[2], entities[4]], calls);
        Assert.Equal([entities[4], entities[3], entities[2], entities[1], entities[0]], frame.Sprites.Select(d => d.Entity));
    }
}
