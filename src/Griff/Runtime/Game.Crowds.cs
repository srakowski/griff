using System.Diagnostics;

namespace Griff.Runtime;

// Running scripts a step at a time: in hierarchy order, each in turn, except a crowd (a run of crowd scripts of one type
// next to each other: see CrowdAttribute), whose scripts run together, spread across the cores (see Workers).
public sealed partial class Game
{
    // A stretch of the scripts with a step: From to From + Count, of one crowd, or not a crowd.
    private readonly record struct ScriptRun(int From, int Count, bool Crowd);

    // The scripts with an Update, a FixedUpdate and a LateUpdate, split into runs.
    private ScriptRun[] _updatingRuns = [];
    private ScriptRun[] _fixedUpdatingRuns = [];
    private ScriptRun[] _lateUpdatingRuns = [];

    private readonly CrowdJob _crowdJob = new();

    // `scripts` in runs: each crowd of scripts of one crowd type next to each other, and what's between them.
    private static ScriptRun[] RunsOf(Behavior[] scripts)
    {
        var runs = new List<ScriptRun>();
        var from = 0;
        for (var i = 1; i <= scripts.Length; i++)
        {
            if (i < scripts.Length && SameRun(scripts[i - 1], scripts[i]))
                continue;
            runs.Add(new ScriptRun(from, i - from, (scripts[from].Hooks & BehaviorHooks.Crowd) != 0));
            from = i;
        }

        return [.. runs];

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
        var profiler = Profiler;
        var paused = hook != BehaviorHooks.FixedUpdate && PausedThisFrame;
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
                if (behavior.Entity?.Scene != Scene || (paused && !InOpenPanel(behavior.Entity)))
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
        job.Start(this, scripts, run, hook, paused, Rand.NextSeed());
        try
        {
            // Paused, a panel's scripts run on this thread: whether one's on an open panel isn't for another to ask.
            if (paused)
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
                    if (entity?.Scene != scene || (_paused && !game.InOpenPanel(entity)))
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
}
