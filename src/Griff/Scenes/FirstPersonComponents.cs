using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Shows the game in first person, through the eyes of its <see cref="Viewer"/>: the level you paint as a top-down map is
/// stood up around the player. Solid tilemaps become walls (textured with their tiles), sprites stand on the floor and
/// always face the player (enemies, pickups, barrels), and <see cref="Block"/>s are boxes of wall (doors, pillars). The
/// player's <see cref="Weapon"/> is held at the bottom of the screen, under a crosshair. Put it on the camera; the HUD
/// (anything with a <see cref="ScreenAnchor"/>) still goes on top.
/// </summary>
/// <remarks>
/// The map is the floor plan: X and Y run across the ground, and the viewer looks the way its Rotation Z points (0 is to
/// the right in the Scene view). One world unit is as tall as <see cref="WallHeight"/> says, so a 1-unit tile makes a
/// cube of wall. In a dark scene (the camera's Ambient), <see cref="Light"/>s light the walls, floor and sprites near them.
/// </remarks>
[Category("First Person")]
[Requires(typeof(Camera))]
[WorksWith(typeof(FirstPersonMovement), Note = "on the viewer: walk, strafe and turn")]
[WorksWith(typeof(Weapon), Note = "on the viewer: shown in hand")]
[ComponentName("Griff.FirstPersonView")]
public sealed class FirstPersonView : Component
{
    /// <summary>The name of the entity to look through, e.g. "Player". Empty looks through the camera's own entity.</summary>
    public string? Viewer { get; set; } = "Player";

    /// <summary>How wide the view is, in degrees across the screen. 60 to 90 suits most games.</summary>
    public float FieldOfView { get; set; } = 70;

    /// <summary>How tall walls are, in world units. With 1-unit tiles, 1 makes each a cube.</summary>
    public float WallHeight { get; set; } = 1;

    /// <summary>How high the viewer's eyes are off the floor, in world units: half the <see cref="WallHeight"/> looks straight across.</summary>
    public float EyeHeight { get; set; } = 0.5f;

    /// <summary>How many strips the view is drawn in, across the screen: more is sharper, fewer is chunkier and faster.</summary>
    public int Columns { get; set; } = 360;

    /// <summary>The floor's color, close up. It fades into <see cref="Fog"/> in the distance.</summary>
    public Color Floor { get; set; } = Color.FromBytes(0x3A, 0x34, 0x30);

    /// <summary>The ceiling's (or sky's) color, close up.</summary>
    public Color Ceiling { get; set; } = Color.FromBytes(0x24, 0x27, 0x30);

    /// <summary>What everything fades into far away: black for a dark dungeon, a haze for outdoors. Transparent for no fog.</summary>
    public Color Fog { get; set; } = Color.FromBytes(0x10, 0x11, 0x16);

    /// <summary>How far away, in world units, things have faded completely into the <see cref="Fog"/>.</summary>
    public float FogDistance { get; set; } = 16;

    /// <summary>Shade walls that face one way darker than those that face the other, so corners read clearly.</summary>
    public bool ShadeSides { get; set; } = true;

    /// <summary>How much the view (and the weapon) bob as the viewer walks. 0 keeps it steady.</summary>
    public float Bob { get; set; } = 1;

    /// <summary>The crosshair's color. Transparent for none.</summary>
    public Color Crosshair { get; set; } = new(1, 1, 1, 0.8f);

    /// <summary>Show a small map of the walls around the viewer in the top-right corner, with enemies and pickups on it.</summary>
    public bool Minimap { get; set; } = true;

    /// <summary>The color the screen flashes when the viewer is hurt. Transparent for none.</summary>
    public Color HurtFlash { get; set; } = new(0.85f, 0.1f, 0.1f, 0.45f);
}

/// <summary>
/// First-person controls with no code: W and S (or up and down) walk, A and D strafe, the left and right arrows turn,
/// and the mouse looks around once the game is clicked (Esc gives the mouse back). Holding Dash (Left Shift) runs. On a
/// controller the left stick walks and strafes and the right stick turns. It turns the entity's Rotation Z, the way a
/// <see cref="FirstPersonView"/> looks. Needs a <see cref="Body"/> and a collider (a circle slides along walls best).
/// </summary>
[Category("First Person")]
[Requires(typeof(Body))]
[Requires(typeof(Collider))]
[WorksWith(typeof(Weapon), Note = "shoots where it looks")]
[ComponentName("Griff.FirstPersonMovement")]
public sealed class FirstPersonMovement : Component
{
    /// <summary>Walking speed, in world units per second.</summary>
    public float Speed { get; set; } = 4;

    /// <summary>Speed while Dash is held. The same as <see cref="Speed"/> for no running.</summary>
    public float RunSpeed { get; set; } = 7;

    /// <summary>How quickly it gets up to speed and stops, in units per second per second. 0 is at once.</summary>
    public float Acceleration { get; set; } = 40;

    /// <summary>Degrees per second the arrow keys (and the right stick, all the way over) turn it.</summary>
    public float TurnSpeed { get; set; } = 150;

    /// <summary>Turn with the mouse. The game takes the mouse when it's clicked, and gives it back with Esc.</summary>
    public bool MouseLook { get; set; } = true;

    /// <summary>Degrees turned for each pixel the mouse moves.</summary>
    public float MouseSensitivity { get; set; } = 0.15f;

    /// <summary>Let the mouse (and the right stick) look up and down a little, as well as turn.</summary>
    public bool LookUpDown { get; set; } = true;

    /// <summary>Untick it (from a script, in a cutscene) to ignore the player's input.</summary>
    public bool Controllable { get; set; } = true;

    /// <summary>A footstep sound, played every <see cref="StepLength"/> units walked.</summary>
    public string? StepSound { get; set; }

    /// <summary>World units between footsteps.</summary>
    public float StepLength { get; set; } = 1.6f;

    /// <summary>Degrees it's looking up (positive) or down (negative), with <see cref="LookUpDown"/>.</summary>
    [DoNotSerialize]
    public float Pitch { get; set; }

    /// <summary>The way it's facing across the floor, as a unit vector.</summary>
    [DoNotSerialize]
    public Vector2 Facing
    {
        get
        {
            var angle = (Entity?.Transform.Rotation.Z ?? 0) * MathF.PI / 180;
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }
    }

    /// <summary>How fast it's walking, from 0 (standing) to 1 (full speed): for the view's bob.</summary>
    [DoNotSerialize]
    public float Stride { get; internal set; }

    // How far it has walked, for the bob and footsteps.
    internal float Walked { get; set; }

    internal float NextStep { get; set; }
}

/// <summary>
/// A gun held in first person, with no code: Fire shoots where the player looks. It hits the first thing in the way
/// straight away (or launches a <see cref="Projectile"/>), hurting anything with <see cref="Health"/>. Several weapons on
/// one player are picked with the number keys (their <see cref="Slot"/>) or the mouse wheel. Put it on the entity a
/// <see cref="FirstPersonView"/> looks through.
/// </summary>
/// <remarks>
/// Ammo comes from the player's <see cref="Inventory"/>: a weapon with <see cref="Ammo"/> "Shells" uses up a shell a
/// shot, a <see cref="Pickup"/> with Item "Shells" gives more, and a <see cref="Counter"/> with Item "Shells" shows how
/// many are left. A weapon with <see cref="NeedsItem"/> can't be used until it's picked up, and is taken out as soon as it is.
/// </remarks>
[Category("First Person")]
[WorksWith(typeof(Inventory), Note = "holds its ammo")]
[WorksWith(typeof(FirstPersonMovement), Note = "aims it")]
[ComponentName("Griff.Weapon")]
public sealed class Weapon : Component
{
    /// <summary>The weapon's name, e.g. "Shotgun".</summary>
    public string? Name { get; set; } = "Pistol";

    /// <summary>The number key that takes it out, 1 to 9.</summary>
    public int Slot { get; set; } = 1;

    /// <summary>The input action that fires it, e.g. "Fire" (the left mouse button, J, or the gamepad's right trigger).</summary>
    public string? Action { get; set; } = "Fire";

    /// <summary>Hit points each bullet (or pellet) takes away.</summary>
    public float Damage { get; set; } = 1;

    /// <summary>Shots a second, at most.</summary>
    public float FireRate { get; set; } = 3;

    /// <summary>Keep firing while the action is held, like a machine gun. Otherwise it's one shot a press.</summary>
    public bool Automatic { get; set; }

    /// <summary>How far bullets reach, in world units.</summary>
    public float Range { get; set; } = 30;

    /// <summary>Bullets in each shot: 1 for a pistol, 7 or so for a shotgun.</summary>
    public int Pellets { get; set; } = 1;

    /// <summary>Degrees the bullets scatter across.</summary>
    public float Spread { get; set; } = 1;

    /// <summary>How hard a hit pushes what it hits, in units per second. 0 doesn't push.</summary>
    public float Knockback { get; set; } = 2;

    /// <summary>The <see cref="Inventory"/> item each shot uses up, e.g. "Bullets". Empty never runs out.</summary>
    public string? Ammo { get; set; }

    /// <summary>An item the player must have to use it, e.g. "Shotgun", given by a <see cref="Pickup"/>. Empty is always ready.</summary>
    public string? NeedsItem { get; set; }

    /// <summary>A prefab to launch instead of hitting straight away, e.g. "Rocket" or "Plasma". It needs a <see cref="Body"/> to fly.</summary>
    public string? Projectile { get; set; }

    /// <summary>How fast a <see cref="Projectile"/> flies, in world units per second.</summary>
    public float ProjectileSpeed { get; set; } = 12;

    /// <summary>A prefab spawned where a bullet hits, e.g. "Puff": sparks off a wall, a splash on an enemy.</summary>
    public string? ImpactEffect { get; set; }

    /// <summary>The sound of a shot, e.g. "Sounds/shoot.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>A click when it's out of ammo.</summary>
    public string? EmptySound { get; set; }

    /// <summary>The picture of the weapon in hand, drawn at the bottom of the screen. Without one it's drawn as a simple gun in <see cref="Color"/>.</summary>
    public string? Image { get; set; }

    /// <summary>The picture shown for a moment as it fires, e.g. with a flash at the muzzle. Empty uses <see cref="Image"/>.</summary>
    public string? FireImage { get; set; }

    /// <summary>The gun's color when it has no <see cref="Image"/> (or a tint over the image).</summary>
    public Color Color { get; set; } = Color.FromBytes(0x9A, 0xA0, 0xAA);

    /// <summary>How big it's held, as a fraction of the screen's height.</summary>
    public float Size { get; set; } = 0.45f;

    /// <summary>The color of the flash at the muzzle as it fires. Transparent for none.</summary>
    public Color MuzzleFlash { get; set; } = Color.FromBytes(0xFF, 0xD8, 0x6B);

    /// <summary>How far it jumps back as it fires, as a fraction of its size.</summary>
    public float Kick { get; set; } = 0.15f;

    /// <summary>Shakes the screen by this much with each shot. 0 for none.</summary>
    public float Shake { get; set; }

    /// <summary>True while it can't fire again yet.</summary>
    [DoNotSerialize]
    public bool IsCoolingDown => Cooldown > 0;

    /// <summary>Raised with each shot fired.</summary>
    public event Action<Weapon>? Fired;

    /// <summary>Raised when a bullet hits something, with what it hit (a wall's tilemap, or something with health).</summary>
    public event Action<Entity>? Hit;

    internal void RaiseFired() => Fired?.Invoke(this);

    internal void RaiseHit(Entity entity) => Hit?.Invoke(entity);

    internal float Cooldown { get; set; }

    // Seconds since it last fired, for the kick and the flash.
    internal float SinceFired { get; set; } = float.PositiveInfinity;
}

/// <summary>
/// How a sprite stands in a <see cref="FirstPersonView"/>. Without one, sprites stand on the floor; this lifts them off
/// it (a lamp on the ceiling, a fireball at chest height) or bobs them up and down (a floating pickup).
/// </summary>
[Category("First Person")]
[Requires(typeof(Sprite))]
[ComponentName("Griff.Billboard")]
public sealed class Billboard : Component
{
    /// <summary>How high its bottom edge is off the floor, in world units.</summary>
    public float Lift { get; set; }

    /// <summary>How far it bobs up and down, in world units. 0 keeps it still.</summary>
    public float Bob { get; set; }

    /// <summary>Bobs a second.</summary>
    public float BobSpeed { get; set; } = 1;

    /// <summary>Sprite size multiplier in first person, so the top-down map can use a different size.</summary>
    public float Scale { get; set; } = 1;
}

/// <summary>
/// Shows the entity as a solid box of wall in a <see cref="FirstPersonView"/> rather than a flat sprite: doors, gates,
/// pillars, secret walls. The box is its <see cref="BoxCollider"/>, as tall as the view's walls, textured with its
/// <see cref="Sprite"/> (or that sprite's color). Move it with a <see cref="Mover"/> or hide it with a <see cref="Switch"/>
/// to open it.
/// </summary>
[Category("First Person")]
[Requires(typeof(BoxCollider))]
[WorksWith(typeof(Sprite), Note = "its image or color covers the box")]
[WorksWith(typeof(Mover), Note = "slides it open")]
[ComponentName("Griff.Block")]
public sealed class Block : Component
{
    /// <summary>Shade it like the walls in the fog and dark. Untick it for something that glows: a lit sign, a force field.</summary>
    public bool Shaded { get; set; } = true;
}
