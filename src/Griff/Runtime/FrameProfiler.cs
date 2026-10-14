using System.Diagnostics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// What one frame the screen showed cost, in milliseconds: the whole frame (from the last one shown to this one), and of
/// it, the game's steps, building the picture to draw (<see cref="Rendering.SceneRenderer"/>) and drawing it. The rest of
/// the frame is time the game had to spare, waiting for the screen. <paramref name="Allocated"/> is the garbage the
/// frame's work made (the bytes it allocated, which the garbage collector will have to clear up), and
/// <paramref name="Collections"/> how many times the garbage collector ran during it.
/// </summary>
public readonly record struct FrameSample(
    float FrameMs, float StepMs, float BuildMs, float DrawMs, int Steps, int Draws, long Allocated = 0, int Collections = 0)
{
    /// <summary>The work the frame took: its steps, building and drawing.</summary>
    public float WorkMs => StepMs + BuildMs + DrawMs;
}

/// <summary>
/// How a stretch of frames went: how many, the frame rate and its 1% low (the rate of the slowest 1% of frames), the
/// average and worst frame, and what the game's steps, building and drawing took on average; then the garbage a frame
/// made on average (in bytes), the most one made, and how many times the garbage collector ran in all.
/// </summary>
public sealed record FrameSummary(
    int Frames, double Fps, double LowFps, double AverageMs, double WorstMs, double StepMs, double BuildMs, double DrawMs, double Steps, double Draws,
    double Allocated = 0, long MostAllocated = 0, int Collections = 0)
{
    public static readonly FrameSummary None = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The garbage a frame made on average, e.g. "0 B", "12.5 KB" or "1.2 MB".</summary>
    public string Garbage => FrameCost.Bytes(Allocated);
}

/// <summary>What a cost in a frame is for: one of Griff's steps, a system a script added, a kind of script, the picture, or the editor's own work.</summary>
public enum FrameCostKind
{
    /// <summary>One of Griff's own steps of the frame (see <see cref="Game.Frame"/>), named for what it runs: Physics, Animations, Particles.</summary>
    Step,

    /// <summary>The <c>Update</c> of a <see cref="GameSystem"/> a script added.</summary>
    System,

    /// <summary>Every script of one kind (a <see cref="Behavior"/> class): their Update, FixedUpdate and LateUpdate together.</summary>
    Script,

    /// <summary>Building the picture of the frame (Build) and drawing it (Draw), as a whole.</summary>
    Picture,

    /// <summary>Work the editor does for the game it plays that an exported game doesn't (keeping rewind snapshots, its panels following the game).</summary>
    Editor,
}

/// <summary>
/// What one thing (a step of the frame, a kind of script, a system: see <see cref="Kind"/>) cost a frame, over the frames
/// a <see cref="FrameProfiler"/> holds: the time it took on average and at worst, in milliseconds, the garbage it made a
/// frame on average (in bytes), and its share of the frame's work (its steps, building and drawing: 0 to 1).
/// </summary>
public sealed record FrameCost(string Name, FrameCostKind Kind, double Ms, double WorstMs, double Allocated, double Share)
{
    /// <summary>The garbage it made a frame on average, e.g. "120 B"; empty when it made none.</summary>
    public string Garbage => Allocated >= 0.5 ? Bytes(Allocated) : "";

    /// <summary>What it is, e.g. "Bunny (script): 1.25 ms".</summary>
    public string Label => $"{Name} ({Kind.ToString().ToLowerInvariant()}): {Ms:0.00} ms" + (Garbage is { Length: > 0 } garbage ? $", {garbage}" : "");

    /// <summary>Bytes as people read them: "0 B", "512 B", "12.5 KB", "1.2 MB".</summary>
    public static string Bytes(double bytes) => bytes switch
    {
        < 1024 => $"{bytes:0} B",
        < 1024 * 1024 => $"{bytes / 1024:0.0} KB",
        _ => $"{bytes / (1024 * 1024):0.0} MB",
    };
}

/// <summary>
/// Keeps the last few seconds of a running game's frames (see <see cref="FrameSample"/>), for a profiler to show. Set one
/// as <see cref="Game.Profiler"/> and whatever runs the game (the editor's Game tab, a game's own window) adds each frame
/// it shows; with none set, nothing is timed. Frames held still (paused) aren't added.
/// </summary>
/// <remarks>
/// While it's set, the game also times each of its steps, each kind of script and each system a script added, and the
/// garbage each makes (see <see cref="AddCost(string, FrameCostKind, long, long)"/>); <see cref="Costs"/> sums them up.
/// What's added between one frame and the next counts in the next frame added. Keeping track of a cost allocates nothing
/// once it's been seen, so it doesn't count as garbage itself.
/// </remarks>
public sealed class FrameProfiler
{
    /// <summary>Ten seconds at 60 frames a second.</summary>
    public const int DefaultCapacity = 600;

    private readonly FrameSample[] _samples;
    private readonly float[] _sorted;
    private int _next;

    // What each step, kind of script and system cost in each frame held (by the frame's place in _samples), and in the
    // frame under way; found by its name (a step's or system's) or its type (a script's).
    private readonly Dictionary<object, CostTrack> _costs = [];
    private readonly List<CostTrack> _tracks = [];

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
        // The costs added since the last frame are this one's.
        foreach (var track in _tracks)
            track.Close(_next, Recorded);
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

        // Kept, so the things it's seen don't have to be made again (as garbage) in the next frame timed.
        foreach (var track in _tracks)
            track.Forget();
    }

    /// <summary>
    /// Adds what a step of the frame (or a system a script added, or the editor's own work) took to the frame under way:
    /// <paramref name="ticks"/> of <see cref="Stopwatch"/> time, making <paramref name="allocated"/> bytes of garbage (as
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/> counts them). A name added twice in a frame adds up.
    /// </summary>
    public void AddCost(string name, FrameCostKind kind, long ticks, long allocated) => Track(name, name, kind).Add(ticks, allocated);

    /// <summary>Adds what scripts of type <paramref name="script"/> took to the frame under way, as <see cref="AddCost(string, FrameCostKind, long, long)"/> does.</summary>
    public void AddCost(Type script, long ticks, long allocated) => Track(script, script.Name, FrameCostKind.Script).Add(ticks, allocated);

    /// <summary>Forgets the costs added since the last frame: the next frame's start from nothing.</summary>
    public void BeginFrame()
    {
        foreach (var track in _tracks)
            track.Discard();
    }

    private CostTrack Track(object key, string name, FrameCostKind kind)
    {
        if (!_costs.TryGetValue(key, out var track))
        {
            track = new CostTrack(name, kind, Capacity);
            _costs.Add(key, track);
            _tracks.Add(track);
        }

        return track;
    }

    /// <summary>
    /// What each step of the frame, kind of script and system cost a frame over the newest <paramref name="frames"/> frames
    /// (all it holds, by default), the most first; things that didn't run in them are left out.
    /// </summary>
    public IReadOnlyList<FrameCost> Costs(int frames = int.MaxValue)
    {
        var count = Math.Min(frames, Count);
        if (count <= 0)
            return [];

        double work = 0;
        for (var i = 0; i < count; i++)
            work += this[Count - count + i].WorkMs;
        work /= count;

        var oldest = Recorded - count;
        var costs = new List<FrameCost>();
        foreach (var track in _tracks)
        {
            if (track.LastFrame < oldest)
                continue;
            double ms = 0, worst = 0, allocated = 0;
            for (var i = 0; i < count; i++)
            {
                var at = (_next - count + i + Capacity) % Capacity;
                ms += track.Ms[at];
                worst = Math.Max(worst, track.Ms[at]);
                allocated += track.Allocated[at];
            }

            ms /= count;
            costs.Add(new FrameCost(track.Name, track.Kind, ms, worst, allocated / count, work > 0 ? Math.Clamp(ms / work, 0, 1) : 0));
        }

        costs.Sort((a, b) => b.Ms.CompareTo(a.Ms) is var order and not 0 ? order : string.CompareOrdinal(a.Name, b.Name));
        return costs;
    }

    // One thing's costs, a frame at a time.
    private sealed class CostTrack(string name, FrameCostKind kind, int capacity)
    {
        public readonly string Name = name;
        public readonly FrameCostKind Kind = kind;
        public readonly float[] Ms = new float[capacity];
        public readonly float[] Allocated = new float[capacity];

        // The newest frame it had a cost in (as FrameProfiler.Recorded counts them), or -1.
        public long LastFrame = -1;
        private long _ticks;
        private long _allocated;
        private bool _added;

        public void Add(long ticks, long allocated)
        {
            _ticks += ticks;
            _allocated += Math.Max(0, allocated);
            _added = true;
        }

        public void Close(int at, long frame)
        {
            Ms[at] = (float)(_ticks * 1000.0 / Stopwatch.Frequency);
            Allocated[at] = _allocated;
            if (_added)
                LastFrame = frame;
            Discard();
        }

        public void Discard()
        {
            _ticks = 0;
            _allocated = 0;
            _added = false;
        }

        public void Forget()
        {
            Discard();
            Array.Clear(Ms);
            Array.Clear(Allocated);
            LastFrame = -1;
        }
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

        double total = 0, worst = 0, step = 0, build = 0, draw = 0, steps = 0, draws = 0, allocated = 0;
        long mostAllocated = 0;
        var collections = 0;
        var times = _sorted.AsSpan(0, count);
        for (var i = 0; i < count; i++)
        {
            var sample = this[Count - count + i];
            times[i] = sample.FrameMs;
            allocated += sample.Allocated;
            mostAllocated = Math.Max(mostAllocated, sample.Allocated);
            collections += sample.Collections;
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
            step / count, build / count, draw / count, steps / count, draws / count, allocated / count, mostAllocated, collections);
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
