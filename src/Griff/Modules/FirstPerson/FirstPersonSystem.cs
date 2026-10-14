using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.FirstPerson;

/// <summary>
/// Runs first-person games with no code: walking, strafing and turning (with the mouse held by the game), weapons that
/// shoot where the player looks, and the screen flashing when they're hurt. It draws the view itself
/// (<see cref="FirstPersonDrawing"/>), and the game is seen and heard through the player's eyes.
/// </summary>
internal sealed class FirstPersonSystem : GameSystem
{
    // How far the view may look up or down, in degrees.
    private const float MaxPitch = 30;

    public override string Name => "First Person";

    public FirstPersonState State { get; private set; } = new();

    private FirstPersonState Fps => State;

    private Scene Scene => Game.Scene;

    protected override void SceneChanged() => State = new FirstPersonState();

    protected override void DrawView(FrameDrawing frame) => FirstPersonDrawing.Draw(frame);

    protected override Entity? Eyes => FirstPersonViewer;

    // The player seen through, and in a network game, each player's own character named as the viewer.
    protected override bool LooksThrough(Entity entity) =>
        FirstPersonView is { } view && (ViewerOf(view) == entity ||
                                        (Game.Net.IsOnline && entity.GetComponent<FirstPersonMovement>() is not null && view.Viewer?.Trim() == entity.Name));

    /// <summary>The camera's <see cref="FirstPersonView"/>, if the game is played in first person.</summary>
    public FirstPersonView? FirstPersonView =>
        SceneRenderer.FindCamera(Scene)?.GetComponent<FirstPersonView>() is { } view && view.Entity!.IsActive ? view : null;

    /// <summary>The entity a <see cref="FirstPersonView"/> looks through (the player), if there is one.</summary>
    public Entity? FirstPersonViewer => FirstPersonView is { } view ? ViewerOf(view) : null;

    /// <summary>The weapon the first-person player has out, if any (see <see cref="Weapon"/>).</summary>
    public Weapon? CurrentWeapon => FirstPersonViewer is { } viewer ? CurrentWeaponOf(viewer) : null;

    /// <summary>Takes out <paramref name="weapon"/> (one of the player's), if it can be used; returns whether it did.</summary>
    public bool Equip(Weapon weapon)
    {
        if (weapon.Entity is not { } owner || !CanUse(weapon))
            return false;
        if (Fps.Weapons.GetValueOrDefault(owner) != weapon)
        {
            Fps.Weapons[owner] = weapon;
            weapon.PutAway();
            Fps.Swapped = 0;
            Fps.Lowered = new Smoothed<float>(1);
        }

        return true;
    }

    internal Entity? ViewerOf(FirstPersonView view)
    {
        var name = view.Viewer?.Trim();
        if (string.IsNullOrEmpty(name))
            return view.Entity;
        // A viewer away respawning is still looked through, from where it fell. In a network game, through your own eyes.
        return Scene.FindNamed(name, activeOnly: true) ?? Scene.FindNamed(name) ?? view.Entity;
    }

    internal void Run()
    {
        var view = FirstPersonView;
        var movements = Game.Moving<FirstPersonMovement>();
        Game.Input.WantsMouseLock = view is not null && !Game.PausedThisFrame && Game.TopPanel is null &&
                                    movements.Any(m => m.MouseLook && m.Controllable && Game.InputOf(m.Entity) == Game.Input);
        if (Time.Delta <= 0 || Game.PausedThisFrame)
            return;

        foreach (var movement in movements)
            RunFirstPersonMovement(movement);
        RunWeapons();

        Fps.Swapped += Time.Delta;
        Fps.Lowered.Set(Scene, Fps.Swapped < FirstPersonState.RaiseSeconds ? 1 - Fps.Swapped / FirstPersonState.RaiseSeconds : 0);
        if (view is null || ViewerOf(view) is not { } viewer)
            return;

        // Hurt: the screen flashes, and shakes a little.
        var health = Health.Of(viewer);
        var current = health?.Current ?? 0;
        if (health is not null && Fps.LastHealth is { } last && current < last && view.HurtFlash.A > 0)
        {
            Game.FlashScreen(view.HurtFlash, 0.3f);
            Game.ShakeCamera(0.08f, 0.2f);
        }

        Fps.LastHealth = health is null ? null : current;
    }

    private void RunFirstPersonMovement(FirstPersonMovement m)
    {
        var entity = m.Entity!;
        if (entity.GetComponent<Body>() is not { } body)
            return;
        var dt = Time.Delta;
        var control = m.Controllable;
        var Input = Game.InputOf(entity);

        // Turning: the arrow keys, the right stick, and the mouse while the game holds it.
        var turn = 0f;
        var look = 0f;
        if (control)
        {
            var keys = (Input.IsDown(Key.Left) ? 1f : 0f) - (Input.IsDown(Key.Right) ? 1f : 0f);
            turn += (keys - Input.RightStick.X) * m.TurnSpeed * dt;
            look += Input.RightStick.Y * m.TurnSpeed * 0.5f * dt;
            if (m.MouseLook && Input.IsMouseLocked)
            {
                turn -= Input.MouseDelta.X * m.MouseSensitivity;
                look += Input.MouseDelta.Y * m.MouseSensitivity;
            }
        }

        if (turn != 0)
        {
            var rotation = entity.Transform.Rotation;
            var z = (rotation.Z + turn) % 360;
            entity.Transform.Rotation = rotation with { Z = z < -180 ? z + 360 : z > 180 ? z - 360 : z };
        }

        m.Pitch = m.LookUpDown ? Math.Clamp(m.Pitch + look, -MaxPitch, MaxPitch) : 0;

        // Walking and strafing, relative to the way it faces.
        var forward = 0f;
        var strafe = 0f;
        if (control)
        {
            forward = (Input.IsDown(Key.W) || Input.IsDown(Key.Up) ? 1f : 0f) - (Input.IsDown(Key.S) || Input.IsDown(Key.Down) ? 1f : 0f);
            strafe = (Input.IsDown(Key.D) ? 1f : 0f) - (Input.IsDown(Key.A) ? 1f : 0f);
            if (forward == 0 && strafe == 0)
                (forward, strafe) = (Input.LeftStick.Y, Input.LeftStick.X);
        }

        var facing = m.Facing;
        var right = new Vector2(facing.Y, -facing.X);
        var wish = facing * forward + right * strafe;
        if (wish.LengthSquared() > 1)
            wish = Vector2.Normalize(wish);
        var speed = control && Input.IsDown("Dash") ? Math.Max(m.RunSpeed, m.Speed) : m.Speed;
        var target = wish * speed;
        body.Velocity = m.Acceleration > 0 ? Game.MoveTowards(body.Velocity, target, m.Acceleration * dt) : target;
        body.FloatThisStep = true;

        // The bob and footsteps follow how far it actually walks (not into a wall).
        var moved = body.Velocity.Length() * dt;
        m.Stride = m.Speed > 0 ? Math.Clamp(body.Velocity.Length() / m.Speed, 0, 1) : 0;
        m.Walked += moved;
        if (!string.IsNullOrWhiteSpace(m.StepSound) && m.StepLength > 0 && m.Walked >= m.NextStep)
        {
            if (m.NextStep > 0)
                Game.Audio.Play(m.StepSound.Trim(), 0.5f, 0, Rand.Range(0.9f, 1.1f));
            m.NextStep = m.Walked + m.StepLength;
        }
    }

    // ----- Weapons -----

    private bool CanUse(Weapon weapon) =>
        string.IsNullOrWhiteSpace(weapon.NeedsItem) || weapon.Entity?.GetComponent<Inventory>()?.Has(weapon.NeedsItem.Trim()) == true;

    private Weapon? CurrentWeaponOf(Entity owner)
    {
        var weapons = owner.GetComponents<Weapon>().ToList();
        if (Fps.Weapons.TryGetValue(owner, out var current) && weapons.Contains(current) && CanUse(current))
            return current;
        current = weapons.Where(CanUse).OrderBy(w => w.Slot).FirstOrDefault();
        if (current is null)
            Fps.Weapons.Remove(owner);
        else
            Fps.Weapons[owner] = current;
        return current;
    }

    private void RunWeapons()
    {
        var all = Game.Active<Weapon>();
        if (all.Count == 0)
            return;
        var owners = all.Select(w => w.Entity!).Distinct().ToList();
        foreach (var owner in owners)
        {
            var weapons = owner.GetComponents<Weapon>().OrderBy(w => w.Slot).ToList();
            foreach (var weapon in weapons)
            {
                weapon.Cooldown = Math.Max(0, weapon.Cooldown - Time.Delta);
                weapon.SinceFired += Time.Delta;
            }

            // A weapon just picked up is taken out straight away.
            var usable = weapons.Where(CanUse).ToList();
            var known = Fps.Usable.GetValueOrDefault(owner);
            if (known is not null && usable.FirstOrDefault(w => !known.Contains(w)) is { } fresh)
                Equip(fresh);
            Fps.Usable[owner] = usable.ToHashSet();

            var controllable = owner.GetComponent<FirstPersonMovement>() is not { Controllable: false };
            if (controllable)
                SwitchWeapons(usable, Game.InputOf(owner));
            if (CurrentWeaponOf(owner) is { } current && controllable)
                RunWeapon(current, owner);
        }
    }

    // The number keys take out the weapon in that slot; the mouse wheel and the gamepad's Y go through them in turn.
    private void SwitchWeapons(List<Weapon> usable, IInput Input)
    {
        if (usable.Count == 0)
            return;
        Key[] numbers = [Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9];
        for (var i = 0; i < numbers.Length; i++)
        {
            if (Input.WasPressed(numbers[i]) && usable.FirstOrDefault(w => w.Slot == i + 1) is { } chosen)
                Equip(chosen);
        }

        var step = Input.Scroll > 0 ? -1 : Input.Scroll < 0 ? 1 : Input.WasPressed(GamepadButton.Y) ? 1 : 0;
        if (step == 0)
            return;
        var at = usable.IndexOf(CurrentWeaponOf(usable[0].Entity!)!);
        Equip(usable[((at < 0 ? 0 : at) + step + usable.Count) % usable.Count]);
    }

    private void RunWeapon(Weapon weapon, Entity owner)
    {
        var action = string.IsNullOrWhiteSpace(weapon.Action) ? "Fire" : weapon.Action.Trim();
        var keys = Game.InputOf(owner);
        var pressed = keys.WasPressed(action);
        var wants = weapon.Automatic ? keys.IsDown(action) : pressed;
        // A weapon just taken out is raised before it fires.
        if (!wants || weapon.Cooldown > 0 || Fps.Swapped < FirstPersonState.RaiseSeconds)
            return;

        var inventory = owner.GetComponent<Inventory>();
        if (!string.IsNullOrWhiteSpace(weapon.Ammo) && inventory?.Take(weapon.Ammo.Trim()) != true)
        {
            if (pressed)
                Game.PlayIfSet(weapon.EmptySound);
            weapon.Cooldown = 0.25f;
            return;
        }

        Fire(weapon, owner);
    }

    /// <summary>
    /// Fires <paramref name="weapon"/> once from its owner the way the owner faces, whether or not Fire is held and
    /// without using ammo: for scripts and cutscenes.
    /// </summary>
    public void Fire(Weapon weapon)
    {
        if (weapon.Entity is { } owner)
            Fire(weapon, owner);
    }

    private void Fire(Weapon weapon, Entity owner)
    {
        weapon.Cooldown = weapon.FireRate > 0 ? 1 / weapon.FireRate : 0;
        weapon.SinceFired = 0;
        Game.PlayIfSet(weapon.Sound);
        if (weapon.Shake > 0)
            Game.ShakeFor(owner, weapon.Shake, 0.15f);

        var position = owner.Transform.WorldPosition;
        var origin = new Vector2(position.X, position.Y);
        var angle = owner.Transform.Rotation.Z * MathF.PI / 180;
        var pellets = Math.Max(1, weapon.Pellets);
        var spread = Math.Max(0, weapon.Spread) * MathF.PI / 180;
        var damage = new Dictionary<Health, (float Amount, Vector2 Direction)>();
        for (var i = 0; i < pellets; i++)
        {
            var off = spread > 0 ? Rand.Range(-spread / 2, spread / 2) : 0;
            var direction = new Vector2(MathF.Cos(angle + off), MathF.Sin(angle + off));
            if (!string.IsNullOrWhiteSpace(weapon.Projectile))
            {
                Launch(weapon, owner, origin, direction);
                continue;
            }

            if (Game.Physics.Raycast(Scene, origin, direction, weapon.Range, ignore: owner) is not { } hit)
                continue;
            if (!string.IsNullOrWhiteSpace(weapon.ImpactEffect))
                Game.Spawn(weapon.ImpactEffect.Trim(), hit.Point - direction * 0.1f);
            weapon.RaiseHit(hit.Entity);
            if (Health.Of(hit.Entity) is { } health && health.Entity != owner && Game.CanHurt(owner, health.Entity!))
            {
                var sum = damage.GetValueOrDefault(health);
                damage[health] = (sum.Amount + weapon.Damage, direction);
            }
        }

        // Every pellet that hits the same thing lands as one blow, so a shotgun at close range hits hard.
        foreach (var (health, (amount, direction)) in damage)
        {
            var landed = health.Damage(amount, owner);
            if (landed && weapon.Knockback > 0 && health.Entity!.GetComponent<Body>() is { } body)
                body.Velocity += direction * weapon.Knockback;
        }

        weapon.RaiseFired();
    }

    private void Launch(Weapon weapon, Entity owner, Vector2 origin, Vector2 direction)
    {
        var shot = Game.Spawn(weapon.Projectile!.Trim(), origin + direction * 0.45f);
        Game.Claim(shot, owner);
        if (shot.GetComponent<Body>() is { } body)
            body.Velocity = direction * weapon.ProjectileSpeed;
        shot.Transform.Rotation = shot.Transform.Rotation with { Z = MathF.Atan2(direction.Y, direction.X) * 180 / MathF.PI };
    }
}

/// <summary>What a first-person game keeps track of between frames.</summary>
internal sealed class FirstPersonState
{
    // Each owner's weapon in hand, and the weapons it could use last frame (to take out one just picked up).
    public Dictionary<Entity, Weapon> Weapons { get; } = [];

    public Dictionary<Entity, HashSet<Weapon>> Usable { get; } = [];

    // Seconds a weapon takes to come up as it's taken out, before it can fire.
    public const float RaiseSeconds = 0.2f;

    // Seconds since a weapon was taken out.
    public float Swapped { get; set; } = 1;

    // How far down the weapon in hand still is as it's taken out (1 just taken out, 0 once it's up), as each step left it,
    // so it's drawn coming up between steps (a weapon just taken out is drawn as it is: all the way down).
    public Smoothed<float> Lowered;

    // The viewer's health last frame, to flash the screen when it drops.
    public float? LastHealth { get; set; }
}
