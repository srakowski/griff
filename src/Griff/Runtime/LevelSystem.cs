using System.Numerics;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Levels with no code: movers, patrols, chasers, shooters, checkpoints and respawning, doors and dialogue, and the
/// flags dialogue sets. Keeps who just came through a door, the dialogues seen and touched, and the one being said.
/// </summary>
internal sealed class LevelSystem : GameSystem
{
    public override string Name => "Levels";

    // Teleported by a door and still touching one: it can't use a door again until it has stepped off them all.
    private readonly HashSet<Entity> _arrivedByDoor = [];

    private readonly HashSet<Dialogue> _dialoguesSeen = [];

    private readonly HashSet<Dialogue> _dialoguesTouched = [];

    private Dialogue? _talking;

    public Dialogue? Talking => _talking;

    private bool _talkJustStarted;

    private float? _scaleBeforeTalk;

    // The rows of the choices offered now, and where the mouse and stick were last frame (to notice them move).
    private readonly List<int> _choiceRows = [];

    private Vector2? _lastMouseOnChoices;

    private int _lastChoiceStick;

    // The row after the line showing and the choices under it: where the talk goes on.
    private int _afterChoices;

    // Those that came in through a door into the scene just started stand on it without going straight back.
    internal void ArrivedThroughDoors(IEnumerable<Entity> arrived) => _arrivedByDoor.UnionWith(arrived);

    // Leaving a scene leaves its doors and conversations behind.
    protected override void SceneChanged()
    {
        EndDialogue(finished: false);
        _arrivedByDoor.Clear();
        _dialoguesSeen.Clear();
        _dialoguesTouched.Clear();
    }

    // How often a chaser finds its way around walls again, in seconds of game time.
    private const float ChasePathEvery = 0.25f;

    // Within this of a waypoint counts as there.
    private const float WaypointReach = 0.15f;

    // Movers go before physics, so whatever stands on them is carried along this frame.
    internal void RunMovers()
    {
        foreach (var mover in Game.Active<Mover>())
            mover.Advance(Game.Time.Delta, mover.Beats > 0 ? Game.ClockFor(mover.Clock) : null);
    }

    // Chasers first, so a patrol knows to stand aside while its chase has the target in sight.
    internal void RunEnemies()
    {
        if (Game.Time.Delta <= 0)
            return;
        // Waiting for their turn, they stand where they are.
        foreach (var chase in Game.Active<Chase>())
        {
            if (Game.Waiting(chase.Entity))
                Game.TurnSystem.HoldStill(chase.Entity!);
            else
                RunChase(chase);
        }

        foreach (var patrol in Game.Active<Patrol>())
        {
            if (Game.Waiting(patrol.Entity))
                Game.TurnSystem.HoldStill(patrol.Entity!);
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
    internal void CountPatrolWalks()
    {
        foreach (var patrol in Game.Active<Patrol>())
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
        var drop = Game.Physics.IsOnSlope(Game.Scene, collider) ? bounds.HalfSize.X * 2 + 0.1f : 0;
        return Game.Physics.Raycast(Game.Scene, origin, -Vector2.UnitY, rise + 0.25f + drop, ignore: entity) is not null;
    }

    private void RunChase(Chase chase)
    {
        var entity = chase.Entity!;
        if (entity.GetComponent<Body>() is not { } body)
            return;

        // In a network game there's a "Player" for everyone: go after whoever is nearest.
        if (chase.TargetEntity?.Scene != Game.Scene || (Game.Net.IsOnline && !string.IsNullOrWhiteSpace(chase.Target)))
            chase.TargetEntity = string.IsNullOrWhiteSpace(chase.Target) ? null : Game.Nearest(chase.Target.Trim(), Game.Flat(entity.Transform.WorldPosition));
        var target = chase.TargetEntity;

        var here = Game.Flat(entity.Transform.WorldPosition);
        var wasChasing = chase.IsChasing;
        var chasing = false;
        if (target is not null && target.IsActive && Health.Of(target) is not { IsDead: true })
        {
            var distance = Vector2.Distance(here, Game.Flat(target.Transform.WorldPosition));
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

        var falls = body.GravityScale != 0 && Game.Physics.Gravity != Vector2.Zero;
        if (!chasing)
        {
            // Stand still, unless a patrol on the same entity has walking to do.
            if (wasChasing || entity.GetComponent<Patrol>() is null)
                body.Velocity = falls ? body.Velocity with { X = 0 } : Vector2.Zero;
            return;
        }

        var toTarget = Game.Flat(target!.Transform.WorldPosition) - here;
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
        var there = Game.Flat(target.Transform.WorldPosition);
        if (string.IsNullOrWhiteSpace(chase.Around) || Game.Find(chase.Around.Trim())?.GetComponent<Tilemap>() is not { } map)
            return there;

        chase.PathAge += Game.Time.Delta;
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
    internal bool CanSee(Entity looker, Entity target)
    {
        var from = Game.Flat(looker.Transform.WorldPosition);
        var toTarget = Game.Flat(target.Transform.WorldPosition) - from;
        var hit = Game.Physics.Raycast(Game.Scene, from, toTarget, toTarget.Length(), ignore: looker);
        return hit is null || hit.Value.Entity.IsSelfOrDescendantOf(target);
    }

    public static void Face(Entity entity, int direction)
    {
        if (entity.GetComponent<Sprite>() is { } sprite)
            sprite.FlipX = direction < 0;
    }

    // Nothing fires while game time stands still: a pause, a hit stop, a conversation.
    internal void RunShooters()
    {
        if (Game.Time.Delta <= 0)
            return;
        foreach (var shooter in Game.Active<Shooter>())
        {
            if (!shooter.Running)
                continue;
            var entity = shooter.Entity!;
            var here = Game.Flat(entity.Transform.WorldPosition);
            var aim = shooter.Direction;
            bool wantsToFire;

            if (!string.IsNullOrWhiteSpace(shooter.Action))
            {
                var keys = Game.InputOf(entity);
                wantsToFire = keys.IsDown(shooter.Action);
                if (keys.AimFrom(here) is var aimed && aimed != Vector2.Zero)
                    aim = aimed;
            }
            else if (!string.IsNullOrWhiteSpace(shooter.Target))
            {
                var target = Game.Nearest(shooter.Target.Trim(), here);
                wantsToFire = target is not null && target.IsActive && Health.Of(target) is not { IsDead: true };
                if (target is not null)
                {
                    aim = Game.Flat(target.Transform.WorldPosition) - here;
                    wantsToFire &= shooter.Range <= 0 || aim.Length() <= shooter.Range;
                    wantsToFire &= !shooter.NeedsSight || CanSee(entity, target);
                }
            }
            else
            {
                wantsToFire = true;
            }

            // Not while it waits for its turn.
            wantsToFire &= !Game.Waiting(entity);

            var onBeat = shooter.Beats > 0 && Game.OnBeat(shooter.Clock, shooter.Beats, shooter.BeatOffset);
            if (!shooter.Ready(Game.Time.Delta, wantsToFire, onBeat) || string.IsNullOrWhiteSpace(shooter.Prefab))
                continue;

            Volley(shooter, here, aim);
            Game.UseAction(entity);
        }
    }

    /// <summary>A shooter's Fire action: one volley now, at its target if it has one in the scene, otherwise its way.</summary>
    internal void FireVolley(Shooter shooter)
    {
        if (shooter.Entity is not { } entity || entity.Scene != Game.Scene || string.IsNullOrWhiteSpace(shooter.Prefab))
            return;
        var here = Game.Flat(entity.Transform.WorldPosition);
        var aim = shooter.Direction;
        if (!string.IsNullOrWhiteSpace(shooter.Target) && Game.Nearest(shooter.Target.Trim(), here) is { } target)
            aim = Game.Flat(target.Transform.WorldPosition) - here;
        Volley(shooter, here, aim);
    }

    private void Volley(Shooter shooter, Vector2 here, Vector2 aim)
    {
        var entity = shooter.Entity!;
        aim = aim.LengthSquared() > 1e-8f ? Vector2.Normalize(aim) : Vector2.UnitX;
        foreach (var direction in shooter.VolleyAround(aim))
        {
            var shot = Game.Spawn(shooter.Prefab!.Trim(), here + shooter.Offset);
            Game.Claim(shot, entity);
            if (shot.GetComponent<Body>() is { } body)
                body.Velocity = direction * shooter.Speed;
            shot.Transform.Rotation = shot.Transform.Rotation with { Z = MathF.Atan2(direction.Y, direction.X) * 180 / MathF.PI };
            shooter.RaiseFired(shot);
        }

        if (!string.IsNullOrWhiteSpace(shooter.Sound))
            Game.PlayShared(shooter.Sound);
    }

    public IEnumerable<(Entity By, string? Layer)> TouchersOf(Entity entity) =>
        Game.Touches.Where(t => t.Item1 == entity && t.Item2.Scene == Game.Scene)
            .Select(t => (By: Game.BodyOwner(t.Item2), Layer: Game.LayerOf(t.Item2)))
            .Where(t => t.By is not null && !entity.IsSelfOrDescendantOf(t.By))
            .Select(t => (t.By!, t.Layer))
            .Distinct();

    internal void RunCheckpoints()
    {
        var all = Game.RunHere<Checkpoint>();
        foreach (var checkpoint in all)
        {
            if (!checkpoint.Entity!.IsActive || checkpoint.Active || checkpoint.Entity.IsPaused)
                continue;
            var (by, _) = TouchersOf(checkpoint.Entity!).FirstOrDefault(t => checkpoint.AcceptsLayer(t.Layer));
            if (by is null)
                continue;

            foreach (var other in all)
                other.Active = false;
            checkpoint.Active = true;
            Game.Flash(checkpoint.Entity!);
            if (!string.IsNullOrWhiteSpace(checkpoint.Sound))
                Game.PlayShared(checkpoint.Sound);
            checkpoint.RaiseReached(by);
            if (!string.IsNullOrWhiteSpace(checkpoint.Message))
                Game.Send(checkpoint.Message.Trim(), checkpoint.Entity, from: checkpoint.Entity);
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
    internal void RunRespawns()
    {
        foreach (var health in Game.RunHere<Health>())
        {
            if (!health.Respawn || !health.IsDead || health.Entity!.IsPaused)
                continue;
            var entity = health.Entity!;
            if (float.IsNaN(health.RespawnLeft))
            {
                health.RespawnLeft = Math.Max(0, health.RespawnDelay);
                entity.IsEnabled = false;
            }

            health.RespawnLeft -= Game.Time.Delta;
            if (health.RespawnLeft > 0)
                continue;

            var checkpoint = Game.Scene.ActiveNow<Checkpoint>().FirstOrDefault(c => c.Active);
            var position = checkpoint is not null ? Game.Flat(checkpoint.Entity!.Transform.WorldPosition) : health.Home ?? Game.Flat(entity.Transform.WorldPosition);
            entity.IsEnabled = true;
            PlaceAt(entity, position);
            health.Revive();
        }
    }

    // Moves an entity to a world position at once, as if it had always been there: no velocity, cameras following it snap.
    internal void PlaceAt(Entity entity, Vector2 world)
    {
        var target = new Vector3(world, entity.Transform.WorldPosition.Z);
        if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            target = Vector3.Transform(target, inverse);
        entity.Transform.Teleport(new Vector3(target.X, target.Y, entity.Transform.Position.Z));
        if (entity.GetComponent<Body>() is { } body)
            (body.Velocity, body.Spin) = (Vector2.Zero, 0);
        foreach (var follow in Game.Scene.AllNow<Follow>().Where(f => f.TargetEntity == entity))
            follow.Snap();
    }

    // Nobody goes through a door while a panel pauses the game.
    internal void RunDoors()
    {
        if (Game.PausedThisFrame)
            return;
        var doors = Game.Active<Door>();
        if (doors.Count == 0)
        {
            if (Game.Time.Delta > 0)
                _arrivedByDoor.Clear();
            return;
        }

        var touchingAnyDoor = new HashSet<Entity>();
        foreach (var door in doors)
        {
            // Nothing paused goes through it (see Entity.IsPaused).
            var users = TouchersOf(door.Entity!).Where(t => door.AcceptsLayer(t.Layer) && !t.By.IsPaused).Select(t => t.By).ToList();
            touchingAnyDoor.UnionWith(users);
            var ready = door.Locked || !FlagHolds(door.NeedsFlag) ? [] : users.Where(u => !_arrivedByDoor.Contains(u) && SwitchSystem.CanUnlock(door, u)).ToList();
            ShowPrompt(door.Entity!, door.Prompt, ready.Count > 0);

            // One trip at a time: not while a scene change or another door's fade is under way.
            if (ready.Count == 0 || Game.SceneSystem.IsFadingOrChangingScene)
                continue;
            var user = door.NeedsInteract ? ready.FirstOrDefault(u => Game.HasFocus(u) && Game.InputOf(u).WasPressed("Interact")) : ready[0];
            if (user is null)
                continue;
            UseDoor(door, user);
        }

        // Physics only reports touches while game time runs; don't forget arrivals during a pause.
        if (Game.Time.Delta > 0)
            _arrivedByDoor.IntersectWith(touchingAnyDoor);
    }

    private void UseDoor(Door door, Entity user)
    {
        SwitchSystem.UseKey(door, user);
        if (!string.IsNullOrWhiteSpace(door.Sound))
            Game.PlayShared(door.Sound);
        door.RaiseUsed(user);

        if (!string.IsNullOrWhiteSpace(door.Scene))
        {
            Game.LoadScene(door.Scene.Trim(), door.Fade, null, door.ArriveAt, traveller: user, fresh: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(door.Target) || Game.Find(door.Target.Trim()) is not { } target)
            return;
        _arrivedByDoor.Add(user);

        // Another player's trip through a door doesn't fade this screen.
        if (Game.Net.IsOnline && !Game.Net.IsMine(user))
        {
            PlaceAt(user, Game.Flat(target.Transform.WorldPosition));
            return;
        }

        Game.FadeThrough(door.Fade, () =>
        {
            if (user.Scene == Game.Scene && target.Scene == Game.Scene)
                PlaceAt(user, Game.Flat(target.Transform.WorldPosition));
        });
    }

    // A pause menu over a conversation holds it where it is: Interact is for the menu. Then skipping and reading on by
    // itself move a talk on.
    internal void RunDialogues()
    {
        if (Game.PausedThisFrame)
            return;
        RunTalks();
        Game.ReadingSystem.RunReading();
    }

    private void RunTalks()
    {
        if (_talking is { } talking)
        {
            if (talking.Entity?.Scene != Game.Scene)
                EndDialogue(finished: false);
            else if (!_talkJustStarted && !RunChoices(talking) && (Game.Input.WasPressed("Interact") || ClickAdvances(talking)))
            {
                // Reading on by hand stops skipping.
                Game.Skipping = false;
                AdvanceDialogue(talking);
            }

            _talkJustStarted = false;
            return;
        }

        var dialogues = Game.Showing<Dialogue>();
        if (dialogues.Count == 0)
        {
            if (Game.Time.Delta > 0)
                _dialoguesTouched.Clear();
            return;
        }

        var touched = new HashSet<Dialogue>();
        foreach (var dialogue in dialogues)
        {
            // Boxes are hidden until there's something to say. One saved partway through a talk (a rewind, a saved game)
            // goes back into it; one that starts with the scene starts.
            if (_dialoguesSeen.Add(dialogue))
            {
                if (BoxOf(dialogue) is { } box && (_talking is not { } saying || BoxOf(saying) != box))
                    box.IsEnabled = false;
                if (_talking is null && !dialogue.Entity!.IsPaused && dialogue.Lines.Count > 0)
                {
                    if (dialogue.IsTalking)
                        ResumeDialogue(dialogue);
                    else if (dialogue is { StartsWithScene: true, IsDone: false })
                        StartDialogue(dialogue);
                }
            }

            if (dialogue.StartsWithScene)
                continue;

            var available = !(dialogue.Once && dialogue.IsDone) && dialogue.Lines.Count > 0;
            // In a network game each player reads for themselves, on their own machine.
            var touching = available && !dialogue.Entity!.IsPaused &&
                           TouchersOf(dialogue.Entity!).Any(t => dialogue.AcceptsLayer(t.Layer) && Game.Net.IsMine(t.By) && !t.By.IsPaused);
            if (touching)
                touched.Add(dialogue);
            ShowPrompt(dialogue.Entity!, dialogue.Prompt, touching && dialogue.Start == DialogueStart.Interact);

            if (!touching || _talking is not null)
                continue;
            var starts = dialogue.Start == DialogueStart.Touch
                ? !_dialoguesTouched.Contains(dialogue)
                : Game.Input.WasPressed("Interact") && TouchersOf(dialogue.Entity!).Any(t => dialogue.AcceptsLayer(t.Layer) && Game.Net.IsMine(t.By) && Game.HasFocus(t.By));
            if (starts)
                StartDialogue(dialogue);
        }

        // Touches only come from physics, which a paused game doesn't run: keep the last ones until it runs again.
        if (Game.Time.Delta > 0 || _talking is not null)
        {
            _dialoguesTouched.Clear();
            _dialoguesTouched.UnionWith(touched);
        }
    }

    // A click (or a tap) on the game moves a talk on as Interact does, so it can be played with the mouse or on a touch
    // screen: it finishes the line typing out or shows the next. Choices are picked by clicking them, so a click elsewhere
    // while they're offered does nothing, and nor does one on a button.
    private bool ClickAdvances(Dialogue talking) =>
        Game.Input.WasPressed(MouseButton.Left) && Game.Input.IsMouseInView && (talking.Choices.Count == 0 || IsTyping(talking)) && !Game.MouseOnUi;

    public void StartDialogue(Dialogue dialogue) => BeginTalk(dialogue, resume: false);

    // Goes back into a talk saved partway through, at the row it was on, without doing that row's tags again: they were
    // done when it was first said, and what they did (flags, a stage) is saved too.
    private void ResumeDialogue(Dialogue dialogue) => BeginTalk(dialogue, resume: true);

    private void BeginTalk(Dialogue dialogue, bool resume)
    {
        if (_talking is not null || dialogue.Lines.Count == 0 || dialogue.Entity?.Scene != Game.Scene)
            return;
        dialogue.CheckParts();
        _dialoguesSeen.Add(dialogue);
        _talking = dialogue;
        _talkJustStarted = true;
        var at = dialogue.Line;
        dialogue.IsTalking = true;
        dialogue.Line = 0;
        ShowPrompt(dialogue.Entity!, dialogue.Prompt, false);
        if (BoxOf(dialogue) is { } box)
            box.IsEnabled = true;
        if (PauseSystem.PausesAll(dialogue.PauseGame, dialogue.Pauses))
        {
            _scaleBeforeTalk = Game.Time.Scale;
            Game.Time.Scale = 0;
        }
        else
        {
            // Only a group stands still, from now.
            Game.PauseSystem.SyncPauses();
        }

        if (resume && at >= 0 && at < dialogue.Lines.Count && dialogue.Row(at).Kind is Dialogue.RowKind.Said or Dialogue.RowKind.Choice)
        {
            ShowLine(dialogue, at, resume: true);
            return;
        }

        // "# Name" on the first row just names the start.
        SayFrom(dialogue, dialogue.Row(0).Kind == Dialogue.RowKind.Part ? 1 : 0);
    }

    public void AdvanceDialogue(Dialogue dialogue)
    {
        if (_talking != dialogue)
            return;
        if (LineText(dialogue)?.Entity?.GetComponent<Typewriter>() is { IsDone: false } typewriter)
        {
            typewriter.Skip();
            return;
        }

        if (dialogue.Choices.Count > 0)
            ChooseDialogue(dialogue, dialogue.Picked);
        else
            SayFrom(dialogue, _afterChoices);
    }

    public void ChooseDialogue(Dialogue dialogue, int choice)
    {
        if (_talking != dialogue || choice < 0 || choice >= _choiceRows.Count)
            return;
        var picked = dialogue.Row(_choiceRows[choice]);
        var after = _afterChoices;
        dialogue.Picked = choice;
        Game.ReadingSystem.AddToLogs("> " + picked.Text);
        DoTags(dialogue, picked.Tags);
        dialogue.RaiseChosen(choice);
        if (_talking != dialogue)
            return;

        if (string.IsNullOrEmpty(picked.To))
            SayFrom(dialogue, after);
        else if (dialogue.RowAfterPart(picked.To) is { } next)
            SayFrom(dialogue, next);
        else
            EndDialogue(finished: true);
    }

    public bool HasFlag(string name) => FlagKey(name) is { } key && Game.SaveData.GetBool(key);

    public void SetFlag(string name, bool on = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = FlagKey(name) ?? name.Trim();
        if (HasFlag(key) == on)
            return;
        if (on)
        {
            Game.SaveSystem.NoteFlagName(key);
            Game.SaveData.Set(key, true);
        }
        else
        {
            Game.SaveData.Delete(key);
        }
        FlagChanged?.Invoke(key, on);
    }

    public event Action<string, bool>? FlagChanged;

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
        if (_setFlags is null || _setFlagsOf != Game.SaveData || _setFlagsVersion != Game.SaveData.Version)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in Game.SaveData.Keys)
            {
                if (Game.SaveData.GetBool(key))
                    set.Add(key);
            }

            (_setFlags, _setFlagsOf, _setFlagsVersion) = (set, Game.SaveData, Game.SaveData.Version);
        }

        return _setFlags.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    [NotState("worked out again from the save data")]
    private HashSet<string>? _setFlags;
    [NotState("worked out again from the save data")]
    private SaveData? _setFlagsOf;
    [NotState("worked out again from the save data")]
    private long _setFlagsVersion;

    // The name a flag is saved under, matched without minding capitals, or null when it's not saved.
    private string? FlagKey(string name)
    {
        name = name.Trim();
        return Game.SaveData.Has(name) ? name : Game.SaveData.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
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
                    Game.Send(tag.Name, dialogue.Entity, from: dialogue.Entity);
                    break;
                case Dialogue.TagKind.Direction when _dialogueTags.TryGetValue(tag.Word!, out var run):
                    run(dialogue, tag.Name);
                    break;
            }
        }
    }

    // What the modules' own dialogue tags do, by the word they start with.
    [NotState("what the modules' tags do, registered as the game is set up")]
    private readonly Dictionary<string, Action<Dialogue, string>> _dialogueTags = new(StringComparer.OrdinalIgnoreCase);

    internal void OnDialogueTag(string word, Action<Dialogue, string> run) => _dialogueTags[word] = run;

    // Shows the first line from row on whose flags let it be said, going where go-tos say and doing rows of tags on the
    // way; the end of the lines or the start of another part ends it.
    private void SayFrom(Dialogue dialogue, int row)
    {
        // Rows of tags can change what's said on a second time round a loop, so allow a few.
        for (var steps = 0; steps <= (dialogue.Lines.Count + 1) * 4; steps++)
        {
            if (_talking != dialogue)
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
                    var after = RowAfterChoices(dialogue, row);
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
                    if (_talking != dialogue)
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
    private static int RowAfterChoices(Dialogue dialogue, int row)
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
    private void ShowLine(Dialogue dialogue, int row, bool resume = false)
    {
        var line = dialogue.Row(row);
        dialogue.Line = row;
        var choices = row;
        if (line.Kind == Dialogue.RowKind.Said)
        {
            if (!resume)
                DoTags(dialogue, line.Tags);
            if (_talking != dialogue)
                return;
            choices = ChoicesUnder(dialogue, row);
        }

        var hasChoices = choices != row || line.Kind == Dialogue.RowKind.Choice;
        _afterChoices = hasChoices ? RowAfterChoices(dialogue, choices) : row + 1;

        // Said by the speaker it starts with ("Guide: ..."), or else the one on the dialogue's own entity.
        var (who, expression, said) = line.Kind == Dialogue.RowKind.Said && !line.AsIs ? Dialogue.SplitSpeaker(line.Text) : (null, null, line.Text);
        var named = who is null ? null : SpeakerNamed(who);
        dialogue.Speaker = line.Kind == Dialogue.RowKind.Choice ? null : named ?? dialogue.Entity!.GetComponent<Speaker>();
        dialogue.Said = line.Kind == Dialogue.RowKind.Choice ? null : named is null ? line.Text : said;
        dialogue.Expression = named is null || line.Kind == Dialogue.RowKind.Choice ? null : expression;
        ShowSpeaker(dialogue);

        _choiceRows.Clear();
        if (hasChoices)
            _choiceRows.AddRange(OfferedChoices(dialogue, choices, _afterChoices));
        dialogue.Choices = _choiceRows.Select(r => dialogue.Row(r).Text).ToList();
        dialogue.Picked = 0;
        _lastMouseOnChoices = null;
        _lastChoiceStick = ChoiceStick;

        if (LineText(dialogue) is { } text)
        {
            _lastMouseOnChoices = MouseOn(text);
            text.Value = LineWithChoices(dialogue, text);
            text.Entity!.GetComponent<Typewriter>()?.Restart();
        }

        Game.ReadingSystem.NoteLineShown(dialogue, row, line.Kind == Dialogue.RowKind.Said, resume);
        dialogue.RaiseLineShown();
    }

    // The speaker called name (or whose entity is, when it has no name of its own), anywhere in the scene, shown or not.
    private Speaker? SpeakerNamed(string name) =>
        Game.Scene.AllNow<Speaker>()
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
    private string LineWithChoices(Dialogue dialogue, Text text)
    {
        var align = text.Align;
        var rows = dialogue.Choices.Select((choice, i) => (i == dialogue.Picked, align) switch
        {
            (true, TextAlign.Left) => "> " + choice,
            (true, TextAlign.Right) => choice + " <",
            (true, _) => "> " + choice + " <",
            (false, TextAlign.Left) => "   " + choice,
            (false, TextAlign.Right) => choice + "   ",
            _ => choice,
        });
        var wrap = BoxOf(dialogue)?.GetComponent<DialogueBox>()?.WrapWidth ?? 0;
        return string.Join("\n", dialogue.Said is { } said ? rows.Prepend(Wrap(said, wrap, text.Size)) : rows);
    }

    // Breaks text onto more lines between words, so no line is much wider than width (0 or less doesn't).
    internal static string Wrap(string text, float width, float size) => TextWrap.Wrap(text, width, size);

    // Choosing with the arrow keys, D-pad or stick, the number keys and the mouse, once the line has typed out. Interact
    // picks too, through AdvanceDialogue. True if one was picked here.
    private bool RunChoices(Dialogue dialogue)
    {
        if (dialogue.Choices.Count == 0 || LineText(dialogue) is not { } text ||
            text.Entity!.GetComponent<Typewriter>() is { IsDone: false })
            return false;

        var count = dialogue.Choices.Count;
        var stick = ChoiceStick;
        var up = Game.Input.WasPressed(Key.Up) || Game.Input.WasPressed(Key.W) || Game.Input.WasPressed(GamepadButton.DPadUp) ||
                 (stick > 0 && _lastChoiceStick <= 0);
        var down = Game.Input.WasPressed(Key.Down) || Game.Input.WasPressed(Key.S) || Game.Input.WasPressed(GamepadButton.DPadDown) ||
                   (stick < 0 && _lastChoiceStick >= 0);
        _lastChoiceStick = stick;
        var picked = dialogue.Picked;
        if (up)
            picked = (picked + count - 1) % count;
        if (down)
            picked = (picked + 1) % count;

        // The mouse picks what it moves onto, so a mouse left where it was doesn't fight the keys.
        var hovered = ChoiceUnderMouse(dialogue, text);
        var onText = MouseOn(text);
        var mouseMoved = _lastMouseOnChoices is { } last && Vector2.DistanceSquared(last, onText) > 1e-8f;
        _lastMouseOnChoices = onText;
        if (hovered is { } under && mouseMoved)
            picked = under;

        if (picked != dialogue.Picked)
        {
            dialogue.Picked = picked;
            text.Entity.GetComponent<Typewriter>()?.ShowNextAtOnce();
            text.Value = LineWithChoices(dialogue, text);
        }

        for (var i = 0; i < Math.Min(count, 9); i++)
        {
            if (Game.Input.WasPressed(Key.D1 + i) || Game.Input.WasPressed(Key.NumPad1 + i))
            {
                ChooseDialogue(dialogue, i);
                return true;
            }
        }

        if (hovered is { } clicked && Game.Input.WasPressed(MouseButton.Left))
        {
            ChooseDialogue(dialogue, clicked);
            return true;
        }

        return false;
    }

    // Where the mouse is from the text: the box moves with the camera, so the mouse only counts as moving over it.
    private Vector2 MouseOn(Text text) => Game.Input.MouseWorld - Game.Flat(text.Entity!.Transform.WorldPosition);

    // The stick pushed up (1), down (-1) or neither: each push moves one choice.
    private int ChoiceStick => Game.Input.LeftStick.Y > Game.NavigateStick ? 1 : Game.Input.LeftStick.Y < -Game.NavigateStick ? -1 : 0;

    // The choice whose row the mouse is over, if any: each row is a line of the text's size, the block centered on its
    // entity, and as wide as the box's sprite when it has one.
    private int? ChoiceUnderMouse(Dialogue dialogue, Text text)
    {
        if (!Game.Input.IsMouseInView)
            return null;
        var world = text.Entity!.Transform.WorldMatrix;
        var height = text.Size * new Vector2(world.M21, world.M22).Length();
        if (!(height > 0))
            return null;
        if (BoxOf(dialogue) is { } box && box.GetComponent<Sprite>() is { } sprite)
        {
            var boxWorld = box.Transform.WorldMatrix;
            var halfWidth = MathF.Abs(sprite.Size.X) * new Vector2(boxWorld.M11, boxWorld.M12).Length() / 2;
            if (MathF.Abs(Game.Input.MouseWorld.X - boxWorld.M41) > halfWidth)
                return null;
        }

        var lines = (text.Value ?? "").Count(c => c == '\n') + 1;
        var top = world.M42 + height * lines / 2;
        var line = (int)MathF.Floor((top - Game.Input.MouseWorld.Y) / height);
        var choice = line - (lines - dialogue.Choices.Count);
        return line >= 0 && line < lines && choice >= 0 ? choice : null;
    }

    internal void EndDialogue(bool finished)
    {
        if (_talking is not { } dialogue)
            return;
        _talking = null;
        dialogue.IsTalking = false;
        dialogue.Choices = [];
        (dialogue.Speaker, dialogue.Said, dialogue.Expression) = (null, null, null);
        _choiceRows.Clear();
        if (dialogue.Entity?.Scene == Game.Scene && BoxOf(dialogue) is { } box)
            box.IsEnabled = false;
        if (_scaleBeforeTalk is { } scale)
            Game.Time.Scale = scale;
        _scaleBeforeTalk = null;
        Game.PauseSystem.SyncPauses();

        if (!finished)
            return;
        dialogue.IsDone = true;
        if (!string.IsNullOrWhiteSpace(dialogue.Message))
            Game.Send(dialogue.Message.Trim(), dialogue.Entity, from: dialogue.Entity);
        dialogue.RaiseFinished();
    }

    private Entity? BoxOf(Dialogue dialogue) => string.IsNullOrWhiteSpace(dialogue.Box) ? null : Game.Find(dialogue.Box.Trim());

    public bool IsTyping(Dialogue dialogue) => LineText(dialogue)?.Entity?.GetComponent<Typewriter>() is { IsDone: false };

    // The first text in the box that isn't its name tag's.
    internal Text? LineText(Dialogue dialogue)
    {
        if (BoxOf(dialogue) is not { } box)
            return null;
        var tag = Inside(box, box.GetComponent<DialogueBox>()?.NameTag);
        var tagged = tag?.SelfAndDescendants().ToHashSet() ?? [];
        return box.SelfAndDescendants().Where(e => !tagged.Contains(e)).SelectMany(e => e.GetComponents<Text>()).FirstOrDefault();
    }

    // A prompt is looked for inside its owner first, so every copy of a door prefab shows its own "Press E".
    internal void ShowPrompt(Entity owner, string? name, bool show)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        name = name.Trim();
        var prompt = owner.SelfAndDescendants().FirstOrDefault(e => e.Name == name) ?? Game.Find(name);
        if (prompt is not null && prompt.IsEnabled != show)
            prompt.IsEnabled = show;
    }
}
