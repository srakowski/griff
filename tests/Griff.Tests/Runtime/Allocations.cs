namespace Griff.Tests.Runtime;

/// <summary>Measuring what a frame allocates once the runtime has settled.</summary>
internal static class Allocations
{
    /// <summary>
    /// The bytes each call of <paramref name="frame"/> allocates, the least over several tries of <paramref name="calls"/>
    /// calls, stopping once a try gets down to <paramref name="enough"/>. The runtime first runs a method compiled quickly,
    /// which can allocate what the fully compiled one doesn't, and while other tests keep the machine busy it takes a
    /// while to compile it fully: between tries it gets a moment to. Something that allocates every frame for good still
    /// shows in every try.
    /// </summary>
    public static long PerCall(Action frame, int calls, long enough = 0)
    {
        var least = long.MaxValue;
        for (var attempt = 0; attempt < 40 && least > enough; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(50);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < calls; i++)
                frame();
            least = Math.Min(least, (GC.GetAllocatedBytesForCurrentThread() - before) / calls);
        }

        return least;
    }
}
