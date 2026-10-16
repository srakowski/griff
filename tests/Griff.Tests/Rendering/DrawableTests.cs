using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Rendering;

/// <summary>A component of a game's own (as a script's would be) that draws a square of its color.</summary>
public sealed class Badge : Component, IDrawable
{
    public Color Color { get; set; } = Color.White;

    public int Order { get; set; }

    public int Step { get; set; } = DrawSteps.Shapes;

    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => Step;

    public void Draw(Drawing drawing) => drawing.Rect(Vector2.Zero, new Vector2(0.5f), Color);
}

/// <summary>A component of a game's own that only changes how its entity is drawn.</summary>
public sealed class Glow : Component, IDrawEffect
{
    public Color Color { get; set; } = Color.White;

    public Vector2 Nudge { get; set; }

    public bool OnTop { get; set; }

    bool IDrawEffect.DrawsOnTop => OnTop;

    Matrix4x4? IDrawEffect.DrawTransform => Nudge == Vector2.Zero ? null : Matrix4x4.CreateTranslation(Nudge.X, Nudge.Y, 0);

    Color IDrawEffect.Tint(IDrawable drawn, Color color) => drawn is Sprite ? Color : color;
}

/// <summary>A component of a game's own that lights the dark around it.</summary>
public sealed class Ember : Component, ILightSource
{
    public void AddLights(List<LightSpot> lights) =>
        lights.Add(new LightSpot(new Vector2(Entity!.Transform.WorldPosition.X, Entity.Transform.WorldPosition.Y), 3, Color.White, 1));
}

public sealed class DrawableTests
{
    private readonly Scene _scene = new();

    private Entity Add(string name, Action<Entity> setUp, Entity? parent = null)
    {
        var entity = new Entity(name);
        setUp(entity);
        _scene.Add(entity, parent);
        return entity;
    }

    [Fact]
    public void A_games_own_component_draws_among_everything_else_by_its_order()
    {
        var back = Add("Back", e => e.AddComponent<Sprite>().Order = 0);
        var badge = Add("Badge", e => e.AddComponent<Badge>().Order = 5);
        var front = Add("Front", e => e.AddComponent<Sprite>().Order = 10);

        var frame = SceneRenderer.Build(_scene, 1);

        Assert.Equal([back, badge, front], frame.Sprites.Select(d => d.Entity));
        Assert.Equal(5, Views.OrderOf(badge));
        Assert.Equal([back, badge, front], Views.DrawOrder(_scene));
    }

    [Fact]
    public void An_entitys_drawables_draw_in_step_order_and_its_sprite_sets_its_order()
    {
        var entity = Add("Hero", e =>
        {
            e.AddComponent<Text>().Order = 7;
            e.AddComponent<Badge>().Color = Color.Red;
            e.AddComponent<Sprite>().Order = 3;
            e.AddComponent<Badge>().Step = DrawSteps.Particles + 1;
        });

        var draws = SceneRenderer.Build(_scene, 1).Sprites;

        // Sprite, then the badge over it (Shapes), then the text, then the badge that asked to go last.
        Assert.Equal(4, draws.Count);
        Assert.Null(draws[0].Text);
        Assert.Equal(Color.Red, draws[1].Tint);
        Assert.NotNull(draws[2].Text);
        Assert.Equal(Color.White, draws[3].Tint);
        Assert.Equal(3, Views.OrderOf(entity));
    }

    [Fact]
    public void Something_that_only_draws_with_others_needs_one_with_an_order()
    {
        // A trail on its own has nothing to draw with; a badge with no order of its own draws at its sprite's.
        Add("Trail Only", e => e.AddComponent<Trail>());

        Assert.Empty(Views.DrawOrder(_scene));
    }

    [Fact]
    public void Draw_effects_tint_move_and_lift_what_an_entity_draws()
    {
        var lifted = Add("Card", e =>
        {
            e.AddComponent<Sprite>().Order = -10;
            e.AddComponent<Glow>().OnTop = true;
        });
        var plain = Add("Table", e => e.AddComponent<Sprite>().Order = 100);
        var shaken = Add("Crate", e =>
        {
            e.AddComponent<Sprite>().Order = 50;
            var glow = e.AddComponent<Glow>();
            glow.Color = Color.Green;
            glow.Nudge = new Vector2(2, 0);
        });
        var inside = Add("Label", e => e.AddComponent<Badge>().Order = 60, parent: shaken);

        var draws = SceneRenderer.Build(_scene, 1).Sprites;

        Assert.Equal([shaken, inside, plain, lifted], draws.Select(d => d.Entity));
        Assert.Equal(Color.Green, draws[0].Tint);
        Assert.Equal(2, draws[0].World.M41, 3);
        // What's inside moves with it, but keeps its own color: the tint is for the entity's own components.
        Assert.Equal(2, draws[1].World.M41, 3);
        Assert.Equal(Color.White, draws[1].Tint);
    }

    [Fact]
    public void Over_the_world_goes_after_every_entity_and_under_the_screen()
    {
        var hud = Add("HUD", e =>
        {
            e.AddComponent<ScreenAnchor>();
            e.AddComponent<Sprite>().Order = -100;
        });
        var world = Add("World", e => e.AddComponent<Sprite>().Order = 0);
        var rope = Add("Rope", e => e.AddComponent<Badge>().Step = DrawSteps.OverWorld);

        var draws = SceneRenderer.Build(_scene, 1).Sprites;

        Assert.Equal([world, rope, hud], draws.Select(d => d.Entity));
    }

    [Fact]
    public void Without_anything_over_the_world_the_screen_keeps_its_order()
    {
        // A backdrop pinned to the screen, drawn under the world.
        var backdrop = Add("Backdrop", e =>
        {
            e.AddComponent<ScreenAnchor>();
            e.AddComponent<Sprite>().Order = -100;
        });
        var world = Add("World", e => e.AddComponent<Sprite>());

        Assert.Equal([backdrop, world], SceneRenderer.Build(_scene, 1).Sprites.Select(d => d.Entity));
    }

    [Fact]
    public void A_games_own_light_source_lights_the_dark()
    {
        Add("Fire", e =>
        {
            e.Transform.Position = new Vector3(2, 1, 0);
            e.AddComponent<Ember>();
        });

        var light = Assert.Single(Views.LightDraws(_scene));

        Assert.Equal(new Vector2(2, 1), light.Center);
        Assert.Equal(3, light.Radius);
    }

    [Fact]
    public void A_system_draws_over_the_world_in_a_running_game()
    {
        var hud = Add("HUD", e =>
        {
            e.AddComponent<ScreenAnchor>();
            e.AddComponent<Text>();
        });
        var world = Add("World", e => e.AddComponent<Sprite>());
        var game = new Game(_scene, new InputState());
        game.AddSystem(new Markers());

        var draws = SceneRenderer.Build(game, 1).Sprites;

        Assert.Equal(world, draws[0].Entity);
        Assert.Equal(Color.Red, draws[1].Tint);
        Assert.Equal(hud, draws[2].Entity);
    }

    [Fact]
    public void The_editor_draws_a_component_on_its_own()
    {
        var entity = Add("Badge", e => e.AddComponent<Badge>().Color = Color.Blue);

        var draws = SceneRenderer.DrawsOf(entity.GetComponent<Badge>()!, new Box(Vector2.Zero, new Vector2(5)), 0.05f);

        Assert.Equal(Color.Blue, Assert.Single(draws).Tint);
    }

    // Marks every sprite with a red square over the world.
    private sealed class Markers : GameSystem
    {
        protected override void Draw(FrameDrawing frame)
        {
            var marks = frame.Scene.Active<Sprite>().AsEnumerable()
                .Select(s => FrameDrawing.Quad(new Vector2(s.Entity!.Transform.WorldPosition.X, s.Entity.Transform.WorldPosition.Y), new Vector2(0.2f), Color.Red));
            frame.AddOverWorld(marks);
        }
    }
}
