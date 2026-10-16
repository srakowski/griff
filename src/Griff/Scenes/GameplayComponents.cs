using System.Numerics;
using Griff.Runtime;

namespace Griff.Scenes;

/// <summary>
/// Hit points. Scripts call <see cref="Damage"/> (or a <see cref="DamageOnTouch"/> does it with no code); after a hit the
/// entity can't be hurt again for <see cref="InvincibleTime"/>, flashing and blinking to show it. At zero it dies: its
/// behaviors hear <c>OnDeath</c>, the <see cref="DeathMessage"/> is sent, and it's destroyed.
/// </summary>
[Category("Combat")]
[WorksWith(typeof(Bar), Note = "a health bar (on another entity)")]
[ComponentName("Griff.Health")]
public sealed class Health : Component, ICarried
{
    private float _max = 3;
    private float _invincibleLeft;
    private bool _deathPending;

    /// <summary>Most hit points it can have. Changing it while at full health keeps it at full health.</summary>
    public float Max
    {
        get => _max;
        set
        {
            if (Current == _max)
                Current = value;
            _max = value;
        }
    }

    /// <summary>Hit points now. Normally starts at <see cref="Max"/>.</summary>
    [Output("Health", OutOf = nameof(Max))]
    public float Current
    {
        get => _current.Value;
        set => _current.Set(this, value);
    }

    // Kept as it was when the step began too, so a meter showing it fills and empties between steps.
    private Smoothed<float> _current = new(3);

    /// <summary>Its <see cref="Current"/> hit points in a frame drawn as <paramref name="blend"/> says, for a meter showing them.</summary>
    internal float CurrentDrawn(StepBlend blend) => blend.Of(_current);

    /// <summary>Seconds after a hit during which further damage is ignored. 0 lets every hit land.</summary>
    [Group("When Hit")]
    public float InvincibleTime { get; set; } = 0.5f;

    /// <summary>Flash the entity's sprites white when hit.</summary>
    public bool FlashOnHit { get; set; } = true;

    /// <summary>Blink the entity's sprites while it can't be hurt.</summary>
    [ShownWhen(nameof(InvincibleTime))]
    public bool BlinkWhileInvincible { get; set; } = true;

    /// <summary>Destroy the entity when it dies. Turn off to play a death animation first, and destroy it from a script.</summary>
    [Group("Death")]
    public bool DestroyOnDeath { get; set; } = true;

    /// <summary>A message sent with <c>Game.Send</c> when it dies, with the entity as its data, e.g. "PlayerDied" or "EnemyDied".</summary>
    [Names(NameKind.Message)]
    public string? DeathMessage { get; set; }

    /// <summary>
    /// Come back after dying instead of being destroyed: hidden for <see cref="RespawnDelay"/>, then back at full health at
    /// the active <see cref="Checkpoint"/> (or where it started), briefly invincible. For the player in a platformer.
    /// </summary>
    public bool Respawn { get; set; }

    /// <summary>Seconds it stays gone before it comes back (see <see cref="Respawn"/>).</summary>
    [ShownWhen(nameof(Respawn))]
    public float RespawnDelay { get; set; } = 1;

    /// <summary>Plays when damage lands but doesn't kill, from where it is. Make one with Audio &gt; New Sound (Hit).</summary>
    [Group("Sounds")]
    public string? HurtSound { get; set; }

    /// <summary>Plays when it dies, from where it is: an explosion, a last cry.</summary>
    public string? DeathSound { get; set; }

    /// <summary>
    /// Keeps its hit points (and its Max) in the game's save data under this name, e.g. "Hearts", so a health saved as the
    /// same name in the next scene starts with them (the player walking into the next level hurt) and so does the next
    /// run. Saved at zero (it died) it starts full. Empty keeps nothing.
    /// </summary>
    [Group("Saving")]
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveAs { get; set; }

    /// <summary>Set by the game once it has taken its hit points from the save data, so a rewind doesn't do it again. Leave it be.</summary>
    public bool Loaded { get; set; }

    public override bool Uses(string property) => property != nameof(Loaded);

    // What it last kept in the save data.
    private (float Current, float Max) _saved = (float.NaN, float.NaN);

    void ICarried.Load(ICarryStore data, string key)
    {
        if (data.Has(key))
        {
            if (data.Has(key + MaxKey))
                _max = Math.Max(0, data.GetFloat(key + MaxKey, _max));
            var current = Math.Min(data.GetFloat(key, Current), _max);
            Current = current > 0 ? current : _max;
        }

        _saved = (Current, _max);
    }

    bool ICarried.Save(ICarryStore data, string key, bool always)
    {
        if (!always && _saved == (Current, _max))
            return false;
        _saved = (Current, _max);
        data.Set(key, Current);
        data.Set(key + MaxKey, _max);
        return true;
    }

    // Its Max is kept under its Save As and this.
    private const string MaxKey = ".Max";

    public bool IsDead => Current <= 0;

    /// <summary>True for <see cref="InvincibleTime"/> after a hit.</summary>
    public bool IsInvincible => _invincibleLeft > 0;

    /// <summary>Raised when damage lands, with the amount and what dealt it (if known).</summary>
    public event Action<float, Entity?>? Damaged;

    /// <summary>Raised once, when <see cref="Current"/> reaches zero.</summary>
    public event Action? Died;

    /// <summary>Raised when it comes back after dying (see <see cref="Respawn"/>).</summary>
    public event Action? Respawned;

    /// <summary>What dealt the last blow that landed, if known: for scoring who defeated it.</summary>
    internal Entity? LastHitBy { get; private set; }

    // Where it was when the game first saw it, to respawn at without a checkpoint; and how long until it comes back.
    internal Vector2? Home { get; set; }

    internal float RespawnLeft { get; set; } = float.NaN;

    /// <summary>Back to full health, invincible (and blinking) for <see cref="InvincibleTime"/>. Called by the game.</summary>
    internal void Revive()
    {
        Current = Max;
        RespawnLeft = float.NaN;
        if (InvincibleTime > 0)
        {
            _invincibleLeft = InvincibleTime;
            if (BlinkWhileInvincible)
            {
                foreach (var sprite in Sprites())
                    sprite.Blink(InvincibleTime);
            }
        }

        Respawned?.Invoke();
    }

    /// <summary>
    /// Takes <paramref name="amount"/> hit points away, unless it's already dead or still invincible from the last hit.
    /// Returns whether the damage landed. <paramref name="source"/> is passed on to <c>OnDamage</c>.
    /// </summary>
    public bool Damage(float amount, Entity? source = null) => Damage(amount, source, always: false);

    /// <summary>
    /// As <see cref="Damage(float, Entity?)"/>; with <paramref name="always"/>, it lands even while it can't be hurt after the
    /// last hit: a blow that isn't a touch, such as a card's damage, each of which counts.
    /// </summary>
    internal bool Damage(float amount, Entity? source, bool always)
    {
        if (!(amount > 0) || IsDead || (IsInvincible && !always))
            return false;

        Current = Math.Max(0, Current - amount);
        LastHitBy = source;
        var sprites = Sprites().ToList();
        if (FlashOnHit)
        {
            foreach (var sprite in sprites)
                sprite.Flash();
        }

        if (Current > 0 && InvincibleTime > 0)
        {
            _invincibleLeft = InvincibleTime;
            if (BlinkWhileInvincible)
            {
                foreach (var sprite in sprites)
                    sprite.Blink(InvincibleTime);
            }
        }

        if (Current > 0)
            Hurt = true;
        Notify(listener => listener.Damaged(amount, source));
        Damaged?.Invoke(amount, source);
        if (IsDead)
            Die();
        return true;
    }

    /// <summary>Gives back up to <paramref name="amount"/> hit points, never past <see cref="Max"/>. Returns how many it got.</summary>
    public float Heal(float amount)
    {
        if (!(amount > 0) || IsDead)
            return 0;
        var before = Current;
        Current = Math.Min(Max, Current + amount);
        return Current - before;
    }

    /// <summary>Takes <paramref name="amount"/> hit points away, as a hit does (not while it can't be hurt after the last one).</summary>
    [Action("Damage")]
    private void DamageBy(float amount) => Damage(amount);

    /// <summary>Gives back up to <paramref name="amount"/> hit points, never past its Max.</summary>
    [Action("Heal")]
    private void HealBy(float amount) => Heal(amount);

    /// <summary>Dies straight away, invincible or not: falling out of the world, a reset button.</summary>
    [Action]
    public void Kill()
    {
        if (IsDead)
            return;
        Current = 0;
        Die();
    }

    /// <summary>Counts down invincibility. Called by the game.</summary>
    public void Advance(float seconds) => _invincibleLeft = Math.Max(0, _invincibleLeft - seconds);

    /// <summary>True once after each death, for the game to send the message and destroy the entity.</summary>
    // Set when damage lands without killing, until the game plays the hurt sound.
    internal bool Hurt { get; set; }

    internal bool TakeDeath()
    {
        var pending = _deathPending;
        _deathPending = false;
        return pending;
    }

    private void Die()
    {
        _deathPending = true;
        _invincibleLeft = 0;
        foreach (var sprite in Sprites())
            sprite.Blink(0);
        Notify(listener => listener.Died());
        Died?.Invoke();
    }

    // The entity's sprites and its children's: a ship and its nose flash together.
    private IEnumerable<Sprite> Sprites() =>
        Entity is null ? [] : Entity.SelfAndDescendants().SelectMany(e => e.GetComponents<Sprite>());

    // Tells the entity's scripts (OnDamage, OnDeath).
    private void Notify(Action<IHealthListener> action)
    {
        if (Entity is null)
            return;
        foreach (var listener in Entity.Components.OfType<IHealthListener>().ToList())
            action(listener);
    }

    /// <summary>The health that counts for <paramref name="entity"/>: its own, or its nearest ancestor's (a hitbox child's owner).</summary>
    public static Health? Of(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Health>() is { } health)
                return health;
        }

        return null;
    }
}

/// <summary>
/// Hurts whatever with <see cref="Health"/> it touches, with no code: spikes, lava, enemies, bullets. Works on solid
/// collisions and triggers, and keeps hurting while they stay touching (each hit still waits out the target's
/// <see cref="Health.InvincibleTime"/>). Needs a collider on the same entity.
/// </summary>
[Category("Combat")]
[Requires(typeof(Collider))]
[ComponentName("Griff.DamageOnTouch")]
public sealed class DamageOnTouch : Component
{
    /// <summary>How much health each hit takes.</summary>
    public float Amount { get; set; } = 1;

    /// <summary>
    /// Dice rolled for each hit in place of <see cref="Amount"/>, e.g. "1d8+2"; empty hits for Amount every time.
    /// Table Values sets it from a dice column given for Amount.
    /// </summary>
    [RollsFor(nameof(Amount))]
    [Dice]
    public string? AmountRoll
    {
        get => _roll.Text;
        set => _roll.Text = value;
    }

    private DiceRoll _roll;

    /// <summary>The damage of one hit: <see cref="AmountRoll"/> rolled, or <see cref="Amount"/>.</summary>
    public float RollAmount() => _roll.Roll(Amount);

    /// <summary>
    /// The collision layers it hurts, e.g. "Player" on spikes and enemies, "Enemy" on the player's bullets. Empty hurts
    /// anything with health.
    /// </summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    /// <summary>How hard a hit pushes the target's body away, in units per second. 0 doesn't push.</summary>
    public float Knockback { get; set; }

    /// <summary>Destroy this entity when it touches something it can hurt, like a bullet or a one-hit kamikaze enemy.</summary>
    public bool DestroyOnHit { get; set; }

    /// <summary>Kill outright, invincible or not, whatever the <see cref="Amount"/>: bottomless pits, lava, crushers.</summary>
    public bool Kill { get; set; }

    /// <summary>Destroy this entity when it runs into a wall or anything else solid it can't hurt: bullets, fireballs, arrows.</summary>
    public bool DestroyOnWall { get; set; }

    /// <summary>True if it hurts things on collision layer <paramref name="layer"/>.</summary>
    public bool Hurts(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }
}

/// <summary>
/// Something to collect, with no code: coins, gems, hearts, keys. When something it accepts touches it (see
/// <see cref="Layers"/>), it sends its <see cref="Message"/> with its <see cref="Amount"/> (a <see cref="Counter"/> adds it
/// up), plays its <see cref="Sound"/>, spawns its <see cref="Effect"/> and disappears. Needs a collider on the same entity,
/// usually a trigger.
/// </summary>
[Category("Gameplay")]
[Requires(typeof(Collider), Note = "usually a trigger")]
[WorksWith(typeof(Counter), Note = "adds up what's collected (on another entity)")]
[ComponentName("Griff.Pickup")]
public sealed class Pickup : Component
{
    /// <summary>The collision layers that can collect it, e.g. "Player". Empty lets anything with a <see cref="Body"/> collect it.</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    /// <summary>The message sent when it's collected, with <see cref="Amount"/> as its data. Empty sends nothing.</summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; } = "Score";

    /// <summary>How much it's worth: the data sent with its <see cref="Message"/>, and how many of its <see cref="Item"/>.</summary>
    public int Amount { get; set; } = 1;

    /// <summary>
    /// An item it puts in the collector's <see cref="Inventory"/> (one is added if it has none), e.g. "Key", or its team's
    /// items if it has none of its own (see <see cref="Items"/>): coins a hero collects for a strategy faction's gold.
    /// Empty puts nothing there.
    /// </summary>
    [Names(NameKind.Item, Defines = true)]
    public string? Item { get; set; }

    /// <summary>
    /// What it costs to collect, e.g. "10 Coin": a shop's wares. It stays where it is until whatever touches it can pay,
    /// and is paid from the collector's items (or its team's, see <see cref="Items"/>) as it's collected. Empty is free.
    /// </summary>
    [Counts(NameKind.Item, CountFirst = true)]
    public string? Price { get; set; }

    /// <summary>A sound played when it's collected, e.g. "Sounds/coin.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>
    /// Hit points it gives back to whoever collects it: a heart, a medkit. One that only heals (with no
    /// <see cref="Item"/>) is left where it is while their health is full.
    /// </summary>
    public float Heal { get; set; }

    /// <summary>A prefab spawned where it was, e.g. "Sparkle": a burst of particles, a score popup.</summary>
    [Names(NameKind.Prefab)]
    public string? Effect { get; set; }

    /// <summary>Gives the <see cref="Effect"/>'s particles this entity's sprite color, so one sparkle suits every pickup.</summary>
    [ShownWhen(nameof(Effect))]
    public bool TintEffect { get; set; } = true;

    /// <summary>Degrees per second it turns while waiting to be collected. 0 keeps it still.</summary>
    public float Spin { get; set; }

    /// <summary>True once it has been collected (it disappears at the end of that frame).</summary>
    [DoNotSerialize]
    public bool IsCollected { get; internal set; }

    /// <summary>Raised when it's collected, with whatever collected it.</summary>
    public event Action<Entity>? Collected;

    /// <summary>True if something on collision layer <paramref name="layer"/> can collect it.</summary>
    public bool AcceptsLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    internal void RaiseCollected(Entity by) => Collected?.Invoke(by);
}

/// <summary>When a <see cref="Counter"/> does something as its count reaches a value.</summary>
public enum CounterReach
{
    /// <summary>It just counts.</summary>
    Never,

    /// <summary>Once the count is at least its Reach: counting up to it (10 coins, a score of 1000).</summary>
    AtLeast,

    /// <summary>Once the count is at most its Reach: counting down to it (no moves left, the last enemy gone).</summary>
    AtMost,
}

/// <summary>
/// Keeps count and shows it in its entity's <see cref="Text"/>: a score, coins, kills. Every <see cref="Message"/> sent
/// adds to it (the number sent along, or 1; or just 1 with <see cref="OnePerMessage"/>), so it adds up
/// <see cref="Pickup"/>s with no code; scripts can call <see cref="Add"/>. It can count anything's output instead (a board's
/// score, the dungeon floor, a faction's gold: see <see cref="Output"/>). When it <see cref="Reaches"/> a value it sends its
/// <see cref="ReachMessage"/> and its own output Reached turns on. With <see cref="SaveBestAs"/> it remembers the highest
/// count ever, between runs.
/// </summary>
[Category("Gameplay")]
[WorksWith(typeof(Text), Note = "shows the count")]
[WorksWith(typeof(Trigger), Note = "doing something on its Reach Message")]
[ComponentName("Griff.Counter")]
public sealed class Counter : Component, ICarried
{
    /// <summary>The message that adds to it, e.g. "Score" for <c>Game.Send("Score", 10)</c>. Empty only counts <see cref="Add"/>.</summary>
    [Names(NameKind.Message, Hears = true)]
    public string? Message { get; set; } = "Score";

    /// <summary>
    /// Add 1 for each <see cref="Message"/>, whatever number it sends: a beat clock's Beat sends the beat's number (0, 1,
    /// 2...), so a counter of beats adds one a beat with this, rather than adding the numbers up.
    /// </summary>
    public bool OnePerMessage { get; set; }

    /// <summary>The count now. Set it to start from something other than 0.</summary>
    [Output("Count", OutOf = nameof(Total))]
    public int Value { get; set; }

    /// <summary>
    /// How the text reads: <c>{0}</c> is the count and <c>{1}</c> the best, e.g. "Score: {0}", "Best: {1}" or
    /// "{0:000000}" (and <c>{2}</c> what it's out of and <c>{3}</c> how many are left, e.g. with <see cref="CountGoals"/>
    /// how many goals there are and how many are still uncovered). Empty shows just the number.
    /// </summary>
    [Format("{0} the count", "{1} the best", "{2} what it's out of", "{3} how many are left", Preview = nameof(ReadsAs))]
    public string? Format { get; set; } = "Score: {0}";

    /// <summary>
    /// A name to keep the best count under in <c>Game.SaveData</c>, e.g. "best". Whenever the count beats it, it's saved,
    /// so another counter (on a menu, with Format "Best: {1}") can show it.
    /// </summary>
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveBestAs { get; set; }

    /// <summary>
    /// A message sent as the count first beats the best kept under <see cref="SaveBestAs"/> while it's being played (a new
    /// best), with the count as its data, e.g. "New Best": a sound player plays a sting on it. Not sent while nothing's
    /// been kept yet.
    /// </summary>
    [Names(NameKind.Message)]
    public string? NewBestMessage { get; set; }

    /// <summary>
    /// Keeps the count itself in the game's save data under this name, e.g. "Coins", so a counter saved as the same name
    /// in the next scene (or a minigame) carries on from it, and so does the next run: coins or a score kept from level to
    /// level. Empty keeps nothing.
    /// </summary>
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveAs { get; set; }

    /// <summary>Set by the game once it has taken its count from the save data, so a rewind doesn't do it again. Leave it be.</summary>
    public bool Loaded { get; set; }

    // The count it last kept in the save data.
    private int? _saved;

    void ICarried.Load(ICarryStore data, string key)
    {
        if (data.Has(key))
            Value = data.GetInt(key, Value);
        _saved = Value;
    }

    bool ICarried.Save(ICarryStore data, string key, bool always)
    {
        if (!always && _saved == Value)
            return false;
        _saved = Value;
        data.Set(key, Value);
        return true;
    }

    /// <summary>
    /// Another counter, by name, whose count multiplies what each <see cref="Message"/> (and the Add action) adds to this
    /// one: a score counted double, triple and so on while a "Multiplier" counter says so. Empty multiplies by nothing.
    /// </summary>
    [EntityName(Incoming = true)]
    public string? MultiplyBy { get; set; }

    /// <summary>The most the count goes up to: adding past it stops there, e.g. 5 for a multiplier up to 5x. 0 has no limit.</summary>
    public int Max { get; set; }

    /// <summary>How big the text pops when the count changes, springing back: 1.3 is a little pop, 1 none.</summary>
    public float Pop { get; set; } = 1.3f;

    /// <summary>
    /// An item to count instead of a message, e.g. "Key": it shows how many the first <see cref="Inventory"/> in the scene
    /// (usually the player's) holds.
    /// </summary>
    [Names(NameKind.Item)]
    public string? Item { get; set; }

    /// <summary>
    /// Count the goals in the scene that are covered instead of a message (a grid puzzle's Goals, with crates on them), with
    /// <c>{2}</c> in <see cref="Format"/> for how many goals there are, e.g. "Crates: {0} / {2}".
    /// </summary>
    public bool CountGoals { get; set; }

    /// <summary>
    /// Count the moves taken instead of a message: in a grid puzzle its Move History's, so an undo counts one back, e.g.
    /// "Moves: {0}"; on a Match Board its swaps, with <c>{2}</c> its Moves and <c>{3}</c> how many are left, e.g.
    /// "Moves: {3}".
    /// </summary>
    public bool CountMoves { get; set; }

    /// <summary>
    /// Count the scene's score instead of a message: a Match Board's, with <c>{2}</c> its Target Score and <c>{3}</c> how
    /// far there is to go, e.g. "Score: {0} / {2}"; or a Falling Board's.
    /// </summary>
    public bool CountScore { get; set; }

    /// <summary>
    /// Count the floor the scene's dungeon (a Dungeon Generator) is on instead of a message, e.g. "Floor {0}": it reads the
    /// dungeon, so it's right even in a panel that was closed as the floors went by.
    /// </summary>
    public bool CountFloor { get; set; }

    /// <summary>
    /// Count the things of one kind (a number, such as a tile) instead of a message: on a Match Board, the gems of that tile
    /// cleared, with <c>{2}</c> how many its Collect asks for and <c>{3}</c> how many are still to clear, e.g. "{0} / {2}".
    /// -1 counts no kind.
    /// </summary>
    [FormerName("CountGem")]
    public int CountKind { get; set; } = -1;

    /// <summary>
    /// An entity whose <see cref="Output"/> it counts, by name, e.g. "Board". Empty is this entity, or the nearest one it's
    /// inside with that output.
    /// </summary>
    [EntityName(Incoming = true)]
    [Group("Counting an Output")]
    public string? Track { get; set; }

    /// <summary>
    /// Count an output instead of a message: whatever it gives, now (see component_describe's Outputs), e.g. a board's
    /// "Score" or "Moves Left", a dungeon's "Floor", a turn order's "Round", a beat clock's "Beat", a faction's
    /// "Amount:Gold" or "Units", another counter's "Count". What it's out of, if it says, is <c>{2}</c> in <see cref="Format"/>.
    /// </summary>
    [OutputName(On = nameof(Track))]
    public string? Output { get; set; }

    /// <summary>
    /// Do something as the count reaches <see cref="Reach"/>: once it's at least that (counting up) or at most that
    /// (counting down). It sends <see cref="ReachMessage"/> each time it gets there, and its output Reached is on while it's
    /// there, for a wire into a door's Power.
    /// </summary>
    [Group("Reaching a Value")]
    public CounterReach Reaches { get; set; }

    /// <summary>The count it reaches, e.g. 10 coins, or 0 moves left.</summary>
    [ShownWhen(nameof(Reaches))]
    public int Reach { get; set; } = 10;

    /// <summary>
    /// A message sent as the count reaches <see cref="Reach"/>, with the count as its data: a trigger opens a door or a panel
    /// on it. Sent again if the count leaves it and comes back.
    /// </summary>
    [Names(NameKind.Message)]
    [ShownWhen(nameof(Reaches))]
    public string? ReachMessage { get; set; }

    /// <summary>True while the count is at (or past) <see cref="Reach"/>, the way <see cref="Reaches"/> says.</summary>
    [DoNotSerialize]
    [Output]
    public bool Reached => Reaches switch
    {
        CounterReach.AtLeast => Value >= Reach,
        CounterReach.AtMost => Value <= Reach,
        _ => false,
    };

    /// <summary>
    /// What the count is out of, for a counter that counts goals, a match board's moves, score or gems (<c>{2}</c> in
    /// <see cref="Format"/>); 0 otherwise.
    /// </summary>
    [DoNotSerialize]
    public int Total { get; internal set; }

    /// <summary>How far the count is from <see cref="Total"/>, never below 0 (<c>{3}</c> in <see cref="Format"/>): goals left, moves left.</summary>
    [DoNotSerialize]
    [Output]
    public int Left => Math.Max(0, Total - Value);

    /// <summary>The best count, from <see cref="SaveBestAs"/> (or this run's, without it).</summary>
    [DoNotSerialize]
    [Output]
    public int Best { get; internal set; }

    /// <summary>
    /// The best kept under <see cref="SaveBestAs"/> as this scene started: what the count has to beat to be a new best. -1
    /// until the game has read it. Set by the game.
    /// </summary>
    [Group("State")]
    public int BestToBeat { get; set; } = -1;

    /// <summary>True once the count has beaten the best kept under <see cref="SaveBestAs"/> as the scene started: a new best.</summary>
    [DoNotSerialize]
    [Output]
    public bool NewBest => !string.IsNullOrWhiteSpace(SaveBestAs) && BestToBeat >= 0 && Value > BestToBeat;

    /// <summary>Raised when the count changes.</summary>
    public event Action<Counter>? Changed;

    /// <summary>
    /// Adds <paramref name="amount"/> (take some away with a negative amount), times the count of the counter it's
    /// multiplied by (see <see cref="MultiplyBy"/>), never past its <see cref="Max"/>.
    /// </summary>
    [Action]
    public void Add(int amount = 1) => Change(amount * Multiplier());

    /// <summary>Sets the count to <paramref name="amount"/>, e.g. a multiplier back to 1 as the ball drains.</summary>
    [Action]
    public void Set(int amount = 0) => Change(amount - Value);

    // Changes the count by `amount`, never past its Max (the game counting an item, an output or a source sets it outright).
    internal void Change(int amount)
    {
        if (Max > 0 && amount > 0)
            amount = Math.Max(0, Math.Min(amount, Max - Value));
        if (amount == 0)
            return;
        WasReached ??= Reached;
        Value += amount;
        Changed?.Invoke(this);
    }

    // The count of the counter it's multiplied by (found by name, kept until the name changes), or 1.
    private int Multiplier()
    {
        var name = MultiplyBy;
        if (string.IsNullOrWhiteSpace(name) || Entity?.Scene is not { } scene)
            return 1;
        if (_multiplier is not { } kept || kept.Scene != scene || !ReferenceEquals(_multiplierName, name))
            (_multiplier, _multiplierName) = (HingeJoint.Nearest(scene, Entity, name.Trim()), name);
        return _multiplier?.GetComponent<Counter>() is { } other && other != this ? other.Value : 1;
    }

    private Entity? _multiplier;
    private string? _multiplierName;

    /// <summary>Starts the count again from 0.</summary>
    [Action]
    public void Reset()
    {
        if (Value == 0)
            return;
        WasReached ??= Reached;
        Value = 0;
        Changed?.Invoke(this);
    }

    /// <summary>The text to show: <see cref="Format"/> filled in, or just the number if it can't be.</summary>
    public string Display() => ReadsAs(Format);

    /// <summary>What its text says with <paramref name="format"/> as its <see cref="Format"/>, as the count is now.</summary>
    public string ReadsAs(string? format)
    {
        if (string.IsNullOrEmpty(format))
            return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, Value, Best, Total, Left);
        }
        catch (FormatException)
        {
            return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // Counts an item, an output, goals, moves, a board's score or gems rather than its Message.
    internal bool CountsOwn => !string.IsNullOrWhiteSpace(Item) || !string.IsNullOrWhiteSpace(Output) || CountsFromSource;

    // The output it counts, found once.
    internal OutputReader Reader { get; } = new();

    // Whether it had reached its value when last looked at, so it sends its Reach Message as it gets there (not when a
    // scene or a rewind starts it there).
    internal bool? WasReached { get; set; }

    // What its text was last written from, and what it said: written again only when that changes.
    internal (int Value, int Best, int Total, string? Format)? Displayed { get; set; }

    internal string? DisplayedText { get; set; }

    // A message's own fields only matter when it counts one.
    public override bool Uses(string property) => property switch
    {
        nameof(Message) => string.IsNullOrWhiteSpace(Output),
        nameof(OnePerMessage) => !CountsOwn,
        nameof(NewBestMessage) => !string.IsNullOrWhiteSpace(SaveBestAs),
        nameof(BestToBeat) or nameof(Loaded) => false,
        _ => true,
    };

    // Its arrow from what it counts says what it counts; from what it's multiplied by, that it multiplies.
    public override string? LinkLabel(string property) => property switch
    {
        nameof(Track) when !string.IsNullOrWhiteSpace(Output) => Output.Trim(),
        nameof(MultiplyBy) => "times",
        _ => null,
    };

    // Counts what a component in the scene counts (see ICounterSource) rather than a message or an item.
    internal bool CountsFromSource => CountGoals || CountMoves || CountScore || CountFloor || CountKind >= 0;

    // What the game last showed, and the entity's own scale (so a pop springs back to it).
    internal int? Shown { get; set; }

    internal Vector3? RestingScale { get; set; }

    // How long a pop takes to spring back, and how far one has got (seconds) from how big; null when it isn't popping.
    internal const float PopTime = 0.3f;

    internal (float Age, float Size)? Popping { get; set; }
}

/// <summary>Destroys its entity after <see cref="Seconds"/> of game time, optionally fading it out first: bullets, effects, popups.</summary>
[Category("Gameplay")]
[ComponentName("Griff.Lifetime")]
public sealed class Lifetime : Component
{
    /// <summary>Seconds until the entity is removed.</summary>
    public float Seconds { get; set; } = 2;

    /// <summary>Seconds at the end of its life spent fading out (see <c>FadeTo</c>). 0 just disappears.</summary>
    public float FadeOut { get; set; }

    /// <summary>Seconds it has been alive: kept with the scene, so a rewind goes on from where it was.</summary>
    [Group("State")]
    [NotSent]
    public float Age { get; set; }

    internal bool IsFading { get; set; }
}

/// <summary>
/// Spawns copies of a prefab every <see cref="Interval"/> seconds at its position, with no code: enemy waves, falling
/// rocks, collectibles that come back. Limit it with <see cref="Count"/> and <see cref="MaxAlive"/>. With
/// <see cref="Beats"/> set, it spawns on the beat of a <see cref="BeatClock"/> instead.
/// </summary>
[Category("Gameplay")]
[ComponentName("Griff.Spawner")]
public sealed class Spawner : Component
{
    private readonly List<Entity> _alive = [];

    /// <summary>The prefab to spawn, e.g. "Enemy" (Assets/Prefabs/Enemy.prefab).</summary>
    [Names(NameKind.Prefab)]
    public string? Prefab { get; set; }

    /// <summary>Seconds between spawns (unless it steps to the <see cref="Beats"/>).</summary>
    public float Interval { get; set; } = 2;

    /// <summary>Seconds before the first spawn.</summary>
    public float Delay { get; set; }

    /// <summary>How many to spawn in all; 0 keeps going forever.</summary>
    public int Count { get; set; }

    /// <summary>
    /// How many of its spawns may be around at once; 0 is no limit. When one is destroyed it makes room, and the next
    /// spawns as soon as it's due.
    /// </summary>
    public int MaxAlive { get; set; }

    /// <summary>Spawns land at a random spot up to this far from the spawner.</summary>
    [Distance]
    public float Radius { get; set; }

    /// <summary>Untick it to stop spawning for now. <see cref="Switch"/>es tick and untick it.</summary>
    [Powered]
    public bool Running { get; set; } = true;

    /// <summary>
    /// A flag that has to be set before it starts spawning, e.g. "AlarmRaised" (set by a trigger's Set Flag, a dialogue's
    /// "[set AlarmRaised]" or a script's <c>Game.SetFlag</c>), or "not AlarmRaised" to spawn only while it isn't. Its count
    /// down waits until then; its Spawn action still spawns one.
    /// </summary>
    [Names(NameKind.Flag, Condition = true)]
    public string? NeedsFlag { get; set; }

    /// <summary>
    /// Steps to the music of a <see cref="BeatClock"/>: spawns on every this many beats instead of every <see cref="Interval"/>
    /// seconds, once <see cref="Delay"/> has passed. A spawn that has no room waits for the next. 0 goes by seconds.
    /// </summary>
    [Group("Beat")]
    public int Beats { get; set; }

    /// <summary>Steps this many beats later: with two spawners on every 2 beats, one 1 beat later, they take turns.</summary>
    [ShownWhen(nameof(Beats))]
    public int BeatOffset { get; set; }

    /// <summary>The entity whose <see cref="BeatClock"/> it steps to. Empty follows the first in the scene.</summary>
    [EntityName]
    [ShownWhen(nameof(Beats))]
    public string? Clock { get; set; }

    /// <summary>How many it has spawned.</summary>
    [DoNotSerialize]
    public int Spawned { get; private set; }

    /// <summary>Its spawns still in the scene.</summary>
    public IReadOnlyList<Entity> Alive => _alive;

    /// <summary>True once it has spawned <see cref="Count"/>.</summary>
    public bool IsFinished => Count > 0 && Spawned >= Count;

    /// <summary>Raised with each new spawn, e.g. to give it a speed or a target.</summary>
    public event Action<Entity>? EntitySpawned;

    private float _untilNext = float.NaN;

    /// <summary>Counts down and spawns when due. Called by the game.</summary>
    internal void Advance(float seconds, IGameContext game)
    {
        ForgetGone(game.Scene);
        if (float.IsNaN(_untilNext))
            _untilNext = Delay;
        if (IsFinished || !Running || string.IsNullOrWhiteSpace(Prefab) || !game.FlagHolds(NeedsFlag))
            return;

        // On the beat, the count down is just its Delay, and each spawn waits for a beat it steps on.
        _untilNext -= seconds;
        if (Beats > 0)
            _untilNext = Math.Max(_untilNext, 0);
        if (_untilNext > 0 || (Beats > 0 && !game.OnBeat(Clock, Beats, BeatOffset)))
            return;

        // Full: wait for room, then spawn straight away (but just the one, not a burst for all the time spent waiting).
        if (MaxAlive > 0 && _alive.Count >= MaxAlive)
        {
            _untilNext = 0;
            return;
        }

        SpawnOne(game);
        _untilNext = Beats > 0 ? 0 : Math.Max(_untilNext + Math.Max(Interval, 0.01f), 0);
    }

    /// <summary>
    /// Spawns one now, running or not: a wave on a message, a crate from a button. Not once it has spawned its
    /// <see cref="Count"/>, or while <see cref="MaxAlive"/> of its spawns are around. Its own timing carries on as it was.
    /// </summary>
    [Action("Spawn")]
    private void SpawnNow(IGameContext game)
    {
        ArgumentNullException.ThrowIfNull(game);
        ForgetGone(game.Scene);
        if (IsFinished || string.IsNullOrWhiteSpace(Prefab) || (MaxAlive > 0 && _alive.Count >= MaxAlive))
            return;
        SpawnOne(game);
    }

    // Its spawns no longer in the scene make room, without any garbage.
    private void ForgetGone(Scene scene)
    {
        for (var i = _alive.Count - 1; i >= 0; i--)
        {
            if (_alive[i].Scene != scene)
                _alive.RemoveAt(i);
        }
    }

    private void SpawnOne(IGameContext game)
    {
        var position = Entity!.Transform.WorldPosition;
        var entity = game.Spawn(Prefab!, new Vector2(position.X, position.Y) + (Radius > 0 ? Rand.InsideCircle(Radius) : Vector2.Zero));
        _alive.Add(entity);
        Spawned++;
        EntitySpawned?.Invoke(entity);
    }
}

/// <summary>
/// Types out its entity's <see cref="Text"/> a letter at a time, for dialogue and story cards. Whenever a script sets the
/// text's Value, the new text types out from the start. Runs on real time, so it works in a paused game.
/// </summary>
[Category("Story")]
[Requires(typeof(Text), Note = "the text it types out")]
[ComponentName("Griff.Typewriter")]
public sealed class Typewriter : Component
{
    private string? _typing;
    private int _shown;
    private float _time;
    private bool _running;
    private bool _nextAtOnce;

    /// <summary>Letters per second; 0 shows everything at once.</summary>
    public float Speed { get; set; } = 30;

    /// <summary>Extra seconds to wait after a full stop, question or exclamation mark (half that after a comma).</summary>
    public float PunctuationPause { get; set; } = 0.2f;

    /// <summary>A sound played as letters appear, e.g. "Sounds/blip.wav". Empty is silent.</summary>
    public string? Sound { get; set; }

    /// <summary>Play <see cref="Sound"/> every this many letters.</summary>
    [ShownWhen(nameof(Sound))]
    public int SoundEvery { get; set; } = 2;

    /// <summary>How many letters are showing.</summary>
    public int Shown => _running ? _shown : (_typing?.Length ?? 0);

    /// <summary>True once the whole text is showing (and always outside a running game).</summary>
    public bool IsDone => !_running || _typing is null || _shown >= _typing.Length;

    /// <summary>Raised when the text has finished typing out.</summary>
    public event Action? Finished;

    /// <summary>Shows the rest of the text straight away, e.g. when the player presses a key mid-line.</summary>
    public void Skip()
    {
        if (_typing is null || IsDone)
            return;
        _shown = _typing.Length;
        Finished?.Invoke();
    }

    /// <summary>Types the text out again from the start.</summary>
    public void Restart() => (_shown, _time) = (0, 0);

    /// <summary>Shows the next change to the text all at once instead of typing it, e.g. a dialogue choice picked.</summary>
    internal void ShowNextAtOnce() => _nextAtOnce = true;

    /// <summary>The part of <paramref name="text"/> to draw now.</summary>
    public string? Visible(string? text) =>
        !_running || text is null || text != _typing ? text : text[..Math.Min(_shown, text.Length)];

    /// <summary>Reveals letters for <paramref name="seconds"/>; returns true if <see cref="Sound"/> should play. Called by the game.</summary>
    internal bool Advance(float seconds)
    {
        var value = Entity?.GetComponent<Text>()?.Shown;
        if (!_running || value != _typing)
        {
            _running = true;
            _typing = value;
            Restart();
            if (_nextAtOnce)
                _shown = _typing?.Length ?? 0;
            _nextAtOnce = false;
        }

        if (_typing is null || _shown >= _typing.Length)
            return false;
        if (!(Speed > 0))
        {
            Skip();
            return false;
        }

        var blip = false;
        _time += seconds;
        while (_shown < _typing.Length)
        {
            var cost = 1 / Speed + (_shown > 0 ? PauseAfter(_typing[_shown - 1]) : 0);
            if (_time < cost)
                break;
            _time -= cost;
            _shown++;
            if (!char.IsWhiteSpace(_typing[_shown - 1]) && _shown % Math.Max(1, SoundEvery) == 0)
                blip = true;
        }

        if (_shown >= _typing.Length)
            Finished?.Invoke();
        return blip && !string.IsNullOrWhiteSpace(Sound);
    }

    private float PauseAfter(char c) => c switch
    {
        '.' or '!' or '?' => PunctuationPause,
        ',' or ';' or ':' => PunctuationPause / 2,
        _ => 0,
    };
}

/// <summary>A component told when its entity's <see cref="Health"/> is hurt or dies: scripts are, once started.</summary>
internal interface IHealthListener
{
    void Damaged(float amount, Entity? source);

    void Died();
}

/// <summary>
/// A component a <see cref="Counter"/> can count from instead of a message, when it counts goals, moves, a score, a floor
/// or things of a kind (Count Goals, Count Moves, Count Score, Count Floor, Count Kind): a puzzle's covered goals and moves,
/// a match board's swaps, score or gems cleared, a falling-block board's score, a dungeon's floor. The counter shows the
/// first one in the scene that counts what it asks for. A script component can implement it too.
/// </summary>
public interface ICounterSource
{
    /// <summary>
    /// What <paramref name="counter"/> shows of this, and what that's out of (its Total, <c>{2}</c> in its Format; 0 for
    /// nothing), or null if this doesn't count what the counter asks for.
    /// </summary>
    (int Value, int Total)? CountFor(Counter counter);
}

/// <summary>
/// A component that does something when a message is sent with <c>Game.Send</c>, with no script listening: a dungeon going
/// down a floor on its Next Floor Message. Called where the game is run (offline, or on the host), for those on active
/// entities. A script component can implement it too.
/// </summary>
public interface IHearsMessages
{
    /// <summary>Hears <paramref name="message"/> sent with <paramref name="data"/>; returns true if it did something with it.</summary>
    bool Hear(string message, object? data);
}

/// <summary>
/// A component that picks things with the number keys and the mouse wheel (a block world's hotbar): other controls on its
/// entity that could use them too (building tools) leave them to it and are picked another way. A script component can
/// implement it too.
/// </summary>
public interface IClaimsNumberKeys
{
}
