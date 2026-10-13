#if DEBUG || DEMO_CHECK
using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// What the demo's pilot decides each frame, kept free of WPF so it can be checked headlessly: hold right, jump the
/// ponds and the step, double-jump up the planks to the ledge, swing at slimes in the way, walk into the door.
/// </summary>
internal sealed class DemoPilotBrain(Action<string>? showKeys)
{
    private readonly List<Jump> _jumps =
    [
        new(13.2f, -5, Double: false),   // first pond
        new(21.6f, -5, Double: false),   // the step
        new(29.4f, -5, Double: true, StopRightAtX: 31.3f), // up through the planks, landing on them
        new(32.6f, -0.5f, Double: true, DoubleAtVelocity: 1f, HoldRightAboveY: 3.7f, ReleaseAboveY: 4.4f), // straight up from the planks, then onto the ledge
        new(43.6f, -5, Double: false),   // the bump that keeps the second slime in
        new(51.9f, -5, Double: false),   // second pond
        // Going back for a missed key: the same obstacles the other way.
        new(58.1f, -5, Double: false, Direction: -1),
        new(46.5f, -5, Double: false, Direction: -1),
        new(37.5f, -5, Double: false, Direction: -1),
    ];

    private readonly HashSet<Key> _down = [];
    private int _jumpStage; // 0 ready, 1 first jump held, 3 let go before the double, 4 double held, 5 falling
    private Jump? _current;
    private int _releaseFrames;
    private int _frames;
    private bool _retreating;
    private float _attackCooldown;
    private float _lastX = float.NaN;
    private double _lastTime;

    private sealed record Jump(float X, float MinY, bool Double, float DoubleAtVelocity = 1.5f, float HoldRightAboveY = float.MinValue, float StopRightAtX = float.MaxValue, float ReleaseAboveY = float.MaxValue, int Direction = 1)
    {
        public bool Used { get; set; }
    }

    public bool Enabled { get; set; } = true;

    /// <summary>When set, stops running right at this x (but still jumps and fights on the way).</summary>
    public float? StopAt { get; set; }

    public bool Attacks { get; set; } = true;

    public Vector2 PlayerPosition { get; private set; }

    public void ReleaseAll(Game? game)
    {
        if (game?.Input is InputState input)
        {
            foreach (var key in _down)
                input.SetKey(key, false);
        }

        _down.Clear();
    }

    /// <summary>Sets the keys for the next tick.</summary>
    public void Step(Game game)
    {
        if (game.Input is not InputState input)
            return;

        var now = game.Time.Total;
        var dt = (float)Math.Clamp(now - _lastTime, 0, 0.1);
        _lastTime = now;
        _attackCooldown -= dt;

        if (!Enabled || game.Find("Player") is not { IsEnabled: true } player || player.GetComponent<Body>() is not { } body)
        {
            ReleaseAll(game);
            return;
        }

        var p = new Vector2(player.Transform.Position.X, player.Transform.Position.Y);
        PlayerPosition = p;

        // Back at a checkpoint after falling in: the jumps ahead are fresh again.
        if (!float.IsNaN(_lastX) && p.X < _lastX - 3)
        {
            foreach (var j in _jumps.Where(j => j.X > p.X - 0.5f))
                j.Used = false;
            _jumpStage = 0;
        }

        _lastX = p.X;

        // Past the planks on the ground without the key (knocked back by a slime, or a jump went wrong): walk back and
        // try the planks again. The door won't open without it.
        var hasKey = player.GetComponent<Inventory>()?.Has("Key") == true || game.Find("Key") is null;
        if (!_retreating && !hasKey && body.IsGrounded && p.Y < -1 && p.X > 35 && _jumpStage == 0)
        {
            _retreating = true;
            foreach (var j in _jumps.Where(j => j.Direction < 0))
                j.Used = false;
            showKeys?.Invoke("←  ·  Back for the key");
        }
        else if (_retreating && p.X <= 27.5f)
        {
            _retreating = false;
            foreach (var j in _jumps.Where(j => j.X is > 29 and < 34))
                j.Used = false;
        }

        var direction = _retreating ? -1 : 1;
        var move = _retreating || StopAt is not { } stop || p.X < stop;
        var attack = false;

        // A slime close ahead (the way we're walking) at about our height: stand and swing.
        if (Attacks && NearestSlime(game, p) is { } enemy)
        {
            var ahead = (enemy.X - p.X) * direction;
            if (ahead is > -0.4f and < 2.0f && Math.Abs(enemy.Y - p.Y) < 1.2f && body.IsGrounded)
            {
                move = ahead > 1.5f;
                if (_attackCooldown <= 0)
                {
                    attack = true;
                    _attackCooldown = 0.32f;
                    showKeys?.Invoke("X  ·  Attack");
                }
            }
        }

        // Jumps: press on reaching the spot on the ground, hold while rising, double-jump near the top.
        var jump = false;
        switch (_jumpStage)
        {
            case 0:
                if (body.IsGrounded && _jumps.FirstOrDefault(j => !j.Used && j.Direction == direction && (direction > 0 ? p.X >= j.X && p.X < j.X + 1.6f : p.X <= j.X && p.X > j.X - 1.6f) && p.Y > j.MinY) is { } next)
                {
                    next.Used = true;
                    _current = next;
                    _jumpStage = 1;
                    jump = true;
                    showKeys?.Invoke("Space  ·  Jump");
                }

                break;
            case 1:
                // Held while rising (the first few frames it may not be moving up yet).
                _frames++;
                jump = _frames < 3 || body.Velocity.Y > 0.5f;
                if (_current!.Double && _frames >= 3 && body.Velocity.Y < _current.DoubleAtVelocity)
                {
                    _jumpStage = 3;
                    _releaseFrames = 2;
                    jump = false;
                }
                else if (!jump)
                {
                    _jumpStage = 5;
                }

                break;
            case 3:
                if (--_releaseFrames <= 0)
                {
                    _jumpStage = 4;
                    _frames = 0;
                    jump = true;
                    showKeys?.Invoke("Space  ·  Double jump");
                }

                break;
            case 4:
                _frames++;
                jump = (_frames < 3 || body.Velocity.Y > 0.5f) && p.Y < _current!.ReleaseAboveY;
                if (!jump)
                    _jumpStage = 5;
                break;
            case 5:
                if (body.IsGrounded)
                    _jumpStage = 0;
                break;
        }

        // Some jumps go straight up first, so as not to hit the underside of what they land on.
        if (!_retreating && _current is { HoldRightAboveY: var clear } && _jumpStage is 1 or 3 or 4 && p.Y < clear)
            move = false;
        if (!_retreating && _current is { StopRightAtX: var limit } && _jumpStage is 1 or 3 or 4 or 5 && p.X >= limit)
            move = false;

        Set(input, Key.Right, move && direction > 0);
        Set(input, Key.Left, move && direction < 0);
        Set(input, Key.Space, jump);
        Set(input, Key.X, attack); // a tap: down this frame, up the next
    }

    private static Vector2? NearestSlime(Game game, Vector2 from)
    {
        Vector2? best = null;
        foreach (var e in game.Scene.Entities)
        {
            if (!e.IsEnabled || !e.Name.StartsWith("Slime", StringComparison.Ordinal))
                continue;
            var at = new Vector2(e.Transform.Position.X, e.Transform.Position.Y);
            if (best is null || Vector2.Distance(at, from) < Vector2.Distance(best.Value, from))
                best = at;
        }

        return best;
    }

    private void Set(InputState input, Key key, bool down)
    {
        if (down == input.IsDown(key) && down == _down.Contains(key))
            return;
        input.SetKey(key, down);
        if (down)
            _down.Add(key);
        else
            _down.Remove(key);
    }
}
#endif
