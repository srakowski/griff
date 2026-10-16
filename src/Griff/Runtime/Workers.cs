using System.Runtime.ExceptionServices;

namespace Griff.Runtime;

/// <summary>
/// The threads Griff spreads a big scene's work over: building a frame of thousands of sprites, and running a crowd of
/// scripts (see <see cref="CrowdAttribute"/>). Work is split into chunks that the game's own thread and the workers take
/// in turn, so a slower core (on a CPU with fast and efficient cores) takes fewer. What comes out is the same however
/// many threads there are, one included: they only change how soon it's done.
/// </summary>
public static class Workers
{
    /// <summary>
    /// How many threads a game shares its work over unless its host says otherwise (<see cref="Game.WorkerCount"/>): one
    /// for each of the computer's logical processors, up to 16, and 1 in a browser (the web player has the page's thread
    /// only). The threads themselves are the process's, shared by every game in it; how many of them a game's work takes
    /// is the game's.
    /// </summary>
    public static int Default { get; } = OperatingSystem.IsBrowser() ? 1 : Math.Clamp(Environment.ProcessorCount, 1, 16);

    // The most threads a game can ask for.
    internal const int Most = 64;

    // The bytes the workers have allocated doing this thread's jobs: a profiler adds them to the thread's own, so garbage
    // made on the workers is counted, and only against the game that made it (another game, or another test, running
    // jobs on the same workers counts its own).
    [ThreadStatic]
    private static long t_workersAllocated;

    // The bytes the workers have allocated for the job under way, each chunk's added before the chunk counts as finished.
    private static long s_jobAllocated;

    /// <summary>
    /// The bytes this thread has allocated so far, and the workers have doing its work, all told: what a profiler counts
    /// the game's garbage by (<see cref="GC.GetAllocatedBytesForCurrentThread"/> would miss what the workers make for it).
    /// </summary>
    public static long AllocatedBytes() => GC.GetAllocatedBytesForCurrentThread() + t_workersAllocated;

    // The job under way: its number (the high half) and the next chunk to take (the low half), in one, so a thread can only
    // take a chunk of the job it read. Each job's work and chunk count sit in one of two slots, by its number.
    private static long s_state;
    private static readonly IChunked?[] s_work = new IChunked?[2];
    private static readonly int[] s_chunks = new int[2];
    private static int s_finished;
    private static Exception?[] s_faults = [];

    private static readonly SemaphoreSlim s_wake = new(0);
    private static readonly ManualResetEventSlim s_done = new(false);
    private static readonly List<Thread> s_threads = [];
    private static readonly Lock s_start = new();

    // One job at a time: another thread (a second game, tests side by side) asking meanwhile runs its job on its own.
    private static int s_running;

    /// <summary>
    /// Runs <paramref name="work"/>'s chunks 0 to <paramref name="chunks"/> - 1, each once, on up to
    /// <paramref name="threads"/> threads (this one too; a game passes its <see cref="Game.WorkerCount"/>), and returns
    /// once all are done. If chunks throw, the lowest chunk's exception is thrown
    /// (each chunk stops at its first) after the others have finished.
    /// </summary>
    internal static void Run(IChunked work, int chunks, int threads)
    {
        if (chunks <= 0)
            return;
        threads = Math.Min(Math.Min(threads, Most), chunks);
        if (threads <= 1 || Interlocked.CompareExchange(ref s_running, 1, 0) != 0)
        {
            RunHere(work, chunks);
            return;
        }

        try
        {
            EnsureThreads(threads - 1);
            if (s_faults.Length < chunks)
                s_faults = new Exception?[Math.Max(chunks, s_faults.Length * 2)];

            // The next job's number, its work in its slot, then the job published with its first chunk.
            var job = (int)(Volatile.Read(ref s_state) >> 32) + 1;
            s_work[job & 1] = work;
            s_chunks[job & 1] = chunks;
            Volatile.Write(ref s_finished, 0);
            Volatile.Write(ref s_jobAllocated, 0);
            s_done.Reset();
            Interlocked.Exchange(ref s_state, (long)job << 32);
            s_wake.Release(Math.Min(threads - 1, s_threads.Count));

            TakeChunks(worker: false);
            while (Volatile.Read(ref s_finished) < chunks)
            {
                // Woken by the last chunk's thread (or one of a job before, late): looked at again either way.
                s_done.Wait();
                s_done.Reset();
            }

            s_work[job & 1] = null;
            t_workersAllocated += Volatile.Read(ref s_jobAllocated);
            ThrowFirstFault(chunks);
        }
        finally
        {
            Volatile.Write(ref s_running, 0);
        }
    }

    // The whole job on this thread, chunk by chunk.
    private static void RunHere(IChunked work, int chunks)
    {
        Exception? first = null;
        for (var chunk = 0; chunk < chunks; chunk++)
        {
            try
            {
                work.Run(chunk);
            }
            catch (Exception ex)
            {
                first ??= ex;
            }
        }

        if (first is not null)
            ExceptionDispatchInfo.Throw(first);
    }

    private static void ThrowFirstFault(int chunks)
    {
        Exception? first = null;
        for (var chunk = 0; chunk < chunks; chunk++)
        {
            if (s_faults[chunk] is { } fault)
            {
                first ??= fault;
                s_faults[chunk] = null;
            }
        }

        if (first is not null)
            ExceptionDispatchInfo.Throw(first);
    }

    // Takes chunks of the job under way until there are none left, or it's over and another has begun. A worker adds what
    // each chunk allocates to the job's count (before the chunk counts as finished, so the job's thread sees it all).
    private static void TakeChunks(bool worker)
    {
        var state = Volatile.Read(ref s_state);
        var job = (int)(state >> 32);
        var (work, chunks) = (s_work[job & 1], s_chunks[job & 1]);
        while (true)
        {
            var chunk = (int)(state & 0xFFFFFFFF);
            if ((int)(state >> 32) != job || chunk >= chunks || work is null)
                return;
            var seen = Interlocked.CompareExchange(ref s_state, state + 1, state);
            if (seen != state)
            {
                state = seen;
                continue;
            }

            var allocated = worker ? GC.GetAllocatedBytesForCurrentThread() : 0;
            try
            {
                work.Run(chunk);
            }
            catch (Exception ex)
            {
                s_faults[chunk] = ex;
            }

            if (worker)
                Interlocked.Add(ref s_jobAllocated, GC.GetAllocatedBytesForCurrentThread() - allocated);
            if (Interlocked.Increment(ref s_finished) == chunks)
                s_done.Set();
            state = Volatile.Read(ref s_state);
        }
    }

    private static void EnsureThreads(int wanted)
    {
        if (s_threads.Count >= wanted)
            return;
        lock (s_start)
        {
            while (s_threads.Count < wanted)
            {
                var thread = new Thread(Work) { IsBackground = true, Name = $"Griff Worker {s_threads.Count + 1}" };
                s_threads.Add(thread);
                thread.Start();
            }
        }
    }

    // A worker: waits to be woken, takes chunks of the job under way with the others, and waits again.
    private static void Work()
    {
        while (true)
        {
            s_wake.Wait();
            TakeChunks(worker: true);
        }
    }
}

/// <summary>Work split into chunks that can run at once, on different threads (see <see cref="Workers.Run"/>).</summary>
internal interface IChunked
{
    /// <summary>Does chunk <paramref name="chunk"/> of the work.</summary>
    void Run(int chunk);
}
