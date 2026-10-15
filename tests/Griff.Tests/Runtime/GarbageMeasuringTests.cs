using System.Runtime;

namespace Griff.Tests.Runtime;

/// <summary>What the tests that check code makes no garbage count on.</summary>
public class GarbageMeasuringTests
{
    [Fact]
    public void The_tests_run_without_background_garbage_collections()
    {
        // A background collection that starts while a thread is between allocations counts what's left of its allocation
        // context as allocated by it, so the garbage tests would see a few KB whenever a test alongside set one off.
        // Griff.Tests.csproj turns them off.
        Assert.False(GCSettings.IsServerGC);
        Assert.NotEqual(GCLatencyMode.Interactive, GCSettings.LatencyMode);
    }
}
