using Griff.Audio;

namespace Griff.Tests.Audio;

/// <summary>
/// The mixer played through an output handed its sound ahead of time (the web player's Web Audio): kept a little ahead,
/// pulled along silently in time until the browser lets it play, and heard in step with what's queued.
/// </summary>
public sealed class BufferedOutputTests
{
    private static SoundClip Constant(double seconds) =>
        new(Enumerable.Repeat(0.5f, (int)(seconds * Mixer.SampleRate)).ToArray(), 1, Mixer.SampleRate);

    [Fact]
    public void Pump_KeepsTheOutputAhead_ByWhatItLacks()
    {
        var now = 0.0;
        var output = new BufferedOutput(new Mixer(), () => now) { Ahead = 0.1 };
        output.Mixer.Play(Constant(5).Open());

        var block = output.Pump(held: 0.02).ToArray();

        Assert.Equal((int)Math.Ceiling(0.08 * Mixer.SampleRate) * 2, block.Length);
        Assert.Equal(0.5f, block[0], 4);
        Assert.Equal(0.5f, block[^1], 4);
        // Holding enough already, it needs nothing.
        now = 1 / 60.0;
        Assert.True(output.Pump(held: 0.1).IsEmpty);
        Assert.True(output.Pump(held: 0.25).IsEmpty);
    }

    [Fact]
    public void NothingPlaying_NeedsNothing()
    {
        var output = new BufferedOutput(new Mixer(), () => 0);

        Assert.True(output.Pump(held: 0).IsEmpty);
        Assert.True(output.Pump(held: null).IsEmpty);
    }

    [Fact]
    public void UntilTheOutputCanPlay_TheMusicKeepsTime_ThenGoesOnFromThere()
    {
        var now = 0.0;
        var output = new BufferedOutput(new Mixer(), () => now);
        var music = output.Mixer.Play(Constant(10).Open());

        // A second of frames with the browser holding the sound back: nothing to hand it, but the music moves on.
        for (var frame = 0; frame <= 60; frame++)
        {
            Assert.True(output.Pump(held: null).IsEmpty);
            now += 1 / 60.0;
        }

        now -= 1 / 60.0;
        Assert.Equal(1.0, music.Heard, 1);

        // Sound allowed: what's mixed now starts where it had got to, heard once what's queued has played.
        var block = output.Pump(held: 0.02, latency: 0.01);
        Assert.False(block.IsEmpty);
        Assert.Equal(0.03, output.Mixer.OutputLatency, 6);
        Assert.InRange(music.Heard, 0.95, 1.01);
        now += 0.5;
        Assert.InRange(music.Heard, 1.45, 1.51);
    }

    [Fact]
    public void AStall_IsLeftOutOfTheClock_SoTheMusicDoesntJumpAhead()
    {
        var now = 0.0;
        var output = new BufferedOutput(new Mixer(), () => now) { MaxGap = 0.25 };
        var music = output.Mixer.Play(Constant(30).Open());
        output.Pump(held: 0);
        now = 0.1;
        output.Pump(held: 0.05);

        // Ten seconds hidden in a background tab: no frames, so nothing was mixed.
        now = 10.1;
        output.Pump(held: 0);
        Assert.Equal(0.35, output.Time, 6);

        Assert.InRange(music.Heard, 0.1, 0.45);
    }
}
