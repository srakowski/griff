using System.Numerics;
using Griff.Scenes;

namespace Griff.Editing.Presets;

/// <summary>Creates an empty entity (just a transform) inside the selected entity, or at the top level, and selects it.</summary>
[Category("Basics")]
public sealed class EmptyPreset : EntityPreset
{
    public override string Id => "Entity.CreateEmpty";
    public override string Text => "Create _Empty";
    public override int Order => 0;
    public override string? Icon => "Box";
    public override string? Shortcut => "Ctrl+Alt+N";
    public override string EntityName(Scene scene) => "Entity";

    public override void Configure(Entity entity, Scene scene)
    {
    }
}

/// <summary>Creates an entity with a Sprite (a white box until it's given an Image) and selects it.</summary>
[Category("Basics")]
public sealed class SpritePreset : EntityPreset
{
    public override string Id => "Entity.CreateSprite";
    public override string Text => "Create _Sprite";
    public override int Order => 1;
    public override string? Icon => "Image";
    public override string EntityName(Scene scene) => "Sprite";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Sprite>();
}

/// <summary>Creates a camera: what the game is seen through.</summary>
[Category("Basics")]
public sealed class CameraPreset : EntityPreset
{
    public override string Id => "Entity.CreateCamera";
    public override string Text => "Create _Camera";
    public override int Order => 3;
    public override string? Icon => "Camera";
    public override string EntityName(Scene scene) => "Camera";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Camera>();
}

/// <summary>Creates an entity with a Text: words in the world, or on the HUD with a ScreenAnchor.</summary>
[Category("Basics")]
public sealed class TextPreset : EntityPreset
{
    public override string Id => "Entity.CreateText";
    public override string Text => "Create _Text";
    public override int Order => 2;
    public override string? Icon => "Font";
    public override string EntityName(Scene scene) => "Text";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Griff.Scenes.Text>();

    public override void Arrange(Entity entity, IReadOnlyList<Entity> children, Entity? inside) =>
        entity.GetComponent<Griff.Scenes.Text>()!.Order = Runtime.Views.OrderAbove(inside, 0);
}

/// <summary>Creates an empty tilemap; selecting it puts the Scene view into tile painting.</summary>
[Category("Level")]
public sealed class TilemapPreset : EntityPreset
{
    public override string Id => "Entity.CreateTilemap";
    public override string Text => "Create Tile_map";
    public override int Order => 0;
    public override string? Icon => "Tiles";
    public override string EntityName(Scene scene) => "Tilemap";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Tilemap>().Tint = Color.FromBytes(0x48, 0x4B, 0x55);
}

/// <summary>Creates a particle emitter: sparks, smoke, dust, magic.</summary>
[Category("Lights and Effects")]
public sealed class ParticlesPreset : EntityPreset
{
    public override string Id => "Entity.CreateParticles";
    public override string Text => "Create Pa_rticles";
    public override int Order => 1;
    public override string? Icon => "Confetti";
    public override string EntityName(Scene scene) => "Particles";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<ParticleEmitter>();
}

/// <summary>Creates a light: an entity with a warm <see cref="Light"/>. It shows once the camera's Ambient is dark.</summary>
[Category("Lights and Effects")]
public sealed class LightPreset : EntityPreset
{
    public override string Id => "Entity.CreateLight";
    public override string Text => "Create Lig_ht";
    public override int Order => 0;
    public override string? Icon => "Lightbulb";
    public override string EntityName(Scene scene) => "Light";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Light>();
}

/// <summary>Creates weather: an entity with a <see cref="Weather"/>, rain to start with, that fills the view while the game runs.</summary>
[Category("Lights and Effects")]
public sealed class WeatherPreset : EntityPreset
{
    public override string Id => "Entity.CreateWeather";
    public override string Text => "Create Weather";
    public override int Order => 2;
    public override string? Icon => "Cloud";
    public override string EntityName(Scene scene) => "Weather";
    public override void Configure(Entity entity, Scene scene) => entity.AddComponent<Weather>();
}

/// <summary>Creates a room one camera view big, for a camera with a Follow to keep to and slide between.</summary>
[Category("Level")]
public sealed class RoomPreset : EntityPreset
{
    public override string Id => "Entity.CreateRoom";
    public override string Text => "Create Roo_m";
    public override int Order => 1;
    public override string? Icon => "Frame";
    public override string EntityName(Scene scene) => "Room";

    public override void Configure(Entity entity, Scene scene)
    {
        var size = scene.Entities.Select(e => e.GetComponent<Camera>()).OfType<Camera>().FirstOrDefault()?.Size ?? Runtime.Views.DefaultCameraSize;
        entity.AddComponent<Room>().Size = new Vector2(MathF.Ceiling(size * 2 * 16 / 9), size * 2);
    }
}

/// <summary>
/// Creates a lever: a small sprite with a trigger around it and a <see cref="Switch"/>. Name what it opens in its Targets
/// (or Hides), or set its Mode to make it a pressure plate or a button.
/// </summary>
[Category("Level")]
public sealed class SwitchPreset : EntityPreset
{
    public override string Id => "Entity.CreateSwitch";
    public override string Text => "Create S_witch";
    public override int Order => 3;
    public override string? Icon => "Lever";
    public override string EntityName(Scene scene) => "Switch";

    public override void Configure(Entity entity, Scene scene)
    {
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.3f, 0.8f);
        sprite.Tint = Color.FromBytes(0xC2, 0x4E, 0x4E);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = new Vector2(1.2f, 1.2f);
        collider.IsTrigger = true;
        entity.AddComponent<Switch>();
    }
}

/// <summary>Creates a ladder: a tall thin sprite behind things, with a trigger to climb. Stretch it with its Scale.</summary>
[Category("Level")]
public sealed class LadderPreset : EntityPreset
{
    public override string Id => "Entity.CreateLadder";
    public override string Text => "Create _Ladder";
    public override int Order => 2;
    public override string? Icon => "Climb";
    public override string EntityName(Scene scene) => "Ladder";

    public override void Configure(Entity entity, Scene scene)
    {
        var size = new Vector2(0.8f, 4);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Color.FromBytes(0x9C, 0x6B, 0x3C);
        sprite.Order = -1;
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.IsTrigger = true;
        entity.AddComponent<Ladder>();
    }
}

/// <summary>Creates a spring: a low solid block with a <see cref="Bouncer"/> that sends whatever lands on it flying.</summary>
[Category("Physics")]
public sealed class SpringPreset : EntityPreset
{
    public override string Id => "Entity.CreateSpring";
    public override string Text => "Create Sprin_g";
    public override int Order => 1;
    public override string? Icon => "Bounce";
    public override string EntityName(Scene scene) => "Spring";

    public override void Configure(Entity entity, Scene scene)
    {
        var size = new Vector2(1.2f, 0.5f);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Color.FromBytes(0x4C, 0x9A, 0xFF);
        entity.AddComponent<BoxCollider>().Size = size;
        entity.AddComponent<Bouncer>();
    }
}

/// <summary>
/// Creates a crate the player pushes along: a solid box with a <see cref="Body"/> and <see cref="Pushable"/>, on the Crate
/// layer. In a scene with grid movers it's a grid crate, pushed a cell at a time: it gets a <see cref="GridMovement"/> and
/// no gravity.
/// </summary>
[Category("Physics")]
public sealed class CratePreset : EntityPreset
{
    public override string Id => "Entity.CreateCrate";
    public override string Text => "Create Cr_ate";
    public override int Order => 0;
    public override string? Icon => "Cube";
    public override string EntityName(Scene scene) => "Crate";

    public override void Configure(Entity entity, Scene scene)
    {
        var sprite = entity.AddComponent<Sprite>();
        sprite.Tint = Color.FromBytes(0x9C, 0x6B, 0x3C);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Layer = "Crate";
        var body = entity.AddComponent<Body>();
        entity.AddComponent<Pushable>();
        if (TurnOrderPreset.GridMovers(scene).Any())
        {
            (sprite.Size, collider.Size) = (new Vector2(0.9f, 0.9f), new Vector2(0.9f, 0.9f));
            body.GravityScale = 0;
            entity.AddComponent<GridMovement>();
        }
    }
}
