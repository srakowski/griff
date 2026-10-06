using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class TimerTests
{
    private const double Step = 0.1;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public TimerTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private void Run(int frames)
    {
        for (var i = 0; i < frames; i++)
            _game.Tick(Step);
    }

    [Fact]
    public void After_FiresOnceWhenTheTimeHasPassed()
    {
        var fired = 0;
        var timer = _game.After(0.25f, () => fired++);

        Run(2);
        Assert.Equal(0, fired);
        Assert.True(timer.IsActive);

        Run(1);
        Assert.Equal(1, fired);
        Assert.False(timer.IsActive);

        Run(10);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Every_RepeatsUntilCancelled()
    {
        var fired = 0;
        var timer = _game.Every(0.2f, () => fired++);

        Run(11); // 1.1 seconds
        Assert.Equal(5, fired);

        timer.Cancel();
        Run(10);
        Assert.Equal(5, fired);
    }

    [Fact]
    public void Every_CatchesUpWhenAFrameIsLongerThanTheInterval()
    {
        var fired = 0;
        _game.Every(0.03f, () => fired++);

        _game.Tick(Step);

        Assert.Equal(3, fired);
    }

    [Fact]
    public void Every_RejectsNonPositiveIntervals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _game.Every(0, () => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _game.Every(float.NaN, () => { }));
    }

    [Fact]
    public void Timers_FollowTimeScale()
    {
        var fired = false;
        _game.After(0.5f, () => fired = true);
        _game.Time.Scale = 0;

        Run(20);
        Assert.False(fired);

        _game.Time.Scale = 1;
        Run(5);
        Assert.True(fired);
    }

    [Fact]
    public void OwnedTimer_IsCancelledWhenTheOwnerIsDestroyed()
    {
        var owner = new Entity("Bomb");
        _scene.Add(owner);
        var fired = false;
        var timer = _game.After(0.5f, () => fired = true, owner);

        _game.Destroy(owner);
        Run(10);

        Assert.False(fired);
        Assert.False(timer.IsActive);
    }

    [Fact]
    public void BehaviorTimers_BelongToTheirEntity()
    {
        var entity = new Entity("Blinker");
        var blinker = entity.AddComponent<Blinker>();
        _scene.Add(entity);

        Run(10);
        Assert.Equal(1, blinker.Fuse);
        Assert.InRange(blinker.Blinks, 4, 5);

        // Destroys apply at the end of the frame, so count from the frame after.
        _game.Destroy(entity);
        Run(1);
        var blinks = blinker.Blinks;
        Run(10);
        Assert.Equal(blinks, blinker.Blinks);
    }

    [Fact]
    public void ThrowingCallback_DoesNotFireAgain()
    {
        var calls = 0;
        _game.After(0.1f, () =>
        {
            calls++;
            throw new InvalidOperationException("boom");
        });

        Assert.Throws<InvalidOperationException>(() => Run(2));
        Run(5);

        Assert.Equal(1, calls);
    }

    public class Blinker : Behavior
    {
        [DoNotSerialize]
        public int Blinks { get; set; }

        [DoNotSerialize]
        public int Fuse { get; set; }

        protected override void Start()
        {
            Every(0.2f, () => Blinks++);
            After(0.5f, () => Fuse++);
        }
    }
}
