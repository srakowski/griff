using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Levels with no code: movers, patrols, chasers, shooters, checkpoints and respawning, doors and dialogue.
public sealed partial class Game
{
    // How often a chaser finds its way around walls again, in seconds of game time.
    private const float ChasePathEvery = 0.25f;

    // Within this of a waypoint counts as there.
    private const float WaypointReach = 0.15f;

    private readonly LevelSystem _levels = new();

    /// <summary>The <see cref="Dialogue"/> being said right now, if any.</summary>
    public Dialogue? Talking => _levels.Talking;

    /// <summary>
    /// Fades the screen to <paramref name="color"/> (black by default) over <paramref name="seconds"/> of real time, calls
    /// <paramref name="then"/> while it's covered, and fades back in just as long: teleports, a jump to the next room, a
    /// cut in a cutscene. 0 seconds calls it straight away.
    /// </summary>
    public void FadeThrough(float seconds, Action then, Color? color = null)
    {
        ArgumentNullException.ThrowIfNull(then);
        if (!(seconds > 0))
        {
            then();
            return;
        }

        _fadingOut = (null, then, seconds);
        _fadeInSeconds = 0;
        FadeColor = color ?? Color.Black;
    }

    // What this machine runs: everything offline and on the host; on a player's machine, only its own things (HUD, menus).
    internal ComponentList<T> Active<T>() where T : Component => Scene.NetFlagged == 0 ? Scene.Active<T>() : Where(Scene.Active<T>(), Runs);

    // What this machine moves: what it runs, less other players' own characters, plus this player's own.
    internal ComponentList<T> Moving<T>() where T : Component => Scene.NetFlagged == 0 ? Scene.Active<T>() : Where(Scene.Active<T>(), Moves);

    // Everything showing, wherever it's run: for what only changes how things look.
    internal ComponentList<T> Showing<T>() where T : Component => Scene.Active<T>();

    // Everything this machine runs, active or not.
    internal ComponentList<T> RunHere<T>() where T : Component => Scene.NetFlagged == 0 ? Scene.All<T>() : Where(Scene.All<T>(), Runs);

    // Those whose entity passes: the same list when they all do (always, offline), so a frame allocates nothing.
    private static ComponentList<T> Where<T>(ComponentList<T> components, Func<Entity, bool> test) where T : Component
    {
        var all = components.AsSpan();
        var first = 0;
        while (first < all.Length && test(all[first].Entity!))
            first++;
        if (first == all.Length)
            return components;

        var kept = new List<T>(all.Length);
        for (var i = 0; i < all.Length; i++)
        {
            if (i < first || (i > first && test(all[i].Entity!)))
                kept.Add(all[i]);
        }

        return new ComponentList<T>([.. kept]);
    }

    // Movers go before physics, so whatever stands on them is carried along this frame.
    private void RunMovers()
    {
        foreach (var mover in Active<Mover>())
            mover.Advance(Time.Delta, mover.Beats > 0 ? ClockFor(mover.Clock) : null);
    }

    // Chasers first, so a patrol knows to stand aside while its chase has the target in sight.
    private void RunEnemies()
    {
        if (Time.Delta <= 0)
            return;
        // Waiting for their turn, they stand where they are.
        foreach (var chase in Active<Chase>())
        {
            if (Waiting(chase.Entity))
                HoldStill(chase.Entity!);
            else
                RunChase(chase);
        }

        foreach (var patrol in Active<Patrol>())
        {
            if (Waiting(patrol.Entity))
                HoldStill(patrol.Entity!);
            else
                RunPatrol(patrol);
        }
    }

    private void RunPatrol(Patrol patrol)
    {
        var entity = patrol.Entity!;
        // Where it starts from, before it moves, for CountPatrolWalks.
        patrol.LastX ??= entity.Transform.Position.X;
        if (entity.GetComponent<Body>() is not { } body || entity.GetComponent<Chase>() is { IsChasing: true })
            return;

        var direction = patrol.GoingLeft ? -1 : 1;
        var pastEnd = direction > 0 ? patrol.Right > 0 && patrol.Walked >= patrol.Right : patrol.Left > 0 && patrol.Walked <= -patrol.Left;
        if (pastEnd || body.WallSide == direction || (patrol.TurnAtEdges && body.IsGrounded && !GroundAhead(entity, direction)))
        {
            patrol.Turn();
            direction = -direction;
        }

        body.Velocity = body.Velocity with { X = direction * patrol.Speed };
        if (patrol.FlipSprite)
            Face(entity, direction);
    }

    // At the end of the frame, once everything has moved them, patrols count how far they are from where they were
    // placed, however they got there (walking, chasing, knockback, a respawn), so a snapshot holds where they are.
    private void CountPatrolWalks()
    {
        foreach (var patrol in Active<Patrol>())
        {
            var x = patrol.Entity!.Transform.Position.X;
            if (patrol.LastX is { } last)
                patrol.Walked += x - last;
            patrol.LastX = x;
        }
    }

    // Is there something to stand on just past its leading foot? The ray starts a little above the foot, so a slope
    // rising ahead (whose surface there is above the foot) counts as ground too; on a slope it reaches further down,
    // since a slope falling away ahead drops as far as the body is wide.
    private bool GroundAhead(Entity entity, int direction)
    {
        if (entity.GetComponent<Collider>() is not { } collider)
            return true;
        var bounds = Physics.BoundsOf(collider);
        var x = direction > 0 ? bounds.Max.X + 0.05f : bounds.Min.X - 0.05f;
        var rise = Math.Min(0.3f, bounds.HalfSize.Y);
        var origin = new Vector2(x, bounds.Min.Y + rise);
        var drop = Physics.IsOnSlope(Scene, collider) ? bounds.HalfSize.X * 2 + 0.1f : 0;
        return Physics.Raycast(Scene, origin, -Vector2.UnitY, rise + 0.25f + drop, ignore: entity) is not null;
    }

    private void RunChase(Chase chase)
    {
        var entity = chase.Entity!;
        if (entity.GetComponent<Body>() is not { } body)
            return;

        // In a network game there's a "Player" for everyone: go after whoever is nearest.
        if (chase.TargetEntity?.Scene != Scene || (Net.IsOnline && !string.IsNullOrWhiteSpace(chase.Target)))
            chase.TargetEntity = string.IsNullOrWhiteSpace(chase.Target) ? null : Nearest(chase.Target.Trim(), Flat(entity.Transform.WorldPosition));
        var target = chase.TargetEntity;

        var here = Flat(entity.Transform.WorldPosition);
        var wasChasing = chase.IsChasing;
        var chasing = false;
        if (target is not null && target.IsActive && Health.Of(target) is not { IsDead: true })
        {
            var distance = Vector2.Distance(here, Flat(target.Transform.WorldPosition));
            var reach = wasChasing ? chase.Range + Math.Max(0, chase.GiveUp) : chase.Range;
            chasing = chase.Range <= 0 || distance <= reach;
            if (chasing && !wasChasing && chase.NeedsSight && !CanSee(entity, target))
                chasing = false;
        }

        chase.IsChasing = chasing;
        if (chasing && !wasChasing)
        {
            chase.PathAge = float.PositiveInfinity;
            chase.RaiseSpotted();
        }

        var falls = body.GravityScale != 0 && Physics.Gravity != Vector2.Zero;
        if (!chasing)
        {
            // Stand still, unless a patrol on the same entity has walking to do.
            if (wasChasing || entity.GetComponent<Patrol>() is null)
                body.Velocity = falls ? body.Velocity with { X = 0 } : Vector2.Zero;
            return;
        }

        var toTarget = Flat(target!.Transform.WorldPosition) - here;
        var stop = Math.Max(chase.StopDistance, WaypointReach);
        if (falls)
        {
            var vx = MathF.Abs(toTarget.X) <= stop ? 0 : MathF.Sign(toTarget.X) * chase.Speed;
            body.Velocity = body.Velocity with { X = vx };
            if (chase.FlipSprite && vx != 0)
                Face(entity, MathF.Sign(vx));
            return;
        }

        if (toTarget.Length() <= stop)
        {
            body.Velocity = Vector2.Zero;
            return;
        }

        var goal = NextStepTowards(chase, here, target);
        var velocity = goal - here is var way && way.LengthSquared() > 1e-8f ? Vector2.Normalize(way) * chase.Speed : Vector2.Zero;
        body.Velocity = velocity;
        if (chase.FlipSprite && MathF.Abs(velocity.X) > 1e-3f)
            Face(entity, MathF.Sign(velocity.X));
    }

    // Straight at the target, or the next corner of the way around the walls of the chase's tilemap.
    private Vector2 NextStepTowards(Chase chase, Vector2 here, Entity target)
    {
        var there = Flat(target.Transform.WorldPosition);
        if (string.IsNullOrWhiteSpace(chase.Around) || Find(chase.Around.Trim())?.GetComponent<Tilemap>() is not { } map)
            return there;

        chase.PathAge += Time.Delta;
        if (chase.PathAge >= ChasePathEvery)
        {
            chase.Path = map.FindPath(here, there);
            chase.PathAge = 0;
        }

        var path = chase.Path;
        var next = 0;
        while (next < path.Count - 1 && Vector2.Distance(here, path[next]) <= WaypointReach)
            next++;
        if (next > 0)
            chase.Path = path.Skip(next).ToList();

        // The last waypoint is the middle of the target's cell; the target itself is better.
        return chase.Path.Count > 1 ? chase.Path[0] : there;
    }

    // Nothing solid between the two, other than parts of either.
    private bool CanSee(Entity looker, Entity target)
    {
        var from = Flat(looker.Transform.WorldPosition);
        var toTarget = Flat(target.Transform.WorldPosition) - from;
        var hit = Physics.Raycast(Scene, from, toTarget, toTarget.Length(), ignore: looker);
        return hit is null || hit.Value.Entity.IsSelfOrDescendantOf(target);
    }

    internal static void Face(Entity entity, int direction)
    {
        if (entity.GetComponent<Sprite>() is { } sprite)
            sprite.FlipX = direction < 0;
    }

    // Nothing fires while game time stands still: a pause, a hit stop, a conversation.
    private void RunShooters()
    {
        if (Time.Delta <= 0)
            return;
        foreach (var shooter in Active<Shooter>())
        {
            if (!shooter.Running)
                continue;
            var entity = shooter.Entity!;
            var here = Flat(entity.Transform.WorldPosition);
            var aim = shooter.Direction;
            bool wantsToFire;

            if (!string.IsNullOrWhiteSpace(shooter.Action))
            {
                var keys = InputOf(entity);
                wantsToFire = keys.IsDown(shooter.Action);
                if (keys.AimFrom(here) is var aimed && aimed != Vector2.Zero)
                    aim = aimed;
            }
            else if (!string.IsNullOrWhiteSpace(shooter.Target))
            {
                var target = Nearest(shooter.Target.Trim(), here);
                wantsToFire = target is not null && target.IsActive && Health.Of(target) is not { IsDead: true };
                if (target is not null)
                {
                    aim = Flat(target.Transform.WorldPosition) - here;
                    wantsToFire &= shooter.Range <= 0 || aim.Length() <= shooter.Range;
                    wantsToFire &= !shooter.NeedsSight || CanSee(entity, target);
                }
            }
            else
            {
                wantsToFire = true;
            }

            // Not while it waits for its turn.
            wantsToFire &= !Waiting(entity);

            var onBeat = shooter.Beats > 0 && OnBeat(shooter.Clock, shooter.Beats, shooter.BeatOffset);
            if (!shooter.Ready(Time.Delta, wantsToFire, onBeat) || string.IsNullOrWhiteSpace(shooter.Prefab))
                continue;

            Volley(shooter, here, aim);
            UseAction(entity);
        }
    }

    void IGameContext.FireVolley(Shooter shooter) => FireVolley(shooter);

    /// <summary>A shooter's Fire action: one volley now, at its target if it has one in the scene, otherwise its way.</summary>
    internal void FireVolley(Shooter shooter)
    {
        if (shooter.Entity is not { } entity || entity.Scene != Scene || string.IsNullOrWhiteSpace(shooter.Prefab))
            return;
        var here = Flat(entity.Transform.WorldPosition);
        var aim = shooter.Direction;
        if (!string.IsNullOrWhiteSpace(shooter.Target) && Nearest(shooter.Target.Trim(), here) is { } target)
            aim = Flat(target.Transform.WorldPosition) - here;
        Volley(shooter, here, aim);
    }

    private void Volley(Shooter shooter, Vector2 here, Vector2 aim)
    {
        var entity = shooter.Entity!;
        aim = aim.LengthSquared() > 1e-8f ? Vector2.Normalize(aim) : Vector2.UnitX;
        foreach (var direction in shooter.VolleyAround(aim))
        {
            var shot = Spawn(shooter.Prefab!.Trim(), here + shooter.Offset);
            Claim(shot, entity);
            if (shot.GetComponent<Body>() is { } body)
                body.Velocity = direction * shooter.Speed;
            shot.Transform.Rotation = shot.Transform.Rotation with { Z = MathF.Atan2(direction.Y, direction.X) * 180 / MathF.PI };
            shooter.RaiseFired(shot);
        }

        if (!string.IsNullOrWhiteSpace(shooter.Sound))
            PlayShared(shooter.Sound);
    }

    // Who's touching what this frame, for checkpoints, doors and dialogue: the thing touched, and whatever touched it
    // (the body that owns the collider, so a player's hitbox child counts as the player), with its collider's layer.
    internal IEnumerable<(Entity By, string? Layer)> TouchersOf(Entity entity) =>
        _touches.Where(t => t.Item1 == entity && t.Item2.Scene == Scene)
            .Select(t => (By: BodyOwner(t.Item2), Layer: LayerOf(t.Item2)))
            .Where(t => t.By is not null && !entity.IsSelfOrDescendantOf(t.By))
            .Select(t => (t.By!, t.Layer))
            .Distinct();

    private void RunCheckpoints()
    {
        var all = RunHere<Checkpoint>();
        foreach (var checkpoint in all)
        {
            if (!checkpoint.Entity!.IsActive || checkpoint.Active)
                continue;
            var (by, _) = TouchersOf(checkpoint.Entity!).FirstOrDefault(t => checkpoint.AcceptsLayer(t.Layer));
            if (by is null)
                continue;

            foreach (var other in all)
                other.Active = false;
            checkpoint.Active = true;
            Flash(checkpoint.Entity!);
            if (!string.IsNullOrWhiteSpace(checkpoint.Sound))
                PlayShared(checkpoint.Sound);
            checkpoint.RaiseReached(by);
            if (!string.IsNullOrWhiteSpace(checkpoint.Message))
                Send(checkpoint.Message.Trim(), checkpoint.Entity, from: checkpoint.Entity);
        }

        // The active one shows its color; the others go back to theirs.
        foreach (var checkpoint in all)
        {
            if (checkpoint.Entity!.GetComponent<Sprite>() is not { } sprite)
                continue;
            checkpoint.RestingTint ??= sprite.Tint;
            var tint = checkpoint.Active && checkpoint.ActiveTint.A > 0 ? checkpoint.ActiveTint : checkpoint.RestingTint.Value;
            if (sprite.Tint != tint)
                sprite.Tint = tint;
        }
    }

    // Dead and set to respawn: gone for a moment, then back at the active checkpoint (or where it started).
    private void RunRespawns()
    {
        foreach (var health in RunHere<Health>())
        {
            if (!health.Respawn || !health.IsDead)
                continue;
            var entity = health.Entity!;
            if (float.IsNaN(health.RespawnLeft))
            {
                health.RespawnLeft = Math.Max(0, health.RespawnDelay);
                entity.IsEnabled = false;
            }

            health.RespawnLeft -= Time.Delta;
            if (health.RespawnLeft > 0)
                continue;

            var checkpoint = Scene.Active<Checkpoint>().FirstOrDefault(c => c.Active);
            var position = checkpoint is not null ? Flat(checkpoint.Entity!.Transform.WorldPosition) : health.Home ?? Flat(entity.Transform.WorldPosition);
            entity.IsEnabled = true;
            PlaceAt(entity, position);
            health.Revive();
        }
    }

    // Moves an entity to a world position at once, as if it had always been there: no velocity, cameras following it snap.
    private void PlaceAt(Entity entity, Vector2 world)
    {
        var target = new Vector3(world, entity.Transform.WorldPosition.Z);
        if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            target = Vector3.Transform(target, inverse);
        entity.Transform.Teleport(new Vector3(target.X, target.Y, entity.Transform.Position.Z));
        if (entity.GetComponent<Body>() is { } body)
            (body.Velocity, body.Spin) = (Vector2.Zero, 0);
        foreach (var follow in Scene.All<Follow>().Where(f => f.TargetEntity == entity))
            follow.Snap();
    }

    // Nobody goes through a door while a panel pauses the game.
    private void RunDoors()
    {
        if (PausedThisFrame)
            return;
        var doors = Active<Door>();
        if (doors.Count == 0)
        {
            if (Time.Delta > 0)
                _levels.ArrivedByDoor.Clear();
            return;
        }

        var touchingAnyDoor = new HashSet<Entity>();
        foreach (var door in doors)
        {
            var users = TouchersOf(door.Entity!).Where(t => door.AcceptsLayer(t.Layer)).Select(t => t.By).ToList();
            touchingAnyDoor.UnionWith(users);
            var ready = door.Locked || !FlagHolds(door.NeedsFlag) ? [] : users.Where(u => !_levels.ArrivedByDoor.Contains(u) && CanUnlock(door, u)).ToList();
            ShowPrompt(door.Entity!, door.Prompt, ready.Count > 0);

            // One trip at a time: not while a scene change or another door's fade is under way.
            if (ready.Count == 0 || _fadingOut is not null || _pendingScene is not null)
                continue;
            var user = door.NeedsInteract ? ready.FirstOrDefault(u => InputOf(u).WasPressed("Interact")) : ready[0];
            if (user is null)
                continue;
            UseDoor(door, user);
        }

        // Physics only reports touches while game time runs; don't forget arrivals during a pause.
        if (Time.Delta > 0)
            _levels.ArrivedByDoor.IntersectWith(touchingAnyDoor);
    }

    private void UseDoor(Door door, Entity user)
    {
        UseKey(door, user);
        if (!string.IsNullOrWhiteSpace(door.Sound))
            PlayShared(door.Sound);
        door.RaiseUsed(user);

        if (!string.IsNullOrWhiteSpace(door.Scene))
        {
            LoadScene(door.Scene.Trim(), door.Fade);
            return;
        }

        if (string.IsNullOrWhiteSpace(door.Target) || Find(door.Target.Trim()) is not { } target)
            return;
        _levels.ArrivedByDoor.Add(user);

        // Another player's trip through a door doesn't fade this screen.
        if (Net.IsOnline && !Net.IsMine(user))
        {
            PlaceAt(user, Flat(target.Transform.WorldPosition));
            return;
        }

        FadeThrough(door.Fade, () =>
        {
            if (user.Scene == Scene && target.Scene == Scene)
                PlaceAt(user, Flat(target.Transform.WorldPosition));
        });
    }

    // A pause menu over a conversation holds it where it is: Interact is for the menu.
    private void RunDialogues()
    {
        if (PausedThisFrame)
            return;
        if (_levels.Talking is { } talking)
        {
            if (talking.Entity?.Scene != Scene)
                EndDialogue(finished: false);
            else if (!_levels.TalkJustStarted && !RunChoices(talking) && Input.WasPressed("Interact"))
                AdvanceDialogue(talking);
            _levels.TalkJustStarted = false;
            return;
        }

        var dialogues = Showing<Dialogue>();
        if (dialogues.Count == 0)
        {
            if (Time.Delta > 0)
                _levels.DialoguesTouched.Clear();
            return;
        }

        var touched = new HashSet<Dialogue>();
        foreach (var dialogue in dialogues)
        {
            // Boxes are hidden until there's something to say.
            if (_levels.DialoguesSeen.Add(dialogue) && BoxOf(dialogue) is { } box)
                box.IsEnabled = false;

            var available = !(dialogue.Once && dialogue.IsDone) && dialogue.Lines.Count > 0;
            // In a network game each player reads for themselves, on their own machine.
            var touching = available && TouchersOf(dialogue.Entity!).Any(t => dialogue.AcceptsLayer(t.Layer) && Net.IsMine(t.By));
            if (touching)
                touched.Add(dialogue);
            ShowPrompt(dialogue.Entity!, dialogue.Prompt, touching && dialogue.Start == DialogueStart.Interact);

            if (!touching || _levels.Talking is not null)
                continue;
            var starts = dialogue.Start == DialogueStart.Touch
                ? !_levels.DialoguesTouched.Contains(dialogue)
                : Input.WasPressed("Interact");
            if (starts)
                StartDialogue(dialogue);
        }

        // Touches only come from physics, which a paused game doesn't run: keep the last ones until it runs again.
        if (Time.Delta > 0 || _levels.Talking is not null)
        {
            _levels.DialoguesTouched.Clear();
            _levels.DialoguesTouched.UnionWith(touched);
        }
    }

    /// <summary>
    /// Starts <paramref name="dialogue"/> straight away, as if the player had pressed Interact beside it: from a script, a
    /// cutscene, a boss appearing. Does nothing while another dialogue is being said, or if it has no lines; one whose
    /// flags let none of its lines be said ends straight away.
    /// </summary>
    public void StartDialogue(Dialogue dialogue)
    {
        if (_levels.Talking is not null || dialogue.Lines.Count == 0 || dialogue.Entity?.Scene != Scene)
            return;
        dialogue.CheckParts();
        _levels.Talking = dialogue;
        _levels.TalkJustStarted = true;
        dialogue.IsTalking = true;
        dialogue.Line = 0;
        ShowPrompt(dialogue.Entity!, dialogue.Prompt, false);
        if (BoxOf(dialogue) is { } box)
            box.IsEnabled = true;
        if (dialogue.PauseGame)
        {
            _levels.ScaleBeforeTalk = Time.Scale;
            Time.Scale = 0;
        }

        // "# Name" on the first row just names the start.
        SayFrom(dialogue, dialogue.Row(0).Kind == Dialogue.RowKind.Part ? 1 : 0);
    }

    /// <summary>
    /// Moves a dialogue on, as Interact does: finishes typing the line if it's still typing, else picks the choice the
    /// player is on (see <see cref="Dialogue.Picked"/>), or shows the next line (or ends it after the last).
    /// </summary>
    public void AdvanceDialogue(Dialogue dialogue)
    {
        if (_levels.Talking != dialogue)
            return;
        if (LineText(dialogue)?.Entity?.GetComponent<Typewriter>() is { IsDone: false } typewriter)
        {
            typewriter.Skip();
            return;
        }

        if (dialogue.Choices.Count > 0)
            ChooseDialogue(dialogue, dialogue.Picked);
        else
            SayFrom(dialogue, _levels.AfterChoices);
    }

    /// <summary>
    /// Picks choice <paramref name="choice"/> (from 0) of those <paramref name="dialogue"/> offers now, as the player
    /// would: does its tags and goes where it leads. Does nothing when it isn't being said or offers no such choice.
    /// </summary>
    public void ChooseDialogue(Dialogue dialogue, int choice)
    {
        if (_levels.Talking != dialogue || choice < 0 || choice >= _levels.ChoiceRows.Count)
            return;
        var picked = dialogue.Row(_levels.ChoiceRows[choice]);
        var after = _levels.AfterChoices;
        dialogue.Picked = choice;
        DoTags(dialogue, picked.Tags);
        dialogue.RaiseChosen(choice);
        if (_levels.Talking != dialogue)
            return;

        if (string.IsNullOrEmpty(picked.To))
            SayFrom(dialogue, after);
        else if (dialogue.RowAfterPart(picked.To) is { } next)
            SayFrom(dialogue, next);
        else
            EndDialogue(finished: true);
    }

    /// <summary>
    /// True if flag <paramref name="name"/> is set: by a dialogue's "[set Name]", by <see cref="SetFlag"/>, or by anything
    /// else that saved true under that name in <see cref="SaveData"/> (a toggle's Save As). Capitals don't matter.
    /// </summary>
    public bool HasFlag(string name) => FlagKey(name) is { } key && SaveData.GetBool(key);

    /// <summary>
    /// Sets flag <paramref name="name"/> (or clears it, with <paramref name="on"/> false), as a dialogue's "[set Name]"
    /// does. Flags are kept in <see cref="SaveData"/>, so they're still set the next time the game runs.
    /// </summary>
    public void SetFlag(string name, bool on = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = FlagKey(name) ?? name.Trim();
        if (HasFlag(key) == on)
            return;
        if (on)
            SaveData.Set(key, true);
        else
            SaveData.Delete(key);
        FlagChanged?.Invoke(key, on);
    }

    /// <summary>Raised when a flag is set or cleared, with its name and whether it's set now.</summary>
    public event Action<string, bool>? FlagChanged;

    /// <summary>
    /// True if <paramref name="needs"/> holds, as a switch's, door's, spawner's or panel's Needs Flag says: empty always
    /// does, "Name" while that flag is set, and "not Name" (or "!Name") while it isn't. Asking every frame makes no garbage.
    /// </summary>
    public bool FlagHolds(string? needs)
    {
        var name = needs.AsSpan().Trim();
        if (name.Length == 0)
            return true;
        var not = false;
        if (name[0] == '!')
        {
            name = name[1..].TrimStart();
            not = true;
        }
        else if (name.Length > 4 && name.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            name = name[4..].TrimStart();
            not = true;
        }

        return SetFlags().Contains(name) != not;
    }

    // The flags set now, by name whatever their capitals, made again only when the save data changes.
    private HashSet<string>.AlternateLookup<ReadOnlySpan<char>> SetFlags()
    {
        if (_setFlags is null || _setFlagsOf != SaveData || _setFlagsVersion != SaveData.Version)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in SaveData.Keys)
            {
                if (SaveData.GetBool(key))
                    set.Add(key);
            }

            (_setFlags, _setFlagsOf, _setFlagsVersion) = (set, SaveData, SaveData.Version);
        }

        return _setFlags.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    private HashSet<string>? _setFlags;
    private SaveData? _setFlagsOf;
    private long _setFlagsVersion;

    // The name a flag is saved under, matched without minding capitals, or null when it's not saved.
    private string? FlagKey(string name)
    {
        name = name.Trim();
        return SaveData.Has(name) ? name : SaveData.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
    }

    // What a row's tags do once it's said, its choice picked or its go-to followed: set and clear flags, send messages.
    private void DoTags(Dialogue dialogue, IReadOnlyList<Dialogue.Tag> tags)
    {
        foreach (var tag in tags)
        {
            switch (tag.Kind)
            {
                case Dialogue.TagKind.Set:
                    SetFlag(tag.Name);
                    break;
                case Dialogue.TagKind.Clear:
                    SetFlag(tag.Name, on: false);
                    break;
                case Dialogue.TagKind.Send:
                    Send(tag.Name, dialogue.Entity, from: dialogue.Entity);
                    break;
            }
        }
    }

    // Shows the first line from row on whose flags let it be said, going where go-tos say and doing rows of tags on the
    // way; the end of the lines or the start of another part ends it.
    private void SayFrom(Dialogue dialogue, int row)
    {
        // Rows of tags can change what's said on a second time round a loop, so allow a few.
        for (var steps = 0; steps <= (dialogue.Lines.Count + 1) * 4; steps++)
        {
            if (_levels.Talking != dialogue)
                return;
            if (row >= dialogue.Lines.Count)
            {
                EndDialogue(finished: true);
                return;
            }

            var line = dialogue.Row(row);
            switch (line.Kind)
            {
                case Dialogue.RowKind.Said when line.Holds(HasFlag):
                    ShowLine(dialogue, row);
                    return;
                case Dialogue.RowKind.Said:
                    row++;
                    continue;
                case Dialogue.RowKind.Choice:
                    // Reached straight after lines that weren't said, the choices go with them; choices on their own are
                    // passed by when none of them can be picked.
                    var after = AfterChoices(dialogue, row);
                    if (!(row > 0 && dialogue.Row(row - 1).Kind == Dialogue.RowKind.Said) && OfferedChoices(dialogue, row, after).Any())
                    {
                        ShowLine(dialogue, row);
                        return;
                    }

                    row = after;
                    continue;
                case Dialogue.RowKind.Tags:
                    if (line.Holds(HasFlag))
                        DoTags(dialogue, line.Tags);
                    row++;
                    continue;
                case Dialogue.RowKind.GoTo when !line.Holds(HasFlag):
                    row++;
                    continue;
                case Dialogue.RowKind.GoTo:
                    DoTags(dialogue, line.Tags);
                    if (_levels.Talking != dialogue)
                        return;
                    if (string.IsNullOrEmpty(line.To) || dialogue.RowAfterPart(line.To) is not { } next)
                    {
                        EndDialogue(finished: true);
                        return;
                    }

                    row = next;
                    continue;
                default:
                    EndDialogue(finished: true);
                    return;
            }
        }

        throw new InvalidOperationException($"The dialogue on \"{dialogue.Entity?.Name}\" goes round in circles without saying anything.");
    }

    // The row after the choices that start at row (row itself when there are none).
    private static int AfterChoices(Dialogue dialogue, int row)
    {
        while (row < dialogue.Lines.Count && dialogue.Row(row).Kind == Dialogue.RowKind.Choice)
            row++;
        return row;
    }

    // Where the choices offered under the line said at row start (row itself when there are none). They're under the last
    // line said of the lines just above them, so "[if Met] Hi again." and "[if not Met] Hello." share the choices after them.
    private int ChoicesUnder(Dialogue dialogue, int row)
    {
        var next = row + 1;
        for (; next < dialogue.Lines.Count && dialogue.Row(next) is { Kind: Dialogue.RowKind.Said } later; next++)
        {
            if (later.Holds(HasFlag))
                return row;
        }

        return next < dialogue.Lines.Count && dialogue.Row(next).Kind == Dialogue.RowKind.Choice ? next : row;
    }

    // The rows of the choices from start up to after whose flags let them be offered.
    private IEnumerable<int> OfferedChoices(Dialogue dialogue, int start, int after) =>
        Enumerable.Range(start, after - start).Where(r => dialogue.Row(r).Holds(HasFlag));

    // A line, with the choices under it that can be picked; choices with no line before them are shown on their own.
    // Its tags are done first, so a flag it sets can offer a choice under it.
    private void ShowLine(Dialogue dialogue, int row)
    {
        var line = dialogue.Row(row);
        dialogue.Line = row;
        var choices = row;
        if (line.Kind == Dialogue.RowKind.Said)
        {
            DoTags(dialogue, line.Tags);
            if (_levels.Talking != dialogue)
                return;
            choices = ChoicesUnder(dialogue, row);
        }

        var hasChoices = choices != row || line.Kind == Dialogue.RowKind.Choice;
        _levels.AfterChoices = hasChoices ? AfterChoices(dialogue, choices) : row + 1;

        // Said by the speaker it starts with ("Guide: ..."), or else the one on the dialogue's own entity.
        var (who, said) = line.Kind == Dialogue.RowKind.Said && !line.AsIs ? Dialogue.SplitSpeaker(line.Text) : (null, line.Text);
        var named = who is null ? null : SpeakerNamed(who);
        dialogue.Speaker = line.Kind == Dialogue.RowKind.Choice ? null : named ?? dialogue.Entity!.GetComponent<Speaker>();
        dialogue.Said = line.Kind == Dialogue.RowKind.Choice ? null : named is null ? line.Text : said;
        ShowSpeaker(dialogue);

        _levels.ChoiceRows.Clear();
        if (hasChoices)
            _levels.ChoiceRows.AddRange(OfferedChoices(dialogue, choices, _levels.AfterChoices));
        dialogue.Choices = _levels.ChoiceRows.Select(r => dialogue.Row(r).Text).ToList();
        dialogue.Picked = 0;
        _levels.LastMouseOnChoices = null;
        _levels.LastChoiceStick = ChoiceStick;

        if (LineText(dialogue) is { } text)
        {
            _levels.LastMouseOnChoices = MouseOn(text);
            text.Value = LineWithChoices(dialogue, text.Align);
            text.Entity!.GetComponent<Typewriter>()?.Restart();
        }

        dialogue.RaiseLineShown();
    }

    // The speaker called name (or whose entity is, when it has no name of its own), anywhere in the scene, shown or not.
    private Speaker? SpeakerNamed(string name) =>
        Scene.All<Speaker>()
            .FirstOrDefault(s => string.Equals(s.ShownName, name, StringComparison.OrdinalIgnoreCase));

    // The box's name tag and portrait show who's talking, and hide when nobody is (or they have no portrait).
    private void ShowSpeaker(Dialogue dialogue)
    {
        if (BoxOf(dialogue) is not { } box || box.GetComponent<DialogueBox>() is not { } layout)
            return;
        var speaker = dialogue.Speaker;
        if (Inside(box, layout.NameTag) is { } tag)
        {
            tag.IsEnabled = speaker is not null;
            if (speaker is not null && tag.SelfAndDescendants().SelectMany(e => e.GetComponents<Text>()).FirstOrDefault() is { } name)
                name.Value = speaker.ShownName;
        }

        if (Inside(box, layout.Portrait) is { } portrait)
        {
            var image = speaker?.PortraitImage is { Length: > 0 } path ? path.Trim() : null;
            portrait.IsEnabled = image is not null;
            if (image is not null && portrait.SelfAndDescendants().SelectMany(e => e.GetComponents<Sprite>()).FirstOrDefault() is { } sprite)
                sprite.Image = image;
        }
    }

    // The entity called name inside box (or the box itself).
    private static Entity? Inside(Entity box, string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : box.SelfAndDescendants().FirstOrDefault(e => e.Name == name.Trim());

    // The line showing and its choices under it, the one picked with arrows by it that keep the rows lined up.
    private static string LineWithChoices(Dialogue dialogue, TextAlign align)
    {
        var rows = dialogue.Choices.Select((choice, i) => (i == dialogue.Picked, align) switch
        {
            (true, TextAlign.Left) => "> " + choice,
            (true, TextAlign.Right) => choice + " <",
            (true, _) => "> " + choice + " <",
            (false, TextAlign.Left) => "   " + choice,
            (false, TextAlign.Right) => choice + "   ",
            _ => choice,
        });
        return string.Join("\n", dialogue.Said is { } said ? rows.Prepend(said) : rows);
    }

    // Choosing with the arrow keys, D-pad or stick, the number keys and the mouse, once the line has typed out. Interact
    // picks too, through AdvanceDialogue. True if one was picked here.
    private bool RunChoices(Dialogue dialogue)
    {
        if (dialogue.Choices.Count == 0 || LineText(dialogue) is not { } text ||
            text.Entity!.GetComponent<Typewriter>() is { IsDone: false })
            return false;

        var count = dialogue.Choices.Count;
        var stick = ChoiceStick;
        var up = Input.WasPressed(Key.Up) || Input.WasPressed(Key.W) || Input.WasPressed(GamepadButton.DPadUp) ||
                 (stick > 0 && _levels.LastChoiceStick <= 0);
        var down = Input.WasPressed(Key.Down) || Input.WasPressed(Key.S) || Input.WasPressed(GamepadButton.DPadDown) ||
                   (stick < 0 && _levels.LastChoiceStick >= 0);
        _levels.LastChoiceStick = stick;
        var picked = dialogue.Picked;
        if (up)
            picked = (picked + count - 1) % count;
        if (down)
            picked = (picked + 1) % count;

        // The mouse picks what it moves onto, so a mouse left where it was doesn't fight the keys.
        var hovered = ChoiceUnderMouse(dialogue, text);
        var onText = MouseOn(text);
        var mouseMoved = _levels.LastMouseOnChoices is { } last && Vector2.DistanceSquared(last, onText) > 1e-8f;
        _levels.LastMouseOnChoices = onText;
        if (hovered is { } under && mouseMoved)
            picked = under;

        if (picked != dialogue.Picked)
        {
            dialogue.Picked = picked;
            text.Entity.GetComponent<Typewriter>()?.ShowNextAtOnce();
            text.Value = LineWithChoices(dialogue, text.Align);
        }

        for (var i = 0; i < Math.Min(count, 9); i++)
        {
            if (Input.WasPressed(Key.D1 + i) || Input.WasPressed(Key.NumPad1 + i))
            {
                ChooseDialogue(dialogue, i);
                return true;
            }
        }

        if (hovered is { } clicked && Input.WasPressed(MouseButton.Left))
        {
            ChooseDialogue(dialogue, clicked);
            return true;
        }

        return false;
    }

    // Where the mouse is from the text: the box moves with the camera, so the mouse only counts as moving over it.
    private Vector2 MouseOn(Text text) => Input.MouseWorld - Flat(text.Entity!.Transform.WorldPosition);

    // The stick pushed up (1), down (-1) or neither: each push moves one choice.
    private int ChoiceStick => Input.LeftStick.Y > NavigateStick ? 1 : Input.LeftStick.Y < -NavigateStick ? -1 : 0;

    // The choice whose row the mouse is over, if any: each row is a line of the text's size, the block centered on its
    // entity, and as wide as the box's sprite when it has one.
    private int? ChoiceUnderMouse(Dialogue dialogue, Text text)
    {
        if (!Input.IsMouseInView)
            return null;
        var world = text.Entity!.Transform.WorldMatrix;
        var height = text.Size * new Vector2(world.M21, world.M22).Length();
        if (!(height > 0))
            return null;
        if (BoxOf(dialogue) is { } box && box.GetComponent<Sprite>() is { } sprite)
        {
            var boxWorld = box.Transform.WorldMatrix;
            var halfWidth = MathF.Abs(sprite.Size.X) * new Vector2(boxWorld.M11, boxWorld.M12).Length() / 2;
            if (MathF.Abs(Input.MouseWorld.X - boxWorld.M41) > halfWidth)
                return null;
        }

        var lines = (text.Value ?? "").Count(c => c == '\n') + 1;
        var top = world.M42 + height * lines / 2;
        var line = (int)MathF.Floor((top - Input.MouseWorld.Y) / height);
        var choice = line - (lines - dialogue.Choices.Count);
        return line >= 0 && line < lines && choice >= 0 ? choice : null;
    }

    internal void EndDialogue(bool finished)
    {
        if (_levels.Talking is not { } dialogue)
            return;
        _levels.Talking = null;
        dialogue.IsTalking = false;
        dialogue.Choices = [];
        (dialogue.Speaker, dialogue.Said) = (null, null);
        _levels.ChoiceRows.Clear();
        if (dialogue.Entity?.Scene == Scene && BoxOf(dialogue) is { } box)
            box.IsEnabled = false;
        if (_levels.ScaleBeforeTalk is { } scale)
            Time.Scale = scale;
        _levels.ScaleBeforeTalk = null;

        if (!finished)
            return;
        dialogue.IsDone = true;
        if (!string.IsNullOrWhiteSpace(dialogue.Message))
            Send(dialogue.Message.Trim(), dialogue.Entity, from: dialogue.Entity);
        dialogue.RaiseFinished();
    }

    private Entity? BoxOf(Dialogue dialogue) => string.IsNullOrWhiteSpace(dialogue.Box) ? null : Find(dialogue.Box.Trim());

    /// <summary>True while the line <paramref name="dialogue"/> shows is still typing out (its choices wait for it).</summary>
    internal bool IsTyping(Dialogue dialogue) => LineText(dialogue)?.Entity?.GetComponent<Typewriter>() is { IsDone: false };

    // The first text in the box that isn't its name tag's.
    private Text? LineText(Dialogue dialogue)
    {
        if (BoxOf(dialogue) is not { } box)
            return null;
        var tag = Inside(box, box.GetComponent<DialogueBox>()?.NameTag);
        var tagged = tag?.SelfAndDescendants().ToHashSet() ?? [];
        return box.SelfAndDescendants().Where(e => !tagged.Contains(e)).SelectMany(e => e.GetComponents<Text>()).FirstOrDefault();
    }

    // A prompt is looked for inside its owner first, so every copy of a door prefab shows its own "Press E".
    private void ShowPrompt(Entity owner, string? name, bool show)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        name = name.Trim();
        var prompt = owner.SelfAndDescendants().FirstOrDefault(e => e.Name == name) ?? Find(name);
        if (prompt is not null && prompt.IsEnabled != show)
            prompt.IsEnabled = show;
    }

    internal static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}

/// <summary>Doors and conversations: who just came through a door, the dialogues seen and touched, and the one being said.</summary>
internal sealed class LevelSystem : GameSystem
{
    public override string Name => "Levels";

    // Teleported by a door and still touching one: it can't use a door again until it has stepped off them all.
    public HashSet<Entity> ArrivedByDoor { get; } = [];

    public HashSet<Dialogue> DialoguesSeen { get; } = [];

    public HashSet<Dialogue> DialoguesTouched { get; } = [];

    public Dialogue? Talking { get; set; }

    public bool TalkJustStarted { get; set; }

    public float? ScaleBeforeTalk { get; set; }

    // The rows of the choices offered now, and where the mouse and stick were last frame (to notice them move).
    public List<int> ChoiceRows { get; } = [];

    public Vector2? LastMouseOnChoices { get; set; }

    public int LastChoiceStick { get; set; }

    // The row after the line showing and the choices under it: where the talk goes on.
    public int AfterChoices { get; set; }

    // Leaving a scene leaves its doors and conversations behind.
    protected override void SceneChanged()
    {
        Game.EndDialogue(finished: false);
        ArrivedByDoor.Clear();
        DialoguesSeen.Clear();
        DialoguesTouched.Clear();
    }
}
