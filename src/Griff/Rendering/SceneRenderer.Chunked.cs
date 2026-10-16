using System.Runtime.InteropServices;
using Griff.Runtime;
using Griff.Scenes;
using static Griff.Runtime.Views;

namespace Griff.Rendering;

// A big scene's frame, built across cores (see Workers). Its entities are split into chunks, in hierarchy order, and each
// chunk, on whichever thread takes it, draws its plain sprites into a list of its own and puts them back to front among
// themselves. A plain sprite is an entity at the top of the hierarchy whose sprite is all it draws, with no effects or
// parallax: Griff's own drawing, which reads nothing but its entity and writes nothing but its draws. The game's thread
// then draws the entities the chunks left (a script's drawables are called in hierarchy order, as ever), and merges the
// chunks' runs back to front a stretch at a time (a crowd on one layer is a stretch a chunk), and the chunks copy their
// draws straight to where they go. The frame is the one built on one thread, draw for draw.
public static partial class SceneRenderer
{
    // Entities a chunk takes, and the fewest a scene is split into chunks from (two chunks' worth) unless its game says
    // otherwise (see Game.ChunkedFrom).
    internal const int ChunkSize = 1024;
    internal const int DefaultChunkedFrom = 2 * ChunkSize;

    // How a frame is built in chunks: from how many entities on (see Game.ChunkedFrom), and over how many threads (the
    // game's Game.WorkerCount; Workers.Default for a scene that isn't being played). It goes along the build, the game's own.
    internal readonly record struct Chunking(int From, int Workers)
    {
        public static Chunking Default => new(DefaultChunkedFrom, Runtime.Workers.Default);

        public static Chunking Of(Runtime.Game? game) =>
            game is null ? Default : new(game.ChunkedFrom ?? DefaultChunkedFrom, game.WorkerCount);
    }

    // Past this many stretches a run (and 64 in all), the runs are sorted as one by key rather than merged: they're mixed
    // through each other (sorting by Y, say), and merging would go an entry or two at a time.
    private const int MergedStretchesPerRun = 8;

    // What an entry sorts by (as Compare does), with its entity's place in the hierarchy last; and which of its run's draws
    // are its.
    private record struct RunEntry(bool Lifted, int Order, float Z, float Y, int At, int First, int Count);

    // A run of entries put back to front among themselves: a chunk's plain sprites, or what the game's thread drew. Its
    // entries are in hierarchy order, then Sort's order back to front, unless they're InOrder already.
    private sealed class Run
    {
        public RunEntry[] Entries = new RunEntry[ChunkSize];
        public int Count;
        public readonly StableSort Sort = new();
        public bool InOrder;

        public List<SpriteDraw> Draws { get; } = [];

        // Its `k`th entry back to front.
        public ref readonly RunEntry Back(int k) => ref Entries[InOrder ? k : Sort.Order[k]];

        public void Add(in RunEntry entry)
        {
            if (Count == Entries.Length)
                Array.Resize(ref Entries, Count * 2);
            Entries[Count++] = entry;
        }

        // Puts them back to front: nothing to do when they're in order, as a crowd on one layer is.
        public void PutInOrder(bool sortByY)
        {
            var entries = Entries.AsSpan(0, Count);
            var stretches = Sort.TryMerge<RunEntry, RunComparer>(entries, default, MaxStretches);
            InOrder = stretches == 1;
            if (stretches == 0)
                SortByKeys(entries, sortByY, Sort, withPlace: false);
        }
    }

    // Back to front, as Compare puts entries, then by place in the hierarchy (in a run, those of one entity in the order they came).
    private readonly struct RunComparer : IComparer<RunEntry>
    {
        public int Compare(RunEntry a, RunEntry b)
        {
            var c = a.Lifted.CompareTo(b.Lifted);
            if (c == 0)
                c = a.Order.CompareTo(b.Order);
            if (c == 0)
                c = a.Z.CompareTo(b.Z);
            if (c == 0)
                c = b.Y.CompareTo(a.Y);
            return c != 0 ? c : a.At.CompareTo(b.At);
        }
    }

    // Puts entries in order by key, as SortBackToFront does, and with `withPlace`, by place in the hierarchy before that.
    private static void SortByKeys(ReadOnlySpan<RunEntry> entries, bool sortByY, StableSort keys, bool withPlace)
    {
        keys.Begin(entries.Length);
        var key = keys.Keys;
        if (withPlace)
        {
            for (var i = 0; i < entries.Length; i++)
                key[i] = (uint)entries[i].At;
            keys.Apply();
        }

        if (sortByY)
        {
            for (var i = 0; i < entries.Length; i++)
                key[i] = ~StableSort.KeyOf(entries[i].Y);
            keys.Apply();
        }

        for (var i = 0; i < entries.Length; i++)
            key[i] = StableSort.KeyOf(entries[i].Z);
        keys.Apply();
        for (var i = 0; i < entries.Length; i++)
            key[i] = StableSort.KeyOf(entries[i].Order);
        keys.Apply();
        for (var i = 0; i < entries.Length; i++)
            key[i] = entries[i].Lifted ? 1u : 0u;
        keys.Apply();
    }

    // Draws a chunk's plain sprites into its run, leaving the rest of its entities to the game's thread, and puts the run
    // back to front.
    private sealed class ChunkJob : IChunked
    {
        public List<Run> Runs { get; } = [];

        public List<int[]> Left { get; } = [];

        public int[] LeftCounts = [];

        public List<Drawing> Drawings { get; } = [];

        public IReadOnlyList<Entity> Entities = s_noEntities;
        public Scene? Scene;
        public bool SortByY;

        public void Run(int index)
        {
            var run = Runs[index];
            var draws = run.Draws;
            var drawing = Drawings[index];
            var left = Left[index];
            var leftCount = 0;
            draws.Clear();
            run.Count = 0;
            var scene = Scene!;
            var entities = Entities;
            var end = Math.Min(entities.Count, (index + 1) * ChunkSize);
            for (var at = index * ChunkSize; at < end; at++)
            {
                var entity = entities[at];
                var roles = entity.Roles;
                if ((roles & ComponentRoles.Drawable) == 0)
                    continue;
                // What's pinned to the screen is left to the game's thread too, which knows whether this view draws it.
                if (entity.SoleSprite is not { } sole || entity.Parent is not null ||
                    (roles & (ComponentRoles.DrawEffect | ComponentRoles.Parallax | ComponentRoles.ScreenAnchor)) != 0)
                {
                    left[leftCount++] = at;
                    continue;
                }

                if (!entity.IsActive)
                    continue;
                var world = entity.Transform.RootDrawn(scene);
                var start = draws.Count;
                drawing.Entity = entity;
                sole.Draw(drawing, world);
                if (draws.Count > start)
                    run.Add(new RunEntry(false, sole.Order, world.M43, SortByY ? FeetOf(sole, world) : 0, at, start, draws.Count - start));
            }

            LeftCounts[index] = leftCount;
            run.PutInOrder(SortByY);
        }
    }

    // A stretch of the frame: entries From to To of a run (back to front, or with Direct in the order they came, their draws
    // next to each other), whose draws go from Place on.
    private readonly record struct Stretch(int Run, int From, int To, int Place, bool Direct = false);

    // Copies the stretches' draws to their places in the frame.
    private sealed class PlaceJob : IChunked
    {
        public List<Run> Runs = [];
        public List<Stretch> Stretches { get; } = [];
        public List<SpriteDraw>? Frame;

        // Stretches each chunk of the job places.
        public int PerChunk = 1;

        public void Run(int index)
        {
            var frame = CollectionsMarshal.AsSpan(Frame);
            var stretches = CollectionsMarshal.AsSpan(Stretches);
            var end = Math.Min(stretches.Length, (index + 1) * PerChunk);
            for (var s = index * PerChunk; s < end; s++)
            {
                var stretch = stretches[s];
                var run = Runs[stretch.Run];
                var draws = CollectionsMarshal.AsSpan(run.Draws);
                if (run.InOrder || stretch.Direct)
                {
                    // Their draws are next to each other: one copy.
                    var first = run.Entries[stretch.From].First;
                    ref readonly var last = ref run.Entries[stretch.To - 1];
                    draws[first..(last.First + last.Count)].CopyTo(frame[stretch.Place..]);
                    continue;
                }

                var place = stretch.Place;
                var order = run.Sort.Order;
                for (var k = stretch.From; k < stretch.To; k++)
                {
                    ref readonly var entry = ref run.Entries[order[k]];
                    if (entry.Count == 1)
                        frame[place] = draws[entry.First];
                    else
                        draws.Slice(entry.First, entry.Count).CopyTo(frame[place..]);
                    place += entry.Count;
                }
            }
        }
    }

    private sealed class ChunkedScratch
    {
        public ChunkedScratch()
        {
            LeftDrawing = new Drawing(LeftRun.Draws, ScreenEntity, default, 0, false);
        }

        public ChunkJob Job { get; } = new();

        public PlaceJob Place { get; } = new();

        // What the game's thread draws: the entities the chunks left.
        public Run LeftRun { get; } = new();

        public Drawing LeftDrawing { get; }

        public List<Drawn> Described { get; } = [];

        // Every run (the chunks', then the game thread's), each one's next entry while merging, and for sorting them as
        // one, every entry and where it came from.
        public List<Run> Runs { get; } = [];

        public int[] Heads = [];
        public Run All { get; } = new();
        public int[] RunOf = [];
        public int[] EntryOf = [];

        // Whether the runs hold the draws of a chunked frame (and the entities they belong to), to let go of when the next
        // frame isn't one.
        public bool Holding;

        public void LetGo()
        {
            if (!Holding)
                return;
            foreach (var run in Job.Runs)
                run.Draws.Clear();
            LeftRun.Draws.Clear();
            Holding = false;
        }
    }

    // The chunked build of DrawScene (see above), with the same result: draws into `sprites` from `first` on, overwriting
    // what's there (last frame's draws) rather than clearing it first.
    private static void DrawChunked(Scene scene, Box view, float cameraSize, bool playing, List<SpriteDraw> sprites, int first, List<IDrawable>? overs,
        Scratch scratch, BuildTimer? timer, bool sortByY, in ViewFilter filter, int workers)
    {
        var chunked = scratch.Chunked ??= new ChunkedScratch();
        chunked.Holding = true;
        var job = chunked.Job;
        var entities = scene.EntitiesNow;
        var count = (entities.Length + ChunkSize - 1) / ChunkSize;
        while (job.Runs.Count < count)
        {
            var run = new Run();
            job.Runs.Add(run);
            job.Left.Add(new int[ChunkSize]);
            job.Drawings.Add(new Drawing(run.Draws, ScreenEntity, default, 0, false));
        }

        if (job.LeftCounts.Length < count)
            job.LeftCounts = new int[job.Runs.Count];
        for (var c = 0; c < count; c++)
        {
            var drawing = job.Drawings[c];
            (drawing.View, drawing.LineWidth, drawing.IsPlaying, drawing.CanCull) = (view, cameraSize / 180, playing, true);
        }

        (job.Entities, job.Scene, job.SortByY) = (entities, scene, sortByY);
        timer?.Mark(typeof(Sprite));
        try
        {
            Workers.Run(job, count, workers);
        }
        finally
        {
            (job.Entities, job.Scene) = (s_noEntities, null);
        }

        DrawLeft(chunked, entities, count, view, cameraSize, playing, overs, scratch.Drawables, timer, sortByY, filter);

        // The stretches of the frame, back to front, and how many draws it has.
        timer?.Mark(SortingPart);
        var runs = chunked.Runs;
        runs.Clear();
        for (var c = 0; c < count; c++)
            runs.Add(job.Runs[c]);
        runs.Add(chunked.LeftRun);
        var placing = chunked.Place;
        var stretches = placing.Stretches;
        var end = MergeRuns(runs, chunked, stretches, first);
        if (end < 0)
            end = SortRuns(runs, chunked, stretches, first, sortByY);

        // The frame's list holds last frame's draws: overwritten, so they needn't be cleared first (only any past the end).
        CollectionsMarshal.SetCount(sprites, end);
        (placing.Runs, placing.Frame) = (runs, sprites);
        placing.PerChunk = Math.Max(1, stretches.Count / 64);
        try
        {
            Workers.Run(placing, (stretches.Count + placing.PerChunk - 1) / placing.PerChunk, workers);
        }
        finally
        {
            placing.Frame = null;
        }
    }

    private static readonly Entity[] s_noEntities = [];

    // Draws the entities the chunks left, in hierarchy order, into the game thread's run, and puts it back to front.
    private static void DrawLeft(ChunkedScratch chunked, Entity[] entities, int count, Box view, float cameraSize, bool playing,
        List<IDrawable>? overs, List<IDrawable> drawables, BuildTimer? timer, bool sortByY, in ViewFilter filter)
    {
        var job = chunked.Job;
        var run = chunked.LeftRun;
        var draws = run.Draws;
        var described = chunked.Described;
        var drawing = chunked.LeftDrawing;
        (drawing.View, drawing.LineWidth, drawing.IsPlaying, drawing.AsPlaced) = (view, cameraSize / 180, playing, filter.AsPlaced);
        draws.Clear();
        run.Count = 0;
        for (var c = 0; c < count; c++)
        {
            var left = job.Left[c];
            for (var l = 0; l < job.LeftCounts[c]; l++)
            {
                var at = left[l];
                var entity = entities[at];
                if (!entity.IsActive || !filter.Shows(entity))
                    continue;
                timer?.Mark(LeadOf(entity));
                if (!Describe(entity, sortByY, drawables, overs, out var entry))
                    continue;
                var start = draws.Count;
                DrawEntity(entity, entry.Sole, drawables, drawing, draws, timer);
                described.Clear();
                if (drawing.Sorts.Count > 0)
                {
                    if (sortByY)
                        AddSorted(described, entry, start, draws.Count, drawing.Sorts);
                    else if (draws.Count > start)
                        described.Add(entry with { First = start, Count = draws.Count - start });
                    drawing.Sorts.Clear();
                }
                else if (draws.Count > start)
                {
                    described.Add(entry with { First = start, Count = draws.Count - start });
                }

                foreach (var d in described)
                    run.Add(new RunEntry(d.Lifted, d.Order, d.Z, d.Y, at, d.First, d.Count));
            }
        }

        described.Clear();
        timer?.Mark(SortingPart);
        run.PutInOrder(sortByY);
    }

    // Merges the runs back to front a stretch at a time: the run whose next entry goes first goes on until another run's
    // next has to come first. Returns where the draws end, or -1 (having done nothing) if there would be too many stretches.
    private static int MergeRuns(List<Run> runs, ChunkedScratch chunked, List<Stretch> stretches, int first)
    {
        stretches.Clear();
        if (chunked.Heads.Length < runs.Count)
            chunked.Heads = new int[runs.Count * 2];
        var heads = chunked.Heads.AsSpan(0, runs.Count);
        heads.Clear();
        var most = Math.Max(64, MergedStretchesPerRun * runs.Count);
        var comparer = default(RunComparer);
        var place = first;
        while (true)
        {
            // The run whose next entry goes first, and the one whose next goes after it.
            var (best, next) = (-1, -1);
            for (var r = 0; r < runs.Count; r++)
            {
                if (heads[r] >= runs[r].Count)
                    continue;
                if (best < 0 || comparer.Compare(runs[r].Back(heads[r]), runs[best].Back(heads[best])) < 0)
                    (next, best) = (best, r);
                else if (next < 0 || comparer.Compare(runs[r].Back(heads[r]), runs[next].Back(heads[next])) < 0)
                    next = r;
            }

            if (best < 0)
                return place;
            if (stretches.Count == most)
                return -1;

            var run = runs[best];
            var from = heads[best];
            var to = next < 0 ? run.Count : Until(run, from, runs[next].Back(heads[next]));
            stretches.Add(new Stretch(best, from, to, place));
            place += DrawsIn(run, from, to);
            heads[best] = to;
        }
    }

    // How many draws a run's entries from `from` to `to` back to front have.
    private static int DrawsIn(Run run, int from, int to)
    {
        if (run.InOrder)
        {
            ref readonly var last = ref run.Entries[to - 1];
            return last.First + last.Count - run.Entries[from].First;
        }

        var count = 0;
        for (var k = from; k < to; k++)
            count += run.Back(k).Count;
        return count;
    }

    // Where a run's entries back to front from `from` (which goes before `next`) stop going before `next`: looking one
    // ahead, then two, four and so on, then halving what's left.
    private static int Until(Run run, int from, in RunEntry next)
    {
        var comparer = default(RunComparer);
        var (ahead, step) = (from + 1, 1);
        while (ahead < run.Count && comparer.Compare(run.Back(ahead), next) < 0)
        {
            from = ahead;
            ahead = Math.Min(run.Count, ahead + step);
            step *= 2;
        }

        var (low, high) = (from + 1, ahead);
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (comparer.Compare(run.Back(middle), next) < 0)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    // Too mixed to merge: every entry sorted as one, by key and then place in the hierarchy, each one a stretch of its own.
    private static int SortRuns(List<Run> runs, ChunkedScratch chunked, List<Stretch> stretches, int first, bool sortByY)
    {
        var all = chunked.All;
        all.Count = 0;
        var total = 0;
        foreach (var run in runs)
            total += run.Count;
        if (chunked.RunOf.Length < total)
            (chunked.RunOf, chunked.EntryOf) = (new int[total * 2], new int[total * 2]);
        for (var r = 0; r < runs.Count; r++)
        {
            var run = runs[r];
            for (var e = 0; e < run.Count; e++)
            {
                (chunked.RunOf[all.Count], chunked.EntryOf[all.Count]) = (r, e);
                all.Add(run.Entries[e]);
            }
        }

        // An entity's entries (its parts, sorting by Y) are next to each other in the order they came: sorting by place in
        // the hierarchy first keeps them so.
        SortByKeys(all.Entries.AsSpan(0, all.Count), sortByY, all.Sort, withPlace: true);
        stretches.Clear();
        var place = first;
        foreach (var i in all.Sort.Order)
        {
            var (r, e) = (chunked.RunOf[i], chunked.EntryOf[i]);
            stretches.Add(new Stretch(r, e, e + 1, place, Direct: true));
            place += runs[r].Entries[e].Count;
        }

        return place;
    }
}
