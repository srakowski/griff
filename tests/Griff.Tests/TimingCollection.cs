namespace Griff.Tests;

/// <summary>
/// Test classes that time themselves with a stopwatch: they run on their own, after the rest, so other classes' threads
/// and garbage collections don't count against their limits.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}
