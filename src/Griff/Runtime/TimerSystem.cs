using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Runs what scripts set going over time: timers (Game.After, Game.Every), coroutines and tweens (Game.MoveTo, ScaleTo,
/// RotateTo, FadeTo).
/// </summary>
internal sealed class TimerSystem : GameSystem
{
    private readonly List<GameTimer> _timers = new(16);

    private readonly List<Tween> _tweens = new(16);

    private readonly List<Coroutine> _coroutines = [];

    // The tweens and timers a frame runs, copied out (into lists kept for the next frame, so running them makes no garbage)
    // since what they call can start more.
    [NotState("lists kept only to use again")]
    private readonly List<Tween> _runningTweens = new(16);

    [NotState("lists kept only to use again")]
    private readonly List<GameTimer> _runningTimers = new(16);

    public override string Name => "Timers";

    public GameTimer After(float seconds, Action action, Entity? owner = null) =>
        AddTimer(new GameTimer(Game.Time.Total + Math.Max(0, seconds), 0, action, owner));

    public GameTimer Every(float seconds, Action action, Entity? owner = null)
    {
        if (!(seconds > 0))
            throw new ArgumentOutOfRangeException(nameof(seconds), "The interval must be more than zero.");
        return AddTimer(new GameTimer(Game.Time.Total + seconds, seconds, action, owner));
    }

    public Coroutine StartCoroutine(System.Collections.IEnumerator routine, Entity? owner = null) =>
        StartCoroutine(new Coroutine(routine, owner, null));

    internal Coroutine StartCoroutine(System.Collections.IEnumerator routine, Behavior owner) =>
        StartCoroutine(new Coroutine(routine, null, owner));

    /// <summary>Stops every coroutine <paramref name="owner"/> started.</summary>
    internal void StopCoroutines(Behavior owner)
    {
        foreach (var coroutine in _coroutines.Where(c => c.OwnerBehavior == owner))
            coroutine.Stop();
    }

    public Tween Tween(float seconds, Action<float> apply, Func<float, float>? ease = null, Entity? owner = null) =>
        AddTween(new Tween(Game.Time.Total, seconds, apply, ease, owner, null));

    public Tween MoveTo(Entity entity, Vector2 position, float seconds, Func<float, float>? ease = null)
    {
        var transform = entity.Transform;
        var from = new Vector2(transform.Position.X, transform.Position.Y);
        return AddTween(new Tween(Game.Time.Total, seconds,
            t => transform.Position = new Vector3(Vector2.Lerp(from, position, t), transform.Position.Z),
            ease, entity, (entity, nameof(MoveTo))));
    }

    public Tween ScaleTo(Entity entity, Vector2 scale, float seconds, Func<float, float>? ease = null)
    {
        var transform = entity.Transform;
        var from = new Vector2(transform.Scale.X, transform.Scale.Y);
        return AddTween(new Tween(Game.Time.Total, seconds,
            t => transform.Scale = new Vector3(Vector2.Lerp(from, scale, t), transform.Scale.Z),
            ease, entity, (entity, nameof(ScaleTo))));
    }

    public Tween RotateTo(Entity entity, float degrees, float seconds, Func<float, float>? ease = null)
    {
        var transform = entity.Transform;
        var from = transform.Rotation.Z;
        return AddTween(new Tween(Game.Time.Total, seconds,
            t => transform.Rotation = transform.Rotation with { Z = from + (degrees - from) * t },
            ease, entity, (entity, nameof(RotateTo))));
    }

    public Tween FadeTo(Entity entity, float alpha, float seconds, Func<float, float>? ease = null)
    {
        var sprite = entity.GetComponent<Sprite>();
        var text = entity.GetComponent<Text>();
        var tilemap = entity.GetComponent<Tilemap>();
        var (spriteFrom, textFrom, tilemapFrom) = (sprite?.Tint.A ?? 0, text?.Color.A ?? 0, tilemap?.Tint.A ?? 0);
        return AddTween(new Tween(Game.Time.Total, seconds, t =>
        {
            if (sprite is not null)
                sprite.Tint = sprite.Tint with { A = spriteFrom + (alpha - spriteFrom) * t };
            if (text is not null)
                text.Color = text.Color with { A = textFrom + (alpha - textFrom) * t };
            if (tilemap is not null)
                tilemap.Tint = tilemap.Tint with { A = tilemapFrom + (alpha - tilemapFrom) * t };
        }, ease, entity, (entity, nameof(FadeTo))));
    }

    private Coroutine StartCoroutine(Coroutine coroutine)
    {
        Crowds.ThrowIfRunning("start coroutines");
        _coroutines.Add(coroutine);
        coroutine.Run(Game);
        return coroutine;
    }

    // Coroutines started by other coroutines have already taken their first step, so they wait for the next frame.
    internal void RunCoroutines()
    {
        if (_coroutines.Count == 0)
            return;
        foreach (var coroutine in _coroutines.ToList())
        {
            // One whose entity (or script) is paused waits, its waits put off as long (see Entity.IsPaused).
            if (coroutine.IsPaused)
                coroutine.Hold(Game.Time.Delta);
            else
                coroutine.Run(Game);
        }
        _coroutines.RemoveAll(c => !c.IsRunning);
    }

    private GameTimer AddTimer(GameTimer timer)
    {
        Crowds.ThrowIfRunning("start timers");
        _timers.Add(timer);
        return timer;
    }

    // A new tween replaces an unfinished one with the same key (the same kind of tween on the same entity).
    private Tween AddTween(Tween tween)
    {
        Crowds.ThrowIfRunning("start tweens");
        if (tween.Key is not null)
        {
            foreach (var other in _tweens.Where(t => Equals(t.Key, tween.Key)))
                other.Cancel();
        }

        _tweens.Add(tween);
        return tween;
    }

    // Tweens added by callbacks (e.g. chained with Then) start on the next frame.
    internal void RunTweens()
    {
        if (_tweens.Count == 0)
            return;
        _runningTweens.AddRange(_tweens);
        try
        {
            foreach (var tween in _runningTweens)
            {
                // One on a paused entity holds where it is, finishing as much later (see Entity.IsPaused).
                if (tween.Owner is { IsPaused: true })
                    tween.Hold(Game.Time.Delta);
                else
                    tween.Run(Game.Time.Total, Game.Scene);
            }
        }
        finally
        {
            _runningTweens.Clear();
        }

        _tweens.RemoveAll(t => !t.IsActive);
    }

    // Timers added by callbacks wait for the next frame, like spawned behaviors.
    internal void RunTimers()
    {
        if (_timers.Count == 0)
            return;
        _runningTimers.AddRange(_timers);
        try
        {
            foreach (var timer in _runningTimers)
            {
                // One whose entity is paused goes off as much later (see Entity.IsPaused).
                if (timer.Owner is { IsPaused: true })
                    timer.Hold(Game.Time.Delta);
                else
                    timer.Run(Game.Time.Total, Game.Scene);
            }
        }
        finally
        {
            _runningTimers.Clear();
        }

        foreach (var timer in _timers)
        {
            if (timer.Owner is not null && timer.Owner.Scene != Game.Scene)
                timer.Cancel();
        }

        _timers.RemoveAll(t => !t.IsActive);
    }
}
