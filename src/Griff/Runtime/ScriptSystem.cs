using System.Diagnostics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Runs the scene's scripts (Behavior): starts those new to it, then runs their Update, FixedUpdate and LateUpdate in
/// their steps, a kind of script at a time and, for a crowd of one kind, across the cores.
/// </summary>
internal sealed class ScriptSystem : GameSystem
{
    // The scripts with an Update, a FixedUpdate and a LateUpdate, split into runs.
    [NotState("worked out again from the scene's scripts")]
    private ScriptRun[] _updatingRuns = [];

    [NotState("worked out again from the scene's scripts")]
    private ScriptRun[] _fixedUpdatingRuns = [];

    [NotState("worked out again from the scene's scripts")]
    private ScriptRun[] _lateUpdatingRuns = [];

    [NotState("used again for each crowd")]
    private readonly CrowdJob _crowdJob = new();

    // The scripts running this frame, taken as it starts: spawned scripts wait for the next one. Then those of them with
    // an Update, a FixedUpdate and a LateUpdate, kept with the snapshot they were taken from (unchanged while nothing is
    // added, removed, enabled or disabled), and the last snapshot every script of had started (by their serials: see
    // ComponentList.Serial). Lists replaced are filled again two steps later (see SnapshotPool), counted by _steps.
    [NotState("worked out again from the scene's scripts")]
    private ComponentList<Behavior> _running;

    [NotState("worked out again from the scene's scripts")]
    private long _hooksFor = -1;

    [NotState("worked out again from the scene's scripts")]
    private Behavior[] _hooksFrom = [];

    [NotState("worked out again from the scene's scripts")]
    private Behavior[] _updating = [];

    [NotState("worked out again from the scene's scripts")]
    private Behavior[] _fixedUpdating = [];

    [NotState("worked out again from the scene's scripts")]
    private Behavior[] _lateUpdating = [];

    [NotState("worked out again from the scene's scripts")]
    private long _startedFor = -1;

    [NotState("lists kept only to use again")]
    private readonly SnapshotPool<Behavior> _scriptLists = new();

    [NotState("lists kept only to use again")]
    private readonly SnapshotPool<ScriptRun> _runLists = new();

    public override string Name => "Scripts";

    // A stretch of the scripts with a step: From to From + Count, of one crowd, or not a crowd.
    private readonly record struct ScriptRun(int From, int Count, bool Crowd);

    // `scripts` in runs: each crowd of scripts of one crowd type next to each other, and what's between them.
    private ScriptRun[] RunsOf(Behavior[] scripts)
    {
        var count = 0;
        for (var i = 1; i <= scripts.Length; i++)
        {
            if (i == scripts.Length || !SameRun(scripts[i - 1], scripts[i]))
                count++;
        }

        var runs = _runLists.Rent(count, Game.StepCount);
        var (from, run) = (0, 0);
        for (var i = 1; i <= scripts.Length; i++)
        {
            if (i < scripts.Length && SameRun(scripts[i - 1], scripts[i]))
                continue;
            runs[run++] = new ScriptRun(from, i - from, (scripts[from].Hooks & BehaviorHooks.Crowd) != 0);
            from = i;
        }

        return runs;

        static bool SameRun(Behavior a, Behavior b)
        {
            var crowd = (a.Hooks & BehaviorHooks.Crowd) != 0;
            return crowd == ((b.Hooks & BehaviorHooks.Crowd) != 0) && (!crowd || a.GetType() == b.GetType());
        }
    }

    // Runs `hook` of each of `scripts` that's still in the scene (and while a panel pauses the game, each on an open panel),
    // a run at a time; with a profiler, timing each run of scripts of one kind (a scene's bunnies all at once).
    private void RunScripts(Behavior[] scripts, ScriptRun[] runs, BehaviorHooks hook)
    {
        var profiler = Game.Profiler;
        var paused = hook != BehaviorHooks.FixedUpdate && Game.PausedThisFrame;
        Type? kind = null;
        long started = 0, allocated = 0;
        foreach (var run in runs)
        {
            if (run.Crowd)
            {
                if (profiler is not null)
                    Close(scripts[run.From].GetType());
                RunCrowd(scripts, run, hook, paused);
                continue;
            }

            for (var i = run.From; i < run.From + run.Count; i++)
            {
                var behavior = scripts[i];
                if (behavior.Entity?.Scene != Game.Scene || (paused && !Game.PanelSystem.InOpenPanel(behavior.Entity)))
                    continue;
                if (profiler is not null)
                    Close(behavior.GetType());
                Call(behavior, hook);
            }
        }

        if (kind is not null)
            profiler!.AddCost(kind, Stopwatch.GetTimestamp() - started, Workers.AllocatedBytes() - allocated);

        // From here, what runs counts to scripts of type `type`.
        void Close(Type type)
        {
            if (type == kind)
                return;
            if (kind is not null)
                profiler!.AddCost(kind, Stopwatch.GetTimestamp() - started, Workers.AllocatedBytes() - allocated);
            (kind, started, allocated) = (type, Stopwatch.GetTimestamp(), Workers.AllocatedBytes());
        }
    }

    private static void Call(Behavior behavior, BehaviorHooks hook)
    {
        switch (hook)
        {
            case BehaviorHooks.Update:
                behavior.RunUpdate();
                break;
            case BehaviorHooks.FixedUpdate:
                behavior.RunFixedUpdate();
                break;
            default:
                behavior.RunLateUpdate();
                break;
        }
    }

    // A crowd's scripts, together: a chunk of them a thread, then those inside other entities on this one, in order. Each
    // gets numbers of its own from Rand, from a seed the crowd takes from this thread's.
    private void RunCrowd(Behavior[] scripts, ScriptRun run, BehaviorHooks hook, bool paused)
    {
        var job = _crowdJob;
        job.Start(Game, scripts, run, hook, paused, Rand.NextSeed());
        try
        {
            // Paused, a panel's scripts run on this thread: whether one's on an open panel isn't for another to ask. With a
            // breakpoint set, they all do, so it can hold them there (the crowd does the same either way).
            if (paused || Debugging.ScriptProbes.Armed)
                job.RunAll();
            else
                Workers.Run(job, job.Chunks);
            job.RunInsideOthers();
        }
        finally
        {
            job.Finish();
        }
    }

    // The scripts of a crowd, a chunk at a time.
    private sealed class CrowdJob : IChunked
    {
        private const int ChunkSize = 1024;

        private Game? _game;
        private Behavior[] _scripts = [];
        private ScriptRun _run;
        private BehaviorHooks _hook;
        private bool _paused;
        private int _seed;
        private Type _type = typeof(Behavior);

        // Each chunk's scripts on entities inside others, left to run on the game's thread afterwards (by place in the crowd).
        private readonly List<List<int>> _inside = [];

        public int Chunks => (_run.Count + ChunkSize - 1) / ChunkSize;

        public void Start(Game game, Behavior[] scripts, ScriptRun run, BehaviorHooks hook, bool paused, int seed)
        {
            (_game, _scripts, _run, _hook, _paused, _seed, _type) = (game, scripts, run, hook, paused, seed, scripts[run.From].GetType());
            while (_inside.Count < Chunks)
                _inside.Add([]);
        }

        public void Finish() => (_game, _scripts) = (null, []);

        public void RunAll()
        {
            for (var chunk = 0; chunk < Chunks; chunk++)
                Run(chunk);
        }

        public void Run(int chunk)
        {
            var game = _game!;
            var scene = game.Scene;
            var inside = _inside[chunk];
            inside.Clear();
            var end = Math.Min(_run.Count, (chunk + 1) * ChunkSize);
            var previous = Crowds.Enter(_type);
            try
            {
                var random = Crowds.Random;
                for (var place = chunk * ChunkSize; place < end; place++)
                {
                    var behavior = _scripts[_run.From + place];
                    var entity = behavior.Entity;
                    if (entity?.Scene != scene || (_paused && !game.PanelSystem.InOpenPanel(entity)))
                        continue;

                    // Inside another entity, it'd read where that is (while another thread might be moving it): later.
                    if (entity.Parent is not null)
                    {
                        inside.Add(place);
                        continue;
                    }

                    random.Start(_seed, place);
                    Call(behavior, _hook);
                }
            }
            finally
            {
                Crowds.Leave(previous);
            }
        }

        // The scripts on entities inside others, in order, on this thread.
        public void RunInsideOthers()
        {
            var previous = Crowds.Enter(_type);
            try
            {
                var random = Crowds.Random;
                for (var chunk = 0; chunk < Chunks; chunk++)
                {
                    foreach (var place in _inside[chunk])
                    {
                        random.Start(_seed, place);
                        Call(_scripts[_run.From + place], _hook);
                    }

                    _inside[chunk].Clear();
                }
            }
            finally
            {
                Crowds.Leave(previous);
            }
        }
    }

    // Snapshot first: behaviors may spawn or destroy while we iterate. In a network game, the host runs scripts.
    internal void StartScripts()
    {
        _running = Game.Active<Behavior>();
        var running = _running.Items;
        var serial = _running.Serial;
        if (serial == 0 || serial != _hooksFor)
        {
            var (updating, fixedUpdating, lateUpdating) = (_updating, _fixedUpdating, _lateUpdating);
            var (updatingRuns, fixedUpdatingRuns, lateUpdatingRuns) = (_updatingRuns, _fixedUpdatingRuns, _lateUpdatingRuns);
            (_updating, _fixedUpdating, _lateUpdating) = (With(running, BehaviorHooks.Update), With(running, BehaviorHooks.FixedUpdate),
                With(running, BehaviorHooks.LateUpdate));
            (_updatingRuns, _fixedUpdatingRuns, _lateUpdatingRuns) = (RunsOf(_updating), RunsOf(_fixedUpdating), RunsOf(_lateUpdating));
            // The lists of their own that were replaced (not the snapshot itself, when every script had the step).
            foreach (var old in (ReadOnlySpan<Behavior[]>)[updating, fixedUpdating, lateUpdating])
            {
                if (old != _hooksFrom && old != _updating && old != _fixedUpdating && old != _lateUpdating)
                    _scriptLists.Retire(old, Game.StepCount);
            }

            _runLists.Retire(updatingRuns, Game.StepCount);
            _runLists.Retire(fixedUpdatingRuns, Game.StepCount);
            _runLists.Retire(lateUpdatingRuns, Game.StepCount);
            (_hooksFor, _hooksFrom) = (serial, running);
        }

        // A big scene's scripts started long ago: unless it's changed, there's nothing to look for.
        if (serial != 0 && serial == _startedFor)
            return;
        _startedFor = -1;
        foreach (var behavior in running)
        {
            if (!behavior.IsStarted)
                behavior.RunStart(Game);
        }

        _startedFor = serial;
    }

    // Those of `running` whose type has `hook`, in the same order.
    private Behavior[] With(Behavior[] running, BehaviorHooks hook)
    {
        var count = 0;
        foreach (var behavior in running)
        {
            if ((behavior.Hooks & hook) != 0)
                count++;
        }

        if (count == running.Length)
            return running;
        var with = _scriptLists.Rent(count, Game.StepCount);
        count = 0;
        foreach (var behavior in running)
        {
            if ((behavior.Hooks & hook) != 0)
                with[count++] = behavior;
        }

        return with;
    }

    // While a panel pauses the game, only the scripts on open panels run.
    internal void UpdateScripts() => RunScripts(_updating, _updatingRuns, BehaviorHooks.Update);

    // Just before bodies move, in steps where game time moves: not while paused (by a panel, or Time.Scale 0) or frozen in
    // a hit stop, when physics doesn't step either.
    internal void FixedUpdateScripts()
    {
        if (Game.Time.Delta > 0)
            RunScripts(_fixedUpdating, _fixedUpdatingRuns, BehaviorHooks.FixedUpdate);
    }

    // After everything has moved and the camera has followed, before what's pinned to the screen is put in place.
    internal void LateUpdateScripts() => RunScripts(_lateUpdating, _lateUpdatingRuns, BehaviorHooks.LateUpdate);
}
