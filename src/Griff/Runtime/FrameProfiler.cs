using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// What one frame the screen showed cost, in milliseconds: the whole frame (from the last one shown to this one), and of
/// it, the game's steps, building the picture to draw (<see cref="Rendering.SceneRenderer"/>) and drawing it. The rest of
/// the frame is time the game had to spare, waiting for the screen.
/// </summary>
public readonly record struct FrameSample(float FrameMs, float StepMs, float BuildMs, float DrawMs, int Steps, int Draws)
{
    /// <summary>The work the frame took: its steps, building and drawing.</summary>
    public float WorkMs => StepMs + BuildMs + DrawMs;
}

/// <summary>
/// How a stretch of frames went: how many, the frame rate and its 1% low (the rate of the slowest 1% of frames), the
/// average and worst frame, and what the game's steps, building and drawing took on average.
/// </summary>
public sealed record FrameSummary(
    int Frames, double Fps, double LowFps, double AverageMs, double WorstMs, double StepMs, double BuildMs, double DrawMs, double Steps, double Draws)
{
    public static readonly FrameSummary None = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Keeps the last few seconds of a running game's frames (see <see cref="FrameSample"/>), for a profiler to show. Set one
/// as <see cref="Game.Profiler"/> and whatever runs the game (the editor's Game tab, a game's own window) adds each frame
/// it shows; with none set, nothing is timed. Frames held still (paused) aren't added.
/// </summary>
public sealed class FrameProfiler
{
    /// <summary>Ten seconds at 60 frames a second.</summary>
    public const int DefaultCapacity = 600;

    private readonly FrameSample[] _samples;
    private readonly float[] _sorted;
    private int _next;

    public FrameProfiler(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _samples = new FrameSample[capacity];
        _sorted = new float[capacity];
    }

    /// <summary>How many frames it keeps: adding one more forgets the oldest.</summary>
    public int Capacity => _samples.Length;

    /// <summary>How many frames it holds now.</summary>
    public int Count { get; private set; }

    /// <summary>Every frame added since it was made or cleared, kept or not: it changes with each one.</summary>
    public long Recorded { get; private set; }

    /// <summary>A frame it holds, from 0 (the oldest) to <see cref="Count"/> - 1 (the newest).</summary>
    public FrameSample this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _samples[(_next - Count + index + Capacity) % Capacity];
        }
    }

    /// <summary>The newest frame, or none (all zeros) when it holds none.</summary>
    public FrameSample Latest => Count == 0 ? default : this[Count - 1];

    public void Add(in FrameSample sample)
    {
        _samples[_next] = sample;
        _next = (_next + 1) % Capacity;
        Count = Math.Min(Count + 1, Capacity);
        Recorded++;
    }

    /// <summary>Forgets every frame.</summary>
    public void Clear()
    {
        _next = 0;
        Count = 0;
        Recorded = 0;
    }

    /// <summary>Copies the frames it holds into <paramref name="into"/>, oldest first, as many as fit (the newest ones). Returns how many.</summary>
    public int CopyTo(Span<FrameSample> into)
    {
        var count = Math.Min(into.Length, Count);
        for (var i = 0; i < count; i++)
            into[i] = this[Count - count + i];
        return count;
    }

    /// <summary>How the newest <paramref name="frames"/> frames went (all it holds, by default).</summary>
    public FrameSummary Summarize(int frames = int.MaxValue)
    {
        var count = Math.Min(frames, Count);
        if (count <= 0)
            return FrameSummary.None;

        double total = 0, worst = 0, step = 0, build = 0, draw = 0, steps = 0, draws = 0;
        var times = _sorted.AsSpan(0, count);
        for (var i = 0; i < count; i++)
        {
            var sample = this[Count - count + i];
            times[i] = sample.FrameMs;
            total += sample.FrameMs;
            worst = Math.Max(worst, sample.FrameMs);
            step += sample.StepMs;
            build += sample.BuildMs;
            draw += sample.DrawMs;
            steps += sample.Steps;
            draws += sample.Draws;
        }

        // The 1% low: the frame rate of the frame 99% of them are no slower than.
        times.Sort();
        var slow = times[Math.Min(count - 1, (int)(count * 0.99))];
        return new FrameSummary(count, total > 0 ? count * 1000 / total : 0, slow > 0 ? 1000 / slow : 0, total / count, worst,
            step / count, build / count, draw / count, steps / count, draws / count);
    }
}

/// <summary>How many of each kind of component are on a scene's active entities: one row of <see cref="SceneCounts"/>.</summary>
public sealed record ComponentCount(Type Type, int Count)
{
    /// <summary>The kind of component and how many, e.g. "Sprite: 5,000".</summary>
    public string Label => $"{Type.Name}: {Count:N0}";
}

/// <summary>
/// How many entities a scene holds, how many of them are active (the game runs and draws them), and how many of each
/// kind of component the active ones have, the most first (every entity's Transform left out).
/// </summary>
public sealed record SceneCounts(int Entities, int Active, IReadOnlyList<ComponentCount> Components)
{
    public static readonly SceneCounts None = new(0, 0, []);

    /// <summary>
    /// Counts <paramref name="scene"/> as it is now, in one pass over every entity: a game's frame can't spare that for a
    /// big scene, so a profiler counts a slice at a time with a <see cref="SceneCounter"/>.
    /// </summary>
    public static SceneCounts Of(Scene scene) => new SceneCounter(int.MaxValue).Count(scene)!;
}

/// <summary>
/// Counts a scene (see <see cref="SceneCounts"/>) a slice of its entities at a time, so a big one is counted over several
/// goes without holding up a frame: 20,000 entities take about 3.6 ms to count at once in a Debug editor.
/// </summary>
public sealed class SceneCounter(int slice = SceneCounter.DefaultSlice)
{
    /// <summary>About half a millisecond's counting in a Debug editor.</summary>
    public const int DefaultSlice = 2500;

    private readonly Dictionary<Type, int> _byType = [];
    private Scene? _scene;
    private IReadOnlyList<Entity> _entities = [];
    private int _next;
    private int _held;
    private int _active;

    /// <summary>Counts the next slice of <paramref name="scene"/>'s entities. Returns its counts once a whole pass is done, otherwise null.</summary>
    /// <remarks>
    /// A pass goes through the entities there were as it started: one destroyed since isn't counted, and one spawned since
    /// waits for the next pass. Asking about another scene starts again.
    /// </remarks>
    public SceneCounts? Count(Scene scene)
    {
        if (scene != _scene)
            Restart(scene);
        var end = (int)Math.Min(_entities.Count, (long)_next + Math.Max(slice, 1));
        for (; _next < end; _next++)
        {
            var entity = _entities[_next];
            if (entity.Scene != scene)
                continue;
            _held++;
            if (!entity.IsActive)
                continue;
            _active++;
            foreach (var component in entity.Components)
            {
                if (component is Transform)
                    continue;
                var type = component.GetType();
                _byType[type] = _byType.GetValueOrDefault(type) + 1;
            }
        }

        if (_next < _entities.Count)
            return null;
        var components = _byType.Select(c => new ComponentCount(c.Key, c.Value))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Type.Name, StringComparer.Ordinal).ToList();
        var counts = new SceneCounts(_held, _active, components);
        Restart(scene);
        return counts;
    }

    /// <summary>Forgets the pass so far: the next <see cref="Count"/> starts a fresh one.</summary>
    public void Restart(Scene? scene = null)
    {
        _scene = scene;
        _entities = scene?.Entities ?? [];
        _next = 0;
        _held = 0;
        _active = 0;
        _byType.Clear();
    }
}
