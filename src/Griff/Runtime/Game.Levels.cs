using System.Numerics;
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

    // Teleported by a door and still touching one: it can't use a door again until it has stepped off them all.
    private readonly HashSet<Entity> _arrivedByDoor = [];
    private readonly HashSet<Dialogue> _dialoguesSeen = [];
    private readonly HashSet<Dialogue> _dialoguesTouched = [];
    private Dialogue? _talking;
    private bool _talkJustStarted;
    private float? _scaleBeforeTalk;

    // The rows of the choices offered now, and where the mouse and stick were last frame (to notice them move).
    private readonly List<int> _choiceRows = [];
    private Vector2? _lastMouseOnChoices;
    private int _lastChoiceStick;

    /// <summary>The <see cref="Dialogue"/> being said right now, if any.</summary>
    public Dialogue? Talking => _talking;

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
    private IEnumerable<T> Active<T>() where T : Component =>
        Scene.Entities.Where(e => SceneRenderer.IsActive(e) && Runs(e)).SelectMany(e => e.GetComponents<T>()).ToList();

    // What this machine moves: what it runs, less other players' own characters, plus this player's own.
    private IEnumerable<T> Moving<T>() where T : Component =>
        Scene.Entities.Where(e => SceneRenderer.IsActive(e) && Moves(e)).SelectMany(e => e.GetComponents<T>()).ToList();

    // Everything showing, wherever it's run: for what only changes how things look.
    private IEnumerable<T> Showing<T>() where T : Component =>
        Scene.Entities.Where(SceneRenderer.IsActive).SelectMany(e => e.GetComponents<T>()).ToList();

    // Movers go before physics, so whatever stands on them is carried along this frame.
    private void RunMovers()
    {
        foreach (var mover in Active<Mover>())
            mover.Advance(Time.Delta);
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
        if (target is not null && SceneRenderer.IsActive(target) && Health.Of(target) is not { IsDead: true })
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

    private static void Face(Entity entity, int direction)
    {
        if (entity.GetComponent<Sprite>() is { } sprite)
            sprite.FlipX = direction < 0;
    }

    // Nothing fires while game time stands still: a pause, a hit stop, a conversation.
    private void RunShooters()
    {
        if (Time.Delta <= 0)
            return;
        foreach (var shooter in Active<Shooter>().Where(s => s.Running))
        {
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
                wantsToFire = target is not null && SceneRenderer.IsActive(target) && Health.Of(target) is not { IsDead: true };
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

            if (!shooter.Ready(Time.Delta, wantsToFire) || string.IsNullOrWhiteSpace(shooter.Prefab))
                continue;

            aim = aim.LengthSquared() > 1e-8f ? Vector2.Normalize(aim) : Vector2.UnitX;
            foreach (var direction in shooter.VolleyAround(aim))
            {
                var shot = Spawn(shooter.Prefab.Trim(), here + shooter.Offset);
                Claim(shot, entity);
                if (shot.GetComponent<Body>() is { } body)
                    body.Velocity = direction * shooter.Speed;
                shot.Transform.Rotation = shot.Transform.Rotation with { Z = MathF.Atan2(direction.Y, direction.X) * 180 / MathF.PI };
                shooter.RaiseFired(shot);
            }

            if (!string.IsNullOrWhiteSpace(shooter.Sound))
                PlayShared(shooter.Sound);
            UseAction(entity);
        }
    }

    // Who's touching what this frame, for checkpoints, doors and dialogue: the thing touched, and whatever touched it
    // (the body that owns the collider, so a player's hitbox child counts as the player), with its collider's layer.
    private IEnumerable<(Entity By, string? Layer)> TouchersOf(Entity entity) =>
        _touches.Where(t => t.Item1 == entity && t.Item2.Scene == Scene)
            .Select(t => (By: BodyOwner(t.Item2), Layer: LayerOf(t.Item2)))
            .Where(t => t.By is not null && !entity.IsSelfOrDescendantOf(t.By))
            .Select(t => (t.By!, t.Layer))
            .Distinct();

    private void RunCheckpoints()
    {
        var all = Scene.Entities.Where(Runs).SelectMany(e => e.GetComponents<Checkpoint>()).ToList();
        foreach (var checkpoint in all.Where(c => SceneRenderer.IsActive(c.Entity!)))
        {
            if (checkpoint.Active)
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
                Send(checkpoint.Message.Trim(), checkpoint.Entity);
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
        foreach (var health in Scene.Entities.Where(Runs).SelectMany(e => e.GetComponents<Health>()).Where(h => h.Respawn && h.IsDead).ToList())
        {
            var entity = health.Entity!;
            if (float.IsNaN(health.RespawnLeft))
            {
                health.RespawnLeft = Math.Max(0, health.RespawnDelay);
                entity.IsEnabled = false;
            }

            health.RespawnLeft -= Time.Delta;
            if (health.RespawnLeft > 0)
                continue;

            var checkpoint = Scene.Entities.Where(SceneRenderer.IsActive).SelectMany(e => e.GetComponents<Checkpoint>()).FirstOrDefault(c => c.Active);
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
        entity.Transform.Position = new Vector3(target.X, target.Y, entity.Transform.Position.Z);
        if (entity.GetComponent<Body>() is { } body)
            body.Velocity = Vector2.Zero;
        foreach (var follow in Scene.Entities.SelectMany(e => e.GetComponents<Follow>()).Where(f => f.TargetEntity == entity))
            follow.Snap();
    }

    // Nobody goes through a door while a panel pauses the game.
    private void RunDoors()
    {
        if (PausedThisFrame)
            return;
        var touchingAnyDoor = new HashSet<Entity>();
        foreach (var door in Active<Door>())
        {
            var users = TouchersOf(door.Entity!).Where(t => door.AcceptsLayer(t.Layer)).Select(t => t.By).ToList();
            touchingAnyDoor.UnionWith(users);
            var ready = door.Locked ? [] : users.Where(u => !_arrivedByDoor.Contains(u) && CanUnlock(door, u)).ToList();
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
            _arrivedByDoor.IntersectWith(touchingAnyDoor);
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
        _arrivedByDoor.Add(user);

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
        if (_talking is { } talking)
        {
            if (talking.Entity?.Scene != Scene)
                EndDialogue(finished: false);
            else if (!_talkJustStarted && !RunChoices(talking) && Input.WasPressed("Interact"))
                AdvanceDialogue(talking);
            _talkJustStarted = false;
            return;
        }

        var touched = new HashSet<Dialogue>();
        foreach (var dialogue in Showing<Dialogue>())
        {
            // Boxes are hidden until there's something to say.
            if (_dialoguesSeen.Add(dialogue) && BoxOf(dialogue) is { } box)
                box.IsEnabled = false;

            var available = !(dialogue.Once && dialogue.IsDone) && dialogue.Lines.Count > 0;
            // In a network game each player reads for themselves, on their own machine.
            var touching = available && TouchersOf(dialogue.Entity!).Any(t => dialogue.AcceptsLayer(t.Layer) && Net.IsMine(t.By));
            if (touching)
                touched.Add(dialogue);
            ShowPrompt(dialogue.Entity!, dialogue.Prompt, touching && dialogue.Start == DialogueStart.Interact);

            if (!touching || _talking is not null)
                continue;
            var starts = dialogue.Start == DialogueStart.Touch
                ? !_dialoguesTouched.Contains(dialogue)
                : Input.WasPressed("Interact");
            if (starts)
                StartDialogue(dialogue);
        }

        // Touches only come from physics, which a paused game doesn't run: keep the last ones until it runs again.
        if (Time.Delta > 0 || _talking is not null)
        {
            _dialoguesTouched.Clear();
            _dialoguesTouched.UnionWith(touched);
        }
    }

    /// <summary>
    /// Starts <paramref name="dialogue"/> straight away, as if the player had pressed Interact beside it: from a script, a
    /// cutscene, a boss appearing. Does nothing while another dialogue is being said, or if it has no lines.
    /// </summary>
    public void StartDialogue(Dialogue dialogue)
    {
        if (_talking is not null || dialogue.Lines.Count == 0 || dialogue.Entity?.Scene != Scene)
            return;
        dialogue.CheckParts();
        _talking = dialogue;
        _talkJustStarted = true;
        dialogue.IsTalking = true;
        dialogue.Line = 0;
        ShowPrompt(dialogue.Entity!, dialogue.Prompt, false);
        if (BoxOf(dialogue) is { } box)
            box.IsEnabled = true;
        if (dialogue.PauseGame)
        {
            _scaleBeforeTalk = Time.Scale;
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
            SayFrom(dialogue, dialogue.Line + 1);
    }

    /// <summary>
    /// Picks choice <paramref name="choice"/> (from 0) of those <paramref name="dialogue"/> offers now, as the player
    /// would, and goes where it leads. Does nothing when it isn't being said or offers no such choice.
    /// </summary>
    public void ChooseDialogue(Dialogue dialogue, int choice)
    {
        if (_talking != dialogue || choice < 0 || choice >= _choiceRows.Count)
            return;
        var (_, _, to) = dialogue.Row(_choiceRows[choice]);
        var after = _choiceRows[^1] + 1;
        dialogue.Picked = choice;
        dialogue.RaiseChosen(choice);
        if (_talking != dialogue)
            return;

        if (string.IsNullOrEmpty(to))
            SayFrom(dialogue, after);
        else if (dialogue.RowAfterPart(to) is { } next)
            SayFrom(dialogue, next);
        else
            EndDialogue(finished: true);
    }

    // Shows the first line from row on, going where go-tos say; the end of the lines or the start of another part ends it.
    private void SayFrom(Dialogue dialogue, int row)
    {
        for (var steps = 0; steps <= dialogue.Lines.Count; steps++)
        {
            if (row >= dialogue.Lines.Count)
            {
                EndDialogue(finished: true);
                return;
            }

            var (kind, _, to) = dialogue.Row(row);
            if (kind is Dialogue.RowKind.Said or Dialogue.RowKind.Choice)
            {
                ShowLine(dialogue, row);
                return;
            }

            if (kind == Dialogue.RowKind.Part || string.IsNullOrEmpty(to) || dialogue.RowAfterPart(to) is not { } next)
            {
                EndDialogue(finished: true);
                return;
            }

            row = next;
        }

        throw new InvalidOperationException($"The dialogue on \"{dialogue.Entity?.Name}\" goes round in circles without saying anything.");
    }

    // A line, with the choices under it; choices with no line before them are shown on their own.
    private void ShowLine(Dialogue dialogue, int row)
    {
        dialogue.Line = row;
        _choiceRows.Clear();
        var start = dialogue.Row(row).Kind == Dialogue.RowKind.Choice ? row : row + 1;
        for (var next = start; next < dialogue.Lines.Count && dialogue.Row(next).Kind == Dialogue.RowKind.Choice; next++)
            _choiceRows.Add(next);
        dialogue.Choices = _choiceRows.Select(r => dialogue.Row(r).Text).ToList();
        dialogue.Picked = 0;
        _lastMouseOnChoices = null;
        _lastChoiceStick = ChoiceStick;

        if (LineText(dialogue) is { } text)
        {
            _lastMouseOnChoices = MouseOn(text);
            text.Value = LineWithChoices(dialogue, text.Align);
            text.Entity!.GetComponent<Typewriter>()?.Restart();
        }

        dialogue.RaiseLineShown();
    }

    // The line showing and its choices under it, the one picked with arrows by it that keep the rows lined up.
    private static string LineWithChoices(Dialogue dialogue, TextAlign align)
    {
        var (kind, said, _) = dialogue.Row(dialogue.Line);
        var rows = dialogue.Choices.Select((choice, i) => (i == dialogue.Picked, align) switch
        {
            (true, TextAlign.Left) => "> " + choice,
            (true, TextAlign.Right) => choice + " <",
            (true, _) => "> " + choice + " <",
            (false, TextAlign.Left) => "   " + choice,
            (false, TextAlign.Right) => choice + "   ",
            _ => choice,
        });
        return string.Join("\n", kind == Dialogue.RowKind.Choice ? rows : rows.Prepend(said));
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
                 (stick > 0 && _lastChoiceStick <= 0);
        var down = Input.WasPressed(Key.Down) || Input.WasPressed(Key.S) || Input.WasPressed(GamepadButton.DPadDown) ||
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

    private void EndDialogue(bool finished)
    {
        if (_talking is not { } dialogue)
            return;
        _talking = null;
        dialogue.IsTalking = false;
        dialogue.Choices = [];
        _choiceRows.Clear();
        if (dialogue.Entity?.Scene == Scene && BoxOf(dialogue) is { } box)
            box.IsEnabled = false;
        if (_scaleBeforeTalk is { } scale)
            Time.Scale = scale;
        _scaleBeforeTalk = null;

        if (!finished)
            return;
        dialogue.IsDone = true;
        if (!string.IsNullOrWhiteSpace(dialogue.Message))
            Send(dialogue.Message.Trim(), dialogue.Entity);
        dialogue.RaiseFinished();
    }

    private Entity? BoxOf(Dialogue dialogue) => string.IsNullOrWhiteSpace(dialogue.Box) ? null : Find(dialogue.Box.Trim());

    private Text? LineText(Dialogue dialogue) =>
        BoxOf(dialogue)?.SelfAndDescendants().SelectMany(e => e.GetComponents<Text>()).FirstOrDefault();

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

    // Leaving a scene leaves its doors and conversations behind.
    private void ForgetLevelState()
    {
        EndDialogue(finished: false);
        _arrivedByDoor.Clear();
        _dialoguesSeen.Clear();
        _dialoguesTouched.Clear();
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
