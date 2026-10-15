using System.Windows.Threading;

namespace Griff.Editor.Tests;

/// <summary>A test that can't finish fails with a message saying where, instead of hanging the test run.</summary>
public sealed class EditorHostTimeoutTests
{
    [Fact]
    public void ATaskThatNeverCompletes_FailsTheWait_SayingWhereTheTestWas()
    {
        var never = new TaskCompletionSource();
        var failure = Record.Exception(() => EditorHost.Run(_ => { }, _ => EditorHost.Pump(never.Task, TimeSpan.FromMilliseconds(200))));

        var timeout = Assert.IsType<TimeoutException>(failure);
        Assert.Contains("dialog", timeout.Message);
        Assert.Contains(nameof(ATaskThatNeverCompletes_FailsTheWait_SayingWhereTheTestWas), timeout.Message);
    }

    [Fact]
    public void AWait_ForATaskThatFinishes_ReturnsItsResult()
    {
        EditorHost.Run(_ =>
        {
            var task = Task.Run(async () => { await Task.Delay(50); return 42; });
            Assert.Equal(42, EditorHost.Pump(task));
        });
    }

    [Fact]
    public void ATestBlockedOnItsThread_FailsInsteadOfHanging()
    {
        using var release = new ManualResetEventSlim();
        try
        {
            var failure = Record.Exception(() => EditorHost.Run(_ => { }, _ => release.Wait(), TimeSpan.FromMilliseconds(300)));

            var timeout = Assert.IsType<TimeoutException>(failure);
            Assert.Contains("didn't finish", timeout.Message);
            Assert.Contains(nameof(ATestBlockedOnItsThread_FailsInsteadOfHanging), timeout.Message);
        }
        finally
        {
            release.Set(); // let the blocked thread go
        }
    }
}
