using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Close-up attacks with no code: a sword swing, a punch, a bite. When its <see cref="Action"/> is pressed (or, for an
/// enemy, when its <see cref="Target"/> comes within <see cref="Range"/>), it winds up for <see cref="Windup"/> seconds,
/// then brings out its <see cref="Hitbox"/> (a child entity with a trigger collider and a <see cref="DamageOnTouch"/>)
/// for <see cref="ActiveTime"/> seconds, then rests for <see cref="Cooldown"/>. Each swing hurts each thing once, however
/// long the hitbox stays on it.
/// </summary>
/// <remarks>
/// The hitbox is hidden the rest of the time, and turned to face the way its owner faces: placed to the right of it in the
/// editor, it swings left when the player faces left (and up or down in a top-down game). If the owner has an
/// <see cref="Animation"/> with an <c>Attack</c> clip (or <c>AttackUp</c> and <c>AttackDown</c> in a top-down game), it
/// plays while the attack does.
/// </remarks>
[Category("Combat")]
[WorksWith(typeof(Animation), Note = "plays an Attack clip")]
[ComponentName("Griff.Attack")]
public sealed class Attack : Component
{
    /// <summary>The input action that swings it, e.g. "Attack" (X, L, the right mouse button or the gamepad's X). Empty for an enemy.</summary>
    [Names(NameKind.Action)]
    public string? Action { get; set; } = "Attack";

    /// <summary>The name of the child entity that does the damage while the attack is out.</summary>
    [EntityName(Inside = true)]
    public string? Hitbox { get; set; } = "Hitbox";

    /// <summary>Seconds between the swing starting and the hitbox coming out: a heavy hammer's wind-up, or an enemy's tell.</summary>
    [Group("Timing")]
    public float Windup { get; set; } = 0.05f;

    /// <summary>Seconds the hitbox is out.</summary>
    public float ActiveTime { get; set; } = 0.15f;

    /// <summary>Seconds after the hitbox goes away before it can swing again. A press meanwhile swings as soon as it can.</summary>
    public float Cooldown { get; set; } = 0.2f;

    /// <summary>A speed to lunge forward at as the hitbox comes out, in world units per second. 0 stands still.</summary>
    public float Lunge { get; set; }

    /// <summary>Freezes the game for this many seconds when a swing lands, so it hits hard. 0 for none.</summary>
    public float HitStop { get; set; } = 0.05f;

    /// <summary>A sound played as the hitbox comes out, e.g. "Sounds/swing.wav".</summary>
    [Group("Sound")]
    public string? Sound { get; set; }

    /// <summary>For an enemy with no <see cref="Action"/>: the name of what it attacks, e.g. "Player".</summary>
    [EntityName]
    [Group("For Enemies")]
    public string? Target { get; set; } = "Player";

    /// <summary>For an enemy: how close its target has to be before it swings, in world units.</summary>
    [Distance]
    public float Range { get; set; } = 1.5f;

    /// <summary>True from the start of a swing until its hitbox goes away.</summary>
    [DoNotSerialize]
    public bool IsAttacking => Phase is AttackPhase.Windup or AttackPhase.Active;

    /// <summary>True while the hitbox is out.</summary>
    [DoNotSerialize]
    public bool IsHitting => Phase == AttackPhase.Active;

    /// <summary>Raised as the hitbox comes out.</summary>
    public event Action? Swung;

    /// <summary>Raised when a swing lands on something, with what it hit.</summary>
    public event Action<Entity>? Hit;

    /// <summary>Swings now if it isn't already swinging or cooling down; returns whether it did. For scripts and cutscenes.</summary>
    public bool Swing()
    {
        if (Phase != AttackPhase.Ready)
            return false;
        (Phase, PhaseTime) = (AttackPhase.Windup, 0);
        Struck.Clear();
        return true;
    }

    internal void RaiseSwung() => Swung?.Invoke();

    internal void RaiseHit(Entity target) => Hit?.Invoke(target);

    internal AttackPhase Phase { get; set; }

    internal float PhaseTime { get; set; }

    // Pressed and waiting to swing as soon as it can.
    internal bool Queued { get; set; }

    // Who this swing has already hurt, so a hitbox that lingers on them hurts them just once.
    internal HashSet<Entity> Struck { get; } = [];

    // The hitbox's own place and size as laid out in the editor, facing right; it's turned from these.
    internal (Vector3 Position, Vector2? Size)? Resting { get; set; }
}

internal enum AttackPhase
{
    Ready,
    Windup,
    Active,
    Cooldown,
}

/// <summary>
/// A roguelike's attack, with no code: its <see cref="GridMovement"/> stepping into something with <see cref="Health"/>
/// on its <see cref="Layers"/> hurts it by <see cref="Damage"/> instead of bumping, with a lunge toward it and back over a
/// step's time. Holding the way keeps hitting. On a player it attacks the monsters it walks into; on a monster with a
/// <see cref="GridChase"/> whose Stop Distance is 0, it attacks the player it walks into.
/// </summary>
/// <remarks>
/// Taking turns, each hit uses an action, as a step does. A <see cref="Health"/> with an Invincible Time ignores hits
/// while it lasts: 0 lets each step's hit land. In a network game the host does the damage.
/// </remarks>
[Category("Combat")]
[Requires(typeof(GridMovement))]
[WorksWith(typeof(GridChase), Note = "with Stop Distance 0, a monster that hits what it chases")]
[ComponentName("Griff.BumpAttack")]
public sealed class BumpAttack : Component
{
    /// <summary>Hit points each hit takes away.</summary>
    public float Damage { get; set; } = 1;

    /// <summary>
    /// Dice rolled for each hit's damage in place of <see cref="Damage"/>, e.g. "1d8+2"; empty hits for Damage every time.
    /// Table Values sets it from a dice column given for Damage.
    /// </summary>
    [RollsFor(nameof(Damage))]
    [Dice]
    public string? DamageRoll
    {
        get => _roll.Text;
        set => _roll.Text = value;
    }

    private DiceRoll _roll;

    /// <summary>The damage of one hit: <see cref="DamageRoll"/> rolled, or <see cref="Damage"/>.</summary>
    public float RollDamage() => _roll.Roll(Damage);

    /// <summary>The collision layers it hurts, e.g. "Enemy" (or "Player" for a monster). Empty hurts anything with health.</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; } = "Enemy";

    /// <summary>A sound played with each hit, e.g. "Sounds/hit.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>A prefab spawned on what it hits, e.g. "Sparkle": a burst of particles, a damage number.</summary>
    [Names(NameKind.Prefab)]
    public string? Effect { get; set; }

    /// <summary>Freezes the game for this many seconds when a hit lands, so it hits hard. 0 for none.</summary>
    public float HitStop { get; set; }

    /// <summary>Raised with each hit, with what it hit.</summary>
    public event Action<Entity>? Hit;

    /// <summary>True if it hurts things on collision layer <paramref name="layer"/>.</summary>
    public bool Hurts(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    internal void RaiseHit(Entity target) => Hit?.Invoke(target);
}
